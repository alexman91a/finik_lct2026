"""Подготовка сцены: импорт Финика, свет, ракурсы вьюпорта, витрина, автосохранение."""
import bpy, math, os
from mathutils import Vector, Euler

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CHAR_FBX = os.path.join(REPO, "unity", "Finik", "Assets", "Meshy_AI_Hoodie_Fox_biped",
                        "Meshy_AI_Hoodie_Fox_biped_Animation_Idle_11_withSkin.fbx")
BLEND_PATH = os.path.join(REPO, "art", "blender", "finik_accessories.blend")
UNITY_ACCESSORIES = os.path.join(REPO, "unity", "Finik", "Assets", "Finik", "Accessories")
SHOWCASE_DIR = os.path.join(REPO, "docs", "accessories")
PREFIXES = ("Glasses_", "Backpack_", "Gear_")


def import_character():
    """Импорт персонажа как в Unity-сетапе: Idle-FBX, без вспомогательной Icosphere, rest-поза."""
    if "Finik_Mesh" in bpy.data.objects:
        return bpy.data.objects["Finik_Mesh"]
    for n in ("Cube",):
        if n in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=CHAR_FBX, automatic_bone_orientation=True)
    new = [bpy.data.objects[n] for n in bpy.data.objects.keys() if n not in before]
    for o in new:
        if o.name.startswith("Icosphere"):
            bpy.data.objects.remove(o, do_unlink=True)
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith("model"))
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh.name = mesh.data.name = "Finik_Mesh"
    arm.name = arm.data.name = "Finik_Armature"
    if mesh.material_slots and mesh.material_slots[0].material:
        mesh.material_slots[0].material.name = "Finik_Meshy"
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    return mesh


def lights():
    """Трёхточечный свет + ровный фон для витрин."""
    for n in ("Acc_Key", "Acc_Fill", "Acc_Rim"):
        if n in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    if "Light" in bpy.data.objects:
        bpy.data.objects["Light"].hide_viewport = True
        bpy.data.objects["Light"].hide_render = True
    for name, loc, energy, size, rot in (
            ("Acc_Key", (-1.4, -2.0, 2.2), 380, 2.5, (52, 0, -35)),
            ("Acc_Fill", (1.8, -1.6, 1.2), 150, 3.0, (72, 0, 48)),
            ("Acc_Rim", (0.4, 2.2, 2.0), 240, 2.0, (125, 0, 190))):
        d = bpy.data.lights.new(name, type='AREA')
        d.energy, d.size = energy, size
        o = bpy.data.objects.new(name, d)
        o.location = loc
        o.rotation_euler = [math.radians(a) for a in rot]
        bpy.context.scene.collection.objects.link(o)
    w = bpy.context.scene.world or bpy.data.worlds.new("World")
    bpy.context.scene.world = w
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = (0.26, 0.27, 0.30, 1.0)
    bg.inputs[1].default_value = 0.55


def view_areas():
    for win in bpy.context.window_manager.windows:
        for area in win.screen.areas:
            if area.type == 'VIEW_3D':
                yield area


def set_view(center=(0, -0.05, 1.34), dist=0.45, yaw=0.0, pitch=0.0, ortho=True):
    """yaw/pitch в градусах; yaw=0 — строго спереди (камера со стороны -Y)."""
    rot = Euler((math.radians(90 + pitch), 0, math.radians(yaw)), 'XYZ').to_quaternion()
    for area in view_areas():
        sp = area.spaces.active
        sp.shading.type = 'MATERIAL'
        sp.shading.use_scene_lights = True
        sp.shading.use_scene_world = True
        sp.overlay.show_overlays = False
        r3d = sp.region_3d
        r3d.view_perspective = 'ORTHO' if ortho else 'PERSP'
        r3d.view_rotation = rot
        r3d.view_location = Vector(center)
        r3d.view_distance = dist
        area.tag_redraw()


def acc_groups(prefix):
    return [o for o in bpy.data.objects
            if o.type == 'EMPTY' and o.parent is None and o.name.startswith(prefix)]


def _set_group_visible(g, vis):
    g.hide_viewport = not vis
    g.hide_render = not vis
    for c in g.children:
        c.hide_viewport = not vis
        c.hide_render = not vis


def _world_bbox(objs):
    pts = []
    for o in objs:
        pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def showcase(prefix, cols=3, gap=0.12, hide_char=True, yaw=18, pitch=-12):
    """Витрина: ассеты сеткой, каждый центрирован в ячейке по своему bbox, с зазором gap."""
    for n in ("Finik_Mesh", "Finik_Armature"):
        if n in bpy.data.objects:
            bpy.data.objects[n].hide_viewport = hide_char
            bpy.data.objects[n].hide_render = hide_char
    for p in PREFIXES:
        for g in acc_groups(p):
            g.location = (0, 0, 0)
            _set_group_visible(g, p == prefix)
    bpy.context.view_layer.update()
    gs = sorted(acc_groups(prefix), key=lambda o: o.name)
    boxes = []
    for g in gs:
        lo, hi = _world_bbox([c for c in g.children if c.type == 'MESH'])
        boxes.append(((lo + hi) / 2, hi - lo))
    cw = max(s.x for _, s in boxes) + gap
    ch = max(s.z for _, s in boxes) + gap
    rows = math.ceil(len(gs) / cols)
    for i, (g, (cen, _)) in enumerate(zip(gs, boxes)):
        r, c = divmod(i, cols)
        target = Vector(((c - (cols - 1) / 2) * cw, -0.9, 1.3 + ((rows - 1) / 2 - r) * ch))
        g.location = target - cen
    bpy.context.view_layer.update()
    fit_view(prefix, yaw=yaw, pitch=pitch)
    return [g.name for g in gs]


def fit_view(prefix, yaw=18, pitch=-12, pad=1.08):
    """Кадрирует по проекции bbox видимых деталей на экранные оси."""
    rot = Euler((math.radians(90 + pitch), 0, math.radians(yaw)), 'XYZ').to_quaternion()
    right, up, fwd = (rot @ Vector(a) for a in ((1, 0, 0), (0, 1, 0), (0, 0, 1)))
    pts = []
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name.startswith(prefix) and not o.hide_viewport:
            pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    if not pts:
        return
    hs, vs, ds = [p.dot(right) for p in pts], [p.dot(up) for p in pts], [p.dot(fwd) for p in pts]
    cen = (right * (min(hs) + max(hs)) + up * (min(vs) + max(vs)) + fwd * (min(ds) + max(ds))) / 2
    w, h = max(hs) - min(hs), max(vs) - min(vs)
    set_view(center=tuple(cen), dist=max(w, h * 1.45) / 1.25 * pad, yaw=yaw, pitch=pitch)


def wear(names, center=(0, -0.05, 1.35), dist=0.46, yaw=0, pitch=0):
    """Надевает на персонажа перечисленные ассеты, остальные прячет."""
    names = {names} if isinstance(names, str) else set(names)
    for n in ("Finik_Mesh", "Finik_Armature"):
        bpy.data.objects[n].hide_viewport = False
        bpy.data.objects[n].hide_render = False
    for p in PREFIXES:
        for g in acc_groups(p):
            g.location = (0, 0, 0)
            _set_group_visible(g, g.name in names)
    bpy.context.view_layer.update()
    set_view(center=center, dist=dist, yaw=yaw, pitch=pitch)


def save():
    """Сохраняет .blend в репозиторий (текстуры персонажа запакованы внутрь)."""
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    try:
        bpy.ops.file.pack_all()
    except RuntimeError:
        pass
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, compress=True)
    return BLEND_PATH
