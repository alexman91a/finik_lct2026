"""Бейсболка v2: купол-лофт по замеру головы, отверстия под уши с окантовкой, 6 панелей.

Вместо «полусфера + булев вырез + выталкивание» купол сразу строится как параметрическая
поверхность P(theta, t), которая огибает голову (включая хохолок) с зазором CLEAR.
Отверстия под уши вырезаются в сетке параметров и сглаживаются в том же пространстве,
поэтому края ровные, а окантовка идёт по реальной границе.
"""
import bpy, bmesh, math
import numpy as np
from mathutils import Vector

CY = -0.19            # ось купола по Y (центр черепа)
N_TH = 72             # сегментов по окружности
N_T = 22              # колец от налобной ленты к макушке
CLEAR = 0.009         # зазор от головы
Z_BINS = np.arange(1.30, 1.80, 0.01)


def _dir(th):
    return Vector((math.sin(th), -math.cos(th), 0.0))       # th=0 -> вперёд (-Y), +90 -> +X


# ---------------------------------------------------------------- замер головы
class Ears:
    """Уши, отделённые от черепа по толщине: луч внутрь от поверхности уха упирается
    в обратную сторону через 4-6 см, от черепа — проходит голову насквозь. Кромки уха
    (нормаль вдоль пластины) доразмечаются голосованием соседей по сетке."""

    def __init__(self, lib, thin=0.12, zone_x=0.06, zone_z=1.36, hole_x=0.115):
        from mathutils.kdtree import KDTree
        char = bpy.data.objects["Finik_Mesh"]
        mw = char.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        me = char.data
        n = len(me.vertices)
        co = np.empty(n * 3)
        me.vertices.foreach_get("co", co)
        co = co.reshape(n, 3)
        co = (np.c_[co, np.ones(n)] @ np.array(mw).T)[:, :3]
        zone = (np.abs(co[:, 0]) > zone_x) & (co[:, 2] > zone_z)
        bvh = lib.body_bvh()
        thin_mask = np.zeros(n, dtype=bool)
        for i in np.where(zone)[0]:
            v = me.vertices[i]
            nrm = (nm @ v.normal).normalized()
            p = Vector(co[i])
            thin_mask[i] = bvh.ray_cast(p - nrm * 1e-4, -nrm, thin)[0] is not None
        ed = np.empty(len(me.edges) * 2, dtype=np.int64)
        me.edges.foreach_get("vertices", ed)
        ed = ed.reshape(-1, 2)
        lab = thin_mask.astype(float)
        for _ in range(6):                          # голосование по 1-кольцу
            acc = np.zeros(n)
            cnt = np.zeros(n)
            np.add.at(acc, ed[:, 0], lab[ed[:, 1]])
            np.add.at(acc, ed[:, 1], lab[ed[:, 0]])
            np.add.at(cnt, ed[:, 0], 1)
            np.add.at(cnt, ed[:, 1], 1)
            vote = (acc + lab) / (cnt + 1)          # большинство по 1-кольцу вместе с собой
            lab = np.where(zone, vote > 0.5, False).astype(float)
        self.all_mask = lab > 0.5                   # всё тонкое: уши и хохолок
        self.ear_mask = self.all_mask & (np.abs(co[:, 0]) > hole_x)
        self.co = co
        self.kd = KDTree(int(self.ear_mask.sum()))
        for k, i in enumerate(np.where(self.ear_mask)[0]):
            self.kd.insert(Vector(co[i]), k)
        self.kd.balance()

    def near(self, points, dist):
        return np.array([self.kd.find(Vector(p))[2] < dist for p in np.asarray(points).reshape(-1, 3)])


