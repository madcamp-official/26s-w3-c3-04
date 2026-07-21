# -*- coding: utf-8 -*-
"""
CathedralArena 생성 스크립트 v3 (Blender 5.x, headless)
- SF 기계 성당 아레나: 3티어(0/6/12m), 벽 22m, 계단식 팔각 돔
- v3 변경:
  * 렌더 최적화: 재질별 서브메시로 병합한 CathedralArena_Merged.fbx (드로우콜 최소화)
  * 바닥/벽 회로(서킷) 트레이스 — 로봇/기계 느낌의 직각 네온 라인 + 패드
  * N벽(입구 반대편) 전면 스테인드 글라스 + 채광 연출, S벽 중앙 이중 게이트(입구)
  * 어두운 무드 + 보라/청록 네온 강조 (M_GlowTeal / M_GlowPurple)
  * 메달리온 십자가 → 성경 아이콘 패널(별/성배/십계명)
  * 발광 강도 하향 (창 5→2, 십자가 12→5)

실행:
  blender.exe --background --python cathedral_map_gen.py
"""
import bpy
import bmesh
import math
import os

# ---------------------------------------------------------------- 경로
ROOT = r"C:/Users/User/kaist_madcamp/week3"
FBX_DIR = ROOT + "/PrecogPrototype/Assets/_Project/Art/CathedralArena"
BLEND_PATH = ROOT + "/ArtSource/CathedralArena.blend"
RENDER_DIR = os.environ.get("CATHEDRAL_RENDER_DIR", ROOT + "/ArtSource/renders")

os.makedirs(FBX_DIR, exist_ok=True)
os.makedirs(RENDER_DIR, exist_ok=True)

# ---------------------------------------------------------------- 씬 초기화
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'

lib_col = bpy.data.collections.new("AssetLibrary")
arena_col = bpy.data.collections.new("Arena")
dome_col = bpy.data.collections.new("Dome")
scene.collection.children.link(lib_col)
scene.collection.children.link(arena_col)
arena_col.children.link(dome_col)

