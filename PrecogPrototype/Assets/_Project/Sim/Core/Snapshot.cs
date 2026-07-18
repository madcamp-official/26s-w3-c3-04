using System;

namespace Game.Sim
{
    /// <summary>월드 깊은 복사. 예측이 "현재를 복제해 미래를 굴리기" 위한 도구.</summary>
    public static class Snapshot
    {
        public static SimWorld Clone(in SimWorld src)
        {
            SimWorld dst = SimWorld.Create();
            CopyTo(in src, ref dst);
            return dst;
        }

        public static void CopyTo(in SimWorld src, ref SimWorld dst)
        {
            if (dst.enemies == null || dst.enemies.Length < src.enemies.Length)
                dst.enemies = new EnemySim[src.enemies.Length];

            dst.tick = src.tick;
            dst.player = src.player;
            dst.enemyCount = src.enemyCount;
            dst.spawnLocked = src.spawnLocked;
            dst.waveId = src.waveId;
            dst.mapVersion = src.mapVersion;
            Array.Copy(src.enemies, dst.enemies, src.enemyCount);
        }
    }
}
