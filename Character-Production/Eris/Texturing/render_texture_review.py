"""Render comparable character/material views in a separate Blender process.

blender --background INPUT.blend --python render_texture_review.py -- --output-dir Review --prefix before
This script never saves or modifies the input file on disk.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector


def render_review(output_dir, prefix, views=None):
    output_dir = Path(output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    source = Path(bpy.data.filepath)
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    scene = bpy.context.scene
    scene.frame_set(1)
    for obj in scene.objects:
        if obj.type == 'MESH' and obj.name not in {'Head', 'Hair'}:
            obj.hide_render = True
        elif obj.type == 'LIGHT':
            obj.hide_render = True
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 4
    scene.render.resolution_x = 640
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.exposure = -.5
    world = bpy.data.worlds.new('TextureReviewWorld')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (.66, .70, .76, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = .45
    scene.world = world
    for name, location, energy, size in [
            ('Key', (-1.3, -2.2, 2.5), 55, 2.0),
            ('Fill', (1.3, -1.0, 1.5), 22, 1.8),
            ('Rim', (.4, 1.5, 1.8), 35, 1.5)]:
        data = bpy.data.lights.new('TextureReview' + name, 'AREA')
        data.energy, data.size = energy, size
        obj = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(obj)
        obj.location = location
        obj.rotation_euler = (Vector((0, 0, .55)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data = bpy.data.cameras.new('TextureReviewCamera')
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    camera_data.type = 'ORTHO'
    scene.camera = camera
    definitions = {
        'front': ((.35, -3, .88), (0, -.02, .51), 1.13),
        'back': ((-.35, 3, .88), (0, .02, .51), 1.13),
        'face': ((.15, -3, .87), (0, -.06, .79), .43),
        'costume': ((.15, -3, .67), (0, -.02, .46), .64),
    }
    images = []
    for name in (views or definitions):
        location, target, scale = definitions[name]
        camera.location = location
        camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera_data.ortho_scale = scale
        filename = prefix + '-' + name + '.png'
        scene.render.filepath = str(output_dir / filename)
        bpy.ops.render.render(write_still=True)
        images.append({'view': name, 'file': filename, 'camera': location, 'target': target, 'ortho_scale': scale})
    assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash
    report = {'source': source.name, 'source_sha256': source_hash, 'source_preserved': True,
              'frame': 1, 'renderer': 'Cycles CPU', 'samples': 24,
              'view_transform': 'Standard', 'exposure': -.5, 'images': images}
    (output_dir / (prefix + '-render-verification.json')).write_text(json.dumps(report, indent=2) + '\n', encoding='utf8', newline='\n')
    print('TEXTURE_REVIEW=' + json.dumps({'prefix': prefix, 'images': len(images)}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', required=True)
    parser.add_argument('--prefix', required=True)
    parser.add_argument('--views', nargs='+', choices=['front', 'back', 'face', 'costume'])
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    render_review(args.output_dir, args.prefix, args.views)