# ---------------------------------------------------------------- 재질
def make_mat(name, color, rough=0.6, emit=None, strength=0.0, display=None, metallic=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = None
    for n in m.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            bsdf = n
            break
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = metallic
        if emit is not None:
            key = "Emission Color" if "Emission Color" in bsdf.inputs else "Emission"
            bsdf.inputs[key].default_value = (*emit, 1.0)
            if "Emission Strength" in bsdf.inputs:
                bsdf.inputs["Emission Strength"].default_value = strength
    m.diffuse_color = (*(display or (emit or color)), 1.0)
    return m

M_STONE  = make_mat("M_StoneWhite", (0.85, 0.85, 0.84), rough=0.65)
M_TRIM   = make_mat("M_StoneTrim",  (0.72, 0.72, 0.71), rough=0.55)
M_METAL  = make_mat("M_MechMetal",  (0.10, 0.11, 0.13), rough=0.35, metallic=0.9,
                    display=(0.22, 0.24, 0.28))
M_DARK   = make_mat("M_DarkPanel",  (0.04, 0.05, 0.08), rough=0.7)
M_GOLD   = make_mat("M_GlowGold",   (0.25, 0.18, 0.08), emit=(1.0, 0.78, 0.35), strength=5.0)
M_TEAL   = make_mat("M_GlowTeal",   (0.03, 0.13, 0.12), emit=(0.20, 0.95, 0.85), strength=6.0)
M_PURPLE = make_mat("M_GlowPurple", (0.10, 0.04, 0.16), emit=(0.62, 0.30, 1.00), strength=6.0)
M_WHITE  = make_mat("M_GlowWindow", (0.9, 0.9, 0.9),    emit=(1.0, 0.97, 0.90), strength=2.0)
G_BLUE   = make_mat("M_Glass_Blue",   (0.025, 0.06, 0.38), emit=(0.03, 0.10, 0.72), strength=0.8)
G_VIOLET = make_mat("M_Glass_Violet", (0.18, 0.025, 0.38), emit=(0.34, 0.05, 0.68), strength=0.7)
G_TEAL   = make_mat("M_Glass_Teal",   (0.015, 0.25, 0.20), emit=(0.03, 0.48, 0.34), strength=0.7)
G_AMBER  = make_mat("M_Glass_Amber",  (0.42, 0.18, 0.015), emit=(0.82, 0.34, 0.03), strength=0.8)
G_RED    = make_mat("M_Glass_Red",    (0.38, 0.012, 0.025), emit=(0.78, 0.025, 0.04), strength=0.8)

def make_translucent_glass(mat, alpha=0.58):
    """발광판이 아니라 색이 밴 반투명 유리로 보이도록 Principled 재질을 조정한다."""
    mat.diffuse_color = (*mat.diffuse_color[:3], alpha)
    bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf:
        bsdf.inputs["Alpha"].default_value = alpha
        bsdf.inputs["Roughness"].default_value = 0.16
        if "IOR" in bsdf.inputs:
            bsdf.inputs["IOR"].default_value = 1.46
        transmission = "Transmission Weight" if "Transmission Weight" in bsdf.inputs else "Transmission"
        if transmission in bsdf.inputs:
            bsdf.inputs[transmission].default_value = 0.42
        if "Coat Weight" in bsdf.inputs:
            bsdf.inputs["Coat Weight"].default_value = 0.18
    try:
        mat.surface_render_method = 'DITHERED'
    except Exception:
        try:
            mat.blend_method = 'BLEND'
        except Exception:
            pass
    try:
        mat.use_transparency_overlap = False
    except Exception:
        pass

for glass_mat in (G_BLUE, G_VIOLET, G_TEAL, G_AMBER, G_RED):
    make_translucent_glass(glass_mat)

# ---------------------------------------------------------------- 지오메트리 빌더
class Builder:
    """박스/프리즘을 모아 하나의 메시 오브젝트로 빌드 (면별 재질 유지)."""
    def __init__(self):
        self.verts, self.faces, self.fmats, self.mats = [], [], [], []

    def _mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def box(self, c, s, mat):
        mi = self._mi(mat)
        x, y, z = c
        hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
        b = len(self.verts)
        for dz in (-hz, hz):
            for dy, dx in ((-hy, -hx), (-hy, hx), (hy, hx), (hy, -hx)):
                self.verts.append((x + dx, y + dy, z + dz))
        for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
                  (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.faces.append(tuple(b + i for i in f))
            self.fmats.append(mi)

    def _prism(self, ring0, ring1, mi):
        n = len(ring0)
        b = len(self.verts)
        self.verts.extend(ring0)
        self.verts.extend(ring1)
        self.faces.append(tuple(b + i for i in range(n)))
        self.fmats.append(mi)
        self.faces.append(tuple(b + n + i for i in reversed(range(n))))
        self.fmats.append(mi)
        for i in range(n):
            j = (i + 1) % n
            self.faces.append((b + i, b + j, b + n + j, b + n + i))
            self.fmats.append(mi)

    def prism_y(self, pts_xz, y0, y1, mat):
        self._prism([(x, y0, z) for x, z in pts_xz],
                    [(x, y1, z) for x, z in pts_xz], self._mi(mat))

    def prism_z(self, pts_xy, z0, z1, mat):
        self._prism([(x, y, z0) for x, y in pts_xy],
                    [(x, y, z1) for x, y in pts_xy], self._mi(mat))

    def prism_x(self, pts_yz, x0, x1, mat):
        self._prism([(x0, y, z) for y, z in pts_yz],
                    [(x1, y, z) for y, z in pts_yz], self._mi(mat))

    def ring_z(self, ro, ri, z0, z1, mat, offset=0.0):
        for i in range(8):
            a0 = math.radians(offset + 45 * i)
            a1 = math.radians(offset + 45 * (i + 1))
            quad = [(ro * math.cos(a0), ro * math.sin(a0)),
                    (ro * math.cos(a1), ro * math.sin(a1)),
                    (ri * math.cos(a1), ri * math.sin(a1)),
                    (ri * math.cos(a0), ri * math.sin(a0))]
            self.prism_z(quad, z0, z1, mat)

    def build(self, name):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.verts, [], self.faces)
        mesh.validate()
        for m in self.mats:
            mesh.materials.append(m)
        for p, mi in zip(mesh.polygons, self.fmats):
            p.material_index = mi
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(name, mesh)
        lib_col.objects.link(obj)
        return obj

def lancet(w, zb, zs, cx=0.0):
    half = w / 2
    pts = [(cx - half, zb), (cx + half, zb), (cx + half, zs)]
    for deg in (15, 30, 45, 60):
        a = math.radians(deg)
        pts.append((cx - half + w * math.cos(a), zs + w * math.sin(a)))
    for deg in (45, 30, 15):
        a = math.radians(deg)
        pts.append((cx + half - w * math.cos(a), zs + w * math.sin(a)))
    pts.append((cx - half, zs))
    return pts

def octagon(r, offset=22.5):
    return [(r * math.cos(math.radians(offset + 45 * i)),
             r * math.sin(math.radians(offset + 45 * i))) for i in range(8)]

def add_cross(b, x, z, scale=1.0):
    b.box((x, -0.07, z), (0.22 * scale, 0.12, 1.4 * scale), M_GOLD)
    b.box((x, -0.07, z + 0.28 * scale), (0.8 * scale, 0.12, 0.22 * scale), M_GOLD)

def add_window(b, w, zb, zs):
    fw = w + 0.36
    b.prism_y(lancet(fw, zb - 0.18, zs), -0.07, 0.05, M_TRIM)
    b.prism_y(lancet(w, zb, zs), -0.11, -0.05, M_WHITE)

# -------- 회로 트레이스 (로봇/기계 라인) --------
def trace_wall(b, pts, mat, w=0.07):
    """벽면 (x,z) 직각 폴리라인 + 꺾임 패드."""
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        sx = abs(x1 - x0) + w
        sz = abs(z1 - z0) + w
        b.box((cx, -0.065, cz), (sx, 0.07, sz), mat)
    for x, z in pts[1:-1]:
        b.box((x, -0.075, z), (0.16, 0.07, 0.16), mat)
    ex, ez = pts[-1]
    b.box((ex, -0.075, ez), (0.26, 0.07, 0.26), mat)   # 종단 패드
    b.box((ex, -0.085, ez), (0.10, 0.07, 0.10), M_DARK)

def trace_floor(b, pts, mat, w=0.12):
    """바닥 (x,y) 직각 폴리라인 + 꺾임 패드."""
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        sx = abs(x1 - x0) + w
        sy = abs(y1 - y0) + w
        b.box((cx, cy, 0.016), (sx, sy, 0.03), mat)
    for x, y in pts[1:-1]:
        b.box((x, y, 0.02), (0.3, 0.3, 0.03), mat)
    ex, ey = pts[-1]
    b.box((ex, ey, 0.02), (0.5, 0.5, 0.032), mat)
    b.box((ex, ey, 0.028), (0.2, 0.2, 0.03), M_DARK)

# -------- 성경 아이콘 패널 --------
def icon_panel(b, z, kind):
    """둥근 메달리온 대체: 액자 안의 성화(별/성배/십계명)."""
    b.box((0, -0.03, z), (2.0, 0.12, 2.5), M_TRIM)     # 액자
    b.box((0, -0.06, z), (1.8, 0.08, 2.3), M_DARK)     # 어두운 배경
    y0, y1 = -0.14, -0.10
    if kind == "star":     # 베들레헴의 별
        b.prism_y([(0, z - 0.75), (0.14, z), (0, z + 0.75), (-0.14, z)], y0, y1, M_GOLD)
        b.prism_y([(-0.55, z), (0, z + 0.11), (0.55, z), (0, z - 0.11)], y0, y1, M_GOLD)
        for dx, dz in ((0.42, 0.42), (-0.42, 0.42), (0.42, -0.42), (-0.42, -0.42)):
            b.box((dx, -0.11, z + dz), (0.1, 0.06, 0.1), M_TEAL)
    elif kind == "chalice":  # 성배
        b.box((0, -0.12, z + 0.05), (0.55, 0.05, 0.30), M_GOLD)
        b.box((0, -0.12, z + 0.22), (0.62, 0.05, 0.08), M_GOLD)
        b.box((0, -0.12, z - 0.20), (0.13, 0.05, 0.35), M_GOLD)
        b.box((0, -0.12, z - 0.42), (0.50, 0.05, 0.09), M_GOLD)
        b.box((0, -0.11, z + 0.52), (0.26, 0.05, 0.26), M_PURPLE)   # 후광
        b.box((0, -0.13, z + 0.52), (0.13, 0.05, 0.13), M_GOLD)
    else:                  # 십계명 돌판
        for cx in (-0.33, 0.33):
            b.prism_y(lancet(0.5, z - 0.62, z + 0.32, cx=cx), y0, y1, M_TRIM)
            for k in range(3):
                b.box((cx, -0.15, z - 0.38 + 0.24 * k), (0.24, 0.04, 0.055), M_TEAL)

def add_mech_details(b, conduit_x):
    for dx in (0.0, 0.16):
        b.box((conduit_x + dx, -0.10, 11.0), (0.07, 0.14, 20.0), M_METAL)
    for z in (5.5, 12.5):
        b.box((conduit_x + 0.08, -0.16, z), (0.4, 0.2, 0.55), M_METAL)
        b.box((conduit_x + 0.08, -0.27, z), (0.12, 0.06, 0.12), M_TEAL)
    for z in (2.0, 6.0, 10.0, 14.0, 18.0):
        b.box((-2.5, -0.48, z), (0.16, 0.1, 0.16), M_METAL)

def add_neon_dashes(b, z, count=6, mat=None):
    x0 = -0.35 * (count - 1)
    for k in range(count):
        b.box((x0 + 0.7 * k, -0.07, z), (0.4, 0.1, 0.1), mat or M_TEAL)

# ---------------------------------------------------------------- 파라미터
WALL_H = 22.0
SEG_W = 5.0
T2, T3 = 6.0, 12.0
GAL_D = 3.0
INNER = 20.0

def wall_base(b, low_cornice=True):
    b.box((0, 0.5, WALL_H / 2), (SEG_W, 1.0, WALL_H), M_STONE)
    b.box((-2.5, -0.2, WALL_H / 2), (0.5, 0.5, WALL_H), M_STONE)
    if low_cornice:
        b.box((0, -0.14, T2 - 0.15), (SEG_W, 0.32, 0.34), M_METAL)
    b.box((0, -0.14, T3 - 0.15), (SEG_W, 0.32, 0.34), M_METAL)
    b.box((0, -0.12, 21.7), (SEG_W, 0.5, 0.6), M_METAL)
    b.box((-2.5, -0.47, 2.5), (0.12, 0.1, 1.6), M_PURPLE)
    b.box((-2.5, -0.47, 13.0), (0.12, 0.1, 1.6), M_PURPLE)

# ---------------------------------------------------------------- 벽 에셋
def asset_wall_window():
    b = Builder()
    wall_base(b)
    add_window(b, 1.3, 6.7, 9.5)
    add_window(b, 1.5, 12.7, 17.6)
    b.prism_y(lancet(0.9, 0.45, 2.0), -0.10, -0.04, M_TEAL)   # 지상 네온 아치
    add_cross(b, -1.55, 2.0); add_cross(b, 1.55, 2.0)
    add_cross(b, -1.7, 8.0);  add_cross(b, 1.7, 8.0)
    add_cross(b, -1.7, 15.0); add_cross(b, 1.7, 15.0)
    add_cross(b, 0, 20.0)
    add_neon_dashes(b, 3.15, mat=M_TEAL)
    trace_wall(b, ((1.6, 0.9), (1.6, 2.7), (2.25, 2.7), (2.25, 4.6)), M_TEAL)
    trace_wall(b, ((-1.5, 16.2), (-1.5, 18.8), (-0.2, 18.8)), M_PURPLE)
    add_mech_details(b, 2.0)
    return b.build("SM_Wall_Window")

def asset_wall_plain(kind, name):
    b = Builder()
    wall_base(b)
    add_cross(b, -1.25, 13.2); add_cross(b, 1.25, 13.2)
    add_cross(b, 0, 4.2); add_cross(b, 0, 18.4)
    icon_panel(b, 8.8, kind)                                   # 성경 아이콘 패널
    add_neon_dashes(b, 5.2, count=5, mat=M_PURPLE)
    tm = M_TEAL if kind == "star" else M_PURPLE
    trace_wall(b, ((-1.9, 0.8), (-1.9, 3.1), (-0.7, 3.1), (-0.7, 5.1)), tm)
    trace_wall(b, ((1.9, 6.6), (1.9, 10.4), (1.3, 10.4)), M_TEAL if kind != "star" else M_PURPLE)
    trace_wall(b, ((-1.4, 15.0), (-1.4, 17.2), (-2.2, 17.2), (-2.2, 19.4)), tm)
    add_mech_details(b, 2.0)
    return b.build(name)

# -------- 로즈 윈도우 (샤르트르 스타일) --------
def _arc_pts(cx, cz, r, a0, a1, n):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cz + r * math.sin(math.radians(a0 + (a1 - a0) * i / n)))
            for i in range(n + 1)]

