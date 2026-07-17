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
    }
}
