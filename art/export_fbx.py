# Run headless: blender -b art/cat.blend --python art/export_fbx.py
# (Re)creates the Meow overlay clip, saves the .blend, and exports CatModel.fbx straight into the Unity project.
import bpy, math, os
from mathutils import Quaternion

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "unity", "CozyCatsAssets", "Assets", "Cat", "CatModel.fbx")

arm = bpy.data.objects["CatRig"]
mesh = bpy.data.objects["CatMesh"]
jaw = arm.pose.bones["Jaw"]
jaw.rotation_mode = "QUATERNION"


def build_meow():
    act = bpy.data.actions.get("Meow")
    if act:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new("Meow")
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    # Quick open, short hold, ease shut: about half a second at 24fps.
    for frame, deg in ((1, 0), (3, 24), (5, 26), (8, 22), (12, 0)):
        jaw.rotation_quaternion = Quaternion((1, 0, 0), math.radians(deg))
        jaw.keyframe_insert("rotation_quaternion", frame=frame)
    act.use_frame_range = True
    act.frame_start, act.frame_end = 1, 12
    jaw.rotation_quaternion = (1, 0, 0, 0)


def export():
    arm.animation_data.action = None
    for p in arm.pose.bones:
        p.rotation_quaternion = (1, 0, 0, 0)
        p.location = (0, 0, 0)
        p.scale = (1, 1, 1)
    # The .blend may have been saved in Pose Mode; headless ops need Object Mode.
    bpy.context.view_layer.objects.active = arm
    if arm.mode != "OBJECT":
        with bpy.context.temp_override(active_object=arm, object=arm):
            bpy.ops.object.mode_set(mode="OBJECT")
    for o in bpy.context.view_layer.objects:
        o.select_set(o in (arm, mesh))
    bpy.ops.export_scene.fbx(
        filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        mesh_smooth_type="FACE", use_mesh_modifiers=True)
    print("exported", OUT, os.path.getsize(OUT))


build_meow()
bpy.ops.wm.save_mainfile()
export()
print("actions:", [a.name for a in bpy.data.actions])