def ring_wall(b, cx, cz, ro, ri, y0, y1, mat, n=32):
    """벽면 원형 고리 (석재 트레이서리/네온 링)."""
    for i in range(n):
        a0, a1 = 360.0 * i / n, 360.0 * (i + 1) / n
        b.prism_y(_arc_pts(cx, cz, ro, a0, a1, 1) + _arc_pts(cx, cz, ri, a1, a0, 1),
                  y0, y1, mat)

def sector_pane(b, cx, cz, ro, ri, a0, a1, y0, y1, mat):
    """벽면 부채꼴 유리 패널."""
    b.prism_y(_arc_pts(cx, cz, ro, a0, a1, 3) + _arc_pts(cx, cz, ri, a1, a0, 3),
              y0, y1, mat)

def disc_wall(b, cx, cz, r, y0, y1, mat, n=24):
    b.prism_y(_arc_pts(cx, cz, r, 0, 360, n)[:-1], y0, y1, mat)

def mosaic_wedge(b, cx, cz, ro, ri, a0, a1, palette, phase=0):
    """하나의 장미창 웨지를 2x2 색유리 조각으로 세분한다."""
    rm = (ro + ri) / 2
    am = (a0 + a1) / 2
    cells = ((ro, rm, a0, am), (ro, rm, am, a1),
             (rm, ri, a0, am), (rm, ri, am, a1))
    for i, (cell_ro, cell_ri, cell_a0, cell_a1) in enumerate(cells):
        sector_pane(b, cx, cz, cell_ro, cell_ri,
                    cell_a0 + 0.45, cell_a1 - 0.45, -0.10, -0.05,
                    palette[(phase + i) % len(palette)])

