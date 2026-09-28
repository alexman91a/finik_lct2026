"""Прочая экипировка Финика: кепка и шапка (finik_cap), наушники, шарф, значок, смарт-часы.

Всё, что прилегает к телу, строится по замерам (лучи изнутри, сечения) и подгоняется
как осевая кривая (finik_curves), а не деформацией готовой сетки.
"""
import bpy, bmesh, math
from mathutils import Vector

import finik_curves as cv


def _emissive(m, color_hex, strength, lib):
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if "Emission Color" in bsdf.inputs:
        bsdf.inputs["Emission Color"].default_value = lib.hex_rgba(color_hex)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


def build_headphones(lib, col):
    """Накладные наушники: дуга по затылку за ушами, чашки по бокам головы."""
    bvh = lib.body_bvh()
    band_bm, cup_bm, pad_bm, ring_bm = (bmesh.new() for _ in range(4))

    cups = []
    for sign in (1, -1):
        side = Vector((sign, 0, 0))
        # чашка ложится на самую выступающую точку головы в пределах своего диска
        y0, z0, rad = -0.052, 1.340, 0.050
        best = 0.18
        for k in range(9):
            for rr in (0.0, rad * 0.5, rad):
                a = k * math.tau / 9
                o = Vector((sign * 0.5, y0 + rr * math.cos(a), z0 + rr * math.sin(a)))
                hit = bvh.ray_cast(o, -side, 0.5)[0]
                if hit is not None:
                    best = max(best, abs(hit.x))
        surf = Vector((sign * best, y0, z0))
        cups.append((sign, surf))
        rot = (0, math.radians(90), 0)
        lib.add_torus(pad_bm, 0.036, 0.013, loc=surf + side * 0.013, major_segs=28,
                      minor_segs=10, rot=rot)
        lib.add_sphere(cup_bm, 0.054, loc=surf + side * 0.031, scale=(0.42, 1.0, 1.08),
                       segs=26, rings=14)
        lib.add_torus(ring_bm, 0.041, 0.0055, loc=surf + side * 0.052, major_segs=28,
                      minor_segs=8, rot=rot)
        lib.add_sphere(ring_bm, 0.013, loc=surf + side * 0.054, scale=(0.35, 1, 1),
                       segs=14, rings=8)

    # дуга: от верха чашки назад-вверх, по затылку за ушами (уши заканчиваются на y=-0.046)
    (s1, a1), (s2, a2) = cups
    ctrl = [a1 + Vector((0.030, 0.0, 0.058)),
            Vector((0.222, -0.030, 1.440)), Vector((0.170, -0.004, 1.488)),
            Vector((0.090, 0.012, 1.508)), Vector((0.0, 0.016, 1.512)),
            Vector((-0.090, 0.012, 1.508)), Vector((-0.170, -0.004, 1.488)),
            Vector((-0.222, -0.030, 1.440)), a2 + Vector((-0.030, 0.0, 0.058))]
    path = cv.catmull(ctrl, per_seg=10)
    path, _ = cv.fit_curve(path, bvh, clear=0.016, iters=60)
    nrms = cv.radial_normals(path, (0, -0.06, 1.36))
    path = cv.lift_band(path, nrms, 0.026, bvh, clear=0.008)
    cv.add_band(band_bm, path, nrms, 0.026, 0.011)
    inner = [p - n * 0.008 for p, n in zip(path, nrms)][12:-12]
    cv.add_band(pad_bm, inner, nrms[12:-12], 0.022, 0.008)
    for (sign, surf), end in ((cups[0], path[0]), (cups[1], path[-1])):
        lib.add_tube(band_bm, [end, surf + Vector((sign * 0.031, 0, 0.052))], 0.007,
                     sections=10, up=(1, 0, 0))

    m_band = lib.mat("Acc_HpBand", "dark", rough=0.40)
    m_cup = lib.mat("Acc_HpCup", "accent", rough=0.36)
    m_pad = lib.mat("Acc_HpPad", "#2B2C36", rough=0.78)
    m_ring = lib.mat("Acc_HpRing", "yellow", rough=0.30, metal=0.6)
    return lib.group("Gear_Headphones", [
        lib.part("Gear_Headphones_Band", band_bm, m_band, col),
        lib.part("Gear_Headphones_Cups", cup_bm, m_cup, col),
        lib.part("Gear_Headphones_Pads", pad_bm, m_pad, col),
        lib.part("Gear_Headphones_Ring", ring_bm, m_ring, col)], col)


