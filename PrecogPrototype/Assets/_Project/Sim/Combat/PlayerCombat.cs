using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 플레이어 전투 상태머신. ★ combat 소유. SimStep이 PlayerMovement 다음에 호출.
    /// 평타(좌클릭) · 막기(우클릭 홀드) · 칼등치기(막기중 좌클릭, 글로리킬식 러쉬).
    /// 판정/대미지는 CombatResolve. 여기선 단계 진행 + 러쉬 이동(pos 덮어씀) + 가드 게이지.
    /// </summary>
    public static class PlayerCombat
    {
        public static void Step(ref SimWorld w, in InputCmd cmd, in SimServices svc, float dt)
        {
            ref PlayerSim p = ref w.player;
            ref PlayerCombatState c = ref p.combat;

            // 글로리킬 처형 중이면 그것만 (최우선 — 무적·조작잠금은 SimStep/CombatResolve가 처리)
            if (c.gloryPhase != CombatConfig.GlNone) { StepGlory(ref w, in svc, dt); return; }

            if (c.frontGuardTicks > 0) c.frontGuardTicks--;

            // ── 칼등치기 진행 중이면 그것만 (러쉬 이동 포함) ──
            if (c.backstrikePhase != CombatConfig.BsNone)
            {
                StepBackstrike(ref p, in svc, dt);
                c.blocking = false;   // 러쉬 중엔 막기 표시 끔
                return;
            }

            // ── 막기 상태 결정 ──
            bool canBlock = c.guardGauge > 0
                            && c.attackPhase != CombatConfig.PhWindup
                            && c.attackPhase != CombatConfig.PhActive;
            c.blocking = cmd.block && canBlock;

            // 가드 게이지 회복 (막지 않을 때, 지연 후, Interval 틱마다 1씩 = 매우 느림)
            if (c.blocking) c.guardIdleTicks = 0;
            else
            {
                c.guardIdleTicks++;
                int since = c.guardIdleTicks - CombatConfig.GuardRegenDelay;
                if (since > 0 && since % CombatConfig.GuardRegenInterval == 0
                    && c.guardGauge < SimConfig.GuardMax)
                    c.guardGauge++;
            }

            // ── 칼등치기 시작: 막기 중 좌클릭 + 게이지 + 조준선 근처 대상 ──
            if (c.blocking && cmd.attack && c.guardGauge >= CombatConfig.BackstrikeGuardCost)
            {
                if (TryPickTarget(in w, in p, out Vector3 tpos))
                {
                    c.guardGauge -= CombatConfig.BackstrikeGuardCost;
                    c.backstrikePhase = CombatConfig.BsLunge;
                    c.backstrikeTicks = 0;
                    c.backstrikeTarget = tpos;
                    c.hasBackstrikeTarget = true;
                    c.backstrikeHitDone = false;
                    c.frontGuardTicks = CombatConfig.BackstrikeLungeTicks + CombatConfig.BackstrikeRecoveryTicks;
                    // 평타 중이었으면 캔슬
                    c.attackPhase = CombatConfig.PhNone;
                    c.attackPhaseTicks = 0;
                    return;
                }
                // 대상 없으면 발동 안 함(게이지 보존)
            }

            // ── 평타 진행/시작 (막기 아닐 때만) ──
            if (c.attackPhase == CombatConfig.PhNone)
            {
                if (cmd.attack && !c.blocking)
                {
                    c.attackPhase = CombatConfig.PhWindup;
                    c.attackPhaseTicks = 0;
                    c.attackHitDone = false;
                }
                return;
            }

            // 콤보 캔슬: 후딜 중 질풍참 입력 시 평타 즉시 종료(대시는 PlayerMovement가 이미 처리)
            if (c.attackPhase == CombatConfig.PhRecovery && p.dashTicks > 0)
            {
                c.attackPhase = CombatConfig.PhNone;
                c.attackPhaseTicks = 0;
                return;
            }

            c.attackPhaseTicks++;
            switch (c.attackPhase)
            {
                case CombatConfig.PhWindup:
                    if (c.attackPhaseTicks >= CombatConfig.AttackWindupTicks)
                    { c.attackPhase = CombatConfig.PhActive; c.attackPhaseTicks = 0; }
                    break;
                case CombatConfig.PhActive:
                    if (c.attackPhaseTicks >= CombatConfig.AttackActiveTicks)
                    { c.attackPhase = CombatConfig.PhRecovery; c.attackPhaseTicks = 0; }
                    break;
                case CombatConfig.PhRecovery:
                    if (c.attackPhaseTicks >= CombatConfig.AttackRecoveryTicks)
                    { c.attackPhase = CombatConfig.PhNone; c.attackPhaseTicks = 0; }
                    break;
            }
        }

        /// <summary>칼등치기 러쉬/자기경직 진행. Lunge 동안 대상 코앞까지 pos 덮어씀(이동보정).</summary>
        static void StepBackstrike(ref PlayerSim p, in SimServices svc, float dt)
        {
            ref PlayerCombatState c = ref p.combat;
            c.backstrikeTicks++;

            if (c.backstrikePhase == CombatConfig.BsLunge)
            {
                Vector3 to = c.backstrikeTarget - p.pos; to.y = 0f;
                float d = to.magnitude;
                if (d > CombatConfig.BackstrikeLungeGap)
                {
                    // 남은 거리를 남은 틱으로 분배 → 러쉬 끝엔 반드시 코앞 도착(대상이 멀어도)
                    int remain = Mathf.Max(1, CombatConfig.BackstrikeLungeTicks - c.backstrikeTicks + 1);
                    Vector3 dir = to / d;
                    float step = (d - CombatConfig.BackstrikeLungeGap) / remain;
                    p.pos = CharacterMotor.MoveHorizontal(svc.Collision, p.pos, dir * step,
                                                          SimConfig.PlayerRadius, SimConfig.PlayerHeight);
                }
                if (c.backstrikeTicks >= CombatConfig.BackstrikeLungeTicks)
                { c.backstrikePhase = CombatConfig.BsRecovery; c.backstrikeTicks = 0; }
                // 임팩트(대미지+넉백)는 CombatResolve가 Recovery 진입 시 1회 처리
            }
            else // BsRecovery
            {
                if (c.backstrikeTicks >= CombatConfig.BackstrikeRecoveryTicks)
                {
                    c.backstrikePhase = CombatConfig.BsNone;
                    c.backstrikeTicks = 0;
                    c.hasBackstrikeTarget = false;
                }
            }
        }

        /// <summary>
        /// 대형몹 글로리킬 처형 컷신. Slash1→Slash2→Dash. 대상은 gloryStage로 얼려둠.
        /// slash 단계는 제자리 응시, dash 단계는 고정 방향 관통 러쉬(무료 질풍참). 끝에 실제 사망.
        /// 절단 3단계 연출은 뷰(Dismemberment)가 gloryStage 읽어 구동.
        /// </summary>
        static void StepGlory(ref SimWorld w, in SimServices svc, float dt)
        {
            ref PlayerSim p = ref w.player;
            ref PlayerCombatState c = ref p.combat;
            c.gloryTicks++;

            ref EnemySim target = ref w.enemies[c.gloryTargetId];

            switch (c.gloryPhase)
            {
                case CombatConfig.GlSlash1:
                    if (c.gloryTicks >= CombatConfig.GlorySlashTicks)
                    { c.gloryPhase = CombatConfig.GlSlash2; c.gloryTicks = 0; target.combat.gloryStage = 2; }
                    break;
                case CombatConfig.GlSlash2:
                    if (c.gloryTicks >= CombatConfig.GlorySlashTicks)
                    {
                        c.gloryPhase = CombatConfig.GlDash; c.gloryTicks = 0;
                        target.combat.gloryStage = 3;   // 폭발 단계
                        Vector3 gd = target.pos - p.pos; gd.y = 0f;
                        c.gloryDir = gd.sqrMagnitude > 1e-4f
                            ? gd.normalized
                            : new Vector3(Mathf.Sin(p.yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(p.yaw * Mathf.Deg2Rad));
                    }
                    break;
                case CombatConfig.GlDash:
                    p.pos = CharacterMotor.MoveHorizontal(svc.Collision, p.pos,
                                                          c.gloryDir * CombatConfig.GloryDashSpeed * dt,
                                                          SimConfig.PlayerRadius, SimConfig.PlayerHeight);
                    if (c.gloryTicks >= CombatConfig.GloryDashTicks)
                    {
                        c.gloryPhase = CombatConfig.GlNone; c.gloryTicks = 0;
                        target.alive = false;             // 실제 사망(뷰 폭발은 gloryStage=3로 이미 처리)
                        target.combat.deathTick = w.tick;
                    }
                    break;
            }

            // 슬래시 단계엔 대상 응시(dash는 이동 중이라 스킵)
            if (c.gloryPhase == CombatConfig.GlSlash1 || c.gloryPhase == CombatConfig.GlSlash2)
            {
                Vector3 face = target.pos - p.pos; face.y = 0f;
                if (face.sqrMagnitude > 1e-4f) p.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            }
        }

        /// <summary>조준선 근처(부채꼴) 중 가장 가까운 살아있는 적. 둠과 달리 전방위 아님.</summary>
        static bool TryPickTarget(in SimWorld w, in PlayerSim p, out Vector3 pos)
        {
            pos = default;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (!e.alive) continue;
                if (!CombatHit.InCone(p.pos, p.yaw, e.pos,
                                      CombatConfig.BackstrikeAimRange + e.radius, CombatConfig.BackstrikeAimHalfAngle))
                    continue;
                Vector3 to = e.pos - p.pos; to.y = 0f;
                float sq = to.sqrMagnitude;
                if (sq < best) { best = sq; pos = e.pos; found = true; }
            }
            return found;
        }
    }
}
