namespace Game.Prediction
{
    /// <summary>
    /// 후보 평가 가중치. docs/shared/PREDICTION_CONTRACT.md 12장 값을 그대로 넣어봤다가
    /// 실제 재검증에서 회귀(적 3·6마리처럼 접근이 필요한 상황에서 아예 안 다가감)가
    /// 나서, 이전에 검증됐던 값으로 되돌렸다 — ThreatEvaluator.cs 주석 참고.
    /// </summary>
    public static class PredictionScoreConfig
    {
        public const float HpWeight = 10f;
        public const float KillWeight = 30f;
        public const float DamageWeight = 8f;
        public const float SafeDistanceWeight = 1f;
        public const float SafeDistanceCap = 6f;
        public const float SurroundedRadius = 5f;
        public const int SurroundedTolerance = 4;
        public const float SurroundedWeight = 2f;

        /// <summary>원거리 솔저 투사체가 명중 궤도일 때의 감점(임박할수록 커짐). 회귀 이력 때문에
        /// 기존 가중치는 안 건드리고 새 항목으로만 추가한다 — ThreatEvaluator.cs 참고.</summary>
        public const float ProjectileImpactWeight = 3f;
        public const int ProjectileImpactHorizonTicks = 45;
        public const float ChargeImpactWeight = 4f;
        public const int ChargeImpactHorizonTicks = 60;
        /// <summary>공중 적 감점. 예전엔 "반경 내 아무 공중 적"에 붙여 접근 자체를 막아(=타겟팅 불가)
        /// 있었으나, 이제 실제로 조준/발사 중(aimingFlyingEnemyCount)인 적에만 적용한다 —
        /// 단순 부유는 회피 대상이 아니라 처치 대상.</summary>
        public const float FlyingThreatWeight = 2f;
        public const float FlyingThreatRadius = 10f;
        /// <summary>런지 사거리 안에 든 공중 적을 "마무리 기회"로 보는 가점(형태 유도).
        /// 실제 피해(DamageWeight)보다 작게 둬서 "자세만 잡고 안 침"으로 정체되지 않게 한다 —
        /// 접근/점프 중간 스텝(아직 피해 0)이 Beam에서 살아남아 실제 처치까지 이어지도록만 돕는다.</summary>
        public const float AerialOpportunityWeight = 4f;
        public const float TraversalCommitmentWeight = 0.5f;

        /// <summary>사망 후보도 서로 순위를 매길 수 있도록 유한값 유지(무한대면 전멸 폴백 시
        /// "가장 덜 나쁜" 후보를 고를 수 없다) — 이건 계약 반영과 무관하게 유지하는 개선.</summary>
        public const float PlayerDeath = -10000f;
    }
}
