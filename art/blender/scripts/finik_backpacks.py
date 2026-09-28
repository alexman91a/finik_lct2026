"""Рюкзаки для Финика: 4 модели, посажены по промеренному торсу (rest-поза).

Спина плоская на y=-0.043 (z 0.84-1.00), капюшон худи выступает до y=+0.022 (z 1.04-1.12),
грудь на y=-0.26, плечи z=1.115. Корпус наклонён на -12 град, чтобы низ прилегал к спине,
а верх обходил капюшон.
"""
import bpy, bmesh, math
from mathutils import Vector, Matrix, Euler

BODY_LOC = Vector((0.0, 0.103, 0.985))
BODY_ROT = (-0.21, 0.0, 0.0)
BODY_SIZE = (0.272, 0.196, 0.285)


def local_frame(loc=BODY_LOC, rot=BODY_ROT):
    """Замыкание: локальные координаты корпуса -> мировые."""
    m = Matrix.Translation(Vector(loc)) @ Euler(rot, "XYZ").to_matrix().to_4x4()
    return lambda p: m @ Vector(p)


# профиль поверхности груди (z -> y) при x ~ 0.11, промерено по мешу
CHEST = [(1.115, -0.250), (1.045, -0.253), (0.975, -0.259), (0.910, -0.264), (0.870, -0.280)]


def strap_path(sign, top=(0.076, 0.032, 1.104), bottom=(0.120, -0.022, 0.872), lift=0.014):
    """Лямка: с верха рюкзака через плечо, по груди, под мышку и обратно к низу рюкзака."""
    pts = [Vector((sign * top[0], top[1], top[2])),
           Vector((sign * 0.086, -0.062, 1.132)),          # через плечо сзади
           Vector((sign * 0.098, -0.150, 1.112))]          # плечо спереди
    for z, y in CHEST[1:]:
        x = 0.108 + (1.115 - z) * 0.10
        pts.append(Vector((sign * x, y - lift, z)))
    pts.append(Vector((sign * 0.142, -0.195, 0.872)))      # уходит под мышку назад
    pts.append(Vector((sign * bottom[0], bottom[1], bottom[2])))
    return pts


_STRAPS = []     # подогнанные лямки последнего рюкзака: (sign, path, normals)


def add_straps(lib, bm, width=0.042, thick=0.014, **kw):
    """Лямки: сплайн по контрольным точкам -> подгонка осевой линии по телу -> лента,
    лежащая плашмя (сечение по нормали поверхности). Без изломов у плеча и подмышки."""
    import finik_curves as cv
    bvh = lib.body_bvh()
    _STRAPS.clear()
    for sign in (1, -1):
        path = cv.catmull(strap_path(sign, **kw), per_seg=8)
        path, nrms = cv.fit_curve(path, bvh, clear=thick / 2 + 0.003, iters=60)
        cv.add_band(bm, path, nrms, width, thick)
        _STRAPS.append((sign, path, nrms))
    return bm


def add_buckles(lib, bm, zs=(0.985,), size=(0.052, 0.020, 0.026)):
    """Пряжки садятся на саму лямку (на груди), а не в расчётную точку."""
    for sign, path, nrms in _STRAPS:
        front = [k for k in range(len(path)) if path[k].y < -0.18]
        for z in zs:
            i = min(front, key=lambda k: abs(path[k].z - z))
            lib.add_box(bm, size, loc=path[i] + nrms[i] * 0.009, round_k=0.30, rows=6)
    return bm


def add_handle(lib, bm, L, y=0.02, z=0.150, span=0.048, r=0.010):
    """Ручка-петля сверху."""
    path = [L((-span, y + 0.02, z - 0.005)), L((-span * 0.8, y + 0.005, z + 0.030)),
            L((0, y, z + 0.040)), L((span * 0.8, y + 0.005, z + 0.030)),
            L((span, y + 0.02, z - 0.005))]
    return lib.add_tube(bm, path, r, sections=8, up=(0, 1, 0))


