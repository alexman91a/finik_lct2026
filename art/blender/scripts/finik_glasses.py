"""Очки для Финика: 5 форм, посажены по промеренным якорям морды."""
import bpy, bmesh, math
from mathutils import Vector

# --- геометрия посадки (мировые координаты меша Финика) ---
EYE_Z = 1.346        # центр зрачка (rest-поза)
EYE_X = 0.082        # разнос зрачков / 2
FRONT_Y = -0.345     # базовая плоскость линз (переносица на -0.323)
WRAP_K = 1.2         # изгиб оправы вокруг морды: y += K * x^2

# профиль головы на уровне глаз: y_world -> полуширина, для трассировки дужек
TEMPLE_PROFILE = [(-0.300, 0.149), (-0.270, 0.190), (-0.240, 0.218), (-0.210, 0.239),
                  (-0.180, 0.250), (-0.150, 0.257), (-0.120, 0.245), (-0.090, 0.226),
                  (-0.060, 0.202), (-0.030, 0.169)]


def place(contour, cx, cz, base_y=FRONT_Y, k=WRAP_K):
    """Контур из плоскости XZ -> мировые координаты с изгибом вокруг морды."""
    out = []
    for p in contour:
        x = cx + p.x
        out.append(Vector((x, base_y + k * x * x, cz + p.z)))
    return out


_BVH = None     # BVH тела, выставляется в build_all


def temple_path(sign, start, clearance=0.012, drop=-0.055):
    """Дужка: от шарнира назад и чуть наружу; кончик уходит в шерсть щеки
    (касается поверхности), поэтому дужка не торчит за головой."""
    import finik_lib as lib
    d = Vector((sign * 0.34, 1.0, -0.04)).normalized()
    pts, p = [Vector(start)], Vector(start)
    for _ in range(70):
        q = p + d * 0.004
        if lib.is_inside(_BVH, q):
            pts.append(_BVH.find_nearest(q)[0])
            break
        p = q
        if (p - pts[-1]).length > 0.010:
            pts.append(p.copy())
    if len(pts) < 2:
        pts.append(Vector(start) + d * 0.02)
    return pts


def build(spec, col):
    """spec: dict со всеми параметрами модели очков."""
    name = spec["name"]
    lib = spec["lib"]
    contour = spec["contour"]
    fr = spec["frame_r"]

    frame_bm = bmesh.new()
    lens_bm = bmesh.new()
    detail_bm = bmesh.new() if spec.get("detail_mat") else None

    rims = []
    for sign in (1, -1):
        c = [Vector((p.x * sign, p.y, p.z)) for p in contour]
        w = place(c, sign * EYE_X, EYE_Z)
        rims.append(w)
        if spec.get("rim", True):
            lib.add_tube(frame_bm, w, fr, sections=spec.get("rim_sections", 8),
                         up=(0, -1, 0), closed=True)
        # линза: чуть внутрь оправы
        cen = sum(w, Vector()) / len(w)
        shrunk = [cen + (p - cen) * spec.get("lens_fit", 0.97) for p in w]
        lib.add_plate(lens_bm, [p - cen for p in shrunk], normal=(0, -1, 0),
                      thickness=spec.get("lens_th", 0.005),
                      bulge=spec.get("bulge", 0.006), origin=cen)

    # переносица
    lz = spec.get("bridge_z", 0.012)
    bx = max(abs(p.x) for p in contour) * 0.0 + EYE_X - spec["inner_x"]
    for dz in spec.get("bridge_rows", [0.012]):
        path = [Vector((-bx, FRONT_Y + WRAP_K * bx * bx, EYE_Z + dz)),
                Vector((-bx * 0.45, FRONT_Y + 0.004, EYE_Z + dz + spec.get("bridge_arc", 0.008))),
                Vector((0, FRONT_Y + 0.006, EYE_Z + dz + spec.get("bridge_arc", 0.008) * 1.15)),
                Vector((bx * 0.45, FRONT_Y + 0.004, EYE_Z + dz + spec.get("bridge_arc", 0.008))),
                Vector((bx, FRONT_Y + WRAP_K * bx * bx, EYE_Z + dz))]
        lib.add_tube(frame_bm, path, spec.get("bridge_r", fr * 0.85), sections=8, up=(0, 0, 1))

    # верхняя планка (авиатор)
    if spec.get("brow_bar"):
        z = spec["brow_bar"]
        xs = [i / 16 for i in range(-16, 17)]
        path = [Vector((x * 0.145, FRONT_Y + WRAP_K * (x * 0.145) ** 2, EYE_Z + z)) for x in xs]
        lib.add_tube(frame_bm, path, fr * 0.8, sections=8, up=(0, 0, 1))

    # дужки
    for sign, rim in zip((1, -1), rims):
        hinge = max(rim, key=lambda p: p.x * sign)
        start = Vector((hinge.x, hinge.y + 0.004, EYE_Z + spec.get("hinge_z", 0.010)))
        path = temple_path(sign, start, clearance=spec.get("clearance", 0.012))
        if spec.get("temple_flat"):
            lib.add_ribbon(frame_bm, path, spec["temple_flat"], fr * 1.1, side=(0, 0, 1))
        else:
            lib.add_tube(frame_bm, path, fr * 0.8, sections=8, up=(0, 0, 1))

    objs = [lib.part(name + "_Frame", frame_bm, spec["frame_mat"], col),
            lib.part(name + "_Lens", lens_bm, spec["lens_mat"], col)]
    if detail_bm is not None:
        spec["detail"](lib, detail_bm, place)
        objs.append(lib.part(name + "_Detail", detail_bm, spec["detail_mat"], col))
    return lib.group(name, objs, col)


