"""Move the Iris upper-arm pivots to the shoulder and retarget its locomotion.

blender --background INPUT.blend --python align_shoulder_joints.py -- --output NEW.blend
Use the textured Iris scene with the shoulder weight pass already applied.
"""
import argparse
import heapq
import json
from math import degrees, sqrt
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / 'Animation'))
from apply_mixamo_animations import channels_for
from repair_shoulder_weights import skin_data, smoothstep
from validate_textured_character import capture, digest, file_sha


PREFIX = 'mixamorig:'
SIDES = (('Left', 1), ('Right', -1))
CHAIN = ('Shoulder', 'Arm', 'ForeArm', 'Hand')
UPPER_BODY = [PREFIX + 'Spine2']
for side, _ in SIDES:
    UPPER_BODY.extend(PREFIX + side + part for part in CHAIN)
PIVOT_X, PIVOT_Y, PIVOT_Z = .075, -.059, .755


def locomotion_actions():
    names = ['Eris_' + name + tag for tag in ('', '_Calm') for name in ('Idle', 'Walk', 'Run')]
    return [bpy.data.actions[name] for name in names]


def sample(armature, actions):
    animation, scene = armature.animation_data, bpy.context.scene
    saved = animation.action, animation.action_slot, animation.use_nla, scene.frame_current, scene.frame_subframe
    data = {}
    try:
        animation.use_nla = False
        for action in actions:
            if len(action.slots) != 1:
                raise ValueError('Expected one Action Slot on ' + action.name)
            animation.action = action
            animation.action_slot = action.slots[0]
            start, end = map(int, action.frame_range)
            frames = {}
            for frame in range(start, end + 1):
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                frames[frame] = {name: pose.matrix.copy() for name, pose in armature.pose.bones.items()}
            data[action.name] = frames
    finally:
        animation.action, animation.action_slot, animation.use_nla = saved[:3]
        scene.frame_set(saved[3], subframe=saved[4])
        bpy.context.view_layer.update()
    return data


def relocate(armature, rest):
    desired = {PREFIX + side + 'Arm': armature.matrix_world.inverted() @ Vector((sign * PIVOT_X, PIVOT_Y, PIVOT_Z))
               for side, sign in SIDES}
    if any(abs((armature.matrix_world @ rest[name].translation).x) < .10 for name in desired):
        raise ValueError('The starting upper-arm pivots are already inside the original position.')
    bpy.ops.object.select_all(action='DESELECT')
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode='EDIT')
    try:
        for side, _ in SIDES:
            shoulder = armature.data.edit_bones[PREFIX + side + 'Shoulder']
            upper = armature.data.edit_bones[PREFIX + side + 'Arm']
            elbow = upper.tail.copy()
            shoulder.tail = desired[upper.name]
            upper.head = desired[upper.name]
            upper.tail = elbow
            # Parallel transport the original roll to the new bone direction.
            for bone in (shoulder, upper):
                old_axes = rest[bone.name].to_3x3()
                direction = (bone.tail - bone.head).normalized()
                swing = old_axes.col[1].rotation_difference(direction)
                bone.align_roll(swing @ old_axes.col[2])
    finally:
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()


def aim_with_old_twist(transform, position, aim):
    rotation = transform.to_quaternion()
    swing = (rotation @ Vector((0, 1, 0))).rotation_difference(aim.normalized())
    result = (swing @ rotation).to_matrix().to_4x4()
    result.translation = position
    return result


def fit_chain(side, old_pose, rest, new_rest):
    shoulder, upper, lower, hand = [PREFIX + side + part for part in CHAIN]
    transfer = {name: old_pose[name] @ rest[name].inverted() @ new_rest[name] for name in (shoulder, upper, lower, hand)}
    shoulder_transform = old_pose[shoulder] @ rest[shoulder].inverted()
    pivot = shoulder_transform @ new_rest[upper].translation
    wrist = old_pose[hand].translation
    previous_elbow = old_pose[lower].translation
    upper_length = (new_rest[lower].translation - new_rest[upper].translation).length
    fore_length = (new_rest[hand].translation - new_rest[lower].translation).length
    direction = wrist - pivot
    reach = direction.length
    direction.normalize()
    distance = min(max(reach, abs(upper_length - fore_length) + .001), upper_length + fore_length - .001)
    projection = (upper_length ** 2 - fore_length ** 2 + distance ** 2) / (2 * distance)
    height = sqrt(max(0, upper_length ** 2 - projection ** 2))
    bend_direction = previous_elbow - pivot
    bend_direction -= direction * bend_direction.dot(direction)
    if bend_direction.length < .01:
        raise ValueError('Cannot determine a stable elbow bend plane.')
    elbow = pivot + direction * projection + bend_direction.normalized() * height
    wrist = pivot + direction * distance
    transfer[upper] = aim_with_old_twist(transfer[upper], pivot, elbow - pivot)
    transfer[lower] = aim_with_old_twist(transfer[lower], elbow, wrist - elbow)
    transfer[hand].translation = wrist
    return transfer


