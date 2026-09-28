"""Главный сборочный пайплайн аксессуаров Финика.

Порядок: собрать по замерам тела -> точечная подгонка мелких деталей -> отчёт -> экспорт.
Всё воспроизводимо: finik_build.rebuild() пересобирает набор с нуля.

Экспорт устроен так, чтобы в Unity аксессуар цеплялся без ручной подгонки: каждый FBX
содержит детали в координатах персонажа и копию скелета. FinikAccessoryRig совмещает
пространства по положениям суставов (Hips/Head/LeftHand) и вешает детали на кость через
bindpose персонажа — см. unity/Finik/Assets/Finik/Scripts/Accessories.
"""
import bpy, os, json, math
from mathutils import Vector

import finik_lib as lib
import finik_curves, finik_glasses, finik_backpacks, finik_gear

# слот -> кость скелета Meshy (имена как в FBX персонажа)
SLOTS = {
    "glasses": "Head", "cap": "Head", "headphones": "Head",
    "scarf": "neck", "backpack": "Spine01", "star": "Spine01", "watch": "LeftForeArm",
}

# ассет -> (слот, человекочитаемое имя, вайб для UI)
CATALOG = {
    "Glasses_Round":     ("glasses", "Круглые", "умный вайб"),
    "Glasses_Nerd":      ("glasses", "Нёрд", "крупная тёмная оправа"),
    "Glasses_Aviator":   ("glasses", "Авиаторы", "затемнённые линзы"),
    "Glasses_Retro":     ("glasses", "Ретро", "фиолетовая оправа"),
    "Glasses_Star":      ("glasses", "Звёзды", "тусовочные"),
    "Glasses_Visor":     ("glasses", "Визор", "спортивный цельный"),
    "Backpack_School":   ("backpack", "Школьный", "клапан, карман, пряжки"),
    "Backpack_RollTop":  ("backpack", "Ролл-топ", "городской минимализм"),
    "Backpack_Piggy":    ("backpack", "Копилка", "прорезь и монетка"),
    "Backpack_FoxMini":  ("backpack", "Лисёнок", "детский с мордочкой"),
    "Gear_Cap":          ("cap", "Кепка", "козырёк вперёд"),
    "Gear_Beanie":       ("cap", "Шапка", "с помпоном"),
    "Gear_Headphones":   ("headphones", "Наушники", "накладные"),
    "Gear_Scarf":        ("scarf", "Шарф", "вязаный в полоску"),
    "Gear_Badge":        ("star", "Значок", "звезда на груди"),
    "Gear_Watch":        ("watch", "Смарт-часы", "финансовый трекер"),
}

# детали, которые должны облегать тело (подгоняются по поверхности)
FIT_PARTS = ("Gear_Badge_Ring", "Glasses_Visor_Frame")


def groups(prefixes=("Glasses_", "Backpack_", "Gear_")):
    return [o for o in bpy.data.objects
            if o.type == 'EMPTY' and o.parent is None and o.name in CATALOG
            and any(o.name.startswith(p) for p in prefixes)]


def rebuild(report=True):
    """Полная пересборка набора: геометрия по замерам -> подгонка мелких деталей -> отчёт."""
    import importlib
    for m in (lib, finik_curves, finik_glasses, finik_backpacks, finik_gear):
        importlib.reload(m)
    arm = bpy.data.objects["Finik_Armature"]
    arm.data.pose_position = 'REST'                    # аксессуары строятся по bind-позе
    bpy.data.objects["Finik_Mesh"].hide_viewport = False
    bpy.context.view_layer.update()

    finik_glasses.build_all(lib)
    finik_backpacks.build_all(lib)
    finik_gear.build_all(lib)
    for g in groups():
        g.location = (0, 0, 0)
        g.hide_viewport = False
        for c in g.children:
            c.hide_viewport = False
    bpy.context.view_layer.update()

    bvh = lib.body_bvh()
    for o in bpy.data.objects:
        if o.type != 'MESH' or not any(k in o.name for k in FIT_PARTS):
            continue
        for i in range(5):
            lib.fit_outside(o, bvh, clearance=0.005, relax=2 if i < 4 else 0)
    bpy.context.view_layer.update()
    if report:
        print(penetration_report())
    return groups()


