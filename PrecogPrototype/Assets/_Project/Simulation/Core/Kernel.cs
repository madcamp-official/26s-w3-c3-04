using UnityEngine;

namespace Game.Simulation
{
    /// <summary>
    /// 시뮬레이션의 유일한 진입점. 월드를 한 틱 전진시킨다.
    /// 실제 게임(Runtime), 예측 탐색(Prediction), 결정론 검증이 모두 이 함수를
    /// 공유한다 → 예측이 본 미래와 실제가 어긋나지 않는다 (단일 시뮬레이션 함수).
    ///
    /// Transform / GameObject / Input / 난수를 절대 참조하지 않는다.
    /// 고정 업데이트 순서를 지킨다 (팀원 v1.1 5.4 참고).
    /// </summary>
    public static class Kernel
    {
        public static void Step(ref SimWorld w, in InputCmd cmd)
        {
            float dt = SimConfig.TickDelta;

            // 1. 플레이어 이동 (묶음 A: 이동 + 중력 + 점프만. 전투는 묶음 B)
            StepPlayer(ref w.player, in cmd, dt);

            // 2. 적: id 오름차순으로 결정 (동률 해소를 위해 순서 고정)
            for (int i = 0; i < w.enemyCount; i++)
            {
                MeleeAI.Tick(ref w.enemies[i], in w.player, dt);
            }

            // 3. 적 이동 + 중력 적용
            for (int i = 0; i < w.enemyCount; i++)
            {
                StepEnemyMotion(ref w.enemies[i], dt);
            }

            // 4. 전투 판정: 적의 공격이 플레이어에 닿는가 (id 오름차순)
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (MeleeAI.AttackConnects(in w.enemies[i], in w.player))
                {
                    DamagePlayer(ref w.player, 1);
                }
            }

            // 5. 틱 증가
            w.tick++;
        }

        static void StepPlayer(ref PlayerState p, in InputCmd cmd, float dt)
        {
            if (!p.alive) return;

            p.yaw = cmd.yaw;

            // 수평 이동: 바라보는 방향 기준
            Vector3 fwd = new Vector3(Mathf.Sin(p.yaw * Mathf.Deg2Rad), 0f,
                                      Mathf.Cos(p.yaw * Mathf.Deg2Rad));
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 wish = (right * cmd.move.x + fwd * cmd.move.y);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            p.vel.x = wish.x * SimConfig.PlayerMoveSpeed;
            p.vel.z = wish.z * SimConfig.PlayerMoveSpeed;

            // 점프 (더블점프까지)
            if (cmd.jump && p.jumpCount < 2)
            {
                p.vel.y = SimConfig.PlayerJumpSpeed;
                p.jumpCount++;
                p.grounded = false;
            }

            // 중력
            p.vel.y += SimConfig.Gravity * dt;

            // 적분
            p.pos += p.vel * dt;

            // 바닥 처리
            if (p.pos.y <= SimConfig.GroundY)
            {
                p.pos.y = SimConfig.GroundY;
                p.vel.y = 0f;
                p.grounded = true;
                p.jumpCount = 0;
            }
        }

        static void StepEnemyMotion(ref EnemyState e, float dt)
        {
            if (!e.alive) return;

            // 중력 (경직 중에도 떨어짐 — ADR-0005)
            e.vel.y += SimConfig.Gravity * dt;
            e.pos += e.vel * dt;

            if (e.pos.y <= SimConfig.GroundY)
            {
                e.pos.y = SimConfig.GroundY;
                e.vel.y = 0f;
            }
        }

        static void DamagePlayer(ref PlayerState p, int amount)
        {
            if (!p.alive) return;
            p.hp -= amount;
            if (p.hp <= 0)
            {
                p.hp = 0;
                p.alive = false;
            }
        }
    }
}
