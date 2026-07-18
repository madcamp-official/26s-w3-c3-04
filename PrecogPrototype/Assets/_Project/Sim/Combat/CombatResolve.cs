using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 대미지·스턴·HP·처치 정리. ★ combat 소유. SimStep이 적 이동 다음에 호출.
    /// 평타(부채꼴) · 질풍참(관통) · 칼등치기(단일 대상 + 넉백).
    /// 모든 공격 = 대미지 1 + 스턴 0.5초 통일.
    /// </summary>
    public static class CombatResolve
    {
        public static void Run(ref SimWorld w, in SimServices svc, float dt)
        {
            // 스턴 타이머 감소
            for (int i = 0; i < w.enemyCount; i++)
                if (w.enemies[i].combat.stunTicks > 0)
                    w.enemies[i].combat.stunTicks--;

            ref PlayerCombatState pc = ref w.player.combat;

            // ── 평타: Active 단계에서 1회, 부채꼴 안 적 전부 ──
            if (pc.attackPhase == CombatConfig.PhActive && !pc.attackHitDone)
            {
                pc.attackHitDone = true;
                for (int i = 0; i < w.enemyCount; i++)
                {
                    ref EnemySim e = ref w.enemies[i];
                    if (!e.alive || e.combat.gloryStage > 0) continue;   // 처형 중 적은 제외
                    if (!CombatHit.InCone(w.player.pos, w.player.yaw, e.pos,
                                          CombatConfig.AttackConeRange + e.radius, CombatConfig.AttackConeHalfAngle))
                        continue;   // + e.radius = 표면까지(대형몹 자동 반영)

                    // 대형몹 막타 → 즉사 대신 글로리킬 컷신 진입 (동시 1건만)
                    if (e.ai.archetype == Archetype.LargeMelee && pc.gloryPhase == CombatConfig.GlNone
                        && e.combat.health - CombatConfig.Damage <= 0)
                    {
                        pc.gloryPhase = CombatConfig.GlSlash1;
                        pc.gloryTicks = 0;
                        pc.gloryTargetId = i;
                        e.combat.gloryStage = 1;     // 처형 시작(view: slash1 절단), 얼림
                        e.combat.stunTicks = 0;
                        // 죽이지 않음: health 유지, gloryStage로 이후 판정·AI·분리 제외
                    }
                    else Damage(ref e, CombatConfig.Damage, w.tick);
                }
            }

            // ── 질풍참 관통: 돌진 중 1회, 경로 선분에 걸린 적 전부 ──
            if (w.player.dashTicks > 0)
            {
                if (!pc.dashPierceDone)
                {
                    pc.dashPierceDone = true;
                    Vector3 p0 = w.player.pos;
                    Vector3 p1 = p0 + w.player.dashDir * w.player.dashDist;   // 실제로 갈 남은 거리까지
                    for (int i = 0; i < w.enemyCount; i++)
                    {
                        ref EnemySim e = ref w.enemies[i];
                        if (!e.alive || e.combat.gloryStage > 0) continue;
                        if (!CombatHit.SegmentHitsSphere(p0, p1, e.pos, CombatConfig.DashPierceRadius + e.radius)) continue;
                        Damage(ref e, CombatConfig.Damage, w.tick);
                    }
                }
            }
            else pc.dashPierceDone = false;

            // ── 칼등치기 임팩트: 러쉬가 자기경직으로 넘어간 첫 틱 1회 ──
            // 대상(카메라 고정용)뿐 아니라 임팩트 지점 주변 적도 함께 타격 + 각자 약한 넉백.
            if (pc.backstrikePhase == CombatConfig.BsRecovery && !pc.backstrikeHitDone && pc.hasBackstrikeTarget)
            {
                pc.backstrikeHitDone = true;
                Vector3 center = pc.backstrikeTarget;   // 붙어서 친 지점
                for (int i = 0; i < w.enemyCount; i++)
                {
                    ref EnemySim e = ref w.enemies[i];
                    if (!e.alive || e.combat.gloryStage > 0) continue;
                    Vector3 to = e.pos - center; to.y = 0f;
                    float rr = CombatConfig.BackstrikeSplashRadius + e.radius;   // 표면까지(대형몹 반영)
                    if (to.sqrMagnitude > rr * rr) continue;
                    Vector3 dir = e.pos - w.player.pos; dir.y = 0f;   // 플레이어→적 = 밀치는 방향
                    Knockback.Apply(ref e, svc.Collision, dir, CombatConfig.KnockbackDist);
                    Damage(ref e, CombatConfig.Damage, w.tick);
                }
            }

            // ── 적 공격 적용: AI가 큐잉한 히트를 방어판정 후 플레이어 HP에 반영 ──
            //    글로리킬 처형 중엔 무적(큐만 비움).
            for (int k = 0; k < w.pendingHitCount && pc.gloryPhase == CombatConfig.GlNone; k++)
            {
                Vector3 dir = w.pendingHits[k].dir;   // 진행 방향(적→플레이어)
                int dmg = w.pendingHits[k].dmg;

                bool frontal = IsFrontal(w.player.yaw, dir);
                bool guarded = frontal && (pc.frontGuardTicks > 0 || (pc.blocking && pc.guardGauge > 0));
                if (guarded)
                {
                    // 질풍참/칼등치기 전면방어(frontGuardTicks)는 게이지 소모 없이 무효.
                    // 순수 막기는 게이지 소모.
                    if (pc.frontGuardTicks == 0)
                        pc.guardGauge = Mathf.Max(0, pc.guardGauge - CombatConfig.GuardHitCost);
                }
                else
                {
                    pc.hp -= dmg;
                    if (pc.hp < 0) pc.hp = 0;
                }
            }
            w.pendingHitCount = 0;
        }

        /// <summary>히트 진행 방향 dir이 플레이어 정면(시야각 안)에서 오는가.</summary>
        static bool IsFrontal(float yaw, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return false;
            dir.Normalize();
            Vector3 fwd = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
            // dir는 플레이어를 향해 진행 → 정면에서 오면 fwd와 거의 반대.
            return Vector3.Dot(fwd, dir) < -Mathf.Cos(CombatConfig.BlockHalfAngle * Mathf.Deg2Rad);
        }

        static void Damage(ref EnemySim e, int dmg, int tick)
        {
            e.combat.health -= dmg;
            e.combat.stunTicks = CombatConfig.StunTicks;   // 대미지 = 스턴 0.5초 동반
            if (e.combat.health <= 0)
            {
                e.alive = false;
                e.combat.deathTick = tick;
            }
        }
    }
}
