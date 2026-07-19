using System.Collections.Generic;
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

        [MenuItem("Tools/맵 부속/컨테이너 앞뒤개방 (2.6x2.8x6)")]
        static void Container()
        {
            // 속 빈 통: 바닥·천장·좌우벽 4장을 '하나의 메시'로 결합 → 단일 부속(자식 없음).
            // 앞뒤(±Z) 개방 → 관통. 배치 후 회전하면 방향 자유.
            const float length = 6f, width = 2.6f, height = 2.8f, thk = 0.2f;
            float wallH = height - thk;                 // 천장 아래까지
            float wx    = width * 0.5f - thk * 0.5f;    // 좌우 벽 안쪽면이 폭 경계에 맞음

            var boxes = new (Vector3 pos, Vector3 size)[]
            {
                (new Vector3(0f, -thk * 0.5f, 0f),         new Vector3(width, thk, length)),  // 바닥(윗면 flush)
                (new Vector3(0f, height - thk * 0.5f, 0f), new Vector3(width, thk, length)),  // 천장(위=발판)
                (new Vector3(-wx, wallH * 0.5f, 0f),       new Vector3(thk, wallH, length)),  // 좌벽
                (new Vector3( wx, wallH * 0.5f, 0f),       new Vector3(thk, wallH, length)),  // 우벽
            };

            // 박스별로 단위큐브 정점을 복사·배치하고, 각 정점의 원래 단위좌표(±0.5)를 UV2에 저장.
            // → 셰이더가 _EDGE_FROM_UV로 판마다 모서리 외곽선을 낸다(결합 메시라도 외곽선 정상).
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh unit = temp.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] uVerts = unit.vertices;   // ±0.5 단위좌표
            Vector3[] uNorms = unit.normals;
            int[]     uTris  = unit.triangles;
            Object.DestroyImmediate(temp);

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var edge  = new List<Vector3>();    // UV2: 박스별 단위좌표(외곽선용)
            var tris  = new List<int>();
            foreach (var b in boxes)
            {
                var M = Matrix4x4.TRS(b.pos, Quaternion.identity, b.size);
                int baseIdx = verts.Count;
                for (int k = 0; k < uVerts.Length; k++)
                {
                    verts.Add(M.MultiplyPoint3x4(uVerts[k]));   // 스케일·이동 적용된 실제 위치
                    norms.Add(uNorms[k]);                       // 축정렬 박스라 방향 그대로
                    edge.Add(uVerts[k]);                        // 원래 ±0.5
                }
                for (int k = 0; k < uTris.Length; k++) tris.Add(baseIdx + uTris[k]);
            }

            var mesh = new Mesh { name = "ContainerMesh" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(2, edge);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Container");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GridMatEdgeUV();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;   // 정적 non-convex
            go.transform.position = SpawnPos();

            Undo.RegisterCreatedObjectUndo(go, "맵 부속 생성");
            Selection.activeGameObject = go;
        }

        // ── 생성 헬퍼 ──
        /// <summary>
        /// 그리드 머티리얼 인스턴스 + 외곽선을 UV2로 계산(_EDGE_FROM_UV). 결합 메시는 positionOS가
        /// ±0.5 밖이라, 박스별 단위좌표를 넣은 UV2로 판마다 모서리 외곽선을 낸다. 없으면 Lit 폴백.
        /// </summary>
        static Material GridMatEdgeUV()
        {
            var baseMat = AssetDatabase.LoadAssetAtPath<Material>(GridMatPath);
            if (baseMat == null) return LitMat(new Color(0.55f, 0.55f, 0.58f));
            var m = new Material(baseMat);   // 그리드 색·격자 설정 복사
            m.SetFloat("_EdgeFromUV", 1f);
            m.EnableKeyword("_EDGE_FROM_UV");
            return m;
        }

        /// <summary>URP Lit 단색 머티리얼(그리드 머티리얼 없을 때 폴백).</summary>
        static Material LitMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }


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
