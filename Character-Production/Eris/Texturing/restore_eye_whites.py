"""Restore the sclera on the textured Iris without changing the skinned mesh.

blender --background INPUT.blend --python restore_eye_whites.py -- --output NEW.blend
The contours use the frontal rest view at 1040 x 620, ortho width .215 m.
"""

import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_textured_character import capture, digest, file_sha

CONTOURS = (
    # The outside and inside corners of the right eye, then the left.
    ((281, 415), (286, 391), (302, 373), (320, 358), (347, 345), (338, 370),
     (331, 393), (329, 416), (332, 443), (342, 472), (321, 467), (299, 454), (286, 435)),
    ((426, 348), (440, 364), (449, 386), (449, 410), (442, 439), (429, 469),
     (427, 456), (434, 430), (434, 404), (430, 378)),
    ((700, 343), (724, 351), (744, 367), (758, 388), (755, 417), (747, 441),
     (730, 463), (708, 478), (693, 483), (706, 462), (712, 439), (716, 416),
     (715, 395), (711, 370)),
    ((611, 348), (627, 343), (621, 359), (612, 385), (607, 412), (609, 433),
     (617, 464), (608, 452), (597, 435), (591, 414), (592, 390), (598, 369)),
)
EMISSION_STRENGTH = .62


def smoothstep(value, low, high):
    ratio = np.clip((value - low) / (high - low), 0, 1)
    return ratio * ratio * (3 - 2 * ratio)


def interior_weight(points, outline):
    inside = np.zeros(len(points), dtype=bool)
    nearest = np.full(len(points), np.inf)
    for start, end in zip(outline, np.roll(outline, -1, axis=0)):
        edge = end - start
        offset = points - start
        slope = 0 if edge[1] == 0 else edge[0] / edge[1]
        inside ^= ((start[1] > points[:, 1]) != (end[1] > points[:, 1])) & (
            points[:, 0] < start[0] + offset[:, 1] * slope)
        along = np.clip((offset @ edge) / (edge @ edge), 0, 1)
        nearest = np.minimum(nearest, np.linalg.norm(offset - along[:, None] * edge, axis=1))
    return smoothstep(np.where(inside, nearest, -nearest), -2.0, 5.0)


def texels(triangle, width, height):
    minimum = np.maximum(np.floor(triangle.min(axis=0)).astype(int), 0)
    maximum = np.minimum(np.ceil(triangle.max(axis=0)).astype(int), (width - 1, height - 1))
    origin, end, top = triangle
    edge, other = end - origin, top - origin
    determinant = edge[0] * other[1] - edge[1] * other[0]
    if abs(determinant) < 1e-8:
        return np.empty(0, int), np.empty(0, int), np.empty((0, 3))
    yy, xx = np.mgrid[minimum[1]:maximum[1] + 1, minimum[0]:maximum[0] + 1]
    offset_x, offset_y = xx - origin[0], yy - origin[1]
    v = (offset_x * other[1] - offset_y * other[0]) / determinant
    w = (edge[0] * offset_y - edge[1] * offset_x) / determinant
    u = 1 - v - w
    inside = (u >= 0) & (v >= 0) & (w >= 0)
    return yy[inside], xx[inside], np.stack((u[inside], v[inside], w[inside]), axis=1)


