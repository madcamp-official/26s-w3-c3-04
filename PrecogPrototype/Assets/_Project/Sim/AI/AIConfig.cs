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
        // 평소(돌진 커밋 전) 추격 속도만 낮춤 — 실물 모델 Walk 애니메이션이 SimConfig.EnemyMoveSpeed
        // 전속력엔 못 따라가 미끄러지듯 보였다. ChargeRun 자체 속도(ChargeSpeed)는 그대로 둔다.
        // ★ 0.65(=3.9)도 여전히 빠르다는 피드백 — 육중하게 천천히 걷는 느낌으로 더 낮춤.
        public const float ChargeChaseSpeedMul = 0.35f;

        // ── 몹 분리(boids Rule 1): 겹치기 전에 이웃 반대방향으로 미리 조향. 결정론(난수 X) ──
        public static float SeparationRadius  = 1.6f;  // 몸(반경 합) 밖으로 이만큼까지 개인공간
        public static float SeparationWeight  = 0.9f;  // 추격/이동 대비 분리 세기
        public static float SeparationMaxPush = 2.5f;  // 과밀 시 분리벡터 폭주 방지 클램프
        // 개체 고정 개성값(EnemySim.personality 0~1)이 분리 세기를 이 값~1배 사이로 낮춘다.
        // 전부 같은 가중치면 정면으로 마주칠 때 밀어내는 힘이 대칭이라 거울처럼 진동한다(ADR-0004 개정).
        public static float SeparationScaleMin = 0.55f;

        // ── 공중 원거리 (커코데몬형) — 원거리 × Flying. 낮게 부유, 공격은 지상 원거리와 공유 ──
        //    벽은 MoveHorizontal 슬라이드, 몹끼리는 분리 스티어링이 담당(클래식 난수 우회 폐기).
        public const float FlyHoverOffset  = 2f;    // 플레이어 y + 이만큼 위를 유지(낮게 = 대공 닿음)
        public const float FlySpeed        = 3.5f;  // 느린 부유(수평·수직 공통)
        public const float FlyBandMin      = 5f;    // 이보다 가까우면 수평 후퇴
        public const float FlyBandMax      = 14f;   // 이보다 멀면 수평 접근
        public const float FlyMinClearance = 1f;    // 지면 위 최소 여유(안 꺼지게)

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
