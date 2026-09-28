"""Процедурная библиотека аксессуаров для Финика. Чистый bpy/bmesh, без operators."""
import bpy, bmesh, math
from mathutils import Vector, Matrix, Euler

TAU = math.tau

# ---------------------------------------------------------------- материалы
PALETTE = {
    "primary":  "#FF7A32", "primaryDark": "#D85B1E", "accent": "#6C63FF",
    "mint":     "#28B77B", "sky": "#2AAEF2", "pink": "#F45F8D",
    "yellow":   "#FFC94A", "dark": "#24252D", "cream": "#FFF8F1",
    "white":    "#FFFFFF", "denim": "#3E5C85", "leather": "#8A5A3B",
    "graphite": "#43454F", "silver": "#C8CCD4",
}


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgba(h, a=1.0):
    h = PALETTE.get(h, h).lstrip("#")
    r, g, b = (int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), a)


def mat(name, color, rough=0.45, metal=0.0, alpha=1.0):
    """Материал по имени, идемпотентно."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    inp = bsdf.inputs
    inp["Base Color"].default_value = hex_rgba(color)
    inp["Roughness"].default_value = rough
    inp["Metallic"].default_value = metal
    if "Alpha" in inp:
        inp["Alpha"].default_value = alpha
    if alpha < 1.0:
        if hasattr(m, "surface_render_method"):
            try:
                m.surface_render_method = "BLENDED"
            except TypeError:
                pass
        if hasattr(m, "blend_method"):
            try:
                m.blend_method = "BLEND"
            except TypeError:
                pass
    m.diffuse_color = hex_rgba(color, alpha)
    return m


# ---------------------------------------------------------------- сцена
def collection(name, parent=None):
    col = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    target = parent or bpy.context.scene.collection
    if col.name not in target.children:
        try:
            target.children.link(col)
        except RuntimeError:
            pass
    return col


def purge(prefix):
    """Удаляет объекты (и осиротевшие меши) по префиксу имени — для чистых пересборок."""
    for o in [o for o in bpy.data.objects if o.name.startswith(prefix)]:
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if isinstance(data, bpy.types.Mesh) and data.users == 0:
            bpy.data.meshes.remove(data)


def new_object(name, bm, material, col, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    if material is not None:
        me.materials.append(material)
    obj = bpy.data.objects.new(name, me)
    col.objects.link(obj)
    return obj


def part(name, bm, material, col):
    """Готовая деталь как отдельный объект (свой материал)."""
    return new_object(name, bm, material, col)


def group(name, objs, col):
    """Пустышка-родитель: детали остаются раздельными (у каждой свой материал)."""
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "PLAIN_AXES"
    empty.empty_display_size = 0.05
    col.objects.link(empty)
    for o in objs:
        o.parent = empty
        o.matrix_parent_inverse = empty.matrix_world.inverted()
    return empty


# ---------------------------------------------------------------- геометрия
def add_tube(bm, path, radius, sections=10, up=(0, -1, 0), closed=False, caps=True, radii=None):
    """Труба вдоль ломаной. up — нормаль плоскости пути (для плоских путей нет скрутки)."""
    pts = [Vector(p) for p in path]
    n = len(pts)
    up = Vector(up).normalized()
    rings = []
    for i, p in enumerate(pts):
        if closed:
            t = pts[(i + 1) % n] - pts[(i - 1) % n]
        elif i == 0:
            t = pts[1] - pts[0]
        elif i == n - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        t.normalize()
        u = up - t * up.dot(t)
        if u.length < 1e-6:
            u = Vector((0, 0, 1)) - t * t.z
        u.normalize()
        v = t.cross(u).normalized()
        r = radius if radii is None else radii[i]
        rings.append([bm.verts.new(p + (math.cos(a) * u + math.sin(a) * v) * r)
                      for a in (k * TAU / sections for k in range(sections))])
    bm.verts.ensure_lookup_table()
    span = n if closed else n - 1
    for i in range(span):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(sections):
            k2 = (k + 1) % sections
            try:
                bm.faces.new((a[k], a[k2], b[k2], b[k]))
            except ValueError:
                pass
    if caps and not closed:
        try:
            bm.faces.new(list(reversed(rings[0])))
            bm.faces.new(rings[-1])
        except ValueError:
            pass
    return bm


def add_plate(bm, contour, normal=(0, -1, 0), thickness=0.006, bulge=0.0, origin=(0, 0, 0)):
    """Пластина по контуру (линза): фронт + бэк + бортик, с выпуклостью bulge."""
    nrm = Vector(normal).normalized()
    o = Vector(origin)
    pts = [Vector(p) for p in contour]
    cen = sum(pts, Vector()) / len(pts)
    rmax = max((p - cen).length for p in pts) or 1.0
    front, back = [], []
    for p in pts:
        k = 1.0 - ((p - cen).length / rmax) ** 2
        front.append(bm.verts.new(o + p - nrm * (thickness * 0.5 + bulge * k)))
        back.append(bm.verts.new(o + p + nrm * thickness * 0.5))
    cf = bm.verts.new(o + cen - nrm * (thickness * 0.5 + bulge))
    cb = bm.verts.new(o + cen + nrm * thickness * 0.5)
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        try:
            bm.faces.new((cf, front[i], front[j]))
            bm.faces.new((cb, back[j], back[i]))
            bm.faces.new((front[j], front[i], back[i], back[j]))
        except ValueError:
            pass
    return bm


def add_box(bm, size, loc=(0, 0, 0), round_k=0.28, taper=1.0, rows=10, cols=24,
            rot=None, squash_top=1.0):
    """Скруглённый бокс через суперэллипс. round_k: 0=куб, 1=эллипсоид."""
    sx, sy, sz = (s / 2 for s in size)
    rings = []
    for iz in range(rows):
        fz = iz / (rows - 1)
        z = -sz + 2 * sz * fz
        # мягкое скругление верх/низ
        edge = 0.16
        if fz < edge:
            k = math.sin(math.pi / 2 * (fz / edge)) ** 0.55
        elif fz > 1 - edge:
            k = math.sin(math.pi / 2 * ((1 - fz) / edge)) ** 0.55
        else:
            k = 1.0
        t = 1.0 + (taper - 1.0) * fz
        ring = []
        for ic in range(cols):
            a = ic * TAU / cols
            ca, sa = math.cos(a), math.sin(a)
            px = math.copysign(abs(ca) ** round_k, ca) * sx * t * k
            py = math.copysign(abs(sa) ** round_k, sa) * sy * k
            if sa > 0:
                py *= squash_top
            ring.append(Vector((px, py, z)))
        rings.append(ring)
    m = Matrix.Translation(Vector(loc))
    if rot:
        m = m @ Euler(rot, "XYZ").to_matrix().to_4x4()
    vs = [[bm.verts.new(m @ p) for p in ring] for ring in rings]
    bm.verts.ensure_lookup_table()
    for iz in range(rows - 1):
        a, b = vs[iz], vs[iz + 1]
        for ic in range(cols):
            j = (ic + 1) % cols
            try:
                bm.faces.new((a[ic], a[j], b[j], b[ic]))
            except ValueError:
                pass
    for ring, flip in ((vs[0], True), (vs[-1], False)):
        try:
            bm.faces.new(list(reversed(ring)) if flip else list(ring))
        except ValueError:
            pass
    return bm


def add_sphere(bm, radius, loc=(0, 0, 0), scale=(1, 1, 1), segs=18, rings=12):
    vs = []
    for ir in range(rings + 1):
        phi = math.pi * ir / rings
        row = []
        for ic in range(segs):
            th = TAU * ic / segs
            p = Vector((math.sin(phi) * math.cos(th), math.sin(phi) * math.sin(th), math.cos(phi)))
            p = Vector((p.x * scale[0], p.y * scale[1], p.z * scale[2])) * radius + Vector(loc)
            row.append(bm.verts.new(p))
        vs.append(row)
    bm.verts.ensure_lookup_table()
    for ir in range(rings):
        a, b = vs[ir], vs[ir + 1]
        for ic in range(segs):
            j = (ic + 1) % segs
            try:
                bm.faces.new((a[ic], a[j], b[j], b[ic]))
            except ValueError:
                pass
    return bm


def add_ribbon(bm, path, width, thickness, up=(0, 0, 1), side=None):
    """Плоская лента (лямка/ремешок) вдоль пути."""
    pts = [Vector(p) for p in path]
    n = len(pts)
    rings = []
    for i, p in enumerate(pts):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        t.normalize()
        s = Vector(side) if side else Vector(up).cross(t)
        s = (s - t * s.dot(t))
        if s.length < 1e-6:
            s = Vector((1, 0, 0))
        s.normalize()
        d = t.cross(s).normalized()
        w, h = width / 2, thickness / 2
        rings.append([bm.verts.new(p + s * w + d * h), bm.verts.new(p - s * w + d * h),
                      bm.verts.new(p - s * w - d * h), bm.verts.new(p + s * w - d * h)])
    bm.verts.ensure_lookup_table()
    for i in range(n - 1):
        a, b = rings[i], rings[i + 1]
        for k in range(4):
            j = (k + 1) % 4
            try:
                bm.faces.new((a[k], a[j], b[j], b[k]))
            except ValueError:
                pass
    try:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    except ValueError:
        pass
    return bm


def add_torus(bm, major, minor, loc=(0, 0, 0), scale=(1, 1, 1), major_segs=28,
              minor_segs=10, arc=1.0, rot=None, start=0.0):
    """Тор или дуга тора (arc<1 — незамкнутая)."""
    from mathutils import Matrix as _M, Euler as _E
    m = _M.Translation(Vector(loc))
    if rot:
        m = m @ _E(rot, "XYZ").to_matrix().to_4x4()
    n = major_segs if arc >= 1.0 else major_segs + 1
    rings = []
    for i in range(n):
        a = start + TAU * arc * (i / major_segs)
        c = Vector((math.cos(a), math.sin(a), 0))
        u = c.copy()
        row = []
        for k in range(minor_segs):
            b = TAU * k / minor_segs
            p = c * major + u * (math.cos(b) * minor) + Vector((0, 0, math.sin(b) * minor))
            p = Vector((p.x * scale[0], p.y * scale[1], p.z * scale[2]))
            row.append(bm.verts.new(m @ p))
        rings.append(row)
    bm.verts.ensure_lookup_table()
    span = n if arc >= 1.0 else n - 1
    for i in range(span):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(minor_segs):
            j = (k + 1) % minor_segs
            try:
                bm.faces.new((a[k], a[j], b[j], b[k]))
            except ValueError:
                pass
    return bm


def add_strip(bm, edge_a, edge_b, thickness=0.012, up=(0, 0, 1), bulge=0.0):
    """Плоская панель между двумя кромками (козырёк, клапан): корректная сетка без веера.

    edge_a и edge_b — списки равной длины, идущие в ОДНОМ направлении.
    """
    n = len(edge_a)
    assert n == len(edge_b) and n >= 2
    nrm = Vector(up).normalized()
    rows_t, rows_b = [], []
    for i in range(n):
        a, b = Vector(edge_a[i]), Vector(edge_b[i])
        k = math.sin(math.pi * (i / (n - 1)))
        off = nrm * (thickness * 0.5 + bulge * k)
        rows_t.append((bm.verts.new(a + off), bm.verts.new(b + off)))
        rows_b.append((bm.verts.new(a - nrm * thickness * 0.5),
                       bm.verts.new(b - nrm * thickness * 0.5)))
    bm.verts.ensure_lookup_table()
    for i in range(n - 1):
        try:
            bm.faces.new((rows_t[i][0], rows_t[i][1], rows_t[i + 1][1], rows_t[i + 1][0]))
            bm.faces.new((rows_b[i + 1][0], rows_b[i + 1][1], rows_b[i][1], rows_b[i][0]))
            bm.faces.new((rows_t[i + 1][0], rows_b[i + 1][0], rows_b[i][0], rows_t[i][0]))
            bm.faces.new((rows_t[i][1], rows_b[i][1], rows_b[i + 1][1], rows_t[i + 1][1]))
        except ValueError:
            pass
    for row_t, row_b, flip in ((rows_t[0], rows_b[0], True), (rows_t[-1], rows_b[-1], False)):
        quad = (row_t[0], row_t[1], row_b[1], row_b[0])
        try:
            bm.faces.new(tuple(reversed(quad)) if flip else quad)
        except ValueError:
            pass
    return bm


# ---------------------------------------------------------------- контуры
def c_round(rx, rz, n=28):
    return [Vector((rx * math.cos(a), 0, rz * math.sin(a))) for a in (k * TAU / n for k in range(n))]


def c_rect(w, h, corner=0.4, n=36):
    """Прямоугольник со скруглёнными углами (суперэллипс: меньше corner = острее)."""
    out = []
    for k in range(n):
        a = k * TAU / n
        ca, sa = math.cos(a), math.sin(a)
        out.append(Vector((math.copysign(abs(ca) ** corner, ca) * w / 2, 0,
                           math.copysign(abs(sa) ** corner, sa) * h / 2)))
    return out


def c_aviator(w, h, n=34):
    """Каплевидная форма: широкий верх, сужение и вытяжка книзу."""
    out = []
    for k in range(n):
        a = k * TAU / n
        ca, sa = math.cos(a), math.sin(a)
        x = math.copysign(abs(ca) ** 0.62, ca) * w / 2
        z = math.copysign(abs(sa) ** 0.62, sa) * h / 2
        if z < 0:
            z *= 1.16
            x *= 1.0 - 0.20 * (abs(z) / (h / 2))
        else:
            z *= 0.88
        out.append(Vector((x, 0, z)))
    return out


def smooth_contour(pts, iterations=3, densify=3):
    dense = []
    for i in range(len(pts)):
        p, q = pts[i], pts[(i + 1) % len(pts)]
        for s in range(densify):
            dense.append(p.lerp(q, s / densify))
    for _ in range(iterations):
        dense = [(dense[i - 1] + dense[i] * 2 + dense[(i + 1) % len(dense)]) / 4
                 for i in range(len(dense))]
    return dense


def c_star(r_out, r_in, points=5, rot=math.pi / 2):
    raw = []
    for k in range(points * 2):
        a = rot + k * math.pi / points
        r = r_out if k % 2 == 0 else r_in
        raw.append(Vector((r * math.cos(a), 0, r * math.sin(a))))
    return smooth_contour(raw, iterations=2, densify=4)


# ---------------------------------------------------------------- подгонка по телу
def body_bvh(char_name="Finik_Mesh"):
    """BVH меша персонажа в мировых координатах (с учётом модификаторов)."""
    from mathutils.bvhtree import BVHTree
    deps = bpy.context.evaluated_depsgraph_get()
    char = bpy.data.objects[char_name]
    char.hide_viewport = False
    bm = bmesh.new()
    bm.from_object(char, deps)
    bm.transform(char.matrix_world)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    return tree


_RAY_DIRS = (Vector((1, 0.013, 0.007)).normalized(), Vector((-0.011, 1, 0.017)).normalized(),
             Vector((0.009, -0.014, 1)).normalized())


def _hits(bvh, origin, direction, limit=4.0):
    """Число пересечений луча с мешем (для теста чётности)."""
    n, o = 0, origin.copy()
    for _ in range(64):
        loc, _, _, dist = bvh.ray_cast(o, direction, limit)
        if loc is None:
            break
        n += 1
        o = loc + direction * 1e-4
    return n


def is_inside(bvh, p):
    """Точка внутри тела: большинство из трёх лучей пересекают меш нечётное число раз.

    Надёжнее знака нормали ближайшей грани — тот врёт у тонких деталей (хохолок, шерсть).
    """
    votes = sum(_hits(bvh, p, d) % 2 for d in _RAY_DIRS)
    return votes >= 2


def measure_penetration(obj, bvh):
    """(доля вершин внутри тела, максимальная глубина в метрах)."""
    mw = obj.matrix_world
    inside, dmax = 0, 0.0
    for v in obj.data.vertices:
        p = mw @ v.co
        if not is_inside(bvh, p):
            continue
        loc, _, _, _ = bvh.find_nearest(p)
        inside += 1
        if loc is not None:
            dmax = max(dmax, (p - loc).length)
    return inside / max(len(obj.data.vertices), 1), dmax


def fit_outside(obj, bvh, clearance=0.004, relax=2):
    """Выталкивает вершины, утонувшие в теле, на поверхность + зазор.

    Держит ассет прижатым к телу без проникновения: то, что снаружи, не трогается,
    утопленное поднимается вдоль нормали поверхности. relax — проходы сглаживания
    смещённых вершин, чтобы лента/ремешок не пошли ступеньками.
    """
    me = obj.data
    mw = obj.matrix_world
    mwi = mw.inverted()
    moved = {}
    for v in me.vertices:
        p = mw @ v.co
        loc, nrm, _, _ = bvh.find_nearest(p)
        if loc is None:
            continue
        if is_inside(bvh, p):
            # наружу = от точки к ближайшей поверхности; не зависит от знака нормали грани
            out = loc - p
            out = out.normalized() if out.length > 1e-7 else nrm
            moved[v.index] = mwi @ (loc + out * clearance)
    for idx, co in moved.items():
        me.vertices[idx].co = co
    if relax and moved:
        neigh = {}
        for e in me.edges:
            a, b = e.vertices
            neigh.setdefault(a, []).append(b)
            neigh.setdefault(b, []).append(a)
        for _ in range(relax):
            snapshot = {i: me.vertices[i].co.copy() for i in moved}
            for idx in moved:
                ns = neigh.get(idx)
                if not ns:
                    continue
                avg = Vector()
                for j in ns:
                    avg += me.vertices[j].co
                avg /= len(ns)
                me.vertices[idx].co = snapshot[idx].lerp(avg, 0.35)
    me.update()
    return len(moved)


