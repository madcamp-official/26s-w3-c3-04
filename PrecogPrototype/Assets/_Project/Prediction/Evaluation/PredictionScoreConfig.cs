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

        /// <summary>사망 후보도 서로 순위를 매길 수 있도록 유한값 유지(무한대면 전멸 폴백 시
        /// "가장 덜 나쁜" 후보를 고를 수 없다) — 이건 계약 반영과 무관하게 유지하는 개선.</summary>
        public const float PlayerDeath = -10000f;
    }
}
