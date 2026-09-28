"""Normalize the supplied Cat St2 FBX without changing its mesh or skin weights."""

import bpy
import sys


source, destination = sys.argv[sys.argv.index("--") + 1 :]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=source, global_scale=100)

armature = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
mesh = next(obj for obj in bpy.data.objects if obj.type == "MESH" and obj.vertex_groups)
for obj in list(bpy.data.objects):
    if obj not in (armature, mesh):
        bpy.data.objects.remove(obj, do_unlink=True)

armature.name = "CatSt2Armature"
mesh.name = "CatSt2Mesh"
bpy.ops.object.select_all(action="DESELECT")
armature.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = armature
bpy.ops.export_scene.fbx(
    filepath=destination,
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    path_mode="COPY",
    embed_textures=True,
    bake_anim=True,
    add_leaf_bones=False,
)
