using System.Runtime.InteropServices;
using UnityEngine;

namespace Game.Sim
{
    [StructLayout(LayoutKind.Explicit)]
    struct FloatBits { [FieldOffset(0)] public float f; [FieldOffset(0)] public uint u; }

    /// <summary>월드 상태 → 64비트. 결정론 검증용(같은 상태 두 번 굴려 해시 비교).</summary>
    public static class WorldHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime  = 1099511628211UL;

        public static ulong Compute(in SimWorld w)
        {
            ulong h = Offset;
            h = Mix(h, (ulong)w.tick);

            h = MixV(h, w.player.pos);
            h = MixV(h, w.player.vel);
            h = MixF(h, w.player.yaw);
            h = Mix(h, w.player.grounded ? 1UL : 0UL);
            h = Mix(h, (ulong)w.player.jumpCount);
            h = Mix(h, (ulong)w.player.dashTicks);
            h = Mix(h, (ulong)w.player.dashCharges);
            h = Mix(h, (ulong)w.player.dashRecharge);

            h = Mix(h, (ulong)w.enemyCount);
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                h = Mix(h, (ulong)e.id);
                h = Mix(h, e.alive ? 1UL : 0UL);
                h = MixV(h, e.pos);
                h = MixV(h, e.vel);
                h = MixF(h, e.yaw);
                h = MixV(h, e.waypoint);
                h = Mix(h, e.hasWaypoint ? 1UL : 0UL);
                h = Mix(h, (ulong)e.repathTicks);
            }
            return h;
        }

        static ulong Mix(ulong h, ulong v) { h ^= v; h *= Prime; return h; }
        static ulong MixF(ulong h, float f) { FloatBits b; b.u = 0; b.f = f; return Mix(h, b.u); }
        static ulong MixV(ulong h, Vector3 v) { h = MixF(h, v.x); h = MixF(h, v.y); h = MixF(h, v.z); return h; }
    }
}
