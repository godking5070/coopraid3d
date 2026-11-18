"""Package one existing diffuse texture inside a NEW FBX without re-exporting meshes.

Run using Blender's bundled Python:
blender --background --factory-startup --python embed_fbx_texture.py -- \
  --source path/to/source.fbx --texture path/to/basecolor.jpg \
  --output path/to/Eris_Mixamo_Textured.fbx

Original files and scene state are never saved or overwritten.
"""
import argparse
import array
import hashlib
import json
from pathlib import Path

from io_scene_fbx import parse_fbx, encode_bin


def sha(data):
    return hashlib.sha256(data).hexdigest()


def child(node, identifier):
    return next((item for item in node.elems if item.id == identifier), None)


def canonical_property(value):
    if isinstance(value, array.array):
        return {'array_type': value.typecode, 'count': len(value), 'sha256': sha(value.tobytes())}
    if isinstance(value, bytes):
        return {'bytes': len(value), 'sha256': sha(value)}
    if isinstance(value, float):
        return {'float': value.hex()}
    return value


def canonical(node):
    return {'id': node.id.decode('ascii'), 'types': list(node.props_type),
            'props': [canonical_property(value) for value in node.props],
            'children': [canonical(item) for item in node.elems]}


def fingerprint(node):
    return sha(json.dumps(canonical(node), sort_keys=True, separators=(',', ':')).encode('utf-8'))


ENCODERS = {
    ord('B'): 'add_bool', ord('C'): 'add_char', ord('Z'): 'add_int8',
    ord('Y'): 'add_int16', ord('I'): 'add_int32', ord('L'): 'add_int64',
    ord('F'): 'add_float32', ord('D'): 'add_float64', ord('R'): 'add_bytes',
    ord('S'): 'add_string', ord('i'): 'add_int32_array', ord('l'): 'add_int64_array',
    ord('f'): 'add_float32_array', ord('d'): 'add_float64_array',
    ord('b'): 'add_bool_array', ord('c'): 'add_byte_array',
}


def encoded(node):
    result = encode_bin.FBXElem(node.id)
    for kind, value in zip(node.props_type, node.props):
        getattr(result, ENCODERS[kind])(value)
    result.elems = [encoded(item) for item in node.elems]
    return result


