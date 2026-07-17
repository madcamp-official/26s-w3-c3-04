using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 시뮬레이션의 유일한 진입점. 월드를 한 틱 전진.
    /// 실제 게임·예측·검증이 모두 이 함수를 공유한다.
    /// 순서: 플레이어 → 적(id 오름차순) → 겹침 분리 → tick++.
    /// </summary>
    public static class SimStep
    {
        public static void Run(ref SimWorld w, in InputCmd cmd, in SimServices svc)
        {
            float dt = SimConfig.TickDelta;

            PlayerMovement.Step(ref w.player, in cmd, in svc, dt);

            for (int i = 0; i < w.enemyCount; i++)
                EnemyMovement.Step(ref w.enemies[i], in w.player, in svc, dt);

            Separate(ref w);

            w.tick++;
        }

        /// <summary>캐릭터끼리 겹치면 서로 밀어냄(대칭). 정확히 겹치면 id로 방향 결정(결정론).</summary>
        static void Separate(ref SimWorld w)
        {
            float pr = SimConfig.PlayerRadius;
            float er = SimConfig.EnemyRadius;

            // 적 ↔ 적
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive) continue;
                for (int j = i + 1; j < w.enemyCount; j++)
                {
                    if (!w.enemies[j].alive) continue;
                    PushApart(ref w.enemies[i].pos, ref w.enemies[j].pos, er + er,
                              w.enemies[i].id, w.enemies[j].id);
                }
            }

            // 적 ↔ 플레이어 (대칭 — 적도 플레이어를 밈)
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive) continue;
                PushApart(ref w.player.pos, ref w.enemies[i].pos, pr + er, -1, w.enemies[i].id);
            }
        }

        static void PushApart(ref Vector3 a, ref Vector3 b, float minDist, int idA, int idB)
        {
            Vector3 d = a - b; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq >= minDist * minDist) return;

            float dist = Mathf.Sqrt(sq);
            Vector3 dir;
            if (dist > 1e-4f) dir = d / dist;
            else dir = (idA < idB) ? Vector3.right : Vector3.left;  // 정확히 겹침: id로 결정

            float overlap = minDist - dist;
            Vector3 push = dir * (overlap * SimConfig.SeparationPush);
            a += push;
            b -= push;
        }
    }
}