def build_scarf(lib, col):
    """Вязаный шарф: петля по сечению шеи, узел, два конца в полоску с бахромой."""
    bvh = lib.body_bvh()
    body_bm, stripe_bm, fringe_bm = bmesh.new(), bmesh.new(), bmesh.new()
    r_tube = 0.030
    center = Vector((0, -0.125, 1.178))
    prof = cv.radial_profile(bvh, center, (0, 0, 1), (0, -1, 0), count=64,
                             max_dist=0.35, outer=True)
    ring = []
    for k, (d, dist) in enumerate(prof):
        a = k * math.tau / len(prof)
        dist = dist if dist is not None else 0.16
        p = center + d * (dist + r_tube + 0.006)
        p.z -= 0.022 * math.cos(a)                      # спереди ниже, сзади выше
        ring.append(p)
    ring = cv.catmull(ring[::2], per_seg=4, closed=True)
    ring, _ = cv.fit_curve(ring, bvh, clear=r_tube + 0.004, iters=40, closed=True)
    lib.add_tube(body_bm, ring, r_tube, sections=14, up=(0, 0, 1), closed=True)
    # второй виток выше и тоньше — читается как намотанный шарф
    ring2 = [p + Vector((0, 0, 0.034)) for p in ring]
    ring2, _ = cv.fit_curve(ring2, bvh, clear=0.026, iters=30, closed=True)
    lib.add_tube(body_bm, ring2, 0.025, sections=14, up=(0, 0, 1), closed=True)

    # узел спереди, чуть сбоку
    knot_i = min(range(len(ring)), key=lambda i: (ring[i] - Vector((0.070, -0.30, 1.16))).length)
    knot = ring[knot_i] + Vector((0, -0.012, -0.004))
    lib.add_sphere(body_bm, 0.034, loc=knot, scale=(1.15, 0.85, 1.0), segs=20, rings=12)

    m_body = lib.mat("Acc_Scarf", "pink", rough=0.86)
    m_stripe = lib.mat("Acc_ScarfStripe", "cream", rough=0.86)

    # концы лежат на груди плашмя; полоски — чередующиеся сегменты ленты, а не наклейки
    for dx, z_end, w in ((0.012, 0.955, 0.074), (0.058, 0.915, 0.068)):
        ctrl = [knot + Vector((dx * 0.3, -0.004, -0.02)), knot + Vector((dx, -0.006, -0.09)),
                Vector((knot.x + dx * 1.2, -0.29, (knot.z + z_end) / 2 - 0.02)),
                Vector((knot.x + dx * 1.35, -0.295, z_end))]
        path = cv.catmull(ctrl, per_seg=14)
        path, nrms = cv.fit_curve(path, bvh, clear=0.013, iters=50, fix_ends=False)
        seg_len, acc, cur, stripe = 0.042, 0.0, [0], False
        for i in range(1, len(path)):
            acc += (path[i] - path[i - 1]).length
            cur.append(i)
            if acc >= seg_len or i == len(path) - 1:
                pts, ns = [path[j] for j in cur], [nrms[j] for j in cur]
                if len(pts) >= 2:
                    cv.add_band(stripe_bm if stripe else body_bm, pts, ns, w, 0.018)
                stripe, acc, cur = not stripe, 0.0, [i]
        tip, tn = path[-1], nrms[-1]
        t = (path[-1] - path[-2]).normalized()
        side = t.cross(tn).normalized()
        for f in range(7):
            o = tip + side * (-w / 2 + w * (f + 0.5) / 7) + tn * 0.002
            lib.add_tube(fringe_bm, [o, o + t * 0.028 + tn * 0.002], 0.0035, sections=6,
                         up=(0, 0, 1))

    return lib.group("Gear_Scarf", [
        lib.part("Gear_Scarf_Body", body_bm, m_body, col),
        lib.part("Gear_Scarf_Stripe", stripe_bm, m_stripe, col),
        lib.part("Gear_Scarf_Fringe", fringe_bm, m_stripe, col)], col)


