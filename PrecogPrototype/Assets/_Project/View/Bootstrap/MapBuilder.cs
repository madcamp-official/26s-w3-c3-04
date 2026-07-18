using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Game.Sim;

namespace Game.View
{
    /// <summary>맵 생성 결과: 하강 테두리 + 스폰 지점 + 플레이어 시작점 + 나브 그래프.</summary>
    public class MapResult
    {
        public List<(Vector3 edge, Vector3 landing)> drops = new();
        public List<Vector3> spawns = new();
        public Vector3 playerSpawn;
        public NavGraph navGraph;   // null이면 NavMeshPathfinder 폴백(씬 모드 등)
    }

    /// <summary>
    /// 코드 아레나(축정렬 박스 + 쐐기 경사로)와 NavMesh 베이크.
    /// 여러 높이의 발판 + 부드러운 경사로 + 벽으로 길찾기 복잡도를 준다.
    /// 하강 테두리 몇 곳 지정(우리가 판단해 순간이동).
    /// </summary>
    public static class MapBuilder
    {
        public static MapResult BuildCubes()
        {
            var gray  = Mat(new Color(0.55f, 0.55f, 0.58f));   // 바닥·외벽
            var g2    = Mat(new Color(0.45f, 0.48f, 0.55f));   // 2F 발판
            var g3    = Mat(new Color(0.36f, 0.40f, 0.52f));   // 3F 타워·다리
            var gMid  = Mat(new Color(0.50f, 0.50f, 0.46f));   // 반층(3.0)
            var gRamp = Mat(new Color(0.34f, 0.50f, 0.42f));   // 경사로(초록기)
            var gLedge= Mat(new Color(0.60f, 0.52f, 0.40f));   // mantle 계단·엄폐(갈색기)
            var r = new MapResult();

            // 높이: 1F 0 · 반층 3.0 · 2F 4.5 · 3F 9 (단차 1.5배). mantle 1.5(3단=한 층).
            // ── 바닥 + 외벽 (3층 담을 높이 12) ──
            Cube("Floor",  new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), gray);
            Cube("Wall_N", new Vector3(0f, 6f,  29f), new Vector3(60f, 12f, 2f), gray);
            Cube("Wall_S", new Vector3(0f, 6f, -29f), new Vector3(60f, 12f, 2f), gray);
            Cube("Wall_E", new Vector3( 29f, 6f, 0f), new Vector3(2f, 12f, 60f), gray);
            Cube("Wall_W", new Vector3(-29f, 6f, 0f), new Vector3(2f, 12f, 60f), gray);

            // ── NE: 2F 대형 발판 + 3F 코너 타워 + 램프 + mantle 계단 ──
            Platform("PlatNE2", 4f, 28f, 4f, 28f, 4.5f, g2);       // 2F (타워가 코너 차지 → ㄱ자)
            Platform("PlatNE3", 18f, 28f, 18f, 28f, 9f, g3);       // 3F 타워
            Ramp("Ramp_NE_12", new Vector3(10f, 0f, -2f), new Vector3(10f, 4.5f, 4f), 4f, gRamp);  // 1F→2F 남면
            Ramp("Ramp_NE_23", new Vector3(10f, 4.5f, 20f), new Vector3(18f, 9f, 20f), 4f, gRamp); // 2F→3F 타워 서면
            MantleStair("MS_NE12", 0f, 10f, Vector3.right, 0f, 4.5f, 3f, gLedge);   // 1F→2F 플레이어 전용
            MantleStair("MS_NE23", 14f, 10f, Vector3.right, 4.5f, 9f, 3f, gLedge);  // 2F→3F 플레이어 전용