def head_envelope(lib, bvh):
    """env[i_theta, i_z] — радиус черепа по лучам ИЗНУТРИ от оси купола.

    Первое попадание луча, выпущенного изнутри головы, — поверхность черепа; уши снаружи
    и в замер не попадают. Уровни, где ось уже вне головы (выше макушки), пустые."""
    env = np.full((N_TH, len(Z_BINS)), np.nan)
    for k, z in enumerate(Z_BINS):
        o = Vector((0.0, CY, float(z)))
        if not lib.is_inside(bvh, o):
            continue
        for i in range(N_TH):
            loc = bvh.ray_cast(o, _dir(i * math.tau / N_TH), 0.6)[0]
            if loc is not None:
                env[i, k] = (loc - o).length
    # основание уха даёт узкие (~30 град) выбросы радиуса по бокам — срезаем их
    # круговой медианой: череп гладкий по углу, ухо — нет
    w = 21
    for k in range(env.shape[1]):
        row = env[:, k]
        if np.all(np.isnan(row)):
            continue
        ext = np.concatenate([row[-w:], row, row[:w]])
        med = np.array([np.nanmedian(ext[i:i + 2 * w + 1]) for i in range(len(row))])
        env[:, k] = np.where(np.isnan(row), row, np.minimum(row, med + 0.015))
    return env


def head_top(ears):
    """Высшая точка головы в центральной полосе (включая хохолок, без ушей)."""
    c = ears.co[(~ears.ear_mask) & (np.abs(ears.co[:, 0]) < 0.12)]
    return float(c[:, 2].max())


def _env_at(env, th, z):
    i = int((th % math.tau) / math.tau * N_TH) % N_TH
    k = int(round((z - Z_BINS[0]) / 0.01))
    if k < 0 or k >= env.shape[1]:
        return float('nan')
    return env[i, k]


def _fill_periodic(a):
    """Заполняет NaN периодической линейной интерполяцией."""
    a = np.asarray(a, dtype=float)
    n = len(a)
    good = np.where(~np.isnan(a))[0]
    if len(good) == 0:
        return np.full(n, 0.2)
    xs = np.concatenate([good - n, good, good + n])
    ys = np.concatenate([a[good]] * 3)
    return np.interp(np.arange(n), xs, ys)


def _smooth_periodic(a, w=5, passes=2):
    for _ in range(passes):
        k = np.ones(w) / w
        a = np.convolve(np.concatenate([a[-w:], a, a[:w]]), k, mode='same')[w:-w]
    return a


# ---------------------------------------------------------------- поверхность
class Crown:
    """Купол вокруг черепа. rim_front/rim_back — высота края спереди/сзади,
    top_lift — запас над макушкой, fullness — показатель профиля (меньше = полнее бока)."""

    def __init__(self, env, head_top, rim_front=1.450, rim_back=1.402, top_lift=0.014,
                 fullness=0.58, clear=CLEAR):
        self.env = env
        self.fullness = fullness
        ths = np.arange(N_TH) * math.tau / N_TH
        mid, amp = (rim_front + rim_back) / 2, (rim_front - rim_back) / 2
        self.rim_z = mid + amp * np.cos(ths)                            # спереди выше, сзади ниже
        raw = np.array([_env_at(env, t, z) for t, z in zip(ths, self.rim_z)])
        rim = _fill_periodic(raw) + clear
        self.rim_r = np.maximum(_smooth_periodic(rim, 7, 3), 0.165)
        self.z_top = max(head_top + clear + top_lift, 1.655)
        # масштаб, гарантирующий зазор по всей высоте (сглаженный, чтобы не было шишек)
        s = np.ones((N_TH, N_T))
        for i, th in enumerate(ths):
            for j in range(N_T):
                t = j / N_T
                r, z = self._base(i, t)
                e = _env_at(env, th, z)
                if not np.isnan(e) and r > 1e-6:
                    s[i, j] = max(1.0, (e + clear) / r)
        for _ in range(3):
            s = np.maximum(s, 0.5 * (np.roll(s, 1, 0) + np.roll(s, -1, 0)))
            s = 0.25 * np.roll(s, 1, 0) + 0.5 * s + 0.25 * np.roll(s, -1, 0)
        self.scale = s

    def _base(self, i, t):
        r = self.rim_r[i] * math.cos(t * math.pi / 2) ** self.fullness
        z = self.rim_z[i] + (self.z_top - self.rim_z[i]) * math.sin(t * math.pi / 2) ** 0.92
        return r, z

    def eval(self, th, t, lift=0.0):
        """Точка поверхности для непрерывных (theta, t); lift — смещение наружу."""
        f = (th % math.tau) / math.tau * N_TH
        i0 = int(f) % N_TH
        i1 = (i0 + 1) % N_TH
        a = f - int(f)
        tt = min(max(t, 0.0), 1.0)
        fj = min(tt * N_T, N_T - 1.0001)
        j0 = int(fj)
        b = fj - j0

        def at(i):
            r, z = self._base(i, tt)
            sc = self.scale[i, j0] * (1 - b) + self.scale[i, min(j0 + 1, N_T - 1)] * b
            return r * sc, z

        r0, z0 = at(i0)
        r1, z1 = at(i1)
        r = r0 * (1 - a) + r1 * a + lift
        z = z0 * (1 - a) + z1 * a + lift * math.sin(tt * math.pi / 2)
        return Vector((0, CY, 0)) + _dir(th) * r + Vector((0, 0, z))


