using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 블록아웃 보조: 맵 배치용 부속(바닥·벽·블록·경사로·기둥·엄폐)을 현재 씬에 생성한다.
    /// 콜라이더 포함(프리미티브 기본) + 격자 머티리얼 자동 적용. Undo 지원, 생성 즉시 선택.
    /// 게임 스케일 1u = 1m (플레이어 키 1.15). 경사로는 회전 판(매끄러움, <45° = NavMesh 걸림).
    /// </summary>
    public static class MapPartsTool
    {
        const string GridMatPath = "Assets/_Project/Editor/GridMaterial.mat";

        [MenuItem("Tools/맵 부속/바닥 타일 (10x0.5x10)")]
        static void Floor() => Box("Floor", new Vector3(10f, 0.5f, 10f));

        [MenuItem("Tools/맵 부속/벽 (8x4x0.5)")]
        static void Wall() => Box("Wall", new Vector3(8f, 4f, 0.5f));

        [MenuItem("Tools/맵 부속/블록·발판 (3x3x3)")]
        static void Block() => Box("Block", new Vector3(3f, 3f, 3f));

        [MenuItem("Tools/맵 부속/얇은 발판 (6x0.4x6)")]
        static void Platform() => Box("Platform", new Vector3(6f, 0.4f, 6f));

        [MenuItem("Tools/맵 부속/기둥 (1.5x5x1.5)")]
        static void Pillar() => Box("Pillar", new Vector3(1.5f, 5f, 1.5f));

        [MenuItem("Tools/맵 부속/엄폐 블록 (3x1.2x1.5)")]
        static void Cover() => Box("Cover", new Vector3(3f, 1.2f, 1.5f));

        [MenuItem("Tools/맵 부속/경사로 22° (오름2 런5)")]
        static void Ramp22() => Ramp("Ramp_22", rise: 2f, run: 5f, width: 4f, thickness: 0.5f);

        [MenuItem("Tools/맵 부속/경사로 27° (오름3 런6)")]
        static void Ramp27() => Ramp("Ramp_27", rise: 3f, run: 6f, width: 4f, thickness: 0.5f);

        // ── 생성 헬퍼 ──
        static void Box(string name, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = SpawnPos();
            go.transform.localScale = size;
            Finish(go);
        }

        /// <summary>경사로: 판을 X축 기준 -θ 회전 → +Z 끝이 rise만큼 올라간다. 윗면이 걷는 면.</summary>
        static void Ramp(string name, float rise, float run, float width, float thickness)
        {
            float length = Mathf.Sqrt(run * run + rise * rise);   // 경사면 길이
            float theta  = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            // 낮은 끝을 원점 근처에, 높은 끝을 +Z로. 중심은 (0, rise/2, run/2).
            go.transform.position = SpawnPos() + new Vector3(0f, rise * 0.5f, run * 0.5f);
            go.transform.rotation = Quaternion.Euler(-theta, 0f, 0f);
            go.transform.localScale = new Vector3(width, thickness, length);
            Finish(go);
        }

        static Vector3 SpawnPos()
        {
            // 씬 뷰 화면 중앙(피벗)에 생성 — 안 보이는 원점에 안 묻히게.
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { Vector3 p = sv.pivot; p.y = 0f; return p; }
            return Vector3.zero;
        }

        static void Finish(GameObject go)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GridMatPath);
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Undo.RegisterCreatedObjectUndo(go, "맵 부속 생성");
            Selection.activeGameObject = go;
        }
    }
}
