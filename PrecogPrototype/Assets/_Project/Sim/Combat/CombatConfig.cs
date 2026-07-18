namespace Game.Sim
{
    /// <summary>전투 세부 수치. 전부 잠정·튜닝 대상. (막기·가드·칼등치기·질풍참은 폐기 사양)</summary>
    public static class CombatConfig
    {
        // 평타 단계
        public const byte PhNone = 0, PhWindup = 1, PhActive = 2, PhRecovery = 3;

        // 평타 (좌클릭): 부채꼴 광역, 이동보정 없음. 판정은 Active 진입 시 1회.
        public const int AttackWindupTicks   = 6;    // 0.10초
        public const int AttackActiveTicks   = 2;    // 슬래시 판정
        public const int AttackRecoveryTicks = 12;   // 0.20초 — 총 0.33초
        public const int AttackTotalTicks    = AttackWindupTicks + AttackActiveTicks + AttackRecoveryTicks;
        public const float AttackConeRange       = 1.8f;
        public const float AttackConeHalfAngle   = 55f;
        public const float AttackHeightTolerance = 1.0f;   // 높이차 허용

        // 공통 대미지/스턴
        public const int Damage    = 1;
        public const int StunTicks = 30;  // 0.5초 (60틱=1초)

        // 플레이어 체력·피격
        public const int PlayerMaxHp       = 3;
        public const int PlayerHitStunTicks = 30;  // 피격 경직 0.5초 (잠정)

        // ── 타깃 런지 (우클릭): 공격 진입기. 피해 1 + 스턴. 무적 없음 ──
        public const byte LgNone = 0, LgWindup = 1, LgTravel = 2, LgRecovery = 3;
        public const int   LungeWindupTicks   = 3;
        public const int   LungeTravelTicks   = 5;
        public const int   LungeRecoveryTicks = 10;
        public const int   LungeCooldownTicks = 120;   // 2초
        public const float LungeMinRange      = 1.2f;
        public const float LungeMaxRange      = 8f;
        public const float LungeHalfAngle     = 30f;   // 정면 반각
        public const float LungeStopDistance  = 0.9f;  // 적 앞 이 거리 지점으로 이동
        public const float LungeHeightTolerance = 0.8f; // 플레이어와 높이차 허용

        // ── 대형몹 글로리킬 처형 (막타 → 컷신). 진행 중 플레이어 무적·조작잠금 ──
        public const byte  GlNone = 0, GlSlash1 = 1, GlSlash2 = 2, GlDash = 3;
        public const int   GlorySlashTicks = 30;   // 슬래시 1·2 각각 (0.5s — 모션 보이게)
        public const int   GloryDashTicks  = 17;   // 피니시 러쉬 (0.28s)
        public const float GloryDashSpeed  = 40f;  // 러쉬 속도
        // 총 컷신 ≈ 30+30+17 = 77틱 ≈ 1.28초
    }
}
