using UnityEngine;

namespace Game.Sim
{
    public static class PlayerCombat
    {
        public static void Step(ref SimWorld world, in InputCmd cmd, in SimServices services, float dt)
        {
            ref PlayerSim player = ref world.player;
            ref PlayerCombatState combat = ref player.combat;
            if (!player.alive) return;

            if (combat.lungeCooldownTicks > 0) combat.lungeCooldownTicks--;

            switch (combat.phase)
            {
                case PlayerActionPhase.None:
                    if (cmd.lunge && combat.lungeCooldownTicks == 0 &&
                        TryStartLunge(ref world, in cmd, in services))
                        return;
                    if (cmd.attack) BeginAttack(ref combat);
                    return;

                case PlayerActionPhase.AttackWindup:
                    if (++combat.phaseTicks >= CombatConfig.AttackWindupTicks)
                    {
                        combat.phase = PlayerActionPhase.AttackActive;
                        combat.phaseTicks = 0;
                        ApplyAttack(ref world);
                    }
                    return;

                case PlayerActionPhase.AttackActive:
                    if (++combat.phaseTicks >= CombatConfig.AttackActiveTicks)
                    {
                        combat.phase = PlayerActionPhase.AttackRecovery;
                        combat.phaseTicks = 0;
                    }
                    return;

                case PlayerActionPhase.AttackRecovery:
                    if (++combat.phaseTicks >= CombatConfig.AttackRecoveryTicks)
                        ResetAction(ref combat);
                    return;

                case PlayerActionPhase.LungeWindup:
                    if (++combat.phaseTicks >= CombatConfig.LungeWindupTicks)
                    {
                        combat.phase = PlayerActionPhase.LungeTravel;
                        combat.phaseTicks = 0;
                        combat.lungeElapsedTicks = 0;
                    }
                    return;

                case PlayerActionPhase.LungeTravel:
                    combat.lungeElapsedTicks++;
                    float t = Mathf.Clamp01((float)combat.lungeElapsedTicks / CombatConfig.LungeTravelTicks);
                    player.pos = Vector3.Lerp(combat.lungeStart, combat.lungeDestination, t);
                    player.vel = Vector3.zero;
                    if (combat.lungeElapsedTicks >= CombatConfig.LungeTravelTicks)
                    {
                        ApplyLungeHit(ref world, combat.lungeTargetId);
                        combat.phase = PlayerActionPhase.LungeRecovery;
                        combat.phaseTicks = 0;
                    }
                    return;

                case PlayerActionPhase.LungeRecovery:
                    if (++combat.phaseTicks >= CombatConfig.LungeRecoveryTicks)
                        ResetAction(ref combat);
                    return;
            }
        }

        static void BeginAttack(ref PlayerCombatState combat)
        {
            combat.phase = PlayerActionPhase.AttackWindup;
            combat.phaseTicks = 0;
            combat.attackSequence++;
        }

        static bool TryStartLunge(
            ref SimWorld world,
            in InputCmd cmd,
            in SimServices services)
        {
            ref PlayerSim player = ref world.player;
            int targetId = cmd.lungeTargetId >= 0
                ? cmd.lungeTargetId
                : FindLungeTarget(in world, in player, in services);
            int index = FindEnemyIndex(in world, targetId);
            if (index < 0) return false;

            ref readonly EnemySim enemy = ref world.enemies[index];
            if (!CanTarget(in player, in enemy, in services, out Vector3 destination))
                return false;

            ref PlayerCombatState combat = ref player.combat;
            combat.phase = PlayerActionPhase.LungeWindup;
            combat.phaseTicks = 0;
            combat.lungeTargetId = enemy.id;
            combat.lungeStart = player.pos;
            combat.lungeDestination = destination;
            combat.lungeElapsedTicks = 0;
            combat.lungeCooldownTicks = CombatConfig.LungeCooldownTicks;
            player.yaw = Mathf.Atan2(
                enemy.pos.x - player.pos.x,
                enemy.pos.z - player.pos.z) * Mathf.Rad2Deg;
            return true;
        }

