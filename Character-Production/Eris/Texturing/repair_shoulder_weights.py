"""Smooth the upper-arm weights on the textured Iris character in Blender.

Load the textured scene in background mode, then run this script with
--output NEW.blend. --compare checks an existing corrected copy instead.
"""
import argparse
import json
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / 'Animation'))
from rig_support import _components, _evaluated_world
from validate_textured_character import capture, digest, file_sha


SIDES = (('Left', 4234, 1513), ('Right', 1006, 1509))
CLIPS = ('Eris_Idle', 'Eris_Walk', 'Eris_Run')
PREFIX = 'mixamorig:'
MAX_POSE_DELTA = 0.003


def smoothstep(values, low, high):
    amount = np.clip((values - low) / (high - low), 0, 1)
    return amount * amount * (3 - 2 * amount)


def skin_data():
    body = bpy.data.objects['Head']
    armature = bpy.data.objects['Armature']
    modifier = body.modifiers[0]
    if (len(body.data.vertices) != 15802 or len(armature.data.bones) != 74
            or len(body.modifiers) != 1 or modifier.type != 'ARMATURE'
            or modifier.object != armature or modifier.use_deform_preserve_volume
            or not modifier.use_vertex_groups or modifier.use_bone_envelopes
            or modifier.vertex_group or armature.data.pose_position != 'POSE'):
        raise ValueError('Expected the 74-bone Iris body with its linear armature modifier.')
    names = [bone.name for bone in armature.data.bones]
    name_columns = {name: index for index, name in enumerate(names)}
    positions = np.asarray([body.matrix_world @ vertex.co for vertex in body.data.vertices])
    edges = np.asarray([edge.vertices[:] for edge in body.data.edges])
    weights = np.zeros((len(positions), len(names)))
    groups = {group.index: name_columns[group.name] for group in body.vertex_groups if group.name in name_columns}
    for vertex in body.data.vertices:
        for group in vertex.groups:
            if group.group in groups:
                weights[vertex.index, groups[group.group]] = group.weight
    if not np.isfinite(weights).all() or np.max(np.abs(weights.sum(axis=1) - 1)) > 1e-5:
        raise ValueError('The source skin weights are not normalized.')
    components = _components(body.data)
    regions = {}
    for side, seed, count in SIDES:
        ids = np.asarray(sorted(components[seed]))
        if len(ids) != count:
            raise ValueError('Upper sleeve mesh topology changed on ' + side)
        local_index = np.full(len(positions), -1, dtype=int)
        local_index[ids] = np.arange(len(ids))
        edge_ids = np.flatnonzero((local_index[edges] >= 0).all(axis=1))
        local_edges = local_index[edges[edge_ids]]
        arm = armature.data.bones[PREFIX + side + 'Arm']
        pivot = np.asarray(armature.matrix_world @ arm.head_local)
        axis = np.asarray(armature.matrix_world @ arm.tail_local) - pivot
        axis /= np.linalg.norm(axis)
        along = (positions[ids] - pivot) @ axis
        fade = smoothstep(along, -.066, -.042) * (1 - smoothstep(along, .024, .055))
        check_region = (along[local_edges] > -.06).all(axis=1) & (along[local_edges] < .105).all(axis=1)
        regions[side] = {'ids': ids, 'edges': local_edges, 'edge_ids': edge_ids[check_region], 'fade': fade}
    return armature, body, names, positions, edges, weights, regions


def sample_motion(armature, body, names, *, transforms=False, stress=False):
    """Capture Blender-evaluated vertices and restore the starting pose and Action."""
    scene = bpy.context.scene
    animation = armature.animation_data
    initial_action, initial_slot = animation.action, animation.action_slot
    initial_nla, frame, subframe = animation.use_nla, scene.frame_current, scene.frame_subframe
    bases = {bone.name: bone.matrix_basis.copy() for bone in armature.pose.bones}
    world = armature.matrix_world.copy()
    inverse = world.inverted()
    rest_inverse = [armature.data.bones[name].matrix_local.inverted() for name in names]
    vertices, matrices, labels = [], [], []

    try:
        animation.use_nla = False
        for name in CLIPS:
            action = bpy.data.actions[name]
            animation.action = action
            animation.action_slot = action.slots[0]
            start, end = map(int, action.frame_range)
            for sample in range(start, end + 1):
                scene.frame_set(sample)
                vertices.append(_evaluated_world(body).astype(np.float32))
                labels.append([name, sample])
                if transforms:
                    matrices.append([np.asarray(world @ armature.pose.bones[name].matrix @ bind @ inverse)
                                     for name, bind in zip(names, rest_inverse)])
        if stress:
            animation.action = None
            for side, _, _ in SIDES:
                for suffix, axis, degrees in [('Shoulder', (1, 0, 0), 25), ('Shoulder', (0, 0, 1), -25),
                                               ('Arm', (1, 0, 0), 40), ('Arm', (0, 0, 1), -45),
                                               ('Arm', (0, 1, 0), 65), ('ForeArm', (1, 0, 0), 60)]:
                    for bone in armature.pose.bones:
                        bone.matrix_basis = Matrix.Identity(4)
                    armature.pose.bones[PREFIX + side + suffix].matrix_basis = Quaternion(axis, np.deg2rad(degrees)).to_matrix().to_4x4()
                    vertices.append(_evaluated_world(body).astype(np.float32))
                    labels.append([side + suffix, [*axis, degrees]])
    finally:
        animation.action = initial_action
        animation.action_slot = initial_slot
        animation.use_nla = initial_nla
        scene.frame_set(frame, subframe=subframe)
        for name, basis in bases.items():
            armature.pose.bones[name].matrix_basis = basis
        bpy.context.view_layer.update()

    return np.asarray(vertices), np.asarray(matrices), labels


