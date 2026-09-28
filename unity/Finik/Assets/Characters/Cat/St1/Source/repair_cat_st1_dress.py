"""Remove arm weights from the pink waist seam. Run with -- input.fbx output.fbx."""
import json
import sys
from pathlib import Path

import bpy


source, output = (Path(value).resolve() for value in sys.argv[-2:])
bpy.ops.import_scene.fbx(filepath=str(source))
mesh = next(obj for obj in bpy.data.objects if obj.type == "MESH" and obj.name == "model")
armature = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
image = bpy.data.images.load(str(source.parent.parent / "Textures" / "CatBaseColor.png"))
pixels = list(image.pixels)
width, height = image.size
uv = mesh.data.uv_layers.active.data
pink = set()
for loop in mesh.data.loops:
    u, v = uv[loop.index].uv
    x = min(width - 1, max(0, int(u % 1 * width)))
    y = min(height - 1, max(0, int(v % 1 * height)))
    r, g, b = pixels[4 * (y * width + x):4 * (y * width + x) + 3]
    if r > .82 and .4 < g < .74 and .4 < b < .74 and r > g * 1.3:
        pink.add(loop.vertex_index)

changed = []
for index in pink:
    vertex = mesh.data.vertices[index]
    # The skirt starts below the arm; do not change sleeve or paw vertices.
    if not (.70 < vertex.co.z < .77 and abs(vertex.co.x) < .17):
        continue
    weights = [(mesh.vertex_groups[item.group], item.weight) for item in vertex.groups]
    arm = [(group, weight) for group, weight in weights if any(term in group.name for term in ("Arm", "Hand", "Shoulder"))]
    if not arm:
        continue
    kept = [(group, weight) for group, weight in weights if (group, weight) not in arm]
    if not kept:
        kept = [(mesh.vertex_groups["Hips"], 1.0)]
    total = sum(weight for _, weight in kept)
    for group, _ in weights:
        group.remove([index])
    for group, weight in kept:
        group.add([index], weight / total, "REPLACE")
    changed.append(index)

for obj in bpy.data.objects:
    obj.select_set(False)
armature.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = armature
output.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.fbx(filepath=str(output), use_selection=True, add_leaf_bones=False,
                         bake_anim=False, path_mode="RELATIVE")
print("CAT_DRESS_REPAIR=" + json.dumps({"vertices": len(changed), "indices": sorted(changed)}))
