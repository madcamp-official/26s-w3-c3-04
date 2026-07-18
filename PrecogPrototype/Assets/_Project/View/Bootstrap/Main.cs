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

        // 전투 뷰(HUD·카메라고정·히트스톱)가 sim 상태를 읽는 최소 접근자 (읽기 전용)
        public static Main Instance { get; private set; }
        public ref readonly SimWorld World => ref world;
        public Camera Cam => cam;

        // 화면연출(칼등치기 카메라 고정)이 끝날 때 최종 시선을 되돌려 써서 원복 방지.
        // yaw는 sim 입력(cmd.yaw)에도 쓰이므로 여기 하나로 시점·조준이 동기화된다.
        public void SetLookYaw(float yaw) => input.Yaw = yaw;
        public float LookYaw   => input.Yaw;
        public float LookPitch => input.Pitch;

        SimWorld world, prevWorld;
        readonly InputReader input = new InputReader();
        readonly EntityViews views = new EntityViews();
        readonly PredictionController prediction = new PredictionController();
        SimServices services;
        Camera cam;
        float fixedAccum;

        System.Collections.Generic.List<Vector3> spawnPoints;
        int spawnTimer, nextSpawn;

        void Start()
        {
            Instance = this;
            Time.fixedDeltaTime = SimConfig.TickDelta;

            Vector3 refPoint = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

            MapResult map = useSceneGeometry ? MapBuilder.BuildFromScene(refPoint) : MapBuilder.BuildCubes();
            services = new SimServices(new PhysicsCollision(Physics.DefaultRaycastLayers),
                                       new NavMeshPathfinder(map.drops));

            world = SimWorld.Create();
            world.player = PlayerSim.Spawn(map.playerSpawn);
            spawnPoints = map.spawns;
            spawnTimer = SimConfig.SpawnIntervalTicks;   // 첫 틱부터 소환 시작

            prevWorld = Snapshot.Clone(in world);

            views.Init();
            SetupCamera();
            prediction.Init(cam);
            input.Yaw = 180f;   // 남쪽(아레나) 바라봄
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Debug.Log($"[Main] 시작. 스폰지점 {spawnPoints.Count}개, {SimConfig.SpawnIntervalTicks}틱마다 소환(최대 {SimConfig.SpawnCap}).\n" +
                      "  WASD 이동 · 마우스 시점 · Space 더블점프 · Shift+WASD 4방향 대시 · 좌클릭 평타 · 우클릭 런지 · F 예측 · F1 튜닝 · Esc 커서");
        }

        void Update()
        {
            if (!prediction.Frozen) input.PollFrame();

            prediction.Tick(in world);   // 정지 아닐 때: F 감시 / 정지 중: 루트 표시·탑다운 카메라

            fixedAccum += Time.deltaTime;
            float alpha = Mathf.Clamp01(fixedAccum / Time.fixedDeltaTime);
            views.Sync(in world, in prevWorld, alpha);

            // 정지 중엔 예측 컨트롤러가 카메라를 잡는다(탑다운). 아닐 때만 1인칭.
            if (!prediction.Frozen)
            {
                if (cam != null && views.PlayerAnchor != null)
                {
                    cam.transform.position = views.PlayerAnchor.position + Vector3.up * eyeHeight;
                    cam.transform.rotation = Quaternion.Euler(input.Pitch, input.Yaw, 0f);
                }
                if (input.EscapePressed()) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            }
        }

        void FixedUpdate()
        {
            if (prediction.Frozen) return;   // 예측 정지 중엔 sim·소환 멈춤

            prevWorld = Snapshot.Clone(in world);
            InputCmd cmd = input.Consume();
            SimStep.Run(ref world, in cmd, in services);
            fixedAccum = 0f;

            SpawnTick();

            // 하강 결정 감지
            for (int i = 0; i < world.enemyCount; i++)
            {
                if (prevWorld.enemyCount > i &&
                    prevWorld.enemies[i].descentPhase == DescentPhase.None &&
                    world.enemies[i].descentPhase == DescentPhase.ApproachEdge)
                    Debug.Log($"[하강] 적 {i} 하강 결정(걷기보다 점프가 짧음) (tick {world.tick})");
            }
        }

        /// <summary>일정 간격으로 지정 스폰 지점에서 순번대로 한 마리씩 (최대치까지).</summary>
        void SpawnTick()
        {
            if (spawnPoints == null || spawnPoints.Count == 0) return;
            spawnTimer++;
            if (spawnTimer < SimConfig.SpawnIntervalTicks) return;
            if (world.AliveCount() >= SimConfig.SpawnCap) return;   // 죽은 적 제외 → 처치하면 재스폰
            spawnTimer = 0;
            world.AddEnemy(spawnPoints[nextSpawn % spawnPoints.Count]);
            nextSpawn++;
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
