using UnityEngine;

namespace Game.Sim
{
    /// <summary>몹 종류.</summary>
    public enum Archetype : byte { MeleeGrunt = 0, RangedSoldier = 1, LargeMelee = 2 }

    /// <summary>
    /// 몹 AI 상태. 근접: Chase→Windup→Active→Recovery. 원거리(다음 단계): Reposition→Aim→Fire.
    /// stunTicks/descentPhase와 직교 — 그쪽이 우선순위 위(경직/하강 중엔 AI 정지).
    /// </summary>
    public enum EnemyState : byte
    {
        Chase = 0, Windup = 1, Active = 2, Recovery = 3,   // 근접
        Reposition = 4, Aim = 5, Fire = 6,                 // 원거리
    }

    /// <summary>몹 AI 상태 묶음. ★ AI 세션 소유. EnemySim이 품기만 한다(필드 늘려도 공유파일 안 건드림).</summary>
    public struct EnemyAI
    {
        public Archetype  archetype;
        public EnemyState state;
        public int        stateTicks;
        public Vector3    committedDir;   // 공격 시작 시 고정 → 예지 회피 성립
        public int        attackCooldown; // 원거리 재발사 쿨(다음 단계)
        public bool       hitDone;        // 이번 공격 판정 1회

        public static EnemyAI Spawn(Archetype a) => new EnemyAI
        {
            archetype = a,
            state = a == Archetype.MeleeGrunt ? EnemyState.Chase : EnemyState.Reposition,
        };
    }

    /// <summary>
    /// 몹 AI 상태머신. ★ AI 세션 소유. SimStep이 적마다 호출(EnemyMovement 대신 이게 진입점).
    /// 공격 시퀀스는 committed(방향 고정 + 제자리). 이동(추격/하강)은 기존 EnemyMovement 재사용.
    /// 적→플레이어 데미지는 직접 안 씀 → world 히트 큐에 넣고 CombatResolve가 방어판정 후 적용.
    /// </summary>
    public static class EnemyBrain
    {
        public static void Step(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            if (!e.alive) return;
            if (e.combat.gloryStage > 0) return;   // 글로리킬 처형 중 — AI·이동 정지(얼림)

            if (e.ai.archetype == Archetype.RangedSoldier) StepRanged(ref w, i, in svc, dt);
            else                                           StepMelee(ref w, i, in svc, dt);
        }

        static void StepMelee(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            // 공격 시퀀스 진행 중
            if (ai.state != EnemyState.Chase)
            {
                if (e.combat.stunTicks > 0)     // 피격 = 카운터: 공격 취소
                { ai.state = EnemyState.Chase; ai.stateTicks = 0; }
                else { AdvanceMelee(ref w, i, in svc, dt); return; }
            }

            // 개시 판단 (경직/하강 아니고, 사거리 + 시야)
            if (e.combat.stunTicks == 0 && e.descentPhase == DescentPhase.None)
            {
                Vector3 to = w.player.pos - e.pos; to.y = 0f;
                float hd = to.magnitude;
                if (hd <= AIConfig.MeleeRangeFor(e.radius) && HasLOS(in e, in w.player, in svc))
                {
                    ai.state = EnemyState.Windup;
                    ai.stateTicks = 0;
                    ai.hitDone = false;
                    ai.committedDir = hd > 1e-4f ? to / hd : Forward(e.yaw);
                    Plant(ref e, in svc, dt);
                    return;
                }
            }

            // 접근/하강/경직/idle → 기존 이동
            EnemyMovement.Step(ref e, in w.player, in svc, dt);
        }

        static void AdvanceMelee(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;
            ai.stateTicks++;

            float yaw = Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;
            e.yaw = yaw;   // committed 방향 고정 응시

            switch (ai.state)
            {
                case EnemyState.Windup:
                    if (ai.stateTicks >= AIConfig.MeleeWindupTicks)
                    { ai.state = EnemyState.Active; ai.stateTicks = 0; }
                    break;
                case EnemyState.Active:
                    if (!ai.hitDone)
                    {
                        ai.hitDone = true;
                        if (CombatHit.InCone(e.pos, yaw, w.player.pos,
                                             AIConfig.MeleeRangeFor(e.radius) + AIConfig.MeleeHitExtra,
                                             AIConfig.MeleeHitHalfAngle))
                            w.QueuePlayerHit(ai.committedDir, AIConfig.MeleeDamage);
                    }
                    if (ai.stateTicks >= AIConfig.MeleeActiveTicks)
                    { ai.state = EnemyState.Recovery; ai.stateTicks = 0; }
                    break;
                case EnemyState.Recovery:
                    if (ai.stateTicks >= AIConfig.MeleeRecoveryTicks)
                    { ai.state = EnemyState.Chase; ai.stateTicks = 0; }
                    break;
            }
            Plant(ref e, in svc, dt);
        }

        // ───────────────────────── 원거리 솔저 ─────────────────────────

