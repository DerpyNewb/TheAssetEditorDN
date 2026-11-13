# AssetEditor IPC (Current Support)

This document describes the current IPC endpoint implemented by `AssetEditor`.

## Status
- Transport: Windows named pipe
- Pipe name: `TheAssetEditor.Ipc`
- Protocol: JSON line per request (`UTF-8`, newline-terminated)
- Supported actions: `open`, `find`, `load`, `ping`, and the editor actions `editors`, `scene`, `select`, `visible`,
  `transform`, `command`, `camera` (see Editor actions)

## Pipe Path (Windows)
- `\\.\pipe\TheAssetEditor.Ipc`

## Multiple AssetEditor Instances
- IPC starts automatically; the `Start_IPC` command-line argument is no longer required
- Every AssetEditor instance participates in ownership of the fixed pipe
- A per-session named mutex (`Local\TheAssetEditor.Ipc.Owner`) ensures only one instance hosts the pipe at a time
- Other instances wait without repeatedly trying to create the pipe
- When the owner exits, one waiting instance takes ownership and starts serving IPC requests

## Request Format
Send one JSON object followed by a newline.

### Supported action: `open`
```json
{"action":"open","path":"variantmeshes/wh_variantmodels/.../file.rigid_model_v2"}
```

### Other actions
```json
{"action":"ping"}
{"action":"load","packPathOnDisk":"k:/SteamLibrary/steamapps/common/Total War WARHAMMER III/data/ovn_araby.pack"}
{"action":"find","path":"arb_base_elephant","limit":50}
```
- `ping`: answers `{"ok":true}` when an AssetEditor is serving the pipe, and touches nothing
- `load`: loads `packPathOnDisk` as `open` would, and opens no file; fails when `packPathOnDisk` is missing
- `find`: every pack-internal path in the loaded packs that contains `path`, ignoring case, in path order, as
  `{"ok":true,"files":[...],"truncated":false}`. At most `limit` (default 200); `truncated` is `true` when there were more.
  Forward slashes are accepted. A `packPathOnDisk` is loaded first, so a mod pack's files can be found before they are opened

### Request fields
- `action` (required): `"open"`, `"find"`, `"load"`, `"ping"`, or an editor action (fields under Editor actions)
- `path` (required for `open` and `find`): pack-internal file path to open, or the text to find
- `limit` (optional, `find` only, default `200`): the most paths to return
- `bringToFront` (optional, default `true`): bring AssetEditor window to front
- `packPathOnDisk` (optional): disk path to a `.pack` to load first if needed
- `openInExistingKitbashTab` (optional, default `false`): if `true`, and a Kitbash tab exists, import supported files into that tab instead of opening a new tab

## Open Behavior by File Type
- `.rigid_model_v2`: normal open flow (or import into existing Kitbash tab if `openInExistingKitbashTab=true`)
- `.wsmodel`: forced to open in Kitbash Editor
- `.variantmeshdefinition`: forced to open in Kitbash Editor and imported as a reference on open

## Path Handling
- Forward slashes and backslashes are accepted
- Repeated backslashes are collapsed
- Absolute paths are accepted if they contain a known pack root such as `variantmeshes\`; AssetEditor extracts the pack-relative suffix

## Pack Loading Behavior
- If `packPathOnDisk` is supplied, AssetEditor attempts to ensure that pack is available before opening `path`
- If the pack is already loaded as a standalone pack, it is not loaded again
- If the pack is already represented inside the merged `All Game Packs` container, it is not loaded again

## Editor actions
These reach into an open editor tab. `editor` (optional) is a tab index from `editors`; without it, the current tab. All
but `editors` and `command` work in Kitbash and in every editor built on `EditorHostBase` (SuperView, Unit Simulator,
Skeleton and the other 3D editors); any other tab answers `"... has no 3D scene"`. Every change goes through the
editor's own commands, so its Undo takes it back. Replies carry their result in `data`.

```json
{"action":"editors"}
{"action":"scene"}
{"action":"select","nodes":["Root/Editable Model/Lod 0/emp_karl_franz_body_01"]}
{"action":"visible","nodes":["3f2c..."],"visible":false}
{"action":"transform","translate":[0,0.1,0],"rotate":[0,90,0],"scale":[2,2,2],"pivot":"origin"}
{"action":"command","name":"UndoCommand"}
{"action":"camera","yaw":45,"pitch":20,"zoom":6,"lookAt":[0,1,0]}
```
- `editors`: `[{index, title, type, current}]`
- `scene`: every node, depth first: `[{id, path, depth, type, visible, editable, selected, vertices, faces}]`; a node
  with no mesh has no `vertices` or `faces`
- `select`: `nodes` takes ids, paths from `Root`, or names. A name used by more than one node (every LOD repeats its
  meshes' names) is refused with the ids it matches. `add: true` adds to the selection; no `nodes` clears it. Meshes only.
  A mesh the viewport will not pick is refused too: every reference mesh, and a part locked in the scene tree
- `visible`: shows or hides `nodes`, as one undo step that gives each node back what it had. (The scene tree's check box
  is still not an undo step)
- `transform`: moves the current selection, as the Kitbash transform box does: scale, then rotate (degrees, X then Y
  then Z), then translate, each its own undo step. `pivot` is `"object"` (default) or `"origin"`. A scale with a 0 in it
  is refused, since its undo cannot invert it, and so is a selection holding a part locked after it was selected
- `command`: runs a Kitbash toolbar or menu command by its class name (case ignored) as its button runs it: `SaveCommand`,
  `UndoCommand`, `RedoCommand`, `DeleteObjectCommand`, `DuplicateObjectCommand`, `FocusCameraCommand` and the rest. No
  `name` lists them. Kitbash only
- `camera`: sets whichever of `yaw`, `pitch` (degrees), `zoom` and `lookAt` are given, and returns all four

The work runs on the UI thread. A command that opens a modal dialog holds that thread until the dialog closes, so after
5 seconds the reply is `{"ok":true,"pending":true}` and the work carries on. The dialog's own message loop keeps serving
the pipe, so the next request is answered while it is open.

## Response Format
AssetEditor returns one JSON response line and closes the connection.

### Success
```json
{"ok":true}
```

### Failure
```json
{"ok":false,"error":"File not found","normalizedPath":"variantmeshes\\..."}
```

## Examples
Open from already-loaded packs:
```json
{"action":"open","path":"variantmeshes/wh_variantmodels/bi1/cth/cth_great_moon_bird/cth_great_moon_bird_body_01.rigid_model_v2"}
```

Open from a mod pack on disk (auto-load if needed):
```json
{"action":"open","path":"variantmeshes/wh_variantmodels/el1/arb/arb_new_elephants/arb_base_elephant/arb_base_elephant.rigid_model_v2","packPathOnDisk":"k:/SteamLibrary/steamapps/common/Total War WARHAMMER III/data/ovn_araby.pack"}
```

Reuse an existing Kitbash tab:
```json
{"action":"open","path":"variantmeshes/wh_variantmodels/el1/arb/ane/abe/arb_base_elephant_1.wsmodel","packPathOnDisk":"k:/SteamLibrary/steamapps/common/Total War WARHAMMER III/data/ovn_araby.pack","openInExistingKitbashTab":true}
```
