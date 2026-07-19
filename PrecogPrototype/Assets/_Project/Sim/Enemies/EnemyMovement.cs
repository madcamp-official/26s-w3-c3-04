using UnityEngine;

namespace Game.Sim
{
    /// <summary>고정 유향 그래프의 PathStep을 실행하며 core-controls 분리 스티어링을 유지한다.</summary>
    public static class EnemyMovement
    {
        public static void Step(ref EnemySim e, in PlayerSim player, Vector3 sep, in SimServices svc, float dt)
        {
            if (!e.alive) return;
            if (e.combat.stunTicks > 0)
            { e.vel.x = e.vel.z = 0f; Move(ref e, Vector3.zero, in svc, dt); return; }
            if (e.traversalPhase != TraversalPhase.None)
            { StepTraversal(ref e, in svc); return; }
            if (FlatSqrDist(e.pos, player.pos) > SimConfig.EnemyAggroRange * SimConfig.EnemyAggroRange)
            { Move(ref e, Vector3.zero, in svc, dt); return; }

            int agentMask = 1 << (int)e.ai.mobility;
            PathStep step = svc.Pathfinder.NextStep(e.pos, player.pos, agentMask);
            e.currentNavNodeId = step.currentNodeId;
            e.destinationNavNodeId = step.destinationNodeId;
            e.nextNavNodeId = step.nextNodeId;
            e.currentFloorId = step.floorId;

            if (step.kind == MoveKind.None)
            {
                e.hasWaypoint = false;
                Move(ref e, Vector3.zero, in svc, dt);
                return;
            }

            if (step.kind == MoveKind.Drop || step.kind == MoveKind.Boost || step.kind == MoveKind.JumpUp)
            {
                Vector3 toStart = step.traversalStart - e.pos; toStart.y = 0f;
                if (toStart.magnitude <= SimConfig.EnemyArriveDist)
                { StartTraversal(ref e, in step); return; }
                WalkTowards(ref e, step.traversalStart, sep, in player, in svc, dt);
                return;
            }

            WalkTowards(ref e, step.next, sep, in player, in svc, dt);
        }

        static void WalkTowards(ref EnemySim e, Vector3 target, Vector3 sep, in PlayerSim player, in SimServices svc, float dt)
        {
            e.waypoint = target; e.hasWaypoint = true;
            Vector3 face = player.pos - e.pos; face.y = 0f;
            if (face.sqrMagnitude > 1e-6f) e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            Vector3 to = target - e.pos; to.y = 0f;
            float d = to.magnitude;
            Vector3 horiz = Vector3.zero;
            if (d > 1e-4f)
            {
                Vector3 dir = to / d;
                Vector3 steer = dir + sep * AIConfig.SeparationWeight;
                if (steer.sqrMagnitude > 1e-6f) dir = steer.normalized;
                horiz = dir * SimConfig.EnemyMoveSpeed * dt;
            }
            Move(ref e, horiz, in svc, dt);
        }

        static void StartTraversal(ref EnemySim e, in PathStep step)
        {
            e.traversalPhase = TraversalPhase.Pause;
            e.activeMoveKind = step.kind;
            e.activeTraversalLinkId = step.linkId;
            e.traversalTicks = 0;
            e.jumpDuration = step.traversalTicks > 0 ? step.traversalTicks : SimConfig.TraversalDefaultAirTicks;
            e.jumpStart = e.pos;
            e.jumpEnd = step.next;
            e.nextNavNodeId = step.nextNodeId;
            e.vel = Vector3.zero;
        }

        static void StepTraversal(ref EnemySim e, in SimServices svc)
        {
            switch (e.traversalPhase)
            {
                case TraversalPhase.Pause:
                    e.vel = Vector3.zero;
                    if (++e.traversalTicks >= SimConfig.TraversalPauseTicks)
                    { e.traversalPhase = TraversalPhase.Airborne; e.traversalTicks = 0; }
                    break;
                case TraversalPhase.Airborne:
                    e.traversalTicks++;
                    float t = Mathf.Clamp01((float)e.traversalTicks / Mathf.Max(1, e.jumpDuration));
                    Vector3 p = Vector3.Lerp(e.jumpStart, e.jumpEnd, t);
                    if (e.activeMoveKind == MoveKind.Drop || e.activeMoveKind == MoveKind.JumpUp)
                        p.y += Mathf.Sin(t * Mathf.PI) * SimConfig.TraversalArcHeight;
                    e.pos = p; e.grounded = false;
                    if (e.traversalTicks >= e.jumpDuration)
                    { e.pos = e.jumpEnd; e.traversalPhase = TraversalPhase.Recovery; e.traversalTicks = 0; }
                    break;
                case TraversalPhase.Recovery:
                    e.vel = Vector3.zero;
                    if (++e.traversalTicks >= SimConfig.TraversalRecoveryTicks)
                    {
                        e.traversalPhase = TraversalPhase.None;
                        e.activeMoveKind = MoveKind.None;
                        e.activeTraversalLinkId = -1;
                        e.currentNavNodeId = e.nextNavNodeId;
                        e.currentFloorId = svc.Pathfinder.FloorIdAt(e.pos);
                        e.hasWaypoint = false; e.traversalTicks = 0;
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
        { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }
    }
}
