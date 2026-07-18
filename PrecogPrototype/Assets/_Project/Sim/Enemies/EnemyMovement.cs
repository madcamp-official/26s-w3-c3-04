using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 적 이동: 그래프 경로 추격(NextStep) + 하강(Jump 링크). 부스터(Boost)는 이후 단계.
    /// 하강 판단을 자체로 안 한다 — 최단경로표가 Jump 링크를 태우면 그때 하강을 실행할 뿐.
    /// 추격 방향엔 분리 스티어링(sep)을 가중해 뭉침을 막는다.
    /// </summary>
    public static class EnemyMovement
    {
        public static void Step(ref EnemySim e, in PlayerSim player, Vector3 sep, in SimServices svc, float dt)
        {
            if (!e.alive) return;

            // 스턴 중이면 정지(스턴 부여는 폐기됐지만 필드는 남아 있어 방어적으로 처리)
            if (e.combat.stunTicks > 0) { Move(ref e, Vector3.zero, svc, dt); return; }

            // 하강 진행 중이면 상태머신만
            if (e.descentPhase != DescentPhase.None) { StepDescent(ref e, in svc, dt); return; }

            if (FlatSqrDist(e.pos, player.pos) > SimConfig.EnemyAggroRange * SimConfig.EnemyAggroRange)
            { Move(ref e, Vector3.zero, svc, dt); return; }

            // 경로 재계산(주기적) — 그래프 표 조회(가벼움, 예측 친화)
            if (e.repathTicks > 0) e.repathTicks--;
            if (e.repathTicks == 0 || !e.hasWaypoint)
            {
                PathStep step = svc.Pathfinder.NextStep(e.pos, player.pos);
                e.waypoint = step.next;
                e.hasWaypoint = step.kind != MoveKind.None;
                e.repathTicks = SimConfig.EnemyRepathTicks;
                if (step.kind == MoveKind.Jump) { StartDescent(ref e, step.next); return; }
                // MoveKind.Boost: 이후 부스터 상행
            }

            Vector3 target = e.hasWaypoint ? e.waypoint : player.pos;
            Vector3 to = target - e.pos; to.y = 0f;
            float d = to.magnitude;
            if (d < SimConfig.EnemyArriveDist) e.repathTicks = 0;   // 웨이포인트 도달 → 다음 홉 재계산

            Vector3 horiz = Vector3.zero;
            if (d > 1e-4f)
            {
                Vector3 dir = to / d;
                Vector3 steer = dir + sep * AIConfig.SeparationWeight;   // 분리 스티어링(뭉침 방지)
                if (steer.sqrMagnitude > 1e-6f) dir = steer.normalized;
                horiz = dir * SimConfig.EnemyMoveSpeed * dt;
                e.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            }
            Move(ref e, horiz, svc, dt);
        }

        /// <summary>하강 시작: 이미 테두리 노드 근처라 걸어가는 단계 없이 멈칫→순간이동→회복.</summary>
        static void StartDescent(ref EnemySim e, Vector3 landing)
        {
            e.descentPhase = DescentPhase.EdgePause;
            e.descentTicks = 0;
            e.vel = Vector3.zero;
            // 착지 분산(id별 결정론) — 여러 몹이 같은 링크로 와도 안 뭉침
            float ox = ((e.id % 3) - 1) * SimConfig.DescentLandingSpread;
            float oz = (((e.id / 3) % 3) - 1) * SimConfig.DescentLandingSpread;
            e.descentLanding = landing + new Vector3(ox, 0f, oz);
            Vector3 face = landing - e.pos; face.y = 0f;
            if (face.sqrMagnitude > 1e-4f) e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
        }

        static void StepDescent(ref EnemySim e, in SimServices svc, float dt)
        {
            switch (e.descentPhase)
            {
                case DescentPhase.EdgePause:   // 멈칫(점프 예고)
                    e.descentTicks++;
                    if (e.descentTicks >= SimConfig.DescentEdgePauseTicks)
                    {
                        e.pos = e.descentLanding;   // 순간이동 (화면은 EntityViews 보간이 슥 미끄러지게)
                        e.descentPhase = DescentPhase.Recovery;
                        e.descentTicks = 0;
                    }
                    break;
                case DescentPhase.Recovery:    // 착지 회복
                    Move(ref e, Vector3.zero, svc, dt);
                    e.descentTicks++;
                    if (e.descentTicks >= SimConfig.DescentRecoveryTicks)
                    {
                        e.descentPhase = DescentPhase.None;
                        e.descentTicks = 0;
                        e.repathTicks = 0;
                        e.hasWaypoint = false;
                    }
                    break;
                default:   // ApproachEdge(그래프 전환으로 미사용) 등 → 종료
                    e.descentPhase = DescentPhase.None;
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
        { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }
    }
}
