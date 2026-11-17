# Blender and OBJ import and export

AssetEditor converts `.rigid_model_v2` to and from `.blend` and `.obj` by running Blender in the background
with ChaosRobie's `io_scene_rmv2` extension
(<https://github.com/robert-d-schultz/rmv2_anim_blender_import_export>, GPL-3.0). AssetEditor calls the
add-on's operators by name and ships none of its code. glTF import and export stay on AssetEditor's own code.

## Where to find it

- **Export:** right-click a `.rigid_model_v2` in the Pack Explorer, then Export to > Blender (.blend),
  glTF (.glb) or OBJ (.obj, LOD0 only). Each one opens a save dialog and uses that exporter's defaults.
  The Export window (Export > Advanced Export) lists the same exporters with their settings.
- **Import:** File > Import 3D model imports into the root of the editable pack, and offers to create a
  pack when there is none. Right-click a folder or a pack root and pick Import 3D model or image, or drag
  a `.blend`, `.obj`, `.gltf` or `.glb` onto a folder or file in the Pack Explorer. A CA pack never
  accepts an import.

## What each route carries

| Route | LODs | Skeleton and weights | Textures | Pivot |
|---|---|---|---|---|
| Export to `.blend` | all | the skeleton `.anim` is attached when the model names one and it is in a loaded pack | written to `<name>_textures\` beside the `.blend` | kept |
| Export to `.obj` | LOD0 | none | written to `<name>_textures\`, referenced by the `.mtl` | not carried: the OBJ holds the file's raw vertex positions |
| Import `.blend` | every LOD collection, one file per RMV2 root collection | from the `.blend` | paths as set in the add-on's material panel | kept |
| Import `.obj` | one | none (a static model) | the add-on's default material | none |

A texture or skeleton missing from every loaded pack is a warning, and the export still finishes.

## Round trips

An exported `.blend` stores the model's pack path, skeleton and RMV2 version as custom properties on its
RMV2 root collection (`ae_pack_path`, `ae_skeleton`, `ae_version`). Importing that `.blend` asks whether
to write the model back to its original path. Yes writes it there, in the pack you imported into, and
replaces any file already at that path. No writes it into the folder you picked.

A `.blend` made in Blender from scratch, with no RMV2 root collection, exports its visible meshes as one
LOD and says so in a warning. To keep LODs, use the add-on's Setup RMV2 LOD Collections first.

## Measured round-trip behaviour

Karl Franz's body (4 LODs, `humanoid01`) through rmv2 -> `.blend` -> rmv2:

- The LOD count, mesh count, skeleton name and every distinct vertex position survive. So does every
  vertex's bone weights, within 0.02 per bone.
- Blender splits vertices at UV and normal seams, so LOD0 comes back with 9,287 vertices for 9,156. The
  geometry is the same.
- CA's LOD3 repeats 2 triangles (the same three indices twice). A Blender mesh cannot hold a face twice,
  so those 2 duplicates are dropped.

## Settings and errors

- Blender is found from Settings `BlenderPath`, then each Steam library's `steamapps\common\Blender`,
  then `Program Files\Blender Foundation\Blender *`. Among those, the one whose user config folder
  (`%APPDATA%\Blender Foundation\Blender\<version>`) holds `io_scene_rmv2` wins, because Steam can
  update Blender to a version whose config folder is empty. When none is found, AssetEditor asks for
  `blender.exe` once and saves the answer.
- A missing add-on gives "Install ChaosRobie's io_scene_rmv2 extension in Blender" with the link.
- A conversion that runs past 5 minutes is stopped, and Blender is killed with its child processes.
- While Blender runs, a small window says so and the rest of AssetEditor keeps drawing.
- When the Blender it found has no add-on, AssetEditor offers to pick a different `blender.exe` and tries once more.
- A `blender.exe` that cannot start, or a `result.json` that cannot be read, is reported as a failure, never a crash.
- When it finishes, a window lists each file written with its LOD count, then any warnings. Blender's own
  `Warning:` lines (an operator report, such as too many bone weights) are included.
- Each run works in `%TEMP%\AssetEditor\BlenderJobs\<name>-<id>`. A run that worked deletes that folder. A run
  that failed keeps it and names it in the error, because its `job.json` and inputs reproduce the failure.

## Code

- `Editors/ImportExportEditor/Editors.ImportExport/Blender/` holds `BlenderRunner`, which finds and runs
  Blender and reads `result.json`, `ae_convert.py`, the script Blender runs (copied to
  `Resources\Blender\` in the output), and `BusyWindow`.
- `Exporting/Exporters/RmvToBlender/RmvToBlenderExporter.cs` (`Export`, `ExportObj`) and
  `Importing/Importers/BlenderToRmv/BlenderImporter.cs`.
- `Exporting/ExportToCommand.cs` and `Importing/ImportDropHandler.cs` are the entry points. The drop
  routing is `PackFileBrowserViewModel.DropExternalFiles` in Shared.Ui, behind `IExternalFileDropHandler`.
- The tests are `Test.ImportExport/Blender/` and `Test.ImportExport/Importing/EntryPointTests.cs`, plus
  `Shared.UiTest/.../ExternalFileDropTests.cs`. The Blender tests ignore themselves on a machine without
  Blender.
