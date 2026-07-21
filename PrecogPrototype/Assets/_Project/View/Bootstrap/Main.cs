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
        [SerializeField] float eyeHeight = 1.25f;   // 키 1.4375(1.15×1.25)에 맞춰 시점 상향
        [SerializeField] float gameplayFov = 0f;   // 0=씬 카메라 FOV 상속, >0=강제(둠식 넓은 시야는 90~100)
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

        /// <summary>웨이브 런타임용: 지정 위치·조합으로 스폰하고 부여된 적 id를 돌려준다(-1 = 실패).</summary>
        public int SpawnEnemyAt(Vector3 pos, CombatType combat, MobilityType mobility, SizeClass size)
        {
            int id = world.nextEnemyId;   // AddEnemy가 이 값을 쓰고 증가시킨다
            return world.AddEnemy(pos, combat, mobility, size) ? id : -1;
        }

        /// <summary>
        /// 웨이브 배관용: 스폰과 동시에 **펄스**(초기 속도)를 주고 발사 상태로 만든다(설계 §4).
        /// 발사 중엔 AI·공격이 멈추고 탄도로 날아가며, 지상몹은 착지 시 · 공중몹은 타이머로 해제된다.
        /// </summary>
        public int SpawnEnemyLaunched(Vector3 pos, CombatType combat, MobilityType mobility, SizeClass size,
                                      Vector3 launchVel)
        {
            int id = SpawnEnemyAt(pos, combat, mobility, size);
            if (id < 0) return -1;
            for (int i = 0; i < world.enemyCount; i++)
                if (world.enemies[i].id == id)
                {
                    world.enemies[i].vel = launchVel;
                    world.enemies[i].launchTicks = 1;   // >0 = 발사 중
                    world.enemies[i].grounded = false;
                    break;
                }
            return id;
        }

        /// <summary>주어진 id들 중 살아있는 적 수 — 웨이브별 생존 카운트용(Sim 수정 없이 웨이브 소속 추적).</summary>
        public int AliveCountAmong(System.Collections.Generic.HashSet<int> ids)
        {
            if (ids == null || ids.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < world.enemyCount; i++)
                if (world.enemies[i].alive && ids.Contains(world.enemies[i].id)) n++;
            return n;
        }
        // <<< [예측 세션 추가 끝]

        // 화면연출(칼등치기 카메라 고정)이 끝날 때 최종 시선을 되돌려 써서 원복 방지.
        // yaw는 sim 입력(cmd.yaw)에도 쓰이므로 여기 하나로 시점·조준이 동기화된다.
        public void SetLookYaw(float yaw) => input.Yaw = yaw;
        public void SetLookPitch(float pitch) => input.Pitch = pitch;
        public float LookYaw   => input.Yaw;
        public float LookPitch => input.Pitch;
        public void SetPredictionSpawnLocked(bool locked) => world.spawnLocked = locked;

        /// <summary>컷신 종료 시 플레이어를 현재 시선(yaw) 정면으로 dist만큼 이동(봉인 박스 탈출).
        /// 스크립트 텔레포트라 결정론과 무관(예측 중엔 컷신을 트리거하지 않음). prevWorld도 맞춰 보간 튐 방지.</summary>
        public void AdvancePlayerForward(float dist)
        {
            float yr = input.Yaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
            world.player.pos += fwd * dist;
            prevWorld = Snapshot.Clone(in world);
        }

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
            ArenaMapBake predictionMap = map.predictionMap;
            if (predictionMap == null || predictionMap.nodes == null || predictionMap.nodes.Length == 0)
            {
                predictionMap = GraphPathfinder.CreateArenaBake();
                Debug.LogWarning("[Prediction] ArenaMapAuthoring이 없어 내장 아레나 그래프를 사용합니다. 씬 지형 변경 시 authoring과 mapVersion을 갱신하세요.");
            }

            // 하이브리드: 평상시 = 런타임 NavMesh(연속 경로·나비 매끄러움).
            // 예측 검색·following 재생 = 고정 그래프(포크·결정론). 둘 다 EnemyMovement가 그대로 씀.
            var collision = new PhysicsCollision(Physics.DefaultRaycastLayers);
            var navPathfinder = new NavMeshPathfinder();
            ValidatePredictionMapAgainstNavMesh(predictionMap, navPathfinder);

            // 층이동 마커 Bake — 씬의 TraversalLink를 NavMeshLink(평상시) + 그래프 링크(예측)로 굽는다.
            // 같은 소스에서 양쪽을 만들기 때문에 평상시·예측의 층이동이 구조적으로 어긋나지 않는다.
            var markers = FindObjectsByType<TraversalLink>(FindObjectsSortMode.None);

            // 예측 그래프: 이 씬용으로 구운 에셋이 있으면 그걸 쓰고, 없으면 코드 그래프로 폴백.
            // 폴백 덕분에 SampleScene(하드코딩 그래프)은 굽지 않아도 그대로 동작한다.
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var graphAsset = Resources.Load<PredictionGraphAsset>(PredictionGraphAsset.ResourceName(sceneName));
            GraphPathfinder graphPathfinder;
            if (graphAsset != null && graphAsset.HasData)
            {
                graphPathfinder = GraphPathfinder.FromBake(graphAsset.ToBake());
                Debug.Log($"[예측 그래프] '{sceneName}' 구운 그래프 사용 — 노드 {graphAsset.nodeCount} · 링크 {graphAsset.linkCount} " +
                          $"(구운 시각 {graphAsset.bakedAt}, 마커 {graphAsset.markerCount}개)");
            }
            else
            {
                graphPathfinder = GraphPathfinder.CreateArena();
                Debug.LogWarning($"[예측 그래프] '{sceneName}'용 구운 그래프가 없어 코드 그래프(SampleScene 전용)로 폴백합니다.\n" +
                                 "  이 씬에서 예측을 쓰려면 Tools/층이동 링크/예측 그래프 굽기 를 실행하십시오.");
            }
            if (markers.Length > 0)
            {
                var baked = TraversalBaker.Bake(markers, System.Array.Empty<ArenaNavNode>());
                navPathfinder.links = baked.ToArray();   // 코너 좌표 매칭으로 진입 판정
                Debug.Log($"[층이동] 마커 {markers.Length}개 → 링크 {baked.Count}개 구움");
            }

            services      = new SimServices(collision, navPathfinder);
            graphServices = new SimServices(collision, graphPathfinder);

            world = SimWorld.Create();
            world.mapVersion = predictionMap.mapVersion;
            world.player = PlayerSim.Spawn(map.playerSpawn);
            spawnPoints = map.spawns;
            spawnTimer = SimConfig.SpawnIntervalTicks;   // 첫 틱부터 소환 시작

            prevWorld = Snapshot.Clone(in world);

            views.Init();
            SetupCamera();
            prediction.Init(cam, gameplayVcam.transform);
            console = gameObject.AddComponent<DevConsole>();   // ` 개발 콘솔(몹 소환 등)
            ReloadSpawnConfig();   // 씬에 MapSpawnConfig 있으면 그 맵의 스폰 세팅 채택
            input.Yaw = map.playerYaw;   // 씬의 PlayerSpawnPoint 방향(없으면 남쪽 180)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Debug.Log($"[Main] 시작. 스폰지점 {spawnPoints.Count}개, {SimConfig.SpawnIntervalTicks}틱마다 소환(최대 {SimConfig.SpawnCap}).\n" +
                      "  WASD 이동 · 마우스 시점 · Space 더블점프 · Shift+WASD 4방향 대시 · 좌클릭 평타 · 우클릭 런지 · F 예측 · F1 튜닝 · Esc 커서");
        }

        static void ValidatePredictionMapAgainstNavMesh(ArenaMapBake bake, NavMeshPathfinder runtimePathfinder)
        {
            int offMesh = 0;
            for (int i = 0; i < bake.nodes.Length; i++)
            {
                Vector3 authored = bake.nodes[i].position;
                if (!runtimePathfinder.ClampToWalkable(authored, 1.5f, out Vector3 sampled) ||
                    Vector3.Distance(authored, sampled) > 1.5f)
                    offMesh++;
            }
            if (offMesh > 0)
                Debug.LogWarning($"[Prediction] mapVersion {bake.mapVersion}: 그래프 노드 {offMesh}/{bake.nodes.Length}개가 런타임 NavMesh와 맞지 않습니다. ArenaMapAuthoring을 다시 베이크하세요.");
        }

        void Update()
        {
            // 콘솔 열림 중엔 게임 입력·시점 정지(sim은 계속 돌아 소환한 몹 관찰 가능). 컷신 중엔 조작 잠금.
            if (!prediction.Frozen && !ConsoleOpen && !Cutscene.Active) input.PollFrame();

            // 정지 아닐 때: F 감시 / 정지 중: 루트 표시·탑다운 카메라.
            // 콘솔에 타이핑 중이면 입력만 차단(표시·카메라는 계속) — 'f' 타이핑에 예지가 발동하던 버그.
            prediction.Tick(in world, ConsoleOpen);

            fixedAccum += Time.deltaTime;
            float alpha = Mathf.Clamp01(fixedAccum / Time.fixedDeltaTime);
            views.Sync(in world, in prevWorld, alpha);
            if (prediction.state == PredictionController.State.Following
                && views.PlayerAnchor != null)
                prediction.UpdateFollowingCameraRenderPose(
                    views.PlayerAnchor.position, views.PlayerAnchor.eulerAngles.y);

            // 정지 중엔 예측 컨트롤러가 카메라를 잡는다(탑다운). 아닐 때만 1인칭. 콘솔·컷신 중엔 시점 고정
            // (컷신 중엔 CinemachineTrack이 컷신 vcam을 잡으므로 게임플레이 vcam pose를 덮지 않는다).
            if (!prediction.Frozen && !ConsoleOpen && !Cutscene.Active)
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

        void OnGUI()
        {
            int previousDepth = GUI.depth;
            GUI.depth = -200;
            try
            {
                prediction.DrawRhythmHud();
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        void OnDisable()
        {
            prediction.RestoreNormalTimeScale();
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

            // 컷신 중엔 sim 완전 정지(플레이어 이동·공격·소환 불가). 카메라는 Timeline이 잡는다.
            if (Cutscene.Active) return;

            // 히트스톱(A안): 얼린 틱만큼 sim 전진을 건너뛴다(입력 소비 전에 return → 기록 입력 보존).
            // timeScale은 건드리지 않으므로 뷰 연출(파티클·셰이크·화면효과)은 계속 재생된다.
            if (HitStop.FrozenTicks > 0) { HitStop.FrozenTicks--; return; }

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
            if (prediction.state == PredictionController.State.Following)
                prediction.AfterFollowingStep();
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
            int configuredCap = spawnConfig != null ? spawnConfig.cap : SimConfig.SpawnCap;
            int cap = Mathf.Min(configuredCap, SimConfig.SpawnCap);

            spawnTimer++;
            if (spawnTimer < interval) return;
            if (world.AliveCount() >= cap) return;   // 죽은 적 제외 → 처치하면 재스폰
            spawnTimer = 0;

            if (spawnConfig != null && spawnConfig.entries != null && spawnConfig.entries.Length > 0)
            {
                SpawnEntry e = spawnConfig.entries[nextSpawn % spawnConfig.entries.Length];
                var (c, m, s) = SimWorld.ExperimentalAutoSpawn(nextSpawn);
                nextSpawn++;
                if (e.point == null) return;
                world.AddEnemy(e.point.position, c, m, s);   // 실험 분포, entries는 위치만 사용
            }
            else if (spawnPoints != null && spawnPoints.Count > 0)
            {
                var (c, m, s) = SimWorld.ExperimentalAutoSpawn(nextSpawn);
                world.AddEnemy(spawnPoints[nextSpawn % spawnPoints.Count], c, m, s);
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
            if (world.AliveCount() >= SimConfig.SpawnCap)
            {
                Debug.LogWarning($"[Spawn] 동시 몬스터 상한 {SimConfig.SpawnCap}마리 — 추가 소환을 거부합니다.");
                return;
            }
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
            var brain = cam.GetComponent<CinemachineBrain>() ?? cam.gameObject.AddComponent<CinemachineBrain>();
            // FPS 게임플레이 카메라는 블렌드 금지 → 진입/전환 시 즉시 컷(잠깐 확대돼 보이는 블렌드 인 제거).
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

            var vgo = new GameObject("GameplayVCam");
            gameplayVcam = vgo.AddComponent<CinemachineCamera>();
            var lens = LensSettings.FromCamera(cam);            // 원래 카메라 렌즈(FOV·클립) 상속 → 이관 후 화면 변화 방지
            if (gameplayFov > 0f) lens.FieldOfView = gameplayFov;
            gameplayVcam.Lens = lens;
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
