using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 탐색 파라미터. `Mini`는 docs/shared/PREDICTION_INTEGRATION_PLAN.md 15장 G3
    /// 미니 탐색 기준(행동 4개 × 깊이 3 × Beam 4), `Full`은 12장 본탐색 기준
    /// (3초/180틱, 매크로 15틱→깊이 12, Beam 12)이다.
    /// </summary>
    public struct PredictionSettings
    {
        /// <summary>예측 게이지 메커닉이 지원하는 최소·최대 예측 시간(초). `ForDuration`이 이 범위로 자른다.</summary>
        public const float MinDurationSeconds = 1f;
        public const float MaxDurationSeconds = 5f;

        /// <summary>매크로 행동 1개가 유지되는 틱 수.</summary>
        public int macroTicks;

        /// <summary>매크로 스텝(깊이) 수.</summary>
        public int macroDepth;

        /// <summary>각 깊이에서 유지할 상위 후보 수.</summary>
        public int beamWidth;

        /// <summary>노드 1개당 생성할 최대 행동 후보 수.</summary>
        public int maxActionsPerNode;

        /// <summary>이 설정의 실제 예측 시간(초). macroTicks×macroDepth 틱을 60Hz 기준으로 환산.</summary>
        public float DurationSeconds => (float)macroTicks * macroDepth / SimConfig.TickRate;

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

        /// <summary>
        /// 예측 게이지 메커닉용: 원하는 예측 시간(초, 1~5초로 잘림)에 맞춰 macroDepth만 조절한다.
        /// Beam 폭·행동 후보 수는 `Full`과 동일하게 유지한다 — 시간에 따른 품질 축소는 별도
        /// 관심사(OPTIMIZATION.md 12장 "시간 예산과 동적 축소")라 여기서 같이 하지 않는다.
        /// macroTicks(매크로 1스텝당 틱 수)는 Full/Mini와 같은 15틱(0.25초) 단위를 그대로 써서
        /// 매크로 스텝 경계가 어긋나지 않게 한다.
        /// </summary>
        public static PredictionSettings ForDuration(float seconds)
        {
            const int macroTicksPerStep = 15;
            float clamped = Mathf.Clamp(seconds, MinDurationSeconds, MaxDurationSeconds);
            int totalTicks = Mathf.RoundToInt(clamped * SimConfig.TickRate);
            int depth = Mathf.Max(1, Mathf.RoundToInt((float)totalTicks / macroTicksPerStep));

            return new PredictionSettings
            {
                macroTicks = macroTicksPerStep,
                macroDepth = depth,
                beamWidth = 12,
                maxActionsPerNode = 12,
            };
        }
    }
}
