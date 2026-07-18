namespace Game.Prediction
{
    /// <summary>
    /// Beam Search 결과 1개. 이번 마일스톤은 매크로 행동 시퀀스와 점수만 담는다.
    /// PredictedFrame/경로선/잔상 데이터는 다음 단계(최종 후보 정밀 재검증)에서 채운다
    /// — 문서 13장 "Beam Search 중에는 매 틱 프레임을 저장하지 않는다" 규칙과 일치.
    /// </summary>
    public sealed class CandidatePath
    {
        public MacroAction[] actions;
        public int killCount;
        public int damageDealt;
        public int durationTicks;
        public float score;
        public ulong initialSnapshotHash;

        /// <summary>탐색이 생존 경로를 하나도 찾지 못했을 때(전부 사망)의 폴백 여부.</summary>
        public bool isDeadFallback;
    }
}