def build_badge(lib, col):
    """Значок-звезда на груди (слот star в приложении)."""
    star, pin = bmesh.new(), bmesh.new()
    contour = lib.c_star(0.040, 0.019)
    cen = Vector((0.086, -0.272, 1.060))
    lib.add_plate(star, contour, normal=(0, -1, 0), thickness=0.010, bulge=0.006, origin=cen)
    lib.add_torus(pin, 0.046, 0.005, loc=cen + Vector((0, 0.004, 0)),
                  scale=(1, 1, 1), major_segs=26, minor_segs=6,
                  rot=(math.radians(90), 0, 0))
    m_star = lib.mat("Acc_BadgeStar", "yellow", rough=0.26, metal=0.85)
    m_pin = lib.mat("Acc_BadgeRing", "#C24C7A", rough=0.42)
    return lib.group("Gear_Badge", [
        lib.part("Gear_Badge_Star", star, m_star, col),
        lib.part("Gear_Badge_Ring", pin, m_pin, col)], col)


def build_watch(lib, col):
    """Смарт-часы: ремешок по сечению запястья, корпус со стеклом и кольцом прогресса."""
    bvh = lib.body_bvh()
    strap_bm, case_bm, glass_bm, ui_bm = (bmesh.new() for _ in range(4))
    center = Vector((0.500, -0.143, 1.057))
    prof = cv.radial_profile(bvh, center, (1, 0, 0), (0, 0, 1), count=40, max_dist=0.15)
    ring = [center + d * ((dist if dist is not None else 0.05) + 0.005) for d, dist in prof]
    ring = cv.catmull(ring[::2], per_seg=4, closed=True)
    ring, nrms = cv.fit_curve(ring, bvh, clear=0.005, iters=30, closed=True)
    cv.add_band(strap_bm, ring, nrms, 0.024, 0.006, closed=True)

    top_i = max(range(len(ring)), key=lambda i: ring[i].z)
    top, up = ring[top_i], nrms[top_i]
    lib.add_box(case_bm, (0.034, 0.040, 0.013), loc=top + up * 0.008, round_k=0.45, rows=10)
    lib.add_box(glass_bm, (0.028, 0.033, 0.004), loc=top + up * 0.0155, round_k=0.40, rows=6)
    lib.add_torus(ui_bm, 0.0095, 0.0013, loc=top + up * 0.0182, major_segs=24,
                  minor_segs=6, arc=0.72, start=math.radians(90))
    lib.add_sphere(ui_bm, 0.0022, loc=top + up * 0.0182, segs=10, rings=6)
    lib.add_box(case_bm, (0.006, 0.007, 0.006), loc=top + up * 0.008 + Vector((0.019, 0, 0)),
                round_k=0.7, rows=6)

    m_strap = lib.mat("Acc_WatchStrap", "mint", rough=0.60)
    m_case = lib.mat("Acc_WatchCase", "silver", rough=0.24, metal=0.9)
    m_glass = lib.mat("Acc_WatchScreen", "#101828", rough=0.08)
    m_ui = _emissive(lib.mat("Acc_WatchUI", "mint", rough=0.3), "#5CF2B0", 3.0, lib)
    return lib.group("Gear_Watch", [
        lib.part("Gear_Watch_Strap", strap_bm, m_strap, col),
        lib.part("Gear_Watch_Case", case_bm, m_case, col),
        lib.part("Gear_Watch_Screen", glass_bm, m_glass, col),
        lib.part("Gear_Watch_UI", ui_bm, m_ui, col)], col)


def build_all(lib):
    import importlib, finik_cap
    importlib.reload(cv)
    importlib.reload(finik_cap)
    col = lib.collection("Finik_Accessories")
    gcol = lib.collection("Gear", col)
    lib.purge("Gear_")
    return [finik_cap.build_cap(lib, gcol), finik_cap.build_beanie(lib, gcol),
            build_headphones(lib, gcol), build_scarf(lib, gcol), build_badge(lib, gcol),
            build_watch(lib, gcol)]