# ---------------------------------------------------------------- отверстия под уши
R_U, H_V = 0.19, 0.24      # метрика пространства параметров: дуга по theta и высота по t
T_MIN = 0.0                # основание уха ниже ленты: вырез открыт от края
HOLE_MARGIN = 0.010


class EarHoles:
    """Овалы в пространстве (theta, t): эллипс вписывается (PCA) в пятно, где купол
    проходит сквозь ухо, и раздувается на HOLE_MARGIN. Край сетки проецируется на овал."""

    def __init__(self, params, raw):
        self.holes = []
        for side in (1, -1):
            uv = np.array([self._uv(p, side) for p, r in zip(params, raw)
                           if r and math.sin(p[0]) * side > 0.2])
            if len(uv) < 4:
                continue
            c = uv.mean(0)
            w, V = np.linalg.eigh(np.cov((uv - c).T))
            sd = np.sqrt(np.maximum(w, 1e-8))
            k = np.sqrt((((uv - c) @ V / sd) ** 2).sum(1)).max()
            self.holes.append((side, c, V, sd * k + HOLE_MARGIN))

    @staticmethod
    def _uv(p, side):
        d = (p[0] - side * math.pi / 2 + math.pi) % math.tau - math.pi
        return np.array([d * R_U, p[1] * H_V])

    def _hole(self, p):
        side = 1 if math.sin(p[0]) > 0 else -1
        return next((h for h in self.holes if h[0] == side), None)

    def contains(self, p, grow=0.0):
        if p[1] < T_MIN - 1e-9:
            return False
        h = self._hole(p)
        if h is None:
            return False
        side, c, V, r = h
        loc = (self._uv(p, side) - c) @ V
        return ((loc / (r + grow)) ** 2).sum() <= 1.0

    def project(self, p):
        """Точка на границе ближайшего овала (по лучу из его центра)."""
        h = self._hole(p)
        if h is None:
            return p
        side, c, V, r = h
        loc = (self._uv(p, side) - c) @ V
        n = math.sqrt(((loc / r) ** 2).sum()) or 1.0
        uv = c + V @ (loc / n)
        th = uv[0] / R_U + side * math.pi / 2
        return [th, max(uv[1] / H_V, T_MIN)]


# ---------------------------------------------------------------- сборка
def _newell(pts):
    n = Vector()
    for a, b in zip(pts, pts[1:] + pts[:1]):
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n.normalized() if n.length > 1e-9 else Vector((0, 0, 1))


def _boundary_loops(bm):
    edges = [e for e in bm.edges if e.is_boundary]
    adj = {}
    for e in edges:
        a, b = e.verts
        adj.setdefault(a, []).append(b)
        adj.setdefault(b, []).append(a)
    seen, loops = set(), []
    for start in adj:
        if start in seen:
            continue
        loop, prev, cur = [start], None, start
        seen.add(start)
        while True:
            nxt = [v for v in adj[cur] if v is not prev]
            if not nxt or nxt[0] is start:
                break
            prev, cur = cur, nxt[0]
            if cur in seen:
                break
            seen.add(cur)
            loop.append(cur)
        if len(loop) > 4:
            loops.append(loop)
    return loops


