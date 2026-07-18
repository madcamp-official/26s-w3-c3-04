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
            h = Mix(h, player.alive ? 1UL : 0UL);
            h = Mix(h, (ulong)player.health);
            h = MixPos(h, player.pos);
            h = Mix(h, QuantizeYaw(player.yaw));
            bool dashReady = player.dashTicks == 0 && player.dashCharges > 0;
            h = Mix(h, dashReady ? 1UL : 0UL);
            h = Mix(h, (ulong)(player.combat.lungeCooldownTicks / CooldownBucketTicks));
            h = Mix(h, (ulong)player.combat.phase);

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                h = Mix(h, (ulong)enemy.id);
                h = Mix(h, enemy.alive ? 1UL : 0UL);
                if (!enemy.alive) continue;
                h = MixPos(h, enemy.pos);
                h = Mix(h, (ulong)enemy.combat.health);
                h = Mix(h, IsThreatening(enemy.aiState) ? 1UL : 0UL);
            }
            return h;
        }

        static bool IsThreatening(EnemyAIState state)
            => state == EnemyAIState.AttackWindup || state == EnemyAIState.AttackActive;

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
