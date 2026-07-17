namespace Game.Simulation
{
    /// <summary>
    /// 시뮬레이션 전역 상수. Time.fixedDeltaTime을 직접 쓰지 않고
    /// 여기 정의한 TickDelta만 사용한다 → 프로젝트 설정이 바뀌어도
    /// 시뮬레이션이 흔들리지 않는다. (ADR-0003 참고)
    /// </summary>
    public static class SimConfig
    {
        public const int   TickRate  = 60;
        public const float TickDelta  = 1f / TickRate;   // 한 틱 = 1/60초

        // 배열 용량 (고정). struct 배열을 풀처럼 쓰기 위해 상한을 둔다.
        public const int MaxEnemies    = 64;
        public const int MaxProjectiles = 128;

        // 물리 (프로토타입 값. 밸런싱 대상)
        public const float Gravity       = -25f;   // m/s^2
        public const float GroundY       = 0f;     // 1층 바닥 높이

        // 플레이어 (묶음 A에서는 최소한만)
        public const float PlayerMoveSpeed = 7f;
        public const float PlayerJumpSpeed = 9f;
        public const int   PlayerMaxHp     = 3;
        public const float PlayerRadius    = 0.4f;  // 구 판정 반지름

        // 일반 근접 적 (묶음 A의 유일한 적)
        public const float MeleeAggroRange  = 25f;   // 이 안에 들어오면 추적 시작
        public const float MeleeSpeed       = 4f;
        public const float MeleeAttackRange = 1.6f;  // 이 안이면 공격
        public const float MeleeRadius      = 0.5f;
        public const int   MeleeWindupTicks   = 24;  // 선딜 0.4초
        public const int   MeleeActiveTicks   = 6;   // 판정 0.1초
        public const int   MeleeRecoveryTicks = 30;  // 후딜 0.5초
        public const int   MeleeAttackCooldownTicks = 12;

        // ── 플레이어 전투 (묶음 B). 전부 잠정값 = 밸런싱 대상 ──

        // 평타 (주력): 부채꼴 광역 + 글로리킬식 보정 전진
        public const int   AttackWindupTicks   = 3;   // 선딜 0.05초 (경쾌하게)
        public const int   AttackActiveTicks   = 4;   // 판정
        public const int   AttackRecoveryTicks = 8;   // 후딜
        public const int   AttackCooldownTicks = 4;
        public const float AttackConeRange     = 3.0f;
        public const float AttackConeHalfAngle = 55f;  // 부채꼴 반각 (약간 광역)
        public const float AttackLungeSpeed    = 14f;  // 보정 전진 속도 (windup+active)
        public const int   AttackIFrameTicks   = 5;    // 보정 중 짧은 무적 (관통방지)

        // 질풍참: 관통 이동 + 스턴 부여 + 돌진 중 정면 막기
        public const float DashDistance      = 8f;
        public const int   DashDurationTicks = 9;    // 0.15초 동안 돌진
        public const int   DashMaxCharges    = 2;
        public const int   DashRechargeTicks = 90;   // 충전 1회당 1.5초 (순수 쿨, 리셋 없음)
        public const int   DashStunTicks     = 60;   // 관통당한 적 경직 1초
        public const float DashHitRadius     = 1.0f; // 돌진 선분 판정 두께

        // 막기: 정면만, 가드 게이지
        public const float BlockFrontHalfAngle = 90f;  // 정면 판정 반각
        public const int   GuardMaxTicks       = 180;  // 게이지 최대 (3초분)
        public const int   GuardHitCostTicks   = 45;   // 피격 1회 막을 때 소모
        public const int   GuardRegenDelayTicks = 30;  // 비홀드 후 회복 시작까지 (안 b: 비홀드후 회복)
        public const int   GuardRegenPerTick    = 2;   // 회복 속도

        // 플레이어 피격 판정 반경 (적 공격이 닿는지)
        public const float PlayerHurtRadius = 0.5f;
    }
}
