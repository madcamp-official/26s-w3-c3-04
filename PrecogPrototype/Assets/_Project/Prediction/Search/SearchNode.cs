namespace Game.Prediction
{
    /// <summary>
    /// Beam Search 트리의 노드 1개. 행동 시퀀스는 매번 배열로 복사하지 않고
    /// parentIndex를 따라가며 재구성한다(고정 배열 인덱스 체인).
    /// </summary>
    public struct SearchNode
    {
        public int worldBufferIndex;
        public int parentIndex;   // 루트는 -1
        public MacroAction actionTaken;
        public float score;
        public int depth;
        public int killCount;
        public int damageDealt;
        public int ticksSurvived;
        public bool alive;
        public ulong stateKey;
    }
}