def retarget(armature, actions, samples, old_rest, new_rest):
    edited = set()
    frames_adjusted = 0
    for action in actions:
        frames = samples[action.name]
        motion = {name: [] for name in UPPER_BODY[1:]}
        for frame, old_pose in frames.items():
            desired = {UPPER_BODY[0]: old_pose[UPPER_BODY[0]]}
            for side, _ in SIDES:
                desired.update(fit_chain(side, old_pose, old_rest, new_rest))
            for name, keys in motion.items():
                bone = armature.data.bones[name]
                basis = bone.convert_local_to_pose(desired[name], new_rest[name], parent_matrix=desired[bone.parent.name],
                    parent_matrix_local=new_rest[bone.parent.name], invert=True)
                translation, rotation, scale = basis.decompose()
                if translation.length > .003 or max(abs(value - 1) for value in scale) > 1e-5:
                    raise ValueError(f'{action.name} frame {frame}: a joint needs translation or scale at {name}')
                if keys and rotation.dot(keys[-1]) < 0:
                    rotation.negate()
                keys.append(rotation)
        paths = {f'pose.bones[{json.dumps(name)}].rotation_quaternion': name for name in motion}
        written = 0
        for curve in channels_for(action, action.slots[0]):
            if curve.data_path not in paths:
                continue
            name = paths[curve.data_path]
            values = motion[name]
            if len(curve.keyframe_points) != len(values):
                raise ValueError('Expected quaternion keys on every frame of ' + action.name)
            for key, (frame, value) in zip(curve.keyframe_points, zip(frames, values)):
                if abs(key.co.x - frame) > 1e-5:
                    raise ValueError('Unexpected key times in ' + action.name)
                key.co.y = value[curve.array_index]
            curve.update()
            written += 1
            edited.add(name)
        if written != 32:
            raise ValueError('Expected four rotation channels for each arm bone in ' + action.name)
        frames_adjusted += len(frames)
    armature.update_tag()
    bpy.context.scene.frame_set(bpy.context.scene.frame_current)
    return edited, frames_adjusted


def max_distance(source, target, bone_names, unit_scale):
    return max((old[name].translation - target[clip][frame][name].translation).length * unit_scale
               for clip, frames in source.items() for frame, old in frames.items() for name in bone_names)


def unmodified_channels(state, rotations):
    return {name: [curve for curve in info['curves'] if not (name.startswith('Eris_') and curve[1] in rotations)]
            for name, info in state['actions'].items()}


def refine_right_sleeve(armature, clips):
    """Soften the two short edges on the right sleeve caught by the run/walk checks."""
    _, body, names, rest, _, weights, regions = skin_data()
    region = regions['Right']
    vertex_ids = region['ids']
    points = rest[vertex_ids]
    columns = [names.index(PREFIX + part) for part in ('Spine', 'Spine1', 'Spine2', 'RightShoulder', 'RightArm', 'RightForeArm')]
    original = weights[np.ix_(vertex_ids, columns)]
    a, b = np.concatenate((region['edges'], region['edges'][:, ::-1])).T
    length = np.linalg.norm(points[a] - points[b], axis=1)
    neighbors = [[] for _ in vertex_ids]
    for start, end, distance in zip(a, b, length):
        neighbors[start].append((end, distance))
    lookup = {int(vertex): index for index, vertex in enumerate(vertex_ids)}
    seeds = [lookup[vertex] for vertex in (1503, 1527, 2189, 2207)]
    # Limit the adjustment to five millimeters along the sleeve surface.
    radius = .005
    distance = np.full(len(vertex_ids), np.inf)
    queue = [(0, vertex) for vertex in seeds]
    distance[seeds] = 0
    heapq.heapify(queue)
    while queue:
        offset, vertex = heapq.heappop(queue)
        if offset != distance[vertex]:
            continue
        for neighbor, edge_length in neighbors[vertex]:
            candidate = offset + edge_length
            if candidate < min(radius, distance[neighbor]):
                distance[neighbor] = candidate
                heapq.heappush(queue, (candidate, neighbor))
    fade = 1 - smoothstep(distance, 0, radius)
    edge_weights = 1 / np.maximum(length, .0015)
    degree = np.bincount(a, weights=edge_weights, minlength=len(vertex_ids))
    smoothed = original.copy()
    for _ in range(2):
        average = np.stack([np.bincount(a, weights=edge_weights * values[b], minlength=len(vertex_ids))
                            for values in smoothed.T], axis=1) / degree[:, None]
        smoothed = .35 * smoothed + .65 * average
    smoothed *= original.sum(axis=1, keepdims=True) / np.maximum(smoothed.sum(axis=1, keepdims=True), 1e-10)
    delta = (smoothed - original) * fade[:, None]
    selected = np.flatnonzero(np.max(np.abs(delta), axis=1) > 2e-6)
    inv_world = armature.matrix_world.inverted()
    inv_rest = [armature.data.bones[names[column]].matrix_local.inverted() for column in columns]
    matrices = np.asarray([[np.asarray(armature.matrix_world @ frame[names[column]] @ inv @ inv_world)
                            for column, inv in zip(columns, inv_rest)]
                           for frames in clips.values() for frame in frames.values()])
    posed = (np.einsum('fbcd,nd->fnbc', matrices[:, :, :3, :3], points[selected], optimize=True)
             + matrices[:, None, :, :3, 3])
    displacement = np.linalg.norm(np.einsum('fnbc,nb->fnc', posed, delta[selected], optimize=True), axis=2).max(axis=0)
    cap = np.minimum(1, .002 / np.maximum(displacement, 1e-10))
    for row, factor in zip(selected, cap):
        vertex = int(vertex_ids[row])
        for column, before, after in zip(columns, original[row], original[row] + delta[row] * factor):
            group = body.vertex_groups[names[column]]
            if after > 1e-7:
                group.add([vertex], float(after), 'REPLACE')
            elif before > 0:
                group.remove([vertex])
    body.data.update()
    body.update_tag()
    _, _, _, _, _, updated, _ = skin_data()
    changed = np.flatnonzero(np.any(weights != updated, axis=1))
    if not set(changed) <= set(vertex_ids[selected]):
        raise AssertionError('Unexpected sleeve vertices changed.')
    return changed.tolist()


