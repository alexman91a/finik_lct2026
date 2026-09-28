"""Repeatable Finik St1 skin cleanup. Run: blender -b --python repair_finik_st1.py -- input.fbx output.fbx audit.blend"""
import json
import math
import sys
from pathlib import Path

import bpy


def arguments():
    args = sys.argv[sys.argv.index("--") + 1:]
    if len(args) != 3:
        raise SystemExit("Expected input.fbx output.fbx audit.blend")
    return tuple(Path(value).resolve() for value in args)


def side_of(name):
    if "Right" in name:
        return -1
    if "Left" in name:
        return 1
    return 0


def is_arm(name):
    return any(part in name for part in ("Shoulder", "Arm", "ForeArm", "Wrist", "Hand"))


def is_leg(name):
    return any(part in name for part in ("UpLeg", "Leg", "Foot", "Toe"))


def counts(mesh):
    values = [sum(group.weight > 1e-5 for group in vertex.groups) for vertex in mesh.data.vertices]
    return {"vertices": len(values), "over_four": sum(value > 4 for value in values), "maximum": max(values)}


def repair(mesh, armature):
    names = {group.index: group.name for group in mesh.vertex_groups}
    bones = armature.data.bones
    center = bones["mixamorig:Hips"].head_local.x
    shoulder_height = max(bones["mixamorig:LeftShoulder"].head_local.z,
                          bones["mixamorig:RightShoulder"].head_local.z)
    head_height = bones["mixamorig:Head"].head_local.z
    shoulder_width = abs(bones["mixamorig:LeftShoulder"].head_local.x -
                         bones["mixamorig:RightShoulder"].head_local.x)
    changed = 0
    cross_leg = 0
    face_arm = 0
    for vertex in mesh.data.vertices:
        point = armature.matrix_world.inverted() @ (mesh.matrix_world @ vertex.co)
        original = {entry.group: entry.weight for entry in vertex.groups if entry.weight > 1e-5}
        if not original:
            continue
        kept = {}
        for index, weight in original.items():
            name = names[index]
            side = side_of(name)
            # Do not let the opposite leg pull a foot or knee across the pelvis.
            if is_leg(name) and side and (point.x - center) * side < -0.025 and point.z < 0.43:
                cross_leg += 1
                continue
            # Head and centre chest must not follow a raised arm. Keep shoulder seams intact.
            if is_arm(name) and abs(point.x - center) < shoulder_width * 0.34 and (
                point.z > head_height - 0.035 or point.z < shoulder_height - 0.13
            ):
                face_arm += 1
                continue
            kept[index] = weight
        if not kept:
            kept = {max(original, key=original.get): original[max(original, key=original.get)]}
        kept = dict(sorted(kept.items(), key=lambda item: item[1], reverse=True)[:4])
        total = sum(kept.values())
        if len(original) != len(kept) or abs(total - 1.0) > 1e-4:
            changed += 1
        for index in original:
            mesh.vertex_groups[index].remove([vertex.index])
        for index, weight in kept.items():
            mesh.vertex_groups[index].add([vertex.index], weight / total, "REPLACE")
    return {"changed_vertices": changed, "removed_opposite_leg": cross_leg,
            "removed_face_or_torso_arm": face_arm}


def pose_tests(armature):
    # These actions are saved in the audit .blend only; the game FBX keeps its original rest pose.
    rotations = {
        "Arms_Up": {"LeftArm": (0, -72, 0), "RightArm": (0, 72, 0)},
        "Arms_Forward": {"LeftArm": (65, 0, 0), "RightArm": (65, 0, 0)},
        "Elbows_Deep": {"LeftForeArm": (0, 0, 115), "RightForeArm": (0, 0, -115)},
        "Step": {"LeftUpLeg": (32, 0, 0), "RightUpLeg": (-28, 0, 0),
                 "LeftLeg": (-28, 0, 0), "RightLeg": (22, 0, 0)},
        "Knee_Up": {"LeftUpLeg": (70, 0, 0), "LeftLeg": (-82, 0, 0)},
        "Foot_Flex": {"LeftFoot": (-32, 0, 0), "RightFoot": (32, 0, 0)},
    }
    for title, changes in rotations.items():
        for bone in armature.pose.bones:
            bone.rotation_mode = "XYZ"
            bone.rotation_euler = (0, 0, 0)
        action = bpy.data.actions.new("QA_" + title)
        action.use_fake_user = True
        armature.animation_data_create().action = action
        for suffix, degrees in changes.items():
            bone = armature.pose.bones.get("mixamorig:" + suffix)
            if not bone:
                continue
            bone.rotation_euler = tuple(math.radians(value) for value in degrees)
            bone.keyframe_insert(data_path="rotation_euler", frame=1)
    armature.animation_data_clear()
    for bone in armature.pose.bones:
        bone.rotation_euler = (0, 0, 0)


def main():
    source, output, audit = arguments()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(source))
    texture_dir = source.parent.parent / "Textures"
    for image in bpy.data.images:
        if "Normal" in image.filepath:
            image.filepath = str(texture_dir / "FinikNormal.png")
        elif "BaseColor" in image.filepath:
            image.filepath = str(texture_dir / "FinikBaseColor.png")
    armature = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE")
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH" and
              any(mod.type == "ARMATURE" for mod in obj.modifiers)]
    if not meshes:
        raise RuntimeError("No skinned mesh found")
    before = {obj.name: counts(obj) for obj in meshes}
    changes = {obj.name: repair(obj, armature) for obj in meshes}
    after = {obj.name: counts(obj) for obj in meshes}
    for obj in meshes:
        for vertex in obj.data.vertices:
            weights = [group.weight for group in vertex.groups if group.weight > 1e-5]
            assert len(weights) <= 4 and (not weights or abs(sum(weights) - 1) < 1e-3)
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.export_scene.fbx(filepath=str(output), use_selection=True, add_leaf_bones=False,
                             bake_anim=False, path_mode="RELATIVE")
    pose_tests(armature)
    bpy.ops.wm.save_as_mainfile(filepath=str(audit))
    print("FINIK_SKIN_AUDIT=" + json.dumps({"before": before, "after": after,
                                             "changes": changes}, sort_keys=True))


if __name__ == "__main__":
    main()