def penetration_report():
    """Таблица проникновения деталей в тело — чтобы дефекты были видны числом."""
    bvh = lib.body_bvh()
    rows = []
    for g in sorted(groups(), key=lambda o: o.name):
        worst_f, worst_d, worst_n = 0.0, 0.0, ""
        for c in g.children:
            if c.type != 'MESH':
                continue
            f, d = lib.measure_penetration(c, bvh)
            if d > worst_d:
                worst_f, worst_d, worst_n = f, d, c.name
        rows.append((g.name, worst_f, worst_d, worst_n))
    out = [f"{'ассет':20s} {'макс.погружение детали':>24s}   деталь"]
    out.append("-" * 72)
    for name, f, d, n in rows:
        flag = "  <-- проверить" if d > 0.030 else ""
        out.append(f"{name:20s} {d*1000:8.1f} мм ({f*100:4.1f}% вершин)   {n}{flag}")
    return "\n".join(out)


# ---------------------------------------------------------------- экспорт
def _select(objs):
    for o in bpy.data.objects:
        try:
            o.select_set(False)
        except RuntimeError:
            pass
    for o in objs:
        o.hide_viewport = False
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0] if objs else None


def _linear_to_srgb(c):
    return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def _material_entry(m):
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    inp = bsdf.inputs
    col = inp["Base Color"].default_value
    e = {
        "name": m.name,
        "color": [round(_linear_to_srgb(c), 4) for c in col[:3]],       # sRGB, как ждёт Unity
        "alpha": round(inp["Alpha"].default_value, 3) if "Alpha" in inp else 1.0,
        "smoothness": round(1.0 - inp["Roughness"].default_value, 3),
        "metallic": round(inp["Metallic"].default_value, 3),
        "emission": [0.0, 0.0, 0.0],
        "emissionStrength": 0.0,
    }
    if "Emission Strength" in inp and inp["Emission Strength"].default_value > 0:
        ec = inp["Emission Color"].default_value
        e["emission"] = [round(_linear_to_srgb(c), 4) for c in ec[:3]]
        e["emissionStrength"] = round(inp["Emission Strength"].default_value, 3)
    return e


def export(out_dir, manifest_path=None):
    """Каждый ассет — отдельный FBX: детали в координатах персонажа + копия скелета.

    Скелет нужен не для скиннинга: по положениям его суставов (Hips/Head/LeftHand)
    FinikAccessoryRig в Unity вычисляет, как совместить пространство аксессуара с
    персонажем. Положения суставов не зависят от ориентации осей костей, поэтому
    различия осей/единиц/поворота корня между FBX поглощаются автоматически."""
    os.makedirs(out_dir, exist_ok=True)
    arm = bpy.data.objects["Finik_Armature"]
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()

    items = []
    for g in sorted(groups(), key=lambda o: o.name):
        parts = [c for c in g.children if c.type == 'MESH']
        if not parts:
            continue
        g.location = (0, 0, 0)
        for c in parts:
            c.hide_viewport = False
        bpy.context.view_layer.update()
        slot, title, vibe = CATALOG[g.name]
        path = os.path.join(out_dir, g.name + ".fbx")
        _select(parts + [arm])
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={'MESH', 'ARMATURE'},
            use_mesh_modifiers=True, mesh_smooth_type='FACE',
            use_armature_deform_only=False, add_leaf_bones=False, bake_anim=False,
            apply_scale_options='FBX_SCALE_UNITS', global_scale=1.0,
            axis_forward='-Z', axis_up='Y', path_mode='AUTO', embed_textures=False)
        tris = sum(sum(len(p.vertices) - 2 for p in c.data.polygons) for c in parts)
        mats = []
        for c in parts:
            for ms in c.material_slots:
                if ms.material is not None and all(x["name"] != ms.material.name for x in mats):
                    mats.append(_material_entry(ms.material))
        verts = [c.matrix_world @ v.co for c in parts for v in c.data.vertices]
        cen = sum(verts, Vector()) / len(verts)
        items.append({
            "id": g.name.lower(), "name": g.name, "title": title, "vibe": vibe,
            "slot": slot, "bone": SLOTS[slot], "file": g.name + ".fbx",
            "parts": [c.name for c in parts], "triangles": tris, "materials": mats,
            "centroid": [round(x, 5) for x in cen],
        })

    manifest = {
        "schema": 2,
        "generator": "art/blender/scripts/finik_build.py",
        "alignJoints": ["Hips", "Head", "LeftHand"],
        "joints": [{"name": b, "position": [round(x, 5) for x in arm.matrix_world @ arm.data.bones[b].head_local]}
                   for b in ("Hips", "Head", "LeftHand")],
        "note": "Детали экспортированы в координатах персонажа вместе с копией скелета. "
                "FinikAccessoryRig совмещает пространства по положениям суставов alignJoints "
                "и вешает деталь на кость bone через bindpose персонажа.",
        "items": items,
    }
    if manifest_path:
        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, ensure_ascii=False, indent=2)
    return manifest
