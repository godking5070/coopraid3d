"""Read-only FBX PBR role/hash checks, independent of motion sampling.

Blender CLI arguments after --: BUILD_JSON EXPORT_JSON REPORT_JSON NEW_STAGING_DIR
The staging directory must not exist; source FBXs and the .blend are never saved.
"""
import bpy,sys,json,hashlib,shutil,math
from pathlib import Path
from io_scene_fbx import parse_fbx
sys.path.insert(0,str(Path(__file__).resolve().parent))
from validate_textured_character import upstream_images
args=sys.argv[sys.argv.index('--')+1:]
build_path,exports_path,report_path,stage=map(lambda value:Path(value).resolve(),args)
build=json.loads(build_path.read_text(encoding='utf-8'));exports=json.loads(exports_path.read_text(encoding='utf-8'))
expected={item['name']:{role:record['sha256'] for role,record in item['maps'].items()} for item in build['materials'].values()}
expected_hashes=sorted(value for maps in expected.values() for value in maps.values())
role_names={'BaseColor':b'DiffuseColor','Roughness':b'ShininessExponent','Metallic':b'ReflectionFactor',
            'Normal':b'NormalMap','Emission':b'EmissiveColor'}
socket_names={'BaseColor':'Base Color','Roughness':'Roughness','Metallic':'Metallic','Normal':'Normal',
              'Emission':'Emission Color'}
color_roles={'BaseColor','Emission'}
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def name(value):return value.split(b'\x00\x01')[0].decode('utf-8') if isinstance(value,bytes) else str(value)
stage.mkdir(parents=True,exist_ok=False)
report={'expected_images':len(expected_hashes),'clips':[],'unity_import_or_play_verified':False}
for clip in exports['clips']:
    path=exports_path.parent/clip['path'];before=sha(path)
    fbx,_=parse_fbx.parse(str(path));objects=next(node for node in fbx.elems if node.id==b'Objects');connections=next(node for node in fbx.elems if node.id==b'Connections')
    videos={node.props[0]:hashlib.sha256(next(child.props[0] for child in node.elems if child.id==b'Content')).hexdigest() for node in objects.elems if node.id==b'Video'}
    materials={name(node.props[1]):node.props[0] for node in objects.elems if node.id==b'Material'}
    textures={node.props[0] for node in objects.elems if node.id==b'Texture'}
    texture_video={node.props[2]:node.props[1] for node in connections.elems if node.id==b'C' and node.props[0]==b'OO' and node.props[1] in videos and node.props[2] in textures}
    checks=[{'name':'All expected PNG payload hashes are embedded','passed':sorted(videos.values())==expected_hashes,'images':len(videos)}]
    role_result={}
    for material_name,maps in expected.items():
        mat_id=materials.get(material_name);roles={}
        for role,payload_hash in maps.items():
            actual=[videos[texture_video[node.props[1]]] for node in connections.elems if node.id==b'C' and node.props[0]==b'OP' and node.props[2]==mat_id and len(node.props)>3 and node.props[3]==role_names[role] and node.props[1] in texture_video]
            roles[role]={'expected':payload_hash,'actual':actual,'passed':actual==[payload_hash]}
        role_result[material_name]=roles
    checks.append({'name':'Each material connects its intended FBX texture roles','passed':all(item['passed'] for roles in role_result.values() for item in roles.values()),'materials':role_result})
    clip_dir=stage/clip['name'];clip_dir.mkdir();staged=clip_dir/path.name;shutil.copy2(path,staged)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(staged),use_anim=True,anim_offset=0,use_image_search=False,automatic_bone_orientation=False,ignore_leaf_bones=False)
    imported={}
    for material_name,maps in expected.items():
        material=bpy.data.materials.get(material_name);shader=next((node for node in material.node_tree.nodes if node.type=='BSDF_PRINCIPLED'),None) if material and material.node_tree else None
        roles={}
        for role,payload_hash in maps.items():
            images=upstream_images(shader.inputs[socket_names[role]]) if shader else []
            actual=[]
            for image in images:
                payload=bytes(image.packed_file.data) if image.packed_file else Path(bpy.path.abspath(image.filepath)).read_bytes()
                actual.append({'image':image.name,'sha256':hashlib.sha256(payload).hexdigest(),'colorspace':image.colorspace_settings.name,'size':list(image.size)})
            roles[role]={'images':actual,'passed':len(actual)==1 and actual[0]['sha256']==payload_hash and actual[0]['colorspace']==('sRGB' if role in color_roles else 'Non-Color') and actual[0]['size']==[4096,4096]}
            if role == 'Emission' and 'emission_strength' in build:
                strength = shader.inputs['Emission Strength'].default_value if shader else None
                roles[role]['strength'] = strength
                roles[role]['passed'] &= strength is not None and math.isclose(strength, build['emission_strength'], abs_tol=1e-5)
        invalid=[]
        if material and material.node_tree:
            for node in material.node_tree.nodes:
                for socket in node.inputs:
                    if not hasattr(socket,'default_value'):continue
                    value=socket.default_value
                    try:values=[value] if isinstance(value,(int,float)) else list(value)
                    except TypeError:continue
                    if any(isinstance(item,(int,float)) and not math.isfinite(item) for item in values):invalid.append(node.name+'/'+socket.name)
        imported[material_name]={'roles':roles,'invalid_mapping_inputs':invalid,'passed':all(item['passed'] for item in roles.values()) and not invalid}
    checks.append({'name':'Fresh isolated import retains map roles, color spaces and finite UV mappings','passed':all(item['passed'] for item in imported.values()),'materials':imported})
    checks.append({'name':'Production FBX bytes remain unchanged','passed':sha(path)==before})
    report['clips'].append({'name':clip['name'],'checks':checks,'passed':all(item['passed'] for item in checks)})
report['passed']=len(report['clips'])==3 and all(item['passed'] for item in report['clips'])
report_path.parent.mkdir(parents=True,exist_ok=True);report_path.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
print('FBX_PBR_ROLES_QA='+json.dumps({'passed':report['passed'],'clips':[{'name':clip['name'],'passed':clip['passed'],'failed':[item['name'] for item in clip['checks'] if not item['passed']]} for clip in report['clips']]}))
if not report['passed']:raise SystemExit(1)
