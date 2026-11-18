"""Focused Eris clothing-weight support for the animated copy.

Call apply_rig_support(report_path=None) in Blender on the unmodified Eris source.
This module never opens or saves a scene and never changes poses or animations.
It preserves real hand/neck/head skin, all74bones, hair, geometry, UVs and materials.
"""
import bpy
import hashlib
import json
from pathlib import Path
import numpy as np
from mathutils import Matrix, Quaternion
import math


def _components(mesh):
    parent=list(range(len(mesh.vertices)))
    def find(index):
        while parent[index]!=index:
            parent[index]=parent[parent[index]];index=parent[index]
        return index
    for edge in mesh.edges:
        a,b=map(find,edge.vertices)
        if a!=b:parent[b]=a
    groups={}
    for index in range(len(parent)):groups.setdefault(find(index),[]).append(index)
    return {index:group for group in groups.values() for index in group}


def _weights(body, vertices=None):
    names={group.index:group.name for group in body.vertex_groups}
    return {index:{names[group.group]:float(group.weight) for group in body.data.vertices[index].groups if group.weight>0}
            for index in (vertices if vertices is not None else range(len(body.data.vertices)))}


def _digest(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':')).encode('utf-8')).hexdigest()


def _geometry_hash(mesh):
    result={}
    for name,collection,field,width,dtype in [('vertices',mesh.vertices,'co',3,np.float32),('edges',mesh.edges,'vertices',2,np.int32),('loops',mesh.loops,'vertex_index',1,np.int32)]:
        values=np.empty(len(collection)*width,dtype=dtype);collection.foreach_get(field,values)
        result[name]=hashlib.sha256(values.tobytes()).hexdigest()
    result['polygons']=[(poly.loop_start,poly.loop_total,poly.material_index,poly.use_smooth) for poly in mesh.polygons]
    result['uv']=[]
    for layer in mesh.uv_layers:
        values=np.empty(len(layer.data)*2,dtype=np.float32);layer.data.foreach_get('uv',values)
        result['uv'].append((layer.name,hashlib.sha256(values.tobytes()).hexdigest()))
    return _digest(result)


def _bone_hash(armature):
    return _digest([{'name':bone.name,'parent':bone.parent.name if bone.parent else None,'head':list(bone.head_local),'tail':list(bone.tail_local),
        'matrix':[list(row) for row in bone.matrix_local],'pose_basis':[list(row) for row in armature.pose.bones[bone.name].matrix_basis]}
        for bone in armature.data.bones])


def _evaluated_world(obj):
    bpy.context.view_layer.update()
    evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh()
    try:
        values=np.empty(len(mesh.vertices)*3,dtype=np.float32);mesh.vertices.foreach_get('co',values)
        values=values.reshape((-1,3)).astype(np.float64);transform=np.asarray(evaluated.matrix_world)
        return values@transform[:3,:3].T+transform[:3,3]
    finally:evaluated.to_mesh_clear()


