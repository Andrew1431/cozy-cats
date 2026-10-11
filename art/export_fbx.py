# Run headless: blender -b art/cat.blend --python art/export_fbx.py
# (Re)creates the Meow overlay clip, saves the .blend, and exports CatModel.fbx and CatTree.fbx straight into the Unity project.
import bpy, math, os
from mathutils import Quaternion

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "unity", "CozyCatsAssets", "Assets", "Cat", "CatModel.fbx")
TREE_OUT = os.path.join(HERE, "..", "unity", "CozyCatsAssets", "Assets", "CatTree", "CatTree.fbx")

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
    # The .blend may have been saved in Pose Mode (or with the cat hidden while posing tree cats); export needs it selectable.
    show_collection("Cat")
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


def show_collection(name):
    lc = bpy.context.view_layer.layer_collection.children.get(name)
    if lc:
        lc.exclude = False
        lc.hide_viewport = False
    for o in bpy.data.collections[name].objects:
        o.hide_set(False)


# The tree goes out as one mesh (the build-mode ghost and outline can only show a single mesh) plus an empty per
# resting spot, named Spot_<Name>_<Clip> from the Spot.<Name> empties and their "pose" property.
# The TreePreview cats are only for posing and never exported.
def export_tree():
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    show_collection("CatTree")
    col = bpy.data.collections["CatTree"]
    dg = bpy.context.evaluated_depsgraph_get()
    temps = []
    for o in col.objects:
        if o.type != "MESH":
            continue
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
        me.transform(o.matrix_world)
        t = bpy.data.objects.new("_tmp", me)
        bpy.context.scene.collection.objects.link(t)
        temps.append(t)
    for o in bpy.context.view_layer.objects:
        o.select_set(o in temps)
    bpy.context.view_layer.objects.active = temps[0]
    bpy.ops.object.join()
    tree = temps[0]
    tree.name = tree.data.name = "TreeMesh"
    spots = []
    for o in col.objects:
        if o.name.startswith("Spot."):
            e = bpy.data.objects.new(f"Spot_{o.name[5:]}_{o['pose']}", None)
            e.matrix_world = o.matrix_world.copy()
            bpy.context.scene.collection.objects.link(e)
            spots.append(e)
    for o in bpy.context.view_layer.objects:
        o.select_set(o is tree or o in spots)
    os.makedirs(os.path.dirname(TREE_OUT), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=TREE_OUT, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_anim=False, mesh_smooth_type="FACE", use_mesh_modifiers=True, use_custom_props=False)
    print("exported", TREE_OUT, os.path.getsize(TREE_OUT), [e.name for e in spots])
    for o in spots + [tree]:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me:
            bpy.data.meshes.remove(me)


build_meow()
bpy.ops.wm.save_mainfile()
export()
export_tree()
print("actions:", [a.name for a in bpy.data.actions])
