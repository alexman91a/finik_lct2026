"""Check that the exported repair changes only weights, not geometry or rig."""
import sys

import bpy


def load(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=path)
    mesh = next(obj for obj in bpy.data.objects if obj.type == "MESH" and obj.name == "model")
    armature = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    vertices = [tuple(vertex.co) for vertex in mesh.data.vertices]
    weights = [{mesh.vertex_groups[group.group].name: group.weight for group in vertex.groups}
               for vertex in mesh.data.vertices]
    return vertices, weights, {bone.name for bone in armature.data.bones}


original = load(sys.argv[-2])
repaired = load(sys.argv[-1])
assert len(original[0]) == len(repaired[0])
assert original[2] == repaired[2]
assert all(max(abs(a - b) for a, b in zip(first, second)) < 1e-5
           for first, second in zip(original[0], repaired[0]))
changed = 0
for before, after in zip(original[1], repaired[1]):
    if any(abs(before.get(name, 0) - after.get(name, 0)) > 1e-4
           for name in before.keys() | after.keys()):
        changed += 1
        assert sum(weight for name, weight in after.items()
                   if any(term in name for term in ("Arm", "Hand", "Shoulder"))) < 1e-4
    assert len([weight for weight in after.values() if weight > 1e-5]) <= 4
    assert abs(sum(after.values()) - 1) < 1e-3
assert changed > 0
print(f"CAT_DRESS_VALIDATED vertices={len(original[0])} bones={len(original[2])} changed={changed}")
