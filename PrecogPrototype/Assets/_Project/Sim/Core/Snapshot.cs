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
            return dst;
        }
    }
}