def _runs(loop, param):
    """Делит замкнутую границу на участки по кромке (t=0) и по вырезам (t>0)."""
    flags = [param[v][1] == 0.0 for v in loop]
    if all(flags) or not any(flags):
        return [(flags[0], [v.co.copy() for v in loop], True)]
    k0 = next(i for i in range(len(loop)) if flags[i] != flags[i - 1])
    loop, flags = loop[k0:] + loop[:k0], flags[k0:] + flags[:k0]
    runs, cur = [], [0]
    for i in range(1, len(loop)):
        if flags[i] == flags[cur[0]]:
            cur.append(i)
        else:
            runs.append(cur)
            cur = [i]
    runs.append(cur)
    out = []
    for r in runs:
        idx = r + [(r[-1] + 1) % len(loop)]            # стык с соседним участком
        out.append((flags[r[0]], [loop[i].co.copy() for i in idx], False))
    return out


def _bump_out(lib, bvh, crown, grid, top, step=1.03, iters=8):
    """Пока вершина купола вне зоны ушей оказывается внутри тела (хохолок, шерсть),
    поднимаем масштаб в её окрестности 3x3 с мягким спадом и пересчитываем сетку."""
    for _ in range(iters):
        hit = set()
        for (i, j), v in grid.items():
            if abs(v.co.x) > 0.10 or j >= N_T - 1:
                continue
            q = (v, grid[(i + 1) % N_TH, j], grid[(i + 1) % N_TH, j + 1], grid[i, j + 1])
            # вершина, центр ячейки и середины рёбер: тонкий кончик хохолка
            # может пройти между вершинами крупной сетки
            probes = [q[0].co, sum((x.co for x in q), Vector()) / 4,
                      (q[0].co + q[1].co) / 2, (q[0].co + q[3].co) / 2]
            if any(lib.is_inside(bvh, p) for p in probes):
                hit.add((i, j))
        hit = sorted(hit)
        if not hit:
            return
        for i, j in hit:
            for di in (-2, -1, 0, 1, 2):
                for dj in (-2, -1, 0, 1, 2):
                    jj = j + dj
                    if 0 <= jj < N_T:
                        w = 1.0 - 0.3 * max(abs(di), abs(dj))
                        ii = (i + di) % N_TH
                        crown.scale[ii, jj] *= 1.0 + (step - 1.0) * w
        for (i, j), v in grid.items():
            v.co = crown.eval(i * math.tau / N_TH, j / N_T)
        top.co = crown.eval(0.0, 1.0)


def shell(lib, **crown_kw):
    """Оболочка головного убора: купол по черепу с вырезами под уши.

    Возвращает dict: bm (с толщиной), crown, holes и runs — участки границы
    [(is_rim, points, closed)] для окантовки."""
    ears = Ears(lib)
    bvh = lib.body_bvh()
    env = head_envelope(lib, bvh)
    crown = Crown(env, head_top(ears), **crown_kw)

    bm = bmesh.new()
    param, grid = {}, {}
    for i in range(N_TH):
        th = i * math.tau / N_TH
        for j in range(N_T):
            t = j / N_T
            v = bm.verts.new(crown.eval(th, t))
            grid[i, j] = v
            param[v] = [th, t]
    top = bm.verts.new(crown.eval(0.0, 1.0))
    param[top] = [0.0, 1.0]
    _bump_out(lib, bvh, crown, grid, top)
    verts = list(bm.verts)
    raw = [lib.is_inside(bvh, v.co) and abs(v.co.x) > 0.10 for v in verts]
    holes = EarHoles([param[v] for v in verts], raw)
    hole = {v: holes.contains(param[v]) for v in verts}
    for i in range(N_TH):
        i1 = (i + 1) % N_TH
        for j in range(N_T - 1):
            q = (grid[i, j], grid[i1, j], grid[i1, j + 1], grid[i, j + 1])
            if not any(hole[v] for v in q):
                bm.faces.new(q)
        tri = (grid[i, N_T - 1], grid[i1, N_T - 1], top)
        if not any(hole[v] for v in tri):
            bm.faces.new(tri)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')

    # край сетки (ступеньки квадов) проецируется точно на овал
    for loop in _boundary_loops(bm):
        for v in loop:
            if param[v][1] > 0.0:
                param[v] = holes.project(param[v])
                v.co = crown.eval(*param[v])
    runs = []
    for loop in _boundary_loops(bm):
        runs += _runs(loop, param)
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.005)
    return {"bm": bm, "crown": crown, "holes": holes, "runs": runs}