# ---------------------------------------------------------------- модели
def build_school(lib, col):
    """Классический школьный: корпус, клапан с ремешками, карман, ручка."""
    L = local_frame()
    body, trim, metal, strap = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()

    lib.add_box(body, BODY_SIZE, loc=BODY_LOC, rot=BODY_ROT, round_k=0.40, rows=12)
    # клапан: накрывает верх и спускается по внешней стороне
    lib.add_box(trim, (0.272, 0.182, 0.070), loc=L((0, 0.006, 0.112)), rot=BODY_ROT,
                round_k=0.46, rows=8)
    lib.add_box(trim, (0.258, 0.050, 0.130), loc=L((0, 0.078, 0.052)), rot=BODY_ROT,
                round_k=0.44, rows=8)
    # передний (внешний) карман
    lib.add_box(trim, (0.176, 0.054, 0.108), loc=L((0, 0.082, -0.058)), rot=BODY_ROT,
                round_k=0.40, rows=8)
    # ремешки клапана + пряжки
    for sx in (-0.072, 0.072):
        lib.add_ribbon(strap, [L((sx, 0.090, 0.088)), L((sx, 0.098, 0.040)),
                               L((sx, 0.096, 0.004))], 0.026, 0.010, side=(1, 0, 0))
        lib.add_box(metal, (0.034, 0.018, 0.022), loc=L((sx, 0.104, 0.012)),
                    rot=BODY_ROT, round_k=0.25, rows=6)
    add_handle(lib, strap, L)
    add_straps(lib, strap)
    add_buckles(lib, metal)

    m_body = lib.mat("Acc_Bag_School", "accent", rough=0.52)
    m_trim = lib.mat("Acc_Bag_SchoolTrim", "#312B8C", rough=0.50)
    m_metal = lib.mat("Acc_Buckle", "yellow", rough=0.28, metal=0.9)
    m_strap = lib.mat("Acc_StrapDark", "#2F3140", rough=0.60)
    objs = [lib.part("Backpack_School_Body", body, m_body, col),
            lib.part("Backpack_School_Trim", trim, m_trim, col),
            lib.part("Backpack_School_Straps", strap, m_strap, col),
            lib.part("Backpack_School_Metal", metal, m_metal, col)]
    return lib.group("Backpack_School", objs, col)


def build_rolltop(lib, col):
    """Городской ролл-топ: скрученный верх и стяжка."""
    L = local_frame(loc=(0, 0.101, 0.972))
    body, trim, metal, strap = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()

    lib.add_box(body, (0.252, 0.168, 0.268), loc=L((0, 0, 0)), rot=BODY_ROT,
                round_k=0.34, rows=12, taper=0.94)
    # скрученный валик сверху
    roll = [L((-0.128, 0.004, 0.142)), L((0, 0.000, 0.150)), L((0.128, 0.004, 0.142))]
    lib.add_tube(trim, roll, 0.034, sections=12, up=(0, 0, 1))
    # стяжка через валик
    for sx in (-0.058, 0.058):
        lib.add_ribbon(strap, [L((sx, 0.086, 0.020)), L((sx, 0.080, 0.110)),
                               L((sx, 0.020, 0.176)), L((sx, -0.040, 0.150))],
                       0.024, 0.009, side=(1, 0, 0))
        lib.add_box(metal, (0.030, 0.016, 0.020), loc=L((sx, 0.092, 0.036)),
                    rot=BODY_ROT, round_k=0.22, rows=6)
    # накладка-карман сеткой строчек
    lib.add_box(trim, (0.150, 0.040, 0.092), loc=L((0, 0.080, -0.070)), rot=BODY_ROT,
                round_k=0.36, rows=8)
    add_handle(lib, strap, L, z=0.176, span=0.040, r=0.009)
    add_straps(lib, strap, top=(0.072, 0.032, 1.096), bottom=(0.116, -0.024, 0.864))

    m_body = lib.mat("Acc_Bag_Roll", "#39414E", rough=0.58)
    m_trim = lib.mat("Acc_Bag_RollTrim", "mint", rough=0.50)
    m_metal = lib.mat("Acc_BuckleSilver", "silver", rough=0.30, metal=0.9)
    m_strap = lib.mat("Acc_StrapMint", "#1F8F60", rough=0.58)
    objs = [lib.part("Backpack_RollTop_Body", body, m_body, col),
            lib.part("Backpack_RollTop_Trim", trim, m_trim, col),
            lib.part("Backpack_RollTop_Straps", strap, m_strap, col),
            lib.part("Backpack_RollTop_Metal", metal, m_metal, col)]
    return lib.group("Backpack_RollTop", objs, col)


