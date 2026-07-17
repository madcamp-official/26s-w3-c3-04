using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 적: NavMesh 길찾기로 플레이어 추격 + 테두리 점프 하강(4단계).
    /// 길찾기가 "여기선 점프(Jump)"라고 하면 → 멈칫 → 포물선 → 착지 → 회복.
    /// </summary>
    public static class EnemyMovement
    {
        public static void Step(ref EnemySim e, in PlayerSim player, in SimServices svc, float dt)
        {
            if (!e.alive) return;

            // 하강 진행 중이면 상태머신만
            if (e.descentPhase != DescentPhase.None) { StepDescent(ref e, dt); return; }

            float flatSqr = FlatSqrDist(e.pos, player.pos);
            if (flatSqr > SimConfig.EnemyAggroRange * SimConfig.EnemyAggroRange)
            {
                Move(ref e, Vector3.zero, svc, dt);   // 감지 밖: 정지(중력만)
                return;
            }

            // 경로 재계산
            if (e.repathTicks > 0) e.repathTicks--;
            if (e.repathTicks == 0 || !e.hasWaypoint)
            {
                MoveKind kind = svc.Pathfinder.NextCorner(e.pos, player.pos, out Vector3 wp);
                e.waypoint = wp;
                e.hasWaypoint = kind != MoveKind.None;
                e.repathTicks = SimConfig.EnemyRepathTicks;

                if (kind == MoveKind.Jump) { StartDescent(ref e, wp); return; }
            }

            // 걷기
            Vector3 target = e.hasWaypoint ? e.waypoint : player.pos;
            Vector3 to = target - e.pos; to.y = 0f;
            float d = to.magnitude;
            if (d < SimConfig.EnemyArriveDist) e.repathTicks = 0;   // 코너 도달 → 다음 틱 재계산

            Vector3 horiz = Vector3.zero;
            if (d > 1e-4f)
            {
                Vector3 dir = to / d;
                horiz = dir * SimConfig.EnemyMoveSpeed * dt;
                e.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            }
            Move(ref e, horiz, svc, dt);
        }

        static void StartDescent(ref EnemySim e, Vector3 landing)
        {
            e.descentPhase = DescentPhase.EdgePause;
            e.descentTicks = 0;
            e.jumpStart = e.pos;
            e.jumpEnd = landing;
            e.vel = Vector3.zero;
            Vector3 d = landing - e.pos; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) e.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        static void StepDescent(ref EnemySim e, float dt)
        {
            switch (e.descentPhase)
            {
                case DescentPhase.EdgePause:
                    e.descentTicks++;
                    if (e.descentTicks >= SimConfig.DescentEdgePauseTicks)
                    {
                        e.descentPhase = DescentPhase.Airborne;
                        e.descentTicks = 0;
                        float horiz = FlatDist(e.jumpStart, e.jumpEnd);
                        e.jumpDuration = Mathf.Max(SimConfig.DescentJumpMinTicks,
                            Mathf.CeilToInt(horiz / (SimConfig.DescentJumpSpeed * dt)));
                    }
                    break;

                case DescentPhase.Airborne:
                {
                    e.descentTicks++;
                    float t = Mathf.Clamp01((float)e.descentTicks / e.jumpDuration);
                    Vector3 flat = Vector3.Lerp(e.jumpStart, e.jumpEnd, t);
                    float arc = SimConfig.DescentJumpArcHeight * Mathf.Sin(Mathf.PI * t);
                    e.pos = flat + Vector3.up * arc;
                    if (e.descentTicks >= e.jumpDuration)
                    {
                        e.pos = e.jumpEnd;
                        e.descentPhase = DescentPhase.Recovery;
                        e.descentTicks = 0;
                    }
                    break;
                }

                case DescentPhase.Recovery:
                    e.descentTicks++;
                    if (e.descentTicks >= SimConfig.DescentRecoveryTicks)
                    {
                        e.descentPhase = DescentPhase.None;
                        e.descentTicks = 0;
                        e.repathTicks = 0;
                        e.hasWaypoint = false;
                    }
                    break;
            }
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
        static float FlatDist(Vector3 a, Vector3 b) => Mathf.Sqrt(FlatSqrDist(a, b));
    }
}
