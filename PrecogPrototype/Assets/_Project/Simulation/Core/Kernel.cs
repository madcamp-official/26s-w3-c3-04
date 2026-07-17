using UnityEngine;

namespace Game.Simulation
{
    /// <summary>
    /// 시뮬레이션의 유일한 진입점. 월드를 한 틱 전진시킨다.
    /// 실제 게임(Runtime), 예측 탐색(Prediction), 결정론 검증이 모두 이 함수를
    /// 공유한다 → 예측이 본 미래와 실제가 어긋나지 않는다 (단일 시뮬레이션 함수).
    ///
    /// Transform / GameObject / Input / 난수를 절대 참조하지 않는다.
    /// 고정 업데이트 순서를 지킨다.
    /// </summary>
    public static class Kernel
    {
        public static void Step(ref SimWorld w, in InputCmd cmd)
        {
            float dt = SimConfig.TickDelta;

            // 1. 플레이어: 입력 반영 + 전투 상태 진행 + 이동
            StepPlayer(ref w, in cmd, dt);

            // 2. 적: id 오름차순 결정 (규칙 트리, 난수 없음)
            for (int i = 0; i < w.enemyCount; i++)
                MeleeAI.Tick(ref w.enemies[i], in w.player, dt);

            // 3. 적 이동 + 중력
            for (int i = 0; i < w.enemyCount; i++)
                StepEnemyMotion(ref w.enemies[i], dt);

            // 4. 플레이어 평타 판정 (Active 진입 틱에 부채꼴 처치)
            ResolvePlayerAttack(ref w);

            // 5. 적 공격 판정 (막기/무적 없으면 플레이어 피격)
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (MeleeAI.AttackConnects(in w.enemies[i], in w.player))
                    ResolveEnemyHit(ref w.player, in w.enemies[i]);
            }

            w.tick++;
        }

        // ─────────────────────────── 플레이어 ───────────────────────────

        static void StepPlayer(ref SimWorld w, in InputCmd cmd, float dt)
        {
            ref PlayerState p = ref w.player;
            if (!p.alive) return;

            p.yaw = cmd.yaw;
            Vector3 fwd = Forward(p.yaw);

            // 쿨타임/타이머 감소
            if (p.attackCooldownTicks > 0) p.attackCooldownTicks--;
            if (p.iFrameTicks > 0) p.iFrameTicks--;
            if (p.dashRechargeTicks > 0)
            {
                p.dashRechargeTicks--;
                if (p.dashRechargeTicks == 0 && p.dashCharges < SimConfig.DashMaxCharges)
                {
                    p.dashCharges++;
                    if (p.dashCharges < SimConfig.DashMaxCharges)
                        p.dashRechargeTicks = SimConfig.DashRechargeTicks; // 다음 충전 예약
                }
            }

            // ── 새 액션 시작 (우선순위: 질풍참 > 평타) ──
            bool busy = p.dashTicksRemaining > 0 || p.attackPhase != AttackPhase.None;

            if (!busy && cmd.dash && p.dashCharges > 0 && !cmd.block)
                StartDash(ref p, fwd);

            if (!busy && cmd.attack && p.attackCooldownTicks == 0
                && p.dashTicksRemaining == 0)
                StartAttack(ref w, fwd);

            // 막기: 돌진/공격 중이 아니고 게이지 있으면
            p.blocking = cmd.block && p.dashTicksRemaining == 0
                         && p.attackPhase == AttackPhase.None && p.guardGauge > 0;

            // ── 이동 결정: 돌진 > 보정 > 일반 ──
            if (p.dashTicksRemaining > 0)
            {
                StepDashMotion(ref w, dt);
            }
            else if (p.attackPhase == AttackPhase.Windup || p.attackPhase == AttackPhase.Active)
            {
                // 글로리킬식 보정 전진
                p.vel.x = p.lungeDir.x * SimConfig.AttackLungeSpeed;
                p.vel.z = p.lungeDir.z * SimConfig.AttackLungeSpeed;
                ApplyGravityAndIntegrate(ref p, dt);
            }
            else
            {
                // 일반 이동
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                Vector3 wish = right * cmd.move.x + fwd * cmd.move.y;
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                p.vel.x = wish.x * SimConfig.PlayerMoveSpeed;
                p.vel.z = wish.z * SimConfig.PlayerMoveSpeed;

                if (cmd.jump && p.jumpCount < 2)
                {
                    p.vel.y = SimConfig.PlayerJumpSpeed;
                    p.jumpCount++;
                    p.grounded = false;
                }
                ApplyGravityAndIntegrate(ref p, dt);
            }

            // 평타 단계 진행 (처치 판정은 ResolvePlayerAttack에서)
            AdvanceAttackPhase(ref p);

            // 가드 게이지 회복 (안 b: 비홀드 후 지연 회복)
            if (p.blocking)
            {
                p.guardIdleTicks = 0;
            }
            else
            {
                p.guardIdleTicks++;
                if (p.guardIdleTicks >= SimConfig.GuardRegenDelayTicks
                    && p.guardGauge < SimConfig.GuardMaxTicks)
                {
                    p.guardGauge = Mathf.Min(SimConfig.GuardMaxTicks,
                                             p.guardGauge + SimConfig.GuardRegenPerTick);
                }
            }
        }

