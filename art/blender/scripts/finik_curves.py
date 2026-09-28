"""Кривые, прилегающие к телу: сплайн -> подгонка кривой (не сетки) -> лента/трубка.

Подгонка сетки после построения ломает форму (изломы лямок, бугры на дуге наушников).
Здесь по поверхности подгоняется сама осевая линия: точки выталкиваются наружу на
заданный зазор и сглаживаются, пока не перестанут пересекать тело. Сечение ленты
ориентируется по нормали поверхности, поэтому лямка лежит плашмя на плече и на груди.
"""
import math
from mathutils import Vector

import finik_lib as lib


def catmull(points, per_seg=8, closed=False):
    """Сплайн Катмулла-Рома через контрольные точки."""
    p = [Vector(x) for x in points]
    n = len(p)
    out = []
    segs = n if closed else n - 1
    for i in range(segs):
        p0 = p[(i - 1) % n] if closed else p[max(i - 1, 0)]
        p1, p2 = p[i % n], p[(i + 1) % n]
        p3 = p[(i + 2) % n] if closed else p[min(i + 2, n - 1)]
        for k in range(per_seg):
            t = k / per_seg
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    if not closed:
        out.append(p[-1].copy())
    return out


def fit_curve(points, bvh, clear, iters=40, fix_ends=True, closed=False, smooth=0.5):
    """Выталкивает осевую линию наружу на clear от тела и сглаживает.

    Возвращает (точки, нормали поверхности в этих точках)."""
    pts = [Vector(p) for p in points]
    n = len(pts)

    def push():
        moved = 0
        for i, p in enumerate(pts):
            if fix_ends and not closed and i in (0, n - 1):
                continue
            loc, nrm, _, dist = bvh.find_nearest(p)
            if loc is None:
                continue
            inside = lib.is_inside(bvh, p)
            if inside or dist < clear:
                out = (loc - p) if inside else (p - loc)
                out = out.normalized() if out.length > 1e-7 else nrm
                pts[i] = loc + out * clear
                moved += 1
        return moved

    for _ in range(iters):
        if push() == 0:
            break
        snap = [p.copy() for p in pts]
        for i in range(n):
            if not closed and i in (0, n - 1):
                continue
            a, b = snap[(i - 1) % n], snap[(i + 1) % n]
            pts[i] = snap[i].lerp((a + b) / 2, smooth)
    push()
    # финальная полировка: сглаживание линии, выталкивание только из тела (не до зазора) —
    # убирает мелкий зигзаг на бугристых местах (складки капюшона)
    for _ in range(3):
        snap = [p.copy() for p in pts]
        for i in range(n):
            if not closed and i in (0, n - 1):
                continue
            pts[i] = snap[i].lerp((snap[(i - 1) % n] + snap[(i + 1) % n]) / 2, 0.3)
            if lib.is_inside(bvh, pts[i]):
                pts[i] = snap[i]
    nrms = []
    for p in pts:
        loc, nrm, _, _ = bvh.find_nearest(p)
        d = p - loc if loc is not None else Vector((0, 0, 1))
        nrms.append(d.normalized() if d.length > 1e-7 else nrm)
    return pts, smooth_normals(nrms, closed=closed)


def smooth_normals(nrms, passes=4, closed=False):
    """Согласует знак соседних нормалей и сглаживает их: без этого лента перекручивается
    там, где ближайшая точка поверхности прыгает (складки, шнурки)."""
    n = len(nrms)
    out = [v.copy() for v in nrms]
    for i in range(1, n):
        if out[i].dot(out[i - 1]) < 0:
            out[i] = -out[i]
    for _ in range(passes):
        snap = [v.copy() for v in out]
        for i in range(n):
            if not closed and i in (0, n - 1):
                continue
            v = snap[(i - 1) % n] + snap[i] * 2 + snap[(i + 1) % n]
            out[i] = v.normalized() if v.length > 1e-7 else snap[i]
    return out