def build_piggy(lib, col):
    """Копилка-рюкзак: прорезь для монет, пятачок, торчащая монетка."""
    L = local_frame(loc=(0, 0.098, 0.980))
    body, face, dark, metal, strap = (bmesh.new() for _ in range(5))

    lib.add_sphere(body, 0.138, loc=L((0, 0, 0)), scale=(1.0, 0.72, 0.86), segs=24, rings=16)
    # ушки
    for sx in (-0.062, 0.062):
        lib.add_box(face, (0.052, 0.030, 0.050), loc=L((sx, -0.010, 0.112)),
                    rot=(BODY_ROT[0], 0, math.radians(14 if sx > 0 else -14)),
                    round_k=0.62, rows=8, taper=0.35)
    # пятачок на внешней стороне
    lib.add_sphere(face, 0.040, loc=L((0, 0.098, -0.014)), scale=(1.0, 0.42, 0.78),
                   segs=18, rings=10)
    for sx in (-0.012, 0.012):
        lib.add_sphere(dark, 0.007, loc=L((sx, 0.116, -0.014)), scale=(1, 0.6, 1.4),
                       segs=10, rings=6)
    # глазки
    for sx in (-0.052, 0.052):
        lib.add_sphere(dark, 0.013, loc=L((sx, 0.094, 0.042)), scale=(1, 0.55, 1.1),
                       segs=12, rings=8)
    # прорезь для монет + монетка
    lib.add_box(dark, (0.090, 0.016, 0.014), loc=L((0, 0.010, 0.124)), rot=BODY_ROT,
                round_k=0.30, rows=6)
    lib.add_sphere(metal, 0.030, loc=L((0.014, 0.012, 0.146)), scale=(1.0, 0.16, 1.0),
                   segs=20, rings=10)
    add_straps(lib, strap, top=(0.070, 0.032, 1.094), bottom=(0.114, -0.020, 0.876))
    add_buckles(lib, metal, zs=(1.00,))

    m_body = lib.mat("Acc_Bag_Piggy", "pink", rough=0.46)
    m_face = lib.mat("Acc_Bag_PiggyFace", "#FFB3CC", rough=0.44)
    m_dark = lib.mat("Acc_Bag_PiggyDark", "#3A2430", rough=0.38)
    m_coin = lib.mat("Acc_Coin", "yellow", rough=0.24, metal=1.0)
    m_strap = lib.mat("Acc_StrapPink", "#C94472", rough=0.56)
    objs = [lib.part("Backpack_Piggy_Body", body, m_body, col),
            lib.part("Backpack_Piggy_Face", face, m_face, col),
            lib.part("Backpack_Piggy_Dark", dark, m_dark, col),
            lib.part("Backpack_Piggy_Coin", metal, m_coin, col),
            lib.part("Backpack_Piggy_Straps", strap, m_strap, col)]
    return lib.group("Backpack_Piggy", objs, col)


def build_foxmini(lib, col):
    """Маленький рюкзачок-лисёнок с мордочкой."""
    L = local_frame(loc=(0, 0.092, 1.000), rot=(-0.18, 0, 0))
    body, cream, dark, strap, metal = (bmesh.new() for _ in range(5))

    lib.add_box(body, (0.206, 0.140, 0.196), loc=L((0, 0, 0)), rot=(-0.18, 0, 0),
                round_k=0.62, rows=12)
    # ушки
    for sx in (-0.068, 0.068):
        lib.add_box(body, (0.066, 0.040, 0.086), loc=L((sx, -0.004, 0.118)),
                    rot=(-0.18, 0, math.radians(18 if sx > 0 else -18)),
                    round_k=0.52, rows=10, taper=0.06)
        lib.add_box(cream, (0.032, 0.022, 0.044), loc=L((sx, -0.016, 0.110)),
                    rot=(-0.18, 0, math.radians(18 if sx > 0 else -18)),
                    round_k=0.52, rows=8, taper=0.10)
    # мордочка на внешней стороне
    lib.add_sphere(cream, 0.052, loc=L((0, 0.076, -0.024)), scale=(1.15, 0.38, 0.80),
                   segs=20, rings=12)
    lib.add_sphere(dark, 0.015, loc=L((0, 0.094, -0.008)), scale=(1.2, 0.5, 0.85),
                   segs=14, rings=8)
    for sx in (-0.044, 0.044):
        lib.add_sphere(dark, 0.013, loc=L((sx, 0.080, 0.034)), scale=(1, 0.5, 1.15),
                       segs=12, rings=8)
    # клапан-крышка сверху
    lib.add_box(body, (0.210, 0.146, 0.044), loc=L((0, 0.004, 0.084)), rot=(-0.18, 0, 0),
                round_k=0.55, rows=8)
    add_straps(lib, strap, top=(0.064, 0.032, 1.086), bottom=(0.108, -0.018, 0.902))
    add_buckles(lib, metal, zs=(1.02,), size=(0.044, 0.018, 0.022))

    m_body = lib.mat("Acc_Bag_Fox", "primary", rough=0.50)
    m_cream = lib.mat("Acc_Bag_FoxCream", "#FFF1E2", rough=0.48)
    m_dark = lib.mat("Acc_Bag_FoxDark", "#3B2318", rough=0.40)
    m_strap = lib.mat("Acc_StrapBrown", "#8A5A3B", rough=0.58)
    m_metal = lib.mat("Acc_Buckle", "yellow", rough=0.28, metal=0.9)
    objs = [lib.part("Backpack_FoxMini_Body", body, m_body, col),
            lib.part("Backpack_FoxMini_Cream", cream, m_cream, col),
            lib.part("Backpack_FoxMini_Dark", dark, m_dark, col),
            lib.part("Backpack_FoxMini_Straps", strap, m_strap, col),
            lib.part("Backpack_FoxMini_Metal", metal, m_metal, col)]
    return lib.group("Backpack_FoxMini", objs, col)


def build_all(lib):
    col = lib.collection("Finik_Accessories")
    bcol = lib.collection("Backpacks", col)
    lib.purge("Backpack_")
    return [build_school(lib, bcol), build_rolltop(lib, bcol),
            build_piggy(lib, bcol), build_foxmini(lib, bcol)]