def _stress_qa(body, hair, armature, components):
    """Transient stress poses, restoring every pose channel/action binding."""
    saved={pose.name:{'mode':pose.rotation_mode,'location':pose.location.copy(),'scale':pose.scale.copy(),
        'euler':pose.rotation_euler.copy(),'quaternion':pose.rotation_quaternion.copy(),'axis_angle':list(pose.rotation_axis_angle)} for pose in armature.pose.bones}
    animation=armature.animation_data
    old_action=animation.action if animation else None;old_slot=animation.action_slot if animation else None
    old_nla=animation.use_nla if animation else None;old_position=armature.data.pose_position
    tests=[]
    def reset():
        for pose in armature.pose.bones:pose.matrix_basis=Matrix.Identity(4)
        bpy.context.view_layer.update()
    try:
        if animation:animation.action=None;animation.use_nla=False
        armature.data.pose_position='POSE';reset()
        rest=_evaluated_world(body);rest_hair=_evaluated_world(hair)
        edges=np.empty(len(body.data.edges)*2,dtype=np.int32);body.data.edges.foreach_get('vertices',edges);edges=edges.reshape((-1,2))
        rest_length=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)
        definitions=[('head20','mixamorig:Head',(0,1,0),20),('neck15','mixamorig:Neck',(1,0,0),15),
            ('spine_flex20','mixamorig:Spine2',(1,0,0),20),('spine_twist20','mixamorig:Spine2',(0,1,0),20)]
        for side in ('Left','Right'):
            hand='mixamorig:'+side+'Hand'
            definitions.extend([(side+'_wrist45',hand,(1,0,0),45),(side+'_wristm45',hand,(1,0,0),-45),
                (side+'_twist60',hand,(0,1,0),60),(side+'_twistm60',hand,(0,1,0),-60),
                (side+'_shoulder25','mixamorig:'+side+'Shoulder',(1,0,0),25),(side+'_elbow60','mixamorig:'+side+'ForeArm',(1,0,0),60)])
            definitions.extend((side+'_'+finger+'60',hand+finger+'1',(1,0,0),60) for finger in ('Thumb','Index','Middle','Ring','Pinky'))
        for label,name,axis,angle in definitions:
            reset();pose=armature.pose.bones[name];pose.matrix_basis=Quaternion(axis,math.radians(angle)).to_matrix().to_4x4()
            current=_evaluated_world(body);current_hair=_evaluated_world(hair)
            test={'pose':label,'finite':bool(np.isfinite(current).all() and np.isfinite(current_hair).all()),'passed':True}
            test['passed']=test['finite']
            side='Left' if label.startswith('Left_') else 'Right' if label.startswith('Right_') else None
            if side:
                seed=4234 if side=='Left' else 1006
                if any(finger+'60' in label for finger in ('Thumb','Index','Middle','Ring','Pinky')):
                    movement=float(np.linalg.norm(current[components[seed]]-rest[components[seed]],axis=1).max())
                    test['sleeve_motion']=movement;test['passed']=test['passed'] and movement<1e-6
                if 'wrist' in label or 'twist' in label:
                    wrist=np.asarray(armature.matrix_world@armature.data.bones['mixamorig:'+side+'Hand'].head_local)
                    near=np.linalg.norm(rest-wrist,axis=1)<.10;mask=(rest_length>1e-6)&(near[edges[:,0]]|near[edges[:,1]])
                    ratios=np.linalg.norm(current[edges[:,0]]-current[edges[:,1]],axis=1)/np.maximum(rest_length,1e-12)
                    test['near_wrist_edge_min']=float(ratios[mask].min());test['near_wrist_edge_max']=float(ratios[mask].max())
                    test['passed']=test['passed'] and test['near_wrist_edge_min']>.25 and test['near_wrist_edge_max']<2.05
            if label in ('head20','neck15'):
                movement=float(np.linalg.norm(current[components[1709]]-rest[components[1709]],axis=1).max())
                test['cape_motion']=movement;test['passed']=test['passed'] and movement<1e-6
            reset();returned=float(np.linalg.norm(_evaluated_world(body)-rest,axis=1).max())
            test['return_to_rest_error']=returned;test['passed']=test['passed'] and returned<1e-6
            tests.append(test)
    finally:
        for name,values in saved.items():
            pose=armature.pose.bones[name];pose.rotation_mode=values['mode'];pose.location=values['location'];pose.scale=values['scale']
            pose.rotation_euler=values['euler'];pose.rotation_quaternion=values['quaternion'];pose.rotation_axis_angle=values['axis_angle']
        armature.data.pose_position=old_position
        if animation:
            animation.action=old_action
            if old_slot is not None:animation.action_slot=old_slot
            animation.use_nla=old_nla
        bpy.context.view_layer.update()
    return {'passed':bool(tests) and all(test['passed'] for test in tests),'poses':tests,'pose_count':len(tests)}


