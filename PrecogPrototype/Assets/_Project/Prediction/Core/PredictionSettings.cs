namespace Game.Prediction
{
    /// <summary>
    /// 탐색 파라미터. `Mini`는 docs/shared/PREDICTION_INTEGRATION_PLAN.md 15장 G3
    /// 미니 탐색 기준(행동 4개 × 깊이 3 × Beam 4), `Full`은 12장 본탐색 기준
    /// (3초/180틱, 매크로 15틱→깊이 12, Beam 12)이다.
    /// </summary>
    public struct PredictionSettings
    {
        /// <summary>매크로 행동 1개가 유지되는 틱 수.</summary>
        public int macroTicks;

        /// <summary>매크로 스텝(깊이) 수.</summary>
        public int macroDepth;

        /// <summary>각 깊이에서 유지할 상위 후보 수.</summary>
        public int beamWidth;

        /// <summary>노드 1개당 생성할 최대 행동 후보 수.</summary>
        public int maxActionsPerNode;

        public static PredictionSettings Mini => new PredictionSettings
        {
            macroTicks = 15,
            macroDepth = 3,
            beamWidth = 4,
            maxActionsPerNode = 4,
        };

        public static PredictionSettings Full => new PredictionSettings
        {
            macroTicks = 15,
            macroDepth = 12,
            beamWidth = 12,
            maxActionsPerNode = 12, // ActionGenerator.Priority 전체(런지 최대 2명 포함, 계약 10장)
        };
    }
}