            // ── SW: 미러(대각 대칭) ──
            Platform("PlatSW2", -28f, -4f, -28f, -4f, 4.5f, g2);
            Platform("PlatSW3", -28f, -18f, -28f, -18f, 9f, g3);
            Ramp("Ramp_SW_12", new Vector3(-10f, 0f, 2f), new Vector3(-10f, 4.5f, -4f), 4f, gRamp);
            Ramp("Ramp_SW_23", new Vector3(-10f, 4.5f, -20f), new Vector3(-18f, 9f, -20f), 4f, gRamp);
            MantleStair("MS_SW12", 0f, -10f, Vector3.left, 0f, 4.5f, 3f, gLedge);
            MantleStair("MS_SW23", -14f, -10f, Vector3.left, 4.5f, 9f, 3f, gLedge);

            // ── 3F 다리(catwalk): 두 타워를 잇는 ㄴ자 통로 + 중앙 노드(기둥 위) ──
            Cube("Bridge_N", new Vector3(-2f, 8.75f, 22f), new Vector3(40f, 0.5f, 4f), g3);   // x[-22,18] z[20,24]
            Cube("Bridge_W", new Vector3(-22f, 8.75f, 0f), new Vector3(4f, 0.5f, 48f), g3);   // x[-24,-20] z[-24,24]
            Cube("Bridge_C", new Vector3(0f, 8.75f, 11f), new Vector3(4f, 0.5f, 18f), g3);    // 중앙 스퍼 z[2,20]
            Cube("Pillar",   new Vector3(0f, 4.5f, 0f),   new Vector3(4f, 9f, 4f), gray);     // 중앙 기둥(0→9, 꼭대기=3F 노드)

            // ── 반층(3.0) 플랫폼: NW·SE 개활지에 "1.5층" 전투 발코니 + 경사로 ──
            Platform("PlatMidNW", -24f, -10f, 10f, 24f, 3f, gMid);
            Ramp("RampMidNW", new Vector3(-6f, 0f, 17f), new Vector3(-10f, 3f, 17f), 4f, gRamp);
            Platform("PlatMidSE", 10f, 24f, -24f, -10f, 3f, gMid);
            Ramp("RampMidSE", new Vector3(6f, 0f, -17f), new Vector3(10f, 3f, -17f), 4f, gRamp);

            // ── 엄폐 턱(0.8, 적 못 넘음) + 잔단차(≤0.4, 적 통과) ──
            Ledge("Cover1", -16f, 0f, -14f, 0.8f, 3f, gLedge);
            Ledge("Cover2", 16f, 0f, 14f, 0.8f, 3f, gLedge);
            Ledge("Step1", 0f, 0f, 14f, 0.3f, 6f, gLedge);
            Ledge("Step2", 0f, 0f, -14f, 0.3f, 6f, gLedge);

            Physics.SyncTransforms();

            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();

            var tri = NavMesh.CalculateTriangulation();
            Debug.Log(tri.vertices != null && tri.vertices.Length > 0
                ? $"[Map] 3층 아레나 v2 NavMesh 베이크 — 정점 {tri.vertices.Length}"
                : "[Map] NavMesh 베이크 실패");
            DrawNavMeshOverlay(tri);

