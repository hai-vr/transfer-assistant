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

        public void MarkVisible()
        {
            if (_analysis == null) return;

            if (_treeView == null)
            {
                _treeViewState ??= new TreeViewState();
                _treeView = new DependencyTreeView(_treeViewState, _analysis, _localize);
                _treeView.Reload();
                _treeView.ExpandAll();
            }

            var rect = EditorGUILayout.GetControlRect(false, GUILayout.ExpandHeight(true), GUILayout.MinHeight(200));
            _treeView.OnGUI(rect);
        }
    }

    internal class DependencyTreeView : TreeView
    {
        private readonly TransferAssistantAnalysis _analysis;
        private readonly HaiEFLoc _localize;

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
            
            var isPersistentGameObject = item.Target is GameObject && _analysis.DataDeepviews.TryGetValue(item.Target, out var that) && that.isMainAsset;
            var isComponentOrStateMachineBehaviour = TransferAssistantAnalysis.IsComponentOrStateMachineBehaviour(item.Target.GetType());
            EditorGUI.BeginDisabledGroup(isComponentOrStateMachineBehaviour || item.Target is GameObject && !isPersistentGameObject);
            TransferAssistantWindow.ColoredBackgroundVoid(
                isPersistentGameObject || isComponentOrStateMachineBehaviour,
                isComponentOrStateMachineBehaviour ? TransferAssistantWindow.ComponentlikeColor : TransferAssistantWindow.PersistentGameObjectColor,
                () => EditorGUI.ObjectField(objectFieldRect, item.Target, typeof(Object), false));
            EditorGUI.EndDisabledGroup();
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