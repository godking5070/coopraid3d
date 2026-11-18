"""Read-only validation of the bright Eris motion revision after fresh reopen.

The incoming rig is already repaired. This revision permits no mesh, skin or
rest-bone edits. Old locomotion Actions survive under the *_Calm names.
Expected motion lengths come from the actual import report, not fixed constants.
"""
import argparse
import ast
import hashlib
import json
import math
from pathlib import Path
import re
import sys

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from validate_animated_character import capture, file_sha


def image_hashes():
    result = {}
    for name in ("Head", "Hair"):
        for slot in bpy.data.objects[name].material_slots:
            if not slot.material or not slot.material.node_tree:
                continue
            for node in slot.material.node_tree.nodes:
                if node.type != "TEX_IMAGE" or not node.image:
                    continue
                image = node.image
                if image.packed_file:
                    result[image.name] = hashlib.sha256(image.packed_file.data).hexdigest()
                else:
                    path = Path(bpy.path.abspath(image.filepath))
                    result[image.name] = file_sha(path) if path.is_file() else None
    return result


def scene_state():
    result = capture()
    result["mesh_transforms"] = {obj.name: [list(row) for row in obj.matrix_world]
                                 for obj in bpy.context.scene.objects if obj.type == "MESH"}
    result["images"] = image_hashes()
    return result


def evaluated_world(obj):
    owner = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = owner.to_mesh()
    try:
        points = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
        mesh.vertices.foreach_get("co", points)
        matrix = np.asarray(owner.matrix_world, dtype=np.float64)
        return points.reshape((-1, 3)) @ matrix[:3, :3].T + matrix[:3, 3]
    finally:
        owner.to_mesh_clear()


