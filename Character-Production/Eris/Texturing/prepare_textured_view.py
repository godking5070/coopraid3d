"""Prepare the newly generated textured scene for viewing, with a checksum guard.

Only the generated output named in texture-build-verification.json is saved.
The pre-view file is retained in the workspace's ignored diagnostics folder.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys

import bpy
from mathutils import Vector


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def prepare(report_path):
    report_path = Path(report_path).resolve()
    report = json.loads(report_path.read_text(encoding='utf8'))
    output = report_path.parent / report['output']
    if Path(bpy.data.filepath).resolve() != output.resolve() or sha(output) != report['output_sha256']:
        raise ValueError('The generated file differs from the build record; refusing to save it.')
    if report.get('view_prepared'):
        print('TEXTURED_VIEW_ALREADY_PREPARED')
        return
    previous_hash = sha(output)
    workspace = Path(__file__).resolve().parents[3]
    backup = workspace / '.utmp/eris-texturing-pipeline/pre-view-preparation.blend'
    backup.parent.mkdir(parents=True, exist_ok=True)
    if backup.exists():
        raise FileExistsError(backup)
    shutil.copy2(output, backup)
    assert sha(backup) == previous_hash
    for name in ('Head', 'Hair'):
        material = bpy.data.objects[name].material_slots[0].material
        for node in material.node_tree.nodes:
            node.select = False
        image = next(node for node in material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.label == 'BaseColor')
        image.select = True
        material.node_tree.nodes.active = image
    bpy.ops.object.select_all(action='DESELECT')
    armature = bpy.data.objects['Armature']
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
            if area.type == 'VIEW_3D':
                space = next((space for space in area.spaces if space.type == 'VIEW_3D'), None)
                if space:
                    space.shading.type = 'SOLID'
                    space.shading.color_type = 'TEXTURE'
                    space.overlay.show_overlays = False
                    space.region_3d.view_location = Vector((0, -.02, .52))
                    space.region_3d.view_distance = 1.65
                    space.region_3d.view_perspective = 'ORTHO'
                    space.region_3d.view_rotation = Vector((.35, -3, .37)).to_track_quat('Z', 'Y')
            elif area.type == 'DOPESHEET_EDITOR':
                space = next((space for space in area.spaces if space.type == 'DOPESHEET_EDITOR'), None)
                if space and space.mode == 'TIMELINE':
                    space.mode = 'ACTION'
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
    report['output_sha256'] = sha(output)
    report['view_prepared'] = {'albedo_nodes_active': True, 'selected': 'Armature',
                               'workspace': 'Layout', 'pre_view_sha256': previous_hash}
    report_path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf8', newline='\n')
    print('TEXTURED_VIEW_PREPARED=' + json.dumps({'output': output.name, 'sha256': report['output_sha256']}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    prepare(args.report)
