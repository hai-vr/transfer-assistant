using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Hai.TransferAssistant
{
    internal class DiscoveryOptions
    {
        /// Prefab instances must retain a working reference to their prefab source at Edit time, otherwise the exported prefab would be unusable.<br/>
        /// This is the main difference in requirement compared to avatar builds.
        public bool IncludePrefabSource = true; // Recommendation is true, always, for the use case that this discovery utility is built for. If this was for avatar builds, things would be different.
        
        /// Some shaders have references to default textures in them.<br/>
        /// Default textures are not included in a build, and may or may not be necessary in a .unitypackage depending on the use.<br/>
        /// (note that since an export does not include shaders by default, it generally means those textures wouldn't get included by default either)
        public bool IncludeDefaultTexturesInsideShader = true; // Recommendation is true, because if the user wants to include shaders, they probably want the textures too.
        
        /// Assets that are siblings (= neighboring sub-assets) of another asset are not included in a build, but would be present in a default Unity .unitypackage, along with everything that those sibling assets depend on.<br/>
        /// Example: We depend on a Mesh of a FBX. The FBX references a Material that has a Texture. The Material and the Texture are included through drive-by.<br/>
        /// Such an inclusion is questionable for transferring assets, but it may be needed sometimes.
        public bool IncludeDriveByAssets = true; // Recommendation is false, often. If IncludeReferencesContainedWithinPrefabSource is true, it will often preclude this.
        
        /// Prefabs instances may contain ghost references, which are very often not desirable.<br/>
        /// This happens when an override is applied to a prefab instance, and the GameObject or Component is then removed; the override still remains,
        /// and is restored when the GameObject or Component is un-removed.<br/>
        /// Setting this to true will introspect all overrides on that prefab instance.
        public bool IncludePrefabInstanceGhostReferences = false; // Recommendation is false, always. If the user overrides stuff, the useful assets will show up during normal traversal of the objects.

        // Assets that are referenced by a prefab source are not included in a build, and are not necessarily needed by the user.<br/>
        // If the user plans to start from the original prefab from scratch, then they may need those assets.
        public bool IncludeReferencesContainedWithinPrefabSource = false; // Recommendation is false, very often, since the user usually wants to transfer only the assets they need for their active avatar setup.
    }
    
    /// This utility class recursively walks through the objects that are required by a root object. However, it also walks through objects
    /// that are referenced which may not be present during an avatar build, meaning it may keep prefab source objects, and objects that
    /// are required by those prefab source objects.<br/>
    /// <br/>
    /// Additional options defined in DiscoveryOptions, may also traverse references inside prefabs, ghost references inside Prefabs,
    /// and nested assets typically contained within Animator Controllers and FBX Prefab models.
    internal class DiscoverAssetsRequiredForEditing
    {
        private readonly HashSet<Object> _results = new();
        private readonly Dictionary<Object, List<TraversalLog>> _reasonsForDiscovery = new();
        
        private readonly List<string> _texturePropertyNames = new();
        private readonly HashSet<Object> _discovered = new();
        private readonly HashSet<Object> _everEnqueued = new();
        private readonly Queue<Object> _queue = new();

        private DiscoverAssetsRequiredForEditing() { }

        /// Finds all assets needed by a project at Edit time, meant for use in .unitypackage asset transfer. This is not meant for finding assets that will be included in a build.<br/>
        /// <br/>
        /// Note: This purposefully ignores state machine transitions, state machines, and states, as controllers get special treatment.<br/>
        /// These assets are assumed to be inside the Animator Controller asset.
        public static FindAllAssetsResult FindAllAssetsAndComponents(GameObject root, DiscoveryOptions discoveryOptions)
        {
            return new DiscoverAssetsRequiredForEditing().DoFindAllAssetsAndComponents(root, discoveryOptions);
        }

        void DiscoverAndEnqueueIfApplicable(Object obj, TraversalReason traversalReason, Object originatorNullableIfRoot, bool needsEnqueue = true, Object generatorNullable = null)
        {
            if (!obj) return;
            if (obj == originatorNullableIfRoot) return;
            if (obj.GetType() == typeof(Object)) return; // What is this, even? This happens a lot
            if (obj is Transform t) obj = t.gameObject;
            _discovered.Add(obj);
            if (needsEnqueue)
            {
                if (_everEnqueued.Add(obj))
                {
                    _queue.Enqueue(obj);
                }
            }

            var traversalPair = new TraversalLog { discoveredObject = obj, originatorNullableIfRoot = originatorNullableIfRoot, reason = traversalReason, generatorNullable = generatorNullable };
            if (_reasonsForDiscovery.ContainsKey(obj))
            {
                _reasonsForDiscovery[obj].Add(traversalPair);
            }
            else
            {
                _reasonsForDiscovery.Add(obj, new List<TraversalLog> { traversalPair });
            }
        }

        private FindAllAssetsResult DoFindAllAssetsAndComponents(GameObject root, DiscoveryOptions discoveryOptions)
        {
            DiscoverAndEnqueueIfApplicable(root, TraversalReason.IsRoot, null);
            
            while (_queue.TryDequeue(out var current))
            {
                if (current is GameObject currentGo)
                {
                    var holder = EditorUtility.IsPersistent(current) ? AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GetAssetPath(current)) : null;
                    
                    var components = currentGo.GetComponents<Component>();
                    foreach (var component in components)
                    {
                        if (component /*!= null*/ && component is not Transform) // Can happen on missing scripts
                        {
                            DiscoverAndEnqueueIfApplicable(component, TraversalReason.IsComponent, current, needsEnqueue: false, generatorNullable: holder);
                            AppendAllAssets(component, discoveryOptions, current);
                        }
                    }
                    
                    foreach (Transform childTransform in currentGo.transform)
                    {
                        DiscoverAndEnqueueIfApplicable(childTransform.gameObject, TraversalReason.IsChildTransform, current);
                    }
                    
                    if ((discoveryOptions.IncludePrefabSource || discoveryOptions.IncludePrefabInstanceGhostReferences))
                    {
                        var prefabCandidate = currentGo;
                        var keepSearching = true;
                        // Keep looking for prefabs. This can find Prefab Instance -> Prefab -> FBX chains.
                        while (keepSearching && PrefabUtility.IsAnyPrefabInstanceRoot(prefabCandidate))
                        {
                            if (discoveryOptions.IncludePrefabSource)
                            {
                                // Source prefabs need to be included in a .unitypackage to be usable, so the following code takes care of that.
                                // Typically, a built avatar does not need to traverse assets like this because prefabs aren't part of the built avatar.
                                var objectFromSource = PrefabUtility.GetCorrespondingObjectFromSource(prefabCandidate);
                                if (objectFromSource /* != null*/)
                                {
                                    // When discovering a prefab source, we do NOT enqueue it, as we don't want to traverse the source's assets unless necessary.
                                    
                                    // Order matters
                                    DiscoverAndEnqueueIfApplicable(objectFromSource, TraversalReason.IsPrefabSource, prefabCandidate, needsEnqueue: discoveryOptions.IncludeReferencesContainedWithinPrefabSource);
                                    prefabCandidate = objectFromSource;
                                    keepSearching = true;
                                }
                                else
                                {
                                    keepSearching = false;
                                }
                            }
                            else
                            {
                                keepSearching = false;
                            }

                            if (discoveryOptions.IncludePrefabInstanceGhostReferences)
                            {
                                // We need to check for hidden references using the code below. Object discovery through the use of SerializedObject->SerializedProperty
                                // cannot discover object references contained in prefab instance overrides on Components or GameObjects that have
                                // been removed from the prefab instance.
                                // To be clear, this refers to objects within prefab instances that have been modified and then removed, leaving ghost references behind that
                                // become visible again when the user un-removes a prefab instance override.
                                //
                                // This does NOT refer to prefab instances that have override a property or that remove a GameObject hiding a reference from a source prefab;
                                // those are still discovered normally through recursion without the below code.
                            
                                var propMod = PrefabUtility.GetPropertyModifications(prefabCandidate);
                                foreach (var propertyModification in propMod)
                                {
                                    var reference = propertyModification.objectReference;
                                    if (reference /* != null*/)
                                    {
                                        DiscoverAndEnqueueIfApplicable(reference, TraversalReason.IsPropertyModificationOfPrefabInstance, current);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    AppendAllAssets(current, discoveryOptions, current);
                }

                if (discoveryOptions.IncludeDriveByAssets)
                {
                    if (EditorUtility.IsPersistent(current))
                    {
                        var assetPath = AssetDatabase.GetAssetPath(current);
                        // We skip drive-by of AnimatorController assets because they have too many assets in them.
                        if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) != typeof(AnimatorController))
                        {
                            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                            foreach (var subAsset in subAssets)
                            {
                                if (subAsset != current)
                                {
                                    DiscoverAndEnqueueIfApplicable(subAsset, TraversalReason.IsDriveByAsset, current);
                                }
                            }
                        }
                    }
                }
            }
            
            _results.UnionWith(_reasonsForDiscovery.Keys);

            return new FindAllAssetsResult
            {
                FoundAssets = _results,
                ReasonsForDiscovery = _reasonsForDiscovery
            };
        }

        private void AppendAllAssets(Object assetOrComponent, DiscoveryOptions discoveryOptions, Object generator)
        {
            if (assetOrComponent is Transform or GameObject)
            {
                throw new InvalidOperationException("Transform and GameObject are not supposed to be passed to this function.");
            }

            // Animator states are too slow, we're skipping all of this here. There's special treatment of animator controllers in UnpackStateMachine.
            if (assetOrComponent is AnimatorTransitionBase) return;
            if (assetOrComponent is AnimatorState) return;
            if (assetOrComponent is AnimatorStateMachine) return;
            
            if (IsLeaf(assetOrComponent)) return;

            var typeName = assetOrComponent.GetType().Name;

            Profiler.BeginSample($"TransferAssistant.AppendAllAssets.{typeName}");

            if (assetOrComponent is Material material)
            {
                if (material.shader /*!= null*/) DiscoverAndEnqueueIfApplicable(material.shader, TraversalReason.IsObjectReference, assetOrComponent, generator);
                
                material.GetTexturePropertyNames(_texturePropertyNames);
                foreach (var texturePropertyName in _texturePropertyNames)
                {
                    var texture = material.GetTexture(texturePropertyName);
                    if (texture /*!= null*/) DiscoverAndEnqueueIfApplicable(texture, TraversalReason.IsObjectReference, assetOrComponent, generator);
                }
            }
            else if (assetOrComponent is AnimatorController ctrl)
            {
                foreach (var layer in ctrl.layers)
                {
                    UnpackStateMachine(layer.stateMachine, assetOrComponent, generator);
                    if (layer.avatarMask /*!= null*/) DiscoverAndEnqueueIfApplicable(layer.avatarMask, TraversalReason.IsObjectReference, assetOrComponent, generator);
                }
            }
            else if (assetOrComponent is SkinnedMeshRenderer smr)
            {
                // This is really slow, don't bother
                // foreach (var smrBone in smr.bones)
                // {
                    // if (smrBone /*!= null*/) DiscoverAndEnqueueIfApplicable(Detransform(smrBone), TraversalReason.IsObjectReference, assetOrComponent, generator);
                // }
                if (smr.sharedMesh /*!= null*/) DiscoverAndEnqueueIfApplicable(smr.sharedMesh, TraversalReason.IsObjectReference, assetOrComponent, generator);
                foreach (var sharedMaterial in smr.sharedMaterials)
                {
                    if (sharedMaterial /*!= null*/) DiscoverAndEnqueueIfApplicable(sharedMaterial, TraversalReason.IsObjectReference, assetOrComponent, generator);
                }
                if (smr.probeAnchor /*!= null*/) DiscoverAndEnqueueIfApplicable(smr.probeAnchor.gameObject, TraversalReason.IsObjectReference, assetOrComponent, generator);
                if (smr.rootBone /*!= null*/) DiscoverAndEnqueueIfApplicable(smr.rootBone.gameObject, TraversalReason.IsObjectReference, assetOrComponent, generator);
                if (smr.lightProbeProxyVolumeOverride /*!= null*/) DiscoverAndEnqueueIfApplicable(smr.lightProbeProxyVolumeOverride.gameObject, TraversalReason.IsObjectReference, assetOrComponent, generator);
                
            }
            else if (assetOrComponent is AnimationClip clip)
            {
                var objectCurve = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                foreach (var binding in objectCurve)
                {
                    var objectReferenceCurve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (null != objectReferenceCurve) // reversed because we can't use short form
                    {
                        foreach (var keyframe in objectReferenceCurve)
                        {
                            if (keyframe.value /*!= null*/)
                            {
                                // Usually these contain Material, but they MIGHT contain other stuff (almost never).
                                DiscoverAndEnqueueIfApplicable(Detransform(keyframe.value), TraversalReason.IsObjectReference, assetOrComponent, generator);
                            }
                        }
                    }
                }
                
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                if (settings.additiveReferencePoseClip /*!= null*/) DiscoverAndEnqueueIfApplicable(settings.additiveReferencePoseClip, TraversalReason.IsObjectReference, assetOrComponent, generator);
            }
            else if (assetOrComponent is Shader shader)
            {
                if (discoveryOptions.IncludeDefaultTexturesInsideShader)
                {
                    if (EditorUtility.IsPersistent(shader)) // Just in case.
                    {
                        var assetPath = AssetDatabase.GetAssetPath(shader);
                        
                        // We're cheating. Using AssetDatabase.GetDependencies makes stuff way easier. This supposed to only return textures anyway.
                        foreach (var dependency in AssetDatabase.GetDependencies(assetPath))
                        {
                            if (AssetDatabase.LoadAssetAtPath<Object>(dependency) is Texture textureDependency)
                            {
                                DiscoverAndEnqueueIfApplicable(textureDependency, TraversalReason.IsObjectReference, assetOrComponent, generator);
                            }
                        }
                    }
                }
                else
                {
                    // Do not include default textures of shader. Using "new SerializedObject..." on Shader is somehow quite slow 
                }
            }
            else
            {
                var so = new SerializedObject(assetOrComponent);
                var sp = so.GetIterator();
        
                var skipRecursion = false;
                while (sp.Next(!skipRecursion))
                {
                    skipRecursion = false;
                    if (sp.name is "m_GameObject" or "m_CorrespondingSourceObject")
                    {
                        // Do nothing
                    }
                    else if (sp.isArray)
                    {
                        if (sp.arraySize == 0)
                        {
                            skipRecursion = true;
                        }
                        else if (sp.arrayElementType == "UnityEngine.Transform")
                        {
                            // We're skipping transform arrays. This can be considered a flaw, but it's very rarely something we want.
                            // This has a risk of skipping hacks like GameObject asset reference in the project, or prefab instantiations in some games like those that integrate Cilbox.
                            skipRecursion = true;
                        }
                        else
                        {
                            var propType = sp.GetArrayElementAtIndex(0).propertyType;
                            var mayContainObjectReferences = propType is SerializedPropertyType.Generic or SerializedPropertyType.ObjectReference;
                            if (!mayContainObjectReferences)
                            {
                                skipRecursion = true;
                            }
                        }
                    }
                    else
                    {
                        if (sp.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var value = sp.objectReferenceValue;
                            if (value /*!= null*/)
                            {
                                DiscoverAndEnqueueIfApplicable(Detransform(value), TraversalReason.IsObjectReference, assetOrComponent, generator);
                            }
                        }
                    }
                }
            }
        
            Profiler.EndSample();
        }

        private void UnpackStateMachine(AnimatorStateMachine stateMachine, Object assetOrComponent, Object generator)
        {
            foreach (var state in stateMachine.states)
            {
                if (state.state.motion /*!= null*/) DiscoverAndEnqueueIfApplicable(state.state.motion, TraversalReason.IsObjectReference, assetOrComponent, generator);
                foreach (var stateMachineBehaviour in state.state.behaviours)
                {
                    if (stateMachineBehaviour /*!= null*/) DiscoverAndEnqueueIfApplicable(stateMachineBehaviour, TraversalReason.IsObjectReference, assetOrComponent, generator);
                }
            }

            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                foreach (var stateMachineBehaviour in subStateMachine.stateMachine.behaviours)
                {
                    if (stateMachineBehaviour /*!= null*/) DiscoverAndEnqueueIfApplicable(stateMachineBehaviour, TraversalReason.IsObjectReference, assetOrComponent, generator);
                }
                
                UnpackStateMachine(subStateMachine.stateMachine, assetOrComponent, generator);
            }
        }

        private static Object Detransform(Object value)
        {
            return value is Transform valueTransform ? valueTransform.gameObject : value;
        }

        /// We don't want to introspect assets that are known not to reference anything else.
        private static bool IsLeaf(Object current)
        {
            if (current == null) return true;
            
            // Profiling (see BeginSample) shows that these take a long time to iterate
            if (current
                is Mesh
                or Texture2D
                or Avatar
            ) return true;
            
            var fullName = current.GetType().FullName;
            if (fullName.EndsWith(".VRCAnimatorTrackingControl")) return true;
            
            return false;
        }
    }
    
    internal enum TraversalReason
    {
        IsRoot,
        IsChildTransform,
        IsPrefabSource,
        IsPropertyModificationOfPrefabInstance,
        IsDriveByAsset,
        IsObjectReference,
        IsComponent
    }

    internal struct TraversalLog
    {
        public Object originatorNullableIfRoot;
        public Object discoveredObject;
        public TraversalReason reason;
        public Object generatorNullable;
    }

    internal struct FindAllAssetsResult
    {
        public HashSet<Object> FoundAssets;
        public new Dictionary<Object, List<TraversalLog>> ReasonsForDiscovery;
    }
}