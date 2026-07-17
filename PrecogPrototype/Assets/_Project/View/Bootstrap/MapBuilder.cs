using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace Game.View
{
    /// <summary>
    /// 테스트 아레나 지형을 코드로 생성하고 NavMesh를 굽는다.
    /// 지형엔 콜라이더가 있어 CapsuleCast/Raycast(ICollision)가 물어볼 수 있고,
    /// NavMeshSurface로 길찾기 NavMesh를 만든다.
    /// </summary>
    public static class MapBuilder
    {
        public static void Build()
        {
            var gray  = Mat(new Color(0.55f, 0.55f, 0.58f));
            var gray2 = Mat(new Color(0.45f, 0.48f, 0.55f));

            // 바닥
            Cube("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), Quaternion.identity, gray);

            // 외벽 4개 (떨어지지 않게 + 벽 충돌 테스트)
            Cube("Wall_N", new Vector3(0f, 2f,  29f), new Vector3(60f, 4f, 2f), Quaternion.identity, gray);
            Cube("Wall_S", new Vector3(0f, 2f, -29f), new Vector3(60f, 4f, 2f), Quaternion.identity, gray);
            Cube("Wall_E", new Vector3( 29f, 2f, 0f), new Vector3(2f, 4f, 60f), Quaternion.identity, gray);
            Cube("Wall_W", new Vector3(-29f, 2f, 0f), new Vector3(2f, 4f, 60f), Quaternion.identity, gray);

            // 내부 장애물 (적이 돌아가야 함)
            Cube("Obstacle", new Vector3(-8f, 2f, 0f), new Vector3(2f, 4f, 16f), Quaternion.identity, gray2);

            // 발판 + 램프 (수직 구조: 적이 오르내림)
            Cube("Platform", new Vector3(16f, 2.5f, 0f), new Vector3(12f, 1f, 20f), Quaternion.identity, gray2); // top y=3, x[10,22]
            Cube("Ramp", new Vector3(5.5f, 1.4f, 0f), new Vector3(11f, 0.6f, 18f),
                 Quaternion.Euler(0f, 0f, Mathf.Atan2(3f, 9f) * Mathf.Rad2Deg), gray2);

            Physics.SyncTransforms();   // 방금 만든 콜라이더를 물리 질의에 반영

            // NavMesh 베이크
            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();

            var tri = NavMesh.CalculateTriangulation();
            Debug.Log(tri.vertices != null && tri.vertices.Length > 0
                ? $"[Map] NavMesh 베이크 완료 — 정점 {tri.vertices.Length}"
                : "[Map] NavMesh 베이크 실패 — 비어 있음");

            // 조명
            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var l = new GameObject("Directional Light").AddComponent<Light>();
                l.type = LightType.Directional;
                l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
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
    }
}
