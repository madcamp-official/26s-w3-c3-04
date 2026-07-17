using System;

namespace Game.Simulation
{
    /// <summary>
    /// 월드 상태의 깊은 복사. 예측이 "현재를 복사해서 가상으로 굴려보기"를
    /// 하려면 이게 정확해야 한다. 배열 내용까지 복사하는 게 핵심.
    ///
    /// 주의: 매 복사마다 배열을 new 한다. 묶음 A의 증명(벤치·결정론)에는
    /// 충분하지만, 본격 Beam Search에서는 배열 풀로 교체해야 한다 (미래 과제).
    /// </summary>
    public static class Snapshot
    {
        public static SimWorld Clone(in SimWorld src)
        {
            SimWorld dst = src;                        // 값 필드는 여기서 복사됨
            dst.enemies = new EnemyState[src.enemies.Length];
            Array.Copy(src.enemies, dst.enemies, src.enemies.Length);
            // EnemyState는 참조 필드가 없는 struct라 Array.Copy가 곧 깊은 복사다.
            return dst;
        }
    }
}
