# -*- coding: utf-8 -*-
"""
RangedBot 생성 스크립트 (Blender 5.x, headless) — 원거리 유닛
- 레퍼런스: 저폴리 다크그레이 로봇 + 빨간 단안 센서 + 라이플 + 탈부착 부스터 백팩
- 구조:
  * Armature(휴머노이드 본) + 파트별 100% 웨이트 스키닝(기계라 변형 불필요)
  * 메시 3개로 분리: RangedBot_Body / RangedBot_Booster / RangedBot_Rifle
    - Booster는 전용 본(booster, chest 자식)에만 스키닝 → Unity에서 그 메시만
      비활성/재부모화하면 탈부착 완성
    - Rifle도 weapon.R 본에 스키닝 → 무기 교체 가능
- 로봇은 -Y를 바라봄(기본 FBX 설정으로 Unity에서 +Z 정면)

실행:
  blender.exe --background --python ranged_bot_gen.py
"""
import bpy
import bmesh
import math

ROOT = r"C:/Users/User/kaist_madcamp/week3"
FBX_DIR = ROOT + "/PrecogPrototype/Assets/_Project/Art/RangedBot"
BLEND_PATH = ROOT + "/ArtSource/RangedBot.blend"

import os
os.makedirs(FBX_DIR, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'

col = bpy.data.collections.new("RangedBot")
scene.collection.children.link(col)

# ---------------------------------------------------------------- 재질
def make_mat(name, color, rough=0.5, emit=None, strength=0.0, metallic=0.0, display=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
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

M_ARMOR = make_mat("M_Bot_Armor", (0.13, 0.135, 0.145), rough=0.45, metallic=0.55,
                   display=(0.30, 0.31, 0.33))
M_JOINT = make_mat("M_Bot_Joint", (0.055, 0.058, 0.065), rough=0.65, metallic=0.35,
                   display=(0.14, 0.15, 0.16))
M_GUN   = make_mat("M_Bot_Gun",   (0.045, 0.047, 0.052), rough=0.4, metallic=0.7,
                   display=(0.11, 0.12, 0.13))
M_RED   = make_mat("M_Bot_RedGlow", (0.25, 0.01, 0.01), emit=(1.0, 0.08, 0.04),
                   strength=6.0)

# ---------------------------------------------------------------- 메시 헬퍼
PARTS = {"Body": [], "Booster": [], "Rifle": []}  # (obj, bone)

def _finish(bm, name, loc, mat, group, bone, rot=None):
    mesh = bpy.data.meshes.new(name)
    if rot:
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0),
                         matrix=rot.to_matrix())
    bmesh.ops.translate(bm, verts=bm.verts, vec=loc)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    obj.data.materials.append(mat)
    col.objects.link(obj)
    PARTS[group].append((obj, bone))
    return obj

def add_box(name, size, loc, mat, group, bone, taper_top=1.0, taper_bottom=1.0):
    """size=(w,d,h). taper_top/bottom: 위/아래 단면의 xy 배율(사다리꼴 장갑판)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]
        v.co.y *= size[1]
        v.co.z *= size[2]
        t = taper_top if v.co.z > 0 else taper_bottom
        v.co.x *= t
        v.co.y *= t
    return _finish(bm, name, loc, mat, group, bone)

def add_prism(name, radius, depth, loc, mat, group, bone, sides=8,
              radius_top=None, axis='Z'):
    """8각 프리즘(저폴리 실린더). axis='Y'면 -Y(정면) 방향으로 눕힘."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=sides,
                          radius1=radius, radius2=radius_top if radius_top is not None else radius,
                          depth=depth)
    rot = None
    if axis == 'Y':
        import mathutils
        rot = mathutils.Euler((math.radians(90), 0, 0))
    return _finish(bm, name, loc, mat, group, bone, rot=rot)

# ---------------------------------------------------------------- Armature
arm_data = bpy.data.armatures.new("RangedBot_Skeleton")
arm_obj = bpy.data.objects.new("RangedBot", arm_data)
col.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode='EDIT')

def bone(name, head, tail, parent=None, connect=False):
    b = arm_data.edit_bones.new(name)
    b.head = head
    b.tail = tail
    if parent:
        b.parent = arm_data.edit_bones[parent]
        b.use_connect = connect
    return b

