"""Render six textured CPU previews without saving or replacing the input scene.

blender --background Eris_Animated.blend --python render_previews.py -- Preview
"""
import bpy,sys,json,math,hashlib
from pathlib import Path
from mathutils import Vector
from bpy_extras.anim_utils import action_get_first_suitable_slot

out=Path(sys.argv[sys.argv.index('--')+1]).resolve();out.mkdir(parents=True,exist_ok=True)
source=Path(bpy.data.filepath);source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=20
scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=2
scene.render.resolution_x=480;scene.render.resolution_y=640;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
scene.view_settings.exposure=-.4
for obj in scene.objects:
    if obj.type=='MESH' and obj.name not in {'Head','Hair'}:obj.hide_render=True
world=bpy.data.worlds.new('AuditWorld');world.use_nodes=True;scene.world=world
world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.7,.74,.8,1)
world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.55
for name,loc,energy,size in [('Key',(-1,-2,2),45,2),('Fill',(1,0,1.5),25,2)]:
    light=bpy.data.lights.new(name,'AREA');light.energy=energy;light.shape='DISK';light.size=size
    lightobj=bpy.data.objects.new(name,light);scene.collection.objects.link(lightobj);lightobj.location=loc
    lightobj.rotation_euler=(Vector((0,0,.5))-lightobj.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.cameras.new('AuditCamera');cameraobj=bpy.data.objects.new('AuditCamera',camera);scene.collection.objects.link(cameraobj);scene.camera=cameraobj
camera.type='ORTHO';camera.ortho_scale=1.18
arm=bpy.data.objects['Armature'];results=[]
for clip,frame in [('Idle',1),('Idle',108),('Walk',10),('Walk',26),('Run',20),('Run',59)]:
    action=bpy.data.actions['Eris_'+clip];arm.animation_data.action=action;arm.animation_data.action_slot=action_get_first_suitable_slot(action,'OBJECT')
    scene.frame_set(frame);bpy.context.view_layer.update()
    cameraobj.location=(1.2,-3,.95);cameraobj.rotation_euler=(Vector((0,-.02,.51))-cameraobj.location).to_track_quat('-Z','Y').to_euler()
    filename=clip.lower()+'-'+str(frame)+'.png';scene.render.filepath=str(out/filename);bpy.ops.render.render(write_still=True)
    results.append({'clip':clip,'frame':frame,'file':filename})
assert hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
(out/'previews.json').write_text(json.dumps({'source':source.name,'source_sha256':source_hash,'source_preserved':True,'renderer':'Cycles CPU','poses':results},indent=2),encoding='utf8')
print('ACTUAL_CLIP_PREVIEWS='+str(out))
