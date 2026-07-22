using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 예측 연출 전용 튜닝값(전부 잠정). 리터럴 하드코딩 금지 — 이 한 곳에서만 조정한다.
    /// 전투/이동 수치는 여기 두지 않는다(그건 SimConfig/CombatConfig/AIConfig 소유).
    /// 공유 파일이 아니라 예측 세션 단독 파일이라 다른 세션과 충돌하지 않는다.
    /// </summary>
    public static class PredictionConfig
    {
        // 사정권(이 반경 내 적만 루트 대상)
        public const float Range = 16f;

        // [HUD 게이지, 2026-07-22] 예지(F) 자원. 예전엔 무제한이라 HUD의 PREDICTION 다이얼에
        // 붙일 수치가 없었다 — 발동 시 전부 소모하고 실시간으로 재충전되는 게이지를 둔다.
        // Idle일 때만 차오르며(미리보기·자동실행 중엔 멈춤), 슬로모와 무관하게 실시간이다.
        /// <summary>0 → 100%까지 걸리는 실시간 초.</summary>
        public const float ChargeRechargeSeconds = 12f;
        /// <summary>예지 진입 시 남기는 잔량(0 = 전부 소모).</summary>
        public const float ChargeAfterEnter = 0f;
        /// <summary>이만큼은 차야 F가 먹는다. 게이지가 곧 예측 지평이라 너무 적게 남은 상태로
        /// 쓰면 1초짜리 경로만 나와서 자원만 버리게 된다 — 그걸 막는 하한선.</summary>
        public const float ChargeMinToUse = 0.25f;

        /// <summary>
        /// 게이지(0~1)를 내다볼 시간(초)으로 환산한다. <b>게이지가 곧 예측 지평</b> —
        /// 아껴서 길게 볼지, 짧게 자주 쓸지가 플레이어의 선택이 된다.
        /// 범위는 PredictionSettings.Min/MaxDurationSeconds(1~5초)와 같이 간다.
        /// </summary>
        public static float ChargeToSeconds(float charge01) =>
            Mathf.Lerp(Game.Prediction.PredictionSettings.MinDurationSeconds,
                       Game.Prediction.PredictionSettings.MaxDurationSeconds,
                       Mathf.Clamp01(charge01));

        // 미리보기 카메라 (3인칭 궤도 — 마우스로 플레이어 중심 회전)
        public const float CamDist  = 7f;      // 피벗에서 뒤로
        public const float CamLookY = 1.2f;    // 피벗 높이(플레이어 기준)
        public const float OrbitSens      = 0.15f;
        public const float OrbitPitchInit = 18f;    // 진입 시 살짝 위에서
        public const float OrbitPitchMin  = -10f;
        public const float OrbitPitchMax  = 80f;
        public const float CamCollisionRadius = 0.28f;
        public const float CamCollisionPadding = 0.12f;
        public const float CamCollisionMinDistance = 0.45f;
        // [예측 세션 추가, 2026-07-21] 진입 시 1인칭→3인칭 궤도 전환 연출 — 예전엔 목표 거리로
        // 즉시 스냅해서 "탁 하고 한 번에" 바뀌어 보였다. 초반엔 빠르게 멀어지다 후반부로
        // 갈수록 감속하는 완급(ease-out)을 줘서 자연스럽게 3인칭으로 빠져나가게 한다.
        public const float EnterOrbitPullbackSeconds = 0.55f;

        // 루트 색 (순서: PredictionPlanner.PlanByProfile이 고정하는 안전형/기회형/공격형)
        public static readonly Color[] RouteColors =
        {
            new Color(0.2f, 1f, 0.9f),   // 청록
            new Color(1f, 0.35f, 0.7f),  // 자홍
            new Color(1f, 0.8f, 0.2f),   // 주황
        };

        /// <summary>[잔상 밀도 상향, 2026-07-22] 경로 표시 선(발밑에 깔리던 LineRenderer)을 그릴지.
        /// 잔상이 촘촘해지면서 경로는 잔상 행렬 자체로 충분히 읽혀 선이 오히려 지저분해졌다 —
        /// 코드는 남겨두고 이 스위치로만 끈다(돔·킬 마커는 그대로).</summary>
        public const bool ShowRoutePathLine = false;

        // 라인 / 돔 / 고스트 / 마커 시각
        public const float RouteWidthSel = 0.32f;   // 선택 루트 굵기
        public const float RouteWidthDim = 0.12f;   // 비선택 루트 굵기
        public const float RouteDimMul   = 0.45f;   // 비선택 루트 밝기 배율
        // >>> [예측 세션 추가] RouteAlphaSel/Dim, ActionMarkColor — 원래 없던 값.
        public const float RouteAlphaSel = 0.75f;   // 선택 루트 반투명도
        public const float RouteAlphaDim = 0.4f;    // 비선택 루트 반투명도(더 흐리게)
        // [예측 세션 수정, 2026-07-20] 1.6 → 2.8: 이동 헤드와 경로가 전개되는 과정을
        // 충분히 눈으로 따라갈 수 있게 늦춘다. 예측 계산·판정·실행 속도에는 영향이 없다.
        public const float PreviewRevealSeconds = 5.0f;
        // [잔상 유지, 2026-07-22] 예전엔 헤드 바로 뒤에 붙어 따라오다 스윕이 끝나면 통째로
        // 페이드아웃되는 "꼬리"였다(PreviewAfterimageSpacing / PreviewAfterimageFadeSeconds).
        // 지금은 "투사주법"처럼 — 헤드가 지나간 자리에 일정 거리마다 분신을 한 번 찍어두고,
        // 그 분신은 Preview가 끝날 때까지 그 자리에 그대로 남는다(움직이지도, 사라지지도 않음).
        // [잔상 밀도 상향, 2026-07-22] 산데비스탄처럼 — 잔상을 촘촘히 깔아서 옆으로 죽 훑으면
        // 연속 동작(걷기 사이클)이 그대로 읽히게 한다. 간격이 스트라이드(GhostRunStrideMeters)에
        // 비해 충분히 작아야 인접 잔상의 다리 각도가 조금씩만 달라져 "이어진 동작"으로 보인다.
        /// <summary>남겨두는 분신 최대 개수(풀 크기). 경로가 길어 이 개수로 모자라면
        /// 아래 Step이 자동으로 벌어져서 경로 전체를 균등하게 덮는다.</summary>
        public const int PreviewAfterimageCount = 90;
        /// <summary>분신을 찍는 경로상 간격(m). 스트라이드 3.4m / 0.4m ≈ 사이클당 8~9장 —
        /// 걷기 동작이 이어져 보이는 최소선은 유지하면서 너무 빽빽하지 않은 값.</summary>
        public const float PreviewAfterimageStepMeters = 0.4f;
        /// <summary>남은 분신의 불투명도(스윕이 끝나도 이 값 그대로 유지된다). 촘촘해진 만큼
        /// 겹침이 심해서 낮춰 잡는다 — 겹쳐 쌓이면서 자연스럽게 진해진다.</summary>
        public const float PreviewAfterimageHeadAlpha = 0.16f;
        public static readonly Color PreviewPathGreen = new Color(0.12f, 1f, 0.35f);
        public static readonly Color PreviewPathBlue = new Color(0.08f, 0.4f, 1f);
        public static readonly Color PreviewPathPurple = new Color(0.72f, 0.12f, 1f);
        public static readonly Color PreviewPathRed = new Color(1f, 0.06f, 0.03f);
        public const float MissGlitchSeconds = 0.38f;
        public const float MissGlitchMaxAlpha = 0.58f;
        // <<< [예측 세션 추가 끝]

        // >>> [잔상 아바타 애니메이션, 2026-07-22] 잔상이 캡슐이 아니라 히어로 아바타가 되면서
        // "가만히 선 바인드 포즈"로 굳어 보이던 문제를 없애기 위해, Mixamo 클립을 특정 시각에서
        // 한 프레임만 굽는(SampleAnimation) 방식으로 포즈를 입힌다. 클립은 Resources에 복제해 둔
        // GhostRun/GhostWalk/GhostJump/GhostSlash. 아래는 그 "어느 시점을 굽느냐" 튜닝 값들.
        /// <summary>달리기 한 사이클(클립 1회 재생)이 커버하는 이동 거리(m). 경로 진행 거리를
        /// 이 값으로 나눠 클립 위상을 만든다 — 작게 잡으면 다리가 더 빨리 돈다.</summary>
        public const float GhostRunStrideMeters = 3.4f;
        // [액션 구간 잔상, 2026-07-22] 예전엔 액션 잔상이 "그 틱 한 장"뿐이고 나머지 잔상은
        // 전부 달리기 포즈였다 — 대시·런지 구간까지 달리는 걸로 보인다는 피드백. 이제 액션은
        // "틱 구간"을 가지며, 그 구간에 걸리는 잔상들은 전부 해당 액션 클립을 아래 정규화 구간에
        // 걸쳐 나눠 굽는다 → 연속으로 보면 대시/찌르기 동작이 펼쳐진다.
        // (Window는 Sim의 실제 지속 틱을 그대로 쓰지 않고 "보기 좋은 길이"로 잡은 연출 값이다.
        //  Attack 20틱 ≈ CombatConfig의 windup6+active2+recovery12, Lunge는 블링크 3틱이 너무
        //  짧아 도착 후 여운까지 포함해 넉넉히 잡는다.)
        public const int GhostAttackWindowTicks = 20;
        public const float GhostAttackFromNormalized = 0.28f;   // 스윙 시작
        public const float GhostAttackToNormalized = 0.62f;     // 임팩트 후 따라나감

        // [찌르기 전용 포즈, 2026-07-22] 런지는 Slash 클립 앞부분을 빌려 쓰다가, 전용 2키 클립
        // GhostLunge(준비 → 꽂힘, GhostDashPoseBaker가 굽는다)로 바뀌었다. 클립 전체가 곧 찌르기
        // 동작이므로 정규화 구간은 0→1을 그대로 쓴다.
        public const int GhostLungeWindowTicks = 12;
        public const float GhostLungeFromNormalized = 0f;       // 찌르기 준비(웅크려 장전)
        public const float GhostLungeToNormalized = 1f;         // 꽂히는 순간(완전히 뻗음)
        /// <summary>런지 잔상 전방 기울기(도) — 준비에서 꽂힘까지 구간에 걸쳐 이만큼 깊어진다.
        /// GhostDashPoseBaker.LungePrepPitch/LungeHitPitch와 짝이므로 포즈를 다시 구우면 같이 맞출 것.</summary>
        public const float GhostLungeFromPitch = 18f;
        public const float GhostLungeToPitch = 60f;

        public const int GhostJumpWindowTicks = 34;
        public const float GhostJumpFromNormalized = 0.14f;
        public const float GhostJumpToNormalized = 0.62f;

        /// <summary>대시 지속(연출용). Sim의 SimConfig.DashDurationTicks(24)와 맞춰둔다.</summary>
        public const int GhostDashWindowTicks = 24;
        // 방향별 대시 "몸통 눕힘". 클립(GhostDashForward/…)에는 팔다리 각도만 들어 있고, 몸 전체를
        // 눕히는 각도는 여기서 배치 회전으로 준다 — AnimationClip.SampleAnimation이 휴머노이드
        // 클립의 루트 회전(RootQ)을 적용하지 않아서 클립 안에 담을 수가 없다(실측 확인).
        // GhostDashPoseBaker가 포즈를 구울 때 전제한 각도이므로, 포즈를 다시 구우면 여기도 같이 맞출 것.
        /// <summary>앞 대시 — 스프린트 발진처럼 앞으로 깊게 눕는다(+ = 앞).</summary>
        public const float GhostDashForwardPitch = 55f;
        /// <summary>뒤 대시 — 뒤로 눕는다(− = 뒤). [피드백 반영, 2026-07-22] -40°는 "넘어지는"
        /// 그림이라 -20°로 완화 — 상체를 뒤로 힘주며 버티되 두 발은 땅 가까이 남는다.</summary>
        public const float GhostDashBackwardPitch = -20f;
        /// <summary>옆 대시 — 얼굴은 정면인 채 몸만 가는 쪽으로 기운다(roll). 앞 대시와의 결정적 차이.</summary>
        public const float GhostDashSideRoll = 30f;
        /// <summary>포즈 샘플 시각을 이 해상도(초당 스텝)로 양자화한다. 같은 칸이면 재샘플을
        /// 건너뛰므로, 움직이지 않는 정지 잔상은 사실상 한 번만 굽는다(휴머노이드 리타게팅 비용 절감).</summary>
        // [잔상 밀도 상향, 2026-07-22] 분신이 한 자리에 고정돼 포즈를 한 번만 굽게 된 뒤로는
        // 재샘플 비용이 사실상 없다 — 해상도를 올려 인접 잔상 사이 포즈 차이를 매끄럽게 만든다.
        public const float GhostPoseSampleRate = 60f;
        // <<< [잔상 아바타 애니메이션 끝]
        public const float DomeWidth     = 0.18f;
        public static readonly Color DomeColor  = new Color(0.3f, 0.9f, 1f, 0.9f);
        public static readonly Color GhostColor = new Color(0.5f, 0.9f, 1f, 0.5f);   // 정지 잔상(반투명)
        public static readonly Color StartMarkerColor = new Color(0.85f, 1f, 0.75f);  // 시작점(=나), 불투명 밝은 연두

        // 정지 포스트fx (산데비스탄 에메랄드 틴트 + 비네트)
        public const float FxSaturation      = 0f;
        public const float FxExposure        = -0.3f;
        public static readonly Color FxTint  = new Color(0.75f, 0.95f, 0.85f);
        public const float FxVignette        = 0.55f;
        public const float FxVignetteSmooth  = 0.65f;
        public static readonly Color FxVignetteColor = new Color(0.02f, 0.08f, 0.05f);
        public const float FxWeightSpeed     = 8f;   // 정지 진입/해제 페이드 속도

        // [2026-07-21 추가] 정지 진입 색반전(RadialInvertFeature) — 카메라 pull-back 진행률(0~1)에
        // 맞춰 화면 중심에서 원이 자라며 반전된다. MaxRadius는 화면비 보정 UV 기준 화면 대각선의
        // 절반(1인칭 중심 기준 코너까지 거리)보다 넉넉하게 잡아 초광각 화면에서도 t=1에 완전히 덮게 한다.
        public const float RadialInvertMaxRadius = 1.35f;

        // Following(자동실행) 1인칭 카메라 회전 제한(도/초) — 예측이 겨냥을 홱 바꿔도 화면이
        // 순간이동하듯 스냅되지 않고, 사용자가 지금 무슨 방향으로 도는지 눈으로 따라올 수
        // 있게 제한된 속도로 회전한다. 시간 배속(슬로모)과는 무관 — 이건 항상 실시간 그대로.
        public const float FollowingCamTurnSpeed = 300f;

        // 성공 입력부터 다음 액션 잔상까지 실제 시간 1초를 목표로 연속 보정한다.
        // [예측 세션 수정, 2026-07-21] 속도감 튜닝: GoodWindowTicks를 8→11로 넓혀 확보한
        // 여유를 슬로모 강도를 줄이는 데 쓰고, 대신 빠른/느린 구간의 대비를 키워서
        // "쭉 빠르게 이동하다 임팩트 직전 한 박자만 확 느려지는" 리듬감을 낸다.
        public const float RhythmNormalMinSeconds = 0.4f;
        public const float RhythmNormalMaxSeconds = 0.85f;
        public const float RhythmNormalReadPadding = 0.18f;
        public const float RhythmComboMinSeconds = 0.22f;
        public const float RhythmComboMaxSeconds = 0.38f;
        public const float RhythmComboReadPadding = 0.08f;
        public const float RhythmComboPositionRadius = 0.9f;
        public const int RhythmComboMaxGapTicks = 24;
        public const float RhythmMinTimeScale = 0.5f;
        public const float RhythmMaxTimeScale = 1.7f;
        public const float RhythmCurveMinSeconds = 0.12f;
        // 세그먼트 중 감속(느려지는) 구간이 앞부분까지 잠식하지 않도록, 감속 시작 지점의
        // 하한을 세그먼트의 마지막 30%로 고정한다 — 나머지 70%는 항상 빠른 스케일을 쓴다.
        public const float RhythmDecelStartFloor = 0.7f;

        // [예측 세션 추가, 2026-07-21] 이동/회전 완급 페이싱. 이벤트까지 남은 시간 기준의
        // 위 감속 커브 위에 얹히는 틱별 보정 — 대시·런지 트리거 직후 몇 틱은 스케일을 강제로
        // 확 끌어올려 "쫀득한" 스냅을 주고(오버라이드), 순수 회전(제자리 선회) 중에는 반대로
        // 낮춰서 방향 전환을 눈으로 따라올 여유를 준다(기존 target에 곱하는 감쇠).
        public const float RhythmBurstTimeScale = 2.4f;   // 대시/런지 직후 강제 스케일
        public const int   RhythmBurstTicks = 12;          // 트리거 틱 이후 이 틱 수만큼 유지
        public const float RhythmTurnTimeScale = 0.55f;    // 순수 회전 구간에 곱하는 감쇠 배율
        public const float RhythmTurnYawDegPerTick = 2.5f; // 이 이상 틱당 요 변화면 "회전 중"
        public const float RhythmTurnMoveSpeedThreshold = 1.5f; // 이 미만 이동속도(유닛/초)여야 회전으로 간주
        // 다음 판정 틱까지 이 틱 수보다 많이 남았으면 실시간 기반 감속 커브를 무시하고 최고
        // 속도로 유지한다(짧은 액션이 줄줄이 이어질 때 매번 멈췄다 가는 느낌을 없애기 위함).
        public const int RhythmApproachTicks = 20;
        // [예측 세션 추가, 2026-07-21] 판정이 한참 남은 순수 이동 구간 전용 상한 — 걷는 속도감을
        // 더 키워달라는 피드백으로 RhythmMaxTimeScale(1.7)보다 한 단계 더 빠르게 잡는다.
        public const float RhythmWalkTimeScale = 2.6f;
        // [예측 세션 수정, 2026-07-21] 0.42 → 0.6 → 0.85: 판정이 너무 빡세다는 피드백 — 이벤트
        // 도달 후 입력을 기다려주는 실시간 유예를 늘려서 Miss로 강제 전환(직접 조작行)되기까지
        // 여유를 준다. RhythmJudge.GoodWindowTicks(22)와 짝을 맞춰 재조정.
        public const float RhythmWaitGoodSeconds = 0.85f;
        // 예측 경로 확정 직후의 첫 박자는 시간 제한 없이 사용자가 원하는 순간에 직접 누른다
        // (TryConsumeFollowingInput이 pending==0일 때 Miss 판정을 걸지 않음) — 이 값은 오직
        // 접근링 연출 속도용이며 실제 입력 마감과는 무관하다.
        public const float RhythmFirstBeatDisplaySeconds = 1.2f;
        public const float ExecutionGhostAlpha = 0.16f;
        public const float ExecutionGhostFadeNear = 0.65f;
        public const float ExecutionGhostFadeFar = 3.2f;

        // >>> [다음 잔상 강조, 2026-07-22] Following 중 "다음에 어디로 가야 하는가"가 안 읽힌다는
        // 피드백. 예전엔 판정 대상 잔상이 alpha 0.28, 지나간 잔상이 0.16, 남은 잔상이 0.08로
        // 차이가 거의 없었고, 게다가 셋 다 ExecutionGhostFade* 근접 페이드를 그대로 먹어서
        // <b>가까워질수록 흐려졌다</b> — 목표에 도착할 때쯤 그 목표가 사라지는 구조였다.
        // 이제 판정 대상(=다음 액션)만 확실히 띄우고, 그 다음 것을 중간 밝기로 예고한다.
        /// <summary>지금 쳐야 할 액션 잔상의 기본 불투명도.</summary>
        public const float GhostNextAlpha = 0.85f;
        /// <summary>그 위에 얹히는 맥동 진폭(±). 시선을 끌되 깜빡임으로 읽히지 않을 정도.</summary>
        public const float GhostNextPulseAmplitude = 0.16f;
        /// <summary>맥동 주기(Hz). 실시간 기준 — 슬로모와 무관하게 일정하게 뛴다.</summary>
        public const float GhostNextPulseHz = 1.8f;
        /// <summary>다음 잔상에만 적용하는 근접 페이드 하한 — 코앞에 와도 이 아래로 안 흐려진다.</summary>
        public const float GhostNextProximityFloor = 0.6f;
        public const float GhostNextWhiteBlend = 0.5f;
        /// <summary>다음의 다음 액션 잔상 — "그 뒤엔 저기"를 미리 알려주는 예고 단계.</summary>
        public const float GhostAfterNextAlpha = 0.34f;
        public const float GhostAfterNextWhiteBlend = 0.2f;
        /// <summary>아직 한참 남은 잔상(예전 하드코딩 0.08).</summary>
        public const float GhostFutureAlpha = 0.07f;
        // <<< [다음 잔상 강조 끝]
        public static readonly Color ExecutionFxTint = new Color(0.52f, 1f, 0.62f);
        public static readonly Color ExecutionFxVignetteColor = new Color(0.01f, 0.22f, 0.06f);
        public static readonly Color ExecutionPlayerColor = new Color(0.25f, 1f, 0.62f);
        public const float RhythmSidePromptAlpha = 0.38f;
        public const float ExecutionSpeedLineAlpha = 0.13f;
        public const float ExecutionSpeedLineRate = 2.8f;
    }
}
