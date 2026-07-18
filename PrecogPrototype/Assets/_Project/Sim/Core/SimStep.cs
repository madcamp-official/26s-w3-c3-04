using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 시뮬레이션의 유일한 진입점. 월드를 한 틱 전진.
    /// 실제 게임·예측·검증이 모두 이 함수를 공유한다.
    /// 순서: 플레이어 → 전투 → 적 → 대미지정리 → 겹침 분리 → tick++.
    /// </summary>
    public static class SimStep
    {
        // 분리 밀침 누적용 스크래치 (단일 스레드 재사용, 매 호출 초기화 → 결정론 안전)
        static readonly Vector3[] pushScratch = new Vector3[SimConfig.MaxEnemies];

        public static void Run(ref SimWorld w, in InputCmd cmd, in SimServices svc)
        {
            float dt = SimConfig.TickDelta;

            w.prevPlayerPos = w.player.pos;   // 이동 전 위치 → 원거리 리드 조준용 속도 산출

            // 글로리킬 처형 중엔 조작 잠금(이동·점프·대시·공격·런지). 시선은 통과(카메라 고정이 이김).
            InputCmd pcmd = cmd;
            if (w.player.combat.gloryPhase != CombatConfig.GlNone)
            {
                pcmd.move = Vector2.zero;
                pcmd.jump = pcmd.dash = pcmd.attack = pcmd.lunge = false;
            }

            PlayerMovement.Step(ref w.player, in pcmd, in svc, dt);
            PlayerCombat.Step(ref w, in pcmd, in svc, dt);   // ← combat (평타/런지/글로리킬)

            for (int i = 0; i < w.enemyCount; i++)
                EnemyBrain.Step(ref w, i, in svc, dt);   // ← AI 세션 (상태머신; 내부에서 이동은 EnemyMovement)

            ProjectileSystem.Step(ref w, in svc, dt);       // ← AI 세션 (투사체 이동·충돌 → 히트 큐)
            CombatResolve.Run(ref w, in svc, dt);           // ← combat 세션 (대미지/스턴/처치 + 히트 큐 적용)

            Separate(ref w, in svc);

            w.tick++;
        }

        /// <summary>
        /// 겹침 분리(대칭). 밀친 결과를 CharacterMotor로 적용해 벽/경사를 뚫지 않게 한다.
        /// 밀침량을 먼저 누적(O(N²) 순수계산)한 뒤, 캐릭터당 한 번 이동(충돌 질의)으로 적용.
        /// </summary>
        static void Separate(ref SimWorld w, in SimServices svc)
        {
            float pr = SimConfig.PlayerRadius;

            for (int i = 0; i < w.enemyCount; i++) pushScratch[i] = Vector3.zero;
            Vector3 playerPush = Vector3.zero;

            // 적 ↔ 적 (하강 중인 적 제외) — 개별 반경 합으로 최소거리
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive || w.enemies[i].descentPhase != DescentPhase.None || w.enemies[i].combat.gloryStage > 0) continue;
                for (int j = i + 1; j < w.enemyCount; j++)
                {
                    if (!w.enemies[j].alive || w.enemies[j].descentPhase != DescentPhase.None || w.enemies[j].combat.gloryStage > 0) continue;
                    Vector3 p = Push(w.enemies[i].pos, w.enemies[j].pos,
                                     w.enemies[i].radius + w.enemies[j].radius,
                                     w.enemies[i].id, w.enemies[j].id);
                    pushScratch[i] += p;
                    pushScratch[j] -= p;
                }
            }

            // 적 ↔ 플레이어 (대칭) — 플레이어 반경 + 개별 적 반경
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive || w.enemies[i].descentPhase != DescentPhase.None || w.enemies[i].combat.gloryStage > 0) continue;
                Vector3 p = Push(w.player.pos, w.enemies[i].pos, pr + w.enemies[i].radius, -1, w.enemies[i].id);
                playerPush += p;
                pushScratch[i] -= p;
            }

            // 적용 (벽 충돌 거침 → 관통 방지). 0 밀침은 캐스트 없이 통과.
            w.player.pos = CharacterMotor.MoveHorizontal(svc.Collision, w.player.pos, playerPush, pr, SimConfig.PlayerHeight);
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive || w.enemies[i].descentPhase != DescentPhase.None || w.enemies[i].combat.gloryStage > 0) continue;
                w.enemies[i].pos = CharacterMotor.MoveHorizontal(svc.Collision, w.enemies[i].pos,
                                                                 pushScratch[i], w.enemies[i].radius, w.enemies[i].height);
            }
        }

        /// <summary>a를 b에서 밀어낼 밀침 벡터(대칭이라 절반). 정확히 겹치면 id로 방향.</summary>
        static Vector3 Push(Vector3 a, Vector3 b, float minDist, int idA, int idB)
        {
            Vector3 d = a - b; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq >= minDist * minDist) return Vector3.zero;

            float dist = Mathf.Sqrt(sq);
            Vector3 dir = dist > 1e-4f ? d / dist : ((idA < idB) ? Vector3.right : Vector3.left);
            float overlap = minDist - dist;
            return dir * (overlap * SimConfig.SeparationPush);
        }
    }
}
