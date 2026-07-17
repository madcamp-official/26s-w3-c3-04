using System.Runtime.InteropServices;
using UnityEngine;

namespace Game.Simulation
{
    /// <summary>float ↔ uint 비트 재해석 (할당·unsafe 없음, 모든 런타임 호환).</summary>
    [StructLayout(LayoutKind.Explicit)]
    struct FloatBits
    {
        [FieldOffset(0)] public float f;
        [FieldOffset(0)] public uint  u;
    }

    /// <summary>
    /// 월드 상태를 하나의 64비트 값으로 압축. FNV-1a 방식.
    /// 결정론 검증에 쓴다 — 같은 상태를 두 번 굴려 해시가 같으면 통과.
    ///
    /// 반드시 "다음 틱 행동에 영향을 주는 모든 필드"를 넣어야 한다.
    /// 위치만 넣고 aiState/stateTicks를 빠뜨리면, 위치가 같아도 다음 틱에
    /// 행동이 갈라지는 디싱크를 놓친다. (팀원 v1.1 검토 참고)
    /// </summary>
    public static class WorldHash
    {
        const ulong FnvOffset = 14695981039346656037UL;
        const ulong FnvPrime  = 1099511628211UL;

        public static ulong Compute(in SimWorld w)
        {
            ulong h = FnvOffset;

            h = Mix(h, (ulong)w.tick);

            // 플레이어
            h = MixVec(h, w.player.pos);
            h = MixVec(h, w.player.vel);
            h = MixF(h, w.player.yaw);
            h = Mix(h, w.player.grounded ? 1UL : 0UL);
            h = Mix(h, (ulong)w.player.jumpCount);
            h = Mix(h, w.player.alive ? 1UL : 0UL);
            h = Mix(h, (ulong)w.player.hp);

            // 적 (id 오름차순 = 배열 순서)
            h = Mix(h, (ulong)w.enemyCount);
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemyState e = ref w.enemies[i];
                h = Mix(h, (ulong)e.id);
                h = Mix(h, (ulong)e.type);
                h = Mix(h, e.alive ? 1UL : 0UL);
                h = MixVec(h, e.pos);
                h = MixVec(h, e.vel);
                h = MixF(h, e.yaw);
                h = Mix(h, (ulong)e.aiState);
                h = Mix(h, (ulong)e.stateTicks);
                h = Mix(h, (ulong)e.attackCooldownTicks);
                h = Mix(h, (ulong)e.stunTicks);
            }

            return h;
        }

        static ulong Mix(ulong h, ulong v)
        {
            h ^= v;
            h *= FnvPrime;
            return h;
        }

        static ulong MixF(ulong h, float f)
        {
            // float의 비트 패턴을 그대로 섞는다.
            FloatBits fb; fb.u = 0; fb.f = f;
            return Mix(h, fb.u);
        }

        static ulong MixVec(ulong h, Vector3 v)
        {
            h = MixF(h, v.x);
            h = MixF(h, v.y);
            h = MixF(h, v.z);
            return h;
        }
    }
}
