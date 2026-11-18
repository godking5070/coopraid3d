"""Independently round-trip exported Eris FBXs in fresh Blender scenes.

Use export-verification.json written by export_animations.py as expected data.
FBXs are copied to unique staging directories before import: embedded texture
extraction never writes beside the production exports or touches the .blend.
This validates Blender FBX interchange, not an actual Unity import/playtest.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import shutil
import sys

import bpy
import numpy as np
from io_scene_fbx import parse_fbx


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def check_record(name, passed, **details):
    return {'name': name, 'passed': bool(passed), **details}


def compare_mesh(obj, expected):
    owner = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = owner.to_mesh()
    try:
        coordinates = np.empty(len(mesh.vertices) * 3, dtype=float)
        mesh.vertices.foreach_get('co', coordinates)
        coordinates = coordinates.reshape((-1, 3))
        matrix = np.array(owner.matrix_world)
        coordinates = coordinates @ matrix[:3, :3].T + matrix[:3, 3]
        count_matches = len(coordinates) == expected['vertex_count']
        finite = bool(np.isfinite(coordinates).all())
        bounds_error = max(float(np.abs(coordinates.min(axis=0) - expected['min']).max()),
                           float(np.abs(coordinates.max(axis=0) - expected['max']).max()))
        sample_error = max((float(np.abs(coordinates[int(index)] - point).max())
                            for index, point in expected['samples'].items() if int(index) < len(coordinates)), default=math.inf)
        return {'count_matches': count_matches, 'vertices': len(coordinates), 'finite': finite,
                'max_bounds_error': bounds_error, 'max_vertex_sample_error': sample_error,
                'passed': count_matches and finite and bounds_error < 0.0002 and sample_error < 0.0002}
    finally:
        owner.to_mesh_clear()


def validate_exports(manifest_path, report_path, staging_dir=None):
    manifest_path, report_path = Path(manifest_path).resolve(), Path(report_path).resolve()
    manifest = json.loads(manifest_path.read_text(encoding='utf8'))
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%f')
    staging_dir = Path(staging_dir).resolve() if staging_dir else report_path.parent / ('fbx-roundtrip-' + stamp)
    staging_dir.mkdir(parents=True, exist_ok=False)
    report = {'manifest': str(manifest_path), 'staging': str(staging_dir), 'clips': [],
              'unity_import_or_play_verified': False, 'original_blend_saved': False, 'passed': False}
    for clip in manifest['clips']:
        checks = []
        original_fbx = manifest_path.parent / clip['path']
        original_hash = sha(original_fbx)
        checks.append(check_record('Export FBX hash matches the exporter record', original_hash == clip['sha256']))
        clip_dir = staging_dir / clip['name']
        clip_dir.mkdir()
        staged_fbx = clip_dir / original_fbx.name
        shutil.copy2(original_fbx, staged_fbx)
        root, version = parse_fbx.parse(str(staged_fbx))
        objects_node = next(node for node in root.elems if node.id == b'Objects')
        payloads = [child.props[0] for node in objects_node.elems if node.id == b'Video'
                    for child in node.elems if child.id == b'Content' and child.props]
        payload_hashes = [hashlib.sha256(payload).hexdigest() for payload in payloads]
        animation_stacks = [node for node in objects_node.elems if node.id == b'AnimationStack']
        checks.append(check_record('Exactly one FBX animation take', len(animation_stacks) == 1, take_count=len(animation_stacks)))
        checks.append(check_record('Embedded texture bytes match the source image', bool(payloads) and all(payloads)
                                   and sorted(payload_hashes) == sorted(clip['embedded_image_sha256']),
                                   payload_bytes=[len(payload) for payload in payloads], sha256=payload_hashes))
        bpy.ops.wm.read_factory_settings(use_empty=True)
        # Blender's default import adds a one-frame offset to Blender-exported
        # absolute FBX key times. Zero keeps exported frame 1 at imported frame 1.
        bpy.ops.import_scene.fbx(filepath=str(staged_fbx), use_anim=True, anim_offset=0.0, use_image_search=False,
                                ignore_leaf_bones=False, automatic_bone_orientation=False)
        armatures = [obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE']
        meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
        checks.append(check_record('Only the intended rig and two meshes imported', len(armatures) == 1
                                   and len(meshes) == 2 and {obj.name for obj in meshes} == {'Head', 'Hair'},
                                   armatures=len(armatures), mesh_names=[obj.name for obj in meshes]))
        if len(armatures) != 1:
            report['clips'].append({'name': clip['name'], 'checks': checks, 'passed': False})
            continue
        armature = armatures[0]
        expected_bones = set(next(iter(clip['expected_samples'].values()))['bones'])
        bone_names = set(armature.data.bones.keys())
        checks.append(check_record('All 74 bones retained without added leaf bones', bone_names == expected_bones
                                   and len(bone_names) == 74 and len([name for name in bone_names if name.startswith('Hair_')]) == 22,
                                   total=len(bone_names), missing=sorted(expected_bones - bone_names), extra=sorted(bone_names - expected_bones)))
        action = armature.animation_data.action if armature.animation_data else None
        slot = armature.animation_data.action_slot if armature.animation_data else None
        imported_range = list(action.frame_range) if action else None
        checks.append(check_record('Armature has one active Action with a valid Slot and matching frame range', bool(action and slot)
                                   and abs(imported_range[0] - clip['frames'][0]) < 0.01
                                   and abs(imported_range[1] - clip['frames'][1]) < 0.01,
                                   action=action.name if action else None, frame_range=imported_range))
        # The staged import must resolve images from FBX extraction, with no image search.
        used_images = set()
        for mesh in meshes:
            for material_slot in mesh.material_slots:
                material = material_slot.material
                if material and material.node_tree:
                    used_images.update(node.image for node in material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image)
        images = []
        for image in used_images:
            if image.packed_file:
                image_hash = hashlib.sha256(image.packed_file.data).hexdigest()
                embedded_resolution = True
            else:
                image_path = Path(bpy.path.abspath(image.filepath)).resolve()
                image_hash = sha(image_path) if image_path.is_file() else None
                embedded_resolution = image_path.is_relative_to(clip_dir.resolve())
            images.append({'name': image.name, 'sha256': image_hash, 'staged_embedded_resolution': embedded_resolution})
        checks.append(check_record('Assigned image materials resolve from embedded content in isolation', bool(images)
                                   and all(image['staged_embedded_resolution'] and image['sha256'] in payload_hashes for image in images),
                                   images=images))
        frames = []
        for frame_string, expected in clip['expected_samples'].items():
            frame = int(frame_string)
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            bone_error = 0
            finite = True
            for bone_name, expected_head in expected['bones'].items():
                if bone_name not in armature.pose.bones:
                    finite = False
                    continue
                actual = armature.matrix_world @ armature.pose.bones[bone_name].head
                finite &= all(math.isfinite(value) for value in actual)
                bone_error = max(bone_error, max(abs(actual[axis] - expected_head[axis]) for axis in range(3)))
            mesh_results = {name: compare_mesh(bpy.data.objects[name], mesh) for name, mesh in expected['meshes'].items()}
            passed = finite and bone_error < 0.0002 and all(item['passed'] for item in mesh_results.values())
            frames.append({'frame': frame, 'max_bone_world_error': bone_error, 'finite': bool(finite), 'meshes': mesh_results, 'passed': bool(passed)})
        checks.append(check_record('Five sampled poses retain world bones and deformed vertex positions', all(frame['passed'] for frame in frames), frames=frames))
        checks.append(check_record('Production FBX remains byte-identical after validation', sha(original_fbx) == original_hash))
        report['clips'].append({'name': clip['name'], 'path': original_fbx.name, 'fbx_version': version,
                                'checks': checks, 'passed': all(check['passed'] for check in checks)})
    report['passed'] = len(report['clips']) == len(manifest['clips']) and all(clip['passed'] for clip in report['clips'])
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n', encoding='utf8')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--report', required=True)
    parser.add_argument('--staging-dir')
    options = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    report = validate_exports(options.manifest, options.report, options.staging_dir)
    print('ANIMATION_EXPORT_ROUNDTRIP=' + json.dumps({'passed': report['passed'],
          'clips': [{'name': clip['name'], 'passed': clip['passed'], 'failed_checks': [check['name'] for check in clip['checks'] if not check['passed']]}
                    for clip in report['clips']]}))
    if not report['passed']:
        raise SystemExit(1)