def rose_window(b, cz=15.45):
    """샤르트르풍 거대 장미창: 청/적 유리와 석재 트레이서리의 3중 방사 링."""
    ring_wall(b, 0, cz, 6.25, 6.00, -0.08, 0.04, M_TRIM)        # 압도적인 석재 외곽
    ring_wall(b, 0, cz, 6.12, 6.00, -0.10, -0.05, G_BLUE)       # 깊은 샤르트르 블루 림
    ring_wall(b, 0, cz, 5.98, 5.38, -0.08, 0.04, M_TRIM)        # 석재 프레임
    rose_palette = (G_BLUE, G_RED, G_VIOLET, G_AMBER, G_TEAL)
    for k in range(16):                                          # 링 A: 64개 다색 조각
        a0, a1 = 22.5 * k + 1.5, 22.5 * (k + 1) - 1.5
        mosaic_wedge(b, 0, cz, 5.32, 4.18, a0, a1,
                     rose_palette, phase=k)
    ring_wall(b, 0, cz, 4.18, 3.92, -0.07, 0.02, M_TRIM)
    for k in range(12):                                          # 링 B: 48개 다색 조각
        a0, a1 = 30 * k + 2, 30 * (k + 1) - 2
        mosaic_wedge(b, 0, cz, 3.86, 2.72, a0, a1,
                     rose_palette, phase=k + 2)
    ring_wall(b, 0, cz, 2.72, 2.46, -0.07, 0.02, M_TRIM)
    for k in range(8):                                           # 링 C: 32개 다색 조각
        a0, a1 = 45 * k + 3, 45 * (k + 1) - 3
        mosaic_wedge(b, 0, cz, 2.40, 1.36, a0, a1,
                     rose_palette, phase=k + 1)
    ring_wall(b, 0, cz, 1.36, 1.12, -0.07, 0.02, M_TRIM)
    disc_wall(b, 0, cz, 1.10, -0.10, -0.05, G_RED)               # 중앙 메달리온
    ring_wall(b, 0, cz, 0.72, 0.55, -0.12, -0.06, M_GOLD, n=24)
    disc_wall(b, 0, cz, 0.53, -0.13, -0.07, G_BLUE, n=24)
    for k in range(16):                                          # 방사 스포크
        a = math.radians(22.5 * k)
        ca, sa = math.cos(a), math.sin(a)
        nx, nz = -sa * 0.05, ca * 0.05
        b.prism_y([(1.12 * ca + nx, cz + 1.12 * sa + nz),
                   (5.38 * ca + nx, cz + 5.38 * sa + nz),
                   (5.38 * ca - nx, cz + 5.38 * sa - nz),
                   (1.12 * ca - nx, cz + 1.12 * sa - nz)], -0.075, 0.02, M_TRIM)

def lancet_figure(b, cx, kind):
    """랜싯 창 안의 성화 (스테인드 글라스 인물/상징)."""
    if kind == "star":
        b.prism_y([(cx, 7.5), (cx + 0.14, 8.2), (cx, 8.9), (cx - 0.14, 8.2)],
                  -0.145, -0.105, M_GOLD)
        b.prism_y([(cx - 0.55, 8.2), (cx, 8.31), (cx + 0.55, 8.2), (cx, 8.09)],
                  -0.145, -0.105, M_GOLD)
        for dx, dz in ((0.4, 0.4), (-0.4, 0.4), (0.4, -0.4), (-0.4, -0.4)):
            b.box((cx + dx, -0.13, 8.2 + dz), (0.1, 0.05, 0.1), M_TEAL)
    elif kind == "chalice":
        b.box((cx, -0.13, 8.25), (0.55, 0.05, 0.30), M_GOLD)
        b.box((cx, -0.13, 8.42), (0.62, 0.05, 0.08), M_GOLD)
        b.box((cx, -0.13, 8.00), (0.13, 0.05, 0.35), M_GOLD)
        b.box((cx, -0.13, 7.78), (0.50, 0.05, 0.09), M_GOLD)
        b.box((cx, -0.12, 8.72), (0.26, 0.05, 0.26), M_PURPLE)
        b.box((cx, -0.14, 8.72), (0.13, 0.05, 0.13), M_GOLD)
    elif kind == "madonna":
        ring_wall(b, cx, 8.95, 0.50, 0.40, -0.135, -0.10, M_GOLD, n=16)   # 후광
        disc_wall(b, cx, 8.95, 0.26, -0.145, -0.11, G_AMBER, n=16)        # 얼굴
        b.prism_y([(cx - 0.42, 7.05), (cx + 0.42, 7.05),
                   (cx + 0.26, 8.6), (cx - 0.26, 8.6)], -0.14, -0.10, G_BLUE)  # 로브
        b.box((cx + 0.18, -0.145, 7.9), (0.22, 0.05, 0.3), M_GOLD)        # 아기
        b.box((cx, -0.13, 6.95), (0.7, 0.05, 0.1), M_TRIM)
    elif kind == "tablets":
        for dx in (-0.33, 0.33):
            b.prism_y(lancet(0.5, 7.55, 8.5, cx=cx + dx), -0.14, -0.10, M_TRIM)
            for k in range(3):
                b.box((cx + dx, -0.15, 7.8 + 0.24 * k), (0.24, 0.04, 0.055), M_TEAL)
    else:  # crown — 십자가와 왕관
        add_cross(b, cx, 8.45, scale=1.1)
        b.box((cx, -0.13, 7.35), (0.78, 0.05, 0.14), M_GOLD)
        for dx in (-0.25, 0.0, 0.25):
            b.box((cx + dx, -0.13, 7.55), (0.14, 0.05, 0.3), M_GOLD)
        for dx in (-0.45, 0.45):
            b.box((cx + dx, -0.13, 7.35), (0.09, 0.05, 0.09), M_TEAL)

def asset_wall_rose():
    """N벽 전체(40m) 통짜 세그먼트: 거대 장미창 + 성화 랜싯 5개 (입구 반대편)."""
    b = Builder()
    b.box((0, 0.5, WALL_H / 2), (40, 1.0, WALL_H), M_STONE)
    for px in (-15, -10, 10, 15):
        b.box((px, -0.2, WALL_H / 2), (0.5, 0.5, WALL_H), M_STONE)
    b.box((0, -0.14, T2 - 0.15), (40, 0.32, 0.34), M_METAL)
    b.box((0, -0.14, T3 - 0.15), (40, 0.32, 0.34), M_METAL)
    b.box((0, -0.12, 21.7), (40, 0.5, 0.6), M_METAL)
    # 유리 바로 뒤의 어두운 공간이 반투명 색과 납선의 깊이를 드러낸다.
    disc_wall(b, 0, 15.45, 6.02, -0.045, -0.015, M_DARK, n=48)
    rose_window(b)
    # 랜싯 5개: 별 / 성배 / 성모자 / 십계명 / 십자가와 왕관
    kinds = ("star", "chalice", "madonna", "tablets", "crown")
    # 샤르트르 레퍼런스처럼 각 창도 깊은 청색을 바탕으로 적색을 반복한다.
    # 금색은 성화의 윤곽/후광에만 써서 유리 팔레트의 적청 대비를 유지한다.
    fields = ((G_BLUE, G_RED, G_AMBER, G_VIOLET, G_TEAL),
              (G_RED, G_BLUE, G_TEAL, G_AMBER, G_VIOLET),
              (G_BLUE, G_VIOLET, G_RED, G_TEAL, G_AMBER),
              (G_RED, G_AMBER, G_BLUE, G_VIOLET, G_TEAL),
              (G_BLUE, G_TEAL, G_RED, G_AMBER, G_VIOLET))
    for cx, kind, fld in zip((-8, -4, 0, 4, 8), kinds, fields):
        b.prism_y(lancet(2.56, 6.62, 9.3, cx=cx), -0.07, 0.04, M_TRIM)
        b.prism_y(lancet(2.30, 6.78, 9.28, cx=cx), -0.045, -0.015, M_DARK)
        for r_i, zb in enumerate((6.86, 7.62, 8.38)):
            for c_i, dx in enumerate((-0.76, 0.0, 0.76)):
                b.box((cx + dx, -0.085, zb + 0.34), (0.68, 0.05, 0.64),
                      fld[(r_i * 2 + c_i) % len(fld)])
        b.prism_y(lancet(1.9, 9.34, 10.28, cx=cx), -0.085, -0.045, fld[3])
        for dx in (-0.76, 0.0, 0.76):                            # 세로 납선
            b.box((cx + dx, -0.10, 8.03), (0.07, 0.05, 2.75), M_METAL)
        for z in (7.58, 8.34, 9.10):                             # 가로 납선
            b.box((cx, -0.10, z), (2.25, 0.05, 0.07), M_METAL)
        lancet_figure(b, cx, kind)
        b.prism_y(lancet(0.9, 0.45, 2.0, cx=cx), -0.10, -0.04, M_TEAL)  # 지상 네온 아치
    # 장미창 주변은 독립 십자가 장식 없이 유리 자체가 시선을 장악한다.
    trace_wall(b, ((-13.5, 0.9), (-13.5, 3.4), (-11.8, 3.4)), M_TEAL)
    trace_wall(b, ((13.5, 0.9), (13.5, 3.4), (11.8, 3.4)), M_PURPLE)
    trace_wall(b, ((-10.5, 17.3), (-9.2, 17.3), (-9.2, 19.6)), M_PURPLE)
    trace_wall(b, ((10.5, 17.3), (9.2, 17.3), (9.2, 19.6)), M_TEAL)
    for px in (-16.3, 16.3):
        for dx in (0.0, 0.16):
            b.box((px + dx, -0.10, 11.0), (0.07, 0.14, 20.0), M_METAL)
        b.box((px + 0.08, -0.16, 9.0), (0.4, 0.2, 0.55), M_METAL)
        b.box((px + 0.08, -0.27, 9.0), (0.12, 0.06, 0.12), M_TEAL)
    return b.build("SM_Wall_Rose")