def _apply_collar_patch(body):
    patch_path=Path(__file__).with_name('collar_weight_patch.json')
    patch=json.loads(patch_path.read_text(encoding='utf-8'))
    if body.type!='MESH' or len(body.data.vertices)!=patch['vertex_count']:
        raise ValueError('Expected the unchanged Eris body mesh.')
    raw=np.empty(len(body.data.vertices)*3,dtype=np.float32);body.data.vertices.foreach_get('co',raw)
    if hashlib.sha256(raw.tobytes()).hexdigest()!=patch['raw_coordinates_sha256']:
        raise ValueError('Source body geometry changed; review the correction patch before applying.')
    current=_weights(body,[entry['vertex'] for entry in patch['entries']])
    for entry in patch['entries']:
        before=current[entry['vertex']];expected=entry['before']
        if any(abs(before.get(name,0)-expected.get(name,0))>=2e-5 for name in before.keys()|expected.keys()):
            raise ValueError('Unexpected prior weight edit at vertex '+str(entry['vertex']))
    for entry in patch['entries']:
        vertex=entry['vertex']
        for group in body.vertex_groups:group.remove([vertex])
        for name,weight in entry['after'].items():body.vertex_groups[name].add([vertex],weight,'REPLACE')
    return {'changed_vertices':len(patch['entries']),'components':patch['components'],
        'method':'Only verified cape/sleeve Head/Neck bleed; harmonic interpolation from clean clothing neighbors; no top4 truncation',
        'patch_sha256':hashlib.sha256(patch_path.read_bytes()).hexdigest()}


def _repair_sleeve_wrist_weights(body, armature, components):
    names={group.index:group.name for group in body.vertex_groups}
    result={'changed_vertices':[],'sides':{},
        'method':'Only two complete clothing sleeve/cuff islands. Reconstruct the wrist/forearm/finger mass with a uniform anatomical wrist gradient; preserve all other weights.'}
    for side,seed,count in [('Left',4234,1513),('Right',1006,1509)]:
        wrist_name='mixamorig:'+side+'Hand';forearm_name='mixamorig:'+side+'ForeArm'
        fingers={wrist_name+finger+str(index) for finger in ('Thumb','Index','Middle','Ring','Pinky') for index in (1,2,3)}
        wrist=body.vertex_groups.get(wrist_name);forearm=body.vertex_groups.get(forearm_name)
        if wrist is None or forearm is None:raise ValueError('Missing original wrist/forearm weights.')
        wrist_world=armature.matrix_world@armature.data.bones[wrist_name].head_local
        forearm_world=armature.matrix_world@armature.data.bones[forearm_name].head_local
        axis=wrist_world-forearm_world;length=axis.length;axis.normalize()
        changed=[];removed_max=0
        for index in components[seed]:
            vertex=body.data.vertices[index]
            weights={names[group.group]:float(group.weight) for group in vertex.groups if group.weight>0}
            removed=sum(weights.get(name,0) for name in fingers)
            prior_wrist=weights.get(wrist_name,0);prior_forearm=weights.get(forearm_name,0)
            pool=prior_wrist+prior_forearm+removed
            if pool<=0:continue
            q=((body.matrix_world@vertex.co)-wrist_world).dot(axis)
            t=max(0,min(1,q/(.20*length)));wrist_fraction=t*t*(3-2*t)
            if removed<=1e-10 and abs(prior_wrist-pool*wrist_fraction)<1e-10:continue
            for name in fingers:
                if weights.get(name,0)>0:body.vertex_groups[name].remove([index])
            if pool*wrist_fraction>0:wrist.add([index],pool*wrist_fraction,'REPLACE')
            elif prior_wrist>0:wrist.remove([index])
            forearm.add([index],pool*(1-wrist_fraction),'REPLACE')
            changed.append(index);removed_max=max(removed_max,removed)
        result['sides'][side]={'component_seed':seed,'component_vertices':count,'changed_vertices':len(changed),
            'max_removed_finger_mass':removed_max,'wrist_follow_start_world':0,'wrist_follow_full_world':.20*length}
        result['changed_vertices'].extend(changed)
    result['changed_vertices']=sorted(set(result['changed_vertices']))
    return result