def add_band(bm, path, normals, width, thickness, closed=False, round_edge=True):
    """Лента вдоль пути; толщина по нормали поверхности, ширина — поперёк.
    round_edge — скруглённое сечение (8 точек) вместо прямоугольника."""
    n = len(path)
    prof = ([(1, 0.5), (0.7, 1), (-0.7, 1), (-1, 0.5), (-1, -0.5), (-0.7, -1), (0.7, -1), (1, -0.5)]
            if round_edge else [(1, 1), (-1, 1), (-1, -1), (1, -1)])
    rings = []
    for i, p in enumerate(path):
        if closed:
            t = path[(i + 1) % n] - path[(i - 1) % n]
        elif i == 0:
            t = path[1] - path[0]
        elif i == n - 1:
            t = path[-1] - path[-2]
        else:
            t = path[i + 1] - path[i - 1]
        t.normalize()
        up = normals[i] - t * normals[i].dot(t)
        up = up.normalized() if up.length > 1e-6 else Vector((0, 0, 1))
        side = t.cross(up).normalized()
        rings.append([bm.verts.new(p + side * (sx * width / 2) + up * (sy * thickness / 2))
                      for sx, sy in prof])
    bm.verts.ensure_lookup_table()
    m = len(prof)
    span = n if closed else n - 1
    for i in range(span):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(m):
            j = (k + 1) % m
            try:
                bm.faces.new((a[k], a[j], b[j], b[k]))
            except ValueError:
                pass
    if not closed:
        for ring, flip in ((rings[0], True), (rings[-1], False)):
            try:
                bm.faces.new(list(reversed(ring)) if flip else ring)
            except ValueError:
                pass
    return bm


def radial_profile(bvh, center, axis, ref, count=48, max_dist=0.4, outer=False):
    """Сечение тела вокруг оси: для count направлений в плоскости, перпендикулярной axis,
    расстояние от center до поверхности. outer=True — внешний силуэт (луч снаружи внутрь),
    нечувствителен к внутренней геометрии (полость рта и т.п.). Возвращает [(dir, dist)]."""
    axis = Vector(axis).normalized()
    ref = (Vector(ref) - axis * Vector(ref).dot(axis)).normalized()
    other = axis.cross(ref)
    out = []
    for k in range(count):
        a = k * math.tau / count
        d = ref * math.cos(a) + other * math.sin(a)
        if outer:
            o = Vector(center) + d * max_dist
            loc = bvh.ray_cast(o, -d, max_dist)[0]
        else:
            loc = bvh.ray_cast(Vector(center), d, max_dist)[0]
        out.append((d, (loc - Vector(center)).length if loc is not None else None))
    return out


def lift_band(path, normals, width, bvh, clear=0.002, iters=4):
    """Поднимает ленту по нормали там, где её КРАЯ (а не ось) уходят в тело:
    на выпуклостях (плечо) широкая плоская лента иначе проседает краями."""
    pts = [p.copy() for p in path]
    n = len(pts)
    for _ in range(iters):
        need = []
        for i, p in enumerate(pts):
            t = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
            side = t.cross(normals[i])
            side = side.normalized() if side.length > 1e-7 else Vector((1, 0, 0))
            worst = 0.0
            for s in (-1.0, -0.5, 0.5, 1.0):
                q = p + side * (s * width / 2)
                if lib.is_inside(bvh, q):
                    depth = (bvh.find_nearest(q)[0] - q).length
                    if depth < width / 2:              # глубже — это соседняя часть тела
                        worst = max(worst, depth + clear)
            need.append(worst)
        if max(need) < 1e-4:
            break
        sm = [max(need[max(i - 2, 0):i + 3]) for i in range(n)]      # без ступенек
        sm = [(sm[max(i - 1, 0)] + 2 * sm[i] + sm[min(i + 1, n - 1)]) / 4 for i in range(n)]
        pts = [p + normals[i] * min(sm[i], 0.006) for i, p in enumerate(pts)]
    return pts


def radial_normals(path, center):
    """Нормали от общего центра — ровная ориентация сечения там, где кривая далеко от тела."""
    c = Vector(center)
    return [(p - c).normalized() for p in path]
