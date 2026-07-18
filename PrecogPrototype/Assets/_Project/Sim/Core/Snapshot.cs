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
    }
}
