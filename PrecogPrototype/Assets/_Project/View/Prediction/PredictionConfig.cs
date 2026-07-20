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
        public const float RhythmNormalMinSeconds = 0.55f;
        public const float RhythmNormalMaxSeconds = 0.85f;
        public const float RhythmNormalReadPadding = 0.18f;
        public const float RhythmComboMinSeconds = 0.22f;
        public const float RhythmComboMaxSeconds = 0.38f;
        public const float RhythmComboReadPadding = 0.08f;
        public const float RhythmComboPositionRadius = 0.9f;
        public const int RhythmComboMaxGapTicks = 24;
        public const float RhythmMinTimeScale = 0.35f;
        public const float RhythmMaxTimeScale = 1f;
        public const float RhythmCurveMinSeconds = 0.12f;
        public const float RhythmWaitGoodSeconds = 0.42f;
        public static readonly Color ExecutionRouteColor = new Color(0.15f, 1f, 0.55f);
        public const float ExecutionGhostAlpha = 0.16f;
        public const float ExecutionGhostHueSpeed = 0.08f;
        public const float ExecutionGhostFadeNear = 0.65f;
        public const float ExecutionGhostFadeFar = 3.2f;
        public static readonly Color ExecutionFxTint = new Color(0.52f, 1f, 0.62f);
        public static readonly Color ExecutionFxVignetteColor = new Color(0.01f, 0.22f, 0.06f);
        public static readonly Color ExecutionPlayerColor = new Color(0.25f, 1f, 0.62f);
    }
}
