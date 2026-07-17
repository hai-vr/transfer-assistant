Transfer Assistant
=====

This Unity tool will assist you when you want to use the *.unitypackage* export function to **transfer assets from one project
to another project**, while stripping assets that you deem unnecessary.

![general.png](Documentation/general.png)

This tool will **deliberately ignore some asset references**, keeping only assets that you actively need.

- Instead of exporting every asset referenced by prefabs, it only exports assets that exist on the main avatar.
  If a prefab uses an asset that the main avatar does not, it is not included. This is the default behavior, but it can be changed.
- You may choose to ignore assets by type or by component, along with any asset referenced by them. Examples:
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

# User manual

The documentation for this tool is available in the following languages:

- [English](https://docs.hai-vr.dev/docs/products/transfer-assistant)
- [Français](https://docs.hai-vr.dev/fr/docs/products/transfer-assistant)
- [日本語](https://docs.hai-vr.dev/ja/docs/products/transfer-assistant)
- [한국어](https://docs.hai-vr.dev/ko/docs/products/transfer-assistant)
- [简体中文](https://docs.hai-vr.dev/zh-Hans/docs/products/transfer-assistant)
- [繁體中文](https://docs.hai-vr.dev/zh-Hant/docs/products/transfer-assistant)