def replace_string_field(node, field_name, value):
    existing = child(node, field_name)
    if existing is None:
        node.elems.append(parse_fbx.FBXElem(field_name, [value], bytearray(b'S'), []))
    else:
        if list(existing.props_type) != list(b'S'):
            raise ValueError('Unexpected field type: ' + field_name.decode())
        existing.props[:] = [value]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True)
    parser.add_argument('--texture', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--details-report', help='Optional path for the full semantic preservation audit.')
    import sys
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    source, texture, output = (Path(value).resolve() for value in (args.source, args.texture, args.output))
    if output in (source, texture) or output.exists():
        raise ValueError('Output must be a new path, separate from existing originals.')
    original_hashes = {str(file): sha(file.read_bytes()) for file in (source, texture)}
    jpeg = texture.read_bytes()
    if not jpeg.startswith(b'\xff\xd8\xff'):
        raise ValueError('This character package expects the original JPEG base color.')
    original_root, version = parse_fbx.parse(str(source))
    root, _ = parse_fbx.parse(str(source))
    objects = child(root, b'Objects')
    videos = [node for node in objects.elems if node.id == b'Video']
    textures = [node for node in objects.elems if node.id == b'Texture']
    if len(videos) != 1 or len(textures) != 1:
        raise ValueError('Expected exactly one diffuse image and texture; inspect multi-texture assets separately.')
    video, tex = videos[0], textures[0]
    connections = child(root, b'Connections')
    if not any(node.props[:4] == [b'OP', tex.props[0], next(node for node in objects.elems if node.id == b'Material').props[0], b'DiffuseColor'] for node in connections.elems):
        raise ValueError('DiffuseColor texture connection was not found.')

    image_name = b'Eris_BaseColor.jpg'
    for field in (b'Filename', b'RelativeFilename'):
        replace_string_field(video, field, image_name)
    for field in (b'FileName', b'RelativeFilename'):
        replace_string_field(tex, field, image_name)
    for owner in (video, tex):
        props = child(owner, b'Properties70')
        if props:
            for prop in props.elems:
                if prop.id == b'P' and prop.props[0] in (b'Path', b'RelPath'):
                    prop.props[-1] = image_name
    video.elems[:] = [node for node in video.elems if node.id != b'Content']
    video.elems.append(parse_fbx.FBXElem(b'Content', [jpeg], bytearray(b'R'), []))

    output.parent.mkdir(parents=True, exist_ok=True)
    image_copy = output.parent / image_name.decode('ascii')
    if image_copy.exists() and image_copy.read_bytes() != jpeg:
        raise ValueError('Existing companion image differs; refusing to overwrite it.')
    encode_bin.write(str(output), encoded(root), version)
    if not image_copy.exists():
        image_copy.write_bytes(jpeg)
    verified, verified_version = parse_fbx.parse(str(output))
    before_objects = child(original_root, b'Objects')
    after_objects = child(verified, b'Objects')
    by_id = {node.props[0]: node for node in after_objects.elems}
    preserved=[]
    for node in before_objects.elems:
        if node.id in (b'Texture', b'Video'):
            continue
        after_node=by_id[node.props[0]]
        before_hash, after_hash = fingerprint(node), fingerprint(after_node)
        if before_hash != after_hash:
            raise AssertionError('Non-media object changed: ' + node.id.decode('ascii'))
        preserved.append({'type':node.id.decode('ascii'),'id':node.props[0],
            'name':node.props[1].decode('utf-8','replace').replace('\x00\x01','::'),
            'semantic_sha256':before_hash})
    if fingerprint(child(original_root,b'Connections')) != fingerprint(child(verified,b'Connections')):
        raise AssertionError('Connections changed.')
    if fingerprint(child(original_root,b'GlobalSettings')) != fingerprint(child(verified,b'GlobalSettings')):
        raise AssertionError('GlobalSettings changed.')
    embedded = child(next(node for node in after_objects.elems if node.id == b'Video'), b'Content').props[0]
    if embedded != jpeg:
        raise AssertionError('Embedded image bytes differ from source JPEG.')
    for name, expected in original_hashes.items():
        if sha(Path(name).read_bytes()) != expected:
            raise AssertionError('An original changed while packaging: ' + name)
    uv_report=[]
    for geometry in (node for node in after_objects.elems if node.id==b'Geometry'):
        for uv in (node for node in geometry.elems if node.id==b'LayerElementUV'):
            values=child(uv,b'UV').props[0]
            indices=child(uv,b'UVIndex').props[0]
            uv_report.append({'geometry_id':geometry.props[0],
                'uv_name':child(uv,b'Name').props[0].decode('utf-8','replace'),
                'uv_pairs':len(values)//2,'uv_indices':len(indices),
                'uv_sha256':sha(values.tobytes()),'indices_sha256':sha(indices.tobytes())})
    report={'source_fbx':str(source),'output_fbx':str(output),'fbx_version':verified_version,
        'source_hashes':original_hashes,'originals_unchanged':True,
        'output_sha256':sha(output.read_bytes()),'output_bytes':output.stat().st_size,
        'embedded_image':{'file':image_name.decode(),'bytes':len(jpeg),'sha256':sha(jpeg),'jpeg_signature':'FFD8FF'},
        'geometry_models_materials_preserved':preserved,'uv_sets':uv_report,
        'connections_preserved':True,'global_settings_preserved':True,
        'change_scope':'Video.Content JPEG added; Video/Texture filename/path strings made relative. No mesh, normals, UV, transform, material, animation, or rig changes. Blender binary encoder rewrites file identity/time footer metadata.',
        'mixamo_upload_verified':False}
    summary={'source_fbx':str(source),'source_fbx_sha256':original_hashes[str(source)],
        'output_fbx':output.name,'output_fbx_bytes':report['output_bytes'],'output_fbx_sha256':report['output_sha256'],
        'basecolor':report['embedded_image'],
        'preservation':{'all_originals_unchanged':True,
            'geometry_objects_preserved':sum(item['type']=='Geometry' for item in preserved),
            'model_objects_preserved':sum(item['type']=='Model' for item in preserved),
            'materials_preserved':sum(item['type']=='Material' for item in preserved),
            'deformer_objects_present_in_source':sum(item['type']=='Deformer' for item in preserved),
            'uv_arrays_and_indices_preserved':True,'connections_preserved':True,'global_settings_preserved':True},
        'standalone_import_verified':False,'mixamo_upload_verified':False,
        'scope':report['change_scope']}
    (output.parent/'verification.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    if args.details_report:
        details=Path(args.details_report).resolve()
        if details in (source,texture,output,image_copy):
            raise ValueError('Details report must be separate from asset files.')
        details.parent.mkdir(parents=True,exist_ok=True)
        details.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('FBX_PACKAGING='+json.dumps(report,ensure_ascii=True))


if __name__ == '__main__':
    main()
