using UnityEngine;

namespace Game.Sim
{
    public static class CombatResolve
    {
        public static void Run(ref SimWorld world, in SimServices services, float dt)
        {
            if (world.player.hitStunTicks > 0) world.player.hitStunTicks--;

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                if (enemy.combat.stunTicks > 0)
                {
                    enemy.combat.stunTicks--;
                    continue;
                }
                if (enemy.attackCooldownTicks > 0) enemy.attackCooldownTicks--;
                AdvanceEnemyAttack(ref enemy, ref world.player);
            }
        }

        static void AdvanceEnemyAttack(ref EnemySim enemy, ref PlayerSim player)
        {
            switch (enemy.aiState)
            {
                case EnemyAIState.AttackWindup:
                    if (++enemy.stateTicks >= CombatConfig.EnemyAttackWindupTicks)
                    {
                        enemy.aiState = EnemyAIState.AttackActive;
                        enemy.stateTicks = 0;
                        ApplyEnemyHit(in enemy, ref player);
                    }
                    break;

                case EnemyAIState.AttackActive:
                    if (++enemy.stateTicks >= CombatConfig.EnemyAttackActiveTicks)
                    {
                        enemy.aiState = EnemyAIState.AttackRecovery;
                        enemy.stateTicks = 0;
                    }
                    break;

                case EnemyAIState.AttackRecovery:
                    if (++enemy.stateTicks >= CombatConfig.EnemyAttackRecoveryTicks)
                    {
                        enemy.aiState = EnemyAIState.Approach;
                        enemy.stateTicks = 0;
                        enemy.attackCooldownTicks = CombatConfig.EnemyAttackCooldownTicks;
                    }
                    break;
            }
        }

        static void ApplyEnemyHit(in EnemySim enemy, ref PlayerSim player)
        {
            if (!player.alive) return;
            if (Mathf.Abs(player.pos.y - enemy.pos.y) > CombatConfig.EnemyAttackHeightTolerance)
                return;
            if (!CombatMath.InCone(
                enemy.pos,
                enemy.committedAttackDirection,
                player.pos,
                CombatConfig.EnemyAttackRange,
                CombatConfig.EnemyAttackHalfAngleDeg))
                return;

            player.health -= CombatConfig.DamagePerHit;
            player.hitStunTicks = CombatConfig.StunTicks;
            if (player.health > 0) return;
            player.health = 0;
            player.alive = false;
            player.vel = Vector3.zero;
        }
    }
}