def repair(output):
    output = Path(output).resolve()
    if output.exists():
        raise FileExistsError(output)
    armature, body, names, positions, _, weights, regions = skin_data()
    evaluated, transforms, _ = sample_motion(armature, body, names, transforms=True)
    changed_count = 0

    for side, region in regions.items():
        ids, local_edges, fade = region['ids'], region['edges'], region['fade']
        columns = [names.index(PREFIX + name) for name in ('Spine', 'Spine1', 'Spine2', side + 'Shoulder',
                                                           side + 'Arm', side + 'ForeArm')]
        original = weights[np.ix_(ids, columns)]
        if np.max(np.abs(original[fade > 0].sum(axis=1) - 1)) > 1e-5:
            raise ValueError('Unexpected bone influence in ' + side + ' shoulder area.')
        endpoint, neighbor = np.concatenate((local_edges, local_edges[:, ::-1])).T
        points = positions[ids]
        distance = np.linalg.norm(points[endpoint] - points[neighbor], axis=1)
        edge_weight = 1 / np.maximum(distance, .0015)
        degree = np.bincount(endpoint, weights=edge_weight, minlength=len(ids))
        smoothed = original.copy()
        for _ in range(24):
            average = np.stack([np.bincount(endpoint, weights=edge_weight * values[neighbor], minlength=len(ids))
                                for values in smoothed.T], axis=1) / degree[:, None]
            smoothed = .35 * smoothed + .65 * average
        # Distal neighbors anchor the transition, while existing hand and cuff weights stay outside it.
        smoothed *= original.sum(axis=1, keepdims=True) / np.maximum(smoothed.sum(axis=1, keepdims=True), 1e-10)
        delta = (smoothed - original) * fade[:, None]
        # Bound joint movement across all three actual motions so the sleeve opening stays aligned.
        matrices = transforms[:, columns]
        posed = (np.einsum('fbcd,nd->fnbc', matrices[:, :, :3, :3], points, optimize=True)
                 + matrices[:, None, :, :3, 3])
        old_positions = np.einsum('fnbc,nb->fnc', posed, original, optimize=True)
        active = fade > 0
        discrepancy = np.linalg.norm(old_positions[:, active] - evaluated[:, ids[active]], axis=2).max()
        if discrepancy > 2e-6:
            raise ValueError('The linear skin calculation differs from Blender evaluation.')
        pose_delta = np.einsum('fnbc,nb->fnc', posed, delta, optimize=True)
        displacement = np.linalg.norm(pose_delta, axis=2).max(axis=0)
        delta *= np.minimum(1, MAX_POSE_DELTA / np.maximum(displacement, 1e-10))[:, None]
        changed = np.flatnonzero(np.max(np.abs(delta), axis=1) > 2e-6)
        for row in changed:
            vertex_id = int(ids[row])
            for column, value, previous in zip(columns, original[row] + delta[row], original[row]):
                group = body.vertex_groups[names[column]]
                if value > 1e-7:
                    group.add([vertex_id], float(value), 'REPLACE')
                elif previous > 0:
                    group.remove([vertex_id])
        changed_count += len(changed)

    if not changed_count:
        raise ValueError('No shoulder weights changed.')
    body.data.update()
    body.update_tag()
    bpy.context.view_layer.update()
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
    print('IRIS_SHOULDER_REPAIRED=' + json.dumps({'vertices': changed_count, 'file': output.name}))


def strain_metrics(values, edges, lengths):
    ratio = np.linalg.norm(values[:, edges[:, 1]] - values[:, edges[:, 0]], axis=2) / lengths
    return {'edge_frame_samples': int(ratio.size), 'peak_length_ratio': float(ratio.max()),
            'over_twice_rest_length': int((ratio > 2).sum()), 'under_40_percent_rest_length': int((ratio < .4).sum()),
            'p99_log_strain': float(np.percentile(np.abs(np.log(np.maximum(ratio, 1e-6))), 99))}


