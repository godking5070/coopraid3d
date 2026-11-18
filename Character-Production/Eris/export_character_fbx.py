"""Export the already-rigged Eris character, preserving the scene and hiding no assets.

Run on a separate Blender process with the completed .blend loaded:
  blender --background --factory-startup Eris_HairRig.blend --python export_character_fbx.py -- --output Eris_HairRig.fbx
Only Armature, Head (the full body) and Hair are included. Hidden source duplicates are excluded.
"""
import argparse
import hashlib
import json
from pathlib import Path

import bpy
from io_scene_fbx import parse_fbx


def export_character(output):
    output = Path(output).resolve()
    if output.suffix.lower() != '.fbx':
        raise ValueError('Expected a new .fbx output path.')
    if output.exists():
        raise FileExistsError('Refusing to replace an existing export: ' + str(output))
    objects = [bpy.data.objects.get(name) for name in ('Armature', 'Head', 'Hair')]
    if any(obj is None for obj in objects) or [obj.type for obj in objects] != ['ARMATURE','MESH','MESH']:
        raise ValueError('Expected the Mixamo Armature and its Head/body + Hair meshes.')
    armature, body, hair = objects
    original_bones = [bone.name for bone in armature.data.bones if bone.name.startswith('mixamorig:')]
    hair_bones = [bone.name for bone in armature.data.bones if bone.name.startswith('Hair_')]
    if len(original_bones) != 52 or not hair_bones:
        raise ValueError('The complete Mixamo skeleton and extra hair bones must be present.')
    images = {}
    for obj in (body, hair):
        for slot in obj.material_slots:
            material = slot.material
            if material and material.use_nodes:
                for node in material.node_tree.nodes:
                    if node.type == 'TEX_IMAGE' and node.image:
                        image = node.image
                        # A freshly opened .blend decodes images lazily; packed bytes
                        # or an existing source file are sufficient for FBX embedding.
                        if not image.has_data and not image.packed_file and not Path(bpy.path.abspath(image.filepath)).is_file():
                            raise ValueError('An active material image has no pixel data: ' + image.name)
                        images[image.name] = image
    if not images:
        raise ValueError('No active image textures were found.')
    previous_active = bpy.context.view_layer.objects.active
    previous_selected = list(bpy.context.selected_objects)
    try:
        if bpy.context.object and bpy.context.object.mode != 'OBJECT':
            raise ValueError('Export from Object Mode so the original edit/pose state is not changed.')
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objects:
            if obj.hide_get() or obj.hide_viewport:
                raise ValueError('The intended character must be visible before exporting: ' + obj.name)
            obj.select_set(True)
        bpy.context.view_layer.objects.active = armature
        output.parent.mkdir(parents=True, exist_ok=True)
        result = bpy.ops.export_scene.fbx(filepath=str(output), check_existing=False,
            use_selection=True, object_types={'ARMATURE','MESH'}, global_scale=1.0,
            apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
            use_space_transform=True, bake_space_transform=False,
            axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True,
            mesh_smooth_type='FACE', use_armature_deform_only=False,
            add_leaf_bones=False, bake_anim=False, path_mode='COPY',
            embed_textures=True, use_custom_props=True)
        if result != {'FINISHED'}:
            raise RuntimeError('FBX exporter did not finish: ' + repr(result))
    finally:
        bpy.ops.object.select_all(action='DESELECT')
        for obj in previous_selected:
            if obj.name in bpy.context.view_layer.objects:
                obj.select_set(True)
        bpy.context.view_layer.objects.active = previous_active

    root, version = parse_fbx.parse(str(output))
    objects_node = next(node for node in root.elems if node.id == b'Objects')
    videos = []
    for video in (node for node in objects_node.elems if node.id == b'Video'):
        content = next((item for item in video.elems if item.id == b'Content'), None)
        payload = content.props[0] if content and content.props else b''
        videos.append({'bytes':len(payload),'sha256':hashlib.sha256(payload).hexdigest() if payload else None})
    if not videos or any(not item['bytes'] for item in videos):
        raise AssertionError('Texture image data was not embedded in the FBX.')
    report = {'output':output.name,'bytes':output.stat().st_size,
        'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'fbx_version':version,
        'objects':['Armature','Head','Hair'],'mixamo_bones':len(original_bones),
        'hair_bones':len(hair_bones),'embedded_images':videos,
        'leaf_bones_added':False,'animation_baked':False,
        'source_scene_saved_by_export':False,'unity_import_verified':False}
    output.with_suffix('.export.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf8')
    print('ERIS_FBX_EXPORT=' + json.dumps(report))
    return report


def main():
    import sys
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    export_character(args.output)


if __name__ == '__main__':
    main()
