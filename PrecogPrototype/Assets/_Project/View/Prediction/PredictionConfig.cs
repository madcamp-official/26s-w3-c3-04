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

        // 루트 색 (순서: PredictionPlanner.PlanByProfile이 고정하는 안전형/기회형/공격형)
        public static readonly Color[] RouteColors =
        {
            new Color(0.2f, 1f, 0.9f),   // 청록
            new Color(1f, 0.35f, 0.7f),  // 자홍
            new Color(1f, 0.8f, 0.2f),   // 주황
        };

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
        // 이동 트레일이 옅어져 사라지기까지 걸리는 시간(초) — 위 스윕 시간의 절반 조금 안 되게
        // 맞춰서, 트레일 꼬리 길이가 "지나온 절반 구간"처럼 보이게 한다.
        public const int PreviewAfterimageCount = 32;
        public const float PreviewAfterimageSpacing = 0.0063f;
        public const float PreviewAfterimageHeadAlpha = 0.38f;
        public const float PreviewAfterimageFadeSeconds = 1.6f;
        public static readonly Color PreviewPathGreen = new Color(0.12f, 1f, 0.35f);
        public static readonly Color PreviewPathBlue = new Color(0.08f, 0.4f, 1f);
        public static readonly Color PreviewPathPurple = new Color(0.72f, 0.12f, 1f);
        public static readonly Color PreviewPathRed = new Color(1f, 0.06f, 0.03f);
        public const float MissGlitchSeconds = 0.38f;
        public const float MissGlitchMaxAlpha = 0.58f;
        // <<< [예측 세션 추가 끝]
        public const float DomeWidth     = 0.18f;
        public static readonly Color DomeColor  = new Color(0.3f, 0.9f, 1f, 0.9f);
        public static readonly Color GhostColor = new Color(0.5f, 0.9f, 1f, 0.5f);   // 정지 잔상(반투명)
        public static readonly Color StartMarkerColor = new Color(0.85f, 1f, 0.75f);  // 시작점(=나), 불투명 밝은 연두

        // 정지 포스트fx (흑백 + 청록 틴트 + 비네트)
        public const float FxSaturation      = -100f;
        public const float FxExposure        = -0.3f;
        public static readonly Color FxTint  = new Color(0.62f, 0.82f, 1f);
        public const float FxVignette        = 0.5f;
        public const float FxVignetteSmooth  = 0.65f;
        public static readonly Color FxVignetteColor = new Color(0.03f, 0.07f, 0.12f);
        public const float FxWeightSpeed     = 8f;   // 정지 진입/해제 페이드 속도

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
        // [예측 세션 수정, 2026-07-21] 0.42 → 0.6: 판정이 너무 빡세다는 피드백 — 이벤트 도달 후
        // 입력을 기다려주는 실시간 유예를 늘려서 Miss로 강제 전환(직접 조작行)되기까지 여유를 준다.
        public const float RhythmWaitGoodSeconds = 0.6f;
        // 예측 경로 확정 직후의 첫 박자는 시간 제한 없이 사용자가 원하는 순간에 직접 누른다
        // (TryConsumeFollowingInput이 pending==0일 때 Miss 판정을 걸지 않음) — 이 값은 오직
        // 접근링 연출 속도용이며 실제 입력 마감과는 무관하다.
        public const float RhythmFirstBeatDisplaySeconds = 1.2f;
        public const float ExecutionGhostAlpha = 0.16f;
        public const float ExecutionGhostFadeNear = 0.65f;
        public const float ExecutionGhostFadeFar = 3.2f;
        public static readonly Color ExecutionFxTint = new Color(0.52f, 1f, 0.62f);
        public static readonly Color ExecutionFxVignetteColor = new Color(0.01f, 0.22f, 0.06f);
        public static readonly Color ExecutionPlayerColor = new Color(0.25f, 1f, 0.62f);
        public const float RhythmSidePromptAlpha = 0.38f;
        public const float ExecutionSpeedLineAlpha = 0.13f;
        public const float ExecutionSpeedLineRate = 2.8f;
    }
}
