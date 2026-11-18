"""Repair Eris's non-finite FBX texture mapping on a material copy.

Use after hair-rig generation. Only the active Head/body and Hair material slots
are reassigned; source materials, UVs, image pixels, geometry and rigs are retained.
"""
import argparse
import json
import math
from pathlib import Path

import bpy


def repair_texture_mapping():
    copies, changes = {}, []
    for name in ('Head','Hair'):
        obj = bpy.data.objects.get(name)
        if obj is None or obj.type != 'MESH':
            raise ValueError('Expected the rigged Eris mesh: ' + name)
        if obj.data.users > 1:
            obj.data = obj.data.copy()
        for slot in obj.material_slots:
            material = slot.material
            if not material or not material.node_tree:
                continue
            bad = [node for node in material.node_tree.nodes if node.type == 'MAPPING'
                   and any(not math.isfinite(value) for value in node.inputs['Scale'].default_value)]
            if not bad:
                continue
            if material.as_pointer() not in copies:
                material.use_fake_user = True
                clone = material.copy()
                clone.name = 'Eris_Textured'
                edits = []
                for node in clone.node_tree.nodes:
                    if node.type == 'MAPPING':
                        before = list(node.inputs['Scale'].default_value)
                        after = [value if math.isfinite(value) else 1.0 for value in before]
                        if before != after:
                            node.inputs['Scale'].default_value = after
                            edits.append({'node':node.name,
                                'before':[value if math.isfinite(value) else str(value) for value in before],
                                'after':after})
                copies[material.as_pointer()] = clone
                changes.append({'source_material':material.name,'new_material':clone.name,'edits':edits})
            slot.material = copies[material.as_pointer()]
    bpy.context.view_layer.update()
    return {'changed':bool(changes),'changes':changes,'original_materials_retained':True,
            'uvs_image_pixels_geometry_and_rig_unchanged':True}


def main():
    import sys
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    output = Path(args.output).resolve()
    if output.exists() or (bpy.data.filepath and output == Path(bpy.data.filepath).resolve()):
        raise ValueError('Save only to a new output path.')
    report = repair_texture_mapping()
    output.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output),copy=True,check_existing=False)
    print('ERIS_MAPPING_REPAIR=' + json.dumps(report))


if __name__ == '__main__':
    main()