        static void StartDash(ref PlayerState p, Vector3 fwd)
        {
            p.dashTicksRemaining = SimConfig.DashDurationTicks;
            p.dashDir = fwd;
            p.dashStartPos = p.pos;
            p.dashCharges--;
            // 충전 회복 타이머 시작 (이미 도는 중이 아니면)
            if (p.dashRechargeTicks == 0)
                p.dashRechargeTicks = SimConfig.DashRechargeTicks;
        }

        static void StepDashMotion(ref SimWorld w, float dt)
        {
            ref PlayerState p = ref w.player;
            Vector3 prev = p.pos;

            float distPerTick = SimConfig.DashDistance / SimConfig.DashDurationTicks;
            p.pos += p.dashDir * distPerTick;

            // 중력은 유지하되 수평은 돌진이 지배
            p.vel.y += SimConfig.Gravity * dt;
            p.pos.y += p.vel.y * dt;
            if (p.pos.y <= SimConfig.GroundY)
            {
                p.pos.y = SimConfig.GroundY;
                p.vel.y = 0f;
                p.jumpCount = 0;
                p.grounded = true;
            }

            // 관통 판정: 이번 틱 이동 선분에 걸린 적에게 스턴 (대미지 0, ADR-0005)
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref EnemyState e = ref w.enemies[i];
                if (!e.alive) continue;
                if (Hit.SegmentHitsSphere(prev, p.pos, e.pos,
                                          SimConfig.DashHitRadius + SimConfig.MeleeRadius))
                {
                    e.stunTicks = SimConfig.DashStunTicks;
                }
            }

