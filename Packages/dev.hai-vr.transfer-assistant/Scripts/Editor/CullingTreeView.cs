using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Hai.TransferAssistant
{
    internal class CullingTreeView : TreeView
    {
        private readonly TransferAssistantAnalysis _analysis;
        private readonly HaiEFLoc _localize;
        private readonly HashSet<string> _afterCullingTypeFullNames;
        private readonly Action _onChanged;
        private readonly Action<Type> _onSearchType;
        private List<Type> _cachedNonComponents;
        private List<TransferAssistantWindow.ComponentGroup> _cachedComponentGroups;
        private Texture _searchIcon;

        public CullingTreeView(TreeViewState state, TransferAssistantAnalysis analysis, HaiEFLoc localize, HashSet<string> afterCullingTypeFullNames, Action onChanged, Action<Type> onSearchType) : base(state)
        {
            _analysis = analysis;
            _localize = localize;
            _afterCullingTypeFullNames = afterCullingTypeFullNames;
            _onChanged = onChanged;
            _onSearchType = onSearchType;
            showAlternatingRowBackgrounds = true;
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            SetSelection(Array.Empty<int>(), TreeViewSelectionOptions.None);
        }

        public void SetData(List<Type> nonComponents, List<TransferAssistantWindow.ComponentGroup> componentGroups)
        {
            _cachedNonComponents = nonComponents;
            _cachedComponentGroups = componentGroups;
            Reload();
            ExpandAll();
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            var allItems = new List<TreeViewItem>();

            if (_cachedNonComponents != null && _cachedNonComponents.Count > 0)
            {
                var cullingHeader = new TreeViewItem { id = 1, depth = 0, displayName = _localize.Text(Phrases.culling) };
                allItems.Add(cullingHeader);
                foreach (var ttype in _cachedNonComponents)
                {
                    allItems.Add(new TypeTreeViewItem(ttype) { id = ttype.FullName.GetHashCode(), depth = 1 });
                }
            }

            if (_cachedComponentGroups != null && _cachedComponentGroups.Count > 0)
            {
                var componentsHeader = new TreeViewItem { id = 2, depth = 0, displayName = _localize.Text(Phrases.components) };
                allItems.Add(componentsHeader);
                foreach (var group in _cachedComponentGroups)
                {
                    if (string.IsNullOrEmpty(group.Key))
                    {
                        foreach (var ttype in group.Types)
                        {
                            allItems.Add(new TypeTreeViewItem(ttype) { id = ttype.FullName.GetHashCode(), depth = 1 });
                        }
                    }
                    else
                    {
                        var groupItem = new GroupTreeViewItem(group) { id = group.Key.GetHashCode(), depth = 1 };
                        allItems.Add(groupItem);
                        foreach (var ttype in group.Types)
                        {
                            allItems.Add(new TypeTreeViewItem(ttype) { id = ttype.FullName.GetHashCode(), depth = 2 });
                        }
                    }
                }
            }

            SetupParentsAndChildrenFromDepths(root, allItems);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item;
            if (item is TypeTreeViewItem typeItem)
            {
                var ttype = typeItem.Type;
                var rect = args.rowRect;
                rect.xMin += GetContentIndent(item);

                var count = _analysis.DataTypeCounts.TryGetValue(ttype, out var total) ? total : 0;
                var culledCount = _analysis.AfterCullingTypeCounts != null && _analysis.AfterCullingTypeCounts.TryGetValue(ttype, out var c) ? c : 0;
                var isCulled = _afterCullingTypeFullNames.Contains(ttype.FullName);
                var friendlyTypeName = ttype.Name == TransferAssistantAnalysis.UnknownAssetAndDLLTypeName ? _localize.Text(Phrases.unknown_assets_and_dll_files) : ttype.Name;

                var isComponentOrStateMachineBehaviour = TransferAssistantAnalysis.IsComponentOrStateMachineBehaviour(ttype);
                var label = isComponentOrStateMachineBehaviour ? friendlyTypeName : $"{friendlyTypeName} ({culledCount} / {count})";
                if (!isComponentOrStateMachineBehaviour && culledCount < count && culledCount != 0)
                {
                    label += _localize.Format(Phrases.culled_suffix, count - culledCount);
                }

                var toggleRect = rect;
                toggleRect.width -= 25;
                EditorGUI.BeginChangeCheck();
                var originalColor = GUI.contentColor;
                if (isCulled)
                {
                    GUI.contentColor = originalColor * new Color(1f, 1f, 1f, 0.5f);
                }
                var newState = EditorGUI.ToggleLeft(toggleRect, label, !isCulled);
                GUI.contentColor = originalColor;
                if (EditorGUI.EndChangeCheck())
                {
                    if (newState) _afterCullingTypeFullNames.Remove(ttype.FullName);
                    else _afterCullingTypeFullNames.Add(ttype.FullName);
                    _onChanged?.Invoke();
                }

                var buttonRect = rect;
                buttonRect.xMin = rect.xMax - 22;
                buttonRect.width = 22;
                buttonRect.height = EditorGUIUtility.singleLineHeight;
                _searchIcon ??= EditorGUIUtility.IconContent(TransferAssistantWindow.SearchIconContent).image;
                if (GUI.Button(buttonRect, _searchIcon))
                {
                    _onSearchType?.Invoke(ttype);
                }
            }
            else if (item is GroupTreeViewItem groupItem)
            {
                var group = groupItem.Group;
                var rect = args.rowRect;
                rect.xMin += GetContentIndent(item);

                var groupList = group.Types;
                var allIncluded = groupList.All(ttype => !_afterCullingTypeFullNames.Contains(ttype.FullName));
                var noneIncluded = groupList.All(ttype => _afterCullingTypeFullNames.Contains(ttype.FullName));

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = !allIncluded && !noneIncluded;
                var originalColor = GUI.contentColor;
                if (noneIncluded)
                {
                    GUI.contentColor = originalColor * new Color(1f, 1f, 1f, 0.5f);
                }
                var newState = EditorGUI.ToggleLeft(rect, group.Key, allIncluded, EditorStyles.boldLabel);
                GUI.contentColor = originalColor;
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var ttype in groupList)
                    {
                        if (newState) _afterCullingTypeFullNames.Remove(ttype.FullName);
                        else _afterCullingTypeFullNames.Add(ttype.FullName);
                    }
                    _onChanged?.Invoke();
                }
            }
            else if (item.id == 1 || item.id == 2)
            {
                var rect = args.rowRect;
                rect.xMin += GetContentIndent(item);
                GUI.Label(rect, item.displayName, EditorStyles.boldLabel);
            }
            else
            {
                base.RowGUI(args);
            }
        }

        private class TypeTreeViewItem : TreeViewItem
        {
            public Type Type { get; }
            public TypeTreeViewItem(Type type) : base()
            {
                Type = type;
                displayName = type.Name;
            }
        }

        private class GroupTreeViewItem : TreeViewItem
        {
            public TransferAssistantWindow.ComponentGroup Group { get; }
            public GroupTreeViewItem(TransferAssistantWindow.ComponentGroup group) : base()
            {
                Group = group;
                displayName = group.Key;
            }
        }
    }
}
