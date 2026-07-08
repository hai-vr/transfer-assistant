using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hai.TransferAssistant
{
    public class TransferAssistantWindow : EditorWindow
    {
        private const string PrefsPrefix = "Hai.TransferAssistant.";
        private const string AfterCullingTypeFullNamesPrefsKey = PrefsPrefix + "AfterCullingTypeFullNames";
        private const string HideItemsFilterPrefsKey = PrefsPrefix + "HideItemsFilter";
        private const string IncludeEditorOnlyPrefsKey = PrefsPrefix + "IncludeEditorOnly";
        private const string MultiTargets = PrefsPrefix + "MultiTargets";
        private const string ShortTitleName = "Transfer Assistant";
        private const float SidebarWidth = 250;

        private static readonly HashSet<string> AfterCullingTypeFullNamesDefault = new() 
        {
            typeof(MonoScript).FullName,
            typeof(Shader).FullName,
            typeof(ComputeShader).FullName,
            typeof(DefaultAsset).FullName,
        };

        public Object target;
        private bool _multiTargets;
        public Object[] targets;

        private HashSet<string> _afterCullingTypeFullNames = AfterCullingTypeFullNamesDefault.ToHashSet();
        private HideItemsFilter _hideItemsFilter = HideItemsFilter.ShowEverything;
        private bool _includeEditorOnly = true;

        private string _search = "";
        private string _lastSearch;
        private int _tabIndex;

        private bool _analysisScheduled;
        private TransferAssistantAnalysis _analysis;
        
        private Vector2 _scrollPos;
        private Vector2 _sidebarScrollPos;
        private readonly HashSet<Type> _foldoutTypeGroups = new();
        private readonly HashSet<Type> _foldoutDeepTraversalTypeGroups = new();

        private List<Type> _cachedComponents = new();
        private List<Type> _cachedNonComponents = new();
        private List<ComponentGroup> _cachedComponentGroups = new();

        private struct ComponentGroup
        {
            public string Key;
            public List<Type> Types;
        }

        private static HaiEFLoc localize
        {
            get
            {
                _localize ??= NewLoc();
                return _localize;
            }
        }
        private static HaiEFLoc _localize;
        private static HaiEFLoc NewLoc() => new("dev.hai-vr.transfer-assistant", "Packages/dev.hai-vr.transfer-assistant/Scripts/Editor/Locale");

        private void OnEnable()
        {
            _analysis = new TransferAssistantAnalysis();
            if (EditorPrefs.HasKey(HideItemsFilterPrefsKey))
            {
                _hideItemsFilter = (HideItemsFilter)EditorPrefs.GetInt(HideItemsFilterPrefsKey);
            }

            if (EditorPrefs.HasKey(AfterCullingTypeFullNamesPrefsKey))
            {
                var storedNames = EditorPrefs.GetString(AfterCullingTypeFullNamesPrefsKey);
                _afterCullingTypeFullNames = storedNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            }

            if (EditorPrefs.HasKey(IncludeEditorOnlyPrefsKey))
            {
                _includeEditorOnly = EditorPrefs.GetBool(IncludeEditorOnlyPrefsKey, true);
            }

            if (EditorPrefs.HasKey(IncludeEditorOnlyPrefsKey))
            {
                _multiTargets = EditorPrefs.GetBool(MultiTargets, false);
            }
        }

        private void SavePrefs()
        {
            EditorPrefs.SetInt(HideItemsFilterPrefsKey, (int)_hideItemsFilter);
            EditorPrefs.SetString(AfterCullingTypeFullNamesPrefsKey, string.Join(",", _afterCullingTypeFullNames));
            EditorPrefs.SetBool(IncludeEditorOnlyPrefsKey, _includeEditorOnly);
            EditorPrefs.SetBool(MultiTargets, _multiTargets);
        }

        [MenuItem("Assets/Transfer Assistant...")]
        public static void AnalyzeSelection()
        {
            var window = GetWindow<TransferAssistantWindow>(ShortTitleName);
            if (Selection.objects.Length > 0)
            {
                window.target = null;
                window.targets = Selection.objects.ToArray();
                window._multiTargets = true;
            }
            else
            {
                window.target = Selection.activeObject;
                window.targets = Array.Empty<Object>();
                window._multiTargets = false;
            }
            window.ScheduleAnalysis();
        }

        [MenuItem("Window/Haï/Transfer Assistant")]
        public static void ShowWindow()
        {
            GetWindow<TransferAssistantWindow>(ShortTitleName);
        }

        private void OnGUI()
        {
            localize.RefreshIfNecessary();

            EditorGUILayout.BeginHorizontal();
            if (_multiTargets)
            {
                var serializedObject = new SerializedObject(this);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(targets)), new GUIContent(localize.Text(Phrases.targets)));
                serializedObject.ApplyModifiedProperties();
            }
            else
            {
                target = EditorGUILayout.ObjectField(localize.Text(Phrases.target), target, typeof(Object), true);
            }
            EditorGUI.BeginChangeCheck();
            _multiTargets = EditorGUILayout.ToggleLeft(localize.Text(Phrases.multiple_targets), _multiTargets, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                SavePrefs();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(target == null || _analysisScheduled);
            if (GUILayout.Button(_analysisScheduled ? localize.Text(Phrases.analysis_in_progress) : localize.Text(Phrases.perform_analysis)))
            {
                ScheduleAnalysis();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.BeginHorizontal();
            LayoutSidebar();
            LayoutMainPane();
            EditorGUILayout.EndHorizontal();
        }

        private void LayoutSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth));
            _sidebarScrollPos = EditorGUILayout.BeginScrollView(_sidebarScrollPos);
            
            if (_analysis.DataPrefabObjectToInstances != null)
            {
                localize.LabelField(Phrases.export, EditorStyles.boldLabel);
                var total = _analysis.TotalAfterCulling;
                EditorGUI.BeginDisabledGroup(total == 0);
                if (GUILayout.Button(localize.Format(Phrases.prepare_export_assets, total)))
                {
                    var allRelevantAssets = _analysis.AfterCullingDataDeepviews.Values
                        .Select(deepview => deepview.path)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .Distinct()
                        .ToArray();

                    TransferExportWindow.ShowWindow(allRelevantAssets, _analysis.AfterCullingDataDeepviews.Keys.ToList());
                }

                // if (target != null && !_analysis.IsTargetAnAsset)
                // {
                    // localize.HelpBox(Phrases.msg_not_prefab_asset, MessageType.Warning);
                // }
                EditorGUI.EndDisabledGroup();
                
                EditorGUILayout.Space();
                
                if (_analysis.DataSortedTypes != null)
                {
                    localize.HelpBox(Phrases.msg_checkboxes_affect_export, MessageType.None);
                    
                    if (_cachedNonComponents.Count > 0)
                    {
                        localize.LabelField(Phrases.culling, EditorStyles.boldLabel);
                        foreach (var ttype in _cachedNonComponents)
                        {
                            LayoutTypeToggle(ttype);
                        }
                        LayoutResetToDefaults();
                        EditorGUILayout.Space();
                    }
                    
                    localize.LabelField(Phrases.options, EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    _includeEditorOnly = EditorGUILayout.ToggleLeft(localize.Text(Phrases.include_editor_only), _includeEditorOnly);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _analysis.UpdateIncludeEditorOnly(_includeEditorOnly);
                        SavePrefs();
                    }
                    EditorGUILayout.Space();

                    if (_cachedComponents.Count > 0)
                    {
                        localize.LabelField(Phrases.components, EditorStyles.boldLabel);

                        foreach (var group in _cachedComponentGroups)
                        {
                            if (group.Key == "")
                            {
                                foreach (var ttype in group.Types)
                                {
                                    LayoutTypeToggle(ttype);
                                }
                            }
                            else
                            {
                                var groupList = group.Types;
                                var allIncluded = groupList.All(ttype => !_afterCullingTypeFullNames.Contains(ttype.FullName));
                                var noneIncluded = groupList.All(ttype => _afterCullingTypeFullNames.Contains(ttype.FullName));

                                EditorGUI.BeginChangeCheck();
                                EditorGUI.showMixedValue = !allIncluded && !noneIncluded;
                                var newState = EditorGUILayout.ToggleLeft(group.Key, allIncluded, EditorStyles.boldLabel);
                                EditorGUI.showMixedValue = false;
                                if (EditorGUI.EndChangeCheck())
                                {
                                    foreach (var ttype in groupList)
                                    {
                                        if (newState)
                                        {
                                            _afterCullingTypeFullNames.Remove(ttype.FullName);
                                        }
                                        else
                                        {
                                            _afterCullingTypeFullNames.Add(ttype.FullName);
                                        }
                                    }
                                    _analysis.UpdateCulledCache(_afterCullingTypeFullNames);
                                    SavePrefs();
                                }

                                EditorGUI.indentLevel++;
                                foreach (var ttype in groupList)
                                {
                                    LayoutTypeToggle(ttype);
                                }
                                EditorGUI.indentLevel--;
                            }
                        }
                        EditorGUILayout.Space();
                    }

                    if (_cachedNonComponents.Count == 0)
                    {
                        LayoutResetToDefaults();
                    }
                }
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private static bool IsComponentOrStateMachineBehaviour(Type t)
        {
            return typeof(Component).IsAssignableFrom(t) || typeof(StateMachineBehaviour).IsAssignableFrom(t);
        }

        private (List<T>, List<T>) PartitionBy<T>(IEnumerable<T> source, Func<T, bool> predicate)
        {
            var trueItems = new List<T>();
            var falseItems = new List<T>();
            foreach (var item in source)
            {
                if (predicate(item))
                {
                    trueItems.Add(item);
                }
                else
                {
                    falseItems.Add(item);
                }
            }
            return (trueItems, falseItems);
        }

        private void LayoutResetToDefaults()
        {
            if (GUILayout.Button(localize.Text(Phrases.reset_to_defaults)))
            {
                _afterCullingTypeFullNames.Clear();
                _afterCullingTypeFullNames.UnionWith(AfterCullingTypeFullNamesDefault);
                _analysis.UpdateCulledCache(_afterCullingTypeFullNames);
                _includeEditorOnly = true;
                SavePrefs();
            }
        }

        private void LayoutMainPane()
        {
            EditorGUILayout.BeginVertical();
            localize.LabelField(Phrases.exploration, EditorStyles.boldLabel);
            localize.HelpBox(Phrases.msg_will_not_affect_export, MessageType.None);
            EditorGUILayout.BeginHorizontal();
            _search = EditorGUILayout.TextField(localize.Text(Phrases.search), _search);
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_search));
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                _search = "";
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_search != _lastSearch)
            {
                _lastSearch = _search;
            }

            if (_analysis.DataPrefabObjectToInstances != null)
            {
                _tabIndex = GUILayout.Toolbar(_tabIndex, new[] { localize.Text(Phrases.dependencies), localize.Text(Phrases.prefabs), localize.Text(Phrases.types) });

                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

                switch (_tabIndex)
                {
                    case 0: LayoutVisualizeDeepTraversal(); break;
                    case 1: LayoutVisualizePrefabs(); break;
                    case 2: LayoutVisualizeTypes(); break;
                }

                EditorGUILayout.EndScrollView();
            }

            localize.Selector(() => _localize = NewLoc());
            EditorGUILayout.EndVertical();
        }

        private void DisplayHideItemsFilter()
        {
            var newHideItemsFilter = (HideItemsFilter)EditorGUILayout.Popup(
                new GUIContent(localize.Text(Phrases.hide_items)),
                (int)_hideItemsFilter,
                new[]
                {
                    localize.Text(Phrases.hide_items_show_everything),
                    localize.Text(Phrases.hide_items_hide_leaf_with_one_parent),
                    localize.Text(Phrases.hide_items_hide_all_leaf)
                }
            );
            if (newHideItemsFilter != _hideItemsFilter)
            {
                _hideItemsFilter = newHideItemsFilter;
                SavePrefs();
            }
        }

        private void LayoutTypeToggle(Type ttype)
        {
            var count = _analysis.DataTypeCounts[ttype];
            var culledCount = _analysis.AfterCullingTypeCounts != null && _analysis.AfterCullingTypeCounts.TryGetValue(ttype, out var c) ? c : 0;
            var isCulled = _afterCullingTypeFullNames.Contains(ttype.FullName);
            var friendlyTypeName = ttype.Name == TransferAssistantAnalysis.UnknownAssetAndDLLTypeName ? localize.Text(Phrases.unknown_assets_and_dll_files) : ttype.Name;

            var isComponentOrStateMachineBehaviour = IsComponentOrStateMachineBehaviour(ttype);
            var label = isComponentOrStateMachineBehaviour ? friendlyTypeName : $"{friendlyTypeName} ({culledCount} / {count})";
            if (!isComponentOrStateMachineBehaviour && culledCount < count && culledCount != 0)
            {
                label += localize.Format(Phrases.culled_suffix, count - culledCount);
            }

            EditorGUI.BeginChangeCheck();
            var newState = EditorGUILayout.ToggleLeft(label, !isCulled);
            if (EditorGUI.EndChangeCheck())
            {
                if (newState)
                {
                    if (_afterCullingTypeFullNames.Remove(ttype.FullName))
                    {
                        _analysis.UpdateCulledCache(_afterCullingTypeFullNames);
                        SavePrefs();
                    }
                }
                else
                {
                    if (_afterCullingTypeFullNames.Add(ttype.FullName))
                    {
                        _analysis.UpdateCulledCache(_afterCullingTypeFullNames);
                        SavePrefs();
                    }
                }
            }
        }

        private void LayoutVisualizePrefabs()
        {
            if (_analysis.DataPrefabObjectToInstances != null)
            {
                var filterLower = _search.ToLower();
                var isFilterEmpty = string.IsNullOrEmpty(_search);

                foreach (var prefabObjectToInstance in _analysis.DataPrefabObjectToInstances)
                {
                    var source = prefabObjectToInstance.Key;
                    if (_analysis.AfterCullingDataDeepviews != null && !_analysis.AfterCullingDataDeepviews.ContainsKey(source)) continue;

                    var instances = prefabObjectToInstance.Value;

                    var sourceMatches = isFilterEmpty || (source != null && source.name.ToLower().Contains(filterLower));
                    var matchingInstances = instances
                        .Where(instance => instance != null && (isFilterEmpty || instance.name.ToLower().Contains(filterLower)))
                        .ToList();

                    if (!sourceMatches && matchingInstances.Count == 0) continue;

                    var instancesToShow = sourceMatches ? instances.ToList() : matchingInstances;

                    EditorGUILayout.BeginVertical("GroupBox");
                    EditorGUILayout.ObjectField(localize.Text(Phrases.prefab_source), source, typeof(GameObject), false);
                    EditorGUILayout.TextField(localize.Text(Phrases.path), AssetDatabase.GetAssetPath(source), EditorStyles.label);

                    foreach (var instance in instancesToShow)
                    {
                        EditorGUILayout.ObjectField(localize.Text(Phrases.instantiated_in), instance, typeof(GameObject), true);
                    }

                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void LayoutVisualizeTypes()
        {
            if (_analysis.AfterCullingDataDeepviews != null)
            {
                var filterLower = _search.ToLower();
                var isFilterEmpty = string.IsNullOrEmpty(_search);

                var typeToAssets = new Dictionary<Type, List<Object>>();
                foreach (var asset in _analysis.AfterCullingDataDeepviews.Keys)
                {
                    if (asset == null) continue;
                    if (!isFilterEmpty && !asset.name.ToLower().Contains(filterLower)) continue;

                    var typeName = asset.GetType();
                    if (!typeToAssets.ContainsKey(typeName))
                    {
                        typeToAssets[typeName] = new List<Object>();
                    }
                    typeToAssets[typeName].Add(asset);
                }

                var typeGroups = typeToAssets
                    .Select(kvp => new { TType = kvp.Key, Assets = kvp.Value.OrderBy(o => o.name, StringComparer.InvariantCulture).ToList() })
                    .OrderBy(group => group.TType.Name, StringComparer.InvariantCulture)
                    .ToList();

                foreach (var group in typeGroups)
                {
                    var friendlyTypeName = group.TType.Name == TransferAssistantAnalysis.UnknownAssetAndDLLTypeName ? localize.Text(Phrases.unknown_assets_and_dll_files) : group.TType.Name;
                    var isFoldedOut = _foldoutTypeGroups.Contains(group.TType);
                    
                    EditorGUILayout.BeginVertical("GroupBox");
                    var nowFoldedOut = EditorGUILayout.Foldout(isFoldedOut, friendlyTypeName, true, EditorStyles.foldoutHeader);
                    if (nowFoldedOut != isFoldedOut)
                    {
                        if (nowFoldedOut) _foldoutTypeGroups.Add(group.TType);
                        else _foldoutTypeGroups.Remove(group.TType);
                    }

                    if (nowFoldedOut)
                    {
                        foreach (var asset in group.Assets)
                        {
                            EditorGUILayout.ObjectField(asset, typeof(Object), false);
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void LayoutVisualizeDeepTraversal()
        {
            DisplayHideItemsFilter();
            if (_analysis.AfterCullingDataDeepviews != null)
            {
                var filterLower = _search.ToLower();
                var isFilterEmpty = string.IsNullOrEmpty(_search);

                var typeToAssets = new Dictionary<Type, List<Object>>();
                foreach (var (asset, deepview) in _analysis.AfterCullingDataDeepviews)
                {
                    if (asset == null) continue;
                    if (deepview.isDeadEnd) continue;
                    if (!isFilterEmpty && !asset.name.ToLower().Contains(filterLower)) continue;

                    if (_hideItemsFilter != HideItemsFilter.ShowEverything)
                    {
                        var hasNoDependencies = deepview.dependsOn.Count == 0;
                        if (hasNoDependencies)
                        {
                            if (_hideItemsFilter == HideItemsFilter.HideAllLeaf) continue;

                            var isDependedByExactlyOne = deepview.isDependedBy.Count == 1;
                            if (_hideItemsFilter == HideItemsFilter.HideLeafWithOneParent && isDependedByExactlyOne) continue;
                        }
                    }

                    var ttype = asset.GetType();
                    if (!typeToAssets.ContainsKey(ttype))
                    {
                        typeToAssets[ttype] = new List<Object>();
                    }
                    typeToAssets[ttype].Add(asset);
                }

                var typeGroups = typeToAssets
                    .Select(typeToAsset => new { TType = typeToAsset.Key, Assets = typeToAsset.Value.OrderBy(o => o.name, StringComparer.InvariantCulture).ToList() })
                    .OrderBy(group => IsComponentOrStateMachineBehaviour(group.TType))
                    .ThenBy(group => group.TType.Name, StringComparer.InvariantCulture)
                    .ToList();

                foreach (var group in typeGroups)
                {
                    var friendlyTypeName = group.TType.Name == TransferAssistantAnalysis.UnknownAssetAndDLLTypeName ? localize.Text(Phrases.unknown_assets_and_dll_files) : group.TType.Name;
                    var isFoldedOut = _foldoutDeepTraversalTypeGroups.Contains(group.TType);

                    EditorGUILayout.BeginVertical("GroupBox");
                    var nowFoldedOut = ColoredBackground(IsComponentOrStateMachineBehaviour(group.TType), Color.yellow, () => EditorGUILayout.Foldout(isFoldedOut, friendlyTypeName, true, EditorStyles.foldoutHeader));
                    if (nowFoldedOut != isFoldedOut)
                    {
                        if (nowFoldedOut) _foldoutDeepTraversalTypeGroups.Add(group.TType);
                        else _foldoutDeepTraversalTypeGroups.Remove(group.TType);
                    }

                    if (nowFoldedOut)
                    {
                        foreach (var asset in group.Assets)
                        {
                            var deepview = _analysis.AfterCullingDataDeepviews[asset];
                                
                            EditorGUILayout.BeginVertical("GroupBox");
                            EditorGUILayout.BeginHorizontal();
                            ColoredBackgroundVoid(asset is Component, Color.yellow, () => EditorGUILayout.LabelField(asset.GetType().Name, EditorStyles.boldLabel));
                            ColoredBackgroundVoid(deepview.isEditorOnly, Color.red, () => EditorGUILayout.ObjectField(asset, typeof(Object), false));
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.TextField(localize.Text(Phrases.path), AssetDatabase.GetAssetPath(asset), EditorStyles.label);

                            EditorGUILayout.BeginHorizontal();
                            var halfWidth = (EditorGUIUtility.currentViewWidth - SidebarWidth - 110) / 2;
                            DrawDependencyList(Phrases.is_depended_by, asset, halfWidth, _analysis.AfterCullingDataDeepviews, dv => dv.isDependedBy, true);
                            DrawDependencyList(Phrases.depends_on, asset, halfWidth, _analysis.AfterCullingDataDeepviews, dv => dv.dependsOn, true);
                            EditorGUILayout.EndHorizontal();

                            EditorGUILayout.EndVertical();
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void DrawDependencyList(string phrases, Object asset, float halfWidth, Dictionary<Object, Deepview> deepviewByAsset, Func<Deepview, List<DeepviewDependency>> dependencyExtractor, bool isDeep)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(halfWidth));
            localize.LabelField(phrases);
            if (deepviewByAsset != null && deepviewByAsset.TryGetValue(asset, out var dv))
            {
                var dependencies = dependencyExtractor(dv);
                foreach (var deepviewDependency in dependencies)
                {
                    var obj = deepviewDependency.item;
                    if (isDeep)
                    {
                        EditorGUILayout.BeginHorizontal();
                        ColoredBackgroundVoid(_analysis.DataDeepviews[obj].isEditorOnly, Color.red, () => EditorGUILayout.ObjectField(obj, typeof(Object), false));
                        if (deepviewDependency.persistentAsset != null && deepviewDependency.persistentAsset != deepviewDependency.item)
                        {
                            EditorGUILayout.LabelField("▶", GUILayout.Width(20));
                            EditorGUILayout.ObjectField(deepviewDependency.persistentAsset, typeof(Object), false);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        EditorGUILayout.ObjectField(obj, typeof(Object), false);
                    }

                    if (deepviewDependency.reason != TraversalReason.IsObjectReference)
                    {
                        var reasonText = deepviewDependency.reason switch
                        {
                            TraversalReason.IsRoot => localize.Text(Phrases.reason_IsRoot),
                            TraversalReason.IsChildTransform => localize.Text(Phrases.reason_IsChildTransform),
                            TraversalReason.IsPrefabSource => localize.Text(Phrases.reason_IsPrefabSource),
                            TraversalReason.IsPropertyModificationOfPrefabInstance => localize.Text(Phrases.reason_IsPropertyModificationOfPrefabInstance),
                            TraversalReason.IsDriveByAsset => localize.Text(Phrases.reason_IsDriveByAsset),
                            TraversalReason.IsObjectReference => localize.Text(Phrases.reason_IsObjectReference),
                            TraversalReason.IsComponent => localize.Text(Phrases.reason_IsComponent),
                            _ => deepviewDependency.reason.ToString()
                        };
                        if (isDeep)
                        {
                            ColoredBackgroundVoid(obj != null && typeof(Component).IsAssignableFrom(obj.GetType()), Color.yellow, () =>
                                EditorGUILayout.LabelField($"({(obj != null ? obj.GetType().Name : "null")}) " + reasonText, EditorStyles.miniLabel));
                        }
                        else
                        {
                            EditorGUILayout.LabelField(reasonText, EditorStyles.miniLabel);
                        }
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void ScheduleAnalysis()
        {
            if (_analysisScheduled) return;
            _analysisScheduled = true;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    _analysis.DoPerformAnalysis(_multiTargets ? targets.Where(o => o != null).Distinct().ToList() : new List<Object> { target }, _afterCullingTypeFullNames, _includeEditorOnly);
                    RefreshCachedTypes();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
                finally
                {
                    _analysisScheduled = false;
                    Repaint();
                }
            };
        }

        private void RefreshCachedTypes()
        {
            if (_analysis.DataSortedTypes == null)
            {
                _cachedComponents = new List<Type>();
                _cachedNonComponents = new List<Type>();
                _cachedComponentGroups = new List<ComponentGroup>();
                return;
            }

            (_cachedComponents, _cachedNonComponents) = PartitionBy(_analysis.DataSortedTypes, IsComponentOrStateMachineBehaviour);
            _cachedComponentGroups = _cachedComponents
                .GroupBy(ttype =>
                {
                    var typeName = ttype.Name;
                    for (var i = typeName.Length - 1; i >= 3; i--)
                    {
                        if (char.IsUpper(typeName[i]))
                        {
                            var prefix = typeName.Substring(0, i);
                            if (_cachedComponents.Count(t => t.Name.StartsWith(prefix)) >= 5)
                            {
                                return prefix;
                            }
                        }
                    }
                    return "";
                })
                .OrderBy(g => g.Key, StringComparer.InvariantCulture)
                .Select(g => new ComponentGroup { Key = g.Key, Types = g.ToList() })
                .ToList();
        }

        private static void ColoredBackgroundVoid(bool isActive, Color bgColor, Action inside)
        {
            ColoredBackground(isActive, bgColor, () =>
            {
                inside();
                return (object)null;
            });
        }

        private static T ColoredBackground<T>(bool isActive, Color bgColor, Func<T> inside)
        {
            var col = GUI.color;
            try
            {
                if (isActive) GUI.color = bgColor;
                return inside();
            }
            finally
            {
                GUI.color = col;
            }
        }
    }

    internal enum HideItemsFilter
    {
        ShowEverything,
        HideLeafWithOneParent,
        HideAllLeaf
    }
}
