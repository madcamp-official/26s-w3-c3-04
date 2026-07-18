namespace Game.Sim
{
    /// <summary>전투 세부 수치. 전부 잠정·튜닝 대상.</summary>
    public static class CombatConfig
    {
        // 평타 단계
        public const byte PhNone = 0, PhWindup = 1, PhActive = 2, PhRecovery = 3;

        // 평타 (좌클릭): 부채꼴 광역, 이동보정 없음. 겐지 용검식 타이밍(잠정).
        public const int AttackWindupTicks   = 3;   // 선딜 짧게 치켜듦 (0.05초)
        public const int AttackActiveTicks   = 4;   // 슬래시 판정
        public const int AttackRecoveryTicks = 22;  // 긴 후딜 — 스킬로 캔슬 가능
        public const int AttackTotalTicks    = AttackWindupTicks + AttackActiveTicks + AttackRecoveryTicks;
        public const float AttackConeRange     = 2.2f;   // 크기 축소 반영
        public const float AttackConeHalfAngle = 55f;

        // 공통 대미지/스턴
        public const int Damage    = 1;
        public const int StunTicks = 30;  // 0.5초 (60틱=1초)

        // 플레이어 체력·피격 방어 (적 공격 도입)
        public const int   PlayerMaxHp     = 20;   // 잠정 — 무방비 20대 맞으면 사망
        public const int   GuardHitCost    = 30;   // 정면 막기로 1대 막을 때 가드 소모
        public const float BlockHalfAngle  = 70f;  // 이 각 안에서 오는 정면 히트만 막힘

        // ── 막기 (우클릭 홀드): 아직 적 공격 없음 → 게이지 관리만 구현 ──
        // 회복 "매우 느리게": 지연 후, Interval 틱마다 1씩만. (240 만충 ≈ 16초, 1/4 ≈ 4초)
        public const int GuardRegenDelay    = 90; // 비홀드 이만큼 지나야 회복 시작 (1.5초)
        public const int GuardRegenInterval = 2;  // 이 틱마다 1 회복 (2배 빠르게: 만충 ≈ 8초, 1/4 ≈ 2초)

        // ── 칼등치기 (막기 중 좌클릭): 글로리킬식 러쉬 + 넉백 ──
        public const byte BsNone = 0, BsLunge = 1, BsRecovery = 2;
        public const int   BackstrikeLungeTicks    = 7;    // 러쉬 지속 (약 0.12초)
        public const int   BackstrikeRecoveryTicks = 8;    // 자기경직 (약 0.13초)
        public const float BackstrikeAimRange      = 7f;   // 조준선 근처 탐색 거리
        public const float BackstrikeAimHalfAngle  = 32f;  // 조준선 근처(둠은 전방위, 우린 좁게)
        public const float BackstrikeLungeGap      = 0.85f;// 대상 코앞 이 거리까지 (러쉬는 남은틱에 무조건 도착)
        public const float BackstrikeHitRadius     = 1.8f; // 임팩트 때 대상 인정 반경
        public const float BackstrikeSplashRadius  = 2.6f; // 임팩트 시 주변 적도 함께 타격
        public const int   BackstrikeGuardCost     = SimConfig.GuardMax / 4;  // 4등분 = 4회

        // ── 넉백 (순간이동식) ──
        // 글로리킬과 사실상 동일 + "아주 약간"의 밀치기만. 화면연출은 별도. 벽 관통만 방지.
        public const float KnockbackDist = 1.2f;

        // ── 질풍참 관통 (이동은 PlayerMovement, 판정만 여기) ──
        public const float DashPierceRadius = 1.0f;

        // ── 대형몹 글로리킬 처형 (막타 → 컷신). 진행 중 플레이어 무적·조작잠금 ──
        public const byte  GlNone = 0, GlSlash1 = 1, GlSlash2 = 2, GlDash = 3;
        public const int   GlorySlashTicks = 30;   // 슬래시 1·2 각각 (0.5s — 모션 보이게)
        public const int   GloryDashTicks  = 17;   // 질풍참 피니시 (0.28s ≈ 일반 질풍참 1.5배 길이)
        public const float GloryDashSpeed  = 40f;  // 러쉬 속도 → 거리 ≈ 일반 질풍참 1.5배
        // 총 컷신 ≈ 30+30+17 = 77틱 ≈ 1.28초
    }
}