def compare(target, report_path):
    target, report_path = Path(target).resolve(), Path(report_path).resolve()
    source = Path(bpy.data.filepath)
    source_hash, target_hash = file_sha(source), file_sha(target)
    armature, body, names, rest, edges, weights, regions = skin_data()
    base = capture()
    before, _, labels = sample_motion(armature, body, names, stress=True)
    bpy.ops.wm.open_mainfile(filepath=str(target), load_ui=False)
    result = capture()
    final_arm, final_body, final_names, _, _, final_weights, _ = skin_data()
    after, _, final_labels = sample_motion(final_arm, final_body, final_names, stress=True)

    base['meshes']['Head'].pop('weights')
    result['meshes']['Head'].pop('weights')
    preserved = {key: digest(value) == digest(result[key]) for key, value in base.items()}
    changed = np.flatnonzero(np.any(weights != final_weights, axis=1))
    allowed = set(int(vertex) for region in regions.values() for vertex in region['ids'][region['fade'] > 0])
    motions = len(labels) - 12
    goal_frames = (before[:motions], after[:motions])
    deltas = np.linalg.norm(after - before, axis=2)
    unaffected = np.ones(len(rest), dtype=bool)
    unaffected[changed] = False
    lengths = np.linalg.norm(rest[edges[:, 1]] - rest[edges[:, 0]], axis=1)
    regional = {}
    for side, region in regions.items():
        filter_ids = region['edge_ids'][lengths[region['edge_ids']] > .0005]
        clips_before, clips_after = (strain_metrics(pose, edges[filter_ids], lengths[filter_ids]) for pose in goal_frames)
        stress_before, stress_after = (strain_metrics(pose[motions:], edges[filter_ids], lengths[filter_ids]) for pose in (before, after))
        regional[side] = {'vertices_changed': int(np.count_nonzero(np.isin(changed, region['ids']))),
                          'motion_before': clips_before, 'motion_after': clips_after,
                          'stress_before': stress_before, 'stress_after': stress_after}
    unchanged_group_names = {index for index, name in enumerate(names)
                             if not name.endswith(('Spine', 'Spine1', 'Spine2', 'Shoulder', 'Arm'))}
    checks = {
        'geometry_uv_materials_textures_animation_and_all_bones_preserved': all(preserved.values()),
        'only_upper_sleeve_vertices_and_expected_bone_weights_changed': bool(len(changed)) and set(changed) <= allowed
                and np.array_equal(weights[:, sorted(unchanged_group_names)], final_weights[:, sorted(unchanged_group_names)]),
        'body_weights_normalized_and_finite': bool(np.isfinite(final_weights).all() and np.max(np.abs(final_weights.sum(1) - 1)) < 2e-6),
        'evaluated_in_same_poses_and_all_finite': labels == final_labels and bool(np.isfinite(after).all()),
        'no_position_change_outside_repair': float(deltas[:, unaffected].max()) < 2e-6,
        'maximum_motion_change_within_three_millimeters': float(deltas[:motions].max()) < MAX_POSE_DELTA + 2e-6,
        'both_shoulders_improved_in_actual_motions': all(
            record['motion_after']['over_twice_rest_length'] == 0
            and record['motion_after']['under_40_percent_rest_length'] < record['motion_before']['under_40_percent_rest_length']
            and record['motion_after']['p99_log_strain'] < record['motion_before']['p99_log_strain']
            for record in regional.values()),
        'no_added_overshoot_in_independent_shoulder_arm_stress_poses': all(
            record['stress_after']['over_twice_rest_length'] <= record['stress_before']['over_twice_rest_length']
            and record['stress_after']['p99_log_strain'] <= record['stress_before']['p99_log_strain'] * 1.02
            for record in regional.values()),
        'read_only_files': file_sha(source) == source_hash and file_sha(target) == target_hash,
    }
    report = {'source_sha256': source_hash, 'output': target.name, 'output_sha256': target_hash, 'checks': checks,
              'preserved': preserved, 'changed_vertices': len(changed), 'frames': motions, 'stress_poses': 12,
              'max_motion_delta_mm': float(deltas[:motions].max() * 1000), 'regions': regional,
              'unity_import_or_play_verified': False, 'passed': all(checks.values())}
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf8', newline='\n')
    print('IRIS_SHOULDER_QA=' + json.dumps(report))
    if not report['passed']:
        raise SystemExit(1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    operation = parser.add_mutually_exclusive_group(required=True)
    operation.add_argument('--output')
    operation.add_argument('--compare')
    parser.add_argument('--report')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    if args.output:
        repair(args.output)
    elif args.report:
        compare(args.compare, args.report)
    else:
        parser.error('--compare requires --report')
