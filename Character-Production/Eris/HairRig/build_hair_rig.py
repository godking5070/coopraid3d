"""Add an Eris hair rig while preserving the Mixamo body rig.

Run in Blender with the intended source already open, or:
  blender --background --factory-startup --disable-autoexec INPUT.blend \
    --python build_hair_rig.py -- --output OUTPUT.blend --report verification.json

For a live, already-open Blender scene, call apply_hair_rig(). This function
never opens another file or saves over the current .blend. All tests restore
the input pose before a new output copy is written.
"""
import argparse
from collections import defaultdict
import hashlib
import heapq
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector
import numpy as np

VERSION = 'eris-hair-rig-v1'
HEAD_BONE = 'mixamorig:Head'


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()


def file_hash(path):
    path = Path(path)
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None


def world_points(obj, evaluated=False):
    dependency_graph = bpy.context.evaluated_depsgraph_get() if evaluated else None
    owner = obj.evaluated_get(dependency_graph) if evaluated else obj
    mesh = owner.to_mesh() if evaluated else owner.data
    try:
        coordinates = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
        mesh.vertices.foreach_get('co', coordinates)
        coordinates = coordinates.reshape((-1, 3))
        transform = np.array(owner.matrix_world, dtype=float)
        return coordinates @ transform[:3, :3].T + transform[:3, 3]
    finally:
        if evaluated:
            owner.to_mesh_clear()


def mesh_fingerprint(obj, include_weights=True):
    mesh = obj.data
    value = {'positions': [list(vertex.co) for vertex in mesh.vertices],
             'edges': [list(edge.vertices) for edge in mesh.edges],
             'polygons': [[list(face.vertices), face.material_index] for face in mesh.polygons],
             'uv': {layer.name: [list(loop.uv) for loop in layer.data] for layer in mesh.uv_layers},
             'materials': [slot.material.name if slot.material else None for slot in obj.material_slots],
             'matrix_world': [list(row) for row in obj.matrix_world],
             'parent': obj.parent.name if obj.parent else None,
             'modifiers': [(mod.name, mod.type, mod.object.name if hasattr(mod, 'object') and mod.object else None)
                           for mod in obj.modifiers]}
    if include_weights:
        names = {group.index: group.name for group in obj.vertex_groups}
        value['groups'] = [group.name for group in obj.vertex_groups]
        value['weights'] = [[(names[group.group], group.weight) for group in vertex.groups] for vertex in mesh.vertices]
    return digest(value)


def action_fingerprint():
    output = []
    for action in bpy.data.actions:
        curves = []
        try:
            curves.extend(action.fcurves)
        except AttributeError:
            for layer in action.layers:
                for strip in layer.strips:
                    for bag in getattr(strip, 'channelbags', []):
                        curves.extend(bag.fcurves)
        output.append({'name': action.name, 'range': list(action.frame_range),
                       'curves': [(curve.data_path, curve.array_index,
                                   [(list(key.co), list(key.handle_left), list(key.handle_right), key.interpolation)
                                    for key in curve.keyframe_points]) for curve in curves]})
    return digest(output)


def bone_state(armature):
    return {bone.name: {'parent': bone.parent.name if bone.parent else None,
                        'head': np.array(bone.head_local), 'tail': np.array(bone.tail_local),
                        'matrix': np.array(bone.matrix_local), 'connected': bone.use_connect,
                        'deform': bone.use_deform}
            for bone in armature.data.bones}


def component_lists(vertex_count, edges, allowed=None):
    parents = list(range(vertex_count))

    def find(index):
        while parents[index] != index:
            parents[index] = parents[parents[index]]
            index = parents[index]
        return index

    for a, b in edges:
        if allowed is not None and not (allowed[a] and allowed[b]):
            continue
        root_a, root_b = find(a), find(b)
        if root_a != root_b:
            parents[root_b] = root_a
    groups = defaultdict(list)
    for index in range(vertex_count):
        if allowed is None or allowed[index]:
            groups[find(index)].append(index)
    return sorted(groups.values(), key=len, reverse=True)


