using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 플레이어 전투 상태머신. ★ combat 소유. SimStep이 PlayerMovement 다음에 호출.
    /// 평타(좌클릭) · 타깃 런지(우클릭) · 대형몹 글로리킬 컷신.
    /// 판정/대미지는 CombatResolve. 여기선 단계 진행 + 런지 이동(pos 구동) + 타깃 선정.
    /// </summary>
    public static class PlayerCombat
    {
        public static void Step(ref SimWorld w, in InputCmd cmd, in SimServices svc, float dt)
        {
            ref PlayerSim p = ref w.player;
            ref PlayerCombatState c = ref p.combat;

            // 글로리킬 처형 중이면 그것만 (최우선 — 무적·조작잠금은 SimStep/CombatResolve가 처리)
            if (c.gloryPhase != CombatConfig.GlNone) { StepGlory(ref w, in svc, dt); return; }

            if (c.hitStunTicks > 0) c.hitStunTicks--;
            if (c.lungeCooldown > 0) c.lungeCooldown--;

            // ── 런지 진행 중이면 그것만 (Travel 이동 포함) ──
            if (c.lungePhase != CombatConfig.LgNone) { StepLunge(ref p, in svc, dt); return; }

            // ── 런지 시작: 우클릭 + 쿨 0 + 유효 대상. 평타 중이어도 캔슬 발동(진입기) ──
            if (cmd.lunge && c.lungeCooldown == 0 && c.hitStunTicks == 0)
            {
                int targetId = cmd.lungeTargetId >= 0
                    ? cmd.lungeTargetId
                    : FindLungeTarget(in w, in p, in svc);
                if (targetId >= 0 && TryLockDestination(in w, in p, in svc, targetId, out Vector3 dest))
                {
                    c.lungePhase = CombatConfig.LgWindup;
                    c.lungeTicks = 0;
                    c.lungeTargetId = targetId;
                    c.lungeStart = p.pos;
                    c.lungeDest = dest;
                    c.lungeHitDone = false;
                    c.lungeCooldown = CombatConfig.LungeCooldownTicks;
                    // 평타 중이었으면 캔슬
                    c.attackPhase = CombatConfig.PhNone;
                    c.attackPhaseTicks = 0;
                    // 대상 응시
                    Vector3 face = dest - p.pos; face.y = 0f;
                    if (face.sqrMagnitude > 1e-4f) p.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
                    return;
                }
                // 대상 없으면 발동 안 함(쿨 보존)
            }

            // ── 평타 진행/시작 ──
            if (c.attackPhase == CombatConfig.PhNone)
            {
                if (cmd.attack && c.hitStunTicks == 0)
                {
                    c.attackPhase = CombatConfig.PhWindup;
                    c.attackPhaseTicks = 0;
                    c.attackHitDone = false;
                }
                return;
            }

            // 콤보 캔슬: 후딜 중 대시 시 평타 즉시 종료(대시는 PlayerMovement가 이미 처리)
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

        /// <summary>
        /// 런지 진행. Windup(대상 응시) → Travel(고정 도착점으로 보간, 벽 슬라이드) → Recovery.
        /// Travel 중 대상이 움직여도 재추적하지 않는다(도착점 고정). 임팩트는 CombatResolve가
        /// Recovery 진입 후 1회 처리(lungeHitDone).
        /// </summary>
        static void StepLunge(ref PlayerSim p, in SimServices svc, float dt)
        {
            ref PlayerCombatState c = ref p.combat;
            c.lungeTicks++;

            switch (c.lungePhase)
            {
                case CombatConfig.LgWindup:
                    if (c.lungeTicks >= CombatConfig.LungeWindupTicks)
                    { c.lungePhase = CombatConfig.LgTravel; c.lungeTicks = 0; }
                    break;

                case CombatConfig.LgTravel:
                {
                    // 남은 거리를 남은 틱으로 분배 → Travel 끝엔 반드시 도착점(벽이면 슬라이드로 최대한)
                    Vector3 to = c.lungeDest - p.pos; to.y = 0f;
                    int remain = Mathf.Max(1, CombatConfig.LungeTravelTicks - c.lungeTicks + 1);
                    Vector3 step = to / remain;
                    p.pos = CharacterMotor.MoveHorizontal(svc.Collision, p.pos, step,
                                                          SimConfig.PlayerRadius, SimConfig.PlayerHeight);
                    // 수직은 도착점 높이로 보간(같은 층 제약이라 미세 차이만)
                    p.pos.y = Mathf.Lerp(c.lungeStart.y, c.lungeDest.y,
                                         (float)c.lungeTicks / CombatConfig.LungeTravelTicks);
                    p.vel = Vector3.zero;
                    if (c.lungeTicks >= CombatConfig.LungeTravelTicks)
                    {
                        if (svc.Collision.SampleGround(p.pos, 2f, out float gy)) p.pos.y = gy;
                        c.lungePhase = CombatConfig.LgRecovery;
                        c.lungeTicks = 0;
                    }
                    break;
                }

                case CombatConfig.LgRecovery:
                    if (c.lungeTicks >= CombatConfig.LungeRecoveryTicks)
                    {
                        c.lungePhase = CombatConfig.LgNone;
                        c.lungeTicks = 0;
                        c.lungeTargetId = -1;
                    }
                    break;
            }
        }

        /// <summary>
        /// 대형몹 글로리킬 처형 컷신. Slash1→Slash2→Dash. 대상은 gloryStage로 얼려둠.
        /// slash 단계는 제자리 응시, dash 단계는 고정 방향 관통 러쉬. 끝에 실제 사망.
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

        /// <summary>
        /// 런지 자동 타깃: 정면 반각 30° + 거리 1.2~8 + 높이차 0.8 + LOS.
        /// 우선순위 = 화면 중앙 각도(dot) → 거리 → id (전부 결정론적 동률 해소).
        /// 예측(ActionGenerator)도 이 함수를 그대로 재사용한다 — 중복 구현 금지.
        /// </summary>
        public static int FindLungeTarget(in SimWorld w, in PlayerSim p, in SimServices svc)
        {
            int bestId = -1;
            float bestDot = -2f;
            float bestSq = float.MaxValue;
            Vector3 fwd = new Vector3(Mathf.Sin(p.yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(p.yaw * Mathf.Deg2Rad));

            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (!IsLungeable(in p, in e, in svc)) continue;

                Vector3 to = e.pos - p.pos; to.y = 0f;
                float sq = to.sqrMagnitude;
                float dot = sq > 1e-6f ? Vector3.Dot(fwd, to / Mathf.Sqrt(sq)) : 1f;

                bool better = dot > bestDot + 1e-6f
                    || (Mathf.Abs(dot - bestDot) <= 1e-6f
                        && (sq < bestSq - 1e-5f
                            || (Mathf.Abs(sq - bestSq) <= 1e-5f && e.id < bestId)));
                if (!better) continue;
                bestId = e.id; bestDot = dot; bestSq = sq;
            }
            return bestId;
        }

        /// <summary>런지 유효 대상인가: 생존 + 처형 중 아님 + 거리·정면각·높이차·시야.</summary>
        static bool IsLungeable(in PlayerSim p, in EnemySim e, in SimServices svc)
        {
            if (!e.alive || e.combat.gloryStage > 0) return false;
            if (Mathf.Abs(e.pos.y - p.pos.y) > CombatConfig.LungeHeightTolerance) return false;

            Vector3 to = e.pos - p.pos; to.y = 0f;
            float dist = to.magnitude;
            if (dist < CombatConfig.LungeMinRange || dist > CombatConfig.LungeMaxRange + e.radius) return false;

            if (!CombatHit.InCone(p.pos, p.yaw, e.pos,
                                  CombatConfig.LungeMaxRange + e.radius, CombatConfig.LungeHalfAngle))
                return false;

            // LOS: 플레이어 몸통 → 적 몸통
            Vector3 eye = p.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 tgt = e.pos + Vector3.up * (e.height * 0.6f);
            Vector3 d = tgt - eye; float len = d.magnitude;
            if (len > 1e-4f && svc.Collision.Raycast(eye, d / len, len).hit) return false;
            return true;
        }

        /// <summary>도착점 = 적 앞 LungeStopDistance 지점(지면 스냅). 시작 순간 1회 고정.</summary>
        static bool TryLockDestination(in SimWorld w, in PlayerSim p, in SimServices svc,
                                       int targetId, out Vector3 dest)
        {
            dest = p.pos;
            int idx = FindEnemyIndex(in w, targetId);
            if (idx < 0) return false;
            ref readonly EnemySim e = ref w.enemies[idx];

            Vector3 dir = e.pos - p.pos; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return false;
            dir.Normalize();
            dest = e.pos - dir * (CombatConfig.LungeStopDistance + e.radius);
            if (svc.Collision.SampleGround(dest + Vector3.up * 0.5f, 2f, out float gy)) dest.y = gy;
            else dest.y = e.pos.y;
            return true;
        }

        /// <summary>id로 살아있는 적 인덱스. 없으면 -1.</summary>
        public static int FindEnemyIndex(in SimWorld w, int id)
        {
            for (int i = 0; i < w.enemyCount; i++)
                if (w.enemies[i].id == id && w.enemies[i].alive) return i;
            return -1;
        }
    }
}
