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
        public static float AttackConeRange       = 2.6f;
        public static float AttackConeHalfAngle   = 55f;
        public static float AttackHeightTolerance = 1.0f;   // 높이차 허용

        // 공통 대미지 (스턴 부여는 폐기 — 피해만)
        public const int Damage = 1;

        // 플레이어 체력·피격
        public static int PlayerMaxHp        = 1000000;   // 임시: 테스트용 무한 체력(원래 3)
        public static int PlayerHitStunTicks = 0;   // 임시: 피격 경직 0(원래 30). 구조는 유지

        // ── 타깃 런지 (우클릭): 둠 글로리킬식. 순간이동급 블링크 → 아래→위 베기. 블링크 틱만 잠금 ──
        public const byte LgNone = 0, LgWindup = 1, LgTravel = 2, LgRecovery = 3;
        public static int   LungeWindupTicks    = 0;     // 없음(즉시 발동)
        public static int   LungeTravelTicks    = 3;     // 블링크(순간이동급). 이 틱만 이동·에임 잠금
        public static int   LungeRecoveryTicks  = 0;     // 없음(도착 즉시 조작 복귀)
        public static int   LungeCooldownTicks  = 15;    // 0.25초 연발 제한
        public static int   LungeMaxStacks      = 2;     // 스택 상한(2). 처치로 +1 충전, 발동 1 소모
        public static int   LungeReserveWindow  = 10;    // 쿨 막판 이 틱 이내(≈0.17초) 클릭 → 예약
        public static float LungeMinRange       = 1.2f;
        public static float LungeMaxRange       = 12f;
        public static float LungeAimRadius      = 2.0f;  // 조준 레이 수직 보정 반경(판정 핵심)
        public static float LungeStopDistance   = 0.9f;  // 적 앞 이 거리 지점으로 이동
        public static float LungeHeightTolerance = 6f;   // 위/아래 허용 높이차(공중 대상 포함)
        public static float LungeAimUp          = 0.4f;  // 도착점을 적보다 살짝 위로(딱 붙는 느낌)
        public static int   LungeBindExtraTicks = 4;     // 바인드 = 블링크+이 여유
        // 임팩트 쫀득함 (View 전용 — 예측 무해)
        public static int   LungeHitStopTicks   = 7;     // 접촉 순간 프리즈(글로리킬 느낌)
        public static float LungeFovKick        = 12f;   // 접촉 순간 FOV 킥(도)

        // ── 대형몹 글로리킬 처형 (막타 → 컷신). 진행 중 플레이어 무적·조작잠금 ──
        public const byte  GlNone = 0, GlSlash1 = 1, GlSlash2 = 2, GlDash = 3;
        public static int   GlorySlashTicks = 7;    // 슬래시 1·2 각각(빠르게). 7+7+10=24틱 ≈ 0.4초
        public static int   GloryDashTicks  = 10;   // 피니시 올려베기
        public static float GloryDashSpeed  = 40f;  // 러쉬 속도
    }
}