def asset_wall_gate():
    """S벽 입구 게이트 (아치 문 + 네온 림)."""
    b = Builder()
    wall_base(b, low_cornice=False)
    b.prism_y(lancet(2.7, 0.0, 3.2), -0.05, 0.01, M_PURPLE)    # 네온 림
    b.prism_y(lancet(2.4, 0.0, 3.15), -0.10, -0.04, M_DARK)    # 어두운 문
    for cx in (-1.55, 1.55):                                    # 금속 도어 프레임
        b.box((cx, -0.25, 2.9), (0.4, 0.6, 5.8), M_METAL)
    b.box((0, -0.35, 0.07), (3.6, 0.8, 0.14), M_METAL)          # 문턱
    add_neon_dashes(b, 6.8, count=5, mat=M_PURPLE)
    icon_panel(b, 9.6, "chalice")
    add_cross(b, -1.7, 15.0); add_cross(b, 1.7, 15.0)
    trace_wall(b, ((-2.1, 0.6), (-2.1, 4.4), (-1.75, 4.4), (-1.75, 7.6)), M_PURPLE)
    add_mech_details(b, 2.0)
    return b.build("SM_Wall_Gate")

def asset_corner_pillar():
    b = Builder()
    b.box((0, 0, WALL_H / 2 + 0.3), (1.8, 1.8, WALL_H + 0.6), M_STONE)
    b.box((0, 0, 0.4), (2.2, 2.2, 0.8), M_METAL)
    b.box((0, 0, WALL_H + 0.35), (2.2, 2.2, 0.5), M_METAL)
    for z in (5.7, 11.7, 17.7):
        b.box((0, 0, z), (2.0, 2.0, 0.3), M_METAL)
    for i, (dx, dy) in enumerate(((0.93, 0), (-0.93, 0), (0, 0.93), (0, -0.93))):
        mat = M_TEAL if i % 2 == 0 else M_PURPLE
        b.box((dx, dy, 8.0), (0.1 if dx else 0.14, 0.1 if dy else 0.14, 4.0), mat)
        b.box((dx, dy, 15.0), (0.1 if dx else 0.14, 0.1 if dy else 0.14, 3.0), mat)
    return b.build("SM_CornerPillar")

# ---------------------------------------------------------------- 갤러리 / 계단
def asset_gallery():
    b = Builder()
    b.box((0, -GAL_D / 2, -0.2), (SEG_W, GAL_D, 0.4), M_STONE)
    for sx in (-1.7, 1.7):
        b.box((sx, -0.55, -0.62), (0.6, 1.1, 0.5), M_METAL)
        p0, p1 = (-0.15, -1.9), (-2.55, -0.5)
        d = (p1[0] - p0[0], p1[1] - p0[1])
        ln = math.hypot(*d)
        nx, nz = -d[1] / ln * 0.09, d[0] / ln * 0.09
        quad = [(p0[0] + nx, p0[1] + nz), (p1[0] + nx, p1[1] + nz),
                (p1[0] - nx, p1[1] - nz), (p0[0] - nx, p0[1] - nz)]
        b.prism_x(quad, sx - 0.12, sx + 0.12, M_METAL)
    for x in (-2.3, -1.15, 0.0, 1.15, 2.3):
        b.box((x, -GAL_D + 0.12, 0.5), (0.14, 0.14, 1.0), M_METAL)
    b.box((0, -GAL_D + 0.12, 1.06), (SEG_W, 0.22, 0.12), M_METAL)
    b.box((0, -GAL_D + 0.14, 0.1), (SEG_W, 0.12, 0.2), M_METAL)
    b.box((0, -GAL_D + 0.06, -0.34), (SEG_W, 0.06, 0.1), M_TEAL)   # 하단 네온 스트립
    return b.build("SM_Gallery")

STAIR_N, STAIR_TREAD = 18, 0.42
STAIR_RISE = 6.0 / STAIR_N
STAIR_RUN = STAIR_N * STAIR_TREAD  # 7.56

def asset_stair():
    b = Builder()
    for i in range(STAIR_N):
        zt = (i + 1) * STAIR_RISE
        y = (i + 0.5) * STAIR_TREAD
        b.box((0, y, zt / 2), (2.6, STAIR_TREAD, zt), M_STONE)
        for sx in (-1.44, 1.44):
            b.box((sx, y, (zt + 0.7) / 2), (0.28, STAIR_TREAD, zt + 0.7), M_TRIM)
            b.box((sx, y, zt + 0.64), (0.3, STAIR_TREAD, 0.12), M_METAL)
        if i % 4 == 1:
            b.box((-1.6, y, zt + 0.3), (0.06, 0.3, 0.5), M_PURPLE)
            b.box((1.6, y, zt + 0.3), (0.06, 0.3, 0.5), M_PURPLE)
    return b.build("SM_Stair_Grand")

