namespace Game.Sim
{
    /// <summary>몹 AI 세부 수치. ★ AI 세션 소유. 전부 잠정·튜닝. DOOM 비공개 수치는 상대값/텔레그래프로.</summary>
    public static class AIConfig
    {
        // ── 근접 그런트 ──
        // 사거리 = 적 반경(개별) + 플레이어 반경 + 팔 길이. 대형몹은 반경이 커 사거리도 자동으로 커짐.
        public const float MeleeReach         = 0.8f;  // 팔 길이
        public const float MeleeHitExtra      = 0.3f;  // 판정 여유
        public const int   MeleeWindupTicks   = 24;   // 0.4s 선딜·텔레그래프(committed 조준)
        public const int   MeleeActiveTicks   = 6;    // 0.1s 판정
        public const int   MeleeRecoveryTicks = 24;   // 0.4s 후딜
        public const float MeleeHitHalfAngle  = 50f;  // committed 방향 부채꼴
        public const int   MeleeDamage        = 1;

        /// <summary>개별 반경 기반 근접 사거리(대형몹 자동 반영).</summary>
        public static float MeleeRangeFor(float enemyRadius) => enemyRadius + SimConfig.PlayerRadius + MeleeReach;

        // ── 돌진 (핑키형) — 근접 × Charge. 완주(피격으로 안 끊김) ──
        public const float ChargeRadiusMul    = 1.5f;  // 반경 1.5배
        public const float ChargeMinRange     = 6f;    // 이 안 + 시야면 돌진 개시
        public const int   ChargeWindupTicks  = 30;    // 0.5s 텔레그래프(committed)
        public const float ChargeSpeed        = 14f;   // 적당한 속도로 쭉
        public const float ChargeMaxDist      = 20f;   // 매우 긴 사거리
        public const int   ChargeDamage       = 1;     // 접촉 피해
        public const int   ChargeHitRecovery  = 24;    // 성공 후딜(짧음)
        public const int   ChargeMissRecovery = 40;    // 실패 후딜(김)
        public const float ChargeWallStopFrac = 0.4f;  // 이번 틱 이동이 의도의 이 비율 미만 = 벽 정지

        // ── 지각(perception) ──
        public const float EnemyEyeHeight = 0.8f;   // LOS 레이 원점(적)·발사 원점
        public const float PlayerTorso    = 0.7f;   // LOS 겨냥점(플레이어 몸통)

        // ── 원거리 솔저 (플라즈마) ──
        public const float RangedMoveSpeed = 4f;    // 플레이어 0.6× (느림)
        public const float RangedBandMin   = 4f;    // 이보다 가까우면 후퇴(반토막)
        public const float RangedBandMax   = 16f;   // 이보다 멀면 접근
        public const int   RangedAimTicks  = 36;    // 0.6s 큰 텔레그래프(committed)
        public const int   RangedCooldown  = 90;    // 발사 후 재발사까지 1.5s
        public const int   RangedDamage    = 1;

        // 투사체 (유도 없음 → 회피 가능)
        public const float ProjectileSpeed  = 12f;
        public const float ProjectileRadius = 0.25f;
        public const int   ProjectileTtl    = 300;  // 5s 안전 소멸

        // 속도 빗맞힘 (DOOM): 발사 확정 시 플레이어가 대시 중이면 일부러 빗나가게
        public const float MissOffsetDeg = 18f;

        // 리드(예측) 조준: 플레이어 속도로 투사체 도달시간만큼 앞을 겨냥하되,
        // 완벽 리드(1)는 불공정 → "아주 약간"만(0.5). 0=현재위치(리드 없음), 1=완벽. 핵심 튜닝값.
        public const float LeadFactor = 0.5f;
    }
}