bone("root",   (0, 0, 0),      (0, 0, 0.2))
bone("hips",   (0, 0, 0.98),   (0, 0, 1.08), "root")
bone("spine",  (0, 0, 1.08),   (0, 0, 1.20), "hips", True)
bone("chest",  (0, 0, 1.20),   (0, 0, 1.46), "spine", True)
bone("neck",   (0, 0, 1.46),   (0, 0, 1.53), "chest", True)
bone("head",   (0, 0, 1.53),   (0, 0, 1.82), "neck", True)
for s, side in ((1, "L"), (-1, "R")):
    bone(f"shoulder.{side}", (0.10 * s, 0, 1.40), (0.29 * s, 0, 1.42), "chest")
    bone(f"upper_arm.{side}", (0.30 * s, 0, 1.40), (0.31 * s, 0, 1.10), f"shoulder.{side}")
    bone(f"forearm.{side}",   (0.31 * s, 0, 1.10), (0.31 * s, 0, 0.84), f"upper_arm.{side}", True)
    bone(f"hand.{side}",      (0.31 * s, 0, 0.84), (0.31 * s, 0, 0.72), f"forearm.{side}", True)
    bone(f"thigh.{side}", (0.14 * s, 0, 0.96), (0.145 * s, 0, 0.62), "hips")
    bone(f"shin.{side}",  (0.145 * s, 0, 0.62), (0.15 * s, 0, 0.11), f"thigh.{side}", True)
    bone(f"foot.{side}",  (0.15 * s, 0, 0.11), (0.15 * s, -0.20, 0.045), f"shin.{side}", True)
bone("booster",  (0, 0.21, 1.22), (0, 0.21, 1.55), "chest")
bone("weapon.R", (-0.31, -0.04, 0.80), (-0.31, -0.34, 0.80), "hand.R")

bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- 몸통 (Body)
add_box("Pelvis", (0.30, 0.22, 0.16), (0, 0, 1.00), M_ARMOR, "Body", "hips",
        taper_bottom=0.85)
add_box("PelvisCore", (0.20, 0.16, 0.10), (0, 0, 0.90), M_JOINT, "Body", "hips")
add_box("Abdomen", (0.22, 0.17, 0.13), (0, 0, 1.13), M_JOINT, "Body", "spine")
add_box("Chest", (0.46, 0.28, 0.30), (0, 0, 1.33), M_ARMOR, "Body", "chest",
        taper_bottom=0.72)
add_box("ChestPlate", (0.30, 0.06, 0.16), (0, -0.14, 1.34), M_ARMOR, "Body", "chest",
        taper_bottom=0.85)
add_box("ChestStripe", (0.12, 0.02, 0.025), (0, -0.165, 1.30), M_RED, "Body", "chest")

# 머리: 8각 헬멧 + 단안 렌즈 + 안테나
add_prism("Helmet", 0.145, 0.20, (0, 0, 1.66), M_ARMOR, "Body", "head",
          radius_top=0.11)
add_box("HelmetJaw", (0.16, 0.16, 0.06), (0, -0.01, 1.545), M_JOINT, "Body", "head")
add_prism("EyeSocket", 0.062, 0.03, (0, -0.135, 1.66), M_JOINT, "Body", "head",
          axis='Y')
add_prism("Eye", 0.042, 0.025, (0, -0.15, 1.66), M_RED, "Body", "head", axis='Y')
add_box("HeadAntenna", (0.012, 0.012, 0.22), (0.09, 0.05, 1.85), M_JOINT, "Body",
        "head")

for s, side in ((1, "L"), (-1, "R")):
    # 어깨 장갑 + 팔
    add_box(f"ShoulderPad.{side}", (0.17, 0.19, 0.15), (0.315 * s, 0, 1.43),
            M_ARMOR, "Body", f"shoulder.{side}", taper_bottom=0.8)
    add_box(f"UpperArm.{side}", (0.10, 0.11, 0.26), (0.305 * s, 0, 1.25),
            M_JOINT, "Body", f"upper_arm.{side}")
    add_box(f"Elbow.{side}", (0.09, 0.10, 0.08), (0.31 * s, 0, 1.10),
            M_ARMOR, "Body", f"upper_arm.{side}")
    add_box(f"Forearm.{side}", (0.095, 0.105, 0.24), (0.31 * s, 0, 0.97),
            M_ARMOR, "Body", f"forearm.{side}", taper_bottom=0.92)
    add_box(f"Hand.{side}", (0.085, 0.10, 0.11), (0.31 * s, -0.01, 0.79),
            M_JOINT, "Body", f"hand.{side}")
    # 다리
    add_box(f"Thigh.{side}", (0.145, 0.17, 0.30), (0.14 * s, 0, 0.80),
            M_ARMOR, "Body", f"thigh.{side}", taper_bottom=0.88)
    add_box(f"Knee.{side}", (0.11, 0.13, 0.09), (0.145 * s, -0.01, 0.62),
            M_ARMOR, "Body", f"thigh.{side}")
    add_box(f"Shin.{side}", (0.12, 0.14, 0.34), (0.148 * s, 0, 0.42),
            M_JOINT, "Body", f"shin.{side}", taper_top=0.9)
    add_box(f"ShinPlate.{side}", (0.10, 0.05, 0.22), (0.148 * s, -0.085, 0.44),
            M_ARMOR, "Body", f"shin.{side}")
    add_box(f"ShinLight.{side}", (0.02, 0.015, 0.07), (0.148 * s, -0.115, 0.44),
            M_RED, "Body", f"shin.{side}")
    add_box(f"Ankle.{side}", (0.09, 0.10, 0.10), (0.15 * s, 0, 0.16),
            M_JOINT, "Body", f"shin.{side}")
    add_box(f"Foot.{side}", (0.13, 0.30, 0.09), (0.15 * s, -0.05, 0.055),
            M_ARMOR, "Body", f"foot.{side}", taper_top=0.85)