# ---------------------------------------------------------------- 받침대 / 발판
def asset_pedestal(h, name):
    b = Builder()
    b.box((0, 0, 0.25), (2.8, 2.8, 0.5), M_METAL)
    b.box((0, 0, (h + 0.2) / 2), (2.1, 2.1, h - 0.8), M_STONE)
    b.box((0, 0, h - 0.45), (2.15, 2.15, 0.1), M_GOLD)
    b.box((0, 0, h - 0.175), (2.6, 2.6, 0.35), M_STONE)
    for dx, dy in ((1.15, 1.15), (-1.15, 1.15), (1.15, -1.15), (-1.15, -1.15)):
        b.box((dx, dy, 0.35), (0.3, 0.3, 0.7), M_METAL)
    return b.build(name)

def asset_platform(s, name):
    b = Builder()
    b.box((0, 0, -0.19), (s, s, 0.38), M_STONE)
    b.box((0, 0, -0.5), (s * 0.6, s * 0.6, 0.32), M_METAL)
    b.box((0, 0, -0.68), (s * 0.42, s * 0.42, 0.1), M_GOLD)
    e, k = s / 2 - 0.1, s / 2 - 0.28
    for dx, dy in ((k, k), (-k, k), (k, -k), (-k, -k)):
        b.box((dx, dy, -0.42), (0.34, 0.34, 0.5), M_METAL)
    b.box((0,  e, 0.015), (s, 0.08, 0.05), M_TEAL)
    b.box((0, -e, 0.015), (s, 0.08, 0.05), M_TEAL)
    b.box(( e, 0, 0.015), (0.08, s - 0.36, 0.05), M_TEAL)
    b.box((-e, 0, 0.015), (0.08, s - 0.36, 0.05), M_TEAL)
    return b.build(name)

# ---------------------------------------------------------------- 제단 / 바닥 / 돔
def asset_dais():
    b = Builder()
    b.prism_z(octagon(3.4), 0.0, 0.7, M_STONE)
    b.prism_z(octagon(2.7), 0.7, 1.3, M_STONE)
    b.prism_z(octagon(2.35), 1.3, 1.33, M_GOLD)
    b.box((0, 0, 1.75), (1.3, 1.3, 0.9), M_TRIM)
    b.ring_z(3.5, 3.3, 0.25, 0.45, M_METAL)
    return b.build("SM_CentralDais")

def asset_floor():
    b = Builder()
    b.box((0, 0, -0.3), (46, 46, 0.6), M_STONE)
    b.ring_z(5.75, 5.5, 0.004, 0.03, M_TEAL)
    b.ring_z(6.15, 5.95, 0.004, 0.03, M_PURPLE)
    # 회로 트레이스 (로봇 라인) — 중앙 링에서 사방으로
    traces = (
        (((6.4, 0), (10, 0), (10, 4), (14.5, 4)), M_TEAL),
        (((0, 6.4), (0, 9.5), (-5, 9.5), (-5, 14)), M_PURPLE),
        (((-6.4, 0), (-10, 0), (-10, -4), (-14.5, -4)), M_TEAL),
        (((0, -6.4), (0, -9.5), (5, -9.5), (5, -14)), M_PURPLE),
        (((4.6, 4.6), (8.5, 4.6), (8.5, 8.5), (12, 8.5)), M_PURPLE),
        (((-4.6, -4.6), (-8.5, -4.6), (-8.5, -8.5), (-12, -8.5)), M_PURPLE),
        (((4.6, -4.6), (4.6, -7.5), (9, -7.5), (9, -12)), M_TEAL),
        (((-4.6, 4.6), (-4.6, 7.5), (-9, 7.5), (-9, 12)), M_TEAL),
    )
    for pts, mat in traces:
        trace_floor(b, pts, mat)
    # 사분면 룬 + 보라 크로스
    for x, y in ((12, 0), (-12, 0), (0, 12), (0, -12)):
        b.prism_z([(x + px, y + py) for px, py in octagon(1.15)], 0.005, 0.03, M_GOLD)
        b.box((x, y, 0.045), (0.24, 1.4, 0.025), M_PURPLE)
        b.box((x, y + 0.28, 0.045), (1.4, 0.24, 0.025), M_PURPLE)
    for c, s_, axis in ((17, 34.2, 'x'), (-17, 34.2, 'x'), (17, 34.2, 'y'), (-17, 34.2, 'y')):
        if axis == 'x':
            b.box((0, c, 0.015), (s_, 0.14, 0.03), M_TEAL)
        else:
            b.box((c, 0, 0.015), (0.14, s_, 0.03), M_TEAL)
    return b.build("SM_Floor")

def asset_dome():
    b = Builder()
    V = 0.0
    b.ring_z(30.0, 16.0, 0.0, 1.4, M_STONE, offset=V)
    b.ring_z(16.2, 15.8, -0.12, 0.02, M_TEAL, offset=V)
    rings = ((0.0, 1.6, 16.0, 13.0), (1.4, 2.8, 13.3, 10.6),
             (2.6, 3.9, 10.9, 8.4), (3.7, 4.9, 8.7, 6.4),
             (4.7, 5.7, 6.7, 4.6), (5.5, 6.4, 4.9, 3.1))
    for k, (z0, z1, ro, ri) in enumerate(rings):
        b.ring_z(ro, ri, z0, z1, M_STONE, offset=V)
        seam = M_PURPLE if k % 2 == 0 else M_TEAL
        b.ring_z(ri + 0.3, ri + 0.02, z0 + 0.02, z0 + 0.12, seam, offset=V)
        b.ring_z(ro + 0.18, ro - 0.02, z0 - 0.1, z0 + 0.05, M_METAL, offset=V)
    b.prism_z(octagon(3.4, V), 6.2, 7.0, M_STONE)
    b.prism_z(octagon(2.3, V), 6.13, 6.22, M_GOLD)
    b.box((0, 0, 4.55), (1.1, 1.1, 3.2), M_METAL)
    b.prism_z(octagon(1.5, V), 1.6, 3.2, M_METAL)
    b.prism_z(octagon(1.62, V), 2.2, 2.5, M_PURPLE)
    b.box((0, 0, 1.25), (0.7, 0.7, 0.7), M_METAL)
    b.box((0, 0, 0.75), (0.4, 0.4, 0.4), M_GOLD)
    return b.build("SM_Dome")

def asset_hanglight():
    b = Builder()
    b.box((0, 0, 1.75), (0.16, 0.16, 3.4), M_METAL)
    b.box((0, 0, -0.15), (2.2, 2.2, 0.3), M_METAL)
    b.box((0, 0, -0.34), (1.5, 1.5, 0.08), M_GOLD)
    b.box((0, 0, 0.04), (1.0, 1.0, 0.04), M_TEAL)
    return b.build("SM_HangLight")

