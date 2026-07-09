using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Hai.TransferAssistant
{
    internal class VisualizeTreeBuilder
    {
        private readonly HaiEFLoc _localize;
        private TransferAssistantAnalysis _analysis;
        private TreeViewState _treeViewState;
        private DependencyTreeView _treeView;

        public VisualizeTreeBuilder(HaiEFLoc localize)
        {
            _localize = localize;
        }

        public void WhenAnalysisUpdated(TransferAssistantAnalysis analysis)
        {
            _analysis = analysis;
            _treeView = null;
        }

        public void MarkVisible(string searchString)
        {
            if (_analysis == null) return;

            if (_treeView == null)
            {
                _treeViewState ??= new TreeViewState();
                _treeView = new DependencyTreeView(_treeViewState, _analysis, _localize);
                _treeView.Reload();
                _treeView.ExpandAll();
            }

            if (_treeView.CustomSearchString != searchString)
            {
                _treeView.CustomSearchString = searchString;
            }

            var rect = EditorGUILayout.GetControlRect(false, GUILayout.ExpandHeight(true), GUILayout.MinHeight(200));
            _treeView.OnGUI(rect);
        }
    }

    internal class DependencyTreeView : TreeView
    {
        private const string EditorOnlyLabel = "EditorOnly";
        private readonly TransferAssistantAnalysis _analysis;
        private readonly HaiEFLoc _localize;
        private string _customSearchString;

        public string CustomSearchString
        {
            get => _customSearchString;
            set
            {
                if (_customSearchString != value)
                {
                    _customSearchString = value;
                    Reload();
                    if (!string.IsNullOrEmpty(_customSearchString))
                    {
                        ExpandAll();
                    }
                }
            }
        }

        public DependencyTreeView(TreeViewState state, TransferAssistantAnalysis analysis, HaiEFLoc localize) : base(state)
        {
            _analysis = analysis;
            _localize = localize;
            showAlternatingRowBackgrounds = true;
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            var allItems = new List<TreeViewItem>();

            var visited = new HashSet<Object>();
            var idCounter = 1;

            foreach (var target in _analysis.Targets)
            {
                if (_analysis.DataDeepviews.ContainsKey(target))
                {
                    BuildNode(target, visited, 0, ref idCounter, allItems, true);
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
            if (string.IsNullOrEmpty(_customSearchString)) return base.BuildRows(root);

            var matchingItems = new HashSet<TreeViewItem>();
            
            FindMatchesAndRelatives(root, _customSearchString, matchingItems);

            var filteredRows = new List<TreeViewItem>();
            if (root.hasChildren)
            {
                AddVisibleItems(root.children, matchingItems, filteredRows);
            }

            return filteredRows;
        }

        private void FindMatchesAndRelatives(TreeViewItem item, string search, HashSet<TreeViewItem> matchingItems)
        {
            if (item.id != 0 && DoesItemMatchSearch(item, search))
            {
                AddMatchAndAncestors(item, matchingItems);
                AddDescendants(item, matchingItems);
            }

            if (item.hasChildren)
            {
                foreach (var child in item.children)
                {
                    FindMatchesAndRelatives(child, search, matchingItems);
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
                    if (item.hasChildren)
                    {
                        AddVisibleItems(item.children, matchingItems, filteredRows);
                    }
                }
            }
        }

        private bool DoesItemMatchSearch(TreeViewItem item, string search)
        {
            return item.displayName.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
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
                }
            }

            var objectFieldRect = rect;
            objectFieldRect.x += indent;
            objectFieldRect.width -= indent;

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
            
            var isMatch = !string.IsNullOrEmpty(_customSearchString) && DoesItemMatchSearch(item, _customSearchString);
            var prevFontStyle = EditorStyles.objectField.fontStyle;
            if (isMatch)
            {
                EditorStyles.objectField.fontStyle = FontStyle.Bold;
            }
            
            EditorGUI.BeginDisabledGroup(isComponentOrStateMachineBehaviour || item.Target is GameObject && !isPrefabDiskAsset);
            TransferAssistantWindow.ColoredBackgroundVoid(
                isAnyPrefabInstanceRoot || isComponentOrStateMachineBehaviour || isPrefabDiskAsset,
                isComponentOrStateMachineBehaviour ? TransferAssistantWindow.ComponentlikeColor :
                    isPrefabDiskAsset ? TransferAssistantWindow.PersistentGameObjectColor :
                    TransferAssistantWindow.PrefabInstanceRootGameObjectColor,
                () => EditorGUI.ObjectField(objectFieldRect, item.Target, typeof(Object), false));
            EditorGUI.EndDisabledGroup();
            
            if (isMatch)
            {
                EditorStyles.objectField.fontStyle = prevFontStyle;
            }
        }
    }

    internal class DependencyTreeViewItem : TreeViewItem
    {
        public Object Target { get; }
        public bool HasDependencies { get; }
        public bool AlreadyVisited { get; }

        public DependencyTreeViewItem(int id, int depth, string displayName, Object target, bool hasDependencies, bool alreadyVisited) : base(id, depth, displayName)
        {
            Target = target;
            HasDependencies = hasDependencies;
            AlreadyVisited = alreadyVisited;
        }
    }
}