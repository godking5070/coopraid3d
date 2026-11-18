"""Add bright Eris motions to an already repaired character; save a NEW copy.

blender --background FIXED_SOURCE.blend --python build_bright_character.py -- \
  --output NEW.blend [--manifest clips.json] [--report-dir REPORT_FOLDER]

The existing Idle/Walk/Run actions survive as *_Calm. This never reapplies the
clothing patch or changes skin weights. Independent reopen validation is handled
by validate_bright_character.py with ranges from motion-verification.json.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent))
from apply_mixamo_animations import apply_mixamo_animations, channels_for, static_fingerprint
from validate_animated_character import action_data, digest, material_data, mesh_geometry


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def action_signature(action):
    data = action_data(action)
    return {name: data[name] for name in ('hash', 'range', 'slots')}


def static_signature(armature):
    return {
        'rig_and_skin': static_fingerprint(armature),
        'mesh_geometry': {obj.name: mesh_geometry(obj) for obj in bpy.context.scene.objects if obj.type == 'MESH'},
        'mesh_transforms': {obj.name: [list(row) for row in obj.matrix_world]
                            for obj in bpy.context.scene.objects if obj.type == 'MESH'},
        'materials': {material.name: material_data(material) for material in bpy.data.materials},
        'hair_pose': {bone.name: [list(row) for row in bone.matrix_basis]
                      for bone in armature.pose.bones if bone.name.startswith('Hair_')},
    }


def active_images():
    images = set()
    for name in ('Head', 'Hair'):
        for slot in bpy.data.objects[name].material_slots:
            if slot.material and slot.material.node_tree:
                images.update(node.image for node in slot.material.node_tree.nodes
                              if node.type == 'TEX_IMAGE' and node.image)
    if not images:
        raise ValueError('The visible character has no image texture.')
    return images


def image_digest(image):
    if image.packed_file:
        return hashlib.sha256(image.packed_file.data).hexdigest()
    return sha(bpy.path.abspath(image.filepath))


def check_action(action, expected_frames, expected_bones):
    curves = list(channels_for(action, action.slots[0]))
    keyed = set()
    finite = True
    bind_translation_scale = True
    loop_error = 0.0
    for curve in curves:
        matched = re.fullmatch(r'pose\.bones\["([^"]+)"\]\.(location|rotation_quaternion|scale)', curve.data_path)
        if not matched:
            raise ValueError('Unexpected animation channel: ' + curve.data_path)
        name, property_name = matched.groups()
        keyed.add(name)
        values = [key.co.y for key in curve.keyframe_points]
        finite &= bool(values) and all(math.isfinite(value) for value in values)
        if property_name == 'location' and name != 'mixamorig:Hips':
            bind_translation_scale &= all(abs(value) < 1e-6 for value in values)
        if property_name == 'scale':
            bind_translation_scale &= all(abs(value - 1) < 1e-6 for value in values)
        if values:
            loop_error = max(loop_error, abs(values[0] - values[-1]))
    result = {'action': action.name, 'frames': list(action.frame_range), 'curves': len(curves),
              'keyed_bones': len(keyed), 'hair_bones_keyed': len([name for name in keyed if name.startswith('Hair_')]),
              'finite_keys': bool(finite), 'nonroot_translation_scale_at_bind': bool(bind_translation_scale),
              'loop_endpoint_error': loop_error}
    result['passed'] = (result['frames'] == [1, expected_frames] and keyed == expected_bones
                        and finite and bind_translation_scale and loop_error < 1e-6)
    return result


def prepare_view(armature):
    bpy.ops.object.select_all(action='DESELECT')
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    layout = bpy.data.workspaces.get('Layout')
    if layout:
        for window in bpy.context.window_manager.windows:
            window.workspace = layout
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'CONSOLE':
                area.type = 'VIEW_3D'
            if area.type == 'DOPESHEET_EDITOR':
                space = next((space for space in area.spaces if space.type == 'DOPESHEET_EDITOR'), None)
                if space and space.mode == 'TIMELINE':
                    space.mode = 'ACTION'
            if area.type == 'VIEW_3D':
                space = next((space for space in area.spaces if space.type == 'VIEW_3D'), None)
                if space is None:
                    continue
                space.shading.type = 'SOLID'
                space.shading.light = 'STUDIO'
                space.shading.color_type = 'TEXTURE'
                space.overlay.show_relationship_lines = False
                space.overlay.show_extras = False
                space.region_3d.view_perspective = 'ORTHO'
                space.region_3d.view_location = Vector((0, -.02, .53))
                space.region_3d.view_distance = 1.9
                space.region_3d.view_rotation = Vector((1.2, -3, .5)).to_track_quat('Z', 'Y')


def build_bright(output, manifest_path=None, report_dir=None):
    output = Path(output).resolve()
    manifest_path = Path(manifest_path or ROOT / 'clips.json').resolve()
    report_dir = Path(report_dir or output.parent).resolve()
    source = Path(bpy.data.filepath).resolve()
    if not source.is_file() or output.exists() or output == source:
        raise ValueError('Load an existing source and choose a new, nonexistent output .blend path.')
    if output.suffix.lower() != '.blend':
        raise ValueError('The output must be a new .blend file.')
    source_hash = sha(source)
    armature = bpy.data.objects['Armature']
    if len(armature.data.bones) != 74:
        raise ValueError('Expected the already repaired 74-bone Eris character.')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    manifest_semantic_hash = digest(manifest)
    if {clip['name'] for clip in manifest['clips']} != {'Idle', 'Walk', 'Run'} or manifest.get('active', 'Idle') != 'Idle':
        raise ValueError('Expected exactly Idle, Walk and Run, with Idle active.')
    for clip in manifest['clips']:
        candidate = Path(clip['path'])
        clip['path'] = str(candidate if candidate.is_absolute() else manifest_path.parent / candidate)
        if not Path(clip['path']).is_file():
            raise FileNotFoundError(clip['path'])
    renames = {'Eris_' + name: 'Eris_' + name + '_Calm' for name in ('Idle', 'Walk', 'Run')}
    if any(name not in bpy.data.actions or destination in bpy.data.actions for name, destination in renames.items()):
        raise ValueError('Expected the original three actions and no existing *_Calm names.')
    static_before = static_signature(armature)
    actions_before = {renames.get(action.name, action.name): action_signature(action) for action in bpy.data.actions}
    images_before = {image.name: image_digest(image) for image in active_images()}
    for name, destination in renames.items():
        bpy.data.actions[name].name = destination
    for action in bpy.data.actions:
        action.use_fake_user = True
    scene = bpy.context.scene
    scene.render.fps, scene.render.fps_base = 30, 1.0
    armature.animation_data.use_nla = False
    report_dir.mkdir(parents=True, exist_ok=True)
    motions = apply_mixamo_animations(manifest['clips'], active='Idle', report_path=report_dir / 'motion-verification.json')
    if not motions['passed']:
        raise AssertionError('Motion transfer validation failed.')
    for image in active_images():
        if not image.packed_file:
            image.pack()
    for material in bpy.data.materials:
        material.use_fake_user = True
    checks = {
        'static_rig_skin_geometry_uv_materials_hair_pose_exact': static_signature(armature) == static_before,
        'all_original_action_curves_ranges_slots_preserved': all(
            name in bpy.data.actions and action_signature(bpy.data.actions[name]) == signature
            for name, signature in actions_before.items()),
        'active_texture_bytes_exact_and_packed': all(image.packed_file and image_digest(image) == images_before[image.name]
                                                    for image in active_images()),
    }
    expected_bones = {bone.name for bone in armature.data.bones if not bone.name.startswith('Hair_')}
    action_checks = [check_action(bpy.data.actions[item['action']], item['frames'], expected_bones) for item in motions['clips']]
    checks['three_new_loop_actions_no_hair_keys_or_joint_stretch'] = all(item['passed'] for item in action_checks)
    if not all(checks.values()):
        raise AssertionError('Bright build preservation checks failed: ' + str(checks))
    prepare_view(armature)
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
    checks['source_file_preserved'] = sha(source) == source_hash
    report = {'source': source.name, 'source_sha256': source_hash, 'output': output.name,
              'output_sha256': sha(output), 'manifest': manifest_path.name, 'manifest_semantic_sha256': manifest_semantic_hash,
              'personality_basis': manifest.get('personality_basis'), 'renamed_original_actions': renames,
              'checks': checks, 'actions': action_checks, 'bones': 74, 'skin_weights_changed': False,
              'hair_secondary_motion_added': False, 'unity_play_verified': False,
              'passed': all(checks.values())}
    (report_dir / 'build-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    if not report['passed']:
        raise AssertionError('The original source changed during the build.')
    print('ERIS_BRIGHT_BUILD=' + json.dumps({'passed': report['passed'], 'output': output.name,
          'sha256': report['output_sha256'], 'frames': {item['action']: item['frames'] for item in action_checks}}))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    parser.add_argument('--manifest')
    parser.add_argument('--report-dir')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    build_bright(args.output, args.manifest, args.report_dir)
