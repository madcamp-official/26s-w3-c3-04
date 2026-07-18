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

        // 루트 색 (순서: 근접순 / 원거리순 / 스윕)
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
        public const float DomeWidth     = 0.18f;
        public static readonly Color DomeColor  = new Color(0.3f, 0.9f, 1f, 0.9f);
        public static readonly Color GhostColor = new Color(0.5f, 0.9f, 1f, 0.5f);
        public static readonly Color StartMarkerColor = new Color(0.85f, 1f, 0.75f);  // 시작점(=나), 불투명 밝은 연두
        public const float GhostLoopPause = 1.2f;   // 경로 끝에서 반복 전 여유 거리
        public const float KillMarkY      = 1.3f;   // 처치 마커 높이

        // 정지 포스트fx (흑백 + 청록 틴트 + 비네트)
        public const float FxSaturation      = -100f;
        public const float FxExposure        = -0.3f;
        public static readonly Color FxTint  = new Color(0.62f, 0.82f, 1f);
        public const float FxVignette        = 0.5f;
        public const float FxVignetteSmooth  = 0.65f;
        public static readonly Color FxVignetteColor = new Color(0.03f, 0.07f, 0.12f);
        public const float FxWeightSpeed     = 8f;   // 정지 진입/해제 페이드 속도
    }
}
