"""Build a NEW Eris scene from an existing hair-rig .blend, preserving that source.

blender --background INPUT.blend --python build_animated_character.py -- --output NEW.blend
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))
from apply_mixamo_animations import apply_mixamo_animations
from rig_support import apply_rig_support


def build(output):
    output = Path(output).resolve()
    if output.exists():
        raise FileExistsError(output)
    source = Path(bpy.data.filepath)
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    support_report = apply_rig_support(report_path=ROOT / "rig-verification.json")
    assert support_report["passed"], "Clothing rig support checks failed"
    manifest = json.loads((ROOT / "clips.json").read_text(encoding="utf-8"))
    for clip in manifest["clips"]:
        clip["path"] = str(ROOT / clip["path"])
    motion_report = apply_mixamo_animations(manifest["clips"], active=manifest["active"],
                                            report_path=ROOT / "motion-verification.json")
    assert motion_report["passed"]
    bpy.context.scene.render.fps = 30
    bpy.context.scene.render.fps_base = 1.0
    active_images = set()
    for name in ("Head", "Hair"):
        for slot in bpy.data.objects[name].material_slots:
            if slot.material and slot.material.use_nodes:
                active_images.update(node.image for node in slot.material.node_tree.nodes
                                     if node.type == "TEX_IMAGE" and node.image)
    assert active_images, "The visible character has no texture image"
    for image in active_images:
        if not image.packed_file:
            image.pack()
    for material in bpy.data.materials:
        material.use_fake_user = True
    armature = bpy.data.objects["Armature"]
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    # Save a viewport that opens on the complete character with the real texture.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "DOPESHEET_EDITOR" and area.spaces.active.mode == "TIMELINE":
                area.spaces.active.mode = "ACTION"
            if area.type == "CONSOLE":
                area.type = "VIEW_3D"
            if area.type == "VIEW_3D":
                space = next((space for space in area.spaces if space.type == "VIEW_3D"), None)
                if space is None:
                    continue
                space.shading.type = "SOLID"
                space.shading.light = "STUDIO"
                space.shading.color_type = "TEXTURE"
                space.overlay.show_relationship_lines = False
                space.overlay.show_extras = False
                region = space.region_3d
                region.view_perspective = "ORTHO"
                region.view_location = Vector((0, -.02, .53))
                region.view_distance = 1.9
                region.view_rotation = (Vector((1.2, -3, .5))).to_track_quat("Z", "Y")
    output.parent.mkdir(exist_ok=True, parents=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
    assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash
    report = {"source": source.name, "source_sha256": source_hash,
              "source_preserved": True, "output": output.name,
              "output_sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
              "rig_report": "rig-verification.json", "motion_report": "motion-verification.json",
              "actions": [item["action"] for item in motion_report["clips"]],
              "bones": len(armature.data.bones), "embedded_texture": True,
              "hair_secondary_motion_added": False, "unity_play_verified": False}
    (ROOT / "build-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("ERIS_ANIMATION_BUILD=" + json.dumps(report))
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    build(args.output)
