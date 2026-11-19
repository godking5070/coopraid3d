"""Bake concept-directed materials onto the existing Iris/Eris UVs.

Load the repaired Bright .blend in Blender 5.3, then run this script with:
  --manifest material-spec.json --output NEW.blend --mode preview|bake
Preview writes two CPU renders and a report using temporary mesh copies only.
Bake writes separate body and hair/hood atlases, replaces only material slot 0,
and saves a new .blend. No source geometry, UVs, weights, rig or Actions change.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent / 'Animation'))
from validate_animated_character import action_data, material_data


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()


def rgb(value):
    if isinstance(value, str):
        value = value.lstrip('#')
        value = [int(value[index:index + 2], 16) / 255 for index in (0, 2, 4)]
    return tuple(channel / 12.92 if channel <= .04045 else ((channel + .055) / 1.055) ** 2.4 for channel in value[:3]) + (1,)


def geometry_state():
    result = {}
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH':
            continue
        groups = {group.index: group.name for group in obj.vertex_groups}
        result[obj.name] = digest({'positions': [list(vertex.co) for vertex in obj.data.vertices],
            'edges': [list(edge.vertices) for edge in obj.data.edges],
            'faces': [(list(face.vertices), face.material_index, face.use_smooth) for face in obj.data.polygons],
            'uv': [(layer.name, [list(loop.uv) for loop in layer.data]) for layer in obj.data.uv_layers],
            'weights': [[(groups[group.group], group.weight) for group in vertex.groups] for vertex in obj.data.vertices],
            'world': [list(row) for row in obj.matrix_world], 'parent': obj.parent.name if obj.parent else None,
            'modifiers': [(mod.name, mod.type, mod.object.name if hasattr(mod, 'object') and mod.object else None) for mod in obj.modifiers]})
    armature = bpy.data.objects['Armature']
    return {'meshes': result, 'bones': digest([(bone.name, bone.parent.name if bone.parent else None,
            [list(row) for row in bone.matrix_local], list(bone.head_local), list(bone.tail_local)) for bone in armature.data.bones]),
            'hair_pose': digest([(bone.name, [list(row) for row in bone.matrix_basis]) for bone in armature.pose.bones if bone.name.startswith('Hair_')]),
            'actions': {action.name: action_data(action)['hash'] for action in bpy.data.actions},
            'armature_world': [list(row) for row in armature.matrix_world]}


class Graph:
    def __init__(self, material):
        self.nodes, self.links = material.node_tree.nodes, material.node_tree.links

    def node(self, kind, **properties):
        node = self.nodes.new(kind)
        for key, value in properties.items():
            setattr(node, key, value)
        return node

    def put(self, value, socket):
        if hasattr(value, 'node'):
            self.links.new(value, socket)
        else:
            if socket.type == 'RGBA' and isinstance(value, (int, float)):
                value = (value, value, value, 1)
            socket.default_value = value

    def math(self, operation, a, b=0):
        node = self.node('ShaderNodeMath', operation=operation)
        self.put(a, node.inputs[0]); self.put(b, node.inputs[1])
        return node.outputs[0]

    def ramp(self, value, low, high):
        node = self.node('ShaderNodeMapRange', interpolation_type='SMOOTHSTEP', clamp=True)
        self.put(value, node.inputs['Value'])
        node.inputs['From Min'].default_value = low; node.inputs['From Max'].default_value = high
        return node.outputs[0]

    def mix(self, factor, a, b, blend='MIX'):
        node = self.node('ShaderNodeMixRGB', blend_type=blend)
        self.put(factor, node.inputs[0]); self.put(a, node.inputs[1]); self.put(b, node.inputs[2])
        return node.outputs[0]

    def recolor(self, color, target, strength, gain=1):
        source_luma = self.node('ShaderNodeRGBToBW'); self.put(color, source_luma.inputs[0])
        target_luma = self.node('ShaderNodeRGBToBW'); self.put(target, target_luma.inputs[0])
        scale = self.math('DIVIDE', self.math('MULTIPLY', source_luma.outputs[0], gain), self.math('MAXIMUM', target_luma.outputs[0], .005))
        tinted = self.mix(1, target, scale, 'MULTIPLY')
        return self.mix(strength, color, tinted)


def source_image(obj):
    material = obj.material_slots[0].material
    images = {node.image for node in material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image}
    if len(images) != 1:
        raise ValueError('Expected one original packed base-color image on ' + obj.name)
    return next(iter(images))


def procedural_material(name, original_image, spec, detail, hair=False):
    material = bpy.data.materials.new(name); material.use_nodes = True
    material.node_tree.nodes.clear(); g = Graph(material)
    output = g.node('ShaderNodeOutputMaterial')
    shader = g.node('ShaderNodeBsdfPrincipled')
    g.links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    uv = g.node('ShaderNodeUVMap', uv_map='Attribute')
    image = g.node('ShaderNodeTexImage', image=original_image, interpolation='Linear', extension='REPEAT')
    g.links.new(uv.outputs['UV'], image.inputs['Vector']); color = image.outputs['Color']
    separate = g.node('ShaderNodeSeparateColor', mode='RGB'); g.put(color, separate.inputs[0])
    red, green, blue = separate.outputs[:3]
    hair_mask = 0.0
    if hair:
        attribute = g.node('ShaderNodeAttribute', attribute_name='_IrisHairWeight')
        hair_mask = attribute.outputs['Fac']
    luma = g.node('ShaderNodeRGBToBW'); g.put(color, luma.inputs[0])
    rg, gb = g.math('SUBTRACT', red, green), g.math('SUBTRACT', green, blue)
    skin = g.ramp(g.math('SUBTRACT', rg, g.math('MULTIPLY', gb, 1.5)), .005, .05)
    # The ratio alone also accepts cool navy (both differences negative).
    # Restrict the chromatic heuristic to warm peach hues before anatomical
    # protection is combined below; blue fabric must remain eligible to tint.
    skin = g.math('MULTIPLY', skin, g.math('MULTIPLY', g.ramp(rg, 0, .01), g.ramp(gb, 0, .01)))
    if not hair:
        skin_region = g.node('ShaderNodeAttribute', attribute_name='_IrisSkinProtect')
        skin = g.math('MAXIMUM', skin, skin_region.outputs['Fac'])
    navy = g.math('MULTIPLY', g.ramp(g.math('SUBTRACT', blue, red), .01, .06), g.math('SUBTRACT', 1, g.ramp(luma.outputs[0], .15, .28)))
    gold = g.math('MULTIPLY', g.ramp(g.math('MINIMUM', rg, gb), .025, .115), g.math('SUBTRACT', 1, skin))
    if hair:
        # Warm brown hair shadows can resemble gold in RGB. Existing hair-bone
        # influence is the semantic exclusion for every metallic/gold branch.
        gold = g.math('MULTIPLY', gold, g.math('SUBTRACT', 1, g.ramp(hair_mask, .01, .10)))
        allowed = g.node('ShaderNodeAttribute', attribute_name='_IrisGoldAllowed')
        gold = g.math('MULTIPLY', gold, allowed.outputs['Fac'])
    ivory = g.math('MULTIPLY', g.ramp(luma.outputs[0], .25, .65), g.math('SUBTRACT', 1, g.math('MAXIMUM', navy, gold)))
    position = g.node('ShaderNodeNewGeometry')
    xyz = g.node('ShaderNodeSeparateXYZ'); g.put(position.outputs['Position'], xyz.inputs[0])
    head = g.ramp(xyz.outputs['Z'], spec.get('head_albedo_protect_z', .66) - .01, spec.get('head_albedo_protect_z', .66))
    protected = g.math('MAXIMUM', skin, head) if not hair else skin
    palette = spec.get('palette', {}); strengths = spec.get('strengths', {})
    polished = color
    for label, mask, default, gain in [('ivory', ivory, '#fff4e3', 1.025), ('navy', navy, '#344c78', .85), ('gold', gold, '#e6bb68', 1.06)]:
        amount = g.math('MULTIPLY', mask, strengths.get(label, .35))
        polished = g.recolor(polished, rgb(palette.get(label, default)), amount, gain)
    polished = g.mix(protected, polished, color)
    if hair:
        low, high = spec.get('hair_gradient_world_z', [.42, .83])
        height = g.ramp(xyz.outputs['Z'], low, high)
        target = g.mix(height, rgb(palette.get('hair_tip', '#f5bdba')), rgb(palette.get('hair_top', '#fff1d9')))
        polished = g.recolor(polished, target, g.math('MULTIPLY', hair_mask, strengths.get('hair', .35)), 1.04)
    roughness = spec.get('roughness', {})
    rough = g.mix(gold, roughness.get('cloth', .69), roughness.get('gold', .30))
    rough = g.mix(skin, rough, roughness.get('skin', .48))
    if not hair:
        rough = g.mix(g.math('MULTIPLY', head, navy), rough, roughness.get('eye', .18))
    else:
        rough = g.mix(hair_mask, rough, roughness.get('hair', .38))
    metal = g.math('MULTIPLY', gold, spec.get('metallic_gold', .75))
    if not hair:
        metal = g.math('MULTIPLY', metal, g.math('SUBTRACT', 1, head))
    texcoord = g.node('ShaderNodeTexCoord')
    mapping = g.node('ShaderNodeVectorMath', operation='SCALE'); g.put(texcoord.outputs['Generated'], mapping.inputs[0]); mapping.inputs['Scale'].default_value = spec.get('detail_scale', 20)
    weave = g.node('ShaderNodeTexImage', image=detail, projection='BOX', projection_blend=.2, extension='REPEAT')
    g.links.new(mapping.outputs[0], weave.inputs['Vector'])
    cloth = g.math('MULTIPLY', g.math('SUBTRACT', 1, skin), g.math('SUBTRACT', 1, gold))
    if hair:
        cloth = g.math('MULTIPLY', cloth, g.math('SUBTRACT', 1, hair_mask))
    detail_strength = spec.get('detail_strength', .025)
    detail_value = g.math('MULTIPLY', g.math('SUBTRACT', weave.outputs['Color'], .5), g.math('MULTIPLY', cloth, detail_strength))
    rough = g.math('ADD', rough, detail_value)
    bump = g.node('ShaderNodeBump'); bump.inputs['Strength'].default_value = .06
    bump.inputs['Distance'].default_value = .00008
    g.put(g.math('MULTIPLY', weave.outputs['Color'], cloth), bump.inputs['Height'])
    g.put(polished, shader.inputs['Base Color']); g.put(rough, shader.inputs['Roughness']); g.put(metal, shader.inputs['Metallic'])
    g.links.new(bump.outputs['Normal'], shader.inputs['Normal'])
    shader.inputs['Alpha'].default_value = 1
    return material, {'BaseColor': polished, 'Roughness': rough, 'Metallic': metal}, output


def temporary_copy(source, material, keep_modifiers):
    obj = source.copy(); obj.data = source.data.copy(); obj.name = '_IrisTextureWork_' + source.name
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.clear(); obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.material_index = 0
    if not keep_modifiers:
        obj.modifiers.clear()
    obj.hide_render = False; obj.hide_viewport = False; obj.hide_set(False)
    if source.name == 'Hair':
        group_names = {group.index: group.name for group in source.vertex_groups}
        weights = [min(1, sum(group.weight for group in vertex.groups if group_names[group.group].startswith('Hair_'))) for vertex in source.data.vertices]
        attribute = obj.data.attributes.new('_IrisHairWeight', 'FLOAT', 'POINT')
        attribute.data.foreach_set('value', weights)
        parent = list(range(len(source.data.vertices)))
        def find(index):
            while parent[index] != index:
                parent[index] = parent[parent[index]]; index = parent[index]
            return index
        for edge in source.data.edges:
            a, b = map(find, edge.vertices)
            if a != b:
                parent[b] = a
        # The 8005-vertex fused hood/hair island contains fixed bangs without
        # Hair_* weights. Warm pixel colors alone cannot classify them as metal.
        # Only independently inspected, disconnected flower/ornament islands
        # may enter the gold branch; painted hood trim remains nonmetal.
        ornaments = {find(seed) for seed in (1405, 1369, 1867, 10388, 10373, 1861)}
        attribute = obj.data.attributes.new('_IrisGoldAllowed', 'FLOAT', 'POINT')
        attribute.data.foreach_set('value', [1.0 if find(vertex.index) in ornaments else 0.0 for vertex in source.data.vertices])
    elif source.name == 'Head':
        parent = list(range(len(source.data.vertices)))
        def find(index):
            while parent[index] != index:
                parent[index] = parent[parent[index]]; index = parent[index]
            return index
        for edge in source.data.edges:
            a, b = map(find, edge.vertices)
            if a != b:
                parent[b] = a
        hands = {find(5798), find(750)}; legs = find(518)
        protected = [1.0 if find(vertex.index) in hands or (find(vertex.index) == legs
                     and .197 < (source.matrix_world @ vertex.co).z < .457) else 0.0
                     for vertex in source.data.vertices]
        attribute = obj.data.attributes.new('_IrisSkinProtect', 'FLOAT', 'POINT')
        attribute.data.foreach_set('value', protected)
    return obj


def image_target(source, role, resolution, path):
    image = bpy.data.images.new(path.stem, width=resolution, height=resolution, alpha=role == 'BaseColor')
    image.colorspace_settings.name = 'sRGB' if role == 'BaseColor' else 'Non-Color'
    if role == 'BaseColor':
        # Do not Image.copy() a packed FILE image: its inherited packed JPEG
        # can be saved instead of the modified bake buffer (and undo scaling).
        # A fresh GENERATED buffer has no packed/file cache to shadow the bake.
        width, height = map(int, source.size)
        pixels = np.empty(width * height * 4, dtype=np.float32)
        source.pixels.foreach_get(pixels)
        if (width, height) != (resolution, resolution):
            resized = bpy.data.images.new('_IrisSourceResize', width=width, height=height, alpha=True)
            resized.colorspace_settings.name = 'sRGB'
            resized.pixels.foreach_set(pixels); resized.scale(resolution, resolution)
            pixels = np.empty(resolution * resolution * 4, dtype=np.float32)
            resized.pixels.foreach_get(pixels); bpy.data.images.remove(resized)
        image.pixels.foreach_set(pixels)
        del pixels
        image.update()
    else:
        image.generated_color = (.5, .5, 1, 1) if role == 'Normal' else ((.68, .68, .68, 1) if role == 'Roughness' else (0, 0, 0, 1))
    image.file_format = 'PNG'; image.filepath_raw = str(path)
    return image


def bake_maps(obj, material, signals, output_node, source, spec, directory, prefix):
    resolution = int(spec.get('resolution', 4096)); maps = {}
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 1
    scene.render.threads_mode = 'FIXED'; scene.render.threads = int(spec.get('bake_threads', 2))
    scene.render.bake.margin = int(spec.get('bake_margin', 2)); scene.render.bake.use_clear = False
    scene.render.bake.normal_space = 'TANGENT'
    directory.mkdir(parents=True, exist_ok=True)
    g = Graph(material); emission = g.node('ShaderNodeEmission')
    surface_link = next(link for link in material.node_tree.links if link.to_node == output_node and link.to_socket.name == 'Surface')
    original_surface = surface_link.from_socket
    target = g.node('ShaderNodeTexImage')
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active = obj
    for role in ('BaseColor', 'Roughness', 'Metallic', 'Normal'):
        path = directory / (prefix + '_' + role + '.png')
        if path.exists():
            raise FileExistsError(path)
        image = image_target(source, role, resolution, path); target.image = image
        for node in material.node_tree.nodes:
            node.select = False
        target.select = True; material.node_tree.nodes.active = target
        if role == 'Normal':
            g.put(original_surface, output_node.inputs['Surface'])
            bpy.ops.object.bake(type='NORMAL')
        else:
            g.put(signals[role], emission.inputs['Color']); g.put(emission.outputs[0], output_node.inputs['Surface'])
            bpy.ops.object.bake(type='EMIT')
        image.save()
        # Reload saved PNG bytes as FILE: packing GENERATED buffers does not
        # reliably survive a fresh .blend reopen in Blender 5.3.
        texture = bpy.data.images.load(str(path), check_existing=False)
        if tuple(texture.size) != (resolution, resolution):
            raise AssertionError('Saved bake dimensions differ from the requested target: ' + str(path))
        texture.colorspace_settings.name = 'sRGB' if role == 'BaseColor' else 'Non-Color'
        texture.pack(); target.image = None
        maps[role] = {'image': texture.name, 'path': str(path), 'width': resolution, 'height': resolution, 'sha256': sha(path), 'colorspace': texture.colorspace_settings.name}
        bpy.data.images.remove(image); texture.buffers_free()
        print('IRIS_MAP_BAKED=' + json.dumps({'object': obj.name, 'role': role, 'path': str(path)}), flush=True)
    g.put(original_surface, output_node.inputs['Surface'])
    return maps


def baked_material(name, maps):
    material = bpy.data.materials.new(name); material.use_nodes = True
    material.node_tree.nodes.clear(); g = Graph(material)
    output = g.node('ShaderNodeOutputMaterial'); shader = g.node('ShaderNodeBsdfPrincipled')
    g.links.new(shader.outputs[0], output.inputs['Surface'])
    uv = g.node('ShaderNodeUVMap', uv_map='Attribute')
    for role, record in maps.items():
        image = bpy.data.images.get(record['image'])
        node = g.node('ShaderNodeTexImage', image=image, label=role)
        g.links.new(uv.outputs[0], node.inputs['Vector'])
        if role == 'Normal':
            normal = g.node('ShaderNodeNormalMap', space='TANGENT', uv_map='Attribute')
            g.links.new(node.outputs['Color'], normal.inputs['Color']); g.links.new(normal.outputs[0], shader.inputs['Normal'])
        else:
            g.links.new(node.outputs['Color'], shader.inputs[{'BaseColor': 'Base Color', 'Roughness': 'Roughness', 'Metallic': 'Metallic'}[role]])
    shader.inputs['Alpha'].default_value = 1
    return material


def render_preview(objects, output):
    scene = bpy.context.scene
    for obj in scene.objects:
        if obj.type == 'MESH':
            obj.hide_render = obj not in objects
    scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 16
    scene.cycles.use_denoising = True; scene.render.threads_mode = 'FIXED'; scene.render.threads = 2
    scene.render.resolution_x = 640; scene.render.resolution_y = 800; scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'; scene.render.film_transparent = False
    scene.view_settings.exposure = -.25
    world = bpy.data.worlds.new('_IrisPreviewWorld'); world.use_nodes = True; scene.world = world
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (.65, .70, .78, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = .55
    for name, location, energy in [('Key', (-1, -2, 2), 50), ('Fill', (1, 1, 1.5), 35)]:
        data = bpy.data.lights.new('_IrisPreview' + name, 'AREA'); data.energy = energy; data.shape = 'DISK'; data.size = 2
        obj = bpy.data.objects.new(data.name, data); scene.collection.objects.link(obj); obj.location = location
        obj.rotation_euler = (Vector((0, 0, .5)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    data = bpy.data.cameras.new('_IrisPreviewCamera'); camera = bpy.data.objects.new(data.name, data); scene.collection.objects.link(camera)
    data.type = 'ORTHO'; data.ortho_scale = 1.18; scene.camera = camera
    results = []
    for label, location in [('front', (1.0, -3, .85)), ('back', (-1.0, 3, .85))]:
        camera.location = location; camera.rotation_euler = (Vector((0, -.02, .51)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        path = output.with_name(output.stem + '-' + label + '.png')
        scene.render.filepath = str(path); bpy.ops.render.render(write_still=True)
        results.append(str(path))
    return results


def build(manifest_path, output, mode):
    manifest_path, output = Path(manifest_path).resolve(), Path(output).resolve()
    if output.exists():
        raise FileExistsError(output)
    spec = json.loads(manifest_path.read_text(encoding='utf-8'))
    source_path = Path(bpy.data.filepath).resolve(); source_hash = sha(source_path)
    expected = Path(spec.get('source_blend', str(source_path)))
    if not expected.is_absolute():
        expected = manifest_path.parent / expected
    if expected.resolve() != source_path:
        raise ValueError('Load the manifest source .blend before running the helper.')
    before = geometry_state(); original_materials = {material.name: material_data(material) for material in bpy.data.materials}
    if len(bpy.data.objects['Armature'].data.bones) != 74:
        raise ValueError('Expected the repaired 74-bone Eris/Iris rig.')
    detail_path = Path(spec['detail_texture'])
    if not detail_path.is_absolute():
        detail_path = manifest_path.parent / detail_path
    if spec.get('detail_texture_sha256') and sha(detail_path) != spec['detail_texture_sha256']:
        raise ValueError('The generated detail texture changed from the declared image.')
    reference_path = Path(spec['reference'])
    if not reference_path.is_absolute():
        reference_path = manifest_path.parent / reference_path
    if spec.get('reference_sha256') and sha(reference_path) != spec['reference_sha256']:
        raise ValueError('The supplied concept reference changed from the declared image.')
    detail = bpy.data.images.load(str(detail_path.resolve()), check_existing=False); detail.colorspace_settings.name = 'Non-Color'; detail.pack()
    output.parent.mkdir(parents=True, exist_ok=True)
    records = {}; working = []; proc_materials = []
    try:
        for name, material_name, prefix in [('Head', 'Eris_ConceptBody', 'Iris_Body'), ('Hair', 'Eris_ConceptHair', 'Iris_HairHood')]:
            if material_name in bpy.data.materials:
                raise ValueError('The new concept material already exists: ' + material_name)
            original = bpy.data.objects[name]; image = source_image(original)
            material, signals, output_node = procedural_material('_IrisWork_' + name, image, spec, detail, hair=name == 'Hair')
            proc_materials.append(material)
            obj = temporary_copy(original, material, keep_modifiers=mode == 'preview'); working.append(obj)
            if mode == 'bake':
                maps = bake_maps(obj, material, signals, output_node, image, spec, output.parent / 'Textures', prefix)
                final_material = baked_material(material_name, maps)
                original.data.materials[0].use_fake_user = True
                original.data.materials[0] = final_material
                records[name] = {'name': material_name, 'maps': maps}
        if mode == 'preview':
            renders = render_preview(working, output)
            report = {'mode': mode, 'source': source_path.name, 'source_sha256': source_hash,
                      'preview_images': renders, 'source_preserved': sha(source_path) == source_hash, 'passed': True}
            report_path = output.with_suffix('.preview.json')
        else:
            for obj in working:
                mesh = obj.data; bpy.data.objects.remove(obj, do_unlink=True); bpy.data.meshes.remove(mesh)
            working.clear()
            for material in proc_materials:
                bpy.data.materials.remove(material)
            proc_materials.clear()
            after = geometry_state()
            checks = {'all_geometry_uv_skin_rest_bones_and_actions_exact': before == after,
                      'all_original_material_graphs_preserved': all(material_data(bpy.data.materials[name]) == value for name, value in original_materials.items()),
                      'source_file_preserved': sha(source_path) == source_hash}
            if not all(checks.values()):
                raise AssertionError(checks)
            for material in bpy.data.materials:
                material.use_fake_user = True
            for name in ('Head', 'Hair'):
                for slot in bpy.data.objects[name].material_slots:
                    slot.material.use_fake_user = True
            bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
            report = {'mode': mode, 'source': source_path.name, 'source_sha256': source_hash,
                'output': output.name, 'output_sha256': sha(output), 'materials': records,
                'reference': {'file': reference_path.name, 'sha256': sha(reference_path)},
                'generated_detail': {'file': detail_path.name, 'sha256': sha(detail_path)},
                'manifest_semantic_sha256': digest(spec),
                'checks': checks, 'resolution': spec.get('resolution', 4096), 'bake_margin': spec.get('bake_margin', 2),
                'source_uv_modified': False, 'skin_weights_modified': False, 'actions_modified': False,
                'unity_runtime_verified': False, 'passed': all(checks.values())}
            report_path = output.parent / 'texture-build-verification.json'
        report_path.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n', encoding='utf-8', newline='\n')
        print('IRIS_CONCEPT_TEXTURE=' + json.dumps({'mode': mode, 'passed': report['passed'], 'report': str(report_path)}), flush=True)
        return report
    finally:
        for obj in working:
            if obj.name in bpy.data.objects:
                mesh = obj.data; bpy.data.objects.remove(obj, do_unlink=True); bpy.data.meshes.remove(mesh)
        for material in proc_materials:
            if material.name in bpy.data.materials:
                bpy.data.materials.remove(material)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--mode', choices=['preview', 'bake'], default='bake')
    options = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    build(options.manifest, options.output, options.mode)
