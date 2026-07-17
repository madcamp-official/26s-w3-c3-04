namespace Game.Sim
{
    /// <summary>
    /// 시뮬레이션 전역 상수. Time.fixedDeltaTime 대신 TickDelta만 쓴다.
    /// 전투 수치는 여기 없다(전투는 다음 단계). 이동·충돌 값만.
    /// </summary>
    public static class SimConfig
    {
        public const int   TickRate  = 60;
        public const float TickDelta = 1f / TickRate;

        public const int   MaxEnemies = 64;

        public const float Gravity = -25f;

        // 플레이어 (캡슐: 발밑 pos 기준, 위로 Height)
        public const float PlayerMoveSpeed = 7f;
        public const float PlayerJumpSpeed = 9f;
        public const float PlayerRadius    = 0.4f;
        public const float PlayerHeight    = 1.8f;

        // 질풍참 (이동만 — 스턴/대미지는 전투 단계)
        public const float DashSpeed         = 26f;   // 순간 속도
        public const int   DashDurationTicks = 9;     // 0.15초
        public const int   DashMaxCharges    = 2;
        public const int   DashRechargeTicks = 90;    // 충전당 1.5초

        // 적
        public const float EnemyMoveSpeed  = 4f;
        public const float EnemyRadius     = 0.5f;
        public const float EnemyHeight     = 1.8f;
        public const float EnemyAggroRange = 40f;
        public const int   EnemyRepathTicks = 15;     // 경로 재계산 주기
        public const float EnemyArriveDist  = 0.6f;   // 코너 도달 판정

        // 캐릭터끼리 겹침 분리 (대칭)
        public const float SeparationPush = 0.5f;     // 겹친 만큼 * 이 비율씩 양쪽으로

        // 테두리 하강 (NavMesh Link 점프). 전부 잠정.
        public const int   DescentEdgePauseTicks = 12;   // 멈칫 (0.2초)
        public const int   DescentRecoveryTicks  = 15;   // 착지 후 회복 (0.25초)
        public const float DescentJumpSpeed      = 8f;   // 점프 수평 속도(궤적 길이 결정)
        public const float DescentJumpArcHeight  = 1.5f; // 포물선 위로 솟는 높이
        public const int   DescentJumpMinTicks   = 12;
    }
}
