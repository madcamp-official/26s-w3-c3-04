using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 적: 플레이어를 NavMesh 길찾기로 쫓아온다(뼈대 단계엔 이게 전부).
    /// 경로 전체를 저장하지 않고 "다음 코너"만 주기적으로 재계산해 따라간다.
    /// </summary>
    public static class EnemyMovement
    {
        public static void Step(ref EnemySim e, in PlayerSim player, in SimServices svc, float dt)
        {
            if (!e.alive) return;

            float flatSqr = FlatSqrDist(e.pos, player.pos);
            if (flatSqr > SimConfig.EnemyAggroRange * SimConfig.EnemyAggroRange)
            {
                // 감지 밖: 정지(중력만)
                Vector3 stop = Vector3.zero;
                Move(ref e, stop, svc, dt);
                return;
            }

            // 경로 재계산 (주기적)
            if (e.repathTicks > 0) e.repathTicks--;
            if (e.repathTicks == 0 || !e.hasWaypoint)
            {
                e.hasWaypoint = svc.Pathfinder.NextCorner(e.pos, player.pos, out e.waypoint);
                e.repathTicks = SimConfig.EnemyRepathTicks;
            }

            Vector3 target = e.hasWaypoint ? e.waypoint : player.pos;
            Vector3 to = target - e.pos; to.y = 0f;
            float d = to.magnitude;

            // 코너 도달 → 다음 틱 재계산
            if (d < SimConfig.EnemyArriveDist) { e.repathTicks = 0; }

            Vector3 horiz = Vector3.zero;
            if (d > 1e-4f)
            {
                Vector3 dir = to / d;
                horiz = dir * SimConfig.EnemyMoveSpeed * dt;
                e.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            }
            Move(ref e, horiz, svc, dt);
        }

        static void Move(ref EnemySim e, Vector3 horiz, in SimServices svc, float dt)
        {
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz,
                                                  SimConfig.EnemyRadius, SimConfig.EnemyHeight);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool grounded);
            e.grounded = grounded;
        }

        static float FlatSqrDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
