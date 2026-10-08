# Headless: blender -b art/cat.blend --python art/render_icon.py
# Renders thunderstore/icon.png (256x256): a ginger cat sitting, on a soft blue circle.
import bpy, os
from mathutils import Vector

scene = bpy.context.scene
root = os.path.dirname(os.path.dirname(bpy.data.filepath))
rig = bpy.data.objects["CatRig"]
rig.animation_data.action = bpy.data.actions["Sit"]
scene.frame_set(1)

def base(mat, rgb, rough=0.8):
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = rough
base(bpy.data.materials["Cat_Fur"], (0.50, 0.13, 0.02))
base(bpy.data.materials["Cat_Eye"], (0.25, 0.65, 0.06), 0.3)

mesh = bpy.data.objects["CatMesh"]
dg = bpy.context.evaluated_depsgraph_get()
pts = [mesh.matrix_world @ Vector(c) for c in mesh.evaluated_get(dg).bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
centre = (lo + hi) / 2

cam_data = bpy.data.cameras.new("IconCam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = max(hi - lo) * 1.2
cam = bpy.data.objects.new("IconCam", cam_data)
scene.collection.objects.link(cam)
cam.location = centre + Vector((0.9, -1.4, 0.45)).normalized() * 3
cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.camera = cam

for name, loc, power in (("Key", (2, -3, 4), 900), ("Fill", (-3, -1, 2), 300), ("Rim", (0, 3, 3), 500)):
    l = bpy.data.lights.new(name, "AREA"); l.energy = power; l.size = 3
    o = bpy.data.objects.new(name, l); scene.collection.objects.link(o)
    o.location = centre + Vector(loc)
    o.rotation_euler = (centre - o.location).to_track_quat("-Z", "Y").to_euler()

world = scene.world or bpy.data.worlds.new("W"); scene.world = world
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs["Color"].default_value = (0.9, 0.9, 1, 1); bg.inputs["Strength"].default_value = 0.4

r = scene.render
scene.view_settings.view_transform = "Standard"
r.resolution_x = r.resolution_y = 512
r.resolution_percentage = 100
r.film_transparent = True
r.image_settings.file_format = "PNG"
r.image_settings.color_mode = "RGBA"
r.filepath = os.path.join(root, "art", "export", "icon_cat.png")
bpy.ops.render.render(write_still=True)
print("ICON", r.filepath)

# Composite onto a soft blue circle and downscale to the 256x256 Thunderstore icon.
import numpy as np
img = bpy.data.images.load(r.filepath)
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
yy, xx = np.mgrid[0:h, 0:w]
d = np.hypot(xx - w / 2, yy - h / 2) / (w / 2)
grad = (yy / h)[..., None]
circle_rgb = (1 - grad) * np.array([0.62, 0.74, 0.95]) + grad * np.array([0.86, 0.90, 1.0])
circle_a = np.clip((0.97 - d) * w / 2, 0, 1)[..., None]
bg = np.concatenate([circle_rgb, circle_a], axis=2)
a = px[..., 3:4]
out_a = a + bg[..., 3:4] * (1 - a)
out_rgb = (px[..., :3] * a + bg[..., :3] * bg[..., 3:4] * (1 - a)) / np.maximum(out_a, 1e-6)
img.pixels[:] = np.concatenate([out_rgb, out_a], axis=2).ravel()
img.scale(256, 256)
img.filepath_raw = os.path.join(root, "thunderstore", "icon.png")
img.file_format = "PNG"
img.save()
print("ICON", img.filepath_raw)
