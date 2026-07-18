using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>가중치와 분리된 미래 위험 관측값.</summary>
    public struct FutureThreatObservation
    {
        public bool playerAlive;
        public int playerHealth;
        public int aliveEnemyCount;
        public int attackWindupCount;
        public int nearestAttackActiveTicks;
        public int activeProjectileCount;
        public int nearestProjectileImpactTicks;
        public float nearestProjectileMissDistance;
        public bool hasEscapeRoute;
    }

    public static class FutureThreatObserver
    {
        public static FutureThreatObservation Observe(in SimWorld world)
        {
            var result = new FutureThreatObservation
            {
                playerAlive = world.player.combat.hp > 0,
                playerHealth = world.player.combat.hp,
                nearestAttackActiveTicks = int.MaxValue,
                nearestProjectileImpactTicks = int.MaxValue,
                nearestProjectileMissDistance = float.MaxValue
            };

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                result.aliveEnemyCount++;

                bool isMeleeWindup = enemy.ai.combat == CombatType.Melee && enemy.ai.state == EnemyState.Windup;
                bool isRangedAim = enemy.ai.combat == CombatType.Ranged && enemy.ai.state == EnemyState.Aim;
                if (isMeleeWindup || isRangedAim)
                {
                    result.attackWindupCount++;
                    int windupTicks = isRangedAim ? AIConfig.RangedAimTicks : AIConfig.MeleeWindupTicks;
                    int ticks = Mathf.Max(0, windupTicks - enemy.ai.stateTicks);
                    if (ticks < result.nearestAttackActiveTicks)
                        result.nearestAttackActiveTicks = ticks;
                }
            }

            for (int i = 0; i < world.projectileCount; i++)
            {
                ref readonly Projectile projectile = ref world.projectiles[i];
                if (!projectile.alive) continue;
                result.activeProjectileCount++;
                ObserveClosestApproach(in world, in projectile, ref result);
            }
            return result;
        }

        static void ObserveClosestApproach(
            in SimWorld world,
            in Projectile projectile,
            ref FutureThreatObservation result)
        {
            // ProjectileSystem.Step의 실제 판정점(플레이어 몸통 구, 발밑이 아님)과 맞춘다.
            Vector3 torso = world.player.pos + Vector3.up * AIConfig.PlayerTorso;
            Vector3 relative = torso - projectile.pos;
            float speedSq = projectile.vel.sqrMagnitude;
            if (speedSq <= 1e-6f) return;
            float seconds = Mathf.Clamp(
                Vector3.Dot(relative, projectile.vel) / speedSq,
                0f,
                projectile.ttl * SimConfig.TickDelta);
            Vector3 closest = projectile.pos + projectile.vel * seconds;
            float missDistance = Vector3.Distance(closest, torso);
            int ticks = Mathf.CeilToInt(seconds * SimConfig.TickRate);
            if (missDistance < result.nearestProjectileMissDistance)
                result.nearestProjectileMissDistance = missDistance;
            if (missDistance <= AIConfig.ProjectileRadius + SimConfig.PlayerRadius &&
                ticks < result.nearestProjectileImpactTicks)
                result.nearestProjectileImpactTicks = ticks;
        }
    }
}