            p.dashTicksRemaining--;
        }

        static void StartAttack(ref SimWorld w, Vector3 fwd)
        {
            ref PlayerState p = ref w.player;
            p.attackPhase = AttackPhase.Windup;
            p.attackPhaseTicks = 0;
            p.iFrameTicks = SimConfig.AttackIFrameTicks;

            // 보정 대상: 조준선(부채꼴) 안 가장 가까운 적. 동률은 id 낮은 쪽.
            int best = -1;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemyState e = ref w.enemies[i];
                if (!e.alive) continue;
                if (!Hit.InCone(p.pos, fwd, e.pos,
                                SimConfig.AttackConeRange, SimConfig.AttackConeHalfAngle))
                    continue;
                float d = Hit.FlatSqrDist(p.pos, e.pos);
                if (d < bestSqr) { bestSqr = d; best = i; }
            }

            if (best >= 0)
            {
                Vector3 to = w.enemies[best].pos - p.pos; to.y = 0f;
                p.lungeDir = to.sqrMagnitude > 1e-6f ? to.normalized : fwd;
            }
            else p.lungeDir = fwd;
        }

        static void AdvanceAttackPhase(ref PlayerState p)
        {
            if (p.attackPhase == AttackPhase.None) return;
            p.attackPhaseTicks++;

            switch (p.attackPhase)
            {
                case AttackPhase.Windup:
                    if (p.attackPhaseTicks >= SimConfig.AttackWindupTicks)
                    { p.attackPhase = AttackPhase.Active; p.attackPhaseTicks = 0; }
                    break;
                case AttackPhase.Active:
                    if (p.attackPhaseTicks >= SimConfig.AttackActiveTicks)
                    { p.attackPhase = AttackPhase.Recovery; p.attackPhaseTicks = 0; }
                    break;
                case AttackPhase.Recovery:
                    if (p.attackPhaseTicks >= SimConfig.AttackRecoveryTicks)
                    {
                        p.attackPhase = AttackPhase.None;
                        p.attackCooldownTicks = SimConfig.AttackCooldownTicks;
                    }
                    break;
            }
        }

        /// <summary>Active 첫 틱에 부채꼴 안 적 전부 처치 (평타 약간 광역).</summary>
        static void ResolvePlayerAttack(ref SimWorld w)
        {
            ref PlayerState p = ref w.player;
            if (p.attackPhase != AttackPhase.Active || p.attackPhaseTicks != 1) return;

            Vector3 fwd = Forward(p.yaw);
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref EnemyState e = ref w.enemies[i];
                if (!e.alive) continue;
                if (Hit.InCone(p.pos, fwd, e.pos,
                               SimConfig.AttackConeRange, SimConfig.AttackConeHalfAngle))
                {
                    e.alive = false;   // 한방컷 (사지절단은 표현 연출)
                }
            }
        }

        static void ApplyGravityAndIntegrate(ref PlayerState p, float dt)
        {
            p.vel.y += SimConfig.Gravity * dt;
            p.pos += p.vel * dt;
            if (p.pos.y <= SimConfig.GroundY)
            {
                p.pos.y = SimConfig.GroundY;
                p.vel.y = 0f;
                p.grounded = true;
                p.jumpCount = 0;
            }
        }

        // ─────────────────────────── 적 ───────────────────────────

        static void StepEnemyMotion(ref EnemyState e, float dt)
        {
            if (!e.alive) return;
            e.vel.y += SimConfig.Gravity * dt;   // 경직 중에도 중력 (ADR-0005)
            e.pos += e.vel * dt;
            if (e.pos.y <= SimConfig.GroundY)
            {
                e.pos.y = SimConfig.GroundY;
                e.vel.y = 0f;
            }
        }

        static void ResolveEnemyHit(ref PlayerState p, in EnemyState e)
        {
            if (!p.alive) return;

            // 보정 중 짧은 무적 (관통 방지)
            if (p.iFrameTicks > 0) return;

            // 정면 방어 판정 (막기 홀드 또는 질풍참 돌진)
            if (p.IsGuardingFront && IsInFront(p, e.pos))
            {
                // 질풍참 돌진 방어는 게이지 소모 없음. 막기 홀드는 소모.
                if (p.dashTicksRemaining == 0)
                    p.guardGauge = Mathf.Max(0, p.guardGauge - SimConfig.GuardHitCostTicks);
                return;   // 막음 → 피해 없음
            }

            p.hp -= 1;
            if (p.hp <= 0) { p.hp = 0; p.alive = false; }
        }

        static bool IsInFront(in PlayerState p, Vector3 target)
        {
            Vector3 fwd = Forward(p.yaw);
            Vector3 to = target - p.pos; to.y = 0f;
            if (to.sqrMagnitude < 1e-6f) return true;
            to.Normalize();
            float cosHalf = Mathf.Cos(SimConfig.BlockFrontHalfAngle * Mathf.Deg2Rad);
            return Vector3.Dot(fwd, to) >= cosHalf;
        }

        static Vector3 Forward(float yaw)
            => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    }
}
