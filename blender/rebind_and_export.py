import os
import shutil
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
gltf_path, out_dir = argv[0], argv[1]
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=gltf_path)
scene = bpy.context.scene
arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]

for o in list(bpy.data.objects):
    if o.type == "MESH" and o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o, do_unlink=True)
mesh = [o for o in bpy.data.objects if o.type == "MESH"][0]
print("armature:", arm.name, "bones:", len(arm.pose.bones), "| mesh:", mesh.name, "verts:", len(mesh.data.vertices))

REST_FRAME = 163
HOP_SRC = list(range(163, 197)) + list(range(214, 224))

# parents before children
ordered = sorted(arm.pose.bones, key=lambda b: len(b.parent_recursive))


def sample_matrices(frames):
    out = []
    for f in frames:
        scene.frame_set(f)
        bpy.context.view_layer.update()
        out.append({pb.name: pb.matrix.copy() for pb in arm.pose.bones})
    return out


hop_mats = sample_matrices(HOP_SRC)

# 1. Bake the sitting pose into the mesh (apply the Armature modifier at the rest frame).
scene.frame_set(REST_FRAME)
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
mesh_eval = mesh.evaluated_get(dg)
baked = bpy.data.meshes.new_from_object(mesh_eval, preserve_all_data_layers=True, depsgraph=dg)
old_data = mesh.data
mesh.data = baked
bpy.data.meshes.remove(old_data)
print("baked mesh verts:", len(mesh.data.vertices), "| vertex groups:", len(mesh.vertex_groups))

# 2. Make the sitting pose the armature's rest (bind) pose.
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")

# The Armature modifier must still exist (new_from_object keeps the object's modifiers).
mods = [m.name for m in mesh.modifiers]
print("modifiers on mesh:", mods)

# 3. Re-key the Hop against the new rest pose.
arm.animation_data_create()
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"


def make_action(name, per_frame_matrices):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    for i, mats in enumerate(per_frame_matrices):
        frame = 1 + i
        for pb in ordered:
            if mats is None:
                pb.matrix_basis = Matrix.Identity(4)
            else:
                pb.matrix = mats[pb.name]
            bpy.context.view_layer.update()
            pb.keyframe_insert("location", frame=frame)
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("scale", frame=frame)
    return act


for act in list(bpy.data.actions):
    bpy.data.actions.remove(act)

idle = make_action("Idle", [None, None])
hop = make_action("Hop", hop_mats)


def blended_action(name, source, factor):
    """A smaller version of an action: every bone's offset from the sitting (rest) pose is scaled by `factor`."""
    arm.animation_data.action = source
    samples = []
    for i in range(len(HOP_SRC)):
        scene.frame_set(1 + i)
        bpy.context.view_layer.update()
        samples.append({pb.name: (pb.location.copy(), pb.rotation_quaternion.copy(), pb.scale.copy()) for pb in arm.pose.bones})

    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    identity = Quaternion((1.0, 0.0, 0.0, 0.0))
    for i, snap in enumerate(samples):
        frame = 1 + i
        for pb in arm.pose.bones:
            loc, rot, scl = snap[pb.name]
            pb.location = loc * factor
            pb.rotation_quaternion = identity.slerp(rot, factor)
            pb.scale = Vector((1.0, 1.0, 1.0)) + (scl - Vector((1.0, 1.0, 1.0))) * factor
            pb.keyframe_insert("location", frame=frame)
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("scale", frame=frame)
    return act


# A small hop for turning around on the spot (about 45 % of the full leap).
turn_hop = blended_action("TurnHop", hop, 0.45)
arm.animation_data.action = hop
scene.frame_start, scene.frame_end = 1, len(HOP_SRC)
print("Hop frames:", len(HOP_SRC), "(%.3fs)" % ((len(HOP_SRC) - 1) / scene.render.fps), "| Idle: 2 frames | TurnHop: same length")

# textures + license
tex_dir = os.path.join(out_dir, "textures")
os.makedirs(tex_dir, exist_ok=True)
src_dir = os.path.dirname(gltf_path)
for fn in os.listdir(os.path.join(src_dir, "textures")):
    shutil.copy(os.path.join(src_dir, "textures", fn), os.path.join(tex_dir, fn))
shutil.copy(os.path.join(src_dir, "license.txt"), os.path.join(out_dir, "license.txt"))

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = arm

fbx_path = os.path.join(out_dir, "frog_schlegel_hop.fbx")
bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                         bake_anim_use_nla_strips=False, bake_anim_step=1.0, path_mode="COPY",
                         embed_textures=False, axis_forward="-Z", axis_up="Y")
print("FBX exported:", fbx_path)

blend_path = os.path.join(out_dir, "frog_schlegel_hop.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_path)
print("BLEND saved:", blend_path)
