"""Export one skinned, textured FBX per Eris action; never save the source scene.

blender --background Eris_Animated.blend --python export_animations.py -- --output-dir Exports
Each FBX contains the 74-bone rig, body, hair and exactly one baked take.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
from io_scene_fbx import parse_fbx


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def snapshot(armature):
    result = {"bones": {}, "meshes": {}}
    bpy.context.view_layer.update()
    for bone in armature.pose.bones:
        result["bones"][bone.name] = list(armature.matrix_world @ bone.head)
    graph = bpy.context.evaluated_depsgraph_get()
    for name in ("Head", "Hair"):
        obj = bpy.data.objects[name].evaluated_get(graph)
        mesh = obj.to_mesh()
        points = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
        result["meshes"][name] = {
            "vertex_count": len(points),
            "min": [min(point[axis] for point in points) for axis in range(3)],
            "max": [max(point[axis] for point in points) for axis in range(3)],
            "samples": {str(index): list(points[index]) for index in range(0, len(points), 97)},
        }
        obj.to_mesh_clear()
    return result


def export_animations(output_dir):
    output_dir = Path(output_dir).resolve()
    output_dir.mkdir(exist_ok=True, parents=True)
    armature = bpy.data.objects["Armature"]
    assert len(armature.data.bones) == 74
    assert len([bone for bone in armature.data.bones if bone.name.startswith("Hair_")]) == 22
    objects = [armature, bpy.data.objects["Head"], bpy.data.objects["Hair"]]
    previous_selected = list(bpy.context.selected_objects)
    previous_active = bpy.context.view_layer.objects.active
    previous = (armature.animation_data.action, armature.animation_data.action_slot,
                armature.animation_data.use_nla, bpy.context.scene.frame_start,
                bpy.context.scene.frame_end, bpy.context.scene.frame_current)
    report = {"source": Path(bpy.data.filepath).name, "source_sha256": sha(bpy.data.filepath),
              "fps": bpy.context.scene.render.fps / bpy.context.scene.render.fps_base,
              "leaf_bones": False, "embedded_textures": True, "clips": [], "unity_play_verified": False}
    try:
        bpy.ops.object.select_all(action="DESELECT")
        for obj in objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = armature
        armature.animation_data.use_nla = False
        for name in ("Idle", "Walk", "Run"):
            action = bpy.data.actions.get("Eris_" + name) or bpy.data.actions.get(name)
            if not action:
                raise ValueError("Missing motion action: " + name)
            armature.animation_data.action = action
            armature.animation_data.action_slot = action.slots[0]
            start, end = map(lambda value: int(round(value)), action.frame_range)
            bpy.context.scene.frame_start, bpy.context.scene.frame_end = start, end
            path = output_dir / ("Eris_" + name + ".fbx")
            if path.exists():
                raise FileExistsError(path)
            expected = {}
            for frame in sorted({start, start + (end - start) // 4, (start + end) // 2,
                                 start + 3 * (end - start) // 4, end}):
                bpy.context.scene.frame_set(frame)
                expected[str(frame)] = snapshot(armature)
            bpy.context.scene.frame_set(start)
            bpy.ops.export_scene.fbx(filepath=str(path), check_existing=False,
                use_selection=True, object_types={"ARMATURE", "MESH"},
                global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE",
                use_space_transform=True, bake_space_transform=False,
                axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True,
                mesh_smooth_type="FACE", use_armature_deform_only=False,
                add_leaf_bones=False, bake_anim=True, bake_anim_use_all_bones=True,
                bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
                bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                bake_anim_simplify_factor=0.0, path_mode="COPY", embed_textures=True,
                use_custom_props=True)
            root, _ = parse_fbx.parse(str(path))
            section = next(node for node in root.elems if node.id == b"Objects")
            payloads = [child.props[0] for node in section.elems if node.id == b"Video"
                        for child in node.elems if child.id == b"Content" and child.props]
            assert payloads and all(payloads), "FBX has no embedded image"
            report["clips"].append({"name": name, "action": action.name, "path": path.name,
                "frames": [start, end], "bytes": path.stat().st_size, "sha256": sha(path),
                "embedded_image_sha256": [hashlib.sha256(payload).hexdigest() for payload in payloads],
                "expected_samples": expected})
    finally:
        action, slot, nla, start, end, frame = previous
        armature.animation_data.action = action
        armature.animation_data.action_slot = slot
        armature.animation_data.use_nla = nla
        bpy.context.scene.frame_start, bpy.context.scene.frame_end = start, end
        bpy.context.scene.frame_set(frame)
        bpy.ops.object.select_all(action="DESELECT")
        for obj in previous_selected:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = previous_active
    assert sha(bpy.data.filepath) == report["source_sha256"]
    (output_dir / "export-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("EXPORTED_MOTIONS=" + json.dumps({"clips": [clip["name"] for clip in report["clips"]]}))
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    export_animations(args.output_dir)