def all_frame_check(action, frames):
    armature = bpy.data.objects["Armature"]
    armature.animation_data.action = action
    armature.animation_data.action_slot = action.slots[0]
    armature.animation_data.use_nla = False
    first, last = frames
    bpy.context.scene.frame_set(first)
    bpy.context.view_layer.update()
    head_at_first = armature.matrix_world @ armature.pose.bones["mixamorig:Head"].matrix
    hair_at_first = evaluated_world(bpy.data.objects["Hair"])
    first_hips = None
    max_hair_error = 0.0
    finite = True
    max_extent = 0.0
    for frame in range(first, last + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        finite &= all(math.isfinite(value) for bone in armature.pose.bones
                      for row in bone.matrix for value in row)
        for name in ("Head", "Hair"):
            points = evaluated_world(bpy.data.objects[name])
            finite &= bool(np.isfinite(points).all())
            max_extent = max(max_extent, float(np.ptp(points, axis=0).max()))
            if name == "Hair":
                head = armature.matrix_world @ armature.pose.bones["mixamorig:Head"].matrix
                delta = np.asarray(head @ head_at_first.inverted(), dtype=np.float64)
                expected = hair_at_first @ delta[:3, :3].T + delta[:3, 3]
                max_hair_error = max(max_hair_error, float(np.abs(points - expected).max()))
        hips = armature.matrix_world @ armature.pose.bones["mixamorig:Hips"].head
        if first_hips is None:
            first_hips = np.asarray(hips, dtype=np.float64)
    horizontal_loop_error = float(np.abs(np.asarray(hips)[:2] - first_hips[:2]).max())
    return {"frames_checked": last - first + 1, "all_bones_and_meshes_finite": bool(finite),
            "maximum_mesh_extent_world": max_extent, "hair_head_rigid_max_error_world": max_hair_error,
            "hips_xy_endpoint_error_world": horizontal_loop_error,
            "passed": bool(finite and max_extent < 5 and max_hair_error < 1e-5 and horizontal_loop_error < 1e-5)}


def validate(source_path, final_path, motion_report_path, report_path):
    source_path, final_path, motion_report_path, report_path = map(
        lambda value: Path(value).resolve(), (source_path, final_path, motion_report_path, report_path))
    source_hash, final_hash = file_sha(source_path), file_sha(final_path)
    motion_report = json.loads(motion_report_path.read_text(encoding="utf-8"))
    bpy.ops.wm.open_mainfile(filepath=str(source_path), load_ui=False, use_scripts=False)
    source = scene_state()
    bpy.ops.wm.open_mainfile(filepath=str(final_path), load_ui=False, use_scripts=False)
    final = scene_state()
    armature = bpy.data.objects["Armature"]
    initial_action = armature.animation_data.action
    initial_slot = armature.animation_data.action_slot
    initial_frame = bpy.context.scene.frame_current
    initial_nla = armature.animation_data.use_nla
    checks = []

    def check(name, passed, **details):
        checks.append({"name": name, "passed": bool(passed), **details})

    check("All74 rest bones and hierarchy are exact", source["bones"] == final["bones"] and len(final["bones"]) == 74)
    check("Armature and all mesh object transforms are exact", source["object_matrix"] == final["object_matrix"]
          and source["mesh_transforms"] == final["mesh_transforms"])
    check("All meshes retain exact geometry UV materials modifiers and every weight", source["meshes"] == final["meshes"])
    check("Existing hair pose bases are exact", source["hair_pose"] == final["hair_pose"])
    check("All source material graphs survive unchanged", all(final["materials"].get(name) == value
                                                             for name, value in source["materials"].items()))
    check("Active image bytes remain exact and packed", bool(source["images"]) and source["images"] == final["images"]
          and all(final["images"].values()) and all(image.packed_file for obj in (bpy.data.objects["Head"], bpy.data.objects["Hair"])
              for slot in obj.material_slots if slot.material and slot.material.node_tree
              for node in slot.material.node_tree.nodes if node.type == "TEX_IMAGE" and (image := node.image)))
    renamed = {"Eris_" + name: "Eris_" + name + "_Calm" for name in ("Idle", "Walk", "Run")}
    old_actions = {}
    for name, previous in source["actions"].items():
        retained_name = renamed.get(name, name)
        retained = final["actions"].get(retained_name)
        old_actions[name] = {"retained_name": retained_name, "passed": bool(retained and retained["hash"] == previous["hash"]
            and retained["range"] == previous["range"] and retained["slots"] == previous["slots"])}
    check("Every original Action retains exact curves slots and range", all(item["passed"] for item in old_actions.values()), actions=old_actions)
    expected_names = {"Eris_" + name for name in ("Idle", "Walk", "Run")}
    reports = {item["action"]: item for item in motion_report["clips"]}
    check("Actual import report contains exactly three requested motions", set(reports) == expected_names and motion_report.get("passed"))
    expected_bones = {name for name in final["bones"] if name.startswith("mixamorig:")}
    motion_results = []
    try:
        for name in sorted(expected_names):
            item, action_info = reports.get(name), final["actions"].get(name)
            if not item or not action_info:
                check(name + " exists", False)
                continue
            frames = [1, int(item["frames"])]
            finite = True
            loop_error = 0.0
            no_stretch = True
            complete_bones = set()
            path_valid = True
            curves = action_info["curves"]
            for _, path, _, keys, modifiers in curves:
                match = re.fullmatch(r"pose\.bones\[(.+)\]\.(location|rotation_quaternion|scale)", path)
                if not match:
                    path_valid = False
                    continue
                bone_name, prop = ast.literal_eval(match.group(1)), match.group(2)
                complete_bones.add(bone_name)
                values = [key[0][1] for key in keys]
                finite &= bool(values) and all(math.isfinite(value) for key in keys for point in key[:3] for value in point)
                if not values:
                    continue
                loop_error = max(loop_error, abs(values[0] - values[-1]))
                if prop == "location" and bone_name != "mixamorig:Hips":
                    no_stretch &= max(abs(value) for value in values) < 1e-6
                if prop == "scale":
                    no_stretch &= max(abs(value - 1) for value in values) < 1e-6
                path_valid &= "CYCLES" in modifiers
            source_fbx = motion_report_path.parent / "Source" / item["source"]
            check(name + " retains its actual downloaded FBX hash", source_fbx.is_file()
                  and file_sha(source_fbx) == item["source_sha256"], source=source_fbx.name)
            check(name + " has complete finite52bone looping channels without joint stretch or Hair keys",
                  action_info["range"] == frames and len(curves) == 520 and complete_bones == expected_bones
                  and len(expected_bones) == 52 and finite and no_stretch and path_valid and loop_error < 1e-6,
                  frames=frames, curves=len(curves), keyed_bones=len(complete_bones), loop_channel_error=loop_error,
                  no_joint_stretch=bool(no_stretch), paths_and_cycles_valid=bool(path_valid))
            result = all_frame_check(bpy.data.actions[name], frames)
            motion_results.append({"action": name, **result})
            check(name + " all actual frames retain finite deformation and hair/head follow", result["passed"], **{key: value for key, value in result.items() if key != "passed"})
    finally:
        armature.animation_data.action = initial_action
        armature.animation_data.action_slot = initial_slot
        armature.animation_data.use_nla = initial_nla
        bpy.context.scene.frame_set(initial_frame)
    check("File opens with Idle at30FPS", initial_action and initial_action.name == "Eris_Idle"
          and bpy.context.scene.render.fps == 30 and bpy.context.scene.render.fps_base == 1)
    check("No retained NLA track overrides the chosen motion", not initial_nla
          or all(track.mute or not track.strips for track in armature.animation_data.nla_tracks))
    check("Source and final files remain byte-identical during read-only validation", file_sha(source_path) == source_hash and file_sha(final_path) == final_hash)
    result = {"source": source_path.name, "source_sha256": source_hash, "final": final_path.name,
              "final_sha256": final_hash, "checks": checks, "motions": motion_results,
              "frames_checked": sum(item["frames_checked"] for item in motion_results),
              "passed": all(item["passed"] for item in checks), "unity_play_verified": False}
    report_path.parent.mkdir(exist_ok=True, parents=True)
    report_path.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8", newline="\n")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True)
    parser.add_argument("--final", required=True)
    parser.add_argument("--motion-report", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    result = validate(args.source, args.final, args.motion_report, args.report)
    print("BRIGHT_CHARACTER_QA=" + json.dumps({"passed": result["passed"], "checks": len(result["checks"]),
          "frames_checked": result["frames_checked"], "failed": [item["name"] for item in result["checks"] if not item["passed"]]}))
    if not result["passed"]:
        raise SystemExit(1)
