using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 몹 정체성 = 3축 조합. 전투(공격 방식) × 기동(이동 방식) × 크기(스탯 변형).
    /// 개별 종 나열이 아니라 조합 → 돌진·비행은 mobility 값 추가, 대형은 size 플래그만.
    /// </summary>
    public enum CombatType   : byte { Melee = 0, Ranged = 1 }
    public enum MobilityType : byte { Ground = 0, Charge = 1, Traversal = 2, Flying = 3 }
    public enum SizeClass    : byte { Normal = 0, Large = 1 }

    /// <summary>
    /// 몹 AI 상태. 근접: Chase→Windup→Active→Recovery. 원거리(다음 단계): Reposition→Aim→Fire.
    /// stunTicks/descentPhase와 직교 — 그쪽이 우선순위 위(경직/하강 중엔 AI 정지).
    /// </summary>
    public enum EnemyState : byte
    {
        Chase = 0, Windup = 1, Active = 2, Recovery = 3,   // 근접 (돌진도 Windup·Recovery 재사용)
        Reposition = 4, Aim = 5, Fire = 6,                 // 원거리
        ChargeRun = 7,                                     // 돌진 직진
    }

    /// <summary>몹 AI 상태 묶음. ★ AI 세션 소유. EnemySim이 품기만 한다(필드 늘려도 공유파일 안 건드림).</summary>
    public struct EnemyAI
    {
        public CombatType   combat;      // 근접/원거리 — 공격 방식
        public MobilityType mobility;    // 지상/돌진/층이동/비행 — 이동 방식
        public SizeClass    size;        // 잡/대형 — 스탯 변형
        public EnemyState state;
        public int        stateTicks;
        public Vector3    committedDir;   // 공격 시작 시 고정 → 예지 회피 성립
        public int        attackCooldown; // 원거리 재발사 쿨
        public bool       hitDone;        // 이번 공격 판정 1회

        public static EnemyAI Spawn(CombatType combat, MobilityType mobility, SizeClass size) => new EnemyAI
        {
            combat = combat,
            mobility = mobility,
            size = size,
            state = combat == CombatType.Melee ? EnemyState.Chase : EnemyState.Reposition,
        };
    }

    /// <summary>
    /// 몹 AI 상태머신. ★ AI 세션 소유. SimStep이 적마다 호출(EnemyMovement 대신 이게 진입점).
    /// 공격 시퀀스는 committed(방향 고정 + 제자리). 이동(추격/하강)은 기존 EnemyMovement 재사용.
    /// 적→플레이어 데미지는 직접 안 씀 → world 히트 큐에 넣고 CombatResolve가 방어판정 후 적용.
    /// </summary>
    public static class EnemyBrain
    {
        // 분리 스티어링 스크래치: 매 틱 SimStep이 ComputeSeparation으로 채우고, 이동부가 추격방향에 가중.
        static readonly Vector3[] sepScratch = new Vector3[SimConfig.MaxEnemies];

        /// <summary>
        /// 각 몹의 "이웃 회피 방향"을 O(N²)로 1회 계산(boids 분리). 매 틱 SimStep이 적 루프 전에 호출.
        /// 겹치기 전에 미리 벌어지게 함(사후 밀기 Separate는 안전망으로 유지). 결정론(난수 없음).
        /// </summary>
        public static void ComputeSeparation(in SimWorld w)
        {
            for (int i = 0; i < w.enemyCount; i++) sepScratch[i] = Vector3.zero;
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive) continue;
                for (int j = i + 1; j < w.enemyCount; j++)
                {
                    if (!w.enemies[j].alive) continue;
                    Vector3 d = w.enemies[i].pos - w.enemies[j].pos; d.y = 0f;
                    float range = w.enemies[i].radius + w.enemies[j].radius + AIConfig.SeparationRadius;
                    float sq = d.sqrMagnitude;
                    if (sq >= range * range || sq < 1e-6f) continue;
                    float dist = Mathf.Sqrt(sq);
                    Vector3 push = d / (dist * dist);   // 멀어질 방향, 가까울수록 강함(1/dist)
                    sepScratch[i] += push;
                    sepScratch[j] -= push;
                }
            }
            for (int i = 0; i < w.enemyCount; i++)
                sepScratch[i] = Vector3.ClampMagnitude(sepScratch[i], AIConfig.SeparationMaxPush);
        }

        public static void Step(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            if (!e.alive) return;
            if (e.combat.gloryStage > 0) return;   // 글로리킬 처형 중 — AI·이동 정지(얼림)

            // 돌진몹(Charge)은 이동·공격 융합 자체 시퀀스 — mobility로 먼저 분기(바인드도 내부 처리)
            if (e.ai.mobility == MobilityType.Charge) { StepCharge(ref w, i, in svc, dt); return; }
            // 공중몹(Flying)도 자체 기동(비행 호버 + 벽 우회) + 기존 원거리 공격 재사용
            if (e.ai.mobility == MobilityType.Flying) { StepFlying(ref w, i, in svc, dt); return; }

            // 런지 표적 이동봉쇄(bind): 위치·중력 동결(공중이면 공중에 얼음).
            // 공격 시퀀스는 계속 진행한다(붙잡혀도 반격 가능) — Plant/RangedMove가 내부에서 위치만 스킵.
            if (e.combat.bindTicks > 0)
            {
                if (e.ai.combat == CombatType.Ranged)
                {
                    if (e.ai.state == EnemyState.Aim || e.ai.state == EnemyState.Fire)
                        AdvanceRanged(ref w, i, in svc, dt);
                }
                else if (e.ai.state != EnemyState.Chase)
                    AdvanceMelee(ref w, i, in svc, dt);
                return;
            }

            if (e.ai.combat == CombatType.Ranged) StepRanged(ref w, i, in svc, dt);
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
            if (e.combat.stunTicks == 0 && e.descentPhase == DescentPhase.None && e.traversalPhase == TraversalPhase.None)
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

            // 접근/하강/경직/idle → 기존 이동 (추격에 분리 스티어링 가중)
            EnemyMovement.Step(ref w, i, sepScratch[i], in svc, dt);
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

        // ───────────────────────── 돌진 (핑키) ─────────────────────────

        /// <summary>
        /// 돌진몹: Chase(추격+하강 재사용) → Windup(committed 텔레그래프) → ChargeRun(직진, 접촉/벽/최대거리 정지)
        /// → Recovery(성공 짧은딜/실패 긴딜). 완주 — 피격으로 안 끊긴다(회피는 옆으로). 반경 1.5배(Spawn에서).
        /// </summary>
        static void StepCharge(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            if (e.combat.bindTicks > 0) return;   // 런지 바인드: 그 자리 동결(공중이면 공중에)

            switch (ai.state)
            {
                case EnemyState.Windup:
                    e.yaw = Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;
                    ai.stateTicks++;
                    if (ai.stateTicks >= AIConfig.ChargeWindupTicks)
                    { ai.state = EnemyState.ChargeRun; ai.stateTicks = 0; ai.hitDone = false; }
                    Plant(ref e, in svc, dt);
                    break;

                case EnemyState.ChargeRun:
                {
                    ai.stateTicks++;
                    Vector3 before = e.pos;
                    float wish = AIConfig.ChargeSpeed * dt;
                    e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, ai.committedDir * wish, e.radius, e.height);
                    CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out e.grounded);
                    float moved = FlatDist(before, e.pos);

                    // 접촉 → 피해 1회 (히트 큐)
                    float contact = e.radius + SimConfig.PlayerRadius + 0.15f;
                    if (!ai.hitDone && FlatDist(e.pos, w.player.pos) <= contact)
                    { w.QueuePlayerHit(ai.committedDir, AIConfig.ChargeDamage); ai.hitDone = true; }

                    bool wall  = moved < wish * AIConfig.ChargeWallStopFrac;   // 벽에 막힘
                    bool maxed = ai.stateTicks * wish >= AIConfig.ChargeMaxDist;
                    if (ai.hitDone || wall || maxed)
                    { ai.state = EnemyState.Recovery; ai.stateTicks = 0; }
                    break;
                }

                case EnemyState.Recovery:
                    ai.stateTicks++;
                    Plant(ref e, in svc, dt);
                    if (ai.stateTicks >= (ai.hitDone ? AIConfig.ChargeHitRecovery : AIConfig.ChargeMissRecovery))
                    { ai.state = EnemyState.Chase; ai.stateTicks = 0; ai.hitDone = false; }
                    break;

                default:   // Chase(및 초기 상태) — 추격 + 개시 판단
                    if (e.descentPhase == DescentPhase.None)
                    {
                        Vector3 to = w.player.pos - e.pos; to.y = 0f;
                        float hd = to.magnitude;
                        if (hd <= AIConfig.ChargeMinRange && hd <= SimConfig.EnemyAggroRange
                            && HasLOS(in e, in w.player, in svc))
                        {
                            ai.committedDir = hd > 1e-4f ? to / hd : Forward(e.yaw);
                            e.yaw = Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;
                            ai.state = EnemyState.Windup;
                            ai.stateTicks = 0;
                            Plant(ref e, in svc, dt);
                            return;
                        }
                    }
                    EnemyMovement.Step(ref w, i, sepScratch[i], in svc, dt);   // 추격 + 하강 + 분리
                    break;
            }
        }

        static float FlatDist(Vector3 a, Vector3 b)
        { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        // ───────────────────────── 공중 원거리 (커코데몬) ─────────────────────────

        /// <summary>
        /// 공중몹: 낮게 부유(플레이어 y + 오프셋 수렴) + 밴드 유지. 몹끼리는 분리 스티어링,
        /// 벽은 MoveHorizontal 슬라이드. 공격(Aim→Fire·투사체·리드)은 지상 원거리와 공유. 조준 중 제자리 호버.
        /// </summary>
        static void StepFlying(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            if (ai.attackCooldown > 0) ai.attackCooldown--;
            if (e.combat.bindTicks > 0) return;   // 런지 바인드: 공중에 그대로 동결

            // 조준/발사 진행 중 → 제자리 호버 + 기존 원거리 시퀀스
            if (ai.state == EnemyState.Aim || ai.state == EnemyState.Fire)
            {
                e.yaw = Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;
                ai.stateTicks++;
                if (ai.state == EnemyState.Aim)
                {
                    if (ai.stateTicks >= AIConfig.RangedAimTicks)
                    { ai.state = EnemyState.Fire; ai.stateTicks = 0; }
                }
                else // Fire
                {
                    Vector3 origin = e.pos + Vector3.up * AIConfig.EnemyEyeHeight;
                    w.SpawnProjectile(origin, ai.committedDir * AIConfig.ProjectileSpeed);
                    ai.attackCooldown = AIConfig.RangedCooldown;
                    ai.state = EnemyState.Reposition;
                    ai.stateTicks = 0;
                }
                return;   // 호버(위치 유지)
            }

            // ── Reposition: 밴드 유지 비행 + 벽 우회 → 조준 개시 ──
            Vector3 toP = w.player.pos - e.pos; toP.y = 0f;
            float hd = toP.magnitude;
            if (hd > SimConfig.EnemyAggroRange) { FlyMove(ref e, Vector3.zero, e.pos.y, sepScratch[i], in svc, dt); return; }

            Vector3 face = hd > 1e-4f ? toP / hd : Forward(e.yaw);
            e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            bool los = HasLOS(in e, in w.player, in svc);

            Vector3 wish = Vector3.zero;
            if (hd < AIConfig.FlyBandMin)                wish = -face;   // 너무 가까움 → 후퇴
            else if (hd > AIConfig.FlyBandMax || !los)   wish =  face;   // 멀거나 시야 없음 → 접근

            FlyMove(ref e, wish, w.player.pos.y + AIConfig.FlyHoverOffset, sepScratch[i], in svc, dt);

            if (hd >= AIConfig.FlyBandMin && hd <= AIConfig.FlyBandMax && los && ai.attackCooldown == 0)
            {
                ai.state = EnemyState.Aim;
                ai.stateTicks = 0;
                Vector3 pVel = (w.player.pos - w.prevPlayerPos) / dt; pVel.y = 0f;
                ai.committedDir = AimDir(in e, in w.player, pVel);
            }
        }

        /// <summary>비행 이동: (수평 wish + 분리) 벽 슬라이드 + 목표 고도로 FlySpeed 수렴. 중력 없음.</summary>
        static void FlyMove(ref EnemySim e, Vector3 wishDir, float targetY, Vector3 sep, in SimServices svc, float dt)
        {
            Vector3 dir = wishDir + sep * AIConfig.SeparationWeight;   // 이동 의도 + 이웃 회피
            Vector3 horiz = dir.sqrMagnitude > 1e-6f ? dir.normalized * AIConfig.FlySpeed * dt : Vector3.zero;
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);

            float step = AIConfig.FlySpeed * dt;
            float newY = e.pos.y + Mathf.Clamp(targetY - e.pos.y, -step, step);
            if (svc.Collision.SampleGround(e.pos, 500f, out float gy))
                newY = Mathf.Max(newY, gy + AIConfig.FlyMinClearance);   // 지면 아래로 안 꺼짐
            e.pos.y = newY;
            e.vel = Vector3.zero;
            e.grounded = false;
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

            if (hd < AIConfig.RangedBandMin)                RangedMove(ref e, -face, sepScratch[i], in svc, dt);  // 후퇴
            else if (hd > AIConfig.RangedBandMax || !los)   RangedMove(ref e,  face, sepScratch[i], in svc, dt);  // 접근/시야확보
            else if (ai.attackCooldown == 0)   // 밴드 안 + 시야 + 쿨 준비 → 조준 개시
            {
                ai.state = EnemyState.Aim;
                ai.stateTicks = 0;
                ai.hitDone = false;
                Vector3 pVel = (w.player.pos - w.prevPlayerPos) / dt; pVel.y = 0f;   // 이번 틱 수평 속도
                ai.committedDir = AimDir(in e, in w.player, pVel);   // 리드+빗맞힘 포함, 여기서 확정
                Plant(ref e, in svc, dt);
            }
            else RangedMove(ref e, Vector3.zero, sepScratch[i], in svc, dt);   // 쿨 대기: 분리로 서로 벌어짐
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

        /// <summary>원거리 재배치 이동(수평 wish + 분리). 응시(yaw)는 호출부가 정한다. 바인드 중 동결.</summary>
        static void RangedMove(ref EnemySim e, Vector3 dir, Vector3 sep, in SimServices svc, float dt)
        {
            if (e.combat.bindTicks > 0) return;
            dir.y = 0f;
            Vector3 steer = dir + sep * AIConfig.SeparationWeight;
            Vector3 horiz = steer.sqrMagnitude > 1e-6f ? steer.normalized * AIConfig.RangedMoveSpeed * dt : Vector3.zero;
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool g);
            e.grounded = g;
        }

        /// <summary>제자리 정지(수평 0) + 중력·지면. 공격 중 committed 유지용. 바인드 중 위치 동결(공중 얼음).</summary>
        static void Plant(ref EnemySim e, in SimServices svc, float dt)
        {
            if (e.combat.bindTicks > 0) return;
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
