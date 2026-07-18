using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 상태 중복 제거용 양자화 키. OPTIMIZATION.md 11장 "초기 양자화" 기준(위치 0.5m,
    /// yaw 15~30도, 쿨타임 6~15틱 단위)으로 거친 격자에만 묶는다 — 실제로 다른 위험
    /// 상태를 과도하게 병합하지 않도록 격자를 넓히지 않는다. WorldHash(Sim, 정확값 기반)와는
    /// 목적이 달라 별도 구현이며, Sim 파일을 참조하지 않는다(예측 전용 판정 새로 안 만든다는
    /// 원칙과는 별개로 이건 순수 상태 뭉치기용 해시라 Sim에 넣을 이유가 없다).
    /// </summary>
    public static class StateDeduplicator
    {
        const float PositionGrid = 0.5f;
        const float YawGridDeg = 22.5f;
        const int CooldownBucketTicks = 10;

        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong ComputeKey(in SimWorld world, int killCount)
        {
            ulong h = Offset;
            h = Mix(h, (ulong)killCount);

            ref readonly PlayerSim player = ref world.player;
            h = Mix(h, player.combat.hp > 0 ? 1UL : 0UL);
            h = Mix(h, (ulong)player.combat.hp);
            h = MixPos(h, player.pos);
            h = Mix(h, QuantizeYaw(player.yaw));
            bool dashReady = player.dashTicks == 0 && player.dashCharges > 0;
            h = Mix(h, dashReady ? 1UL : 0UL);
            h = Mix(h, (ulong)(player.combat.lungeCooldown / CooldownBucketTicks));
            h = Mix(h, (ulong)player.combat.attackPhase);
            h = Mix(h, (ulong)player.combat.lungePhase);
            // 글로리킬 컷신 중엔 무적·조작잠금이라 완전히 다른 위상이다 — 뭉치면 안 됨.
            h = Mix(h, (ulong)player.combat.gloryPhase);

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                h = Mix(h, (ulong)enemy.id);
                h = Mix(h, enemy.alive ? 1UL : 0UL);
                if (!enemy.alive) continue;
                h = Mix(h, (ulong)enemy.ai.archetype);
                h = MixPos(h, enemy.pos);
                h = Mix(h, (ulong)enemy.combat.health);
                h = Mix(h, IsThreatening(enemy.ai.state) ? 1UL : 0UL);
                h = Mix(h, (ulong)(enemy.ai.attackCooldown / CooldownBucketTicks));
                // 처형 확정(gloryStage>0)은 이후 alive=false와 별개로 이미 결과가 잠긴 상태다.
                h = Mix(h, enemy.combat.gloryStage > 0 ? 1UL : 0UL);
            }

            // 활성 투사체가 다른 후보를 같은 상태로 합치면 미래 피격 결과가 달라질 수 있다.
            h = Mix(h, (ulong)world.projectileCount);
            for (int i = 0; i < world.projectileCount; i++)
            {
                ref readonly Projectile projectile = ref world.projectiles[i];
                h = Mix(h, projectile.alive ? 1UL : 0UL);
                if (!projectile.alive) continue;
                h = MixPos(h, projectile.pos);
                h = Mix(h, QuantizeYaw(Mathf.Atan2(
                    projectile.vel.x, projectile.vel.z) * Mathf.Rad2Deg));
                h = Mix(h, (ulong)(projectile.ttl / CooldownBucketTicks));
            }
            return h;
        }

        static bool IsThreatening(EnemyState state)
            => state == EnemyState.Windup || state == EnemyState.Active
            || state == EnemyState.Aim || state == EnemyState.Fire;

        static ulong MixPos(ulong h, Vector3 p)
        {
            h = Mix(h, unchecked((ulong)Mathf.RoundToInt(p.x / PositionGrid)));
            h = Mix(h, unchecked((ulong)Mathf.RoundToInt(p.y / PositionGrid)));
            h = Mix(h, unchecked((ulong)Mathf.RoundToInt(p.z / PositionGrid)));
            return h;
        }

        static ulong QuantizeYaw(float yaw) => unchecked((ulong)Mathf.RoundToInt(yaw / YawGridDeg));

        static ulong Mix(ulong h, ulong v) { h ^= v; h *= Prime; return h; }
    }
}
