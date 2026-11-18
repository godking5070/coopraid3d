"""Independent source-to-final preservation checks for the Eris animated copy.

Only body clothing weights explicitly declared by rig_support may differ.
Geometry, UVs, hair weights, all74rest bones, materials and original Actions
must survive a fresh reopen. This reads both files in a background process and
never saves either. Actual Unity playback remains outside this check.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np


def file_sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def clean(value):
    if value is None or isinstance(value, (str, int, bool)):
        return value
    if isinstance(value, float):
        return value if math.isfinite(value) else str(value)
    try:
        return [clean(item) for item in value]
    except TypeError:
        return str(value)


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()


def mesh_geometry(obj):
    mesh = obj.data
    return digest({'vertices': [list(vertex.co) for vertex in mesh.vertices],
                   'edges': [list(edge.vertices) for edge in mesh.edges],
                   'faces': [(list(face.vertices), face.material_index, face.use_smooth) for face in mesh.polygons],
                   'uv': [(layer.name, [list(loop.uv) for loop in layer.data]) for layer in mesh.uv_layers],
                   'parent': obj.parent.name if obj.parent else None,
                   'modifiers': [(mod.name, mod.type, mod.object.name if hasattr(mod, 'object') and mod.object else None)
                                 for mod in obj.modifiers],
                   'materials': [slot.material.name if slot.material else None for slot in obj.material_slots]})


def skin_weights(obj):
    names = {group.index: group.name for group in obj.vertex_groups}
    return [{names[group.group]: group.weight for group in vertex.groups if group.weight > 0} for vertex in obj.data.vertices]


def action_data(action):
    curves = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    curves.append((bag.slot.identifier, curve.data_path, curve.array_index,
                                   [(list(key.co), list(key.handle_left), list(key.handle_right), key.interpolation)
                                    for key in curve.keyframe_points],
                                   [modifier.type for modifier in curve.modifiers]))
    return {'hash': digest(curves), 'curves': curves, 'fake_user': action.use_fake_user,
            'range': list(action.frame_range), 'slots': [slot.identifier for slot in action.slots]}


def material_data(material):
    result = {'diffuse': list(material.diffuse_color), 'nodes': [], 'links': []}
    if material.node_tree:
        for node in material.node_tree.nodes:
            result['nodes'].append({'name': node.name, 'type': node.bl_idname,
                'inputs': [(socket.name, clean(socket.default_value)) for socket in node.inputs if hasattr(socket, 'default_value')],
                'image': node.image.name if node.type == 'TEX_IMAGE' and node.image else None,
                'uv_map': getattr(node, 'uv_map', None), 'vector_type': getattr(node, 'vector_type', None)})
        result['links'] = [(link.from_node.name, link.from_socket.name, link.to_node.name, link.to_socket.name)
                           for link in material.node_tree.links]
    return digest(result)


def component_for(mesh, seed):
    adjacency = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        adjacency[a].append(b)
        adjacency[b].append(a)
    found, stack = {seed}, [seed]
    while stack:
        for neighbor in adjacency[stack.pop()]:
            if neighbor not in found:
                found.add(neighbor)
                stack.append(neighbor)
    return found


def capture():
    armature = bpy.data.objects['Armature']
    meshes = {obj.name: {'geometry': mesh_geometry(obj), 'weights': skin_weights(obj)}
              for obj in bpy.context.scene.objects if obj.type == 'MESH'}
    clothing = set()
    body = bpy.data.objects['Head']
    for seed, expected_count in [(1709, 2299), (4234, 1513), (1006, 1509)]:
        group = component_for(body.data, seed)
        if len(group) != expected_count:
            raise ValueError('Original clothing topology changed before validation.')
        clothing |= group
    return {'meshes': meshes,
            'bones': {bone.name: {'parent': bone.parent.name if bone.parent else None,
                                 'matrix': [list(row) for row in bone.matrix_local], 'head': list(bone.head_local),
                                 'tail': list(bone.tail_local), 'deform': bone.use_deform} for bone in armature.data.bones},
            'object_matrix': [list(row) for row in armature.matrix_world],
            'hair_pose': {bone.name: [list(row) for row in bone.matrix_basis] for bone in armature.pose.bones if bone.name.startswith('Hair_')},
            'actions': {action.name: action_data(action) for action in bpy.data.actions},
            'materials': {material.name: material_data(material) for material in bpy.data.materials},
            'clothing_vertex_ids': clothing}


def validate_character(source_path, final_path, rig_report_path, report_path):
    source_path, final_path, report_path = map(lambda path: Path(path).resolve(), (source_path, final_path, report_path))
    hashes = {'source': file_sha(source_path), 'final': file_sha(final_path)}
    bpy.ops.wm.open_mainfile(filepath=str(source_path), load_ui=False, use_scripts=False)
    source = capture()
    bpy.ops.wm.open_mainfile(filepath=str(final_path), load_ui=False, use_scripts=False)
    final = capture()
    checks = []

    def check(name, passed, **details):
        checks.append({'name': name, 'passed': bool(passed), **details})

    check('All74bone rest transforms and hierarchy are exact', source['bones'] == final['bones'] and len(final['bones']) == 74)
    check('Armature object transform is exact', source['object_matrix'] == final['object_matrix'])
    check('All original mesh positions topology UVs materials and modifiers are exact', source['meshes'].keys() == final['meshes'].keys()
          and all(source['meshes'][name]['geometry'] == final['meshes'][name]['geometry'] for name in source['meshes']))
    check('Hair weights are exact', source['meshes']['Hair']['weights'] == final['meshes']['Hair']['weights'])
    check('Hair local pose basis is preserved', source['hair_pose'] == final['hair_pose'])
    changed = []
    before, after = source['meshes']['Head']['weights'], final['meshes']['Head']['weights']
    for index, (original, current) in enumerate(zip(before, after)):
        if any(abs(original.get(name, 0) - current.get(name, 0)) > 2e-6 for name in original.keys() | current.keys()):
            changed.append(index)
    declared = set(json.loads(Path(rig_report_path).read_text(encoding='utf8'))['changed_vertex_ids'])
    allowed = source['clothing_vertex_ids']
    check('Body weight edits are only declared clothing vertices', set(changed) <= declared <= allowed,
          changed_vertices=len(changed), declared_vertices=len(declared), undeclared_changes=sorted(set(changed) - declared),
          non_clothing_declarations=sorted(declared - allowed))
    untouched_meshes = [name for name in source['meshes'] if name not in {'Head', 'Hair'}]
    check('Hidden original duplicate skin data is exact', all(source['meshes'][name]['weights'] == final['meshes'][name]['weights'] for name in untouched_meshes))
    check('All original material graphs are exact', all(final['materials'].get(name) == fingerprint for name, fingerprint in source['materials'].items()))
    original_actions_ok = all(name in final['actions'] and final['actions'][name]['hash'] == value['hash']
                              and final['actions'][name]['range'] == value['range'] for name, value in source['actions'].items())
    check('Original Actions survive reopen with exact curves and ranges', original_actions_ok,
          original_actions={name: {'present': name in final['actions'], 'fake_user': final['actions'].get(name, {}).get('fake_user')}
                            for name in source['actions']})
    expected_ranges = {'Eris_Idle': [1, 432], 'Eris_Walk': [1, 38], 'Eris_Run': [1, 78]}
    for name, frames in expected_ranges.items():
        action = final['actions'].get(name)
        curves = action['curves'] if action else []
        hair_keys = [curve[1] for curve in curves if 'Hair_' in curve[1]]
        finite = all(math.isfinite(value) for curve in curves for key in curve[3] for point in key[:3] for value in point)
        no_nonroot_stretch = True
        loop_error = 0.0
        for _, data_path, _, keys, modifiers in curves:
            if not keys:
                continue
            values = [key[0][1] for key in keys]
            if data_path.endswith('.location') and 'mixamorig:Hips' not in data_path:
                no_nonroot_stretch &= max(abs(value) for value in values) < 1e-6
            if data_path.endswith('.scale'):
                no_nonroot_stretch &= max(abs(value - 1) for value in values) < 1e-6
            loop_error = max(loop_error, abs(values[0] - values[-1]))
        check(name + ': valid looping52bone motion noHairkeys nojointstretch', bool(action) and action['range'] == frames
              and not hair_keys and finite and no_nonroot_stretch and loop_error < 1e-6,
              range=action['range'] if action else None, curves=len(curves), hair_keys=len(hair_keys),
              finite=finite, nonroot_translations_and_scales_valid=no_nonroot_stretch, loop_channel_endpoint_error=loop_error)
    check('Playback is30FPS with Idle active', bpy.context.scene.render.fps == 30 and bpy.context.scene.render.fps_base == 1
          and bpy.data.objects['Armature'].animation_data.action.name == 'Eris_Idle')
    check('Source and final files remained byte-identical during read-only validation', file_sha(source_path) == hashes['source'] and file_sha(final_path) == hashes['final'])
    result = {'source': source_path.name, 'source_sha256': hashes['source'], 'final': final_path.name, 'final_sha256': hashes['final'],
              'checks': checks, 'passed': all(item['passed'] for item in checks), 'unity_play_verified': False}
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(result, indent=2, allow_nan=False) + '\n', encoding='utf8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True)
    parser.add_argument('--final', required=True)
    parser.add_argument('--rig-report', required=True)
    parser.add_argument('--report', required=True)
    arguments = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    result = validate_character(arguments.source, arguments.final, arguments.rig_report, arguments.report)
    print('ANIMATED_CHARACTER_VALIDATION=' + json.dumps({'passed': result['passed'],
          'checks': [{'name': item['name'], 'passed': item['passed']} for item in result['checks']]}))
    if not result['passed']:
        raise SystemExit(1)