        static void StepRanged(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            if (ai.attackCooldown > 0) ai.attackCooldown--;

            // 피격 = 카운터: 조준/발사 취소하고 정지
            if (e.combat.stunTicks > 0)
            {
                if (ai.state == EnemyState.Aim || ai.state == EnemyState.Fire)
                { ai.state = EnemyState.Reposition; ai.stateTicks = 0; }
                Plant(ref e, in svc, dt);
                return;
            }

            // 조준/발사 진행 중
            if (ai.state == EnemyState.Aim || ai.state == EnemyState.Fire)
            { AdvanceRanged(ref w, i, in svc, dt); return; }

            // ── Reposition: 선호 밴드 유지 + 시야 확보, 되면 조준 개시 ──
            Vector3 toP = w.player.pos - e.pos; toP.y = 0f;
            float hd = toP.magnitude;

            if (hd > SimConfig.EnemyAggroRange) { Plant(ref e, in svc, dt); return; }  // 인지 밖 대기

            Vector3 face = hd > 1e-4f ? toP / hd : Forward(e.yaw);
            e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;   // 항상 플레이어 응시(텔레그래프)
            bool los = HasLOS(in e, in w.player, in svc);

            if (hd < AIConfig.RangedBandMin)                RangedMove(ref e, -face, in svc, dt);  // 후퇴
            else if (hd > AIConfig.RangedBandMax || !los)   RangedMove(ref e,  face, in svc, dt);  // 접근/시야확보
            else if (ai.attackCooldown == 0)   // 밴드 안 + 시야 + 쿨 준비 → 조준 개시
            {
                ai.state = EnemyState.Aim;
                ai.stateTicks = 0;
                ai.hitDone = false;
                Vector3 pVel = (w.player.pos - w.prevPlayerPos) / dt; pVel.y = 0f;   // 이번 틱 수평 속도
                ai.committedDir = AimDir(in e, in w.player, pVel);   // 리드+빗맞힘 포함, 여기서 확정
                Plant(ref e, in svc, dt);
            }
            else Plant(ref e, in svc, dt);   // 쿨 대기(정지, 응시)
        }

        static void AdvanceRanged(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;
            ai.stateTicks++;

            e.yaw = Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;

            switch (ai.state)
            {
                case EnemyState.Aim:
                    if (ai.stateTicks >= AIConfig.RangedAimTicks)
                    { ai.state = EnemyState.Fire; ai.stateTicks = 0; }
                    break;
                case EnemyState.Fire:
                    Vector3 origin = e.pos + Vector3.up * AIConfig.EnemyEyeHeight;
                    w.SpawnProjectile(origin, ai.committedDir * AIConfig.ProjectileSpeed);
                    ai.attackCooldown = AIConfig.RangedCooldown;
                    ai.state = EnemyState.Reposition;
                    ai.stateTicks = 0;
                    break;
            }
            Plant(ref e, in svc, dt);
        }

        /// <summary>
        /// 발사 방향(3D). 대시 중이면 일부러 빗나가게(id 결정론), 아니면 플레이어 속도로 "약간" 리드.
        /// 리드는 투사체 도달시간 × LeadFactor 만큼만 앞을 겨냥 → 완벽 아님(저글 회피 가능).
        /// </summary>
        static Vector3 AimDir(in EnemySim e, in PlayerSim p, Vector3 pVel)
        {
            Vector3 origin = e.pos + Vector3.up * AIConfig.EnemyEyeHeight;
            Vector3 target = p.pos + Vector3.up * AIConfig.PlayerTorso;

            if (p.dashTicks > 0)   // 대시로 빠르게 이동 중 → 리드 안 하고 일부러 빗나감(안전 경로)
            {
                Vector3 miss = target - origin;
                float sign = (e.id % 2 == 0) ? 1f : -1f;
                miss = Quaternion.AngleAxis(AIConfig.MissOffsetDeg * sign, Vector3.up) * miss;
                return miss.sqrMagnitude > 1e-6f ? miss.normalized : Forward(e.yaw);
            }

            // 부분 리드: 현재 거리로 도달시간 추정 → 그만큼 앞을 겨냥(계수로 약화)
            float dist = (target - origin).magnitude;
            float travelTime = dist / AIConfig.ProjectileSpeed;
            Vector3 predicted = target + pVel * (travelTime * AIConfig.LeadFactor);
            Vector3 aim = predicted - origin;
            return aim.sqrMagnitude > 1e-6f ? aim.normalized : Forward(e.yaw);
        }

        /// <summary>원거리 재배치 이동(수평). 응시(yaw)는 호출부가 정한다.</summary>
        static void RangedMove(ref EnemySim e, Vector3 dir, in SimServices svc, float dt)
        {
            dir.y = 0f;
            Vector3 horiz = dir.sqrMagnitude > 1e-6f ? dir.normalized * AIConfig.RangedMoveSpeed * dt : Vector3.zero;
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool g);
            e.grounded = g;
        }

        /// <summary>제자리 정지(수평 0) + 중력·지면. 공격 중 committed 유지용.</summary>
        static void Plant(ref EnemySim e, in SimServices svc, float dt)
        {
            e.vel.x = 0f; e.vel.z = 0f;
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, Vector3.zero, e.radius, e.height);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool g);
            e.grounded = g;
        }

        /// <summary>적 눈 → 플레이어 몸통 레이가 지형에 안 막히면 시야 있음.</summary>
        static bool HasLOS(in EnemySim e, in PlayerSim p, in SimServices svc)
        {
            Vector3 eye = e.pos + Vector3.up * AIConfig.EnemyEyeHeight;
            Vector3 tgt = p.pos + Vector3.up * AIConfig.PlayerTorso;
            Vector3 d = tgt - eye; float dist = d.magnitude;
            if (dist < 1e-4f) return true;
            return !svc.Collision.Raycast(eye, d / dist, dist).hit;
        }

        static Vector3 Forward(float yaw)
            => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    }
}
