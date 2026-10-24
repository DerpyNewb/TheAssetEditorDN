![AssetEditor](https://user-images.githubusercontent.com/54080240/143955132-badd843e-b823-4a4d-8326-e64be4e2c877.png)

# The Asset Editor, DN fork

[![Latest release](https://img.shields.io/github/v/release/DerpyNewb/TheAssetEditorDN)](https://github.com/DerpyNewb/TheAssetEditorDN/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/DerpyNewb/TheAssetEditorDN/total)](https://github.com/DerpyNewb/TheAssetEditorDN/releases)

A fork of [donkeyProgramming's AssetEditor](https://github.com/donkeyProgramming/TheAssetEditor), the modding tool for Total War games with editors for 3D models, animations, audio, textures and more in one desktop app. It is built on upstream v0.78 and adds a VFX editor, a Unit Simulator, a particle preview much closer to the game's, better LOD generation and a long list of fixes.

Releases are numbered DN v1, v1.1, v1.2 and so on, and the title bar shows both numbers, as in "AssetEditor v0.78 - DN v1.3".

**[Latest Release](https://github.com/DerpyNewb/TheAssetEditorDN/releases/latest)** · **[Documentation](https://donkeyprogramming.github.io/TheAssetEditor/)** · **[Video Tutorial](https://www.youtube.com/watch?v=ZlmG2AVL5-g)** · **[Upstream](https://github.com/donkeyProgramming/TheAssetEditor)**

---

## What this fork adds

The headline items. Every change is listed in the [release notes](https://github.com/DerpyNewb/TheAssetEditorDN/releases).

### VFX Editor
In the Tools menu. Opens `vfx/*.xml` and edits it in place: quality tiers, curve keys, properties, modifiers, emitters and references, with undo and a live preview you can fly like a projectile to see its trail. Its guide and a tutorial are in [docs/Documentation/VfxEditor.html](docs/Documentation/VfxEditor.html), which the upstream documentation site does not have. GitHub shows that file as source, so open it from a clone.

### Unit Simulator
In the Tools menu. Plays a rider on its mount the way the game pairs them, edits both battle tables and saves them back into their animpack, and adds `battle_personalities` riders on the mount's attach bones, with TSV export for RPFM.

### Particle preview
Flipbooks, trails, decals, HDR glow at the game's bloom threshold, light particles lighting the ground and models, distortion and heat haze, rigid-model particles, terrain spawning, vector fields. A chain with no Ultra block draws nothing at Ultra, as in the game.

### SuperView and the Anm.Meta editor
Vanilla `.anm.meta` files re-save byte-identical. Tag descriptions, a New dialog, PROP_10 and above, EFFECT particles, hand poses and animated props drawn in place, and `.variantmeshdefinition` files open and play.

### LOD generation
LODs and "Reduce mesh by 10%" use [meshoptimizer](https://github.com/zeux/meshoptimizer). It lands close to the triangle count you ask for and keeps UV seams, open edges, skin weights and mirrored tangent frames. The old Fast Quadric decimator is still in the save dialog as "Fast Quadric (old)", with three of its bugs fixed.

### Packs, saving and undo
A backup of the `.pack` before every save, autosave, Restore backup and File > Create a new packfile. Redo in Kitbash and the KeyFrame editor, and undo and redo in the Skeleton, Audio, AnimPack and Unit Simulator editors.

### Upstream bugs fixed
The viewport capped at 37 to 50 FPS on 75 to 144 Hz displays, faction colours did not tint WH3 models, copied models lost their textures, and labels printed `{loc:...}` instead of their text. These and more are fixed.

---

## Editors

### Kitbasher (3D Editor)
Full-featured 3D mesh editor for creating composite models by combining parts from multiple meshes. Supports material editing, mesh operations (merge, split, duplicate), vertex/face/object selection modes, and real-time 3D preview with a MonoGame-based viewport.

### Visual Metadata Editor
Editing interface for animation metadata. Supports manipulation of animation tags, attributes, and timing properties that control how animations blend and trigger in-game.

### Audio Editor
Comprehensive audio project editor with support for Wwise-based audio exploration, project creation, waveform visualization, format conversion, and dialogue event management.

### Animation Re-Targeting
Retargets animations between different skeletons, allowing you to transfer animations from one character model to another. Provides bone mapping and re-rigging capabilities for cross-skeleton animation reuse.

### Skeleton Editor
Visual skeleton editor for creating, editing, and rigging skeletal structures. Supports bone manipulation, weight visualization, and reference mesh display for precise rigging workflows.

### Texture Editor
Texture preview and editing tool supporting all game texture formats. Provides metadata inspection and visual inspection of textures used by game models.

### Animation Fragment Editor
Manages animation packs (.animpack files) and campaign animation bin files. Includes a batch animation exporter for bulk format conversions across your mod.

### Import/Export Editor
Handles bidirectional import/export of game asset formats, supporting GLTF/FBX to RMV2 conversions for bringing external 3D assets into the game.

### Reports
Generates analysis and diagnostic reports including material reports, mesh info dumps, metadata JSON exports, deep file searches, and dialogue event information.

---

## Getting Started

1. Download the [latest release](https://github.com/DerpyNewb/TheAssetEditorDN/releases/latest)
2. Extract it and run `AssetEditor/AssetEditor.exe`. Windows x64; the .NET runtime is included.
3. Load your game's pack files and start editing

From DN v1.2 on, the update prompt checks this fork's releases. DN v1 and v1.1 still check upstream's, so download a newer DN release by hand.

For detailed guides, see the **[Documentation](https://donkeyprogramming.github.io/TheAssetEditor/)**.

## Building from Source

Prerequisites: .NET 10 SDK, Windows

```bash
git clone https://github.com/DerpyNewb/TheAssetEditorDN.git
cd TheAssetEditorDN
dotnet build AssetEditor.sln
```

`meshoptimizer.dll` is checked in prebuilt; `GameWorld/GameWorldCore/GameWorld.Core/Services/SceneSaving/Lod/MeshOptimizerIntegration/Native/README.md` has the commands to rebuild it from source.

## Contributing

Pull requests are welcome. The project uses a modular architecture with dependency injection; each editor is a self-contained project under the `Editors/` folder. A bug that upstream has too is best reported [upstream](https://github.com/donkeyProgramming/TheAssetEditor).

## License

See the repository for license details. meshoptimizer is MIT licensed, and its licence ships beside the DLL.
