using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Hai.TransferAssistant
{
    internal class TransferAssistantAnalysis
    {
        internal const string UnknownAssetAndDLLTypeName = "DefaultAsset";
        
        public HashSet<Object> Targets => _targets;
        private HashSet<Object> _targets;
        
        public Dictionary<GameObject, HashSet<GameObject>> DataPrefabObjectToInstances;
        public List<Type> DataSortedTypes;
        
        public Dictionary<Object, Deepview> DataDeepviews;
        public Dictionary<Type, int> DataTypeCounts;
        public Dictionary<Object, Deepview> AfterCullingDataDeepviews;
        public Dictionary<Type, int> AfterCullingTypeCounts;
        public Dictionary<Object, List<Object>> AfterCullingSubassetManifest;
        public int TotalAfterCulling;
        private HashSet<string> _excludedTypeNames;
        private bool _includeEditorOnly;
        private bool _includeHiddenInPrefabs;
        private Dictionary<Object, Object> _subAssetToAssetMap;

        public event Action OnUpdate;

        public void DoPerformAnalysis(List<Object> targets, HashSet<string> afterCullingTypeFullNames, bool includeEditorOnly, bool includeHiddenInPrefabs)
        {
            if (targets.Count == 0)
            {
                Debug.LogWarning("Targets is empty.");
                return;
            }

            _targets = ExpandFolders(targets.ToHashSet());
            _excludedTypeNames = afterCullingTypeFullNames.ToHashSet();
            _includeEditorOnly = includeEditorOnly;
            _includeHiddenInPrefabs = includeHiddenInPrefabs;

            DiscoverPrefabInstances();
            DiscoverThroughTraversal();
            _subAssetToAssetMap = new Dictionary<Object, Object>();
            foreach (var deepview in DataDeepviews.Values)
            {
                if (deepview.isAssetOnDisk && !deepview.isMainAsset)
                {
                    _subAssetToAssetMap[deepview.subject] = AssetDatabase.LoadAssetAtPath<Object>(deepview.path);
                }
            }
            UpdateCullingInternal();
        }

        private HashSet<Object> ExpandFolders(HashSet<Object> targets)
        {
            var newTargets = new HashSet<Object>(targets);
            
            foreach (var target in targets)
            {
                if (target is DefaultAsset)
                {
                    var assetPath = AssetDatabase.GetAssetPath(target);
                    var assetGuids = AssetDatabase.FindAssets("t:Object", new[] { assetPath });
                    foreach (var assetGuid in assetGuids)
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(assetGuid));
                        if (asset != null)
                        {
                            newTargets.Add(asset);
                        }
                    }
                }
            }
            
            return newTargets.ToHashSet();
        }

        private void DiscoverThroughTraversal()
        {
            DataDeepviews = new Dictionary<Object, Deepview>();
            DataTypeCounts = new Dictionary<Type, int>();
            
            var dataTraversalDependentToOrigins = new Dictionary<Object, List<TraversalLog>>();
            var traversalResults = new HashSet<Object>();
            
            foreach (var target in _targets)
            {
                var results = DiscoverAssetsRequiredForEditing.FindAllAssetsAndComponents(target, new DiscoveryOptions
                {
                    IncludePrefabSource = true,
                    IncludeDefaultTexturesInsideShader = true,
                    IncludeDriveByAssets = false,
                    IncludePrefabInstanceGhostReferences = false,
                    IncludeReferencesContainedWithinPrefabSource = _includeHiddenInPrefabs
                });
                foreach (var pair in results.ReasonsForDiscovery)
                {
                    if (!dataTraversalDependentToOrigins.ContainsKey(pair.Key))
                    {
                        dataTraversalDependentToOrigins.Add(pair.Key, new List<TraversalLog>());
                    }
                    dataTraversalDependentToOrigins[pair.Key].AddRange(pair.Value);
                }
                traversalResults.UnionWith(results.FoundAssets);
            }
            
            DiscoverThroughTraversal(dataTraversalDependentToOrigins, traversalResults.ToList());
            
            DataSortedTypes = DataTypeCounts.Keys
                .OrderBy(t => t.Name == UnknownAssetAndDLLTypeName)
                .ThenBy(t => t.Name, StringComparer.InvariantCulture).ToList();
        }

        private void DiscoverThroughTraversal(Dictionary<Object, List<TraversalLog>> dataTraversalDependentToOrigins, List<Object> traversalResults)
        {
            var dataTraversalOriginToDependents = new Dictionary<Object, List<TraversalLog>>();
            
            foreach (var dependentToOrigins in dataTraversalDependentToOrigins)
            {
                foreach (var traversalLog in dependentToOrigins.Value)
                {
                    if (traversalLog.originatorNullableIfRoot == null) continue;
                    
                    if (dataTraversalOriginToDependents.ContainsKey(traversalLog.originatorNullableIfRoot))
                    {
                        dataTraversalOriginToDependents[traversalLog.originatorNullableIfRoot].Add(traversalLog);
                    }
                    else
                    {
                        dataTraversalOriginToDependents.Add(traversalLog.originatorNullableIfRoot, new List<TraversalLog> { traversalLog });
                    }
                }
            }
            
            var allAssetsInTraversal = new HashSet<Object>();
            foreach (var kvp in dataTraversalOriginToDependents)
            {
                if (kvp.Key != null) allAssetsInTraversal.Add(kvp.Key);
            }
            foreach (var kvp in dataTraversalDependentToOrigins)
            {
                if (kvp.Key != null) allAssetsInTraversal.Add(kvp.Key);
            }

            foreach (var asset in allAssetsInTraversal)
            {
                if (asset == null) continue;

                var dependsOn = dataTraversalOriginToDependents.TryGetValue(asset, out var deps) ? deps : new List<TraversalLog>();
                var isDependedBy = dataTraversalDependentToOrigins.TryGetValue(asset, out var parents) ? parents : new List<TraversalLog>();

                var isMainAsset = AssetDatabase.IsMainAsset(asset);
                // This differs from IsPersistent in that IsPersistent considers that all child GameObjects in the hierarchy of a prefab are persistent,
                // while isDiskAsset does not.
                var isDiskAsset = AssetDatabase.IsSubAsset(asset) || isMainAsset;
                var assetPath = isDiskAsset ? AssetDatabase.GetAssetPath(asset) : "";
                var filteredDependsOn = dependsOn
                    .Where(log => log.discoveredObject != null)
                    .Select(log => new DeepviewDependency
                    {
                        item = log.discoveredObject,
                        persistentAsset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GetAssetPath(log.discoveredObject)),
                        reason = log.reason,
                    })
                    .Distinct()
                    // We stopped sorting because it messes up the hierarchy order in the Tree view tab.
                    // .OrderBy(dependency => dependency.reason != TraversalReason.IsChildTransform) // This is sort of a hack so that the Tree view tab prioritizes child transforms, so that "Already Shown" doesn't happen on child transform traversal.
                    // .ThenBy(dependency => dependency.item.name)
                    .ToList();
                var filteredIsDependedBy = isDependedBy
                    .Where(log => log.originatorNullableIfRoot != null)
                    .Select(log => new DeepviewDependency
                    {
                        item = log.originatorNullableIfRoot,
                        persistentAsset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GetAssetPath(log.originatorNullableIfRoot)),
                        reason = log.reason,
                    })
                    // We stopped sorting because it messes up the hierarchy order in the Tree view tab.
                    // .OrderBy(dependency => dependency.reason != TraversalReason.IsChildTransform) // This is sort of a hack so that the Tree view tab prioritizes child transforms, so that "Already Shown" doesn't happen on child transform traversal.
                    // .ThenBy(dependency => dependency.item == null)
                    // .ThenBy(dependency => dependency.item != null ? dependency.item.name : "")
                    .ToList();
                var boringReferences = filteredDependsOn.Concat(filteredIsDependedBy)
                    .Select(dependency => dependency.persistentAsset != null ? dependency.persistentAsset : (dependency.item is GameObject ? dependency.item : null))
                    .Distinct()
                    .ToList();
                var isBoring = !isDiskAsset // This is important so that prefab source don't get withered out
                               && boringReferences.Count == 1 && boringReferences[0] != null && boringReferences[0] is GameObject;
                DataDeepviews.Add(asset, new Deepview
                {
                    subject = asset,
                    subjectTypeName = asset.GetType().Name,
                    path = assetPath,
                    isAssetOnDisk = isDiskAsset,
                    isAnyPrefabInstanceRoot = asset is GameObject assetGo && PrefabUtility.IsAnyPrefabInstanceRoot(assetGo),
                    isMainAsset = isMainAsset,
                    isExportable = isMainAsset && (assetPath.StartsWith("Assets/") || assetPath.StartsWith("Packages/")),
                    dependsOn = filteredDependsOn,
                    isDependedBy = filteredIsDependedBy,
                    isBoring = isBoring,
                    isEditorOnly = IsAnyEditorOnly(asset),
                    isPrefabModelDiskReference = isDiskAsset && PrefabUtility.IsPartOfModelPrefab(asset) && isMainAsset,
                });
            }

            foreach (var asset in traversalResults)
            {
                if (asset == null) continue;

                var typeName = asset.GetType();
                
                DataTypeCounts.TryAdd(typeName, 0);
                if (DataDeepviews.TryGetValue(asset, out var dv) && !dv.isBoring)
                {
                    DataTypeCounts[typeName]++;
                }
            }
        }

        private bool IsAnyEditorOnly(Object asset)
        {
            if (asset is Component comp)
            {
                asset = comp.gameObject;
            }
            
            if (asset is not GameObject go) return false;
            if (go.CompareTag("EditorOnly")) return true;
            if (go.transform.parent != null) return IsAnyEditorOnly(go.transform.parent.gameObject);
            return false;
        }

        private void DiscoverPrefabInstances()
        {
            DataPrefabObjectToInstances = new Dictionary<GameObject, HashSet<GameObject>>();
            foreach (var target in _targets)
            {
                if (target is GameObject targetGo)
                {
                    var tryFindPrefabsIn = new Queue<GameObject>();
                    tryFindPrefabsIn.Enqueue(targetGo);
                    while (tryFindPrefabsIn.TryDequeue(out var root))
                    {
                        var prefabInstances = FindAllPrefabInstances(root);
                        foreach (var prefabInstance in prefabInstances)
                        {
                            var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(prefabInstance);
                            if (DataPrefabObjectToInstances.TryGetValue(prefabSource, out var instances))
                            {
                                instances.Add(prefabInstance);
                                // We don't traverse the prefab source again, because it already existed in the dictionary.
                            }
                            else
                            {
                                DataPrefabObjectToInstances.Add(prefabSource, new HashSet<GameObject> { prefabInstance });
                                tryFindPrefabsIn.Enqueue(prefabInstance);
                            }
                        }
                    }
                }
            }
        }

        public void UpdateCulledCache(HashSet<string> excludedTypeFullNames)
        {
            _excludedTypeNames = excludedTypeFullNames.ToHashSet();
            UpdateCullingInternal();
        }

        public void UpdateIncludeEditorOnly(bool includeEditorOnly)
        {
            _includeEditorOnly = includeEditorOnly;
            UpdateCullingInternal();
        }

        private void UpdateCullingInternal()
        {
            AfterCullingDataDeepviews = new Dictionary<Object, Deepview>();
            AfterCullingTypeCounts = new Dictionary<Type, int>();
            TotalAfterCulling = 0;
            
            // First, reset.
            foreach (var current in DataDeepviews.Values)
            {
                current.isReachable = false;
                current.isDeadEnd = true;
            }

            var deadEndResolutionQueue = new Queue<Deepview>();
            
            var visited = new HashSet<Object>();
            var reachabilityQueue = new Queue<Object>();
            foreach (var target in _targets)
            {
                reachabilityQueue.Enqueue(target);
            }
            
            // Then, mark all assets that are reachable from the root.
            while (reachabilityQueue.TryDequeue(out var dequeu))
            {
                if (visited.Add(dequeu))
                {
                    var deepview = DataDeepviews[dequeu];
                    var isExcludedType = _excludedTypeNames.Contains(deepview.subject.GetType().FullName);
                    if (!isExcludedType && (_includeEditorOnly || (
                            // Prefabs still need child prefabs to exist in the project, even if that GameObject is inside EditorOnly.
                            !deepview.isEditorOnly || deepview.isAnyPrefabInstanceRoot
                        )) || _targets.Contains(dequeu))
                    {
                        deepview.isReachable = true;
                        if (deepview.isAssetOnDisk)
                        {
                            deepview.isDeadEnd = false;
                            deadEndResolutionQueue.Enqueue(deepview);
                        }
                        foreach (var deepviewDependency in deepview.dependsOn)
                        {
                            reachabilityQueue.Enqueue(deepviewDependency.item);
                        }
                    }
                }
            }
            
            // Then, prune objects that don't depend on assets on disk.
            while (deadEndResolutionQueue.TryDequeue(out var current))
            {
                foreach (var parentInfo in current.isDependedBy)
                {
                    if (DataDeepviews.TryGetValue(parentInfo.item, out var parentDeepview))
                    {
                        if (parentDeepview.isDeadEnd && parentDeepview.isReachable)
                        {
                            parentDeepview.isDeadEnd = false;
                            deadEndResolutionQueue.Enqueue(parentDeepview);
                        }
                    }
                }
            }

            // Finally, create a new DataDeepview that only has the relevant stuff.
            foreach (var (asset, current) in DataDeepviews)
            {
                if (current.isDeadEnd) continue;
                if (current.isBoring) continue;

                AfterCullingDataDeepviews.Add(asset, new Deepview
                {
                    subject = current.subject,
                    subjectTypeName = current.subjectTypeName,
                    path = current.path,
                    isAssetOnDisk = current.isAssetOnDisk,
                    isAnyPrefabInstanceRoot = current.isAnyPrefabInstanceRoot,
                    isMainAsset = current.isMainAsset,
                    isExportable = current.isExportable,
                    dependsOn = current.dependsOn
                        .Where(dep => DataDeepviews.TryGetValue(dep.item, out var depDv) && !depDv.isDeadEnd)
                        .Distinct()
                        .ToList(),
                    isDependedBy = current.isDependedBy
                        .Where(dep => DataDeepviews.TryGetValue(dep.item, out var depDv) && !depDv.isDeadEnd)
                        .Distinct()
                        .ToList(),
                    isReachable = current.isReachable,
                    isDeadEnd = current.isDeadEnd,
                    isBoring = current.isBoring,
                    isEditorOnly = current.isEditorOnly,
                    isPrefabModelDiskReference = current.isPrefabModelDiskReference,
                });

                var ttype = asset.GetType();
                AfterCullingTypeCounts.TryAdd(ttype, 0);
                AfterCullingTypeCounts[ttype]++;
                if (current.isMainAsset)
                {
                    TotalAfterCulling++;
                }
            }
            
            AfterCullingSubassetManifest = new Dictionary<Object, List<Object>>();
            foreach (var deepview in AfterCullingDataDeepviews.Values)
            {
                if (_subAssetToAssetMap.TryGetValue(deepview.subject, out var mainAsset))
                {
                    if (!AfterCullingSubassetManifest.ContainsKey(mainAsset))
                    {
                        AfterCullingSubassetManifest[mainAsset] = new List<Object> { deepview.subject };
                    }
                    else
                    {
                        AfterCullingSubassetManifest[mainAsset].Add(deepview.subject);
                    }
                }
            }
            
            OnUpdate?.Invoke();
        }


        private List<GameObject> FindAllPrefabInstances(GameObject root)
        {
            var result = new List<GameObject>();
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                {
                    result.Add(t.gameObject);
                }
            }
            return result;
        }

        public static bool IsIgnoredScene(Scene scene)
        {
            return scene.name.Contains("NDMF Preview");
        }

        public static bool IsComponentOrStateMachineBehaviour(Type t)
        {
            return typeof(Component).IsAssignableFrom(t) || typeof(StateMachineBehaviour).IsAssignableFrom(t);
        }
    }
    
    internal class Deepview
    {
        public Object subject;
        public string subjectTypeName;
        public string path;
        public List<DeepviewDependency> dependsOn;
        public List<DeepviewDependency> isDependedBy;
        public bool isReachable;
        public bool isDeadEnd;
        public bool isAssetOnDisk;
        public bool isMainAsset;
        public bool isExportable;
        public bool isBoring;
        public bool isEditorOnly;
        public bool isAnyPrefabInstanceRoot;
        public bool isPrefabModelDiskReference;
    }
    
    internal class DeepviewDependency
    {
        public Object item;
        public Object persistentAsset;
        public TraversalReason reason;

        // Makes it easier to deduplicate a dependency, for example when a material requires the same texture multiple times.
        protected bool Equals(DeepviewDependency other)
        {
            return Equals(item, other.item) && Equals(persistentAsset, other.persistentAsset) && reason == other.reason;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((DeepviewDependency)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (item != null ? item.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (persistentAsset != null ? persistentAsset.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)reason;
                return hashCode;
            }
        }
    }

    internal struct PrefabGroup
    {
        public GameObject Source;
        public List<GameObject> InstancesToShow;
    }

    internal struct TypeGroup
    {
        public Type TType;
        public List<Object> Assets;
    }
}
