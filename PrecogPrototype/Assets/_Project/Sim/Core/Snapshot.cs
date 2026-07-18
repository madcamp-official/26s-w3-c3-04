using System;

namespace Game.Sim
{
    /// <summary>월드 깊은 복사. 예측이 "현재를 복제해 미래를 굴리기" 위한 도구.</summary>
    public static class Snapshot
    {
        public static SimWorld Clone(in SimWorld src)
        {
            SimWorld dst = src;                            // 값 필드 복사
            dst.enemies = new EnemySim[src.enemies.Length];
            Array.Copy(src.enemies, dst.enemies, src.enemies.Length);
            if (src.pendingHits != null)                   // 히트 큐도 독립 배열로(예지 복제 안전)
            {
                dst.pendingHits = new PlayerHit[src.pendingHits.Length];
                Array.Copy(src.pendingHits, dst.pendingHits, src.pendingHits.Length);
            }
            if (src.projectiles != null)                   // 투사체도 독립 배열로
            {
                dst.projectiles = new Projectile[src.projectiles.Length];
                Array.Copy(src.projectiles, dst.projectiles, src.projectiles.Length);
            }
            return dst;
        }

        /// <summary>dst의 배열을 재사용해 src를 깊은 복사(할당 없음). WorldBufferPool 전용 — 후보마다 new SimWorld를 만들지 않기 위함.</summary>
        public static void CopyTo(in SimWorld src, ref SimWorld dst)
        {
            if (dst.enemies == null || dst.enemies.Length < src.enemies.Length)
                dst.enemies = new EnemySim[src.enemies.Length];
            Array.Copy(src.enemies, dst.enemies, src.enemies.Length);

            if (src.pendingHits != null)
            {
                if (dst.pendingHits == null || dst.pendingHits.Length < src.pendingHits.Length)
                    dst.pendingHits = new PlayerHit[src.pendingHits.Length];
                Array.Copy(src.pendingHits, dst.pendingHits, src.pendingHits.Length);
            }
            if (src.projectiles != null)
            {
                if (dst.projectiles == null || dst.projectiles.Length < src.projectiles.Length)
                    dst.projectiles = new Projectile[src.projectiles.Length];
                Array.Copy(src.projectiles, dst.projectiles, src.projectiles.Length);
            }

            dst.tick = src.tick;
            dst.player = src.player;
            dst.enemyCount = src.enemyCount;
            dst.rngState = src.rngState;
            dst.pendingHitCount = src.pendingHitCount;
            dst.projectileCount = src.projectileCount;
            dst.prevPlayerPos = src.prevPlayerPos;
            dst.spawnLocked = src.spawnLocked;
            dst.waveId = src.waveId;
            dst.mapVersion = src.mapVersion;
        }
    }
}
