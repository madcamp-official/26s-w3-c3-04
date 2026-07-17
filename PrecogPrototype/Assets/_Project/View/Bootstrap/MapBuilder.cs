using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace Game.View
{
    /// <summary>
    /// 깨끗한 테스트 아레나: 발판 1개 + 동쪽 램프(걸어 내려가기) + 서쪽 절벽(점프 하강 링크).
    /// NavMesh와 하강 링크를 화면에 그려서 눈으로 확인 가능.
    /// 반환: 하강 링크 목록(start→end).
    /// </summary>
    public static class MapBuilder
    {
        /// <summary>씬에 이미 있는 지형(Synty 등)에서 NavMesh만 굽는다. 큐브 생성 안 함.</summary>
        public static List<(Vector3 start, Vector3 end)> BuildFromScene()
        {
            Physics.SyncTransforms();
            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;  // 메시 대신 콜라이더 (Synty 메시 read 불가 회피)
            surface.BuildNavMesh();

            var tri = NavMesh.CalculateTriangulation();
            Debug.Log(tri.vertices != null && tri.vertices.Length > 0
                ? $"[Map] 씬 지형 NavMesh 베이크 완료(콜라이더 기반) — 정점 {tri.vertices.Length}"
                : "[Map] 씬 지형 NavMesh 실패 — 콜라이더 없음? (Synty 지형에 콜라이더 확인 필요)");

            return new List<(Vector3, Vector3)>();   // 하강 링크는 씬에 맞게 나중에
        }

        public static List<(Vector3 start, Vector3 end)> BuildCubes()
        {
            var gray  = Mat(new Color(0.55f, 0.55f, 0.58f));
            var gray2 = Mat(new Color(0.45f, 0.48f, 0.55f));

            // 바닥
            Cube("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), Quaternion.identity, gray);

            // 발판: top y=4, x[-8,8], z[8,20]  (바닥에 붙은 솔리드 블록)
            Cube("Platform", new Vector3(0f, 2f, 14f), new Vector3(16f, 4f, 12f), Quaternion.identity, gray2);

            // (램프 임시 제거 — 링크가 유일한 하강 경로가 되게 해서 링크 작동 여부를 확정)

            // 외벽
            Cube("Wall_N", new Vector3(0f, 2f,  29f), new Vector3(60f, 4f, 2f), Quaternion.identity, gray);
            Cube("Wall_S", new Vector3(0f, 2f, -29f), new Vector3(60f, 4f, 2f), Quaternion.identity, gray);
            Cube("Wall_E", new Vector3( 29f, 2f, 0f), new Vector3(2f, 4f, 60f), Quaternion.identity, gray);
            Cube("Wall_W", new Vector3(-29f, 2f, 0f), new Vector3(2f, 4f, 60f), Quaternion.identity, gray);

            Physics.SyncTransforms();

            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();

            // 서쪽 절벽 하강 링크: 발판 -X 모서리(-8,4) → 바닥(-11,0). 한 방향.
            var links = new List<(Vector3, Vector3)>();
            Vector3 lstart = new Vector3(-6.5f, 4f, 14f);   // 발판 NavMesh 안쪽(테두리 아님)
            Vector3 lend   = new Vector3(-12f,  0f, 14f);   // 바닥
            AddDropLink(lstart, lend, width: 12f);
            links.Add((lstart, lend));

            var tri = NavMesh.CalculateTriangulation();
            Debug.Log(tri.vertices != null && tri.vertices.Length > 0
                ? $"[Map] NavMesh 베이크 완료 — 정점 {tri.vertices.Length}"
                : "[Map] NavMesh 베이크 실패");

            DrawNavMeshOverlay(tri);
            DrawLink(lstart, lend);

            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var l = new GameObject("Directional Light").AddComponent<Light>();
                l.type = LightType.Directional;
                l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            return links;
        }

        static void AddDropLink(Vector3 start, Vector3 end, float width)
        {
            var go = new GameObject("DropLink");
            var link = go.AddComponent<NavMeshLink>();
            link.startPoint = start;
            link.endPoint = end;
            link.width = width;
            link.bidirectional = false;
            link.area = 0;
            link.UpdateLink();
        }

        static void DrawNavMeshOverlay(NavMeshTriangulation tri)
        {
            if (tri.vertices == null || tri.vertices.Length == 0) return;
            var mesh = new Mesh { name = "NavMeshOverlay",
                                  indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = tri.vertices; mesh.triangles = tri.indices; mesh.RecalculateNormals();
            var go = new GameObject("NavMeshOverlay");
            go.transform.position = Vector3.up * 0.06f;
            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material = Unlit(new Color(0.25f, 0.75f, 1f, 1f));
        }

        static void DrawLink(Vector3 start, Vector3 end)
        {
            var lr = new GameObject("DropLinkViz").AddComponent<LineRenderer>();
            lr.material = Unlit(new Color(1f, 0.9f, 0.1f));
            lr.widthMultiplier = 0.25f;
            lr.positionCount = 2;
            lr.SetPosition(0, start + Vector3.up * 0.2f);
            lr.SetPosition(1, end + Vector3.up * 0.2f);

            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "LandingMark"; Object.Destroy(s.GetComponent<Collider>());
            s.transform.position = end + Vector3.up * 0.3f;
            s.transform.localScale = Vector3.one * 0.8f;
            s.GetComponent<Renderer>().material = Unlit(new Color(1f, 0.5f, 0.1f));
        }

        static GameObject Cube(string name, Vector3 c, Vector3 s, Quaternion r, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.position = c; go.transform.localScale = s; go.transform.rotation = r;
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
