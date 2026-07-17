using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace Game.Verify
{
    /// <summary>
    /// NavMesh 검증 + 시각 샌드박스.
    ///   - 벽 → 경로가 돌아가는지(꺾임)
    ///   - 램프 → 경로 선이 표면을 따라 /ㅡ로 그려지는지
    ///   - 기둥 위 다리(오버패스) → 그 아래도 걸을 수 있는지(NavMesh가 아래에도 깔리는지)
    ///   - 결정론(같은 입력 → 같은 경로)
    ///
    /// 화면: 회색=지형, 하늘색 면=NavMesh(걸을 수 있는 영역), 빨간 선=경로, 노란 구슬=코너.
    /// Play만 누르면 자동 실행. 검증 끝나면 이 파일 삭제 가능.
    /// </summary>
    public class NavMeshDeterminismTest : MonoBehaviour
    {
        // 쇼케이스 경로: 1층 왼쪽 → 발판 위. 벽을 돌아 램프로 올라가야 함.
        static readonly Vector3 FromPt = new Vector3(-18f, 0f, 0f);
        static readonly Vector3 ToPt   = new Vector3( 16f, 3f, 0f);

        void Start()
        {
            BuildGeometry();
            Physics.SyncTransforms();   // 방금 만든 콜라이더를 물리 질의(raycast)에 반영 — 이게 없으면 raycast가 빗나감
            if (!BuildNavMesh()) return;
            Determinism();
            Visualize();
            SetupCamera();
        }

        void Determinism()
        {
            if (!Snap(FromPt, out var f) || !Snap(ToPt, out var t)) return;
            ulong first = 0; var status = NavMeshPathStatus.PathInvalid; int corners = 0; bool same = true;
            for (int i = 0; i < 200; i++)
            {
                var p = new NavMeshPath();
                NavMesh.CalculatePath(f, t, NavMesh.AllAreas, p);
                ulong h = Hash(p);
                if (i == 0) { first = h; status = p.status; corners = p.corners.Length; }
                else if (h != first) same = false;
            }
            Debug.Log($"[검증] 쇼케이스 경로 200회 → {(same ? "결정론 동일 ✓" : "매번 다름 ✗")}  " +
                      $"코너 {corners} (2보다 크면 벽을 돌아 꺾인 것), 상태 {status}");
        }

        void Visualize()
        {
            // NavMesh 면 (하늘색) — 다리 아래에도 깔리면 그 밑을 걸을 수 있다는 뜻
            var tri = NavMesh.CalculateTriangulation();
            var mesh = new Mesh { name = "NavMeshView", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = tri.vertices; mesh.triangles = tri.indices; mesh.RecalculateNormals();
            var nav = new GameObject("NavMeshView");
            nav.transform.position = Vector3.up * 0.06f;
            nav.AddComponent<MeshFilter>().mesh = mesh;
            nav.AddComponent<MeshRenderer>().material = Mat(new Color(0.25f, 0.75f, 1f));

            // 빨강: 벽을 돌아가는 경로
            DrawPath(FromPt, ToPt, new Color(1f, 0.2f, 0.1f), "PathLine_Wall");
            // 초록: 벽 없이 램프만 타는 경로 (여기서 /ㅡ 꺾임이 보여야 함)
            DrawPath(new Vector3(-3f, 0f, 0f), new Vector3(16f, 3f, 0f),
                     new Color(0.2f, 1f, 0.3f), "PathLine_Ramp");
        }

        void DrawPath(Vector3 from, Vector3 to, Color color, string name)
        {
            if (!Snap(from, out var f) || !Snap(to, out var t)) return;
            var path = new NavMeshPath();
            NavMesh.CalculatePath(f, t, NavMesh.AllAreas, path);

            var follow = FollowSurface(path.corners);
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.material = Mat(color);
            lr.widthMultiplier = 0.25f;
            lr.positionCount = follow.Count;
            lr.SetPositions(follow.ToArray());

            foreach (var c in path.corners)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "Corner"; Object.Destroy(s.GetComponent<Collider>());
                s.transform.position = c + Vector3.up * 0.3f;
                s.transform.localScale = Vector3.one * 0.6f;
                s.GetComponent<Renderer>().material = Mat(new Color(1f, 0.9f, 0.2f));
            }
        }

        /// <summary>코너 사이를 잘게 쪼개고, 각 점에서 아래로 raycast해 지형 표면 높이를 따라간다.</summary>
        static List<Vector3> FollowSurface(Vector3[] corners)
        {
            var outp = new List<Vector3>();
            for (int i = 0; i < corners.Length - 1; i++)
            {
                Vector3 a = corners[i], b = corners[i + 1];
                Vector2 axz = new Vector2(a.x, a.z), bxz = new Vector2(b.x, b.z);
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(axz, bxz) / 0.2f));
                for (int k = 0; k <= n; k++)
                {
                    float u = (float)k / n;
                    float x = Mathf.Lerp(a.x, b.x, u), z = Mathf.Lerp(a.z, b.z, u);
                    float y = Mathf.Lerp(a.y, b.y, u);
                    if (Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out var hit, 200f))
                        y = hit.point.y;   // 지형 표면 높이를 따라감
                    outp.Add(new Vector3(x, y + 0.2f, z));
                }
            }
            return outp;
        }

        void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
            cam.transform.position = new Vector3(-2f, 30f, -30f);
            cam.transform.rotation = Quaternion.Euler(46f, 5f, 0f);
        }

        // ── 지형 ──
        void BuildGeometry()
        {
            Cube("Floor",    new Vector3(0f, -0.5f, 0f), new Vector3(52f, 1f, 42f), Quaternion.identity);
            Cube("Platform", new Vector3(14f, 2.5f, 0f), new Vector3(12f, 1f, 20f), Quaternion.identity); // top y=3, x[8,20]

            // 램프 윗면이 정확히 (x=8,y=3)=발판 모서리에서 끝나고, 아래끝은 바닥(y=0)에 박히게.
            // 윗면 두 끝점: A=(-1.5,-0.56) 바닥 속, B=(8,3) 발판 모서리.  두께 0.6.
            Cube("Ramp", new Vector3(3.355f, 0.939f, 0f), new Vector3(10.14f, 0.6f, 20f),
                 Quaternion.Euler(0f, 0f, 20.556f));

            // 벽: 쇼케이스 경로를 막아 돌아가게 함 (x=-6, z=-6..6)
            Cube("Wall", new Vector3(-6f, 2f, 0f), new Vector3(1.5f, 4f, 12f), Quaternion.identity);

            // 다리(오버패스): 기둥 위에 띄움. 바닥~다리밑 = 3.75 > 몹키(2) → 아래도 걸을 수 있음
            Cube("Overpass", new Vector3(-9f, 4.0f, -13f), new Vector3(16f, 0.5f, 7f), Quaternion.identity); // bottom y=3.75
            foreach (var (px, pz) in new[] { (-16f, -16f), (-2f, -16f), (-16f, -10f), (-2f, -10f) })
                Cube("Pillar", new Vector3(px, 1.9f, pz), new Vector3(0.6f, 3.75f, 0.6f), Quaternion.identity);

            if (FindFirstObjectByType<Light>() == null)
            {
                var l = new GameObject("Light").AddComponent<Light>();
                l.type = LightType.Directional; l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        bool BuildNavMesh()
        {
            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();

            var s = surface.GetBuildSettings();
            Debug.Log($"[NavMesh] 몹 기준 — 반지름 {s.agentRadius}, 키 {s.agentHeight}, " +
                      $"오를수있는턱 {s.agentClimb}, 최대경사 {s.agentSlope}°\n" +
                      $"  → 천장 높이가 '키({s.agentHeight})'보다 낮으면 그 아래는 파란색이 안 생김");

            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            { Debug.LogError("[NavMesh] 베이크 실패 — 비어 있음"); return false; }
            Debug.Log($"[NavMesh] 베이크 완료 — 정점 {tri.vertices.Length}, 삼각형 {tri.indices.Length / 3}");
            return true;
        }

        static bool Snap(Vector3 p, out Vector3 hit)
        {
            if (NavMesh.SamplePosition(p, out var h, 6f, NavMesh.AllAreas)) { hit = h.position; return true; }
            hit = p; return false;
        }

        static ulong Hash(NavMeshPath path)
        {
            const ulong prime = 1099511628211UL;
            ulong h = 14695981039346656037UL; var c = path.corners;
            h ^= (ulong)c.Length; h *= prime;
            for (int i = 0; i < c.Length; i++)
            { h = MixF(h, c[i].x, prime); h = MixF(h, c[i].y, prime); h = MixF(h, c[i].z, prime); }
            h ^= (ulong)path.status; h *= prime; return h;
        }
        static ulong MixF(ulong h, float f, ulong prime)
        { uint b = (uint)System.BitConverter.SingleToInt32Bits(f); h ^= b; h *= prime; return h; }

        static GameObject Cube(string name, Vector3 c, Vector3 s, Quaternion r)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.position = c; go.transform.localScale = s; go.transform.rotation = r;
            return go;
        }
        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }

    public static class NavMeshTestBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => new GameObject("[NavMeshDeterminismTest]").AddComponent<NavMeshDeterminismTest>();
    }
}
