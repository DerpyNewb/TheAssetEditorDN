"""AssetEditor's Blender converter. Run by BlenderRunner:

    blender --background --python ae_convert.py -- <job.json>

Drives ChaosRobie's io_scene_rmv2 add-on (GPL-3.0,
https://github.com/robert-d-schultz/rmv2_anim_blender_import_export) by operator name only.
Always writes <job folder>/result.json: {ok, error, files: [{path, name, lods, packPath}], warnings}.
"""
import json
import sys
import traceback
from pathlib import Path

import bpy

MISSING_ADDON = ("Install ChaosRobie's io_scene_rmv2 extension in Blender: "
                 "https://github.com/robert-d-schultz/rmv2_anim_blender_import_export")

job_path = Path(sys.argv[sys.argv.index("--") + 1])
result = {"ok": False, "error": None, "files": [], "warnings": []}


def write_result():
    (job_path.parent / "result.json").write_text(json.dumps(result, indent=1), encoding="utf-8")


def addon_present():
    # hasattr(bpy.ops.import_scene, "rmv2") is always True; the registered class is the real test
    return hasattr(bpy.types, "IMPORT_SCENE_OT_rmv2") and hasattr(bpy.types, "EXPORT_SCENE_OT_rmv2")


def rmv2_roots():
    return [c for c in bpy.data.collections if c.rmv2.is_rmv2_root]


def lod_count(root):
    with_meshes = [c for c in root.children if any(o.type == "MESH" for o in c.objects)]
    return max(1, len(with_meshes))


def bound_bones():
    """Bones of the armature the meshes deform with (0 for a static model)."""
    for o in bpy.context.scene.objects:
        if o.type == "MESH":
            for m in o.modifiers:
                if m.type == "ARMATURE" and m.object is not None:
                    return len(m.object.data.bones)
    return 0


def import_rmv2(job, with_skeleton=True):
    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.ops.import_scene.rmv2(filepath=job["input"], import_all_lods=True, build_materials=True,
                              texture_root=job.get("textureRoot") or "", attach_armature=False)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if with_skeleton and job.get("skeletonAnim"):
        for o in bpy.context.scene.objects:
            o.select_set(o in meshes)
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.import_scene.tw_anim(filepath=job["skeletonAnim"], attach_meshes=True, import_animation=False)
    roots = rmv2_roots()
    if len(roots) != 1:
        raise RuntimeError("expected one RMV2 root collection after import, found %d" % len(roots))
    root = roots[0]
    root["ae_pack_path"] = job.get("packPath") or ""
    root["ae_skeleton"] = job.get("skeletonName") or ""
    root["ae_version"] = int(job.get("version") or 7)
    return root


def rmv2_to_blend(job):
    root = import_rmv2(job)
    text = bpy.data.texts.new("WH3_README")
    text.write("Exported by AssetEditor from %s\n"
               "Edit, save, then in AssetEditor use Import 3D model on this .blend.\n"
               "Every LOD collection under '%s' is written back.\n" % (job.get("packPath") or job["input"], root.name))
    bpy.ops.wm.save_as_mainfile(filepath=job["output"])
    result["files"].append({"path": job["output"], "name": Path(job["output"]).name,
                            "lods": lod_count(root), "packPath": job.get("packPath"), "bones": bound_bones()})


MODES = {"rmv2_to_blend": rmv2_to_blend}

try:
    job = json.loads(job_path.read_text(encoding="utf-8"))
    if not addon_present():
        result["error"] = MISSING_ADDON
    elif job["mode"] not in MODES:
        result["error"] = "unknown mode %r" % job["mode"]
    else:
        MODES[job["mode"]](job)
        result["ok"] = True
except Exception as exc:  # reported to AssetEditor, never swallowed
    traceback.print_exc()
    result["error"] = "%s: %s" % (type(exc).__name__, exc)
finally:
    write_result()
