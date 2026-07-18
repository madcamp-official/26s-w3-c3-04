using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 대미지·스턴·HP·처치 정리. ★ combat 소유. SimStep이 적 이동 다음에 호출.
    /// 평타(부채꼴) · 런지 임팩트(단일 대상) · 적→플레이어 히트 큐 적용.
    /// 공격 = 피해만(스턴 부여 폐기). 대형몹 막타는 글로리킬 컷신 진입.
    /// </summary>
    public static class CombatResolve
    {
        public static void Run(ref SimWorld w, in SimServices svc, float dt)
        {
            // 타이머 감소 (스턴은 잔존 필드 — 부여처 없음. 바인드 = 런지 표적 이동봉쇄)
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (w.enemies[i].combat.stunTicks > 0) w.enemies[i].combat.stunTicks--;
                if (w.enemies[i].combat.bindTicks > 0) w.enemies[i].combat.bindTicks--;
            }

            ref PlayerCombatState pc = ref w.player.combat;

            // ── 평타: Active 단계에서 1회, 부채꼴 안 적 전부 (높이차 허용 1m) ──
            if (pc.attackPhase == CombatConfig.PhActive && !pc.attackHitDone)
            {
                pc.attackHitDone = true;
                for (int i = 0; i < w.enemyCount; i++)
                {
                    ref EnemySim e = ref w.enemies[i];
                    if (!e.alive || e.combat.gloryStage > 0) continue;   // 처형 중 적은 제외
                    if (Mathf.Abs(e.pos.y - w.player.pos.y) > CombatConfig.AttackHeightTolerance) continue;
                    if (!CombatHit.InCone(w.player.pos, w.player.yaw, e.pos,
                                          CombatConfig.AttackConeRange + e.radius, CombatConfig.AttackConeHalfAngle))
                        continue;   // + e.radius = 표면까지(대형몹 자동 반영)
                    HitEnemy(ref w, i);
                }
            }

            // ── 런지 임팩트: Travel 완료(Recovery 진입) 후 1회, 고정 대상만 ──
            //    이동 중 대상이 죽었으면 도착만 하고 추가 피해 없음.
            if (pc.lungePhase == CombatConfig.LgRecovery && !pc.lungeHitDone)
            {
                pc.lungeHitDone = true;
                int idx = PlayerCombat.FindEnemyIndex(in w, pc.lungeTargetId);
                if (idx >= 0 && w.enemies[idx].combat.gloryStage == 0)
                    HitEnemy(ref w, idx);
            }

            // ── 적 공격 적용: AI가 큐잉한 히트를 플레이어 HP에 반영 + 피격 경직 ──
            //    글로리킬 처형 중엔 무적(큐만 비움). 막기는 폐기 — 방어판정 없음.
            for (int k = 0; k < w.pendingHitCount; k++)
            {
                if (pc.gloryPhase != CombatConfig.GlNone) break;
                pc.hp -= w.pendingHits[k].dmg;
                if (pc.hp < 0) pc.hp = 0;
                pc.hitStunTicks = CombatConfig.PlayerHitStunTicks;
            }
            w.pendingHitCount = 0;
        }

        /// <summary>
        /// 적 1타 처리. 대형몹 막타면 즉사 대신 글로리킬 컷신 진입(동시 1건).
        /// </summary>
        static void HitEnemy(ref SimWorld w, int i)
        {
            ref EnemySim e = ref w.enemies[i];
            ref PlayerCombatState pc = ref w.player.combat;

            if (e.ai.archetype == Archetype.LargeMelee && pc.gloryPhase == CombatConfig.GlNone
                && e.combat.health - CombatConfig.Damage <= 0)
            {
                pc.gloryPhase = CombatConfig.GlSlash1;
                pc.gloryTicks = 0;
                pc.gloryTargetId = i;
                e.combat.gloryStage = 1;     // 처형 시작(view: slash1 절단), 얼림
                e.combat.stunTicks = 0;
                // 죽이지 않음: health 유지, gloryStage로 이후 판정·AI·분리 제외
                return;
            }

            e.combat.health -= CombatConfig.Damage;   // 스턴 부여 폐기 — 피해만
            if (e.combat.health <= 0)
            {
                e.alive = false;
                e.combat.deathTick = w.tick;
            }
        }
    }
}
