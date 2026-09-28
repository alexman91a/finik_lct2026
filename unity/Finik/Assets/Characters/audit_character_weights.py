"""Audit the nine runtime character FBX files without changing their sources."""
import json
import sys
from pathlib import Path

import bpy


def audit(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(path))
    armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH" and obj.vertex_groups]
    report = {"model": str(path), "armatures": len(armatures), "meshes": []}
    for mesh in meshes:
        names = {group.index: group.name for group in mesh.vertex_groups}
        over_four = unnormalized = unweighted = 0
        arm_at_hips = cross_leg = 0
        armature = next((mod.object for mod in mesh.modifiers if mod.type == "ARMATURE" and mod.object), None)
        bones = armature.data.bones if armature else None
        hips = next((bone for bone in bones if bone.name.endswith("Hips")), None) if bones else None
        hips_z = hips.head_local.z if hips else None
        center_x = hips.head_local.x if hips else 0
        shoulder_bones = [bone for bone in bones if bone.name.endswith("Shoulder")] if bones else []
        shoulder_width = abs(shoulder_bones[0].head_local.x - shoulder_bones[1].head_local.x) if len(shoulder_bones) == 2 else 0
        to_armature = armature.matrix_world.inverted() @ mesh.matrix_world if armature else None
        examples = []
        for vertex in mesh.data.vertices:
            weights = [(names.get(item.group, ""), item.weight) for item in vertex.groups if item.weight > 1e-5]
            if not weights:
                unweighted += 1
                continue
            over_four += len(weights) > 4
            unnormalized += abs(sum(weight for _, weight in weights) - 1) > 1e-3
            if hips_z is None:
                continue
            point = to_armature @ vertex.co
            arm_weight = sum(weight for name, weight in weights if any(term in name for term in ("Arm", "Hand", "Shoulder")))
            if point.z < hips_z - 0.04 and abs(point.x - center_x) < shoulder_width * 1.5 and arm_weight > 0.1:
                arm_at_hips += 1
                if len(examples) < 6:
                    examples.append([vertex.index, [round(v, 3) for v in point],
                                     [[name, round(weight, 2)] for name, weight in weights]])
            for name, weight in weights:
                if weight < 0.1 or not any(term in name for term in ("Leg", "Foot", "Toe")):
                    continue
                side = 1 if "Left" in name else -1 if "Right" in name else 0
                if side and (point.x - center_x) * side < -0.04 and point.z < hips_z - 0.1:
                    cross_leg += 1
                    break
        report["meshes"].append({"name": mesh.name, "vertices": len(mesh.data.vertices),
                                 "over_four": over_four, "unnormalized": unnormalized,
                                 "unweighted": unweighted, "arm_below_hips": arm_at_hips,
                                 "cross_leg": cross_leg, "examples": examples})
    return report


root = Path(sys.argv[-1])
paths = [root] if root.is_file() else sorted(root.glob("*/St?/Models/*.fbx"))
for path in paths:
    if root.is_file() or "SkinRepaired" not in path.name:
        print("CHARACTER_WEIGHT_AUDIT=" + json.dumps(audit(path), ensure_ascii=False))
