"""Import Mixamo motion onto Eris's existing body + hair armature.

No armature, mesh, material, weights or rest bones are replaced. Imported FBX
objects are temporary; only new sampled Actions remain. This uses Blender 5.3
Action Slots and rest-relative world transforms instead of copying Euler axes.
The current .blend is never saved unless an explicit NEW output path is given.

Example manifest: {"clips":[{"name":"Idle","path":"Idle.fbx"},
 {"name":"Walk","path":"Walk.fbx","in_place":true},
 {"name":"Run","path":"Run.fbx","in_place":true}],"active":"Idle"}
CLI: blender --background --factory-startup INPUT.blend --python SCRIPT.py --
 --manifest clips.json --report verification.json --output NEW_OUTPUT.blend
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from bpy_extras.anim_utils import action_ensure_channelbag_for_slot
from mathutils import Matrix, Quaternion, Vector
import numpy as np


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def canonical(name):
    return name.rsplit(':', 1)[-1]


def channels_for(action, slot):
    for layer in action.layers:
        for strip in layer.strips:
            bag = strip.channelbag(slot)
            if bag:
                yield from bag.fcurves


def static_fingerprint(target):
    """Hash rest data and every scene mesh; animation/pose are deliberately excluded."""
    data = {'rest': {bone.name: {'matrix': [list(row) for row in bone.matrix_local],
                               'head': list(bone.head_local), 'tail': list(bone.tail_local),
                               'parent': bone.parent.name if bone.parent else None,
                               'deform': bone.use_deform} for bone in target.data.bones},
            'object_matrix': [list(row) for row in target.matrix_world], 'meshes': []}
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH':
            continue
        names = {group.index: group.name for group in obj.vertex_groups}
        data['meshes'].append({'name': obj.name, 'positions': [list(v.co) for v in obj.data.vertices],
                               'faces': [list(face.vertices) for face in obj.data.polygons],
                               'uv': {layer.name: [list(v.uv) for v in layer.data] for layer in obj.data.uv_layers},
                               'weights': [[(names[g.group], g.weight) for g in v.groups] for v in obj.data.vertices],
                               'materials': [slot.material.name if slot.material else None for slot in obj.material_slots],
                               'parent': obj.parent.name if obj.parent else None,
                               'modifiers': [(mod.name, mod.type, mod.object.name if hasattr(mod, 'object') and mod.object else None)
                                             for mod in obj.modifiers]})
    return hashlib.sha256(json.dumps(data, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()


def bone_map(armature):
    result = {}
    for bone in armature.data.bones:
        if bone.name.startswith('Hair_'):
            continue
        key = canonical(bone.name)
        if key in result:
            raise ValueError('Ambiguous source bone name: ' + key)
        result[key] = bone.name
    return result


def similarity(source_points, target_points):
    source_points, target_points = np.asarray(source_points), np.asarray(target_points)
    centre_a, centre_b = source_points.mean(axis=0), target_points.mean(axis=0)
    a, b = source_points - centre_a, target_points - centre_b
    u, _, vt = np.linalg.svd(a.T @ b)
    rotation = vt.T @ u.T
    if np.linalg.det(rotation) < 0:
        vt[-1] *= -1
        rotation = vt.T @ u.T
    scale = float(np.sum((a @ rotation.T) * b) / np.sum(a * a))
    translation = centre_b - scale * rotation @ centre_a
    transform = np.eye(4)
    transform[:3, :3] = scale * rotation
    transform[:3, 3] = translation
    aligned = source_points @ transform[:3, :3].T + transform[:3, 3]
    error = float(np.linalg.norm(aligned - target_points, axis=1).max())
    return Matrix(transform.tolist()), scale, error


def evaluated_mesh_bounds(obj):
    dependency_graph = bpy.context.evaluated_depsgraph_get()
    owner = obj.evaluated_get(dependency_graph)
    mesh = owner.to_mesh()
    try:
        points = np.empty(len(mesh.vertices) * 3, dtype=float)
        mesh.vertices.foreach_get('co', points)
        points = points.reshape((-1, 3))
        matrix = np.array(owner.matrix_world)
        points = points @ matrix[:3, :3].T + matrix[:3, 3]
        return points.min(axis=0), points.max(axis=0)
    finally:
        owner.to_mesh_clear()


def import_mixamo_clip(path, name, target_name='Armature', in_place=True, loop=True, loop_blend_frames=4):
    """Return a new Action+Slot+report. Restore the existing active pose/action."""
    source_path = Path(path).resolve()
    if not source_path.is_file() or source_path.suffix.lower() != '.fbx':
        raise FileNotFoundError('Expected downloaded Mixamo FBX: ' + str(source_path))
    action_name = 'Eris_' + name
    if action_name in bpy.data.actions:
        raise ValueError('An action already exists: ' + action_name)
    target = bpy.data.objects.get(target_name)
    if not target or target.type != 'ARMATURE':
        raise ValueError('Existing target Armature not found.')
    target_names = bone_map(target)
    if len(target_names) != 52 or len([bone for bone in target.data.bones if bone.name.startswith('Hair_')]) != 22:
        raise ValueError('Expected existing Eris 52 Mixamo + 22 Hair bones.')
    if any(pose.rotation_mode != 'QUATERNION' for pose in target.pose.bones if pose.name in target_names.values()):
        raise ValueError('Expected existing Mixamo quaternion channels; refusing to alter rotation modes.')
    scene = bpy.context.scene
    before_objects = set(bpy.data.objects)
    before_actions = set(bpy.data.actions)
    before_ids = {key: set(getattr(bpy.data, key)) for key in ('meshes', 'armatures', 'materials', 'images')}
    original_animation = target.animation_data_create()
    original_action, original_slot = original_animation.action, original_animation.action_slot
    original_frame, original_subframe = scene.frame_current, scene.frame_subframe
    original_fps, original_fps_base = scene.render.fps, scene.render.fps_base
    original_world = target.matrix_world.copy()
    original_bases = {pose.name: pose.matrix_basis.copy() for pose in target.pose.bones}
    original_active = bpy.context.view_layer.objects.active
    original_selected = list(bpy.context.selected_objects)
    source_hash = sha256(source_path)
    source_action = action = slot = None
    result = None
    try:
        bpy.ops.import_scene.fbx(filepath=str(source_path), use_anim=True, ignore_leaf_bones=False,
                                automatic_bone_orientation=False, use_image_search=False)
        imported = set(bpy.data.objects) - before_objects
        candidates = [obj for obj in imported if obj.type == 'ARMATURE']
        if len(candidates) != 1:
            raise ValueError('Expected exactly one temporary Mixamo armature, got ' + str(len(candidates)))
        source = candidates[0]
        if not source.animation_data or not source.animation_data.action or not source.animation_data.action_slot:
            raise ValueError('The imported FBX has no assigned animation Action/Slot.')
        source_action = source.animation_data.action
        source_slot = source.animation_data.action_slot
        source_names = bone_map(source)
        missing = sorted(set(target_names) - set(source_names))
        if missing:
            raise ValueError('Mixamo clip does not contain required target bones: ' + ', '.join(missing))
        first, last = map(float, source_action.frame_range)
        start, end = first, last
        if end < start:
            raise ValueError('Source animation has no integer-frame samples.')
        source_fps = scene.render.fps / scene.render.fps_base
        target_fps = original_fps / original_fps_base
        sample_count = max(1, int(round((end - start) * target_fps / source_fps)) + 1)
        source_frames = np.linspace(start, end, sample_count).tolist()
        if len(source_frames) > 3000:
            raise ValueError('Unexpectedly long clip; review source trimming before importing.')
        scene.frame_set(math.floor(start), subframe=start - math.floor(start))
        bpy.context.view_layer.update()
        source_world_rest = source.matrix_world.copy()
        source_rest = {key: source_world_rest @ source.data.bones[source_names[key]].matrix_local for key in target_names}
        target_rest = {key: original_world @ target.data.bones[target_names[key]].matrix_local for key in target_names}
        alignment, uniform_scale, rest_error = similarity([matrix.translation[:] for matrix in source_rest.values()],
                                                         [matrix.translation[:] for matrix in target_rest.values()])
        length_ratios = []
        hierarchy_match = True
        for key in target_names:
            source_bone = source.data.bones[source_names[key]]
            target_bone = target.data.bones[target_names[key]]
            source_length = ((source_world_rest @ source_bone.tail_local) - source_rest[key].translation).length
            target_length = ((original_world @ target_bone.tail_local) - target_rest[key].translation).length
            if source_length > 1e-8:
                length_ratios.append(target_length / source_length)
            hierarchy_match &= ((canonical(source_bone.parent.name) if source_bone.parent else None)
                                == (canonical(target_bone.parent.name) if target_bone.parent else None))
        # Mixamo Without Skin may use a different bind stance from its T-Pose
        # download even for the identical character. Bone proportions identify
        # that case; do not force the target's hand/foot rest positions to it.
        proportion_scale = float(np.median(length_ratios))
        proportion_error = float(max(abs(ratio / proportion_scale - 1) for ratio in length_ratios))
        aligned_rotation = alignment.to_3x3() * (1 / uniform_scale)
        alignment = (aligned_rotation * proportion_scale).to_4x4()
        alignment.translation = target_rest['Hips'].translation - alignment.to_3x3() @ source_rest['Hips'].translation
        uniform_scale = proportion_scale
        rest_error = max(((alignment @ source_rest[key]).translation - target_rest[key].translation).length for key in target_names)
        height = max(matrix.translation.z for matrix in target_rest.values()) - min(matrix.translation.z for matrix in target_rest.values())
        if (not math.isfinite(uniform_scale) or uniform_scale <= 0 or not hierarchy_match
                or proportion_error > 0.02 or rest_error > max(0.03, height * 0.15)):
            raise ValueError('Source skeleton proportions/hierarchy differ from Eris; transfer refused. '
                             'Rest error=%r proportion error=%r hierarchy=%r' % (rest_error, proportion_error, hierarchy_match))
        aligned_rest_inverse = {key: (alignment @ matrix).inverted() for key, matrix in source_rest.items()}
        target_inverse = original_world.inverted()
        samples = []
        source_hips_world = []
        for source_frame in source_frames:
            scene.frame_set(math.floor(source_frame), subframe=source_frame - math.floor(source_frame))
            bpy.context.view_layer.update()
            evaluated = source.evaluated_get(bpy.context.evaluated_depsgraph_get())
            desired = {}
            for key in target_names:
                aligned_pose = alignment @ evaluated.matrix_world @ evaluated.pose.bones[source_names[key]].matrix
                desired[target_names[key]] = target_inverse @ aligned_pose @ aligned_rest_inverse[key] @ target_rest[key]
            if not all(math.isfinite(value) for matrix in desired.values() for row in matrix for value in row):
                raise ValueError('Non-finite source pose at frame ' + str(source_frame))
            source_hips_world.append((original_world @ desired[target_names['Hips']]).translation.copy())
            samples.append(desired)
        # Remove net horizontal travel, preserving periodic hip sway and vertical bob.
        travel = source_hips_world[-1] - source_hips_world[0]
        removed = Vector((travel.x, travel.y, 0)) if in_place else Vector((0, 0, 0))
        first_offset = source_hips_world[0] - target_rest['Hips'].translation
        first_offset = Vector((first_offset.x, first_offset.y, 0)) if in_place else Vector((0, 0, 0))
        if in_place:
            for index, desired in enumerate(samples):
                world_shift = first_offset + removed * (index / max(1, len(samples) - 1))
                local_shift = target_inverse.to_3x3() @ world_shift
                for matrix in desired.values():
                    matrix.translation -= local_shift
        # Convert desired armature-space matrices into the EXISTING rest hierarchy.
        bases = []
        for desired in samples:
            frame_basis = {}
            for bone_name in target_names.values():
                bone = target.data.bones[bone_name]
                kwargs = {}
                if bone.parent:
                    kwargs['parent_matrix'] = desired[bone.parent.name]
                    kwargs['parent_matrix_local'] = bone.parent.matrix_local
                frame_basis[bone_name] = bone.convert_local_to_pose(desired[bone_name], bone.matrix_local, invert=True, **kwargs)
            bases.append(frame_basis)
        values = {bone_name: [] for bone_name in target_names.values()}
        for frame_basis in bases:
            for bone_name, matrix in frame_basis.items():
                location, rotation, scale = matrix.decompose()
                if bone_name != target_names['Hips']:
                    location = Vector((0, 0, 0))
                if max(abs(value - 1) for value in scale) > 0.02:
                    raise ValueError('Unexpected animated bone scale in ' + bone_name)
                scale = Vector((1, 1, 1))
                if values[bone_name] and rotation.dot(values[bone_name][-1][1]) < 0:
                    rotation.negate()
                values[bone_name].append((location.copy(), rotation.copy(), scale.copy()))
        blend_count = min(max(int(loop_blend_frames), 0), max(0, len(samples) // 5)) if loop else 0
        if loop and len(samples) > 1:
            for bone_name, frames in values.items():
                first_value = frames[0]
                for index in range(max(1, len(frames) - blend_count - 1), len(frames)):
                    amount = (index - (len(frames) - blend_count - 1)) / max(1, blend_count)
                    amount = min(1.0, max(0.0, amount))
                    amount = amount * amount * (3 - 2 * amount)
                    loc, rot, scale = frames[index]
                    frames[index] = (loc.lerp(first_value[0], amount), rot.slerp(first_value[1], amount), scale.lerp(first_value[2], amount))
                # Ensure an exact same-pose loop boundary, including one/two-frame inputs.
                frames[-1] = tuple(value.copy() for value in first_value)
        action = bpy.data.actions.new(action_name)
        slot = action.slots.new(id_type='OBJECT', name=target.name)
        bag = action_ensure_channelbag_for_slot(action, slot)
        frame_numbers = np.arange(1, len(samples) + 1, dtype=float)
        for bone_name, frames in values.items():
            properties = {'location': np.array([list(value[0]) for value in frames]),
                          'rotation_quaternion': np.array([list(value[1]) for value in frames]),
                          'scale': np.array([list(value[2]) for value in frames])}
            for property_name, data in properties.items():
                for axis in range(data.shape[1]):
                    curve = bag.fcurves.new('pose.bones[' + json.dumps(bone_name) + '].' + property_name,
                                            index=axis, group_name=bone_name)
                    curve.keyframe_points.add(len(samples))
                    pairs = np.column_stack((frame_numbers, data[:, axis])).reshape(-1)
                    curve.keyframe_points.foreach_set('co', pairs)
                    for key in curve.keyframe_points:
                        key.interpolation = 'LINEAR'
                    if loop:
                        curve.modifiers.new(type='CYCLES')
                    curve.update()
        action.use_fake_user = True
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, max(1, len(samples))
        action['mixamo_source_file'] = source_path.name
        action['mixamo_source_sha256'] = source_hash
        action['in_place'] = bool(in_place)
        action['loop'] = bool(loop)
        action['loop_blend_frames'] = blend_count
        action['source_rest_max_world_error'] = rest_error
        original_animation.action = action
        original_animation.action_slot = slot
        target.matrix_world = original_world
        for bone_name, basis in original_bases.items():
            if bone_name.startswith('Hair_'):
                target.pose.bones[bone_name].matrix_basis = basis
        target_bounds = []
        finite = True
        hips = []
        max_pose_error = 0.0
        max_rotation_matrix_error = 0.0
        probe_frames = sorted(set([1, max(1, len(samples) // 4), max(1, len(samples) // 2), max(1, len(samples) * 3 // 4), len(samples)]))
        for frame in range(1, len(samples) + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            evaluated = target.evaluated_get(bpy.context.evaluated_depsgraph_get())
            for bone_name in target_names.values():
                matrix = evaluated.pose.bones[bone_name].matrix
                finite &= all(math.isfinite(value) for row in matrix for value in row)
                # Last blend frames intentionally differ from the source for seamless looping.
                if frame < len(samples) - blend_count - 1 or not loop:
                    desired_matrix = samples[frame - 1][bone_name]
                    rotation_error = np.abs(np.array(matrix.to_3x3().normalized())
                                            - np.array(desired_matrix.to_3x3().normalized())).max()
                    max_rotation_matrix_error = max(max_rotation_matrix_error, float(rotation_error))
                    if bone_name == target_names['Hips']:
                        delta = np.abs(np.array(matrix.translation) - np.array(desired_matrix.translation)).max()
                        max_pose_error = max(max_pose_error, float(delta))
            hips.append((evaluated.matrix_world @ evaluated.pose.bones[target_names['Hips']].matrix).translation[:])
            if frame in probe_frames:
                frame_bounds = []
                for object_name in ('Head', 'Hair'):
                    minimum, maximum = evaluated_mesh_bounds(bpy.data.objects[object_name])
                    finite &= bool(np.isfinite(minimum).all() and np.isfinite(maximum).all())
                    if float(np.linalg.norm(maximum - minimum)) > max(3.0, height * 5):
                        raise ValueError('Exploded mesh bounds at frame ' + str(frame) + ': ' + object_name)
                    frame_bounds.append({'object': object_name, 'min': minimum.tolist(), 'max': maximum.tolist()})
                target_bounds.append({'frame': frame, 'bounds': frame_bounds})
        if not finite or max_pose_error > 0.001 or max_rotation_matrix_error > 0.001:
            raise ValueError('Transferred pose validation failed: finite=%r root error=%r rotation error=%r'
                             % (finite, max_pose_error, max_rotation_matrix_error))
        hair_curves = [curve.data_path for curve in bag.fcurves if 'Hair_' in curve.data_path]
        if hair_curves:
            raise AssertionError('Mixamo transfer unexpectedly keyed hair bones.')
        result = {'action': action.name, 'slot': slot.identifier, 'source': source_path.name,
                  'source_sha256': source_hash, 'source_action': source_action.name,
                  'source_frame_range': [start, end], 'source_fps': source_fps, 'frames': len(samples), 'fps': target_fps,
                  'bone_channels': len(target_names), 'hair_bones_keyed': 0, 'source_alignment_scale': uniform_scale,
                  'source_rest_max_world_error': rest_error, 'source_bone_proportion_error': proportion_error,
                  'source_hierarchy_matches': bool(hierarchy_match), 'retarget_mode': 'rest_relative_rotation_and_root_translation',
                  'max_root_translation_error': max_pose_error, 'max_rotation_matrix_error': max_rotation_matrix_error,
                  'non_root_joint_translation_policy': 'constant_zero_at_bind',
                  'loop': bool(loop), 'loop_blend_frames': blend_count, 'in_place': bool(in_place),
                  'removed_initial_world_xy_offset': list(first_offset),
                  'removed_world_xy_travel': list(removed), 'hips_world_start': hips[0], 'hips_world_end': hips[-1],
                  'mesh_bounds_samples': target_bounds, 'source_file_unchanged': sha256(source_path) == source_hash,
                  'finite_pose_samples': finite, 'motion_quality_visually_verified': False}
        return action, slot, result
    except Exception:
        if action:
            if target.animation_data and target.animation_data.action == action:
                target.animation_data.action = None
            bpy.data.actions.remove(action)
            action = None
        raise
    finally:
        original_animation.action = original_action
        if original_action and original_slot:
            original_animation.action_slot = original_slot
        target.matrix_world = original_world
        scene.render.fps, scene.render.fps_base = original_fps, original_fps_base
        scene.frame_set(original_frame, subframe=original_subframe)
        for bone_name, basis in original_bases.items():
            target.pose.bones[bone_name].matrix_basis = basis
        for obj in list(set(bpy.data.objects) - before_objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        for temporary_action in list(set(bpy.data.actions) - before_actions):
            if temporary_action != action:
                bpy.data.actions.remove(temporary_action)
        for key, previous in before_ids.items():
            collection = getattr(bpy.data, key)
            for item in list(set(collection) - previous):
                if item.users == 0:
                    collection.remove(item)
        for obj in bpy.context.selected_objects:
            obj.select_set(False)
        for obj in original_selected:
            if obj.name in bpy.context.view_layer.objects:
                obj.select_set(True)
        bpy.context.view_layer.objects.active = original_active
        bpy.context.view_layer.update()


def activate_clip(action, slot, target_name='Armature'):
    target = bpy.data.objects[target_name]
    target.animation_data_create().action = action
    target.animation_data.action_slot = slot
    bpy.context.scene.frame_start = int(action.frame_start)
    bpy.context.scene.frame_end = int(action.frame_end)
    bpy.context.scene.frame_set(int(action.frame_start))
    bpy.context.view_layer.update()


def apply_mixamo_animations(clips, active='Idle', target_name='Armature', report_path=None, output_path=None):
    """Apply explicit local FBX clips to an existing rig; no downloads or source saves."""
    target = bpy.data.objects[target_name]
    initial_file = Path(bpy.data.filepath) if bpy.data.filepath else None
    initial_hash = sha256(initial_file) if initial_file and initial_file.is_file() else None
    if output_path:
        output_path = Path(output_path).resolve()
        if output_path.exists() or (initial_file and output_path == initial_file.resolve()):
            raise FileExistsError('Refusing to overwrite an existing .blend: ' + str(output_path))
    before = static_fingerprint(target)
    actions = {}
    reports = []
    try:
        for clip in clips:
            name = clip['name']
            if name in actions:
                raise ValueError('Duplicate clip name in batch: ' + name)
            action, slot, report = import_mixamo_clip(clip['path'], name, target_name,
                                                    in_place=clip.get('in_place', True), loop=clip.get('loop', True),
                                                    loop_blend_frames=clip.get('loop_blend_frames', 4))
            actions[name] = (action, slot)
            reports.append(report)
    except Exception:
        for action, _ in actions.values():
            bpy.data.actions.remove(action)
        raise
    unchanged = static_fingerprint(target) == before
    if not unchanged:
        raise AssertionError('Static rig, skin, mesh or UV data changed during motion transfer.')
    if active:
        if active not in actions:
            raise ValueError('Requested active clip not in batch: ' + active)
        activate_clip(*actions[active], target_name=target_name)
    source_unchanged = initial_hash is None or sha256(initial_file) == initial_hash
    report = {'clips': reports, 'active': active, 'static_character_unchanged': unchanged,
              'source_blend_sha256': initial_hash, 'source_blend_unchanged': source_unchanged,
              'existing_bones': len(target.data.bones), 'hair_bones': len([bone for bone in target.data.bones if bone.name.startswith('Hair_')]),
              'new_armatures_retained': 0,
              'passed': unchanged and source_unchanged and all(item['finite_pose_samples'] and item['source_file_unchanged'] for item in reports)}
    if output_path:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(output_path), copy=True, check_existing=False)
        report['output'] = str(output_path)
        report['output_sha256'] = sha256(output_path)
    if report_path:
        Path(report_path).parent.mkdir(parents=True, exist_ok=True)
        Path(report_path).write_text(json.dumps(report, indent=2, allow_nan=False), encoding='utf8')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--report', required=True)
    parser.add_argument('--output')
    arguments = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    manifest_path = Path(arguments.manifest).resolve()
    manifest = json.loads(manifest_path.read_text(encoding='utf8'))
    for clip in manifest['clips']:
        candidate = Path(clip['path'])
        if not candidate.is_absolute():
            clip['path'] = str(manifest_path.parent / candidate)
    result = apply_mixamo_animations(manifest['clips'], active=manifest.get('active', 'Idle'),
                                     report_path=arguments.report, output_path=arguments.output)
    print('MIXAMO_APPLY=' + json.dumps({'passed': result['passed'], 'clips': [(item['action'], item['frames']) for item in result['clips']]}))