def _trim(lib, bm, runs, r_rim, r_cut):
    """Окантовка: толще по кромке, тоньше по вырезам под уши."""
    for is_rim, pts, closed in runs:
        if closed and len(pts) > 8:
            pts = lib.smooth_contour(pts, iterations=2, densify=1)
        if len(pts) < 2:
            continue
        lib.add_tube(bm, pts, r_rim if is_rim else r_cut, sections=10,
                     up=_newell(pts) if closed else (0, 0, 1), closed=closed)


def _meridians(lib, bm, crown, holes, count, t0, t1, radius, lift, phase=0.0):
    """Линии от края к макушке (швы панелей, рубчик вязки) с разрывом на вырезах."""
    for k in range(count):
        th = phase + k * math.tau / count
        seg = []
        for s_ in range(22):
            t = t0 + (t1 - t0) * s_ / 21
            if holes.contains([th, t], grow=0.010):
                if len(seg) > 2:
                    lib.add_tube(bm, seg, radius, sections=4, up=(0, 0, 1))
                seg = []
            else:
                seg.append(crown.eval(th, t, lift=lift))
        if len(seg) > 2:
            lib.add_tube(bm, seg, radius, sections=4, up=(0, 0, 1))


def build_cap(lib, col):
    """Бейсболка: 6 панелей со швами, пуговка, лента, козырёк из переднего края."""
    sh = shell(lib)
    crown, holes = sh["crown"], sh["holes"]
    band_bm, seam_bm, brim_bm = bmesh.new(), bmesh.new(), bmesh.new()
    _trim(lib, band_bm, sh["runs"], 0.0068, 0.0056)
    _meridians(lib, seam_bm, crown, holes, 6, 0.03, 0.97, 0.0024, 0.0022)
    lib.add_sphere(seam_bm, 0.018, loc=crown.eval(0, 1.0) + Vector((0, 0, 0.003)),
                   scale=(1, 1, 0.62), segs=16, rings=8)

    # козырёк: растёт из переднего края купола, бока загнуты вниз пропорционально вылету
    sweep, n = math.radians(76), 34
    back_edge, front_edge = [], []
    for s_ in range(n):
        th = -sweep + 2 * sweep * s_ / (n - 1)
        base = crown.eval(th, 0.0, lift=-0.002)
        w = max(0.0, (math.cos(th) - math.cos(sweep)) / (1 - math.cos(sweep)))
        L = 0.168 * w ** 0.5
        out = base + _dir(th) * L
        out.z = base.z - (0.020 + 0.030 * math.sin(th) ** 2) * (L / 0.168)
        back_edge.append(base + Vector((0, 0, 0.002)))
        front_edge.append(out)
    lib.add_strip(brim_bm, back_edge, front_edge, thickness=0.0075, up=(0, 0, 1), bulge=0.004)
    lib.add_tube(brim_bm, front_edge, 0.0038, sections=8, up=(0, 0, 1))

    m_crown = lib.mat("Acc_Cap", "accent", rough=0.62)
    m_brim = lib.mat("Acc_CapBrim", "#2F2A86", rough=0.55)
    m_trim = lib.mat("Acc_CapTrim", "yellow", rough=0.45)
    return lib.group("Gear_Cap", [
        lib.part("Gear_Cap_Crown", sh["bm"], m_crown, col),
        lib.part("Gear_Cap_Brim", brim_bm, m_brim, col),
        lib.part("Gear_Cap_Band", band_bm, m_brim, col),
        lib.part("Gear_Cap_Trim", seam_bm, m_trim, col)], col)