def align(output):
    output = Path(output).resolve()
    source = Path(bpy.data.filepath).resolve()
    if output.exists():
        raise FileExistsError(output)
    source_hash = file_sha(source)
    armature = bpy.data.objects['Armature']
    if len(armature.data.bones) != 74:
        raise ValueError('Expected the 74-bone Iris skeleton.')
    base = capture()
    actions = locomotion_actions()
    clips_before = sample(armature, actions)
    old_rest = {bone.name: bone.matrix_local.copy() for bone in armature.data.bones}
    relocate(armature, old_rest)
    new_rest = {bone.name: bone.matrix_local.copy() for bone in armature.data.bones}
    edited, frame_count = retarget(armature, actions, clips_before, old_rest, new_rest)
    clips_after = sample(armature, actions)
    changed_weights = refine_right_sleeve(armature, clips_after)
    final = capture()
    unit_scale = armature.matrix_world.to_scale().x

    moved = {PREFIX + side + part for side, _ in SIDES for part in ('Shoulder', 'Arm')}
    outside = set(old_rest) - moved
    hands = {name for name in old_rest if 'Hand' in name}
    body_bones = set(old_rest) - edited - hands
    rest_delta = max(max(abs(value) for row in old_rest[name] - new_rest[name] for value in row) for name in outside)
    hand_error = max_distance(clips_before, clips_after, hands, unit_scale)
    body_error = max_distance(clips_before, clips_after, body_bones, unit_scale)
    rotation_paths = {f'pose.bones[{json.dumps(name)}].rotation_quaternion' for name in edited}
    base['meshes']['Head'].pop('weights')
    final['meshes']['Head'].pop('weights')
    checks = {
        'meshes_uv_materials_and_images_exact_except_local_sleeve_weights': all(
            digest(base[key]) == digest(final[key]) for key in ('meshes', 'materials', 'images')),
        'bone_hierarchy_exact': {name: bone['parent'] for name, bone in base['bones'].items()}
            == {name: bone['parent'] for name, bone in final['bones'].items()},
        'other_bone_rest_matrices_preserved': rest_delta < .0001,
        'non_arm_animation_curves_exact': digest(unmodified_channels(base, rotation_paths))
            == digest(unmodified_channels(final, rotation_paths)),
        'other_body_bone_positions_match': body_error < .00002,
        'hands_and_fingers_follow_the_original_motion': hand_error < .0002,
        'source_file_preserved': file_sha(source) == source_hash,
    }
    joints = {}
    for side, _ in SIDES:
        name = PREFIX + side + 'Arm'
        before = armature.matrix_world @ old_rest[name].translation
        after = armature.matrix_world @ new_rest[name].translation
        joints[side] = {'world_before': list(before), 'world_after': list(after),
                       'inward_mm': (abs(before.x) - abs(after.x)) * 1000,
                       'upward_mm': (after.z - before.z) * 1000}

    if not all(checks.values()):
        raise AssertionError({'checks': checks, 'hand_error_mm': hand_error * 1000,
                              'body_error_mm': body_error * 1000, 'rest_matrix_max_delta': rest_delta})
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
    report = {'source_sha256': source_hash, 'output': output.name, 'output_sha256': file_sha(output),
              'checks': checks, 'joints': joints, 'action_frames': frame_count, 'actions': [a.name for a in actions],
              'refined_right_sleeve_vertices': changed_weights,
              'max_hand_error_mm': hand_error * 1000, 'max_other_body_error_mm': body_error * 1000,
              'rest_matrix_max_delta_outside_repair': rest_delta, 'passed': True, 'unity_import_or_play_verified': False}
    (output.parent / 'shoulder-joint-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf8', newline='\n')
    print('IRIS_SHOULDER_JOINTS=' + json.dumps(report))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    align(args.output)
