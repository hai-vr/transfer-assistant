using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hai.TransferAssistant
{
    public class TransferExportWindow : EditorWindow
    {
        private const string DefaultExportFileName = "export.unitypackage";
        
        internal static HaiEFLoc localize
        {
            get
            {
                _localize ??= NewLoc();
                return _localize;
            }
        }
        private static HaiEFLoc _localize;
        private static HaiEFLoc NewLoc() => new("dev.hai-vr.transfer-assistant", "Packages/dev.hai-vr.transfer-assistant/Scripts/Editor/Locale");

        private TreeViewState _treeViewState;
        private HaiAssetTreeView _treeView;
        private string[] _assetPaths;
        private List<string> _assetsByTraversal;

        public static void ShowWindow(string[] assetPaths, List<Object> assetsByTraversal)
        {
            var window = GetWindow<TransferExportWindow>(localize.Text(Phrases.window_title));
            window._assetPaths = assetPaths;
            window._assetsByTraversal = assetsByTraversal.Select(AssetDatabase.GetAssetPath).ToList();
            window.Initialize();
            window.Show();
        }

        private void Initialize()
        {
            _treeViewState = new TreeViewState();
            _treeView = new HaiAssetTreeView(_treeViewState, _assetPaths);
            _treeView.Reload();

            _treeView.UnselectPathsNotIn(_assetsByTraversal);
            
            _treeView.ExpandPartially();
        }

        private Vector2 _scrollPos;
        private Vector2 _sidebarScrollPos;

        private void OnGUI()
        {
            localize.RefreshIfNecessary();
            
            if (_treeView == null)
            {
                if (_assetPaths != null)
                {
                    Initialize();
                }
                else
                {
                    localize.LabelField(Phrases.no_assets_to_export);
                    return;
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            _sidebarScrollPos = EditorGUILayout.BeginScrollView(_sidebarScrollPos);

            var typeCounts = _treeView.GetTypeCounts();
            if (typeCounts.Length > 0)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(localize.Text(Phrases.select_all), EditorStyles.toolbarButton))
                {
                    _treeView.SetAllSelected(true);
                }
                if (GUILayout.Button(localize.Text(Phrases.deselect_all), EditorStyles.toolbarButton))
                {
                    _treeView.SetAllSelected(false);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
                
                localize.LabelField(Phrases.filtering, EditorStyles.boldLabel);
                foreach (var typeCount in typeCounts)
                {
                    var type = typeCount.Type;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"{type.Name} ({typeCount.Count})");
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(localize.Text(Phrases.select), GUILayout.Width(60)))
                    {
                        _treeView.SetTypeSelected(type, true);
                    }
                    if (GUILayout.Button(localize.Text(Phrases.deselect), GUILayout.Width(70)))
                    {
                        _treeView.SetTypeSelected(type, false);
                    }
                    if (GUILayout.Button(localize.Text(Phrases.hide)))
                    {
                        _treeView.SetTypeUnselectedAndRemoveType(type);
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                }
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(localize.Text(Phrases.expand_all), EditorStyles.toolbarButton))
            {
                _treeView.ExpandAllCustom();
            }
            if (GUILayout.Button(localize.Text(Phrases.collapse_all), EditorStyles.toolbarButton))
            {
                _treeView.CollapseAllCustom();
            }
            EditorGUILayout.EndHorizontal();

            var rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
            _treeView.OnGUI(rect);

            if (GUILayout.Button(localize.Text(Phrases.export_selected)))
            {
                var selectedPaths = _treeView.GetSelectedPaths();
                if (selectedPaths.Length > 0)
                {
                    AssetDatabase.ExportPackage(selectedPaths, DefaultExportFileName, ExportPackageOptions.Interactive);
                }
                else
                {
                    EditorUtility.DisplayDialog(localize.Text(Phrases.window_title), localize.Text(Phrases.msg_no_assets_selected), localize.Text(Phrases.dialog_ok_button));
                }
            }

            localize.Selector(() => _localize = NewLoc());
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }
    }

    public class HaiAssetTreeView : TreeView
    {
        private const string RootNodeDisplayName = "Root";

        private readonly Dictionary<int, string> _idToPath = new();
        private readonly Dictionary<string, Type> _pathToType = new();
        private readonly Dictionary<string, bool> _selectionStates = new();
        private readonly Dictionary<string, int> _pathToId = new();
        private readonly HashSet<string> _assetPathSet;
        private TypeCount[] _cachedTypeCounts;
        private int _nextId = 1;
        private string[] _assetPaths;
        private readonly Texture _searchIcon = EditorGUIUtility.IconContent(TransferAssistantWindow.SearchIconContent).image;

        public struct TypeCount
        {
            public Type Type;
            public int Count;
        }

        public HaiAssetTreeView(TreeViewState state, string[] assetPaths) : base(state)
        {
            assetPaths = assetPaths.OrderBy(p => p, StringComparer.InvariantCulture).ToArray();
            _assetPaths = assetPaths;
            _assetPathSet = new HashSet<string>(assetPaths);
            foreach (var path in _assetPaths)
            {
                _pathToType[path] = AssetDatabase.GetMainAssetTypeAtPath(path);
            }
            showAlternatingRowBackgrounds = true;
        }

        private int GetIdForPath(string path)
        {
            if (!_pathToId.TryGetValue(path, out var id))
            {
                id = _nextId++;
                _pathToId[path] = id;
            }
            return id;
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = RootNodeDisplayName };
            var allItems = new List<TreeViewItem>();

            var pathToItem = new Dictionary<string, TreeViewItem>();

            foreach (var path in _assetPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;

                var parts = path.Split('/');
                var currentPath = "";
                var parent = root;

                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0) currentPath += "/";
                    currentPath += parts[i];

                    if (!pathToItem.TryGetValue(currentPath, out var item))
                    {
                        item = new TreeViewItem { id = GetIdForPath(currentPath), displayName = parts[i] };
                        _idToPath[item.id] = currentPath;
                        if (!_selectionStates.ContainsKey(currentPath))
                        {
                            _selectionStates[currentPath] = !currentPath.StartsWith("Packages/");
                        }
                    
                        if (parent == root)
                        {
                            root.AddChild(item);
                        }
                        else
                        {
                            parent.AddChild(item);
                        }
                    
                        pathToItem[currentPath] = item;
                        allItems.Add(item);
                    }
                    parent = item;
                }
            }

            if (allItems.Count == 0)
            {
                var noItems = new TreeViewItem { id = -1, displayName = TransferExportWindow.localize.Text(Phrases.no_items_to_show) };
                root.AddChild(noItems);
            }

            SetupDepthsFromParentsAndChildren(root);
            SortChildrenRecursive(root);
            ReevaluateSelectionStates(root);

            return root;
        }

        private void ReevaluateSelectionStates(TreeViewItem item)
        {
            if (item.hasChildren)
            {
                var allChildrenSelected = true;
                foreach (var child in item.children)
                {
                    ReevaluateSelectionStates(child);
                    
                    var childPath = _idToPath.TryGetValue(child.id, out var cp) ? cp : "";
                    if (!_selectionStates.TryGetValue(childPath, out var val) || !val)
                    {
                        allChildrenSelected = false;
                    }
                }

                if (_idToPath.TryGetValue(item.id, out var path))
                {
                    _selectionStates[path] = allChildrenSelected;
                }
            }
        }

        private void SortChildrenRecursive(TreeViewItem item)
        {
            if (item.hasChildren)
            {
                item.children.Sort((a, b) =>
                {
                    if (a.hasChildren && !b.hasChildren) return -1;
                    if (!a.hasChildren && b.hasChildren) return 1;
                    return string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
                });

                foreach (var child in item.children)
                {
                    SortChildrenRecursive(child);
                }
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item;
            var rect = args.rowRect;

            if (item.id == -1)
            {
                base.RowGUI(args);
                return;
            }

            var indent = GetContentIndent(item);
            rect.x += indent;
            rect.width -= indent;

            var toggleRect = rect;
            toggleRect.width = 16f;

            var path = _idToPath.TryGetValue(item.id, out var p) ? p : "";
            var isSelected = _selectionStates.TryGetValue(path, out var val) && val;
            EditorGUI.BeginChangeCheck();
            var nextSelected = EditorGUI.Toggle(toggleRect, isSelected);
            if (EditorGUI.EndChangeCheck())
            {
                SetSelectionRecursive(item, nextSelected);
                UpdateParentSelection(item.parent);
            }

            rect.x += 20f;
            rect.width -= 20f;

            if (_assetPathSet.Contains(path))
            {
                var buttonWidth = 25;
                rect.width -= buttonWidth + 4;
                var buttonRect = new Rect(rect.xMax + 4, rect.y, buttonWidth, rect.height);
                if (GUI.Button(buttonRect, _searchIcon, EditorStyles.miniButton))
                {
                    var window = EditorWindow.GetWindow<TransferAssistantWindow>();
                    if (window != null)
                    {
                        window.searchObject = AssetDatabase.LoadMainAssetAtPath(path);
                    }
                }
            }
        
            var iconRect = rect;
            iconRect.width = 16f;
            var icon = AssetDatabase.GetCachedIcon(_idToPath.ContainsKey(item.id) ? _idToPath[item.id] : "");
            if (icon != null)
            {
                GUI.DrawTexture(iconRect, icon);
            }
        
            rect.x += 20f;
            rect.width -= 20f;

            EditorGUI.LabelField(rect, item.displayName);
        }

        private void SetSelectionRecursive(TreeViewItem item, bool selected)
        {
            if (_idToPath.TryGetValue(item.id, out var path))
            {
                _selectionStates[path] = selected;
            }
            if (item.hasChildren)
            {
                foreach (var child in item.children)
                {
                    SetSelectionRecursive(child, selected);
                }
            }
        }

        private void UpdateParentSelection(TreeViewItem parent)
        {
            if (parent == null || parent.id == 0) return;

            ReevaluateSelectionStates(parent);
            UpdateParentSelection(parent.parent);
        }

        public void SetAllSelected(bool selected)
        {
            SetSelectionRecursive(rootItem, selected);
        }

        public void ExpandAllCustom()
        {
            ExpandAll();
        }

        public void ExpandPartially()
        {
            ExpandAll();

            var idsToCollapse = new List<int>();
            if (rootItem.hasChildren)
            {
                foreach (var topLevel in rootItem.children)
                {
                    if ((topLevel.displayName == "Assets" || topLevel.displayName == "Packages") && topLevel.hasChildren)
                    {
                        foreach (var firstLevelFolder in topLevel.children)
                        {
                            if (firstLevelFolder.hasChildren)
                            {
                                idsToCollapse.Add(firstLevelFolder.id);
                            }
                        }
                    }
                }
            }

            foreach (var id in idsToCollapse)
            {
                SetExpanded(id, false);
            }
        }

        public void CollapseAllCustom()
        {
            CollapseAll();
            var idsToExpand = new List<int>();
            if (rootItem.hasChildren)
            {
                foreach (var child in rootItem.children)
                {
                    if (child.displayName == "Assets" || child.displayName == "Packages")
                    {
                        idsToExpand.Add(child.id);
                    }
                }
            }

            if (idsToExpand.Count > 0)
            {
                SetExpanded(idsToExpand);
            }
        }

        public void SetTypeSelected(Type type, bool selected)
        {
            SetTypeSelectedRecursive(rootItem, type, selected);
        }

        public void SetTypeUnselectedAndRemoveType(Type type)
        {
            SetTypeSelected(type, false);

            var pathsToRemove = _pathToType.Where(kvp => kvp.Value == type).Select(kvp => kvp.Key).ToHashSet();
            
            var allRemainingPaths = _assetPaths.Where(p => !pathsToRemove.Contains(p)).ToList();
            bool changed;
            do
            {
                changed = false;
                var currentPaths = allRemainingPaths.ToHashSet();
                for (var i = allRemainingPaths.Count - 1; i >= 0; i--)
                {
                    var path = allRemainingPaths[i];
                    if (_pathToType.TryGetValue(path, out var pathType) && pathType == typeof(DefaultAsset))
                    {
                        var hasChildren = false;
                        var prefix = path + "/";
                        foreach (var otherPath in currentPaths)
                        {
                            if (otherPath.StartsWith(prefix))
                            {
                                hasChildren = true;
                                break;
                            }
                        }

                        if (!hasChildren)
                        {
                            pathsToRemove.Add(path);
                            allRemainingPaths.RemoveAt(i);
                            changed = true;
                        }
                    }
                }
            } while (changed);

            _assetPaths = allRemainingPaths.ToArray();

            _assetPathSet.RemoveWhere(p => pathsToRemove.Contains(p));
            foreach (var path in pathsToRemove)
            {
                _pathToType.Remove(path);
                _selectionStates.Remove(path);
            }

            _idToPath.Clear();
            _cachedTypeCounts = null;
            Reload();
        }

        public void UnselectPathsNotIn(List<string> paths)
        {
            var pathsToKeep = new HashSet<string>(paths);
            foreach (var path in _assetPaths)
            {
                if (!pathsToKeep.Contains(path))
                {
                    _selectionStates[path] = false;
                }
            }
            ReevaluateSelectionStates(rootItem);
        }

        private void SetTypeSelectedRecursive(TreeViewItem item, Type type, bool selected)
        {
            if (_idToPath.TryGetValue(item.id, out var path) && _pathToType.TryGetValue(path, out var assetType) && assetType == type)
            {
                SetSelectionRecursive(item, selected);
                UpdateParentSelection(item.parent);
            }

            if (item.hasChildren)
            {
                foreach (var child in item.children)
                {
                    SetTypeSelectedRecursive(child, type, selected);
                }
            }
        }

        public TypeCount[] GetTypeCounts()
        {
            if (_cachedTypeCounts != null) return _cachedTypeCounts;

            _cachedTypeCounts = _pathToType.Values
                .Where(t => t != null)
                .GroupBy(t => t)
                .Select(g => new TypeCount { Type = g.Key, Count = g.Count() })
                .OrderBy(tc => tc.Type.Name, StringComparer.InvariantCulture)
                .ToArray();

            return _cachedTypeCounts;
        }

        public Type[] GetUniqueTypes()
        {
            return GetTypeCounts().Select(tc => tc.Type).ToArray();
        }

        public string[] GetSelectedPaths()
        {
            var result = new List<string>();
            foreach (var kvp in _selectionStates)
            {
                if (kvp.Value)
                {
                    if (_assetPathSet.Contains(kvp.Key))
                    {
                        result.Add(kvp.Key);
                    }
                }
            }
            return result.ToArray();
        }
    }
}