            // ── 하강 테두리 (우리가 판단해 순간이동) ──
            AddDrop(r, new Vector3(18f, 9f, 22f),  new Vector3(15f, 4.5f, 22f));   // 3F→2F NE 서
            AddDrop(r, new Vector3(22f, 9f, 18f),  new Vector3(22f, 4.5f, 15f));   // 3F→2F NE 남
            AddDrop(r, new Vector3(-18f, 9f, -22f), new Vector3(-15f, 4.5f, -22f)); // 3F→2F SW
            AddDrop(r, new Vector3(-22f, 9f, -18f), new Vector3(-22f, 4.5f, -15f)); // 3F→2F SW
            AddDrop(r, new Vector3(0f, 9f, 22f),   new Vector3(0f, 0f, 22f));      // 3F 다리→1F 북
            AddDrop(r, new Vector3(-22f, 9f, 0f),  new Vector3(-26f, 0f, 0f));     // 3F 다리→1F 서
            AddDrop(r, new Vector3(6f, 4.5f, 10f), new Vector3(0f, 0f, 10f));      // 2F→1F NE 서
            AddDrop(r, new Vector3(10f, 4.5f, 6f), new Vector3(10f, 0f, 0f));      // 2F→1F NE 남
            AddDrop(r, new Vector3(-6f, 4.5f, -10f), new Vector3(0f, 0f, -10f));   // 2F→1F SW
            AddDrop(r, new Vector3(-10f, 4.5f, -6f), new Vector3(-10f, 0f, 0f));   // 2F→1F SW
            AddDrop(r, new Vector3(-10f, 3f, 17f), new Vector3(-6f, 0f, 17f));     // 반층→1F NW
            AddDrop(r, new Vector3(10f, 3f, -17f), new Vector3(6f, 0f, -17f));     // 반층→1F SE

            // ── 스폰 지점 (1F·반층·2F·3F 다양한 위치·고저차) ──
            r.spawns.Add(new Vector3(0f, 0f, -22f));    // 1F S
            r.spawns.Add(new Vector3(22f, 0f, -6f));    // 1F SE
            r.spawns.Add(new Vector3(-22f, 0f, 6f));    // 1F NW
            r.spawns.Add(new Vector3(6f, 0f, -8f));     // 1F 중앙
            r.spawns.Add(new Vector3(-17f, 3f, 17f));   // 반층 NW
            r.spawns.Add(new Vector3(17f, 3f, -17f));   // 반층 SE
            r.spawns.Add(new Vector3(10f, 4.5f, 10f));  // 2F NE
            r.spawns.Add(new Vector3(-10f, 4.5f, -10f)); // 2F SW
            r.spawns.Add(new Vector3(23f, 9f, 23f));    // 3F NE 타워
            r.spawns.Add(new Vector3(-23f, 9f, -23f));  // 3F SW 타워
            r.playerSpawn = new Vector3(0f, 0f, 10f);   // 1F 중앙 개활지

            r.navGraph = BuildNavGraph();   // ← 예측 친화 노드 그래프(NavMesh 대체)

            Light();
            return r;
        }

        /// <summary>
        /// 이 아레나의 나브 노드 그래프(G1 1차). 층별 주요 지점 + 경사로 연결.
        /// 하강은 drops 목록(NearestDropEdge)이 담당 — 그래프는 걷기 경로만(walk 링크).
        /// 노드-투-노드라 다소 거칠다(로컬 스티어링/부스터/그래프-하강은 이후 단계).
        /// </summary>
        static NavGraph BuildNavGraph()
        {
            var nodes = new NavNode[]
            {
                N(0f, 0f, 0f, 0),        // 0  1F 중앙
                N(14f, 0f, -6f, 0),      // 1  1F SE
                N(-14f, 0f, 6f, 0),      // 2  1F NW
                N(10f, 0f, -2f, 0),      // 3  NE 경사로12 하단
                N(-10f, 0f, 2f, 0),      // 4  SW 경사로12 하단
                N(-6f, 0f, 17f, 0),      // 5  NW 반층경사로 하단
                N(6f, 0f, -17f, 0),      // 6  SE 반층경사로 하단
                N(10f, 4.5f, 6f, 2),     // 7  NE 경사로12 상단(2F)
                N(10f, 4.5f, 18f, 2),    // 8  NE 경사로23 하단
                N(-10f, 4.5f, -6f, 2),   // 9  SW 경사로12 상단
                N(-10f, 4.5f, -18f, 2),  // 10 SW 경사로23 하단
                N(18f, 9f, 20f, 3),      // 11 NE 타워/경사로23 상단(3F)
                N(-18f, 9f, -20f, 3),    // 12 SW 타워
                N(0f, 9f, 22f, 3),       // 13 3F 다리 중앙
                N(-14f, 3f, 17f, 1),     // 14 반층 NW
                N(14f, 3f, -17f, 1),     // 15 반층 SE
            };

            var links = new List<NavLink>();
            void W(int a, int b) { const float w = 4f; links.Add(new NavLink(a, b, MoveKind.Walk, w)); links.Add(new NavLink(b, a, MoveKind.Walk, w)); }
            W(0, 1); W(0, 2); W(0, 3); W(0, 4); W(0, 5); W(0, 6); W(1, 3); W(2, 4);  // 1F
            W(3, 7); W(4, 9);          // 1F→2F 경사로12
            W(7, 8); W(9, 10);         // 2F 내부
            W(8, 11); W(10, 12);       // 2F→3F 경사로23
            W(11, 13); W(12, 13);      // 3F 다리
            W(5, 14); W(6, 15);        // 1F→반층 경사로

            return NavGraph.Create(nodes, links.ToArray());
        }