def main_hair_labels(points, edges, main_indices):
    """Propagate the four physical lower hair branches through the connected scalp."""
    count = len(points)
    allowed = np.zeros(count, dtype=bool)
    allowed[main_indices] = True
    lower = allowed & (points[:, 2] < 0.72)
    candidates = component_lists(count, edges, lower)
    branches = [indices for indices in candidates if len(indices) >= 150]
    if len(branches) != 4:
        raise ValueError('Expected four lower long-hair branches; found ' + str([len(x) for x in branches]))
    # Explicit spatial names avoid relying on changing union-find component IDs.
    by_x = sorted(branches, key=lambda indices: points[indices, 0].mean())
    names = ['Side.R', 'Back.R', 'Back.L', 'Side.L']
    branches = by_x
    adjacency = [[] for _ in range(count)]
    for a, b in edges:
        if allowed[a] and allowed[b]:
            length = float(np.linalg.norm(points[a] - points[b]))
            adjacency[a].append((b, max(length, 1e-8)))
            adjacency[b].append((a, max(length, 1e-8)))
    labels = np.full(count, -1, dtype=np.int32)
    distances = np.full(count, np.inf)
    queue = []
    for label, indices in enumerate(branches):
        for index in indices:
            labels[index] = label
            distances[index] = 0
            heapq.heappush(queue, (0.0, index, label))
    while queue:
        distance, index, label = heapq.heappop(queue)
        if distance > distances[index] + 1e-12:
            continue
        for neighbor, edge_length in adjacency[index]:
            candidate = distance + edge_length
            if candidate + 1e-12 < distances[neighbor]:
                distances[neighbor] = candidate
                labels[neighbor] = label
                heapq.heappush(queue, (candidate, neighbor, label))
    if np.any(labels[main_indices] < 0):
        raise ValueError('Unclassified connected main hair vertices.')
    probabilities = np.zeros((count, 4), dtype=np.float64)
    probabilities[main_indices, labels[main_indices]] = 1
    seed_mask = np.zeros(count, dtype=bool)
    for branch in branches:
        seed_mask[branch] = True
    # Blend across branch joins near the scalp; keep disconnected lower curls local.
    for _ in range(5):
        updated = probabilities.copy()
        for index in main_indices:
            if seed_mask[index] or not adjacency[index]:
                continue
            neighbors = [neighbor for neighbor, _ in adjacency[index]]
            updated[index] = (probabilities[index] * 4 + probabilities[neighbors].sum(axis=0)) / (4 + len(neighbors))
        probabilities = updated
    return names, labels, probabilities


def fit_chain(points, indices, name, bone_count, root_z):
    strand = points[indices]
    tip_z = float(strand[:, 2].min()) - 0.005
    heights = np.linspace(root_z, tip_z, bone_count + 1)
    joints = []
    for height in heights:
        distances = np.abs(strand[:, 2] - height)
        band = strand[distances <= 0.03]
        if len(band) < 8:
            band = strand[np.argsort(distances)[:min(40, len(strand))]]
        centre = band[:, :2].mean(axis=0)
        joints.append(np.array([centre[0], centre[1], height]))
    unfiltered = np.array(joints)
    for index in range(1, len(joints) - 1):
        joints[index][:2] = unfiltered[index - 1, :2] * 0.2 + unfiltered[index, :2] * 0.6 + unfiltered[index + 1, :2] * 0.2
    return {'name': name, 'joints': joints,
            'bones': ['Hair_' + name + '_%02d' % (index + 1) for index in range(bone_count)],
            'centers_z': np.array([(joints[index][2] + joints[index + 1][2]) * 0.5 for index in range(bone_count)])}


def chain_weights(chain, z):
    centres = chain['centers_z']
    if z >= centres[0]:
        return [(chain['bones'][0], 1.0)]
    if z <= centres[-1]:
        return [(chain['bones'][-1], 1.0)]
    for index in range(len(centres) - 1):
        if centres[index] >= z >= centres[index + 1]:
            amount = float((centres[index] - z) / (centres[index] - centres[index + 1]))
            return [(chain['bones'][index], 1 - amount), (chain['bones'][index + 1], amount)]
    raise ValueError('Could not interpolate hair chain.')


