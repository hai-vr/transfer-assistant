TestCase
=====

Items in **bold** denote assets.

**VariantTestCube** and **SceneHasPrefabOverride** are the entry points of the test.

- **SceneHasPrefabOverride** contains **OfficialTestCube** with overrides. ----- The contents are similar to **VariantTestCube**, but it is not an instance of it.
- **VariantTestCube** is a variant prefab of **OfficialTestCube** with overrides.
  - TestCube
    - SkinnedMeshRenderer
      - **CustomMaterial** (Override)
        - **CustomTexture**
  - TestSphere
    - **SkinnedMeshRenderer** (Removed)
      - **GhostMaterial** (Override) ----- This is an overriden Material slot inside a Removed component, which is the confusing part being tested. The Prefab override inspector is wrong and won't show GhostMaterial until it is un-Removed.
        - **GhostTexture**
  - EnabledPhysicsEditorOnly (Added) is EditorOnly
    - CapsuleCollider
      - **EditorOnlyPhysics**
  - DisabledCameraEditorOnly (Added) is EditorOnly and Disabled GameObject
    - Camera
      - **EditorOnlyRenderTexture**
- **OfficialTestCube** is a prefab derived from the **TestCube** model prefab.
  - Animator
    - **AnimatorController**
      - **AnimationClipHasMaterial**
        - **AnimationClipMaterial**
      - **AnimationClipHasReferencePose** ----- Use ReferencePose is a hidden field inside an AnimationClip, which still has its niche uses. Use the Debug inspector to access it.
        - **ReferencePoseAnimationClip**
      - CustomStateMachineBehaviour
        - **AudioClipInsideCustomStateMachineBehaviour**
  - TestCube
    - SkinnedMeshRenderer
      - **OfficialMaterial**
        - **OfficialTexture**
  - TestSphere
    - SkinnedMeshRenderer
      - **OfficialMaterial**
        - **OfficialTexture**