def apply_rig_support(report_path=None):
    """Apply validated clothing-only fixes to the CURRENT scene; return a report.

    Must run on the fresh source before transferring motions. A second application
    is rejected by the collar patch's exact prior-weight validation.
    """
    body=bpy.data.objects.get('Head');hair=bpy.data.objects.get('Hair');armature=bpy.data.objects.get('Armature')
    if not body or not hair or not armature or body.type!='MESH' or armature.type!='ARMATURE':
        raise ValueError('Expected Head(body), Hair and Armature objects.')
    if len(body.data.vertices)!=15802 or len(hair.data.vertices)!=10447 or len(armature.data.bones)!=74:
        raise ValueError('Unexpected Eris topology or skeleton; review the support correction.')
    components=_components(body.data)
    for seed,count in [(4234,1513),(1006,1509),(5798,700),(750,700),(509,3363),(1709,2299)]:
        if len(components[seed])!=count:raise ValueError('Verified clothing/skin component changed at vertex '+str(seed))
    skin=set(components[5798])|set(components[750])|set(components[509])
    skin_before=_digest(_weights(body,sorted(skin)));geometry_before=_geometry_hash(body.data)
    hair_before=_digest(_weights(hair));bones_before=_bone_hash(armature)
    slots_before={obj.name:[slot.material.name if slot.material else None for slot in obj.material_slots] for obj in (body,hair)}
    all_weights_before=_weights(body)
    collar=_apply_collar_patch(body)
    cuffs=_repair_sleeve_wrist_weights(body,armature,components)
    body.data.update();body.update_tag();bpy.context.view_layer.update()
    stress=_stress_qa(body,hair,armature,components)
    all_weights_after=_weights(body)
    changed=[index for index in all_weights_before if all_weights_before[index]!=all_weights_after[index]]
    checks={'actual_hand_neck_head_skin_weights_exact':skin_before==_digest(_weights(body,sorted(skin))),
        'body_geometry_and_uv_exact':geometry_before==_geometry_hash(body.data),
        'hair_weights_exact':hair_before==_digest(_weights(hair)),
        'all74_bone_rest_and_pose_exact':bones_before==_bone_hash(armature),
        'body_and_hair_material_slots_exact':slots_before=={obj.name:[slot.material.name if slot.material else None for slot in obj.material_slots] for obj in (body,hair)}}
    if not all(checks.values()):raise AssertionError('Rig support preservation check failed: '+str(checks))
    if not stress['passed']:raise AssertionError('Rig support stress check failed: '+str([test for test in stress['poses'] if not test['passed']]))
    result={'passed':True,'scope':'Only cape/collar and sleeve/cuff clothing weights; real hand/head/neck skin, all74bones and hair are preserved.',
        'changed_body_vertices':len(changed),'changed_vertex_ids':changed,'collar':collar,'cuffs':cuffs,
        'preservation':checks,'stress_qa':stress,'body_weights_sha256_before':_digest(all_weights_before),'body_weights_sha256_after':_digest(all_weights_after),
        'body_max_positive_influences':max(len(weights) for weights in all_weights_after.values()),
        'body_max_weight_sum_error':max(abs(sum(weights.values())-1) for weights in all_weights_after.values()),
        'saves_scene':False,'changes_actions_or_animation':False}
    if report_path:
        destination=Path(report_path).resolve()
        if destination.suffix.lower()!='.json':raise ValueError('Rig support report destination must be JSON.')
        destination.parent.mkdir(parents=True,exist_ok=True);destination.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return result
