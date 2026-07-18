using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 적: NavMesh 추격 + 자체 판단 테두리 하강(순간이동식).
    /// 하강 판단은 우리가 한다 — "걷는 길 길이 vs 점프 길 길이"를 비교해, 점프가 명확히
    /// 짧을 때만 내려간다(NavMeshLink 라우팅에 안 맡김). 하강은 궤적 없이 순간이동 + 화면 보간.
    /// </summary>
    public static class EnemyMovement
    {
        public static void Step(ref EnemySim e, in PlayerSim player, in SimServices svc, float dt)
        {
            if (!e.alive) return;

            // 스턴 중이면 AI 정지 (combat이 stunTicks 부여, 감소는 CombatResolve가)
            if (e.combat.stunTicks > 0) { e.vel.x = 0f; e.vel.z = 0f; Move(ref e, Vector3.zero, svc, dt); return; }

            // 하강 진행 중이면 상태머신만
            if (e.descentPhase != DescentPhase.None) { StepDescent(ref e, in svc, dt); return; }

            float flatSqr = FlatSqrDist(e.pos, player.pos);
            if (flatSqr > SimConfig.EnemyAggroRange * SimConfig.EnemyAggroRange)
            { Move(ref e, Vector3.zero, svc, dt); return; }

            // ── 하강 판단: 플레이어보다 충분히 높을 때만 ──
            if (e.pos.y > player.pos.y + SimConfig.DescentMinHeight
                && svc.Pathfinder.NearestDropEdge(e.pos, out Vector3 edge, out Vector3 landing))
            {
                float walk     = svc.Pathfinder.PathLength(e.pos, player.pos);
                float toEdge   = svc.Pathfinder.PathLength(e.pos, edge);
                float fromLand = svc.Pathfinder.PathLength(landing, player.pos);
                if (toEdge >= 0f && fromLand >= 0f)
                {
                    float jump = toEdge + FlatDist(edge, landing) + fromLand;
                    bool walkOk = walk >= 0f;
                    // 걷는 길이 없거나, 점프가 걷기의 문턱값보다 짧으면 하강
                    if (!walkOk || jump < walk * SimConfig.DescentThreshold)
                    { StartDescent(ref e, edge, landing); return; }
                }
            }

            // ── 일반 걷기 ──
            if (e.repathTicks > 0) e.repathTicks--;
            if (e.repathTicks == 0 || !e.hasWaypoint)
            {
                e.hasWaypoint = svc.Pathfinder.NextCorner(e.pos, player.pos, out e.waypoint);
                e.repathTicks = SimConfig.EnemyRepathTicks;
            }
            Vector3 target = e.hasWaypoint ? e.waypoint : player.pos;
            Vector3 to = target - e.pos; to.y = 0f;
            float d = to.magnitude;
            if (d < SimConfig.EnemyArriveDist) e.repathTicks = 0;

            Vector3 horiz = Vector3.zero;
            if (d > 1e-4f)
            {
                Vector3 dir = to / d;
                horiz = dir * SimConfig.EnemyMoveSpeed * dt;
                e.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            }
            Move(ref e, horiz, svc, dt);
        }

        static void StartDescent(ref EnemySim e, Vector3 edge, Vector3 landing)
        {
            e.descentPhase = DescentPhase.ApproachEdge;
            e.descentTicks = 0;
            e.descentEdge = edge;
            // 착지 분산 (id별 결정론) — 여러 몹이 같은 테두리로 와도 안 뭉침
            float ox = ((e.id % 3) - 1) * SimConfig.DescentLandingSpread;
            float oz = (((e.id / 3) % 3) - 1) * SimConfig.DescentLandingSpread;
            e.descentLanding = landing + new Vector3(ox, 0f, oz);
        }

        static void StepDescent(ref EnemySim e, in SimServices svc, float dt)
        {
            switch (e.descentPhase)
            {
                case DescentPhase.ApproachEdge:
                {
                    Vector3 to = e.descentEdge - e.pos; to.y = 0f;
                    float d = to.magnitude;
                    if (d < SimConfig.DescentEdgeReach)
                    { e.descentPhase = DescentPhase.EdgePause; e.descentTicks = 0; e.vel.x = 0f; e.vel.z = 0f; }
                    else
                    {
                        Vector3 dir = to / d;
                        e.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                        Move(ref e, dir * SimConfig.EnemyMoveSpeed * dt, svc, dt);
                    }
                    break;
                }
                case DescentPhase.EdgePause:
                    e.vel.x = 0f; e.vel.z = 0f;
                    e.descentTicks++;
                    if (e.descentTicks >= SimConfig.DescentEdgePauseTicks)
                    {
                        e.pos = e.descentLanding;   // 순간이동 (화면은 EntityViews 보간이 슥 미끄러지게)
                        e.descentPhase = DescentPhase.Recovery;
                        e.descentTicks = 0;
                    }
                    break;
                case DescentPhase.Recovery:
                    e.vel.x = 0f; e.vel.z = 0f;
                    Move(ref e, Vector3.zero, svc, dt);   // 착지 지면 안착
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
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);
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
