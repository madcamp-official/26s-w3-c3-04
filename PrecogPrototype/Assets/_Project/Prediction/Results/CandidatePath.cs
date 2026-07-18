namespace Game.Prediction
{
    /// <summary>
    /// Beam Search 결과 1개. docs/shared/PREDICTION_CONTRACT.md 11장 "후보 결과 계약"의
    /// 필드를 최대한 반영하되, `controls`(InputCmd[] 전체 틱)·`predictedFrames`·
    /// `actionEvents`는 이번 범위에 없다 — 계약 스스로 "상위 후보만 최초 스냅샷에서
    /// 60Hz로 다시 실행해 만든다"고 못박은 별도 단계(최종 후보 정밀 재검증)라서, 매크로
    /// 행동 시퀀스(`actions`)만 담고 그 단계는 다음 마일스톤으로 남긴다.
    /// </summary>
    public sealed class CandidatePath
    {
        public int candidateId;
        public MacroAction[] actions;

        public int killCount;
        public int damageDealt;
        public int dashCount;
        public int lungeCount;
        public int expectedHits;
        public int durationTicks;

        public float safetyScore;
        public float killScore;
        public float difficultyScore;
        public float TotalScore => safetyScore + killScore + difficultyScore;

        public ulong initialSnapshotHash;
        public int mapVersion;

        /// <summary>탐색이 생존 경로를 하나도 찾지 못했을 때(전부 사망)의 폴백 여부. 계약엔 없는 내부용.</summary>
        public bool isDeadFallback;
    }
}