# ---------------------------------------------------------------- 에셋 빌드
ASSETS = {}
ASSETS["Wall_Window"] = asset_wall_window()
ASSETS["Wall_PlainA"] = asset_wall_plain("star", "SM_Wall_PlainA")
ASSETS["Wall_PlainB"] = asset_wall_plain("tablets", "SM_Wall_PlainB")
ASSETS["Wall_Rose"] = asset_wall_rose()
ASSETS["Wall_Gate"] = asset_wall_gate()
ASSETS["CornerPillar"] = asset_corner_pillar()
ASSETS["Gallery"] = asset_gallery()
ASSETS["Stair_Grand"] = asset_stair()
ASSETS["Pedestal_S"] = asset_pedestal(1.2, "SM_Pedestal_S")
ASSETS["Pedestal_M"] = asset_pedestal(2.0, "SM_Pedestal_M")
ASSETS["Pedestal_L"] = asset_pedestal(3.0, "SM_Pedestal_L")
ASSETS["Platform_S"] = asset_platform(2.2, "SM_Platform_S")
ASSETS["Platform_M"] = asset_platform(3.2, "SM_Platform_M")
ASSETS["Platform_L"] = asset_platform(4.4, "SM_Platform_L")
ASSETS["CentralDais"] = asset_dais()
ASSETS["Floor"] = asset_floor()
ASSETS["Dome"] = asset_dome()
ASSETS["HangLight"] = asset_hanglight()

# ---------------------------------------------------------------- FBX export
def select_only(objs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]

def export_fbx(objs, path):
    objs = [o for o in objs if o and bpy.data.objects.get(o.name) is o]
    if not objs:
        raise RuntimeError(f"FBX export has no valid objects: {path}")
    select_only(objs)
    # Blender 5.x의 FBX 애드온은 Text Editor/일부 headless 컨텍스트에서
    # context.selected_objects가 없으면 실패한다. 내보낼 선택 상태를 명시적으로
    # override하여 실행 위치와 무관하게 동일한 컨텍스트를 제공한다.
    override = {
        "active_object": objs[0],
        "object": objs[0],
        "selected_objects": objs,
        "selected_editable_objects": objs,
    }
    with bpy.context.temp_override(**override):
        try:
            bpy.ops.export_scene.fbx(
                filepath=path, use_selection=True,
                axis_forward='-Z', axis_up='Y',
                apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True)
        except TypeError:
            bpy.ops.export_scene.fbx(filepath=path, use_selection=True)

for key, obj in ASSETS.items():
    export_fbx([obj], f"{FBX_DIR}/{key}.fbx")
    print(f"[FBX] {key}.fbx")

# ---------------------------------------------------------------- 아레나 조립
def place(asset_key, loc, rot_deg=0.0, col=arena_col):
    o = ASSETS[asset_key].copy()
    o.location = loc
    o.rotation_euler = (0, 0, math.radians(rot_deg))
    col.objects.link(o)
    return o

place("Floor", (0, 0, 0))
place("CentralDais", (0, 0, 0))
place("Dome", (0, 0, WALL_H), col=dome_col)
for sx in (10, -10):
    for sy in (10, -10):
        place("HangLight", (sx, sy, 18.6), col=dome_col)

# 벽 배치: N = 스테인드 글라스(채광부), S = 입구 게이트, E/W = 창/성화 교차
slots = [-17.5 + 5 * i for i in range(8)]
S_KINDS = ("Wall_Window", "Wall_PlainA", "Wall_PlainB", "Wall_Gate",
           "Wall_Gate", "Wall_PlainB", "Wall_PlainA", "Wall_Window")
EW_KINDS = ("Wall_Window", "Wall_PlainA", "Wall_Window", "Wall_PlainB",
            "Wall_Window", "Wall_PlainA", "Wall_Window", "Wall_PlainB")
place("Wall_Rose", (0, INNER, 0), 0)            # N (입구 반대편, 장미창 통짜 벽)
for i, x in enumerate(slots):
    place(S_KINDS[i], (x, -INNER, 0), 180)      # S (입구)
for i, y in enumerate(slots):
    place(EW_KINDS[i], (INNER, y, 0), -90)      # E
    place(EW_KINDS[i], (-INNER, y, 0), 90)      # W
for cx in (INNER, -INNER):
    for cy in (INNER, -INNER):
        place("CornerPillar", (cx, cy, 0))

# 갤러리 (인덱스는 전역 좌표 기준)
GALLERY = {
    (T2, 'N'): {0, 1, 2, 3, 4, 5, 7}, (T2, 'S'): {0, 2, 3, 4, 5, 6, 7},
    (T2, 'E'): {2, 3, 4, 5, 6},        (T2, 'W'): {1, 2, 3, 4, 5},
    (T3, 'N'): set(range(8)),          (T3, 'S'): set(range(8)),
    (T3, 'E'): {1, 2, 3, 4, 6},        (T3, 'W'): {1, 3, 4, 5, 6},
}
for (z, side), idx in GALLERY.items():
    for i in idx:
        c = slots[i]
        if side == 'N':
            place("Gallery", (c, INNER, z), 0)
        elif side == 'S':
            place("Gallery", (c, -INNER, z), 180)
        elif side == 'E':
            place("Gallery", (INNER, c, z), -90)
        else:
            place("Gallery", (-INNER, c, z), 90)

# 대계단
S0 = 10.0 + STAIR_RUN  # 17.56
place("Stair_Grand", (-S0, -18.5, 0), -90)
place("Stair_Grand", (18.5, -S0, 0), 0)
place("Stair_Grand", (S0, 18.5, 0), 90)
place("Stair_Grand", (-18.5, S0, 0), 180)
place("Stair_Grand", (18.5, 10.0 - STAIR_RUN, T2), 0)
place("Stair_Grand", (-18.5, STAIR_RUN - 10.0, T2), 180)

# 받침대
for key, x, y in (("Pedestal_M", 6, -2), ("Pedestal_M", -6, 2),
                  ("Pedestal_S", 2, 6), ("Pedestal_S", -2, -6),
                  ("Pedestal_S", 10, 2), ("Pedestal_S", -10, -2),
                  ("Pedestal_L", 9, -9), ("Pedestal_L", -9, 9)):
    place(key, (x, y, 0))

# 공중 발판 23개
PLATFORMS = (
    ("Platform_M", 8, 4, 2.0),      ("Platform_M", 11, 7.5, 3.8),
    ("Platform_S", 14, 11, 5.2),
    ("Platform_M", -8, -4, 2.0),    ("Platform_M", -11, -7.5, 3.8),
    ("Platform_S", -14, -11, 5.2),
    ("Platform_M", 12.5, -9, 4.6),  ("Platform_M", -12.5, 9, 4.6),
    ("Platform_S", 4, -11, 2.3),    ("Platform_S", -4, 11, 2.3),
    ("Platform_M", 13, -6, 7.8),    ("Platform_S", 13, -11.5, 9.6),
    ("Platform_S", 10, -14.5, 11.2),
    ("Platform_M", -13, 6, 7.8),    ("Platform_S", -13, 11.5, 9.6),
    ("Platform_S", -10, 14.5, 11.2),
    ("Platform_L", 0, 12, 8.2),     ("Platform_L", 0, -12, 8.2),
    ("Platform_M", 12, 0, 8.6),     ("Platform_M", -12, 0, 8.6),
    ("Platform_M", 0, 5.5, 9.4),    ("Platform_M", 0, -5.5, 9.4),
    ("Platform_L", 0, 0, 10.8),
)
for key, x, y, z in PLATFORMS:
    place(key, (x, y, z))

