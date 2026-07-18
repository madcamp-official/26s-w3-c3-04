namespace Game.Sim
{
    /// <summary>
    /// 전투 세부 수치. static = F1 튜닝 패널에서 실시간 조정(예측 중 변경 금지),
    /// const = 구조 상수(단계 식별자 등 — 변경 불가). 막기·가드·칼등치기·질풍참·스턴은 폐기 사양.
    /// </summary>
    public static class CombatConfig
    {
        // 평타 단계 (구조 상수 — switch 라벨용)
        public const byte PhNone = 0, PhWindup = 1, PhActive = 2, PhRecovery = 3;

        // 평타 (좌클릭): 부채꼴 광역, 이동보정 없음. 판정은 Active 진입 시 1회.
        public static int AttackWindupTicks   = 6;    // 0.10초
        public static int AttackActiveTicks   = 2;    // 슬래시 판정
        public static int AttackRecoveryTicks = 12;   // 0.20초 — 총 0.33초
        public static float AttackConeRange       = 1.8f;
        public static float AttackConeHalfAngle   = 55f;
        public static float AttackHeightTolerance = 1.0f;   // 높이차 허용

        // 공통 대미지 (스턴 부여는 폐기 — 피해만)
        public const int Damage = 1;

        // 플레이어 체력·피격
        public static int PlayerMaxHp        = 1000000;   // 임시: 테스트용 무한 체력(원래 3)
        public static int PlayerHitStunTicks = 0;   // 임시: 피격 경직 0(원래 30). 구조는 유지

        // ── 타깃 런지 (우클릭): 제2의 평타(갭클로저 딜). 쿨 없음, 후딜로만 페이스 조절 ──
        public const byte LgNone = 0, LgWindup = 1, LgTravel = 2, LgRecovery = 3;
        public static int   LungeWindupTicks    = 3;
        public static float LungeTravelSpeed    = 34f;   // m/s — Travel 틱 = 거리/속도 (거리 비례)
        public static int   LungeTravelMinTicks = 4;     // 너무 순간이동 같지 않게 하한
        public static int   LungeRecoveryTicks  = 10;    // 후딜(유일한 페이스 제약)
        public static int   LungeCooldownTicks  = 0;     // 쿨 없음(패널에서 부활 가능)
        public static float LungeMinRange       = 1.2f;
        public static float LungeMaxRange       = 12f;
        public static float LungeHalfAngle      = 30f;   // 정면 반각
        public static float LungeStopDistance   = 0.9f;  // 적 앞 이 거리 지점으로 이동
        public static float LungeHeightTolerance = 0.8f; // 플레이어와 높이차 허용
        public static int   LungeBindExtraTicks = 8;     // 바인드 = 윈드업+Travel+이 여유

        // ── 대형몹 글로리킬 처형 (막타 → 컷신). 진행 중 플레이어 무적·조작잠금 ──
        public const byte  GlNone = 0, GlSlash1 = 1, GlSlash2 = 2, GlDash = 3;
        public static int   GlorySlashTicks = 30;   // 슬래시 1·2 각각 (0.5s — 모션 보이게)
        public static int   GloryDashTicks  = 17;   // 피니시 러쉬 (0.28s)
        public static float GloryDashSpeed  = 40f;  // 러쉬 속도
    }
}
