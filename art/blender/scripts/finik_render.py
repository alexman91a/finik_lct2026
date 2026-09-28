"""Рендер витрин аксессуаров через ортокамеру — детерминированные картинки для репозитория."""
import bpy, math, os
from mathutils import Vector, Euler

import finik_lib as lib
import finik_scene as scene
import finik_build as build

RES = (1600, 1000)


def _engine_eevee():
    sc = bpy.context.scene
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            continue
    ee = getattr(sc, "eevee", None)
    if ee is not None and hasattr(ee, "taa_render_samples"):
        ee.taa_render_samples = 48
    sc.render.resolution_x, sc.render.resolution_y = RES
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    try:
        sc.view_settings.view_transform = 'AgX'
    except TypeError:
        pass


def _backdrop(color="#FFF8F1", light=0.32):
    """Камера видит светлый брендовый фон, а освещение от мира остаётся приглушённым."""
    w = bpy.context.scene.world
    nt = w.node_tree
    for n in [n for n in nt.nodes if n.name.startswith("Acc_BG")]:
        nt.nodes.remove(n)
    out = next(n for n in nt.nodes if n.type == 'OUTPUT_WORLD')
    dim = nt.nodes.new("ShaderNodeBackground"); dim.name = "Acc_BG_dim"
    cam = nt.nodes.new("ShaderNodeBackground"); cam.name = "Acc_BG_cam"
    lp = nt.nodes.new("ShaderNodeLightPath"); lp.name = "Acc_BG_lp"
    mix = nt.nodes.new("ShaderNodeMixShader"); mix.name = "Acc_BG_mix"
    dim.inputs[0].default_value = lib.hex_rgba(color)
    dim.inputs[1].default_value = light
    cam.inputs[0].default_value = lib.hex_rgba(color)
    cam.inputs[1].default_value = 1.0
    nt.links.new(lp.outputs["Is Camera Ray"], mix.inputs[0])
    nt.links.new(dim.outputs[0], mix.inputs[1])
    nt.links.new(cam.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])


def _camera():
    cam = bpy.data.objects.get("Acc_Cam")
    if cam is None:
        cam = bpy.data.objects.new("Acc_Cam", bpy.data.cameras.new("Acc_Cam"))
        bpy.context.scene.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.clip_start, cam.data.clip_end = 0.01, 50
    bpy.context.scene.camera = cam
    return cam


def _visible_mesh_points(names_prefixes):
    pts = []
    for o in bpy.data.objects:
        if o.type not in {'MESH', 'FONT'} or o.hide_render:
            continue
        if not any(o.name.startswith(p) for p in names_prefixes):
            continue
        pts += [o.matrix_world @ Vector(c) for c in o.bound_box]
    return pts


def frame(prefixes, yaw, pitch, pad=1.10):
    """Ставит ортокамеру так, чтобы все видимые объекты с префиксами влезли в кадр."""
    cam = _camera()
    rot = Euler((math.radians(90 + pitch), 0, math.radians(yaw)), 'XYZ')
    q = rot.to_quaternion()
    right, up, fwd = (q @ Vector(a) for a in ((1, 0, 0), (0, 1, 0), (0, 0, -1)))
    pts = _visible_mesh_points(prefixes)
    hs, vs, ds = [p.dot(right) for p in pts], [p.dot(up) for p in pts], [p.dot(fwd) for p in pts]
    cen = right * (min(hs) + max(hs)) / 2 + up * (min(vs) + max(vs)) / 2 + fwd * min(ds)
    w, h = max(hs) - min(hs), max(vs) - min(vs)
    aspect = RES[0] / RES[1]
    cam.data.ortho_scale = max(w, h * aspect) * pad
    cam.rotation_euler = rot
    cam.location = cen - fwd * 3.0
    return cam


def _captions(groups, yaw, pitch, size=0.045):
    """Подписи под предметами (названия из каталога), повёрнуты к камере."""
    lib.purge("Caption_")
    col = lib.collection("Acc_Captions")
    m = lib.mat("Acc_CaptionText", "#1E1D23", rough=0.9)
    rot = Euler((math.radians(90 + pitch), 0, math.radians(yaw)), 'XYZ')
    q = rot.to_quaternion()
    up = q @ Vector((0, 1, 0))
    for g in groups:
        parts = [c for c in g.children if c.type == 'MESH']
        pts = [c.matrix_world @ Vector(v) for c in parts for v in c.bound_box]
        cen = sum(pts, Vector()) / len(pts)
        low = min(p.dot(up) for p in pts)
        title = build.CATALOG[g.name][1]
        cu = bpy.data.curves.new("Caption_" + g.name, 'FONT')
        cu.body = title
        cu.align_x, cu.align_y = 'CENTER', 'TOP'
        cu.size = size
        t = bpy.data.objects.new("Caption_" + g.name, cu)
        t.data.materials.append(m)
        t.rotation_euler = rot
        t.location = cen + up * (low - cen.dot(up) - 0.03)
        col.objects.link(t)


def _clear_captions():
    lib.purge("Caption_")
    for cu in [c for c in bpy.data.curves if c.name.startswith("Caption_") and c.users == 0]:
        bpy.data.curves.remove(cu)


def render_to(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def render_sheet(prefix, path, cols, yaw=20, pitch=-12, gap=0.16):
    """Лист витрины одной категории: сетка + подписи."""
    _engine_eevee()
    _backdrop()
    scene.showcase(prefix, cols=cols, gap=gap, hide_char=True, yaw=yaw, pitch=pitch)
    gs = sorted(scene.acc_groups(prefix), key=lambda o: o.name)
    _captions(gs, yaw, pitch)
    bpy.context.view_layer.update()             # новые подписи получают matrix_world
    frame((prefix, "Caption_"), yaw, pitch)
    out = render_to(path)
    _clear_captions()
    return out


def render_outfit(names, path, yaw, pitch=-6, full=True):
    """Финик в наборе аксессуаров."""
    _engine_eevee()
    _backdrop()
    scene.wear(names)
    bpy.context.view_layer.update()
    if full:
        frame(("Finik_Mesh",) + tuple(names), yaw, pitch, pad=1.06)
    return render_to(path)


def render_closeup(names, path, center=(0, -0.15, 1.46), scale=0.62, yaw=30, pitch=-8):
    """Крупный план головы/детали с надетыми ассетами."""
    _engine_eevee()
    _backdrop()
    scene.wear(names)
    cam = _camera()
    rot = Euler((math.radians(90 + pitch), 0, math.radians(yaw)), 'XYZ')
    fwd = rot.to_quaternion() @ Vector((0, 0, -1))
    cam.data.ortho_scale = scale
    cam.rotation_euler = rot
    cam.location = Vector(center) - fwd * 3.0
    return render_to(path)