def eye_masks(body, base):
    """Project the contours onto the exact existing UV triangle interiors."""
    mesh = body.data
    mesh.calc_loop_triangles()
    positions = np.array([body.matrix_world @ vertex.co for vertex in mesh.vertices])
    center_z = positions[:, 2].max() - .065
    scale = 1040 / .215
    screen = np.stack((520 + positions[:, 0] * scale, 310 + (center_z - positions[:, 2]) * scale), axis=1)
    height, width = base.shape[:2]
    coords = np.array([loop.vector for loop in mesh.uv_layers['Attribute'].uv]) * (width, height) - .5
    contours = [np.array(contour) for contour in CONTOURS]
    bounds = [(contour.min(axis=0) - 3, contour.max(axis=0) + 3) for contour in contours]
    coverage = np.zeros((height, width), dtype=bool)
    protected = np.zeros_like(coverage)
    masks = [np.zeros((height, width), dtype=np.float32) for _ in range(2)]
    cleaned = np.zeros_like(masks[0])
    for triangle in mesh.loop_triangles:
        vertices = np.array(triangle.vertices)
        projected = screen[vertices]
        projected_min, projected_max = projected.min(axis=0), projected.max(axis=0)
        candidates = [index for index, (low, high) in enumerate(bounds)
                      if (projected_min < high).all() and (projected_max > low).all()]
        y, x, barycentric = texels(coords[list(triangle.loops)], width, height)
        coverage[y, x] = True
        if not candidates:
            protected[y, x] = True
            continue
        points = barycentric @ projected
        depth = barycentric @ positions[vertices, 1]
        red, green, blue = base[y, x, :3].T
        # Match light sclera tones and preserve the warm lid and blue iris.
        neutral = smoothstep(np.minimum(np.minimum(red, green), blue), .48, .73)
        neutral *= 1 - smoothstep(red - blue, .058, .14)
        neutral *= 1 - smoothstep(blue - red, .095, .23)
        front = smoothstep(-depth, .087, .095) * (1 - smoothstep(-depth, .122, .131))
        footprint = np.zeros(len(x), dtype=np.float32)
        for index in candidates:
            area = interior_weight(points, contours[index]) * front
            weight = area * neutral
            if index == 0:
                # The reflected blue stripe lies on the outer sclera.
                radius = np.linalg.norm((points - (315, 418)) / (16, 47), axis=1)
                stripe = (1 - smoothstep(radius, .78, 1.02)) * area
                weight = np.maximum(weight, stripe)
            shade = .58 + .42 * smoothstep(points[:, 1], 349, 431)
            np.maximum.at(masks[index // 2], (y, x), weight * shade)
            np.maximum.at(cleaned, (y, x), weight)
            footprint = np.maximum(footprint, area)
        protected[y[footprint < .015], x[footprint < .015]] = True
    for array in (*masks, cleaned):
        array[protected] = 0
    return masks, cleaned, coverage


def extend_into_padding(values, coverage):
    """Give linear filtering two pixels of padding within unused UV space."""
    result = values.copy()
    for _ in range(2):
        expanded = result.copy()
        for incoming, adjacent in ((np.s_[1:], np.s_[:-1]), (np.s_[:-1], np.s_[1:]),
                                   (np.s_[:, 1:], np.s_[:, :-1]), (np.s_[:, :-1], np.s_[:, 1:])):
            np.maximum(expanded[incoming], result[adjacent], out=expanded[incoming])
        result[~coverage] = expanded[~coverage]
    return result


def save_map(values, path, source_name):
    height, width = values.shape[:2]
    work = bpy.data.images.new('_IrisEyeMap', width=width, height=height, alpha=True)
    work.colorspace_settings.name = 'sRGB'
    work.file_format = 'PNG'
    work.filepath_raw = str(path)
    work.pixels.foreach_set(values.ravel())
    work.save()
    bpy.data.images.remove(work)
    result = bpy.data.images.load(str(path), check_existing=False)
    result.name = source_name
    result.colorspace_settings.name = 'sRGB'
    result.pack()
    result.filepath = '//Textures/' + path.name
    return result


def restore(output):
    source = Path(bpy.data.filepath).resolve()
    output = Path(output).resolve()
    maps_dir = output.parent / 'Textures'
    base_path = maps_dir / 'Iris_Body_BaseColor.png'
    light_path = maps_dir / 'Iris_Body_Emission.png'
    report_path = output.parent / 'eye-material-verification.json'
    if any(path.exists() for path in (output, base_path, light_path, report_path)):
        raise FileExistsError('Use a new Iris output directory.')

    source_hash = file_sha(source)
    before = capture()
    body = bpy.data.objects['Head']
    tree = body.material_slots[0].material.node_tree
    shader = next(node for node in tree.nodes if node.type == 'BSDF_PRINCIPLED')
    base_node = shader.inputs['Base Color'].links[0].from_node
    previous_image = base_node.image
    if tuple(previous_image.size) != (4096, 4096) or shader.inputs['Emission Color'].is_linked:
        raise ValueError('Expected the Iris body atlas before the eye material pass.')
    values = np.empty(len(previous_image.pixels), dtype=np.float32)
    previous_image.pixels.foreach_get(values)
    base = values.reshape(4096, 4096, 4)
    masks, cleaned, coverage = eye_masks(body, base)
    counts = [int(np.count_nonzero(mask > .25)) for mask in masks]
    if min(counts) < 200 or max(counts) > 35000:
        raise ValueError(f'Unexpected sclera atlas coverage at {counts} pixels.')
    mask = extend_into_padding(np.maximum(masks[0], masks[1]), coverage)
    clean = extend_into_padding(cleaned, coverage)
    affected = clean > 0
    amount = clean[affected, None] * .64
    base[affected, :3] = base[affected, :3] * (1 - amount) + (0.955, 0.956, 0.980) * amount
    fill = np.zeros_like(base)
    # PNG is sRGB. Encode the coverage in linear light before saving.
    lit = mask > 0
    linear = mask[lit, None] * np.array((.92, .95, 1), dtype=np.float32)
    fill[lit, :3] = np.where(linear <= .0031308, 12.92 * linear, 1.055 * linear ** (1 / 2.4) - .055)
    fill[:, :, 3] = 1
    maps_dir.mkdir(parents=True, exist_ok=True)
    old_name = previous_image.name
    previous_image.name = '_IrisPreviousBodyBaseColor'
    base_node.image = save_map(base, base_path, old_name)
    if previous_image.users == 0:
        bpy.data.images.remove(previous_image)
    emission_node = tree.nodes.new('ShaderNodeTexImage')
    emission_node.label = 'Emission'
    emission_node.image = save_map(fill, light_path, light_path.name)
    emission_node.location = (base_node.location.x, base_node.location.y - 980)
    uv_socket = base_node.inputs['Vector'].links[0].from_socket
    tree.links.new(uv_socket, emission_node.inputs['Vector'])
    tree.links.new(emission_node.outputs['Color'], shader.inputs['Emission Color'])
    shader.inputs['Emission Strength'].default_value = EMISSION_STRENGTH
    tree.nodes.active = base_node

    after = capture()
    unchanged = ('meshes', 'bones', 'armature_world', 'pose_position', 'pose_bases', 'actions', 'animation', 'playback')
    checks = {
        'all_geometry_uvs_weights_bones_actions_exact': all(digest(before[key]) == digest(after[key]) for key in unchanged),
        'all_other_image_payloads_exact': all(before['images'][name] == after['images'][name]
                                             for name in before['images'] if name != old_name),
        'source_file_preserved': file_sha(source) == source_hash,
    }
    if not all(checks.values()):
        raise AssertionError(checks)
    materials = {}
    for object_name in ('Head', 'Hair'):
        material = bpy.data.objects[object_name].material_slots[0].material
        maps = {node.label: {'image': node.image.name, 'sha256': hashlib.sha256(node.image.packed_file.data).hexdigest()}
                for node in material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image}
        materials[object_name] = {'name': material.name, 'maps': maps}
    bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False, relative_remap=False)
    report = {'source_sha256': source_hash, 'output': output.name, 'output_sha256': file_sha(output),
              'materials': materials, 'emission_strength': EMISSION_STRENGTH,
              'sclera_pixels': dict(zip(('Right', 'Left'), counts)), 'checks': checks, 'passed': True}
    report_path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf8', newline='\n')
    print('IRIS_EYE_MATERIAL=' + json.dumps({'atlas_pixels': counts, 'checks': checks}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    restore(parser.parse_args(sys.argv[sys.argv.index('--') + 1:]).output)
