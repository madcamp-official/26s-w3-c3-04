namespace Game.Simulation
{
    /// <summary>
    /// 월드 전체 상태. 이 하나를 복사하면 그 시점으로 완전히 되돌릴 수 있어야 한다.
    /// enemies는 배열(참조)이라 통째 복사만으로는 얕은 복사가 된다 →
    /// 스냅샷은 반드시 Snapshot.Clone을 써서 배열 내용까지 복사한다.
    /// </summary>
    public struct SimWorld
    {
        public int tick;
        public PlayerState player;

        public EnemyState[] enemies;   // 용량 MaxEnemies 고정
        public int enemyCount;         // 실제 사용 중인 개수

        public static SimWorld Create()
        {
            return new SimWorld
            {
                tick = 0,
                player = PlayerState.Spawn(UnityEngine.Vector3.zero),
                enemies = new EnemyState[SimConfig.MaxEnemies],
                enemyCount = 0,
            };
        }

        public void AddMeleeEnemy(UnityEngine.Vector3 at)
        {
            if (enemyCount >= SimConfig.MaxEnemies) return;
            enemies[enemyCount] = EnemyState.SpawnMelee(enemyCount, at);
            enemyCount++;
        }
    }
}
