Transfer Assistant
=====

This Unity tool will assist you when you want to use the *.unitypackage* export function to **transfer assets from one project
to another project**, while stripping assets that you deem unnecessary.

![general.png](Documentation/general.png)

This tool will **deliberately ignore some asset references**, keeping only assets that you actively need.

- Instead of exporting every asset referenced by prefabs, it only exports assets that exist on the main avatar.
  If a prefab uses an asset that the main avatar does not, it is not included. This is the default behavior, but it can be changed.
- You may choose to ignore assets by type or by component, along with any asset referenced by them.
  - When you ignore materials, it will also ignore textures used by those materials.
  - When you ignore components such as *Modular Avatar Menu Item*, it will also ignore the icon textures referenced by those components.
- You may choose to ignore assets referenced by objects marked as EditorOnly.

If an unexpected asset is being included in the export, the *Transfer Assistant* user interface can help you locate which object or component depends on it.

![dependency_reason.png](Documentation/dependency_reason.png)

*Transfer Assistant* will never modify the contents of Scenes or Prefabs.

The intended use for this tool is to **export an avatar project file between different games**, e.g., from a Unity 2022 BIRP project to a standalone Unity 6.4 URP project.
In such a project, assets are scattered across multiple folders, and within those folders, there are assets that you want to exclude because they serve no purpose in the destination project,
such as animator controllers or animation files.

That said, this tool may still be used to transfer avatars or other asset types between projects designed for the same game.

### What this tool is NOT designed for

If you are a product creator, this tool is **not** designed for exporting *.unitypackage* files meant to be published and distributed as part of a release, as such assets typically need more rigorous discipline in the folder structure.
Some functionality of this tool may be used for introspection purposes, but it is **not recommended** to use this tool for the management of product releases.

## User manual

The documentation for this tool is being written, and it will be available [here in English](https://docs.hai-vr.dev/docs/products/transfer-assistant),
and [here in Japanese](https://docs.hai-vr.dev/docs/products/transfer-assistant/ja).

-----

-----

## The problem this tries to solve

When exporting a custom avatar using Unity's default *Export .unitypackage* function to transfer assets between projects, assets are often scattered across multiple folders as expected
from a custom avatar. However, the contents of the export often end up with extra assets that aren't particularly needed by the custom avatar because:
- You do not want accidental references left over unintentionally inside the active hierarchy:
  - Prefab instances may contain ghost references to assets that aren't being used.
  - Components may contain stray references to sub-assets of a model or a transform within a prefab, which would in turn pull all the assets of the source prefab.
- You do not want assets referenced by incompatible components:
  - Some components may be irrelevant in another project (e.g. *Modular Avatar Menu Item*) and contain references to assets such as icon Textures.
  - Some proprietary assets may be incompatible in another project (e.g. *Expression Menu*), and those assets may themselves contain references to other assets.
  - Some Unity assets may be irrelevant in another project (e.g. *Animator Controller*, *Animation Clip*).
- You have installed some common assets separately:
  - Shaders, scripts, and DLLs are often undesirable as they are usually installed separately by the user.
- You have replaced assets or removed objects from the active hierarchy:
  - The original version of a prefab may contain undesirable references to assets (such as Materials) that your prefab instance is not using.
    - For example, an undesirable Material may be shipped with the original prefab of a purchased avatar, but you have already replaced it with your own.
    - Or, you have removed a Component or a GameObject from a prefab which contained references to other assets.
  - In some cases, you may have deliberately used the *EditorOnly* tag to signify that you aren't interested in some objects, so Materials and Textures referenced by those *EditorOnly* objects might not be required.
    While we will still export assets from objects marked as *EditorOnly* by default, you have the choice to perform more aggressive culling by excluding assets referenced by such objects as well.
    - The GameObjects and Components will continue to exist in the hierarchy as this tool does not modify scenes or prefabs; only the assets will be excluded.
    - If a prefab instance is marked as EditorOnly, we will still export the corresponding prefab source to prevent errors during import.

This tool attempts to facilitate the transfer of an avatar between incompatible game projects by offering opportunities to cull superfluous assets while keeping the avatar in an editable state in the destination project.

*Transfer Assistant* will never modify the contents of Scenes or Prefabs.

### Before

![before.png](Documentation/before.png)

### After

![after.png](Documentation/after.png)