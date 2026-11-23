"""Render Iris quarter-view data passes for 2D stylization.

Usage:
  blender -b iris.blend --python render_passes.py -- OUT_DIR ACTION FRAME HEIGHT_PX [lowres] [passes=albedo,normal,...]

Writes linear EXR passes (albedo, normal, depth, lambert, key, ao) of the
character seen from an orthographic 45-degree quarter view (camera pitched
45 degrees down, yawed 45 degrees so Iris faces screen down-left).
"""
import bpy
import math
import os
import sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]
ACTION = argv[1]
FRAME = int(argv[2])
HEIGHT = int(argv[3])
LOWRES = "lowres" in argv[4:]
PASSES = ["albedo", "normal", "depth", "lambert", "key", "ao"]
for a in argv[4:]:
    if a.startswith("passes="):
        PASSES = a.split("=", 1)[1].split(",")
YAW = 45.0
for a in argv[4:]:
    if a.startswith("yaw="):
        YAW = float(a.split("=", 1)[1])
os.makedirs(OUT, exist_ok=True)

scene = bpy.context.scene
arm = bpy.data.objects["Armature"]
arm.animation_data.action = bpy.data.actions[ACTION]
scene.frame_set(FRAME)

KEEP = {"Head", "Hair", "Armature"}
for o in bpy.data.objects:
    if o.type in {"MESH", "CURVE", "GPENCIL", "GREASEPENCIL"} and o.name not in KEEP:
        o.hide_render = True
    if o.type == "LIGHT":
        o.hide_render = True

# --- camera: orthographic, 45 deg pitch, YAW deg around Z ---------------------------------
cam_data = bpy.data.cameras.new("QuarterCam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("QuarterCam", cam_data)
scene.collection.objects.link(cam)
pitch = math.radians(45.0)
yaw = math.radians(YAW)
cam.rotation_euler = (pitch, 0.0, yaw)
target = Vector((0.0, 0.0, 0.47))
view = Vector((-math.sin(yaw) * math.sin(pitch), math.cos(yaw) * math.sin(pitch), -math.cos(pitch)))
cam.location = target - view * 6.0
cam_data.ortho_scale = 1.18
cam_data.clip_start = 0.1
cam_data.clip_end = 20.0
scene.camera = cam

r = scene.render
r.resolution_y = HEIGHT
r.resolution_x = int(round(HEIGHT * 0.8))
r.resolution_percentage = 100
r.film_transparent = True
r.image_settings.file_format = "OPEN_EXR"
r.image_settings.color_depth = "32"
r.image_settings.exr_codec = "ZIP"
r.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.view_settings.exposure = 0.0
scene.view_settings.gamma = 1.0
r.engine = "CYCLES"
cy = scene.cycles
cy.device = "CPU"
cy.use_adaptive_sampling = False
cy.max_bounces = 4
cy.diffuse_bounces = 2
cy.glossy_bounces = 0
cy.transparent_max_bounces = 8
cy.pixel_filter_type = "BOX" if LOWRES else "BLACKMAN_HARRIS"
cy.filter_width = 0.01 if LOWRES else 1.5

world = bpy.data.worlds.new("PassWorld")
world.use_nodes = True
scene.world = world
bg = world.node_tree.nodes.get("Background")


def set_world(strength):
    bg.inputs["Color"].default_value = (1, 1, 1, 1)
    bg.inputs["Strength"].default_value = strength


# key light: from screen upper-left and toward the viewer
sun_data = bpy.data.lights.new("KeySun", "SUN")
sun_data.energy = 3.0
sun_data.angle = math.radians(2.0)
sun = bpy.data.objects.new("KeySun", sun_data)
scene.collection.objects.link(sun)
cam_right = Vector((math.cos(yaw), math.sin(yaw), 0.0))
to_cam = -view
light_from = (to_cam * 1.0 - cam_right * 0.7 + Vector((0, 0, 0.25))).normalized()
sun.rotation_euler = (-light_from).to_track_quat("-Z", "Y").to_euler()

# --- material variants --------------------------------------------------------------------
orig = {}
for obj_name in ("Head", "Hair"):
    obj = bpy.data.objects[obj_name]
    orig[obj_name] = [s.material for s in obj.material_slots]


def base_image(mat):
    out = mat.node_tree.nodes.get("Material Output")
    bsdf = [n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"][0]
    link = bsdf.inputs["Base Color"].links[0]
    return link.from_node.image


def make_variant(mat, kind):
    m = bpy.data.materials.new(f"{mat.name}__{kind}")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    if kind == "albedo":
        uv = nt.nodes.new("ShaderNodeUVMap")
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = base_image(mat)
        tex.interpolation = "Cubic" if not LOWRES else "Linear"
        nt.links.new(uv.outputs["UV"], tex.inputs["Vector"])
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(tex.outputs["Color"], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    elif kind == "normal":
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        vt = nt.nodes.new("ShaderNodeVectorTransform")
        vt.vector_type = "NORMAL"
        vt.convert_from = "WORLD"
        vt.convert_to = "CAMERA"
        nt.links.new(geo.outputs["Normal"], vt.inputs["Vector"])
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(vt.outputs["Vector"], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    elif kind == "depth":
        camd = nt.nodes.new("ShaderNodeCameraData")
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(camd.outputs["View Z Depth"], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    elif kind in ("lambert", "key"):
        d = nt.nodes.new("ShaderNodeBsdfDiffuse")
        d.inputs["Color"].default_value = (1, 1, 1, 1)
        nt.links.new(d.outputs[0], out.inputs["Surface"])
    elif kind == "ao":
        ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
        ao.samples = 16
        ao.inputs["Distance"].default_value = 0.06
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(ao.outputs["AO"], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    return m


def apply(kind):
    for obj_name, mats in orig.items():
        obj = bpy.data.objects[obj_name]
        for i, mat in enumerate(mats):
            obj.material_slots[i].material = make_variant(mat, kind)
        obj.visible_shadow = kind != "lambert"


SETTINGS = {
    "albedo": dict(samples=1 if LOWRES else 24, world=0.0, sun=False, denoise=False),
    "normal": dict(samples=1 if LOWRES else 24, world=0.0, sun=False, denoise=False),
    "depth": dict(samples=1 if LOWRES else 8, world=0.0, sun=False, denoise=False),
    "lambert": dict(samples=16 if LOWRES else 48, world=0.0, sun=True, denoise=True),
    "key": dict(samples=16 if LOWRES else 64, world=0.0, sun=True, denoise=True),
    "ao": dict(samples=16 if LOWRES else 48, world=0.0, sun=False, denoise=True),
}

for kind in PASSES:
    s = SETTINGS[kind]
    apply(kind)
    cy.samples = s["samples"]
    cy.use_denoising = s["denoise"]
    sun.hide_render = not s["sun"]
    set_world(s["world"])
    r.filepath = os.path.join(OUT, f"{kind}.exr")
    bpy.ops.render.render(write_still=True)
    print("WROTE", r.filepath)
