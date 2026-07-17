namespace Game.Sim
{
    /// <summary>
    /// 전투 상태의 결정론 해시. ★ combat 세션 소유.
    /// WorldHash가 이걸 호출한다. combat이 필드를 늘리면 여기만 고치면 되고,
    /// 공유 파일 WorldHash.cs는 안 건드린다. FNV-1a (WorldHash와 동일 상수).
    /// </summary>
    public static class CombatHash
    {
        const ulong Prime = 1099511628211UL;
        static ulong Mix(ulong h, ulong v) { h ^= v; h *= Prime; return h; }

        public static ulong MixPlayer(ulong h, in PlayerCombatState c)
        {
            h = Mix(h, c.attackPhase);
            h = Mix(h, (ulong)c.attackPhaseTicks);
            h = Mix(h, (ulong)c.comboCancelTicks);
            h = Mix(h, c.blocking ? 1UL : 0UL);
            h = Mix(h, (ulong)c.guardGauge);
            h = Mix(h, c.backstrikePhase);
            h = Mix(h, (ulong)c.backstrikeTicks);
            h = Mix(h, (ulong)c.frontGuardTicks);
            return h;
        }

        public static ulong MixEnemy(ulong h, in EnemyCombatState c)
        {
            h = Mix(h, (ulong)c.health);
            h = Mix(h, (ulong)c.stunTicks);
            h = Mix(h, (ulong)c.deathTick);
            return h;
        }
    }
}
