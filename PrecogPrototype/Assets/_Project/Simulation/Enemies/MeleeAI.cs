using UnityEngine;

namespace Game.Simulation
{
    /// <summary>
    /// 일반 근접 적의 규칙 기반 AI. 난수 없음 (ADR-0004).
    /// 같은 상태 → 항상 같은 결정. 한 틱 분량의 행동을 적 하나에 적용한다.
    ///
    /// 상태 흐름: Idle → Chase → Windup → Active → Recovery → Chase
    /// 묶음 A에서는 길찾기 없이 플레이어를 향해 직선 접근한다.
    /// (수직 길찾기는 묶음 D)
    /// </summary>
    public static class MeleeAI
    {
        public static void Tick(ref EnemyState e, in PlayerState player, float dt)
        {
            if (!e.alive) return;

            // 경직(스턴) 중이면 AI 정지. 중력만 별도로 Kernel이 처리.
            if (e.stunTicks > 0)
            {
                e.stunTicks--;
                e.vel = Vector3.zero;
                return;
            }

            if (e.attackCooldownTicks > 0) e.attackCooldownTicks--;

            float flatSqr = Hit.FlatSqrDist(e.pos, player.pos);

            switch (e.aiState)
            {
                case AiState.Idle:
                    e.vel = Vector3.zero;
                    if (player.alive &&
                        flatSqr <= Sq(SimConfig.MeleeAggroRange))
                        Enter(ref e, AiState.Chase);
                    break;

                case AiState.Chase:
                    FaceAndMove(ref e, player.pos, SimConfig.MeleeSpeed, dt);
                    if (flatSqr <= Sq(SimConfig.MeleeAttackRange) &&
                        e.attackCooldownTicks == 0)
                    {
                        e.vel = Vector3.zero;
                        Enter(ref e, AiState.Windup);
                    }
                    else if (!player.alive)
                    {
                        Enter(ref e, AiState.Idle);
                    }
                    break;

                case AiState.Windup:
                    e.vel = Vector3.zero;
                    FaceTo(ref e, player.pos);           // 선딜 동안 조준만
                    e.stateTicks++;
                    if (e.stateTicks >= SimConfig.MeleeWindupTicks)
                        Enter(ref e, AiState.Active);
                    break;

                case AiState.Active:
                    // 판정은 Kernel의 전투 단계에서 처리 (여기선 상태만 진행)
                    e.stateTicks++;
                    if (e.stateTicks >= SimConfig.MeleeActiveTicks)
                    {
                        e.attackCooldownTicks = SimConfig.MeleeAttackCooldownTicks;
                        Enter(ref e, AiState.Recovery);
                    }
                    break;

                case AiState.Recovery:
                    e.vel = Vector3.zero;
                    e.stateTicks++;
                    if (e.stateTicks >= SimConfig.MeleeRecoveryTicks)
                        Enter(ref e, AiState.Chase);
                    break;
            }
        }

        /// <summary>Active 틱에 플레이어가 사거리 안이면 공격 성공. Kernel이 호출.</summary>
        public static bool AttackConnects(in EnemyState e, in PlayerState player)
        {
            if (e.aiState != AiState.Active) return false;
            // Active의 첫 틱에만 판정 (stateTicks==1 시점)
            if (e.stateTicks != 1) return false;
            return Hit.FlatSqrDist(e.pos, player.pos) <= Sq(SimConfig.MeleeAttackRange);
        }

        static void Enter(ref EnemyState e, AiState next)
        {
            e.aiState = next;
            e.stateTicks = 0;
        }

        static void FaceAndMove(ref EnemyState e, Vector3 target, float speed, float dt)
        {
            Vector3 to = target - e.pos; to.y = 0f;
            float d = to.magnitude;
            if (d > 1e-4f)
            {
                Vector3 dir = to / d;
                e.vel = dir * speed;
                e.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            }
            else e.vel = Vector3.zero;
        }

        static void FaceTo(ref EnemyState e, Vector3 target)
        {
            Vector3 to = target - e.pos; to.y = 0f;
            if (to.sqrMagnitude > 1e-6f)
                e.yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        }

        static float Sq(float x) => x * x;
    }
}
