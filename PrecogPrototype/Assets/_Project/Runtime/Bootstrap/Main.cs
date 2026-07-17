using UnityEngine;
using Game.Simulation;

namespace Game.Runtime
{
    /// <summary>
    /// 프로토타입의 심장. 빈 GameObject에 이 컴포넌트 하나만 붙이면 동작한다.
    ///
    /// - Start:       증명 3종 실행(콘솔) + 월드/뷰 생성 + 바닥·카메라 세팅
    /// - Update:      입력 버퍼링, 뷰 동기화(보간)
    /// - FixedUpdate: 60Hz로 Kernel.Step 호출 (게임 로직은 여기서만 진행)
    ///
    /// Sim(진짜 상태)과 View(거울)를 잇는다. Sim은 여기 없으면 그냥 데이터다.
    /// </summary>
    public class Main : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] int   enemyCount = 8;
        [SerializeField] bool  runProofsOnStart = true;
        [SerializeField] bool  drawGizmos = true;

        SimWorld world;
        SimWorld prevWorld;   // 렌더 보간용 (이전 틱)
        readonly InputReader input = new InputReader();
        readonly EntityViews views = new EntityViews();

        float fixedAccumTime;   // 마지막 FixedUpdate 이후 경과 (보간 alpha 계산용)

        void Start()
        {
            // 시뮬레이션 틱 주기를 60Hz로 맞춘다 (SimConfig.TickDelta와 일치)
            Time.fixedDeltaTime = SimConfig.TickDelta;

            if (runProofsOnStart)
                ProofRunner.RunAll();

            // 월드 구성
            world = SimWorld.Create();
            world.player = PlayerState.Spawn(Vector3.zero);
            for (int i = 0; i < enemyCount; i++)
            {
                float ang = (float)i / Mathf.Max(1, enemyCount) * Mathf.PI * 2f;
                world.AddMeleeEnemy(new Vector3(Mathf.Cos(ang) * 10f, 0f,
                                                Mathf.Sin(ang) * 10f));
            }
            prevWorld = Snapshot.Clone(in world);

            BuildEnvironment();
            views.Init();

            Debug.Log($"[Main] 라이브 시뮬레이션 시작. 적 {enemyCount}마리. " +
                      $"WASD 이동 / 마우스 회전 / Space 점프. HP={world.player.hp}");
        }

        void Update()
        {
            input.PollFrame();

            // 보간 alpha = 다음 틱까지 얼마나 진행했나
            fixedAccumTime += Time.deltaTime;
            float alpha = Mathf.Clamp01(fixedAccumTime / Time.fixedDeltaTime);
            views.Sync(in world, alpha, in prevWorld);
        }

        void FixedUpdate()
        {
            prevWorld = Snapshot.Clone(in world);
            InputCmd cmd = input.Consume();

            bool wasAlive = world.player.alive;
            Kernel.Step(ref world, in cmd);
            fixedAccumTime = 0f;

            if (wasAlive && !world.player.alive)
                Debug.Log($"[Main] 플레이어 사망 (tick={world.tick})");
        }

        void BuildEnvironment()
        {
            // 바닥
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(5f, 1f, 5f);

            // 카메라 (1인칭 근사 — 플레이어 뷰가 없으니 위에서 비스듬히)
            if (Camera.main == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                camGo.transform.position = new Vector3(0f, 18f, -14f);
                camGo.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            }

            // 광원
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
            if (world.enemies == null) return;   // Start 전 방어

            // 플레이어 판정 구
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(world.player.pos + Vector3.up, SimConfig.PlayerRadius);

            // 적 판정 구 + 공격 사거리 + 상태색
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemyState e = ref world.enemies[i];
                if (!e.alive) continue;

                Gizmos.color = StateColor(e.aiState);
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
