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

        // 플레이어 (캡슐: 발밑 pos 기준, 위로 Height). 크기는 구조 상수(런타임 변경 금지).
        public const float PlayerRadius    = 0.28f;
        public const float PlayerHeight    = 1.15f;

        // ── 이동·점프 (static = F1 튜닝 패널에서 실시간 조정. 예측 중 변경 금지) ──
        //    2026-07-18 F1 튜닝으로 확정한 기본값.
        public static float PlayerMoveSpeed = 8.01f;
        public static float PlayerJumpSpeed = 10.24f;
        public static int   JumpBufferTicks = 12;    // 착지 직전 점프 선입력 허용
        public static float AirJumpBoost    = 7.86f; // 2단 점프 시 입력 방향 수평 임펄스(추가 속도)
        public static int   AirJumpBoostTicks = 12;  // 임펄스 지속(감쇠)

        // ── 4방향 대시 (진짜 임펄스: 초기 속도 부여 → 매 틱 드래그로 감쇠. 이동 전용) ──
        //    총 거리 = InitialSpeed·dt·(1-decay^N)/(1-decay) 로 자동 산출(F1 패널에 표시). ≈7.74m
        public static float DashInitialSpeed  = 32.82f; // 튀어나가는 힘(m/s) — 첫 틱이 가장 강함
        public static float DashDecay         = 0.95f;  // 틱별 속도 유지율(드래그). 낮을수록 빨리 멈춤
        public static int   DashDurationTicks = 24;     // 최대 지속(속도가 죽어도 이 틱에 종료)
        public static int   DashMaxCharges    = 2;      // 둠식 2스택
        public static int   DashRechargeTicks = 60;     // 스택당 1초
        public static int   DashReserveWindow = 9;      // 대시 막판 이 틱 이내 입력 → 예약(끝나면 즉시 다음 대시)

        // 적. 크기 축소(부피 ~1/4), 튜닝 대상
        public const float EnemyMoveSpeed  = 6f;    // 근접 그런트 = 플레이어 7의 ~0.85× (원거리는 자체 4)
        public const float EnemyRadius     = 0.32f;
        public const float EnemyHeight     = 1.15f;
        public const float EnemyAggroRange = 40f;
        public const int   EnemyRepathTicks = 15;     // 경로 재계산 주기
        public const float EnemyArriveDist  = 0.6f;   // 코너 도달 판정
        public const float EnemyNavClampDist = 2f;    // 틱 끝에 지상몹을 navmesh로 되당기는 최대 거리(다리 낙하 방지)

        // 캐릭터끼리 겹침 분리 (대칭)
        public const float SeparationPush = 0.5f;     // 겹친 만큼 * 이 비율씩 양쪽으로

        // ── 전투 (combat 세션이 튜닝. rebuild는 스폰·해시에만 씀) ──
        public const int EnemyNormalHp = 2;   // 일반몹 HP
        public const int EnemyMidHp    = 3;   // 중형몹 HP
        public const int EnemyLargeHp  = 4;   // 대형몹 HP (크기 3배)
        public const float EnemyLargeScale = 3f;   // 대형몹 크기 배율
        //  스킬 세부 틱(윈드업/액티브/스턴 등)은 combat 소유 파일에 둔다.

        // 절벽 낙하 (자연 낙하). off-mesh link를 큰 낙차로 감지 → 착지점으로 걸어 나가 떨어짐.
        public const float DropDetectMinHeight = 2f;    // 다음 코너가 이만큼 아래면 낙하 후보
        public const float DropDetectRatio     = 1.5f;  // 낙차 > 수평거리 * 이 값 이면 절벽(경사로와 구분)
        public const float DescentLandEpsilon  = 0.3f;  // 착지점 높이 이 오차 안이고 지면이면 착지 완료
        public const int   DescentMaxTicks     = 120;   // 낙하 안전장치(2초). 어디도 못 닿으면 강제 종료

        // 소환 (지정 지점 + 일정 간격)
        public const int SpawnIntervalTicks = 45;   // 0.75초마다 한 마리
        public const int SpawnCap           = 40;   // 최대 동시 적 수
    }
}
