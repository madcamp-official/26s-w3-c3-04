using UnityEngine;

namespace Game.Sim
{
    /// <summary>월드 전체 상태. 이 하나를 복제(Snapshot)하면 그 시점으로 되돌아간다.</summary>
    public struct SimWorld
    {
        public int       tick;
        public PlayerSim player;
        public EnemySim[] enemies;   // 용량 MaxEnemies 고정
        public int       enemyCount;
        public uint      rngState;   // 시드 고정 결정론 RNG 상태 (DetRng)

        public static SimWorld Create()
        {
            return new SimWorld
            {
                tick = 0,
                player = PlayerSim.Spawn(Vector3.zero),
                enemies = new EnemySim[SimConfig.MaxEnemies],
                enemyCount = 0,
                rngState = 0x1234_5678u,   // 고정 시드
            };
        }

        public void AddEnemy(Vector3 at)
        {
            if (enemyCount >= SimConfig.MaxEnemies) return;
            enemies[enemyCount] = EnemySim.Spawn(enemyCount, at);
            enemyCount++;
        }
    }
}
