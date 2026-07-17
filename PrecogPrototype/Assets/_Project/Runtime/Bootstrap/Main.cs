using UnityEngine;
using Game.Simulation;

namespace Game.Runtime
{
    /// <summary>
    /// 프로토타입의 심장. 빈 GameObject에 이 컴포넌트 하나만 붙이면 동작한다.
    ///
    /// - Start:       증명 3종(콘솔) + 월드/뷰/환경 생성 + 커서 잠금
    /// - Update:      입력 버퍼링, 뷰 보간, 1인칭 카메라 구동
    /// - FixedUpdate: 60Hz로 Kernel.Step (게임 로직은 여기서만) + 전투 로그
    /// </summary>
    public class Main : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] int  enemyCount = 8;
        [SerializeField] bool runProofsOnStart = true;
        [SerializeField] bool drawGizmos = true;
        [SerializeField] float eyeHeight = 1.6f;

        SimWorld world;
        SimWorld prevWorld;
        readonly InputReader input = new InputReader();
        readonly EntityViews views = new EntityViews();

        Camera cam;
        float fixedAccumTime;
        int   prevAliveEnemies;
        int   prevHp;

        void Start()
        {
            Time.fixedDeltaTime = SimConfig.TickDelta;   // 60Hz

            if (runProofsOnStart)
                ProofRunner.RunAll();

            world = SimWorld.Create();
            world.player = PlayerState.Spawn(Vector3.zero);
            for (int i = 0; i < enemyCount; i++)
            {
                float ang = (float)i / Mathf.Max(1, enemyCount) * Mathf.PI * 2f;
                world.AddMeleeEnemy(new Vector3(Mathf.Cos(ang) * 10f, 0f,
                                                Mathf.Sin(ang) * 10f));
            }
            prevWorld = Snapshot.Clone(in world);
            prevAliveEnemies = world.enemyCount;
            prevHp = world.player.hp;

            BuildEnvironment();
            views.Init();

            Cursor.lockState = CursorLockMode.Locked;   // 1인칭 마우스 잠금
            Cursor.visible = false;

            Debug.Log($"[Main] 시작. 적 {enemyCount} / HP {world.player.hp} / 대시 {world.player.dashCharges}\n" +
                      "  WASD 이동 · 마우스 시점 · Space 점프 · 좌클릭 평타 · Shift 질풍참 · 우클릭 막기\n" +
                      "  (커서 잠금 해제: Esc)");
        }

        void Update()
        {
            input.PollFrame();

            fixedAccumTime += Time.deltaTime;
            float alpha = Mathf.Clamp01(fixedAccumTime / Time.fixedDeltaTime);
            views.Sync(in world, alpha, in prevWorld);

            // 1인칭 카메라: 플레이어 눈높이에서 yaw/pitch로 바라봄
            if (cam != null && views.PlayerAnchor != null)
            {
                cam.transform.position = views.PlayerAnchor.position + Vector3.up * eyeHeight;
                cam.transform.rotation = Quaternion.Euler(input.Pitch, input.Yaw, 0f);
            }

            if (EscapePressed())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void FixedUpdate()
        {
            prevWorld = Snapshot.Clone(in world);
            InputCmd cmd = input.Consume();
            Kernel.Step(ref world, in cmd);
            fixedAccumTime = 0f;

            // 전투 로그 (변화 있을 때만)
            int alive = CountAlive(in world);
            if (alive != prevAliveEnemies)
            {
                Debug.Log($"[전투] 적 처치 → 남은 적 {alive}/{world.enemyCount} (tick {world.tick})");
                prevAliveEnemies = alive;
            }
            if (world.player.hp != prevHp)
            {
                Debug.Log($"[전투] 피격! HP {world.player.hp} (가드 {world.player.guardGauge}) (tick {world.tick})");
                prevHp = world.player.hp;
                if (!world.player.alive)
                    Debug.Log($"[Main] 플레이어 사망 (tick {world.tick})");
            }
        }

        static bool EscapePressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }

        static int CountAlive(in SimWorld w)
        {
            int n = 0;
            for (int i = 0; i < w.enemyCount; i++)
                if (w.enemies[i].alive) n++;
            return n;
        }

        void BuildEnvironment()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(5f, 1f, 5f);

            cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }

            if (FindLight() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        static Light FindLight()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<Light>();
#else
            return Object.FindObjectOfType<Light>();
#endif
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos || !Application.isPlaying) return;
            if (world.enemies == null) return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(world.player.pos + Vector3.up, SimConfig.PlayerRadius);

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemyState e = ref world.enemies[i];
                if (!e.alive) continue;

                Gizmos.color = e.stunTicks > 0 ? Color.blue : StateColor(e.aiState);
                Gizmos.DrawWireSphere(e.pos + Vector3.up, SimConfig.MeleeRadius);

                if (e.aiState == AiState.Windup || e.aiState == AiState.Active)
                {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
                    Gizmos.DrawWireSphere(e.pos, SimConfig.MeleeAttackRange);
                }
            }
        }

        static Color StateColor(AiState s)
        {
            switch (s)
            {
                case AiState.Idle:     return Color.gray;
                case AiState.Chase:    return Color.yellow;
                case AiState.Windup:   return new Color(1f, 0.5f, 0f);
                case AiState.Active:   return Color.red;
                case AiState.Recovery: return Color.magenta;
                default:               return Color.white;
            }
        }
    }
}
