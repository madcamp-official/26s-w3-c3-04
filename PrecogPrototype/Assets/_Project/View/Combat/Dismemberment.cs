using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 처치 시 몸을 슬라이스해 "잘린 단면"을 보여준다. ★ combat 소유·독립. 읽기 전용.
    /// 적이 죽는 프레임(alive true→false)에 그 위치·회전·크기로 캡슐 메시를 만들어
    /// 조준/스윙 방향의 평면으로 두 조각으로 가른다. 조각엔 Rigidbody로 물리·피 파티클.
    /// EntityViews는 죽으면 캡슐을 끄므로 그쪽 안 건드리고 같은 자리 시체가 나타난다.
    /// 지금은 캡슐(알약 두 쪽) — 나중에 진짜 사람 메시로 원본만 교체하면 슬라이서 재사용.
    /// </summary>
    public class Dismemberment : MonoBehaviour
    {
        readonly bool[] prevAlive = new bool[SimConfig.MaxEnemies];
        readonly bool[] seen      = new bool[SimConfig.MaxEnemies];
        int slashParity;

        Mesh capsuleSrc;                 // 적 크기 반영된 캡슐(슬라이스 원본)
        Material shellMat, fleshMat;
        ParticleSystem blood;

        const float CorpseLife = 5f;

        void Awake()
        {
            capsuleSrc = BuildScaledCapsule();
            shellMat = Mat(new Color(0.5f, 0.05f, 0.05f), false);        // 겉면(검붉은, 피격색 통일)
            fleshMat = Mat(new Color(0.30f, 0.02f, 0.02f), true);        // 단면(짙은 속살, 양면)
            BuildBlood();
        }

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (seen[i] && prevAlive[i] && !e.alive)
                    SliceCorpse(e.pos, e.yaw);
                seen[i]      = true;
                prevAlive[i] = e.alive;
            }
        }

        void SliceCorpse(Vector3 feet, float yaw)
        {
            Vector3 center = feet + Vector3.up * (SimConfig.EnemyHeight * 0.5f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            // 절단 평면: 카메라 스크린 평면 내 대각선 법선(슬래시 방향 교대) → 대각 단면
            Camera cam = Main.Instance.Cam;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 up  = cam != null ? cam.transform.up      : Vector3.up;
            slashParity ^= 1;
            Vector3 worldN = (Quaternion.AngleAxis(slashParity == 0 ? 45f : -45f, fwd) * up).normalized;

            // 로컬 평면(코프스 스케일=1이라 회전만 역변환). 평면점=중심=로컬 원점.
            Vector3 localN = (Quaternion.Inverse(rot) * worldN).normalized;

            if (!MeshSlicer.Slice(capsuleSrc, Vector3.zero, localN, out Mesh aboveM, out Mesh belowM))
                return;

            MakePiece(aboveM, center, rot,  worldN);
            MakePiece(belowM, center, rot, -worldN);

            blood.transform.SetPositionAndRotation(center, Quaternion.LookRotation(worldN));
            blood.Emit(40);
        }

        void MakePiece(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 pushDir)
        {
            var go = new GameObject("Corpse");
            // Ignore Raycast(2): 플레이어 지형 감지(DefaultRaycastLayers)에선 빠지고,
            // 물리 충돌 매트릭스로는 지형과 계속 부딪힘 → 밟히지 않으면서 바닥엔 떨어짐.
            go.layer = 2;
            go.transform.SetPositionAndRotation(pos, rot);

            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { shellMat, fleshMat };

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh; mc.convex = true;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 3f;
            rb.AddForce(pushDir * 2.2f + Vector3.up * 1.5f, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);

            Destroy(go, CorpseLife);
        }

        // ── 리소스 구성 ──

        static Mesh BuildScaledCapsule()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            tmp.SetActive(false);
            var srcMesh = tmp.GetComponent<MeshFilter>().sharedMesh;

            Vector3[] v = srcMesh.vertices;
            Vector3[] n = srcMesh.normals;
            // EntityViews 캡슐 스케일과 동일: (r*2, h*0.5, r*2). 기본 캡슐 높이2·반경0.5 기준.
            Vector3 s = new Vector3(SimConfig.EnemyRadius * 2f, SimConfig.EnemyHeight * 0.5f,
                                    SimConfig.EnemyRadius * 2f);
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = new Vector3(v[i].x * s.x, v[i].y * s.y, v[i].z * s.z);
                if (n != null && n.Length == v.Length)
                    n[i] = new Vector3(n[i].x / s.x, n[i].y / s.y, n[i].z / s.z).normalized;
            }

            var res = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            res.vertices  = v;
            res.normals   = n;
            res.triangles = srcMesh.triangles;
            res.RecalculateBounds();
            Destroy(tmp);
            return res;
        }

        void BuildBlood()
        {
            var go = new GameObject("BloodBurst");
            go.transform.SetParent(transform, false);
            blood = go.AddComponent<ParticleSystem>();
            blood.Stop();

            var m = blood.main;
            m.startLifetime   = 0.6f;
            m.startSpeed      = 5f;
            m.startSize       = 0.1f;
            m.startColor      = new Color(0.45f, 0.02f, 0.02f);
            m.gravityModifier = 1.5f;
            m.maxParticles    = 400;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.playOnAwake     = false;

            var em = blood.emission; em.enabled = false;   // 수동 Emit
            var sh = blood.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.1f;

            var r = blood.GetComponent<ParticleSystemRenderer>();
            var s = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (s != null) r.material = new Material(s);
        }

        static Material Mat(Color c, bool doubleSided)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // 단면 양면 렌더
            return m;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class DismembermentBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<Dismemberment>() == null)
                new GameObject("[Dismemberment]").AddComponent<Dismemberment>();
        }
    }
}