        static NavNode N(float x, float y, float z, byte level)
            => new NavNode { pos = new Vector3(x, y, z), level = level };

        public static MapResult BuildFromScene(Vector3 refPoint)
        {
            Physics.SyncTransforms();
            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            var tri = NavMesh.CalculateTriangulation();
            Debug.Log(tri.vertices != null && tri.vertices.Length > 0
                ? $"[Map] 씬 지형 NavMesh 베이크(콜라이더) — 정점 {tri.vertices.Length}"
                : "[Map] 씬 지형 NavMesh 실패 — 콜라이더 확인");

            var r = new MapResult();
            if (NavMesh.SamplePosition(refPoint, out var p, 80f, NavMesh.AllAreas))
                r.playerSpawn = p.position;
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f;
                Vector3 q = r.playerSpawn + new Vector3(Mathf.Cos(a) * 10f, 0f, Mathf.Sin(a) * 10f);
                if (NavMesh.SamplePosition(q, out var h, 8f, NavMesh.AllAreas)) r.spawns.Add(h.position);
            }
            Light();
            return r;
        }

        static void AddDrop(MapResult r, Vector3 edge, Vector3 landing)
        {
            r.drops.Add((edge, landing));
            DrawLink(edge, landing);
        }

        // ── 지형 헬퍼 (전부 축정렬) ──
        static void Platform(string name, float minX, float maxX, float minZ, float maxZ, float topY, Material m)
        {
            Cube(name, new Vector3((minX + maxX) / 2f, topY / 2f, (minZ + maxZ) / 2f),
                 new Vector3(maxX - minX, topY, maxZ - minZ), m);
        }

        /// <summary>
        /// 턱(정사각 박스). baseY 위에 height 만큼 솟은 솔리드. mantle 턱·엄폐물·잔단차 공용.
        /// height ≤ 0.4 이면 적도 NavMesh step 으로 넘고, 초과면 플레이어 전용(적 못 오름).
        /// </summary>
        static void Ledge(string name, float cx, float baseY, float cz, float height, float side, Material m)
        {
            Cube(name, new Vector3(cx, baseY + height * 0.5f, cz), new Vector3(side, height, side), m);
        }

        /// <summary>
        /// mantle 계단: foot에서 dir로 1.5씩 올라 topY 도달(플레이어 전용, 적은 각 1.5 턱 못 넘음).
        /// 각 단은 바닥(0)까지 솔리드라 baseY 위 발판에 얹으면 노출부만 밟힌다.
        /// </summary>
        static void MantleStair(string name, float footX, float footZ, Vector3 dir,
                                float baseY, float topY, float width, Material m)
        {
            const float stepH = 1.5f, stepD = 1.6f;
            int steps = Mathf.CeilToInt((topY - baseY) / stepH);
            for (int i = 0; i < steps; i++)
            {
                float top = Mathf.Min(topY, baseY + stepH * (i + 1));   // 이 단의 절대 윗면 Y
                Vector3 c = new Vector3(footX, 0f, footZ) + dir * (stepD * (i + 0.5f));
                c.y = top * 0.5f;
                Vector3 s = Mathf.Abs(dir.x) > 0.5f
                    ? new Vector3(stepD, top, width)
                    : new Vector3(width, top, stepD);
                Cube($"{name}_{i}", c, s, m);
            }
        }

        /// <summary>
        /// 경사로(부드러운 빗면). low(바닥,y=0)에서 high(발판 모서리,y=H)로 오르는 솔리드 쐐기.
        /// 회전 큐브가 아니라 6정점 쐐기 메시를 직접 만든다 — 좌표를 정확히 박아 어긋남이 없다.
        /// 빗면 법선은 run 방향과 무관하게 항상 위(+Y) → NavMesh가 빗면 위에 깔린다.
        /// </summary>
        static void Ramp(string name, Vector3 low, Vector3 high, float width, Material m)
        {
            Vector3 runH = new Vector3(high.x - low.x, 0f, high.z - low.z);
            Vector3 perp = new Vector3(-runH.z, 0f, runH.x).normalized * (width * 0.5f);
            Vector3 highBottom = new Vector3(high.x, low.y, high.z);

            Vector3 P0 = low - perp;         // 바닥 낮은쪽 A
            Vector3 P1 = highBottom - perp;  // 바닥 높은쪽 A
            Vector3 P2 = highBottom + perp;  // 바닥 높은쪽 B
            Vector3 P3 = low + perp;         // 바닥 낮은쪽 B
            Vector3 P4 = high - perp;        // 꼭대기 A
            Vector3 P5 = high + perp;        // 꼭대기 B

            var mesh = new Mesh { name = name + "Mesh" };
            mesh.vertices = new[] { P0, P1, P2, P3, P4, P5 };
            mesh.triangles = new[]
            {
                0, 3, 4,  4, 3, 5,   // 빗면(위 향함, 걷는 면)
                0, 2, 1,  0, 3, 2,   // 바닥
                1, 2, 5,  1, 5, 4,   // 높은쪽 수직면
                0, 4, 1,             // 옆면 A
                3, 2, 5,             // 옆면 B
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().material = m;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;   // 정적 non-convex, CapsuleCast·NavMesh용
        }

        static void Light()
        {
            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var l = new GameObject("Directional Light").AddComponent<Light>();
                l.type = LightType.Directional;
                l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        static void DrawNavMeshOverlay(NavMeshTriangulation tri)
        {
            if (tri.vertices == null || tri.vertices.Length == 0) return;
            var mesh = new Mesh { name = "NavMeshOverlay", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = tri.vertices; mesh.triangles = tri.indices; mesh.RecalculateNormals();
            var go = new GameObject("NavMeshOverlay");
            go.transform.position = Vector3.up * 0.06f;
            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material = Unlit(new Color(0.25f, 0.75f, 1f));
        }

        static void DrawLink(Vector3 start, Vector3 end)
        {
            var lr = new GameObject("DropViz").AddComponent<LineRenderer>();
            lr.material = Unlit(new Color(1f, 0.9f, 0.1f));
            lr.widthMultiplier = 0.25f;
            lr.positionCount = 2;
            lr.SetPosition(0, start + Vector3.up * 0.2f);
            lr.SetPosition(1, end + Vector3.up * 0.2f);
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "LandingMark"; Object.Destroy(s.GetComponent<Collider>());
            s.transform.position = end + Vector3.up * 0.3f; s.transform.localScale = Vector3.one * 0.6f;
            s.GetComponent<Renderer>().material = Unlit(new Color(1f, 0.5f, 0.1f));
        }

        static GameObject Cube(string name, Vector3 c, Vector3 s, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.position = c; go.transform.localScale = s;
            go.GetComponent<Renderer>().material = m;
            return go;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
        static Material Unlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