        public static int FindLungeTarget(
            in SimWorld world,
            in PlayerSim player,
            in SimServices services)
        {
            int bestId = -1;
            float bestDot = -2f;
            float bestDistance = float.MaxValue;
            Vector3 forward = CombatMath.Forward(player.yaw);

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!CanTarget(in player, in enemy, in services, out _)) continue;

                Vector3 direction = CombatMath.FlatDirection(player.pos, enemy.pos);
                float dot = Vector3.Dot(forward, direction);
                float distance = CombatMath.FlatDistance(player.pos, enemy.pos);

                bool better = dot > bestDot + 1e-6f ||
                    (Mathf.Abs(dot - bestDot) <= 1e-6f &&
                     (distance < bestDistance - 1e-5f ||
                      (Mathf.Abs(distance - bestDistance) <= 1e-5f && enemy.id < bestId)));
                if (!better) continue;
                bestId = enemy.id;
                bestDot = dot;
                bestDistance = distance;
            }
            return bestId;
        }

        static bool CanTarget(
            in PlayerSim player,
            in EnemySim enemy,
            in SimServices services,
            out Vector3 destination)
        {
            destination = player.pos;
            if (!enemy.alive) return false;
            if (Mathf.Abs(enemy.pos.y - player.pos.y) > CombatConfig.LungeHeightTolerance)
                return false;

            float distance = CombatMath.FlatDistance(player.pos, enemy.pos);
            if (distance < CombatConfig.LungeMinRange || distance > CombatConfig.LungeMaxRange)
                return false;

            Vector3 forward = CombatMath.Forward(player.yaw);
            if (!CombatMath.InCone(
                player.pos, forward, enemy.pos,
                CombatConfig.LungeMaxRange, CombatConfig.LungeHalfAngleDeg))
                return false;

            Vector3 eye = player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 target = enemy.pos + Vector3.up * (SimConfig.EnemyHeight * 0.6f);
            if (!services.Collision.HasLineOfSight(eye, target)) return false;

            Vector3 direction = CombatMath.FlatDirection(player.pos, enemy.pos);
            destination = enemy.pos - direction * CombatConfig.LungeStopDistance;
            if (!services.Collision.SampleGround(destination, 2f, out float groundY)) return false;
            destination.y = groundY;
            return services.Collision.CanOccupyCapsule(
                destination, SimConfig.PlayerRadius, SimConfig.PlayerHeight);
        }

        static void ApplyAttack(ref SimWorld world)
        {
            Vector3 forward = CombatMath.Forward(world.player.yaw);
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                if (Mathf.Abs(enemy.pos.y - world.player.pos.y) >
                    CombatConfig.AttackHeightTolerance) continue;
                if (!CombatMath.InCone(
                    world.player.pos, forward, enemy.pos,
                    CombatConfig.AttackRange, CombatConfig.AttackHalfAngleDeg)) continue;
                ApplyEnemyDamage(ref enemy, world.tick);
            }
        }

        static void ApplyLungeHit(ref SimWorld world, int targetId)
        {
            int index = FindEnemyIndex(in world, targetId);
            if (index >= 0) ApplyEnemyDamage(ref world.enemies[index], world.tick);
        }

        static void ApplyEnemyDamage(ref EnemySim enemy, int tick)
        {
            enemy.combat.health -= CombatConfig.DamagePerHit;
            enemy.combat.stunTicks = CombatConfig.StunTicks;
            if (enemy.combat.health > 0) return;
            enemy.combat.health = 0;
            enemy.combat.deathTick = tick;
            enemy.alive = false;
            enemy.aiState = EnemyAIState.Dead;
        }

        static int FindEnemyIndex(in SimWorld world, int id)
        {
            for (int i = 0; i < world.enemyCount; i++)
                if (world.enemies[i].id == id && world.enemies[i].alive) return i;
            return -1;
        }

        static void ResetAction(ref PlayerCombatState combat)
        {
            combat.phase = PlayerActionPhase.None;
            combat.phaseTicks = 0;
            combat.lungeTargetId = -1;
            combat.lungeElapsedTicks = 0;
        }
    }
}