def _cuff(lib, bm, edge_bm, crown, holes, tc, lift, steps=120):
    """Подвёрнутый отворот: полоса над нижней частью купола, разрезанная у ушей,
    с валиками по верхнему и нижнему краю."""
    rows = 3
    segs, cur = [], []
    for k in range(steps + 1):
        th = k * math.tau / steps
        blocked = any(holes.contains([th, tc * r / rows], grow=0.006) for r in range(rows + 1))
        if blocked:
            if len(cur) > 1:
                segs.append(cur)
            cur = []
        else:
            cur.append(th)
    if len(cur) > 1:
        segs.append(cur)
    if len(segs) > 1 and segs[0][0] == 0.0 and abs(segs[-1][-1] - math.tau) < 1e-9:
        segs[0] = segs[-1][:-1] + segs[0]              # склейка через theta=0
        segs.pop()
    for seg in segs:
        closed = len(seg) >= steps
        grid = [[bm.verts.new(crown.eval(th, tc * r / rows, lift=lift)) for r in range(rows + 1)]
                for th in seg]
        n = len(grid) if closed else len(grid) - 1
        for i in range(n):
            a, b = grid[i], grid[(i + 1) % len(grid)]
            for r in range(rows):
                bm.faces.new((a[r], b[r], b[r + 1], a[r + 1]))
        for t_edge, rad in ((tc, 0.0095), (0.0, 0.0075)):
            pts = [crown.eval(th, t_edge, lift=lift + 0.002) for th in seg]
            lib.add_tube(edge_bm, pts, rad, sections=10, up=(0, 0, 1), closed=closed)
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.006)


def build_beanie(lib, col):
    """Вязаная шапка: купол, подвёрнутый отворот, рубчик, пушистый помпон."""
    from mathutils import noise
    sh = shell(lib, rim_front=1.428, rim_back=1.378, top_lift=0.030, fullness=0.95,
               clear=0.011)
    crown, holes = sh["crown"], sh["holes"]
    cuff_bm, cuff_edge_bm, rib_bm, pom_bm, cut_bm = (bmesh.new() for _ in range(5))
    tc = 0.16
    _cuff(lib, cuff_bm, cuff_edge_bm, crown, holes, tc, lift=0.010)
    _trim(lib, cut_bm, [r for r in sh["runs"] if not r[0]], 0.0, 0.0060)   # только вырезы
    _meridians(lib, rib_bm, crown, holes, 26, tc + 0.03, 0.93, 0.0021, 0.0012, phase=0.05)

    cen = crown.eval(0, 1.0) + Vector((0, 0, 0.040))
    lib.add_sphere(pom_bm, 0.048, loc=cen, segs=20, rings=14)
    for v in pom_bm.verts:
        d = v.co - cen
        v.co = cen + d * (1.0 + 0.09 * noise.noise(d * 95.0))

    m_body = lib.mat("Acc_Beanie", "sky", rough=0.82)
    m_rib = lib.mat("Acc_BeanieRib", "#1E8FCB", rough=0.84)
    m_cuff = lib.mat("Acc_BeanieCuff", "#7FCBF4", rough=0.84)
    m_trim = lib.mat("Acc_BeanieTrim", "#F4FBFF", rough=0.80)
    return lib.group("Gear_Beanie", [
        lib.part("Gear_Beanie_Body", sh["bm"], m_body, col),
        lib.part("Gear_Beanie_Rib", rib_bm, m_rib, col),
        lib.part("Gear_Beanie_Cuff", cuff_bm, m_cuff, col),
        lib.part("Gear_Beanie_Trim", cuff_edge_bm, m_trim, col),
        lib.part("Gear_Beanie_Binding", cut_bm, m_trim, col),
        lib.part("Gear_Beanie_Pom", pom_bm, m_trim, col)], col)
