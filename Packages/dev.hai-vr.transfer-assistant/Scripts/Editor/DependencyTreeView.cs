using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hai.TransferAssistant
{
    internal class VisualizeTreeBuilder
    {
        private readonly HaiEFLoc _localize;
        private readonly TransferAssistantWindow _window;
        private TransferAssistantAnalysis _analysis;
        private TreeViewState _treeViewState;
        private DependencyTreeView _treeView;
        private bool _expandDependencies = true;
        private bool _expandTypes = true;
        private bool _expandAssetsContainingOthers = true;

        public VisualizeTreeBuilder(HaiEFLoc localize, TransferAssistantWindow window)
        {
            _localize = localize;
            _window = window;
        }

        public void WhenAnalysisUpdated(TransferAssistantAnalysis analysis)
        {
            if (_treeViewState != null)
            {
                var expandedIDs = new HashSet<int>(_treeViewState.expandedIDs);
                _expandDependencies = expandedIDs.Contains(DependencyTreeView.DependenciesId);
                _expandTypes = expandedIDs.Contains(DependencyTreeView.TypesId);
                _expandAssetsContainingOthers = expandedIDs.Contains(DependencyTreeView.AssetsContainingOthersId);
            }
            _analysis = analysis;
            _treeView = null;
        }

        public void MarkVisible(string searchString, Object searchObject)
        {
            if (_analysis == null) return;

            if (_treeView == null)
            {
                _treeViewState ??= new TreeViewState();
                _treeView = new DependencyTreeView(_treeViewState, _analysis, _localize, _window);
                _treeView.Reload();
                _treeView.ExpandAll();
                if (!_expandDependencies) _treeView.SetExpanded(DependencyTreeView.DependenciesId, false);
                if (!_expandTypes) _treeView.SetExpanded(DependencyTreeView.TypesId, false);
                if (!_expandAssetsContainingOthers) _treeView.SetExpanded(DependencyTreeView.AssetsContainingOthersId, false);
            }

            if (_treeView.CustomSearchString != searchString)
            {
                _treeView.CustomSearchString = searchString;
            }

            if (_treeView.CustomSearchObject != searchObject)
            {
                _treeView.CustomSearchObject = searchObject;
            }

            var rect = EditorGUILayout.GetControlRect(false, GUILayout.ExpandHeight(true), GUILayout.MinHeight(200));
            _treeView.OnGUI(rect);
        }
    }

    internal class DependencyTreeView : TreeView
    {
        private const string EditorOnlyLabel = "EditorOnly";
        internal const int DependenciesId = 1;
        internal const int TypesId = 2;
        internal const int AssetsContainingOthersId = 3;
        private const int FirstDynamicId = 100;

        private readonly TransferAssistantAnalysis _analysis;
        private readonly HaiEFLoc _localize;
        private readonly TransferAssistantWindow _window;
        private string _customSearchString;
        private Object _customSearchObject;

        private readonly Texture _searchIcon = EditorGUIUtility.IconContent(TransferAssistantWindow.SearchIconContent).image;

        public string CustomSearchString
        {
            get => _customSearchString;
            set
            {
                if (_customSearchString != value)
                {
                    _customSearchString = value;
                    Reload();
                }
            }
        }

        public Object CustomSearchObject
        {
            get => _customSearchObject;
            set
            {
                if (_customSearchObject != value)
                {
                    _customSearchObject = value;
                    Reload();
                }
            }
        }

        public DependencyTreeView(TreeViewState state, TransferAssistantAnalysis analysis, HaiEFLoc localize, TransferAssistantWindow window) : base(state)
        {
            _analysis = analysis;
            _localize = localize;
            _window = window;
            showAlternatingRowBackgrounds = true;
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            var allItems = new List<TreeViewItem>();

            var idCounter = FirstDynamicId;

            var dependenciesItem = new DependencyTreeViewItem(DependenciesId, 0, _localize.Text(Phrases.dependencies), (Object)null, true, false);
            allItems.Add(dependenciesItem);
            
            var visited = new HashSet<Object>();
            foreach (var target in _analysis.Targets)
            {
                if (_analysis.DataDeepviews.ContainsKey(target))
                {
                    BuildNode(target, visited, 1, ref idCounter, allItems, true);
                }
            }

            var typesItem = new DependencyTreeViewItem(TypesId, 0, _localize.Text(Phrases.types), (Object)null, true, false);
            allItems.Add(typesItem);

            var assetsByType = _analysis.AfterCullingDataDeepviews.Keys
                .Where(obj => obj != null)
                .GroupBy(obj => obj.GetType())
                .Where(group => !TransferAssistantAnalysis.IsComponentOrStateMachineBehaviour(group.Key))
                .OrderBy(group => group.Key.Name, StringComparer.InvariantCulture);

            foreach (var group in assetsByType)
            {
                var typeItem = new DependencyTreeViewItem(idCounter++, 1, group.Key.Name, (Type)group.Key, true, false);
                allItems.Add(typeItem);

                var sortedAssets = group.OrderBy(obj => obj.name, StringComparer.InvariantCulture);
                foreach (var asset in sortedAssets)
                {
                    var assetItem = new DependencyTreeViewItem(idCounter++, 2, asset.name, asset, false, false);
                    allItems.Add(assetItem);
                }
            }

            var assetsContainingOthersItem = new DependencyTreeViewItem(AssetsContainingOthersId, 0, _localize.Text(Phrases.assets_containing_other_assets), (Object)null, true, false);
            allItems.Add(assetsContainingOthersItem);

            var subassetManifest = _analysis.AfterCullingSubassetManifest
                .OrderBy(pair => pair.Key.name, StringComparer.InvariantCulture);

            foreach (var pair in subassetManifest)
            {
                var parentAsset = pair.Key;
                var subAssets = pair.Value;

                var parentItem = new DependencyTreeViewItem(idCounter++, 1, parentAsset.name, parentAsset, subAssets.Count > 0, false);
                allItems.Add(parentItem);

                foreach (var subAsset in subAssets.OrderBy(obj => obj.name, StringComparer.InvariantCulture))
                {
                    var subItem = new DependencyTreeViewItem(idCounter++, 2, subAsset.name, subAsset, false, false);
                    allItems.Add(subItem);
                }
            }

            SetupParentsAndChildrenFromDepths(root, allItems);
            return root;
        }

        private void BuildNode(Object obj, HashSet<Object> visited, int depth, ref int idCounter, List<TreeViewItem> allItems, bool hasDependencies)
        {
            if (obj == null) return;

            bool alreadyVisited = visited.Contains(obj);
            var displayName = obj.name;
            if (alreadyVisited)
            {
                displayName += " " + _localize.Text(Phrases.already_shown);
            }

            var item = new DependencyTreeViewItem(idCounter++, depth, displayName, obj, hasDependencies, alreadyVisited);
            allItems.Add(item);

            if (!alreadyVisited)
            {
                visited.Add(obj);
                if (_analysis.DataDeepviews.TryGetValue(obj, out var dv))
                {
                    foreach (var dependency in dv.dependsOn)
                    {
                        if (_analysis.DataDeepviews.TryGetValue(dependency.item, out var dv2) && !dv2.isDeadEnd)
                        {
                            BuildNode(dependency.item, visited, depth + 1, ref idCounter, allItems, dv2.dependsOn.Count > 0);
                        }
                    }
                }
            }
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            if (string.IsNullOrEmpty(_customSearchString) && _customSearchObject == null) return base.BuildRows(root);

            var matchingItems = new HashSet<TreeViewItem>();
            
            FindMatchesAndRelatives(root, matchingItems);

            var filteredRows = new List<TreeViewItem>();
            if (root.hasChildren)
            {
                AddVisibleItems(root.children, matchingItems, filteredRows);
            }

            return filteredRows;
        }

        private void FindMatchesAndRelatives(TreeViewItem item, HashSet<TreeViewItem> matchingItems)
        {
            if (item.id != 0 && DoesItemMatchSearch(item))
            {
                AddMatchAndAncestors(item, matchingItems);
                AddDescendants(item, matchingItems);
            }

            if (item.hasChildren)
            {
                foreach (var child in item.children)
                {
                    FindMatchesAndRelatives(child, matchingItems);
                }
            }
        }

        private void AddVisibleItems(IList<TreeViewItem> items, HashSet<TreeViewItem> matchingItems, List<TreeViewItem> filteredRows)
        {
            foreach (var item in items)
            {
                if (matchingItems.Contains(item))
                {
                    filteredRows.Add(item);
                    if (item.hasChildren && IsExpanded(item.id))
                    {
                        AddVisibleItems(item.children, matchingItems, filteredRows);
                    }
                }
            }
        }

        private bool DoesItemMatchSearch(TreeViewItem item)
        {
            if (_customSearchObject != null)
            {
                return item is DependencyTreeViewItem dependencyItem && dependencyItem.Target == _customSearchObject;
            }

            if (string.IsNullOrEmpty(_customSearchString)) return false;
            if (_customSearchString.StartsWith("t:"))
            {
                return item is DependencyTreeViewItem dependencyItem && dependencyItem.Target != null && dependencyItem.Target.GetType().FullName == _customSearchString.Substring(2);
            }
            
            return item.displayName.IndexOf(_customSearchString, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AddMatchAndAncestors(TreeViewItem item, HashSet<TreeViewItem> matchingItems)
        {
            var current = item;
            while (current != null && current.id != 0) // id 0 is root
            {
                if (!matchingItems.Add(current)) break;
                current = current.parent;
            }
        }

        private void AddDescendants(TreeViewItem item, HashSet<TreeViewItem> matchingItems)
        {
            if (item.hasChildren)
            {
                foreach (var child in item.children)
                {
                    if (matchingItems.Add(child))
                    {
                        AddDescendants(child, matchingItems);
                    }
                }
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = (DependencyTreeViewItem)args.item;
            
            var rect = args.rowRect;
            
            var indent = GetContentIndent(args.item);
            var foldoutRect = rect;
            foldoutRect.x += indent - 16f;
            foldoutRect.width = 16f;
            if (args.item.hasChildren)
            {
                var isExpanded = IsExpanded(args.item.id);
                var newExpanded = GUI.Toggle(foldoutRect, isExpanded, GUIContent.none, EditorStyles.foldout);
                if (newExpanded != isExpanded)
                {
                    SetExpanded(args.item.id, newExpanded);
                    if (!string.IsNullOrEmpty(_customSearchString) || _customSearchObject != null)
                    {
                        Reload();
                    }
                }
            }

            if (item.Target == null)
            {
                var labelRect = rect;
                labelRect.x += indent;
                labelRect.width -= indent;

                if (item.Type != null)
                {
                    var buttonWidth = 25;
                    labelRect.width -= buttonWidth + 4;
                    var buttonRect = new Rect(labelRect.xMax + 4, labelRect.y, buttonWidth, labelRect.height);
                    if (GUI.Button(buttonRect, _searchIcon, EditorStyles.miniButton))
                    {
                        _window.ApplyOrToggleSearchString("t:" + item.Type.FullName);
                        _window.Repaint();
                    }
                }

                EditorGUI.LabelField(labelRect, item.displayName, EditorStyles.boldLabel);
                return;
            }

            var objectFieldRect = rect;
            objectFieldRect.x += indent;
            objectFieldRect.width -= indent;

            {
                var buttonWidth = 25;
                objectFieldRect.width -= buttonWidth + 4;
                var buttonRect = new Rect(objectFieldRect.xMax + 4, objectFieldRect.y, buttonWidth, objectFieldRect.height);
                if (GUI.Button(buttonRect, _searchIcon, EditorStyles.miniButton))
                {
                    _window.ApplyOrToggleSearchObject(item.Target);
                    _window.Repaint();
                }
            }

            if (item.AlreadyVisited && item.HasDependencies)
            {
                var labelContent = new GUIContent(_localize.Text(Phrases.already_shown));
                var labelWidth = EditorStyles.miniLabel.CalcSize(labelContent).x;
                objectFieldRect.width -= labelWidth + 4;
                
                var labelRect = new Rect(objectFieldRect.xMax + 4, objectFieldRect.y, labelWidth, objectFieldRect.height);
                EditorGUI.LabelField(labelRect, labelContent, EditorStyles.miniLabel);
            }

            Deepview deepview = null;
            var isDeepviewGameObject = item.Target is GameObject && _analysis.DataDeepviews.TryGetValue(item.Target, out deepview);
            var isAnyPrefabInstanceRoot = isDeepviewGameObject && deepview.isAnyPrefabInstanceRoot;
            var isPrefabDiskAsset = isDeepviewGameObject && deepview.isAssetOnDisk && deepview.isMainAsset;
            var isPrefabModel = isDeepviewGameObject && deepview.isPrefabModelDiskReference;
            var isComponentOrStateMachineBehaviour = TransferAssistantAnalysis.IsComponentOrStateMachineBehaviour(item.Target.GetType());

            if (isDeepviewGameObject && deepview.isEditorOnly)
            {
                var labelContent = new GUIContent(EditorOnlyLabel);
                var labelWidth = EditorStyles.miniLabel.CalcSize(labelContent).x;
                objectFieldRect.width -= labelWidth + 4;
                var editorOnlyLabelRect = new Rect(objectFieldRect.xMax + 4, objectFieldRect.y, labelWidth, objectFieldRect.height);
                TransferAssistantWindow.ColoredBackgroundVoid(true, TransferAssistantWindow.EditorOnlyColor, () =>
                    EditorGUI.LabelField(editorOnlyLabelRect, labelContent, EditorStyles.miniLabel));
            }
            
            if (isPrefabDiskAsset || isAnyPrefabInstanceRoot)
            {
                var prefabLabelContent = new GUIContent(_localize.Text(isPrefabModel ? Phrases.prefab_model : isPrefabDiskAsset ? Phrases.prefab_source : Phrases.prefab_instance));
                var prefabLabelWidth = EditorStyles.miniLabel.CalcSize(prefabLabelContent).x;
                objectFieldRect.width -= prefabLabelWidth + 4;
                var prefabLabelRect = new Rect(objectFieldRect.xMax + 4, objectFieldRect.y, prefabLabelWidth, objectFieldRect.height);
                TransferAssistantWindow.ColoredBackgroundVoid(true, isPrefabDiskAsset ? TransferAssistantWindow.PersistentGameObjectColor : TransferAssistantWindow.PrefabInstanceRootGameObjectColor, () =>
                    EditorGUI.LabelField(prefabLabelRect, prefabLabelContent, EditorStyles.miniLabel)
                );
            }
            
            var isMatch = DoesItemMatchSearch(item);
            var prevFontStyle = EditorStyles.objectField.fontStyle;
            try
            {
                if (isMatch)
                {
                    EditorStyles.objectField.fontStyle = FontStyle.Bold;
                }

                TransferAssistantWindow.ColoredBackgroundVoid(
                    isAnyPrefabInstanceRoot || isComponentOrStateMachineBehaviour || isPrefabDiskAsset,
                    isComponentOrStateMachineBehaviour ? TransferAssistantWindow.ComponentlikeColor :
                    isPrefabDiskAsset ? TransferAssistantWindow.PersistentGameObjectColor :
                    TransferAssistantWindow.PrefabInstanceRootGameObjectColor,
                    () =>
                    {
                        EditorGUI.BeginDisabledGroup(isComponentOrStateMachineBehaviour || item.Target is GameObject && !isPrefabDiskAsset);
                        EditorGUI.ObjectField(objectFieldRect, item.Target, typeof(Object), false);
                        EditorGUI.EndDisabledGroup();
                    });
            }
            finally
            {
                if (isMatch)
                {
                    EditorStyles.objectField.fontStyle = prevFontStyle;
                }
            }
        }
    }

    internal class DependencyTreeViewItem : TreeViewItem
    {
        public Object Target { get; }
        public Type Type { get; }
        public bool HasDependencies { get; }
        public bool AlreadyVisited { get; }

        public DependencyTreeViewItem(int id, int depth, string displayName, Object target, bool hasDependencies, bool alreadyVisited) : base(id, depth, displayName)
        {
            Target = target;
            HasDependencies = hasDependencies;
            AlreadyVisited = alreadyVisited;
        }

        public DependencyTreeViewItem(int id, int depth, string displayName, Type type, bool hasDependencies, bool alreadyVisited) : base(id, depth, displayName)
        {
            Type = type;
            HasDependencies = hasDependencies;
            AlreadyVisited = alreadyVisited;
        }
    }
}