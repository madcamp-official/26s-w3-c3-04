using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [예측 세션 대폭 수정, 2026-07-18] 원래 이 파일은 core-controls(KJH)가 만든 버전으로,
    // RoutePreviewStub(같은 폴더, 지금도 그대로 남아있음)이 만든 가짜 루트(근접순/원거리순/
    // 스윕 휴리스틱, 이동만)를 보여주고 확정 시 그냥 로그만 찍고 닫혔다. 이번 세션에 실제
    // Game.Prediction 결과 연결 + Following(1인칭 실제 자동실행) + 정지/액션 잔상을 새로
    // 넣으면서 상당 부분을 고쳤다 — "원래 어땠는지"는 git에서 `git show ecb4d5b:PrecogPrototype/Assets/_Project/View/Prediction/PredictionController.cs`
    // 로 보거나, 같은 폴더의 RoutePreviewStub.cs(안 지움, 지금은 안 씀)를 참고하면 된다.
    // <<< [예측 세션 대폭 수정 — 아래 전체]
    /// <summary>
    /// 예측(예지) 연출 컨트롤러 — View 전용, 예측 봇과 독립.
    /// F: 정지 진입(시간 멈춤+흑백+3인칭) / 진입 중 F: 강조 후보 전환(모든 후보는 진입과
    /// 동시에 함께 애니메이션됨) / 마우스: 궤도 회전 / 좌클릭: 확정 / Esc: 취소.
    /// 확정하면 실제 플레이어가 기록된 궤적을 따라가되 액션 잔상마다 사용자가 직접 입력하고
    /// (Main.FixedUpdate가 TryConsumeFollowingInput을 통해 구동), 카메라는 1인칭으로 그
    /// 실제 위치·시선을 따라간다(Following). Preview 중 경로는 이동하며 페이드되는 트레일로
    /// 보여주고, 실제 행동이 시작되는 ActionEvent 틱마다 정지 잔상을 겹쳐 찍는다(고정 간격
    /// 아님 — 2026-07-20 변경, PREDICTION_CONTRACT.md §3.1.1의 "30틱 간격/선택 후보만" 서술과는
    /// 다름). 액션 이벤트는 ±3틱 Perfect, ±8틱 Good이며 미입력은 Miss로 직접 조작 전환한다.
    /// </summary>
    public class PredictionController
    {
        public enum State { Idle, Preview, Following }
        public State state = State.Idle;
        public bool Frozen => state != State.Idle;

        Camera cam;
        Camera accentCamera;
        const int PredictionAccentLayer = 30;
        Transform camPose;   // Cinemachine vcam pose(여기 쓰면 Brain이 실제 카메라를 따라감). fx·칼 토글엔 cam 사용.
        readonly FreezeFx fx = new FreezeFx();

        List<PredictedRoute> routes = new List<PredictedRoute>();
        int selected;
        float orbitYaw, orbitPitch;   // 미리보기 3인칭 궤도(마우스)

        InputCmd[] followingControls;
        int followingIndex;
        RhythmJudge rhythmJudge;
        struct TimedRhythmInput
        {
            public PredictedActionType type;
            public float realTime;
        }

        readonly List<TimedRhythmInput> rhythmInputs = new List<TimedRhythmInput>(4);
        bool exitAfterFollowingStep;
        bool completedAfterFollowingStep;
        string rhythmFeedback = "";
        float rhythmFeedbackUntil;
        int rhythmSegmentEventIndex = -1;
        int rhythmSegmentStartTick;
        float rhythmSegmentStartRealTime;
        float rhythmSegmentDuration;
        float rhythmSegmentStartScale;
        float rhythmSegmentDecelStart;
        int rhythmWaitEventIndex = -1;
        float rhythmWaitStartRealTime;
        float cameraYawVelocity;
        PredictedRoute followingRoute;
        Texture2D rhythmRingTexture;
        Texture2D missGlitchTexture;
        float missGlitchStartedAt;
        float missGlitchUntil;
        readonly Dictionary<Renderer, Color> swordOriginalColors = new Dictionary<Renderer, Color>();
        readonly Dictionary<Transform, int> swordOriginalLayers = new Dictionary<Transform, int>();

        LineRenderer domeLr;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        readonly Gradient previewLineGradient = new Gradient();
        readonly GradientColorKey[] previewLineColorKeys = new GradientColorKey[4];
        readonly GradientAlphaKey[] previewLineAlphaKeys =
        {
            new GradientAlphaKey(PredictionConfig.RouteAlphaSel, 0f),
            new GradientAlphaKey(PredictionConfig.RouteAlphaSel, 1f),
        };
        // [예측 세션 수정, 2026-07-20] 이전엔 routes[selected] 하나만 채우는 단일 풀이라
        // Preview 중 후보를 F로 순환해야만 다음 후보가 애니메이션됐다("하나씩 나가는" 문제).
        // 경로별 풀로 바꿔서 Preview 중엔 모든 후보가 동시에 표시되게 한다. Following 중엔
        // 실행 중인 selected 경로만 채워진다(그 외 경로 인덱스는 need=0으로 비어있음).
        readonly List<List<Transform>> ghostMarksByRoute = new List<List<Transform>>();    // 액션 지점 정지 잔상(가독성용)
        Transform startMarker;   // 시작 위치(=나) 표시 캡슐
        readonly List<Transform> revealGhosts = new List<Transform>();       // 경로별 이동 트레일 헤드
        readonly List<List<Transform>> revealAfterimagesByRoute = new List<List<Transform>>();
        // [예측 세션 수정, 2026-07-20] 매 프레임 new Gradient()를 만들면 GC 압박으로 프레임이
        // 튀어 트레일이 "한 번에 나타나는" 것처럼 보인다 — 선택/비선택 그라디언트를 경로당
        // 한 번만 만들어 캐싱한다(색은 경로 인덱스에 고정이라 재계산할 필요가 없다).
        // 예측 진입(F) 순간 GameObject.CreatePrimitive를 경로 3개 분량 한꺼번에 호출하면
        // 그 한 프레임이 크게 늘어져 reveal 진행률이 "점프"해 보인다 — 게임 시작 시 미리
        // 채워두고, 이후엔 아래 need > PreWarmCount인 드문 경우에만 추가 생성한다.
        // [버그 수정, 2026-07-20] 10이었지만 Full 설정(macroDepth 12)의 실제 경로는
        // actionMarkers/ghostFrames가 13~16개까지 나와 매번 풀이 모자랐다 — Preview 진입
        // 직후 첫 프레임마다 추가 CreatePrimitive가 실행되며 그 비용이 실시간 기준인
        // reveal 타이머(PreviewRevealSeconds)를 갉아먹어 "렉 걸렸다가 팟 하고 경로가
        // 그냥 뜨는" 현상으로 보였다. macroDepth 최대(ForDuration 5초 ≈ 20스텝)까지
        // 여유를 두고 24로 올린다.
        const int PreWarmMarksPerRoute = 24;
        float previewRevealStartRealTime;
        float previewRevealProgress;

        public void Init(Camera camera, Transform pose)
        {
            cam = camera;
            camPose = pose;
            fx.Init();
            fx.EnableOnCamera(cam);
            SetupAccentCamera();
            domeLr = MakeDome();
            startMarker = MakeStartMarker();
            PreWarmRoutePools();
            SetVisible(false);
        }

        /// <summary>경로별 풀(고스트 마크, 이동 트레일)을 게임 시작 시점에 미리 만들어 둔다 —
        /// F를 눌러 Preview에 처음 들어가는 순간 CreatePrimitive 수십 번이 한 프레임에 몰려
        /// 프레임이 늘어지는 것을 막는다(트레일이 "점프"해 보이는 원인 중 하나).</summary>
        void PreWarmRoutePools()
        {
            int routeCount = PredictionConfig.RouteColors.Length;
            for (int ri = 0; ri < routeCount; ri++)
            {
                var ghostPool = new List<Transform>();
                for (int i = 0; i < PreWarmMarksPerRoute; i++)
                {
                    Transform g = MakeGhostMark(); g.gameObject.SetActive(false); ghostPool.Add(g);
                }
                ghostMarksByRoute.Add(ghostPool);

                if (ri != 0) continue; // 이동 잔상은 안전형 경로 하나만 생성한다.
                Transform ghost = MakeRevealGhost();
                ghost.gameObject.SetActive(false);
                revealGhosts.Add(ghost);
                var afterimages = new List<Transform>(PredictionConfig.PreviewAfterimageCount);
                for (int i = 0; i < PredictionConfig.PreviewAfterimageCount; i++)
                {
                    Transform afterimage = MakeRevealGhost();
                    afterimage.name = "PredictRevealAfterimage";
                    afterimage.gameObject.SetActive(false);
                    afterimages.Add(afterimage);
                }
                revealAfterimagesByRoute.Add(afterimages);
            }
        }

        void SetupAccentCamera()
        {
            if (cam == null || accentCamera != null) return;
            int accentMask = 1 << PredictionAccentLayer;
            cam.cullingMask &= ~accentMask;

            var go = new GameObject("PredictionAccentCamera");
            go.transform.SetParent(cam.transform, false);
            accentCamera = go.AddComponent<Camera>();
            accentCamera.CopyFrom(cam);
            accentCamera.cullingMask = accentMask;
            accentCamera.clearFlags = CameraClearFlags.Depth;
            accentCamera.depth = cam.depth + 1f;

            UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData overlayData = accentCamera.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            overlayData.renderPostProcessing = false;
            if (!baseData.cameraStack.Contains(accentCamera))
                baseData.cameraStack.Add(accentCamera);
        }

        /// <summary>Main.Update에서 매 프레임 호출.</summary>
        public void Tick(in SimWorld w)
        {
            fx.Update();
            UpdateRhythmTimeScale();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (state == State.Idle)
            {
                if (kb.fKey.wasPressedThisFrame) Enter(in w);
                return;
            }

            if (state == State.Following)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                { Exit(); return; }
                CaptureRhythmInputs(kb, mouse);

                // 실제 이동·전투는 Main.FixedUpdate가 TryConsumeFollowingInput으로 구동한다.
                // 여기서는 그 결과(실제로 움직인 w)를 카메라·잔상 표시에만 반영한다.
                UpdateGhostMarks(in w);
                return;
            }

            // ── Preview 중 ──
            if (kb.escapeKey.wasPressedThisFrame) { Exit(); return; }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) { Confirm(in w); return; }
            if (kb.fKey.wasPressedThisFrame && routes.Count > 0)
            {
                // [예측 세션 수정, 2026-07-20] 모든 후보가 이미 동시에 애니메이션 중이라,
                // F는 강조 대상만 바꾼다 — 예전처럼 reveal 타이머를 리셋하면 나머지 두
                // 후보까지 처음부터 다시 스윕해서 "하나씩 나가는" 것처럼 보인다.
                selected = (selected + 1) % routes.Count;
                LogSelected();
            }

            if (mouse != null)   // 마우스로 플레이어 중심 궤도 회전
            {
                Vector2 md = mouse.delta.ReadValue();
                orbitYaw += md.x * PredictionConfig.OrbitSens;
                orbitPitch = Mathf.Clamp(orbitPitch - md.y * PredictionConfig.OrbitSens,
                                         PredictionConfig.OrbitPitchMin, PredictionConfig.OrbitPitchMax);
            }

            Render(in w);
            PlaceCamera(in w);
        }

        void Enter(in SimWorld w)
        {
            routes = RealRoutePreview.Build(in w, Main.Instance.Services, PredictionConfig.RouteColors);
            if (routes.Count == 0)
            {
                state = State.Idle;
                fx.SetExecution(false);
                fx.SetActive(false);
                SetVisible(false);
                RestoreNormalTimeScale();
                Debug.LogWarning("[예측] 유효한 경로를 만들지 못해 미리보기를 취소했습니다.");
                return;
            }
            // PlanByProfile의 출력 순서나 일부 프로필의 재생 실패 여부와 무관하게
            // 공격형 대표 경로를 기본 강조/확정 대상으로 삼는다.
            selected = 0;
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i].profileLabel != "공격형") continue;
                selected = i;
                break;
            }
            previewRevealProgress = 0f;
            state = State.Preview;
            fx.SetExecution(false);
            fx.SetActive(true);
            CombatAudio.Prediction();   // 예지 발동음 (combat 오디오 훅)
            ToggleSword(false);         // 3인칭이라 1인칭 칼 숨김
            SetVisible(true);
            BuildLines();
            orbitYaw = w.player.yaw; orbitPitch = PredictionConfig.OrbitPitchInit;   // 플레이어 뒤에서 시작
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;        // 마우스=궤도 회전
            // [버그 수정, 2026-07-20] 예전엔 이 타임스탬프를 Build() 직후(=SetVisible/BuildLines
            // 등 나머지 진입 준비 작업 전)에 찍었다 — 그 나머지 작업도 같은 프레임 안에서 실시간을
            // 잡아먹는데, reveal 진행률은 실시간 기준이라 그 시간만큼 이미 흘러간 채로 첫 프레임이
            // 그려졌다. 그 결과 스윕의 앞부분이 렌더링될 기회 없이 스킵되고 뒷부분만 보였다 —
            // 화면에 그릴 준비가 실제로 끝난 지금 시점에서 타이머를 시작한다.
            previewRevealStartRealTime = Time.unscaledTime;
            Debug.Log($"[예측] 진입 — 사정권 {PredictionConfig.Range}, 루트 {routes.Count}개. (F 순환 · 좌클릭 확정 · Esc 취소)");
            LogSelected();
        }

        void Exit()
        {
            state = State.Idle;
            followingControls = null;
            followingRoute = null;
            followingIndex = 0;
            rhythmJudge = null;
            rhythmInputs.Clear();
            exitAfterFollowingStep = false;
            completedAfterFollowingStep = false;
            rhythmSegmentEventIndex = -1;
            rhythmWaitEventIndex = -1;
            previewRevealProgress = 0f;
            cameraYawVelocity = 0f;
            RestoreNormalTimeScale();
            if (Main.Instance != null) Main.Instance.SetPredictionSpawnLocked(false);
            fx.SetExecution(false);
            fx.SetActive(false);
            SetSwordExecutionStyle(false);
            ToggleSword(true);         // 1인칭 칼 복구
            SetVisible(false);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        void UpdateRhythmTimeScale()
        {
            float target = 1f;
            if (state == State.Following && rhythmJudge != null)
            {
                int pending = rhythmJudge.FirstPendingIndex;
                if (pending >= 0)
                {
                    if (pending != rhythmSegmentEventIndex)
                        BeginRhythmSegment();

                    float elapsedReal = Time.unscaledTime - rhythmSegmentStartRealTime;
                    float u = Mathf.Clamp01(elapsedReal / Mathf.Max(0.01f, rhythmSegmentDuration));
                    if (u >= rhythmSegmentDecelStart)
                    {
                        float span = Mathf.Max(
                            PredictionConfig.RhythmCurveMinSeconds, 1f - rhythmSegmentDecelStart);
                        float decelU = Mathf.Clamp01((u - rhythmSegmentDecelStart) / span);
                        float eased = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * decelU);
                        target = Mathf.Lerp(
                            rhythmSegmentStartScale, PredictionConfig.RhythmMinTimeScale, eased);
                    }
                    else target = rhythmSegmentStartScale;
                }
            }
            Time.timeScale = target;
        }

        void BeginRhythmSegment()
        {
            rhythmSegmentEventIndex = rhythmJudge != null ? rhythmJudge.FirstPendingIndex : -1;
            rhythmSegmentStartTick = followingIndex;
            rhythmSegmentStartRealTime = Time.unscaledTime;
            rhythmSegmentDuration = PredictionConfig.RhythmNormalMinSeconds;
            if (rhythmSegmentEventIndex < 0)
            {
                rhythmSegmentStartScale = 1f;
                rhythmSegmentDecelStart = 1f;
                return;
            }

            int eventTick = rhythmJudge.GetEvent(rhythmSegmentEventIndex).tick;
            float simSeconds = Mathf.Max(0f,
                eventTick - rhythmSegmentStartTick) / (float)SimConfig.TickRate;
            bool combo = IsSamePositionCombo(rhythmSegmentEventIndex);
            rhythmSegmentDuration = combo
                ? Mathf.Clamp(simSeconds + PredictionConfig.RhythmComboReadPadding,
                    PredictionConfig.RhythmComboMinSeconds, PredictionConfig.RhythmComboMaxSeconds)
                : Mathf.Clamp(simSeconds + PredictionConfig.RhythmNormalReadPadding,
                    PredictionConfig.RhythmNormalMinSeconds, PredictionConfig.RhythmNormalMaxSeconds);
            rhythmSegmentDuration = Mathf.Max(rhythmSegmentDuration, simSeconds);
            float requiredAverage = Mathf.Clamp01(simSeconds / Mathf.Max(0.01f, rhythmSegmentDuration));
            float fullCurveAverage = (PredictionConfig.RhythmMaxTimeScale
                                      + PredictionConfig.RhythmMinTimeScale) * 0.5f;
            if (requiredAverage <= fullCurveAverage)
            {
                rhythmSegmentStartScale = Mathf.Clamp(
                    requiredAverage * 2f - PredictionConfig.RhythmMinTimeScale,
                    PredictionConfig.RhythmMinTimeScale,
                    PredictionConfig.RhythmMaxTimeScale);
                rhythmSegmentDecelStart = 0f;
            }
            else
            {
                rhythmSegmentStartScale = PredictionConfig.RhythmMaxTimeScale;
                float decelFraction = 2f * (1f - requiredAverage)
                                      / (1f - PredictionConfig.RhythmMinTimeScale);
                rhythmSegmentDecelStart = Mathf.Min(
                    Mathf.Clamp01(1f - decelFraction),
                    1f - PredictionConfig.RhythmCurveMinSeconds);
            }
        }

        bool IsSamePositionCombo(int currentIndex)
        {
            if (followingRoute == null || currentIndex <= 0
                || currentIndex >= followingRoute.actionMarkers.Count)
                return false;
            ActionMarker previous = followingRoute.actionMarkers[currentIndex - 1];
            ActionMarker current = followingRoute.actionMarkers[currentIndex];
            return current.tick - previous.tick <= PredictionConfig.RhythmComboMaxGapTicks
                   && Vector3.Distance(previous.position, current.position)
                      <= PredictionConfig.RhythmComboPositionRadius;
        }

        public void RestoreNormalTimeScale()
        {
            Time.timeScale = 1f;
        }

        void Confirm(in SimWorld w)
        {
            if (routes.Count == 0) { Exit(); return; }
            var r = routes[selected];
            if (r.controls == null || r.controls.Length == 0)
            {
                Debug.LogWarning("[예측] 이 루트엔 재생할 입력이 없음(스텁 데이터?) — 자동실행 없이 닫음.");
                Exit();
                return;
            }
            state = State.Following;
            followingRoute = r;
            followingControls = r.controls;
            followingIndex = 0;
            var events = new PredictedActionEvent[r.actionMarkers.Count];
            for (int i = 0; i < events.Length; i++)
            {
                ActionMarker marker = r.actionMarkers[i];
                events[i] = new PredictedActionEvent
                {
                    tick = marker.tick,
                    type = marker.type,
                    targetId = marker.targetId,
                };
            }
            rhythmJudge = new RhythmJudge(events);
            rhythmInputs.Clear();
            exitAfterFollowingStep = false;
            completedAfterFollowingStep = false;
            rhythmFeedback = "";
            rhythmFeedbackUntil = 0f;
            BeginRhythmSegment();
            fx.SetExecution(false);
            if (Main.Instance != null) Main.Instance.SetPredictionSpawnLocked(true);
            // 3인칭 궤도 → 1인칭 전환 순간은 즉시 스냅(이후 Following 중 겨냥 변화만 서서히 따라간다).
            if (camPose != null) camPose.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
            cameraYawVelocity = 0f;
            ApplyFollowingVisuals();
            ToggleSword(true);      // 1인칭 복귀 — 칼도 다시 보이게
            SetSwordExecutionStyle(true);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            Debug.Log($"[예측] 루트 {selected} 확정 — 리듬 실행 ({r.seconds:0.0}초). " +
                      "Space 점프 · WASD 대시 · 좌클릭 공격 · 우클릭 런지 · Esc 취소");
        }

        void CaptureRhythmInputs(Keyboard kb, Mouse mouse)
        {
            if (kb.spaceKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.Jump);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.Attack);
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.Lunge);

            if (kb.wKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.DashForward);
            if (kb.sKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.DashBackward);
            if (kb.aKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.DashLeft);
            if (kb.dKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.DashRight);
        }

        void AddRhythmInput(PredictedActionType type)
        {
            rhythmInputs.Add(new TimedRhythmInput
            {
                type = type,
                realTime = Time.unscaledTime,
            });
        }

        /// <summary>
        /// Main.FixedUpdate 전용. Following 중이면 이번 틱에 실제로 넣을 기록된 입력을 반환하고
        /// true를 준다. 재생이 끝났으면(또는 Following이 아니면) false를 주고 — 재생이 끝나서
        /// false가 된 경우엔 자동으로 Idle로 돌아간다(Exit 호출).
        /// </summary>
        public bool TryConsumeFollowingInput(out InputCmd cmd)
        {
            if (state != State.Following || followingControls == null || followingIndex >= followingControls.Length)
            {
                cmd = default;
                if (state == State.Following) Exit();   // 재생 끝 — 정상 종료
                return false;
            }

            int tick = followingIndex;
            if (rhythmJudge != null)
            {
                int pending = rhythmJudge.FirstPendingIndex;
                if (pending >= 0)
                {
                    PredictedActionEvent evt = rhythmJudge.GetEvent(pending);
                    float targetRealTime = rhythmSegmentStartRealTime + rhythmSegmentDuration;
                    float earlyGoodSeconds = RhythmJudge.GoodWindowTicks / (float)SimConfig.TickRate;
                    if (Time.unscaledTime < targetRealTime - earlyGoodSeconds
                        && rhythmInputs.Count == 0)
                    {
                        // 아직 표시상 Good 창 전이며 입력도 없으므로 평소 경로를 계속 재생한다.
                    }
                    else
                    {
                        bool waitingAtEvent = tick >= evt.tick;
                        int judgementTick = tick;
                        if (waitingAtEvent && rhythmWaitEventIndex != pending)
                        {
                            rhythmWaitEventIndex = pending;
                            rhythmWaitStartRealTime = Time.unscaledTime;
                        }

                        if (waitingAtEvent)
                        {
                            float waited = Time.unscaledTime - rhythmWaitStartRealTime;
                            int lateTicks = Mathf.Clamp(
                                Mathf.RoundToInt(waited / PredictionConfig.RhythmWaitGoodSeconds
                                                 * RhythmJudge.GoodWindowTicks),
                                0, RhythmJudge.GoodWindowTicks + 1);
                            judgementTick = evt.tick + lateTicks;
                        }
                        bool accepted = false;
                        for (int i = 0; i < rhythmInputs.Count; i++)
                        {
                            int inputTick = RhythmJudge.MapDisplayTimeToTick(
                                rhythmInputs[i].realTime, targetRealTime, evt.tick,
                                PredictionConfig.RhythmWaitGoodSeconds);
                            RhythmJudgement result = rhythmJudge.Submit(
                                rhythmInputs[i].type, inputTick);
                            if (result == RhythmJudgement.Perfect || result == RhythmJudgement.Good)
                            {
                                accepted = true;
                                rhythmFeedback = result == RhythmJudgement.Perfect ? "PERFECT" : "GOOD";
                                rhythmFeedbackUntil = Time.unscaledTime + 0.45f;
                                CombatAudio.Hit();
                                Debug.Log($"[예측 리듬] {rhythmFeedback} — 사용자 입력으로 " +
                                          $"{evt.type} 실행 (tick {tick})");
                                break;
                            }
                        }
                        rhythmInputs.Clear();

                        if (!accepted)
                        {
                            if (!waitingAtEvent)
                                goto ConsumeFollowingControl;

                            int missed = rhythmJudge.CompleteTick(judgementTick);
                            if (missed >= 0)
                            {
                                rhythmFeedback = "MISS";
                                rhythmFeedbackUntil = Time.unscaledTime + 0.45f;
                                missGlitchStartedAt = Time.unscaledTime;
                                missGlitchUntil = missGlitchStartedAt + PredictionConfig.MissGlitchSeconds;
                                CombatAudio.PlayerHurt();
                                Debug.LogWarning($"[예측 리듬] Miss — 액션을 실행하지 않고 직접 조작으로 전환");
                                Exit();
                            }
                            cmd = default;
                            return false;
                        }
                        rhythmWaitEventIndex = -1;
                    }
                }
            }

        ConsumeFollowingControl:
            cmd = followingControls[followingIndex++];
            if (followingIndex >= followingControls.Length)
                completedAfterFollowingStep = true;
            return true;
        }

        public void AfterFollowingStep()
        {
            if (state == State.Following && (exitAfterFollowingStep || completedAfterFollowingStep))
                Exit();
        }

        public void DrawRhythmHud()
        {
            DrawMissGlitch();
            bool feedbackActive = Time.unscaledTime < rhythmFeedbackUntil;
            if (state != State.Following || rhythmJudge == null)
            {
                if (feedbackActive) DrawRhythmFeedback();
                return;
            }
            int current = rhythmJudge.FirstPendingIndex;
            if (current < 0)
            {
                if (feedbackActive) DrawRhythmFeedback();
                return;
            }

            EnsureRhythmRingTexture();
            PredictedActionEvent evt = rhythmJudge.GetEvent(current);
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - rhythmSegmentStartRealTime)
                / Mathf.Max(0.01f, rhythmSegmentDuration));
            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;
            float targetSize = Mathf.Clamp(Screen.height * 0.16f, 105f, 150f);
            float approachSize = Mathf.Lerp(targetSize * 2.35f, targetSize, progress);

            Color oldColor = GUI.color;
            GUI.color = new Color(0.3f, 1f, 0.72f, 0.5f);
            GUI.DrawTexture(CenteredRect(centerX, centerY, targetSize), rhythmRingTexture);
            GUI.color = Color.Lerp(
                new Color(0.25f, 1f, 0.55f, 0.85f), Color.white, progress * progress);
            GUI.DrawTexture(CenteredRect(centerX, centerY, approachSize), rhythmRingTexture);
            GUI.color = oldColor;

            var centerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.043f, 34f, 54f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            string prompt = progress >= 0.94f
                ? $"{ShortInputGuide(evt.type)}\n<size=22>NOW</size>"
                : ShortInputGuide(evt.type);
            GUI.Label(new Rect(centerX - 230f, centerY - 42f, 460f, 84f),
                $"<color=white>{prompt}</color>", centerStyle);

            DrawRhythmSequence(current, centerX, centerY);
            DrawExecutionSpeedFx(progress);
            if (feedbackActive) DrawRhythmFeedback();
        }

        static Rect CenteredRect(float x, float y, float size)
            => new Rect(x - size * 0.5f, y - size * 0.5f, size, size);

        void EnsureRhythmRingTexture()
        {
            if (rhythmRingTexture != null) return;
            const int size = 128;
            rhythmRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PredictionRhythmRing",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            Vector2 center = Vector2.one * ((size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
                float alpha = Mathf.Clamp01(1f - Mathf.Abs(radius - 0.86f) / 0.055f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            rhythmRingTexture.SetPixels32(pixels);
            rhythmRingTexture.Apply(false, true);
        }

        void DrawRhythmSequence(int current, float centerX, float centerY)
        {
            float sideOffset = Mathf.Clamp(Screen.width * 0.19f, 190f, 320f);
            float sideWidth = Mathf.Clamp(Screen.width * 0.16f, 150f, 230f);
            const float sideHeight = 82f;
            var sideStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.03f, 24f, 36f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };

            Rect left = new Rect(centerX - sideOffset - sideWidth * 0.5f,
                centerY - sideHeight * 0.5f, sideWidth, sideHeight);
            Rect right = new Rect(centerX + sideOffset - sideWidth * 0.5f,
                centerY - sideHeight * 0.5f, sideWidth, sideHeight);
            Color old = GUI.color;
            GUI.color = new Color(0.08f, 0.18f, 0.16f, PredictionConfig.RhythmSidePromptAlpha);
            GUI.Box(left, GUIContent.none);
            GUI.Box(right, GUIContent.none);
            GUI.color = old;

            string previous = current > 0
                ? ShortInputGuide(rhythmJudge.GetEvent(current - 1).type)
                : "—";
            string next = current + 1 < rhythmJudge.Count
                ? ShortInputGuide(rhythmJudge.GetEvent(current + 1).type)
                : "—";
            GUI.Label(left,
                $"<size=15><color=#74A99A80>PREV</color></size>\n<color=#B7D8CE88>{previous}</color>",
                sideStyle);
            GUI.Label(right,
                $"<size=15><color=#74A99A80>NEXT</color></size>\n<color=#B7D8CE88>{next}</color>",
                sideStyle);

            string label = IsSamePositionCombo(current) ? "RAPID COMBO" : "NEXT BEAT";
            sideStyle.fontSize = 22;
            GUI.Label(new Rect(centerX - 180f, centerY - 116f, 360f, 34f),
                $"<color=#50FF9A>{label}</color>", sideStyle);
        }

        void DrawExecutionSpeedFx(float beatProgress)
        {
            float pulse = 0.55f + 0.45f * Mathf.Sin(
                (Time.unscaledTime * PredictionConfig.ExecutionSpeedLineRate + beatProgress)
                * Mathf.PI * 2f);
            Color old = GUI.color;
            GUI.color = new Color(0.25f, 1f, 0.72f,
                PredictionConfig.ExecutionSpeedLineAlpha * pulse);
            Texture2D white = Texture2D.whiteTexture;
            const int streaks = 9;
            for (int i = 0; i < streaks; i++)
            {
                float lane = (i + 0.5f) / streaks;
                float travel = Mathf.Repeat(
                    Time.unscaledTime * PredictionConfig.ExecutionSpeedLineRate + i * 0.137f, 1f);
                float y = Mathf.Lerp(Screen.height * 0.1f, Screen.height * 0.9f, lane);
                float width = Mathf.Lerp(42f, 150f, travel);
                float edgeInset = Mathf.Lerp(12f, Screen.width * 0.12f, travel);
                GUI.DrawTexture(new Rect(edgeInset, y, width, 2f), white);
                GUI.DrawTexture(new Rect(Screen.width - edgeInset - width, y, width, 2f), white);
            }
            GUI.color = old;
        }

        void DrawRhythmFeedback()
        {
            float width = Mathf.Min(980f, Screen.width - 24f);
            float x = (Screen.width - width) * 0.5f;
            float y = Mathf.Max(18f, Screen.height * 0.06f) + 218f;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 68,
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            string color = rhythmFeedback == "PERFECT" ? "#dfffff"
                : rhythmFeedback == "GOOD" ? "#40ffd8" : "#ff4038";
            GUI.Label(new Rect(x, y, width, 76f),
                $"<color={color}>{rhythmFeedback}</color>", style);
        }

        void DrawMissGlitch()
        {
            if (Time.unscaledTime >= missGlitchUntil) return;
            EnsureMissGlitchTexture();

            float elapsed = Time.unscaledTime - missGlitchStartedAt;
            float life = Mathf.Clamp01(elapsed / PredictionConfig.MissGlitchSeconds);
            float alpha = PredictionConfig.MissGlitchMaxAlpha
                * (1f - life) * (0.7f + 0.3f * Mathf.Abs(Mathf.Sin(elapsed * 95f)));
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float offsetX = Mathf.Repeat(elapsed * 17.3f, 1f);
            float offsetY = Mathf.Repeat(elapsed * 29.7f, 1f);
            GUI.DrawTextureWithTexCoords(
                new Rect(0f, 0f, Screen.width, Screen.height),
                missGlitchTexture,
                new Rect(offsetX, offsetY, 3f, 3f));

            for (int i = 0; i < 7; i++)
            {
                float wave = Mathf.Repeat(elapsed * (31f + i * 3.7f) + i * 0.173f, 1f);
                float y = wave * Screen.height;
                float h = 4f + (i % 3) * 7f;
                float shift = Mathf.Sin(elapsed * 80f + i * 2.1f) * 0.18f;
                GUI.color = new Color(i % 2 == 0 ? 1f : 0.2f, 0.12f, 0.14f, alpha * 0.75f);
                GUI.DrawTextureWithTexCoords(
                    new Rect(0f, y, Screen.width, h),
                    missGlitchTexture,
                    new Rect(offsetX + shift, offsetY + i * 0.11f, 3f, 0.18f));
            }
            GUI.color = previous;
        }

        void EnsureMissGlitchTexture()
        {
            if (missGlitchTexture != null) return;
            const int size = 128;
            missGlitchTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PredictionMissGlitch",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            var pixels = new Color32[size * size];
            var random = new System.Random(19770421);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte value = (byte)random.Next(15, 256);
                byte pixelAlpha = (byte)random.Next(45, 210);
                pixels[i] = new Color32(value, value, value, pixelAlpha);
            }
            missGlitchTexture.SetPixels32(pixels);
            missGlitchTexture.Apply(false, true);
        }

        static string InputGuide(PredictedActionType type)
        {
            switch (type)
            {
                case PredictedActionType.Jump: return "[ SPACE ]  점프";
                case PredictedActionType.DashForward: return "[ W ]  전방 대시";
                case PredictedActionType.DashBackward: return "[ S ]  후방 대시";
                case PredictedActionType.DashLeft: return "[ A ]  좌측 대시";
                case PredictedActionType.DashRight: return "[ D ]  우측 대시";
                case PredictedActionType.Attack: return "[ 좌클릭 ]  공격";
                case PredictedActionType.Lunge: return "[ 우클릭 ]  런지";
                default: return type.ToString();
            }
        }

        static string ShortInputGuide(PredictedActionType type)
        {
            switch (type)
            {
                case PredictedActionType.Jump: return "SPACE";
                case PredictedActionType.DashForward: return "W";
                case PredictedActionType.DashBackward: return "S";
                case PredictedActionType.DashLeft: return "A";
                case PredictedActionType.DashRight: return "D";
                case PredictedActionType.Attack: return "L-CLICK";
                case PredictedActionType.Lunge: return "R-CLICK";
                default: return type.ToString();
            }
        }

        void LogSelected()
        {
            if (routes.Count == 0) return;
            var r = routes[selected];
            // 이제 점수 순위가 아니라 서로 다른 목적(안전형/기회형/공격형)의 대표 경로다 —
            // PredictedRoute.profileLabel(RealRoutePreview가 PlanByProfile에서 채움)을 그대로 표시.
            string nm = !string.IsNullOrEmpty(r.profileLabel) ? r.profileLabel : selected.ToString();
            Debug.Log($"[예측] 선택 루트 {selected}({nm}) — 처치 {r.kills.Count}, {r.seconds:0.0}초");
        }

        // ── 렌더 ──
        void Render(in SimWorld w)
        {
            UpdateDome(w.player.pos);
            previewRevealProgress = Mathf.Clamp01(
                (Time.unscaledTime - previewRevealStartRealTime)
                / Mathf.Max(0.01f, PredictionConfig.PreviewRevealSeconds));
            UpdatePreviewLines(previewRevealProgress);

            if (startMarker != null)   // 정지된 플레이어 위치 = 루트 시작점
            {
                startMarker.position = w.player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
                startMarker.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
            }

            UpdateRevealTrails(previewRevealProgress);
            UpdateGhostMarks(in w);
        }

        void PlaceCamera(in SimWorld w)
        {
            if (camPose == null) return;
            Vector3 pivot = w.player.pos + Vector3.up * PredictionConfig.CamLookY;
            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 direction = -(rot * Vector3.forward);
            float distance = PredictionConfig.CamDist;
            int collisionMask = Physics.DefaultRaycastLayers & ~(1 << PredictionAccentLayer);
            if (Physics.SphereCast(
                pivot,
                PredictionConfig.CamCollisionRadius,
                direction,
                out RaycastHit hit,
                PredictionConfig.CamDist,
                collisionMask,
                QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Clamp(
                    hit.distance - PredictionConfig.CamCollisionPadding,
                    PredictionConfig.CamCollisionMinDistance,
                    PredictionConfig.CamDist);
            }
            camPose.position = pivot + direction * distance;
            camPose.rotation = rot;
        }

        /// <summary>
        /// Following 단계: 실제로 자동 실행 중인 플레이어의 위치·시선을 1인칭으로 따라간다.
        /// 회전은 즉시 스냅하지 않고 초당 최대 각도(FollowingCamTurnSpeed)로 제한해서 따라가게
        /// 한다 — 예측이 겨냥을 홱 바꿔도 화면이 순간 회전하지 않고, 지금 어느 쪽으로 도는지
        /// 눈으로 좇을 수 있다.
        /// </summary>
        public void UpdateFollowingCameraRenderPose(Vector3 position, float targetYaw)
        {
            if (state != State.Following || camPose == null) return;
            camPose.position = position + Vector3.up * PredictionConfig.CamLookY;
            float yaw = Mathf.SmoothDampAngle(
                camPose.eulerAngles.y, targetYaw, ref cameraYawVelocity,
                0.12f, PredictionConfig.FollowingCamTurnSpeed, Time.unscaledDeltaTime);
            camPose.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ── 비주얼 요소 ──
        LineRenderer MakeDome()
        {
            var go = new GameObject("PredictDome");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.loop = true;
            lr.widthMultiplier = PredictionConfig.DomeWidth;
            lr.positionCount = 64;
            lr.material = LineMat();
            lr.startColor = lr.endColor = PredictionConfig.DomeColor;
            lr.numCapVertices = 2;
            return lr;
        }

        void UpdateDome(Vector3 center)
        {
            int seg = domeLr.positionCount;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float rr = PredictionConfig.Range;
                domeLr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * rr, 0.12f, Mathf.Sin(a) * rr));
            }
        }

        void BuildLines()
        {
            while (lines.Count < routes.Count)
            {
                var go = new GameObject($"Route_{lines.Count}");
                go.layer = PredictionAccentLayer;
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.numCapVertices = 2; lr.numCornerVertices = 2;
                lr.material = LineMat();
                lines.Add(lr);
            }
            for (int i = 0; i < lines.Count; i++)
            {
                bool used = i == 0 && i < routes.Count;
                lines[i].gameObject.SetActive(used);
                if (!used) continue;
                lines[i].positionCount = 0;
                StyleLine(lines[i], PredictionConfig.RouteColors[0], true);
            }
        }

        void UpdatePreviewLines(float progress)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                bool safetyRoute = i == 0 && i < routes.Count;
                lines[i].gameObject.SetActive(safetyRoute);
                if (safetyRoute)
                    SetPreviewLineWindow(lines[i], routes[i], progress);
            }
        }

        void SetPreviewLineWindow(LineRenderer line, PredictedRoute route, float progress)
        {
            float headProgress = Mathf.Clamp01(progress);
            SetLineReveal(line, route, headProgress);
            line.widthMultiplier = PredictionConfig.RouteWidthSel;
            ApplyPreviewLineGradient(line, headProgress);
        }

        void ApplyPreviewLineGradient(LineRenderer line, float progress)
        {
            float p = Mathf.Max(0.0001f, Mathf.Clamp01(progress));
            Color end = PreviewPathColor(p);
            previewLineColorKeys[0] = new GradientColorKey(PredictionConfig.PreviewPathGreen, 0f);
            if (p <= 1f / 3f)
            {
                previewLineColorKeys[1] = new GradientColorKey(
                    Color.Lerp(PredictionConfig.PreviewPathGreen, end, 1f / 3f), 1f / 3f);
                previewLineColorKeys[2] = new GradientColorKey(
                    Color.Lerp(PredictionConfig.PreviewPathGreen, end, 2f / 3f), 2f / 3f);
            }
            else if (p <= 2f / 3f)
            {
                float blueAt = (1f / 3f) / p;
                previewLineColorKeys[1] =
                    new GradientColorKey(PredictionConfig.PreviewPathBlue, blueAt);
                previewLineColorKeys[2] =
                    new GradientColorKey(Color.Lerp(PredictionConfig.PreviewPathBlue, end, 0.5f),
                        Mathf.Lerp(blueAt, 1f, 0.5f));
            }
            else
            {
                previewLineColorKeys[1] = new GradientColorKey(
                    PredictionConfig.PreviewPathBlue, (1f / 3f) / p);
                previewLineColorKeys[2] = new GradientColorKey(
                    PredictionConfig.PreviewPathPurple, (2f / 3f) / p);
            }
            previewLineColorKeys[3] = new GradientColorKey(end, 1f);
            previewLineGradient.SetKeys(previewLineColorKeys, previewLineAlphaKeys);
            line.colorGradient = previewLineGradient;
        }

        static void SetLineReveal(LineRenderer line, PredictedRoute route, float progress)
        {
            if (route == null || route.path.Count == 0)
            {
                line.positionCount = 0;
                return;
            }
            if (route.path.Count == 1)
            {
                line.positionCount = 1;
                line.SetPosition(0, route.path[0] + Vector3.up * 0.15f);
                return;
            }

            float scaled = Mathf.Clamp01(progress) * (route.path.Count - 1);
            int whole = Mathf.FloorToInt(scaled);
            float fraction = scaled - whole;
            int count = Mathf.Min(route.path.Count, whole + 2);
            line.positionCount = count;
            for (int i = 0; i <= whole && i < count; i++)
                line.SetPosition(i, route.path[i] + Vector3.up * 0.15f);
            if (whole + 1 < count)
            {
                Vector3 tip = Vector3.Lerp(route.path[whole], route.path[whole + 1], fraction);
                line.SetPosition(count - 1, tip + Vector3.up * 0.15f);
            }
        }

        static void StyleLine(LineRenderer lr, Color c, bool sel)
        {
            lr.widthMultiplier = sel ? PredictionConfig.RouteWidthSel : PredictionConfig.RouteWidthDim;
            Color col = sel ? c : c * PredictionConfig.RouteDimMul;
            col.a = sel ? PredictionConfig.RouteAlphaSel : PredictionConfig.RouteAlphaDim;   // 반투명 경로선
            lr.startColor = lr.endColor = col;
        }

        void ApplyFollowingVisuals()
        {
            for (int i = 0; i < lines.Count; i++)
            {
                bool chosen = i == selected && i < routes.Count;
                lines[i].gameObject.SetActive(chosen);
                if (chosen)
                {
                    SetLineReveal(lines[i], routes[i], 1f);
                    StyleLine(lines[i], PredictionConfig.ExecutionRouteColor, true);
                }
            }
            for (int i = 0; i < revealGhosts.Count; i++)
            {
                revealGhosts[i].gameObject.SetActive(false);
                if (i >= revealAfterimagesByRoute.Count) continue;
                for (int j = 0; j < revealAfterimagesByRoute[i].Count; j++)
                    revealAfterimagesByRoute[i][j].gameObject.SetActive(false);
            }
        }

        Transform MakeStartMarker()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "PredictStart";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.PlayerRadius * 2f,
                                                  SimConfig.PlayerHeight * 0.5f,
                                                  SimConfig.PlayerRadius * 2f);
            go.GetComponent<Renderer>().material = SolidMat(PredictionConfig.StartMarkerColor);
            return go.transform;
        }

        Transform MakeRevealGhost()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "PredictRevealGhost";
            go.layer = PredictionAccentLayer;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(
                SimConfig.PlayerRadius * 2.2f,
                SimConfig.PlayerHeight * 0.55f,
                SimConfig.PlayerRadius * 2.2f);
            go.GetComponent<Renderer>().material = GhostMat();

            // "잔상을 일정 간격으로 찍는" 대신 "움직이는 경로가 사라지는 잔상"을 만드는 부분 —
            // 캡슐이 경로를 따라 이동하고, 지나간 자리는 TrailRenderer가 알파 0으로 페이드되며 남긴다.
            return go.transform;
        }

        /// <summary>Preview 중 모든 후보가 동시에 자기 경로를 따라 이동하며 트레일을 남긴다
        /// (goal: 후보가 하나씩 순차 재생되지 않고 동시에 나가야 함). 정지 스탬프(액션 지점)는
        /// UpdateGhostMarks가 따로 담당한다.</summary>
        void UpdateRevealTrails(float progress)
        {
            float revealElapsed = Time.unscaledTime - previewRevealStartRealTime;
            float afterimageFade = 1f - Mathf.Clamp01(
                (revealElapsed - PredictionConfig.PreviewRevealSeconds)
                / PredictionConfig.PreviewAfterimageFadeSeconds);

            // 보통 PreWarmRoutePools가 이미 RouteColors.Length(3)개를 다 채워둬서 여기서 자랄 일은
            // 없다 — 후보 수가 그 이상으로 늘어나는 미래 변경에 대비한 안전망만 유지한다.
            int revealRouteCount = routes.Count > 0 ? 1 : 0;
            while (revealGhosts.Count < revealRouteCount)
            {
                revealGhosts.Add(MakeRevealGhost());
                var afterimages = new List<Transform>(PredictionConfig.PreviewAfterimageCount);
                for (int i = 0; i < PredictionConfig.PreviewAfterimageCount; i++)
                {
                    Transform afterimage = MakeRevealGhost();
                    afterimage.name = "PredictRevealAfterimage";
                    afterimages.Add(afterimage);
                }
                revealAfterimagesByRoute.Add(afterimages);
            }

            for (int ri = 0; ri < revealGhosts.Count; ri++)
            {
                Transform ghost = revealGhosts[ri];
                PredictedRoute route = ri < routes.Count ? routes[ri] : null;
                bool show = state == State.Preview && route != null && route.path.Count > 0;
                ghost.gameObject.SetActive(show);
                List<Transform> afterimages = revealAfterimagesByRoute[ri];
                if (!show)
                {
                    for (int i = 0; i < afterimages.Count; i++)
                        afterimages[i].gameObject.SetActive(false);
                    continue;
                }

                float scaled = Mathf.Clamp01(progress) * Mathf.Max(0, route.path.Count - 1);
                bool sel = ri == selected;
                Color color = PreviewPathColor(progress);
                color.a = sel ? 0.85f : 0.4f;
                PlaceRevealBody(ghost, route, scaled);
                SetMarkColor(ghost, color);

                for (int i = 0; i < afterimages.Count; i++)
                {
                    float afterProgress = progress
                        - PredictionConfig.PreviewAfterimageSpacing * (i + 1);
                    bool afterVisible = afterProgress >= 0f && afterimageFade > 0f;
                    Transform afterimage = afterimages[i];
                    afterimage.gameObject.SetActive(afterVisible);
                    if (!afterVisible) continue;

                    float afterScaled = afterProgress * Mathf.Max(0, route.path.Count - 1);
                    PlaceRevealBody(afterimage, route, afterScaled);
                    float fade = 1f - i / (float)afterimages.Count;
                    Color afterColor = PreviewPathColor(afterProgress);
                    afterColor.a = PredictionConfig.PreviewAfterimageHeadAlpha
                        * fade * fade * afterimageFade
                        * (sel ? 1f : PredictionConfig.RouteDimMul);
                    SetMarkColor(afterimage, afterColor);
                }
            }
        }

        static void PlaceRevealBody(Transform body, PredictedRoute route, float scaled)
        {
            int from = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, route.path.Count - 1);
            int to = Mathf.Min(from + 1, route.path.Count - 1);
            float fraction = Mathf.Clamp01(scaled - from);
            body.position = Vector3.Lerp(route.path[from], route.path[to], fraction)
                + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
            Vector3 direction = route.path[to] - route.path[from];
            if (direction.sqrMagnitude > 1e-5f)
                body.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        static Color PreviewPathColor(float progress)
        {
            float p = Mathf.Clamp01(progress);
            if (p < 1f / 3f)
                return Color.Lerp(
                    PredictionConfig.PreviewPathGreen,
                    PredictionConfig.PreviewPathBlue,
                    p * 3f);
            if (p < 2f / 3f)
                return Color.Lerp(
                    PredictionConfig.PreviewPathBlue,
                    PredictionConfig.PreviewPathPurple,
                    (p - 1f / 3f) * 3f);
            return Color.Lerp(
                PredictionConfig.PreviewPathPurple,
                PredictionConfig.PreviewPathRed,
                (p - 2f / 3f) * 3f);
        }

        /// <summary>정지 잔상 — 고정 간격이 아니라 route.ghostFrames(RealRoutePreview가
        /// ActionEvent 틱에서 샘플링)의 위치·yaw를 그대로 가져와 풀링된 캡슐로 배치한다.
        /// Preview 중엔 모든 후보가 동시에 표시되고(경로별 풀), Following 중엔 실행 중인
        /// selected 경로만 표시된다.</summary>
        void UpdateGhostMarks(in SimWorld world)
        {
            while (ghostMarksByRoute.Count < routes.Count) ghostMarksByRoute.Add(new List<Transform>());

            for (int ri = 0; ri < ghostMarksByRoute.Count; ri++)
            {
                List<Transform> pool = ghostMarksByRoute[ri];
                PredictedRoute r = ri < routes.Count ? routes[ri] : null;
                bool routeVisible = r != null
                    && ((state == State.Preview && ri == 0)
                        || (state == State.Following && ri == selected));
                int need = routeVisible ? r.ghostFrames.Count : 0;
                while (pool.Count < need) pool.Add(MakeGhostMark());

                int revealTick = r != null && r.path.Count > 0
                    ? Mathf.RoundToInt(previewRevealProgress * (r.path.Count - 1))
                    : -1;
                for (int i = 0; i < pool.Count; i++)
                {
                    PredictedFrame f = i < need ? r.ghostFrames[i] : default;
                    bool used = i < need && (state != State.Preview || f.tick <= revealTick);
                    pool[i].gameObject.SetActive(used);
                    if (!used) continue;
                    pool[i].position = f.playerPosition + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
                    pool[i].rotation = Quaternion.Euler(0f, f.playerYaw, 0f);
                    SetMarkColor(pool[i], GhostDisplayColor(
                        r, ri, f, i, need, world.player.pos));
                }
            }
        }

        Color GhostDisplayColor(
            PredictedRoute route, int routeIndex, PredictedFrame frame, int index, int count,
            Vector3 playerPosition)
        {
            if (state == State.Following)
            {
                float distance = Vector3.Distance(playerPosition, frame.playerPosition);
                float proximityFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    PredictionConfig.ExecutionGhostFadeNear,
                    PredictionConfig.ExecutionGhostFadeFar,
                    distance));
                int currentEvent = rhythmJudge != null ? rhythmJudge.FirstPendingIndex : -1;
                int eventTick = currentEvent >= 0 ? rhythmJudge.GetEvent(currentEvent).tick : followingIndex;
                if (Mathf.Abs(frame.tick - eventTick) <= 15)
                    return new Color(0.35f, 1f, 0.65f, 0.28f * proximityFade);
                if (frame.tick < followingIndex)
                {
                    Color trail = RainbowGhostColor(index, count);
                    trail.a = PredictionConfig.ExecutionGhostAlpha * proximityFade;
                    return trail;
                }
                return new Color(0.65f, 0.72f, 0.68f, 0.08f * proximityFade);
            }

            // Preview: 경로 색으로 후보를 구분하고, 선택된 후보만 또렷하게 강조한다
            // (다른 시각 요소인 라인·트레일의 RouteDimMul 감쇠와 동일한 규칙).
            bool sel = routeIndex == selected;
            float pathProgress = route.path.Count > 1
                ? frame.tick / (float)(route.path.Count - 1)
                : 0f;
            Color c = PreviewPathColor(pathProgress);
            c.a = sel ? 0.62f : 0.3f;
            if (!sel) c = new Color(c.r * PredictionConfig.RouteDimMul, c.g * PredictionConfig.RouteDimMul,
                                     c.b * PredictionConfig.RouteDimMul, c.a);
            return c;
        }

        Transform MakeGhostMark()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "PredictGhostMark";
            go.layer = PredictionAccentLayer;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.PlayerRadius * 2f,
                                                  SimConfig.PlayerHeight * 0.5f,
                                                  SimConfig.PlayerRadius * 2f);
            go.GetComponent<Renderer>().material = GhostMat();
            return go.transform;
        }

        static void SetMarkColor(Transform mark, Color color)
        {
            Renderer renderer = mark.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.material.color = color;
            if (renderer.material.HasProperty("_BaseColor"))
                renderer.material.SetColor("_BaseColor", color);
        }

        static Color RainbowGhostColor(int index, int count)
        {
            float spacing = count > 1 ? index / (float)(count - 1) : 0f;
            float hue = Mathf.Repeat(
                spacing * 0.82f + Time.unscaledTime * PredictionConfig.ExecutionGhostHueSpeed, 1f);
            Color color = Color.HSVToRGB(hue, 0.78f, 1f);
            color.a = PredictionConfig.ExecutionGhostAlpha;
            return color;
        }

        void SetVisible(bool on)
        {
            if (domeLr != null) domeLr.gameObject.SetActive(on);
            for (int i = 0; i < lines.Count; i++) lines[i].gameObject.SetActive(on && i < routes.Count);
            for (int ri = 0; ri < ghostMarksByRoute.Count; ri++)
                for (int i = 0; i < ghostMarksByRoute[ri].Count; i++)
                    ghostMarksByRoute[ri][i].gameObject.SetActive(on);
            if (startMarker != null) startMarker.gameObject.SetActive(on);
            for (int i = 0; i < revealGhosts.Count; i++)
            {
                revealGhosts[i].gameObject.SetActive(on && state == State.Preview);
                if (i >= revealAfterimagesByRoute.Count) continue;
                for (int j = 0; j < revealAfterimagesByRoute[i].Count; j++)
                    revealAfterimagesByRoute[i][j].gameObject.SetActive(on && state == State.Preview);
            }
        }

        /// <summary>1인칭 칼(SwordView가 카메라 자식으로 만든 "SwordPivot") 표시 토글. combat 파일 미편집(이름 결합).</summary>
        void ToggleSword(bool show)
        {
            if (cam == null) return;
            var sp = cam.transform.Find("SwordPivot");
            if (sp != null) sp.gameObject.SetActive(show);
        }

        void SetSwordExecutionStyle(bool active)
        {
            if (cam == null) return;
            Transform sword = cam.transform.Find("SwordPivot");
            if (sword == null) return;
            Renderer[] renderers = sword.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material material = renderer.material;
                if (active)
                {
                    Transform current = renderer.transform;
                    if (!swordOriginalLayers.ContainsKey(current))
                        swordOriginalLayers.Add(current, current.gameObject.layer);
                    current.gameObject.layer = PredictionAccentLayer;
                    if (!swordOriginalColors.ContainsKey(renderer))
                        swordOriginalColors.Add(renderer, material.color);
                    material.color = PredictionConfig.ExecutionPlayerColor;
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", PredictionConfig.ExecutionPlayerColor);
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor",
                            PredictionConfig.ExecutionPlayerColor * 2.2f);
                    }
                }
                else if (swordOriginalColors.TryGetValue(renderer, out Color original))
                {
                    if (swordOriginalLayers.TryGetValue(renderer.transform, out int originalLayer))
                        renderer.gameObject.layer = originalLayer;
                    material.color = original;
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", original);
                    if (material.HasProperty("_EmissionColor"))
                        material.SetColor("_EmissionColor", Color.black);
                }
            }
            if (!active)
            {
                swordOriginalColors.Clear();
                swordOriginalLayers.Clear();
            }
        }

        // ── 머티리얼 ──
        static Material LineMat()
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh);
            // Unlit로 폴백된 경우 기본이 Opaque라 반투명 경로선의 알파가 무시된다 — 명시적으로 켠다.
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.renderQueue = 3000; }
            return m;
        }

        static Material GhostMat()
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh);
            Color c = PredictionConfig.GhostColor;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.renderQueue = 3000; }
            return m;
        }

        static Material SolidMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
