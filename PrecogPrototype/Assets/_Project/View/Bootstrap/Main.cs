using UnityEngine;
using Game.Sim;
using Game.Bridge;

namespace Game.View
{
    /// <summary>
    /// 심장. 빈 GameObject에 이 컴포넌트 하나만 붙이면 동작한다.
    /// Start: 맵·NavMesh·서비스·월드 구성. Update: 입력·뷰·1인칭 카메라. FixedUpdate: SimStep.
    /// 전투 없음(뼈대) — 이동·지형충돌·적 길찾기·겹침밀치기만.
    /// </summary>
    public class Main : MonoBehaviour
    {
        [SerializeField] float eyeHeight = 1.0f;   // 줄인 키(1.15)에 맞춤
        public bool useSceneGeometry;   // true=씬 지형(Synty) 사용, false=코드 큐브맵

        SimWorld world, prevWorld;
        readonly InputReader input = new InputReader();
        readonly EntityViews views = new EntityViews();
        SimServices services;
        Camera cam;
        float fixedAccum;

        void Start()
        {
            Time.fixedDeltaTime = SimConfig.TickDelta;

            // 씬 지형 스폰 기준점: 기존 카메라 위치(액션 지점 힌트)
            Vector3 refPoint = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

            var links = useSceneGeometry ? MapBuilder.BuildFromScene() : MapBuilder.BuildCubes();
            services = new SimServices(new PhysicsCollision(Physics.DefaultRaycastLayers),
                                       new NavMeshPathfinder(links));

            world = SimWorld.Create();

            if (useSceneGeometry)
            {
                Vector3 spawn = SampleNav(refPoint, 80f, out var sp) ? sp : Vector3.zero;
                world.player = PlayerSim.Spawn(spawn);
                for (int i = 0; i < 8; i++)
                {
                    float a = (float)i / 8f * Mathf.PI * 2f;
                    Vector3 p = spawn + new Vector3(Mathf.Cos(a) * 10f, 0f, Mathf.Sin(a) * 10f);
                    if (SampleNav(p, 8f, out var e)) world.AddEnemy(e);
                }
                Debug.Log($"[Main] 씬 모드. 플레이어 {spawn}, 적 {world.enemyCount}");
            }
            else
            {
                world.player = PlayerSim.Spawn(new Vector3(-14f, 0f, 14f));
                world.AddEnemy(new Vector3( 0f, 4f, 14f));
                world.AddEnemy(new Vector3( 4f, 4f, 12f));
                world.AddEnemy(new Vector3(-4f, 4f, 16f));
                world.AddEnemy(new Vector3( 2f, 4f, 17f));
                world.AddEnemy(new Vector3(-2f, 4f, 10f));
                world.AddEnemy(new Vector3( 5f, 4f, 14f));
                DiagnoseLink(new Vector3(0f, 4f, 14f), world.player.pos,
                             new Vector3(-6.5f, 4f, 14f), new Vector3(-12f, 0f, 14f));
            }
            prevWorld = Snapshot.Clone(in world);

            views.Init();
            SetupCamera();
            input.Yaw = 90f;   // 시작 시 발판·절벽(+X) 쪽을 봄
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Debug.Log($"[Main] 뼈대 시작. 적 {world.enemyCount}. " +
                      "WASD 이동 · 마우스 시점 · Space 더블점프 · Shift 질풍참 · Esc 커서해제");
        }

        void Update()
        {
            input.PollFrame();

            fixedAccum += Time.deltaTime;
            float alpha = Mathf.Clamp01(fixedAccum / Time.fixedDeltaTime);
            views.Sync(in world, in prevWorld, alpha);

            if (cam != null && views.PlayerAnchor != null)
            {
                cam.transform.position = views.PlayerAnchor.position + Vector3.up * eyeHeight;
                cam.transform.rotation = Quaternion.Euler(input.Pitch, input.Yaw, 0f);
            }

            if (input.EscapePressed()) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        void FixedUpdate()
        {
            prevWorld = Snapshot.Clone(in world);
            InputCmd cmd = input.Consume();
            SimStep.Run(ref world, in cmd, in services);
            fixedAccum = 0f;

            // 하강 시작 감지 (링크 라우팅 확인용)
            for (int i = 0; i < world.enemyCount; i++)
            {
                if (prevWorld.enemies[i].descentPhase == DescentPhase.None &&
                    world.enemies[i].descentPhase == DescentPhase.EdgePause)
                    Debug.Log($"[하강] 적 {i} 테두리 점프 시작 (tick {world.tick})");
            }
        }

        /// <summary>발판→플레이어 경로가 하강 링크를 실제로 지나는지 시작 시 확인.</summary>
        static void DiagnoseLink(Vector3 from, Vector3 to, Vector3 linkStart, Vector3 linkEnd)
        {
            var path = new UnityEngine.AI.NavMeshPath();
            UnityEngine.AI.NavMesh.SamplePosition(from, out var f, 4f, UnityEngine.AI.NavMesh.AllAreas);
            UnityEngine.AI.NavMesh.SamplePosition(to,   out var t, 4f, UnityEngine.AI.NavMesh.AllAreas);
            UnityEngine.AI.NavMesh.CalculatePath(f.position, t.position, UnityEngine.AI.NavMesh.AllAreas, path);

            bool usesLink = false;
            var c = path.corners;
            for (int i = 0; i < c.Length; i++)
            {
                if (Flat(c[i], linkStart) < 1.5f) usesLink = true;
            }
            bool startOnNav = UnityEngine.AI.NavMesh.SamplePosition(linkStart, out _, 0.6f, UnityEngine.AI.NavMesh.AllAreas);
            bool endOnNav   = UnityEngine.AI.NavMesh.SamplePosition(linkEnd,   out _, 0.6f, UnityEngine.AI.NavMesh.AllAreas);
            var sb = new System.Text.StringBuilder();
            sb.Append($"[진단] 발판→플레이어 경로: 상태 {path.status}, 코너 {c.Length}개, ");
            sb.Append(usesLink ? "하강링크 통과함 ✓" : "하강링크 안 씀 ✗");
            sb.Append($"  [링크시작 NavMesh위:{startOnNav} 끝:{endOnNav}]");
            sb.Append("\n  코너들:");
            for (int i = 0; i < c.Length; i++) sb.Append($" ({c[i].x:F1},{c[i].y:F1},{c[i].z:F1})");
            Debug.Log(sb.ToString());
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static bool SampleNav(Vector3 p, float radius, out Vector3 hit)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var h, radius, UnityEngine.AI.NavMesh.AllAreas))
            { hit = h.position; return true; }
            hit = p; return false;
        }

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
        }

        void OnDrawGizmos()
        {
            if (Application.isPlaying) SimGizmos.Draw(in world);
        }
    }

    /// <summary>Play 누르면 자동 실행. 씬에 Main이 없으면 하나 만든다(수동 추가해도 중복 안 됨).</summary>
    public static class AutoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<Main>() != null) return;
            var main = new GameObject("[Main]").AddComponent<Main>();
            // SampleScene = 코드 큐브맵, 그 외(Demo 등) = 씬 지형 사용
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            main.useSceneGeometry = scene != "SampleScene";
        }
    }
}
