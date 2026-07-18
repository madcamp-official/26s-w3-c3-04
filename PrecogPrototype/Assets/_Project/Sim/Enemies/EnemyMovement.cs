using UnityEngine;

namespace Game.Sim
{
    public static class EnemyMovement
    {
        public static void Step(ref EnemySim enemy, in PlayerSim player, in SimServices services, float dt)
        {
            if (!enemy.alive || !player.alive) return;

            if (enemy.combat.stunTicks > 0)
            {
                enemy.aiState = EnemyAIState.Stunned;
                Move(ref enemy, Vector3.zero, in services, dt);
                return;
            }

            if (enemy.aiState == EnemyAIState.Stunned)
                enemy.aiState = EnemyAIState.Approach;

            if (IsAttackState(enemy.aiState))
            {
                Move(ref enemy, Vector3.zero, in services, dt);
                return;
            }

            if (enemy.descentPhase != DescentPhase.None)
            {
                enemy.aiState = enemy.descentPhase == DescentPhase.Recovery
                    ? EnemyAIState.LandingRecovery
                    : EnemyAIState.Traversal;
                StepDescent(ref enemy, dt);
                return;
            }

            float flatDistance = CombatMath.FlatDistance(enemy.pos, player.pos);
            if (flatDistance > SimConfig.EnemyAggroRange)
            {
                enemy.aiState = EnemyAIState.Idle;
                Move(ref enemy, Vector3.zero, in services, dt);
                return;
            }

            if (enemy.attackCooldownTicks == 0 &&
                flatDistance <= CombatConfig.EnemyAttackRange &&
                Mathf.Abs(player.pos.y - enemy.pos.y) <= CombatConfig.EnemyAttackHeightTolerance)
            {
                BeginAttack(ref enemy, in player);
                return;
            }

            enemy.aiState = EnemyAIState.Approach;
            if (enemy.repathTicks > 0) enemy.repathTicks--;
            if (enemy.repathTicks == 0 || !enemy.hasWaypoint)
            {
                PathStep step = services.Pathfinder.NextStep(enemy.pos, player.pos);
                enemy.waypoint = step.next;
                enemy.currentNavNodeId = step.currentNodeId;
                enemy.destinationNavNodeId = step.destinationNodeId;
                enemy.nextNavNodeId = step.nextNodeId;
                enemy.activeTraversalLinkId = step.linkId;
                enemy.hasWaypoint = step.kind != MoveKind.None;
                enemy.repathTicks = SimConfig.EnemyRepathTicks;
                if (step.kind == MoveKind.Jump)
                {
                    StartDescent(ref enemy, step.next);
                    return;
                }
            }

            Vector3 target = enemy.hasWaypoint ? enemy.waypoint : player.pos;
            Vector3 to = target - enemy.pos;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance < SimConfig.EnemyArriveDist) enemy.repathTicks = 0;

            Vector3 horizontal = Vector3.zero;
            if (distance > CombatConfig.EnemyStopDistance)
            {
                Vector3 direction = to / distance;
                horizontal = direction * SimConfig.EnemyMoveSpeed * dt;
                enemy.yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            }
            Move(ref enemy, horizontal, in services, dt);
        }

        static bool IsAttackState(EnemyAIState state)
            => state == EnemyAIState.AttackWindup ||
               state == EnemyAIState.AttackActive ||
               state == EnemyAIState.AttackRecovery;

        static void BeginAttack(ref EnemySim enemy, in PlayerSim player)
        {
            enemy.aiState = EnemyAIState.AttackWindup;
            enemy.stateTicks = 0;
            enemy.committedAttackDirection = CombatMath.FlatDirection(enemy.pos, player.pos);
            enemy.yaw = Mathf.Atan2(
                enemy.committedAttackDirection.x,
                enemy.committedAttackDirection.z) * Mathf.Rad2Deg;
        }

        static void StartDescent(ref EnemySim enemy, Vector3 landing)
        {
            enemy.descentPhase = DescentPhase.EdgePause;
            enemy.aiState = EnemyAIState.Traversal;
            enemy.descentTicks = 0;
            enemy.jumpStart = enemy.pos;
            enemy.jumpEnd = landing;
            enemy.vel = Vector3.zero;
            Vector3 direction = landing - enemy.pos;
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-4f)
                enemy.yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        static void StepDescent(ref EnemySim enemy, float dt)
        {
            switch (enemy.descentPhase)
            {
                case DescentPhase.EdgePause:
                    if (++enemy.descentTicks >= SimConfig.DescentEdgePauseTicks)
                    {
                        enemy.descentPhase = DescentPhase.Airborne;
                        enemy.descentTicks = 0;
                        float horizontal = CombatMath.FlatDistance(enemy.jumpStart, enemy.jumpEnd);
                        enemy.jumpDuration = Mathf.Max(
                            SimConfig.DescentJumpMinTicks,
                            Mathf.CeilToInt(horizontal / (SimConfig.DescentJumpSpeed * dt)));
                    }
                    break;

                case DescentPhase.Airborne:
                    enemy.descentTicks++;
                    float t = Mathf.Clamp01((float)enemy.descentTicks / enemy.jumpDuration);
                    Vector3 position = Vector3.Lerp(enemy.jumpStart, enemy.jumpEnd, t);
                    position.y += SimConfig.DescentJumpArcHeight * Mathf.Sin(Mathf.PI * t);
                    enemy.pos = position;
                    if (enemy.descentTicks >= enemy.jumpDuration)
                    {
                        enemy.pos = enemy.jumpEnd;
                        enemy.descentPhase = DescentPhase.Recovery;
                        enemy.aiState = EnemyAIState.LandingRecovery;
                        enemy.descentTicks = 0;
                    }
                    break;

                case DescentPhase.Recovery:
                    if (++enemy.descentTicks >= SimConfig.DescentRecoveryTicks)
                    {
                        enemy.descentPhase = DescentPhase.None;
                        enemy.aiState = EnemyAIState.Approach;
                        enemy.descentTicks = 0;
                        enemy.repathTicks = 0;
                        enemy.hasWaypoint = false;
                    }
                    break;
            }
        }

        static void Move(ref EnemySim enemy, Vector3 horizontal, in SimServices services, float dt)
        {
            enemy.pos = CharacterMotor.MoveHorizontal(
                services.Collision, enemy.pos, horizontal,
                SimConfig.EnemyRadius, SimConfig.EnemyHeight);
            CharacterMotor.ResolveVertical(
                services.Collision, ref enemy.pos, ref enemy.vel, dt, out enemy.grounded);
        }
    }
}
