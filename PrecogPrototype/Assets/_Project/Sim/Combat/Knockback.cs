using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 넉백 = 순간이동. 궤적 물리 대신, dir로 dist만큼 순간 이동시키되
    /// 벽이 있으면 그 앞까지만(MoveHorizontal 슬라이드), 끝에서 지면에 스냅.
    /// 화면 보간은 뷰(EntityViews의 prev→cur lerp)가 알아서 부드럽게 만든다.
    /// </summary>
    public static class Knockback
    {
        public static void Apply(ref EnemySim e, in ICollision col, Vector3 dir, float dist)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();

            Vector3 np = CharacterMotor.MoveHorizontal(col, e.pos, dir * dist, e.radius, e.height);
            if (col.SampleGround(np, 100f, out float gy)) np.y = gy;  // 절벽이면 아래 지면으로 낙하 착지
            e.pos = np;
        }
    }
}
