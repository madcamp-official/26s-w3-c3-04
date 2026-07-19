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
        public void SetLookPitch(float pitch) => input.Pitch = pitch;
        public float LookYaw   => input.Yaw;
        public float LookPitch => input.Pitch;

        SimWorld world, prevWorld;
        readonly InputReader input = new InputReader();
        readonly EntityViews views = new EntityViews();
        readonly PredictionController prediction = new PredictionController();
        SimServices services;
        Camera cam;
        DevConsole console;
        MapSpawnConfig spawnConfig;   // 씬에 있으면 그 맵의 스폰지점·종류 세팅을 사용
        float fixedAccum;

        System.Collections.Generic.List<Vector3> spawnPoints;
        int spawnTimer, nextSpawn;

        // 개발 콘솔 훅
        public bool AutoSpawn = true;                 // 기본 on. 씬 세팅 있으면 그 값으로 덮음
        const float DevSpawnDistance = 6f;            // 콘솔 소환 위치 = 플레이어 정면 이 거리
        bool ConsoleOpen => console != null && console.IsOpen;

        void Start()
        {
            Instance = this;
            Time.fixedDeltaTime = SimConfig.TickDelta;

            Vector3 refPoint = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

            MapResult map = useSceneGeometry ? MapBuilder.BuildFromScene(refPoint) : MapBuilder.BuildCubes();
            // NavMesh 복귀: 런타임 NavMesh 길찾기(연속 메시·기둥 우회). 얇은 다리 낙하는
            // 매 틱 navmesh 되당김(ClampToNavMesh)으로 방지. 노드 그래프는 은퇴 예정(Phase 3).
            IPathfinder pathfinder = new NavMeshPathfinder();
            services = new SimServices(new PhysicsCollision(Physics.DefaultRaycastLayers), pathfinder);

            world = SimWorld.Create();
            world.player = PlayerSim.Spawn(map.playerSpawn);
            spawnPoints = map.spawns;
            spawnTimer = SimConfig.SpawnIntervalTicks;   // 첫 틱부터 소환 시작

            prevWorld = Snapshot.Clone(in world);

            views.Init();
            SetupCamera();
            prediction.Init(cam);
            console = gameObject.AddComponent<DevConsole>();   // ` 개발 콘솔(몹 소환 등)
            ReloadSpawnConfig();   // 씬에 MapSpawnConfig 있으면 그 맵의 스폰 세팅 채택
            input.Yaw = 180f;   // 남쪽(아레나) 바라봄
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Debug.Log($"[Main] 시작. 스폰지점 {spawnPoints.Count}개, {SimConfig.SpawnIntervalTicks}틱마다 소환(최대 {SimConfig.SpawnCap}).\n" +
                      "  WASD 이동 · 마우스 시점 · Space 더블점프 · Shift+WASD 4방향 대시 · 좌클릭 평타 · 우클릭 런지 · F 예측 · F1 튜닝 · Esc 커서");
        }

        void Update()
        {
            // 콘솔 열림 중엔 게임 입력·시점 정지(sim은 계속 돌아 소환한 몹 관찰 가능)
            if (!prediction.Frozen && !ConsoleOpen) input.PollFrame();

            prediction.Tick(in world);   // 정지 아닐 때: F 감시 / 정지 중: 루트 표시·탑다운 카메라

            fixedAccum += Time.deltaTime;
            float alpha = Mathf.Clamp01(fixedAccum / Time.fixedDeltaTime);
            views.Sync(in world, in prevWorld, alpha);

            // 정지 중엔 예측 컨트롤러가 카메라를 잡는다(탑다운). 아닐 때만 1인칭. 콘솔 중엔 시점 고정.
            if (!prediction.Frozen && !ConsoleOpen)
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
            // 콘솔 열림 중엔 플레이어 입력 무시(시점만 유지) — 몹은 계속 움직임
            InputCmd cmd;
            if (ConsoleOpen) { cmd = InputCmd.Empty; cmd.yaw = input.Yaw; cmd.pitch = input.Pitch; }
            else cmd = input.Consume();
            SimStep.Run(ref world, in cmd, in services);
            fixedAccum = 0f;

            SpawnTick();

            // 절벽 도약 시작 감지 (off-mesh link 큰 낙차 → Leaping 진입)
            for (int i = 0; i < world.enemyCount; i++)
            {
                if (prevWorld.enemyCount > i &&
                    prevWorld.enemies[i].descentPhase == DescentPhase.None &&
                    world.enemies[i].descentPhase == DescentPhase.Leaping)
                    Debug.Log($"[도약] 적 {i} 절벽 도약 (tick {world.tick})");
            }
        }

        /// <summary>
        /// 일정 간격으로 스폰. 씬 세팅(MapSpawnConfig)이 있으면 지점별 지정 종류로,
        /// 없으면 맵 기본 스폰지점 + 3축 분포(폴백)로 순번대로 한 마리씩(최대치까지).
        /// </summary>
        void SpawnTick()
        {
            if (!AutoSpawn) return;   // 콘솔에서 autospawn off 하면 멈춤

            int interval = spawnConfig != null ? spawnConfig.intervalTicks : SimConfig.SpawnIntervalTicks;
            int cap      = spawnConfig != null ? spawnConfig.cap           : SimConfig.SpawnCap;

            spawnTimer++;
            if (spawnTimer < interval) return;
            if (world.AliveCount() >= cap) return;   // 죽은 적 제외 → 처치하면 재스폰
            spawnTimer = 0;

            if (spawnConfig != null && spawnConfig.entries != null && spawnConfig.entries.Length > 0)
            {
                SpawnEntry e = spawnConfig.entries[nextSpawn % spawnConfig.entries.Length];
                nextSpawn++;
                if (e.point == null) return;
                var (c, m, s) = MapSpawnConfig.Axes(e.kind);
                world.AddEnemy(e.point.position, c, m, s);   // 지점별 지정 종류
            }
            else if (spawnPoints != null && spawnPoints.Count > 0)
            {
                world.AddEnemy(spawnPoints[nextSpawn % spawnPoints.Count]);   // 폴백: 3축 분포
                nextSpawn++;
            }
        }

        /// <summary>씬의 MapSpawnConfig를 다시 찾아 채택(Inspector 수정 후 콘솔 reload용).</summary>
        public void ReloadSpawnConfig()
        {
            spawnConfig = FindFirstObjectByType<MapSpawnConfig>();
            if (spawnConfig != null) AutoSpawn = spawnConfig.autoSpawnOnStart;
        }

        // ── 개발 콘솔 훅 ──
        /// <summary>플레이어 정면에 지정 조합 몹 한 마리 소환.</summary>
        public void DevSpawn(CombatType combat, MobilityType mobility, SizeClass size)
        {
            float yr = input.Yaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
            Vector3 at = world.player.pos + fwd * DevSpawnDistance;
            world.AddEnemy(at, combat, mobility, size);
        }

        public void DevClear() => world.DevClearEnemies();
        public int AliveEnemyCount() => world.AliveCount();

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.nearClipPlane = 0.1f;   // 벽면 최소거리(≈0.25) 안쪽 → 벽에 붙어도 뒤가 안 잘림
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