def build_visor(spec, col):
    """Спортивный визор: одна цельная изогнутая линза."""
    lib = spec["lib"]
    name = spec["name"]
    frame_bm, lens_bm = bmesh.new(), bmesh.new()

    half_w, top, bot = 0.152, 0.052, -0.050
    contour = []
    n = 30
    for k in range(n):                      # верхняя кромка слева направо, нижняя обратно
        t = k / (n - 1)
        contour.append(Vector((-half_w + 2 * half_w * t, 0,
                               top - 0.012 * math.sin(math.pi * t))))
    for k in range(n):
        t = k / (n - 1)
        x = half_w - 2 * half_w * t
        contour.append(Vector((x, 0, bot + 0.020 * (abs(x) / half_w) ** 1.6)))
    world = [Vector((p.x, FRONT_Y - 0.020 + 1.55 * p.x * p.x, EYE_Z + p.z)) for p in contour]
    cen = sum(world, Vector()) / len(world)
    lib.add_plate(lens_bm, [p - cen for p in world], normal=(0, -1, 0),
                  thickness=0.006, bulge=0.010, origin=cen)
    lib.add_tube(frame_bm, world, 0.006, sections=8, up=(0, -1, 0), closed=True)

    for sign in (1, -1):
        start = Vector((sign * half_w, FRONT_Y - 0.020 + 1.55 * half_w * half_w, EYE_Z + 0.030))
        path = temple_path(sign, start, clearance=0.014)
        lib.add_ribbon(frame_bm, path, 0.026, 0.009, side=(0, 0, 1))
    objs = [lib.part(name + "_Frame", frame_bm, spec["frame_mat"], col),
            lib.part(name + "_Lens", lens_bm, spec["lens_mat"], col)]
    return lib.group(name, objs, col)


def build_all(lib):
    global _BVH
    _BVH = lib.body_bvh()
    col = lib.collection("Finik_Accessories")
    gcol = lib.collection("Glasses", col)
    lib.purge("Glasses_")

    m_gold = lib.mat("Acc_Gold", "yellow", rough=0.22, metal=1.0)
    m_silver = lib.mat("Acc_Silver", "silver", rough=0.25, metal=1.0)
    m_dark = lib.mat("Acc_FrameDark", "dark", rough=0.34)
    m_accent = lib.mat("Acc_FrameAccent", "accent", rough=0.30)
    m_pink = lib.mat("Acc_FramePink", "pink", rough=0.30)
    m_clear = lib.mat("Acc_LensClear", "#E8F4FF", rough=0.05, alpha=0.24)
    m_tint = lib.mat("Acc_LensTint", "#1B2230", rough=0.06, alpha=0.82)
    m_rose = lib.mat("Acc_LensRose", "#FFB6D0", rough=0.06, alpha=0.52)
    m_sun = lib.mat("Acc_LensSun", "primary", rough=0.08, alpha=0.62)

    specs = [
        dict(name="Glasses_Round", lib=lib, contour=lib.c_round(0.053, 0.053),
             frame_r=0.0048, frame_mat=m_gold, lens_mat=m_clear, inner_x=0.053,
             bridge_rows=[0.004], bridge_arc=0.010, hinge_z=0.012, bulge=0.007),
        dict(name="Glasses_Nerd", lib=lib, contour=lib.c_rect(0.118, 0.092, corner=0.45),
             frame_r=0.0095, frame_mat=m_dark, lens_mat=m_clear, inner_x=0.059,
             bridge_rows=[0.018], bridge_arc=0.006, hinge_z=0.026, bulge=0.006,
             temple_flat=0.017, clearance=0.014, lens_fit=0.94),
        dict(name="Glasses_Aviator", lib=lib, contour=lib.c_aviator(0.116, 0.098),
             frame_r=0.0042, frame_mat=m_silver, lens_mat=m_tint, inner_x=0.058,
             bridge_rows=[0.026, 0.006], bridge_arc=0.004, brow_bar=0.048,
             hinge_z=0.030, bulge=0.008),
        dict(name="Glasses_Star", lib=lib, contour=lib.c_star(0.062, 0.030),
             frame_r=0.0055, frame_mat=m_pink, lens_mat=m_rose, inner_x=0.040,
             bridge_rows=[0.002], bridge_arc=0.009, hinge_z=0.004, bulge=0.005,
             lens_fit=0.92, rim_sections=7),
        dict(name="Glasses_Retro", lib=lib, contour=lib.c_rect(0.112, 0.078, corner=0.62),
             frame_r=0.0072, frame_mat=m_accent, lens_mat=m_clear, inner_x=0.056,
             bridge_rows=[0.014], bridge_arc=0.007, hinge_z=0.020, bulge=0.006,
             temple_flat=0.013, lens_fit=0.95),
    ]
    made = [build(s, gcol) for s in specs]
    made.append(build_visor(dict(name="Glasses_Visor", lib=lib,
                                 frame_mat=m_dark, lens_mat=m_sun), gcol))
    return made
