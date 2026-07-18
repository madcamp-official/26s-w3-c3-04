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

        // 플레이어 (캡슐: 발밑 pos 기준, 위로 Height). 크기 축소(부피 ~1/4), 튜닝 대상
        public const float PlayerMoveSpeed = 7f;
        public const float PlayerJumpSpeed = 9f;
        public const float PlayerRadius    = 0.28f;
        public const float PlayerHeight    = 1.15f;

        // 질풍참 (이동만 — 스턴/대미지는 전투 단계)
        public const float DashSpeed         = 40f;   // 순간 속도 (거리↑)
        public const int   DashDurationTicks = 11;    // 약 0.18초 → 최대 사거리 ~7.3칸
        public const float DashAimHeight     = 0.95f; // 돌진 조준 레이 원점 높이(눈높이) — 표면까지만 감
        public const int   DashMaxCharges    = 2;
        public const int   DashRechargeTicks = 90;    // 충전당 1.5초

        // 적. 크기 축소(부피 ~1/4), 튜닝 대상
        public const float EnemyMoveSpeed  = 6f;    // 근접 그런트 = 플레이어 7의 ~0.85× (원거리는 자체 4)
        public const float EnemyRadius     = 0.32f;
        public const float EnemyHeight     = 1.15f;
        public const float EnemyAggroRange = 40f;
        public const int   EnemyRepathTicks = 15;     // 경로 재계산 주기
        public const float EnemyArriveDist  = 0.6f;   // 코너 도달 판정

        // 캐릭터끼리 겹침 분리 (대칭)
        public const float SeparationPush = 0.5f;     // 겹친 만큼 * 이 비율씩 양쪽으로

        // ── 전투 (combat 세션이 튜닝. rebuild는 스폰·해시에만 씀) ──
        public const int EnemyNormalHp = 2;   // 일반몹 HP
        public const int EnemyMidHp    = 3;   // 중형몹 HP
        public const int EnemyLargeHp  = 4;   // 대형몹 HP (크기 3배)
        public const float EnemyLargeScale = 3f;   // 대형몹 크기 배율
        public const int GuardMax      = 240; // 가드 게이지 최대(4등분 → 칼등치기 4회)
        //  스킬 세부 틱(윈드업/액티브/스턴 등)은 combat 소유 파일에 둔다.

        // 테두리 하강 (순간이동식 + 자체 판단). 전부 잠정.
        public const int   DescentEdgePauseTicks = 12;   // 멈칫 (0.2초)
        public const int   DescentRecoveryTicks  = 15;   // 착지 후 회복 (0.25초)
        public const float DescentThreshold      = 0.8f; // 점프길 < 걷는길 * 이 값 이면 하강
        public const float DescentEdgeReach      = 0.8f; // 테두리 도달 판정
        public const float DescentLandingSpread  = 1.2f; // 착지 분산(여러 몹 안 뭉치게)
        public const float DescentMinHeight      = 2f;   // 이만큼 위에 있을 때만 하강 검토

        // 소환 (지정 지점 + 일정 간격)
        public const int SpawnIntervalTicks = 45;   // 0.75초마다 한 마리
        public const int SpawnCap           = 40;   // 최대 동시 적 수
    }
}
