using UnityEngine;
using Unity.Cinemachine;
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
        public CinemachineCamera GameplayVcam => gameplayVcam;   // 연출(FOV킥·Impulse)이 vcam을 건드리게

        // >>> [예측 세션 추가, 2026-07-18] Services/SpawnEnemyNear/ClearAllEnemies는 원래 없던
        // 접근자다. 예측 쪽(PredictionPreview.cs, RealRoutePreview.cs)이 실제 SimServices와
        // 디버그용 적 스폰/제거가 필요해서 추가함 — 전부 읽기 전용이거나 디버그 전용이라
        // 기존 게임 루프 동작에는 영향 없음.
        public SimServices Services => graphServices;   // 예측/following = 그래프(포크·결정론)

        /// <summary>디버그용: 지정 위치 근처에 적 1마리 소환(예측 미리보기 시나리오 설정용).</summary>
        public void SpawnEnemyNear(Vector3 pos) => world.AddEnemy(pos);

        /// <summary>디버그용: 살아있는 적 전부 제거(예측 미리보기 시나리오 리셋용).</summary>
        public void ClearAllEnemies() => world.enemyCount = 0;
        // <<< [예측 세션 추가 끝]

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
        SimServices services;        // 평상시 = 런타임 NavMesh
        SimServices graphServices;   // 예측 검색·following 재생 = 고정 그래프
        Camera cam;
        CinemachineCamera gameplayVcam;   // 1인칭·예측·컷신 모두 이 vcam pose에 씀 → Brain이 실제 카메라 구동(+Impulse)
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
            // 하이브리드: 평상시 = 런타임 NavMesh(연속 경로·나비 매끄러움).
            // 예측 검색·following 재생 = 고정 그래프(포크·결정론). 둘 다 EnemyMovement가 그대로 씀.
            var collision = new PhysicsCollision(Physics.DefaultRaycastLayers);
            services      = new SimServices(collision, new NavMeshPathfinder());
            graphServices = new SimServices(collision, GraphPathfinder.CreateArena());

            world = SimWorld.Create();
            world.player = PlayerSim.Spawn(map.playerSpawn);
            spawnPoints = map.spawns;
            spawnTimer = SimConfig.SpawnIntervalTicks;   // 첫 틱부터 소환 시작

            prevWorld = Snapshot.Clone(in world);

            views.Init();
            SetupCamera();
            prediction.Init(cam, gameplayVcam.transform);
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
                if (gameplayVcam != null && views.PlayerAnchor != null)
                {
                    // 실제 카메라가 아니라 vcam pose에 쓴다 — Brain이 이걸 따라가며 Impulse(쉐이킹)를 얹는다.
                    gameplayVcam.transform.position = views.PlayerAnchor.position + Vector3.up * eyeHeight;
                    gameplayVcam.transform.rotation = Quaternion.Euler(input.Pitch, input.Yaw, 0f);
                }
                if (input.EscapePressed()) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            }
        }

        void FixedUpdate()
        {
            // >>> [예측 세션 변경, 2026-07-18] 원래 코드는 아래 두 줄이었다:
            //   if (prediction.Frozen) return;   // 예측 정지 중엔 sim·소환 멈춤
            //   prevWorld = Snapshot.Clone(in world);
            //   InputCmd cmd = input.Consume();
            // 즉 예측 관련 상태(Preview든 Following이든)면 통째로 멈췄었다. 확정한 경로를
            // 실제 플레이어가 자동으로 수행하게 하려면(Following) 시뮬레이션을 계속 돌리되
            // 실시간 입력 대신 기록된 입력을 넣어야 해서, Preview/Following을 분리했다.
            // 되돌리려면: 아래 if/else 블록을 지우고 위 두 줄로 교체 + SpawnTick() 조건도 제거.
            if (prediction.state == PredictionController.State.Preview) return;   // 미리보기 중엔 정지

            InputCmd cmd;
            if (prediction.state == PredictionController.State.Following)
            {
                // 확정된 예측 경로를 실제로 재생 — 기록된 입력을 그대로 넣는다. 재생이 끝나면
                // TryConsumeFollowingInput이 false를 주면서 자동으로 Idle로 돌아간다.
                if (!prediction.TryConsumeFollowingInput(out cmd)) return;
            }
            else if (ConsoleOpen)
            {
                cmd = InputCmd.Empty;
                cmd.yaw = input.Yaw;
                cmd.pitch = input.Pitch;
            }
            else
            {
                cmd = input.Consume();
            }

            prevWorld = Snapshot.Clone(in world);
            // following(확정 경로 재생)은 예측과 동일하게 그래프로 돌려야 예측 결과와 일치. 평소는 NavMesh.
            SimServices step = prediction.state == PredictionController.State.Following ? graphServices : services;
            SimStep.Run(ref world, in cmd, in step);
            fixedAccum = 0f;

            // 확정 경로 재생 중엔 예측이 가정한 대로(spawnLocked) 새 적 소환을 잠근다 —
            // 아니면 예측이 못 본 적이 재생 중에 끼어들어 결과가 어긋난다.
            if (prediction.state != PredictionController.State.Following)
                SpawnTick();
            // <<< [예측 세션 변경 끝]

            // 고정 그래프 층이동 시작 감지
            for (int i = 0; i < world.enemyCount; i++)
            {
                if (prevWorld.enemyCount > i &&
                    prevWorld.enemies[i].traversalPhase == TraversalPhase.None &&
                    world.enemies[i].traversalPhase == TraversalPhase.Pause)
                    Debug.Log($"[층이동] 적 {i} {world.enemies[i].activeMoveKind} 시작 (tick {world.tick})");
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

            // Cinemachine: Brain이 vcam pose를 따라 실제 카메라를 움직인다. 1인칭·예측·컷신을 vcam으로 통일.
            if (cam.GetComponent<CinemachineBrain>() == null) cam.gameObject.AddComponent<CinemachineBrain>();
            var vgo = new GameObject("GameplayVCam");
            gameplayVcam = vgo.AddComponent<CinemachineCamera>();
            vgo.AddComponent<CinemachineImpulseListener>();   // 타격 쉐이킹(Impulse) 수신 — 발생은 Phase 3
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