# ---------------------------------------------------------------- 부스터 (탈부착)
add_box("BoosterCore", (0.34, 0.13, 0.32), (0, 0.20, 1.34), M_JOINT, "Booster",
        "booster")
add_box("BoosterSpine", (0.10, 0.06, 0.38), (0, 0.28, 1.36), M_ARMOR, "Booster",
        "booster")
for s, side in ((1, "L"), (-1, "R")):
    add_prism(f"BoosterTank.{side}", 0.088, 0.40, (0.115 * s, 0.25, 1.42),
              M_ARMOR, "Booster", "booster", radius_top=0.055)
    add_prism(f"BoosterNozzle.{side}", 0.055, 0.09, (0.115 * s, 0.25, 1.175),
              M_JOINT, "Booster", "booster", radius_top=0.075)
    add_prism(f"BoosterFlame.{side}", 0.045, 0.03, (0.115 * s, 0.25, 1.14),
              M_RED, "Booster", "booster")
    add_box(f"BoosterAntenna.{side}", (0.012, 0.012, 0.26), (0.10 * s, 0.30, 1.72),
            M_JOINT, "Booster", "booster")
add_box("BoosterLatch", (0.16, 0.03, 0.10), (0, 0.145, 1.36), M_GUN, "Booster",
        "booster")

# ---------------------------------------------------------------- 라이플
GX, GZ = -0.31, 0.80   # 오른손 그립 기준
add_box("RifleBody", (0.055, 0.42, 0.10), (GX, -0.16, GZ + 0.05), M_GUN, "Rifle",
        "weapon.R")
add_prism("RifleBarrel", 0.024, 0.42, (GX, -0.55, GZ + 0.06), M_GUN, "Rifle",
          "weapon.R", axis='Y')
add_prism("RifleMuzzle", 0.032, 0.07, (GX, -0.74, GZ + 0.06), M_JOINT, "Rifle",
          "weapon.R", axis='Y')
add_prism("RifleScope", 0.03, 0.16, (GX, -0.20, GZ + 0.135), M_JOINT, "Rifle",
          "weapon.R", axis='Y')
add_prism("RifleScopeLens", 0.02, 0.012, (GX, -0.115, GZ + 0.135), M_RED, "Rifle",
          "weapon.R", axis='Y')
add_box("RifleStock", (0.045, 0.16, 0.085), (GX, 0.10, GZ + 0.03), M_GUN, "Rifle",
        "weapon.R", taper_bottom=0.8)
add_box("RifleMag", (0.04, 0.09, 0.12), (GX, -0.10, GZ - 0.055), M_JOINT, "Rifle",
        "weapon.R", taper_bottom=0.75)
add_box("RifleGrip", (0.04, 0.06, 0.10), (GX, 0.02, GZ - 0.04), M_JOINT, "Rifle",
        "weapon.R")

# ---------------------------------------------------------------- 스키닝 + 병합
def merge_group(group_name):
    objs = PARTS[group_name]
    for obj, bone_name in objs:
        vg = obj.vertex_groups.new(name=bone_name)
        vg.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    bpy.ops.object.select_all(action='DESELECT')
    for obj, _ in objs:
        obj.select_set(True)
    merged = objs[0][0]
    bpy.context.view_layer.objects.active = merged
    bpy.ops.object.join()
    merged.name = f"RangedBot_{group_name}"
    merged.data.name = f"RangedBot_{group_name}"
    merged.parent = arm_obj
    mod = merged.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm_obj
    # 미세 베벨(모서리 하이라이트) — FBX 익스포터가 자동 적용
    bev = merged.modifiers.new("Bevel", 'BEVEL')
    bev.width = 0.006
    bev.segments = 1
    bev.limit_method = 'ANGLE'
    bev.angle_limit = math.radians(40)
    return merged

merge_group("Body")
merge_group("Booster")
merge_group("Rifle")

# ---------------------------------------------------------------- 저장 + 내보내기
bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(
    filepath=FBX_DIR + "/RangedBot.fbx",
    use_selection=True,
    object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False,
    apply_scale_options='FBX_SCALE_ALL',
    bake_anim=False,
)
print("[Done] RangedBot.fbx ->", FBX_DIR)