# ---------------------------------------------------------------- 전체/병합 FBX
all_arena = list(arena_col.objects) + list(dome_col.objects)
export_fbx(all_arena, f"{FBX_DIR}/CathedralArena_Full.fbx")
print(f"[FBX] CathedralArena_Full.fbx ({len(all_arena)} objects)")

# 렌더 최적화용 병합본: 전체를 1오브젝트(재질별 서브메시)로 — 드로우콜 대폭 감소
select_only(all_arena)
bpy.ops.object.duplicate()
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.object.join()
merged = bpy.context.view_layer.objects.active
merged.name = "SM_CathedralArena_Merged"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
export_fbx([merged], f"{FBX_DIR}/CathedralArena_Merged.fbx")
print(f"[FBX] CathedralArena_Merged.fbx (1 object, {len(merged.data.materials)} submeshes)")
bpy.data.objects.remove(merged, do_unlink=True)

# ---------------------------------------------------------------- 라이브러리 정렬 & 라이트
for i, obj in enumerate(ASSETS.values()):
    obj.location = (i * 14 - 120, -80, 0)

def add_light(name, kind, loc, energy, color=(1, 1, 1), size=None, rot=None,
              area=None, shadows=True):
    data = bpy.data.lights.new(name, kind)
    data.energy = energy
    data.color = color
    data.use_shadow = shadows
    if size and kind == 'POINT':
        data.shadow_soft_size = size
    if area and kind == 'AREA':
        data.shape = 'RECTANGLE'
        data.size, data.size_y = area
    o = bpy.data.objects.new(name, data)
    o.location = loc
    if rot:
        o.rotation_euler = rot
    scene.collection.objects.link(o)
    return o

# 채광: N벽 스테인드 글라스 뒤에서 실내로 쏟아지는 빛
add_light("Glass_Light", 'AREA', (0, 19.15, 14.5), 4200, (0.70, 0.76, 1.00),
          rot=(math.radians(-110), 0, 0), area=(32, 16))
add_light("Glass_BlueCast", 'AREA', (-3.6, 18.9, 15.4), 1200, (0.08, 0.20, 1.00),
          rot=(math.radians(-108), 0, math.radians(-7)), area=(5.5, 10), shadows=False)
add_light("Glass_RedCast", 'AREA', (3.6, 18.9, 15.4), 1050, (1.00, 0.035, 0.045),
          rot=(math.radians(-108), 0, math.radians(7)), area=(5.5, 10), shadows=False)
add_light("Lancet_GoldCast", 'AREA', (0, 18.85, 8.2), 650, (1.00, 0.38, 0.06),
          rot=(math.radians(-104), 0, 0), area=(14, 4), shadows=False)
add_light("Rose_Glow", 'POINT', (0, 17.4, 15.45), 650, (0.65, 0.18, 0.55), size=4)
# 나머지는 어둡게, 포인트만 은은히
add_light("Dome_Oculus", 'POINT', (0, 0, 26), 1200, (1.0, 0.8, 0.5), size=2)
add_light("Accent_P1", 'POINT', (14, -12, 4), 500, (0.65, 0.35, 1.0), size=2)
add_light("Accent_P2", 'POINT', (-14, -12, 4), 500, (0.65, 0.35, 1.0), size=2)
add_light("Accent_T1", 'POINT', (0, -6, 7), 400, (0.25, 0.9, 0.85), size=2)
add_light("Fill_Low", 'POINT', (0, 0, 3), 250, (0.6, 0.7, 1.0), size=2)

world = bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
for n in world.node_tree.nodes:
    if n.type == 'BACKGROUND':
        n.inputs[0].default_value = (0.02, 0.02, 0.045, 1)
        n.inputs[1].default_value = 0.3

# ---------------------------------------------------------------- 카메라
from mathutils import Vector

def add_cam(name, loc, target=None, ortho=None, fov=70):
    cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
    cam.location = loc
    if ortho:
        cam.data.type = 'ORTHO'
        cam.data.ortho_scale = ortho
        cam.rotation_euler = (0, 0, 0)
    else:
        cam.data.angle = math.radians(fov)
        d = Vector(target) - Vector(loc)
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.data.clip_end = 400
    scene.collection.objects.link(cam)
    return cam

cam_corner = add_cam("Cam_Corner", (17, -17, 14), target=(-3, 3, 7.5), fov=82)
cam_eye = add_cam("Cam_Eye", (2, -15.5, 2.1), target=(0, 3, 7), fov=78)
cam_glass = add_cam("Cam_Glass", (0, -13, 4.5), target=(0, 20, 13), fov=75)
cam_dome = add_cam("Cam_DomeUp", (10, -10, 3), target=(0, 0, 24), fov=80)
cam_top = add_cam("Cam_Top", (0, 0, 90), ortho=56)

# ---------------------------------------------------------------- 렌더
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
lib_col.hide_render = True

def shoot(cam, name):
    scene.camera = cam
    scene.render.filepath = f"{RENDER_DIR}/{name}.png"
    bpy.ops.render.render(write_still=True)
    print(f"[Render] {name}.png")

# 레이아웃 확인용 Workbench (탑다운: 돔 숨김)
scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading
sh.light = 'STUDIO'
sh.color_type = 'MATERIAL'
sh.show_cavity = True
try:
    sh.cavity_type = 'BOTH'
except Exception:
    pass
sh.show_shadows = True
dome_col.hide_render = True
shoot(cam_top, "arena_top")
dome_col.hide_render = False

# 무드 확인용 EEVEE
try:
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            scene.render.engine = eng
            break
        except Exception:
            continue
    if scene.render.engine != 'BLENDER_WORKBENCH':
        try:
            scene.eevee.taa_render_samples = 32
        except Exception:
            pass
        shoot(cam_corner, "arena_glow_corner")
        shoot(cam_glass, "arena_glow_glass")
        shoot(cam_eye, "arena_glow_eye")
        shoot(cam_dome, "arena_glow_dome")
except Exception as e:
    print(f"[EEVEE skip] {e}")

# ---------------------------------------------------------------- 저장
bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
print(f"[Done] blend={BLEND_PATH}")
print(f"[Done] fbx dir={FBX_DIR}")
