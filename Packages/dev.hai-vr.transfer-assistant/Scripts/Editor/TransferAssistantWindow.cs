using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Hai.TransferAssistant
{
    public class TransferAssistantWindow : EditorWindow
    {
        internal static readonly Color PrefabInstanceRootGameObjectColor = new(0.66f, 1f, 0.82f);
        internal static readonly Color PersistentGameObjectColor = new(0.57f, 0.9f, 1f);
        internal static readonly Color ComponentlikeColor = Color.yellow;
        internal static readonly Color EditorOnlyColor = new(1f, 0.65f, 0.67f);
        
        private const string PrefsPrefix = "Hai.TransferAssistant.";
        private const string AfterCullingTypeFullNamesPrefsKey = PrefsPrefix + "AfterCullingTypeFullNames";
        private const string IncludeEditorOnlyPrefsKey = PrefsPrefix + "IncludeEditorOnly";
        private const string IncludeHiddenInPrefabsPrefsKey = PrefsPrefix + "IncludeHiddenInPrefabs";
        private const string TargetModePrefsKey = PrefsPrefix + "TargetMode";
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
        private TargetMode _targetMode;
        public Object[] targets;

        private HashSet<string> _afterCullingTypeFullNames = AfterCullingTypeFullNamesDefault.ToHashSet();
        private bool _includeEditorOnly = true;
        private bool _includeHiddenInPrefabs = false;

        private string _search = "";
        private Object _searchObject;

        private bool _analysisScheduled;
        private TransferAssistantAnalysis _analysis;
        
        private Vector2 _sidebarScrollPos;

        private List<Type> _cachedComponents = new();
        private List<Type> _cachedNonComponents = new();
        private List<ComponentGroup> _cachedComponentGroups = new();
        private VisualizeTreeBuilder _visualizeTreeBuilder;

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

            if (EditorPrefs.HasKey(AfterCullingTypeFullNamesPrefsKey))
            {
                var storedNames = EditorPrefs.GetString(AfterCullingTypeFullNamesPrefsKey);
                _afterCullingTypeFullNames = storedNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            }

            if (EditorPrefs.HasKey(IncludeEditorOnlyPrefsKey))
            {
                _includeEditorOnly = EditorPrefs.GetBool(IncludeEditorOnlyPrefsKey, true);
            }

            if (EditorPrefs.HasKey(TargetModePrefsKey))
            {
                _targetMode = (TargetMode)EditorPrefs.GetInt(TargetModePrefsKey, (int)TargetMode.SingleTarget);
            }
            
            if (EditorPrefs.HasKey(IncludeHiddenInPrefabsPrefsKey))
            {
                _includeHiddenInPrefabs = EditorPrefs.GetBool(IncludeHiddenInPrefabsPrefsKey, false);
            }
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(AfterCullingTypeFullNamesPrefsKey, string.Join(",", _afterCullingTypeFullNames));
            EditorPrefs.SetBool(IncludeEditorOnlyPrefsKey, _includeEditorOnly);
            EditorPrefs.SetInt(TargetModePrefsKey, (int)_targetMode);
            EditorPrefs.SetBool(IncludeHiddenInPrefabsPrefsKey, _includeHiddenInPrefabs);
        }

        [MenuItem("Assets/Transfer Assistant...")]
        public static void AnalyzeSelection()
        {
            var window = GetWindow<TransferAssistantWindow>(ShortTitleName);
            if (Selection.objects.Length > 1)
            {
                window.target = null;
                window.targets = Selection.objects.ToArray();
                window._targetMode = TargetMode.MultipleTargets;
            }
            else
            {
                window.target = Selection.activeObject;
                window.targets = Array.Empty<Object>();
                window._targetMode = TargetMode.SingleTarget;
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
            if (_targetMode == TargetMode.MultipleTargets)
            {
                var serializedObject = new SerializedObject(this);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(targets)), new GUIContent(localize.Text(Phrases.targets)));
                serializedObject.ApplyModifiedProperties();
            }
            else if (_targetMode == TargetMode.SingleTarget)
            {
                target = EditorGUILayout.ObjectField(localize.Text(Phrases.target), target, typeof(Object), true);
            }
            else if (_targetMode == TargetMode.CurrentScenes)
            {
                EditorGUI.BeginDisabledGroup(true);
                var loadedSceneCount = SceneManager.sceneCount;
                for (var i = 0; i < loadedSceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded && !TransferAssistantAnalysis.IsIgnoredScene(scene))
                    {
                        var sceneAsset = AssetDatabase.LoadAssetAtPath<Object>(scene.path);
                        if (sceneAsset != null)
                        {
                            EditorGUILayout.ObjectField("", sceneAsset, typeof(SceneAsset), true);
                        }
                    }
                }
                EditorGUI.EndDisabledGroup();
            }

            EditorGUI.BeginChangeCheck();
            var targetModePhrases = new[] { Phrases.target_mode_single, Phrases.target_mode_multiple, Phrases.target_mode_current_scenes };
            var targetModeOptions = targetModePhrases.Select(phrase => localize.Text(phrase)).ToArray();
            _targetMode = (TargetMode)EditorGUILayout.Popup((int)_targetMode, targetModeOptions, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck())
            {
                SavePrefs();
            }
            EditorGUILayout.EndHorizontal();

            var invalid = _targetMode switch
            {
                TargetMode.SingleTarget => target == null || target is SceneAsset,
                TargetMode.MultipleTargets => targets == null || targets.Length == 0 || targets.Length == 1 && targets[0] is SceneAsset,
                TargetMode.CurrentScenes => false,
                _ => throw new ArgumentOutOfRangeException()
            };
            if (_targetMode == TargetMode.SingleTarget && target is SceneAsset || _targetMode == TargetMode.MultipleTargets && targets != null && targets.Length == 1 && targets[0] is SceneAsset)
            {
                localize.HelpBox(Phrases.msg_scenes_must_be_open, MessageType.Error);
            }
            if (!invalid)
            {
                var usesSceneObject = _targetMode switch
                {
                    TargetMode.SingleTarget => !EditorUtility.IsPersistent(target),
                    TargetMode.MultipleTargets => targets.Any(o => !EditorUtility.IsPersistent(o)),
                    TargetMode.CurrentScenes => false,
                    _ => throw new ArgumentOutOfRangeException()
                };
                if (usesSceneObject)
                {
                    localize.HelpBox(Phrases.msg_scene_objects_selected, MessageType.Warning);
                }
            }

            var isAnalysisPossible = invalid || _analysisScheduled;
            
            EditorGUI.BeginDisabledGroup(isAnalysisPossible);
            if (GUILayout.Button(_analysisScheduled ? localize.Text(Phrases.analysis_in_progress) : localize.Text(Phrases.perform_analysis)))
            {
                ScheduleAnalysis();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.BeginHorizontal();
            LayoutSidebar(isAnalysisPossible);
            LayoutMainPane();
            EditorGUILayout.EndHorizontal();
        }

        private void LayoutSidebar(bool isAnalysisPossible)
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
                    EditorGUI.BeginChangeCheck();
                    
                    EditorGUI.BeginDisabledGroup(isAnalysisPossible);
                    _includeHiddenInPrefabs = EditorGUILayout.ToggleLeft(localize.Text(Phrases.include_hidden_in_prefabs), _includeHiddenInPrefabs);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SavePrefs();
                        ScheduleAnalysis();
                    }
                    EditorGUI.EndDisabledGroup();
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
                _includeEditorOnly = true;
                _includeHiddenInPrefabs = false;
                SavePrefs();
                ScheduleAnalysis();
            }
        }

        private void LayoutMainPane()
        {
            EditorGUILayout.BeginVertical();
            localize.LabelField(Phrases.exploration, EditorStyles.boldLabel);
            localize.HelpBox(Phrases.msg_will_not_affect_export, MessageType.None);
            EditorGUILayout.BeginHorizontal();
            var prevSearch = _search;
            _search = EditorGUILayout.TextField(localize.Text(Phrases.search), prevSearch);
            if (prevSearch != _search)
            {
                _searchObject = null;
            }
            _searchObject = EditorGUILayout.ObjectField(_searchObject, typeof(Object), true);
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_search) && _searchObject == null);
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                _search = "";
                _searchObject = null;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_analysis.DataPrefabObjectToInstances != null)
            {
                LayoutVisualizeTree();
            }

            localize.Selector(() => _localize = NewLoc());
            EditorGUILayout.EndVertical();
        }

        private void LayoutTypeToggle(Type ttype)
        {
            var count = _analysis.DataTypeCounts[ttype];
            var culledCount = _analysis.AfterCullingTypeCounts != null && _analysis.AfterCullingTypeCounts.TryGetValue(ttype, out var c) ? c : 0;
            var isCulled = _afterCullingTypeFullNames.Contains(ttype.FullName);
            var friendlyTypeName = ttype.Name == TransferAssistantAnalysis.UnknownAssetAndDLLTypeName ? localize.Text(Phrases.unknown_assets_and_dll_files) : ttype.Name;

            var isComponentOrStateMachineBehaviour = TransferAssistantAnalysis.IsComponentOrStateMachineBehaviour(ttype);
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

        private void LayoutVisualizeTree()
        {
            if (_visualizeTreeBuilder == null)
            {
                _visualizeTreeBuilder = new VisualizeTreeBuilder(_localize);
                _visualizeTreeBuilder.OnSearchObjectRequested = obj =>
                {
                    _searchObject = _searchObject == obj ? null : obj;
                };
                _visualizeTreeBuilder.WhenAnalysisUpdated(_analysis);
                _analysis.OnUpdate += () => _visualizeTreeBuilder.WhenAnalysisUpdated(_analysis);
            }

            _visualizeTreeBuilder.MarkVisible(_search, _searchObject);
        }

        private void ScheduleAnalysis()
        {
            if (_analysisScheduled) return;
            _analysisScheduled = true;

            Repaint();
            EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
            {
                try
                {
                    _analysis.DoPerformAnalysis(CollectTargets(), _afterCullingTypeFullNames, _includeEditorOnly, _includeHiddenInPrefabs);
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

        private List<Object> CollectTargets()
        {
            switch (_targetMode)
            {
                case TargetMode.SingleTarget:
                    return new List<Object> { target };
                case TargetMode.MultipleTargets:
                    return targets.Where(o => o != null).Distinct().ToList();
                case TargetMode.CurrentScenes:
                {
                    var objects = new List<Object>();
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (scene.isLoaded && !TransferAssistantAnalysis.IsIgnoredScene(scene))
                        {
                            var sceneAsset = AssetDatabase.LoadAssetAtPath<Object>(scene.path);
                            if (sceneAsset != null)
                            {
                                objects.Add(sceneAsset);
                            }
                            
                            // TODO: We should also try to pull the skybox, fog settings, lighting data, etc.
                            
                            var rootObjects = scene.GetRootGameObjects();
                            objects.AddRange(rootObjects);
                        }
                    }
                    return objects;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(_targetMode));
            }
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

            (_cachedComponents, _cachedNonComponents) = PartitionBy(_analysis.DataSortedTypes, TransferAssistantAnalysis.IsComponentOrStateMachineBehaviour);
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

        internal static void ColoredBackgroundVoid(bool isActive, Color bgColor, Action inside)
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

        private string ToLocalizedReason(TraversalReason reason)
        {
            return reason switch
            {
                TraversalReason.IsRoot => localize.Text(Phrases.reason_IsRoot),
                TraversalReason.IsChildTransform => localize.Text(Phrases.reason_IsChildTransform),
                TraversalReason.IsPrefabSource => localize.Text(Phrases.reason_IsPrefabSource),
                TraversalReason.IsPropertyModificationOfPrefabInstance => localize.Text(Phrases.reason_IsPropertyModificationOfPrefabInstance),
                TraversalReason.IsDriveByAsset => localize.Text(Phrases.reason_IsDriveByAsset),
                TraversalReason.IsObjectReference => localize.Text(Phrases.reason_IsObjectReference),
                TraversalReason.IsComponent => localize.Text(Phrases.reason_IsComponent),
                _ => reason.ToString()
            };
        }
    }

    public enum TargetMode
    {
        SingleTarget,
        MultipleTargets,
        CurrentScenes
    }
}