def smoothstep(value):
    value = min(1.0, max(0.0, value))
    return value * value * (3 - 2 * value)


def apply_hair_rig(output_path=None, report_path=None, pack_images=False, restore_modeling_view=True):
    """Apply the focused rig to the CURRENT scene. Save only an optional NEW copy."""
    source = Path(bpy.data.filepath) if bpy.data.filepath else None
    source_hash = file_hash(source) if source else None
    if output_path:
        output_path = Path(output_path).resolve()
        if source and output_path == source.resolve():
            raise ValueError('Refusing to overwrite the source .blend.')
        if output_path.exists():
            raise FileExistsError('Refusing to overwrite an existing output: ' + str(output_path))
    hair = bpy.data.objects.get('Hair')
    body = bpy.data.objects.get('Head')
    if not hair or hair.type != 'MESH' or not body or body.type != 'MESH':
        raise ValueError('Expected the current Eris Hair and full-body Head meshes.')
    modifiers = [mod for mod in hair.modifiers if mod.type == 'ARMATURE' and mod.object]
    if len(modifiers) != 1:
        raise ValueError('Expected one existing Mixamo armature modifier on Hair.')
    armature = modifiers[0].object
    original_bones = bone_state(armature)
    if len(original_bones) != 52 or HEAD_BONE not in original_bones:
        raise ValueError('Expected the existing 52-bone Mixamo rig, including ' + HEAD_BONE)
    if any(name.startswith('Hair_') for name in original_bones):
        raise ValueError('A Hair_ rig already exists; refusing to add duplicates.')
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    unchanged_meshes = {obj.name: mesh_fingerprint(obj) for obj in mesh_objects if obj != hair}
    hair_geometry = mesh_fingerprint(hair, include_weights=False)
    actions_before = action_fingerprint()
    poses = {bone.name: bone.matrix_basis.copy() for bone in armature.pose.bones}
    selected = list(bpy.context.selected_objects)
    active = bpy.context.view_layer.objects.active
    hidden_flags = {obj.name: (obj.hide_viewport, obj.hide_render, obj.hide_get()) for obj in bpy.context.scene.objects}
    body_neutral = world_points(body, evaluated=True)
    head_pose = armature.pose.bones[HEAD_BONE]
    head_test_basis = poses[HEAD_BONE] @ Matrix.Rotation(math.radians(12), 4, 'Z')
    head_pose.matrix_basis = head_test_basis
    bpy.context.view_layer.update()
    original_body_head_test = world_points(body, evaluated=True)
    head_pose.matrix_basis = poses[HEAD_BONE]
    bpy.context.view_layer.update()
    points = world_points(hair)
    edges = [tuple(edge.vertices) for edge in hair.data.edges]
    islands = component_lists(len(points), edges)
    if [len(indices) for indices in islands[:3]] != [8005, 862, 861] or len(islands) != 11:
        raise ValueError('Hair topology differs from the audited Eris mesh; aborting focused fitting.')
    main_names, main_labels, main_probabilities = main_hair_labels(points, edges, islands[0])
    chains = []
    for index, name in enumerate(main_names):
        # Graph labels reach the connected bangs through the scalp. Those rigid
        # front vertices must not pull long-hair control roots through the face.
        facing_limit = 0.015 if name.startswith('Back.') else -0.07
        eligible = np.where((main_labels == index) & (points[:, 1] >= facing_limit))[0]
        chain = fit_chain(points, eligible, name, 4, 0.88)
        if name.startswith('Back.'):
            lower_branch = points[(main_labels == index) & (points[:, 2] < 0.72)]
            anchor_x = float(lower_branch[:, 0].mean())
            scalp_patch = points[np.isin(np.arange(len(points)), islands[0])
                                 & (np.abs(points[:, 2] - 0.88) < 0.03)
                                 & (np.abs(points[:, 0] - anchor_x) < 0.05)
                                 & (points[:, 1] > 0.015)]
            chain['joints'][0][0] = anchor_x
            if len(scalp_patch):
                chain['joints'][0][1] = float(np.quantile(scalp_patch[:, 1], 0.7))
        chains.append(chain)
    front_indices = {}
    for indices in islands[1:3]:
        side = 'L' if float(points[indices, 0].mean()) > 0 else 'R'
        front_indices[side] = indices
        chains.append(fit_chain(points, indices, 'Front.' + side, 3, 0.89))
    chain_lookup = {chain['name']: chain for chain in chains}
    inverse_armature = armature.matrix_world.inverted()
    old_hide = armature.hide_get()
    armature.hide_set(False)
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode='EDIT')
    try:
        collection = armature.data.collections.get('Hair Controls') or armature.data.collections.new('Hair Controls')
        roll_axis = inverse_armature.to_3x3() @ Vector((0, -1, 0))
        for chain in chains:
            parent = armature.data.edit_bones[HEAD_BONE]
            for index, name in enumerate(chain['bones']):
                bone = armature.data.edit_bones.new(name)
                bone.head = inverse_armature @ Vector(chain['joints'][index])
                bone.tail = inverse_armature @ Vector(chain['joints'][index + 1])
                bone.parent = parent
                bone.use_connect = index > 0
                bone.use_deform = True
                bone.align_roll(roll_axis)
                collection.assign(bone)
                parent = bone
    finally:
        bpy.ops.object.mode_set(mode='OBJECT')
    armature.hide_set(old_hide)
    # Do not let a shared mesh data-block leak revised hair weights to archived originals.
    if hair.data.users > 1:
        hair.data = hair.data.copy()
    for group in list(hair.vertex_groups):
        if group.name in original_bones or group.name.startswith('Hair_'):
            hair.vertex_groups.remove(group)
    group_names = [HEAD_BONE] + [name for chain in chains for name in chain['bones']]
    groups = {name: hair.vertex_groups.get(name) or hair.vertex_groups.new(name=name) for name in group_names}
    membership = {}
    for island_index, indices in enumerate(islands):
        for vertex_index in indices:
            membership[vertex_index] = island_index
    front_islands = {1, 2, 4, 5}  # The two small 90-vertex ornaments belong to the front curls.
    contributions = []
    for index, point in enumerate(points):
        island_index = membership[index]
        weights = defaultdict(float)
        if island_index == 0:
            strength = smoothstep((0.88 - point[2]) / 0.12) * smoothstep((point[1] + 0.10) / 0.05)
            weights[HEAD_BONE] = 1 - strength
            probabilities = main_probabilities[index]
            selected_labels = np.argsort(probabilities)[-2:]
            total = float(probabilities[selected_labels].sum())
            for label in selected_labels:
                amount = strength * float(probabilities[label]) / total
                if amount > 0:
                    for name, value in chain_weights(chains[int(label)], float(point[2])):
                        weights[name] += amount * value
        elif island_index in front_islands:
            side = 'L' if point[0] > 0 else 'R'
            strength = smoothstep((0.882 - point[2]) / 0.065)
            weights[HEAD_BONE] = 1 - strength
            for name, value in chain_weights(chain_lookup['Front.' + side], float(point[2])):
                weights[name] += strength * value
        else:
            weights[HEAD_BONE] = 1
        # At most four skin influences, suitable for the usual game import profile.
        weights = dict(sorted(((name, value) for name, value in weights.items() if value > 1e-8),
                              key=lambda pair: -pair[1])[:4])
        total = sum(weights.values())
        if not math.isfinite(total) or total <= 0:
            raise ValueError('Invalid skin weights at vertex ' + str(index))
        weights = {name: value / total for name, value in weights.items()}
        for name, value in weights.items():
            groups[name].add([index], value, 'REPLACE')
        contributions.append(weights)
    for name, basis in poses.items():
        armature.pose.bones[name].matrix_basis = basis
    for chain in chains:
        for name in chain['bones']:
            armature.pose.bones[name].matrix_basis = Matrix.Identity(4)
            armature.pose.bones[name]['rig_role'] = 'Eris hair deformation / manual pose or engine secondary motion'
    bpy.context.view_layer.update()
    checks = []

    def check(name, passed, **details):
        checks.append({'name': name, 'passed': bool(passed), **details})

    after_bones = bone_state(armature)
    rest_delta = 0.0
    hierarchy_ok = True
    for name, before in original_bones.items():
        after = after_bones[name]
        rest_delta = max(rest_delta, float(np.abs(before['matrix'] - after['matrix']).max()),
                         float(np.abs(before['head'] - after['head']).max()), float(np.abs(before['tail'] - after['tail']).max()))
        hierarchy_ok &= all(before[key] == after[key] for key in ['parent', 'connected', 'deform'])
    check('Existing 52 Mixamo bone hierarchy and rest transforms preserved', hierarchy_ok and rest_delta < 0.0001,
          original_count=len(original_bones), max_local_delta=rest_delta, max_world_delta=rest_delta * max(armature.scale))
    check('Body and hidden original mesh data, UVs, groups and modifiers unchanged',
          all(mesh_fingerprint(bpy.data.objects[name]) == fingerprint for name, fingerprint in unchanged_meshes.items()))
    check('Hair positions, topology, UVs, material slots and modifier unchanged', mesh_fingerprint(hair, include_weights=False) == hair_geometry)
    check('Animation actions and keyframes unchanged', action_fingerprint() == actions_before)
    actual_sums = [sum(group.weight for group in vertex.groups if hair.vertex_groups[group.group].name in groups) for vertex in hair.data.vertices]
    influences = [sum(group.weight > 1e-8 for group in vertex.groups if hair.vertex_groups[group.group].name in groups) for vertex in hair.data.vertices]
    check('All hair vertices have normalized finite weights and at most four influences',
          all(math.isfinite(value) and abs(value - 1) < 1e-6 for value in actual_sums) and max(influences) <= 4,
          vertices=len(actual_sums), minimum_sum=min(actual_sums), maximum_sum=max(actual_sums), maximum_influences=max(influences))
    bad_groups = [group.name for group in hair.vertex_groups if group.name in original_bones and group.name != HEAD_BONE]
    check('No torso, limb or neck groups remain on Hair', not bad_groups, unexpected_groups=bad_groups)
    body_after = world_points(body, evaluated=True)
    check('Neutral body deformation unchanged', float(np.abs(body_after - body_neutral).max()) < 0.000002,
          max_world_delta=float(np.abs(body_after - body_neutral).max()))
    hair_neutral = world_points(hair, evaluated=True)
    rigid = np.array([weights.get(HEAD_BONE, 0) > 0.999999 for weights in contributions])
    pose_cases = []
    for chain in chains:
        controller = armature.pose.bones[chain['bones'][0]]
        controller.matrix_basis = Matrix.Rotation(math.radians(12), 4, 'X')
        bpy.context.view_layer.update()
        posed_hair = world_points(hair, evaluated=True)
        posed_body = world_points(body, evaluated=True)
        movement = np.linalg.norm(posed_hair - hair_neutral, axis=1)
        target = np.array([sum(weight for name, weight in weights.items() if name in chain['bones']) > 0.05 for weights in contributions])
        target_count = int(target.sum())
        moved_targets = int(((movement > 1e-6) & target).sum())
        body_delta = float(np.abs(posed_body - body_after).max())
        scalp_delta = float(movement[rigid].max()) if rigid.any() else 0
        passed = target_count > 20 and moved_targets >= int(target_count * 0.9) and body_delta < 2e-6 and scalp_delta < 2e-6
        pose_cases.append({'chain': chain['name'], 'target_vertices': target_count, 'moved_target_vertices': moved_targets,
                           'max_hair_world_movement': float(movement.max()), 'max_body_delta': body_delta,
                           'max_rigid_scalp_delta': scalp_delta, 'passed': bool(passed)})
        controller.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
    check('Every hair chain moves its strand while leaving body and rigid scalp unchanged', all(case['passed'] for case in pose_cases), cases=pose_cases)
    old_head_matrix = armature.pose.bones[HEAD_BONE].matrix.copy()
    armature.pose.bones[HEAD_BONE].matrix_basis = head_test_basis
    bpy.context.view_layer.update()
    head_delta = armature.matrix_world @ armature.pose.bones[HEAD_BONE].matrix @ old_head_matrix.inverted() @ armature.matrix_world.inverted()
    transform = np.array(head_delta, dtype=float)
    expected_hair = hair_neutral @ transform[:3, :3].T + transform[:3, 3]
    head_hair_delta = float(np.abs(world_points(hair, evaluated=True) - expected_hair).max())
    head_body_delta = float(np.abs(world_points(body, evaluated=True) - original_body_head_test).max())
    check('Head rotation carries all hair and preserves original body response', head_hair_delta < 3e-6 and head_body_delta < 3e-6,
          max_hair_rigid_transform_error=head_hair_delta, max_body_response_error=head_body_delta)
    for name, basis in poses.items():
        armature.pose.bones[name].matrix_basis = basis
    for chain in chains:
        for name in chain['bones']:
            armature.pose.bones[name].matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    restored = float(np.abs(world_points(hair, evaluated=True) - hair_neutral).max())
    check('All test poses restored before saving', restored < 1e-6, max_restored_hair_delta=restored)
    check('Existing mesh visibility flags preserved', all((bpy.data.objects[name].hide_viewport, bpy.data.objects[name].hide_render, bpy.data.objects[name].hide_get()) == flags for name, flags in hidden_flags.items()))
    packed_images = []
    if pack_images:
        referenced_images = {node.image for obj in (body, hair) for slot in obj.material_slots if slot.material and slot.material.node_tree
                             for node in slot.material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image}
        for image in referenced_images:
            resolved = Path(bpy.path.abspath(image.filepath))
            item = {'name': image.name, 'path': str(resolved), 'was_packed': bool(image.packed_file)}
            if image.packed_file:
                item['packed_sha256'] = hashlib.sha256(image.packed_file.data).hexdigest()
            elif resolved.is_file():
                item['source_sha256'] = file_hash(resolved)
                image.pack()
                item['packed_sha256'] = hashlib.sha256(image.packed_file.data).hexdigest() if image.packed_file else None
            else:
                item['missing_source'] = True
            item['packed'] = bool(image.packed_file)
            packed_images.append(item)
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    for obj in selected:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active
    armature['hair_rig_version'] = VERSION
    armature['hair_rig_bone_count'] = 22
    hair['hair_rig_version'] = VERSION
    if restore_modeling_view:
        for screen in bpy.data.screens:
            if screen.name == 'Modeling':
                for area in screen.areas:
                    if area.type == 'CONSOLE':
                        area.type = 'VIEW_3D'
    unchanged_source = not source or file_hash(source) == source_hash
    check('Input blend file remains byte-identical', unchanged_source, source_sha256=source_hash)
    passed = all(item['passed'] for item in checks)
    report = {'rig_version': VERSION, 'source': str(source) if source else None, 'source_sha256': source_hash,
              'source_mtime': source.stat().st_mtime if source and source.is_file() else None,
              'output': str(output_path) if output_path else None, 'passed': passed, 'checks': checks,
              'hair_object': hair.name, 'body_object': body.name, 'armature': armature.name,
              'original_mixamo_bones': len(original_bones), 'new_hair_bones': 22, 'total_bones': len(armature.data.bones),
              'chains': [{'name': chain['name'], 'bones': chain['bones'], 'joints_world': [list(point) for point in chain['joints']]} for chain in chains],
              'packed_images': packed_images, 'automatic_physics_added': False}
    if not passed:
        if report_path:
            Path(report_path).parent.mkdir(parents=True, exist_ok=True)
            Path(report_path).write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
        raise RuntimeError('Hair rig validation failed: ' + ', '.join(item['name'] for item in checks if not item['passed']))
    if output_path:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(output_path), copy=True, check_existing=False)
        report['output_sha256'] = file_hash(output_path)
    if report_path:
        Path(report_path).parent.mkdir(parents=True, exist_ok=True)
        Path(report_path).write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True)
    parser.add_argument('--report', required=True)
    options = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    result = apply_hair_rig(options.output, options.report, pack_images=True)
    print('HAIR_RIG_RESULT=' + json.dumps({'passed': result['passed'], 'output': result['output'], 'new_bones': result['new_hair_bones'],
                                          'checks': [(item['name'], item['passed']) for item in result['checks']]}, ensure_ascii=True))
