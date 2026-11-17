"""Test helper run through BlenderRunner: builds a .blend for the import tests.

AE_TEST_KIND = "two_models" (imports AE_TEST_A and AE_TEST_B, two RMV2 root collections)
             | "cube"       (one default cube, no RMV2 root collection)
             | "renamed"    (imports AE_TEST_A and renames its root collection to AE_TEST_NAME)
Saves AE_TEST_OUT and writes result.json beside the job.
"""
import json
import os
import sys
from pathlib import Path

import bpy

job_path = Path(sys.argv[sys.argv.index("--") + 1])
kind = os.environ["AE_TEST_KIND"]
bpy.ops.wm.read_homefile(use_empty=True)
if kind == "cube":
    bpy.ops.mesh.primitive_cube_add()
else:
    bpy.ops.import_scene.rmv2(filepath=os.environ["AE_TEST_A"], import_all_lods=True, build_materials=False)
    if kind == "two_models":
        bpy.ops.import_scene.rmv2(filepath=os.environ["AE_TEST_B"], import_all_lods=True, build_materials=False)
    if kind == "renamed":
        root = next(c for c in bpy.data.collections if c.rmv2.is_rmv2_root)
        root.name = os.environ["AE_TEST_NAME"]
bpy.ops.wm.save_as_mainfile(filepath=os.environ["AE_TEST_OUT"])
(job_path.parent / "result.json").write_text(json.dumps({"ok": True, "files": [], "warnings": []}), encoding="utf-8")
