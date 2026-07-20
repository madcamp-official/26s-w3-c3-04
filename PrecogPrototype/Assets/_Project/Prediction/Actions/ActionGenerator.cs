using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 상황별 행동 후보 생성. docs/shared/PREDICTION_CONTRACT.md 10장 순서에서 Wait만
    /// 맨 뒤로 옮겼다 — Wait가 맨 앞이면 "아직 아무 일도 안 일어난" 동점 상황에서 항상
    /// Wait가 이겨서, 접근이 필요한 상황(적이 처음부터 사거리 밖)에서 Beam Search가 아예
    /// 안 다가가는 문제가 실제로 관찰됐다(평가 롤백만으로는 완전히 안 고쳐짐). 나머지
    /// 순서는 계약 그대로 — 이 순서 자체가 Beam Search 동률 해소 기준이 된다.
    /// 실전과 같은 공개 Sim 규칙(CombatMath, CombatConfig)만 재사용한다.
    /// LINQ/할당 없이 버퍼에 채운다.
    /// </summary>
    public static class ActionGenerator
    {
        static readonly MacroActionType[] Priority =
        {
            MacroActionType.MoveForward,
            MacroActionType.MoveLeft,
            MacroActionType.MoveRight,
            MacroActionType.Retreat,
            MacroActionType.Jump,
            MacroActionType.TerrainLeap,
            MacroActionType.DashForward,
            MacroActionType.DashBackward,
            MacroActionType.DashLeft,
            MacroActionType.DashRight,
            MacroActionType.JumpStrike,
            MacroActionType.AerialPursuit,
            MacroActionType.Attack,
            MacroActionType.Lunge,
            MacroActionType.Wait,
        };

        /// <summary>JumpStrike가 닿는 최대 높이차(플레이어 발밑 → 공중 적). 단일 점프가 매크로 안에서
        /// 오르는 정점(≈1.8m) + 좌클릭 높이차 허용(1m)보다 약간 보수적으로 잡아, 판정 시점에 확실히
        /// 사거리 안이게 한다. 표준 부유 고도(FlyHoverOffset=2m)를 넉넉히 포함.</summary>
        const float JumpStrikeReachHeight = 2.6f;

        /// <summary>buffer에 후보를 채우고 개수를 반환한다. buffer는 호출자가 재사용(풀링)한다.</summary>
        public static int Generate(in SimWorld world, in SimServices services, in PredictionSettings settings, MacroAction[] buffer)
        {
            int count = 0;
            int cap = Mathf.Min(settings.maxActionsPerNode, buffer.Length);
            ref readonly PlayerSim player = ref world.player;
            bool canStartAction = player.combat.hp > 0
                && player.combat.attackPhase == CombatConfig.PhNone
                && player.combat.lungePhase == CombatConfig.LgNone
                && player.combat.gloryPhase == CombatConfig.GlNone;

            for (int p = 0; p < Priority.Length && count < cap; p++)
            {
                switch (Priority[p])
                {
                    case MacroActionType.TerrainLeap:
                        if (TryBuildTerrainLeap(in world, in services, out MacroAction leap))
                            buffer[count++] = leap;
                        break;

                    case MacroActionType.Jump:
                        // 동일 jump 입력을 실제 Sim이 grounded/jumpCount로 1단·2단 점프로 구분한다.
                        // 대시 중 입력 버퍼로 뒤늦게 발동하는 후보는 행동 의미가 불명확하므로 제외한다.
                        if (player.dashTicks == 0
                            && player.combat.hitStunTicks == 0
                            && player.combat.lungePhase != CombatConfig.LgTravel
                            && player.jumpCount < 2)
                            buffer[count++] = MacroAction.Simple(MacroActionType.Jump);
                        break;

                    case MacroActionType.JumpStrike:
                        // 얼어붙은 공중 슈터를 런지 없이 점프+좌클릭으로. 지상에서 솟구쳐 만나므로
                        // 점프 가능(지상·미대시·미경직·점프잔량) + 좌클릭 개시 가능 + 사거리 대상 필요.
                        if (canStartAction && player.grounded && player.dashTicks == 0
                            && player.jumpCount < 2 && HasJumpStrikeTarget(in world))
                            buffer[count++] = MacroAction.JumpStrikeAction();
                        break;

                    case MacroActionType.AerialPursuit:
                        if (canStartAction && player.grounded && player.jumpCount == 0
                            && player.dashTicks == 0 && player.combat.lungeCooldown == 0
                            && TryFindAerialPursuitTarget(in world, in services, out int pursuitTarget))
                            buffer[count++] = MacroAction.AerialPursuitTo(pursuitTarget);
                        break;

                    case MacroActionType.Attack:
                        if (canStartAction && HasAttackTarget(in world))
                            buffer[count++] = MacroAction.Simple(MacroActionType.Attack);
                        break;

                    case MacroActionType.DashForward:
                    case MacroActionType.DashBackward:
                    case MacroActionType.DashLeft:
                    case MacroActionType.DashRight:
                        if (player.dashTicks == 0 && player.dashCharges > 0)
                            buffer[count++] = MacroAction.Simple(Priority[p]);
                        break;

                    case MacroActionType.Lunge:
                        if (canStartAction && player.combat.lungeCooldown == 0)
                            count = AddLungeCandidates(in world, in player, in services, buffer, count, cap);
                        break;

                    default:
                        buffer[count++] = MacroAction.Simple(Priority[p]);
                        break;
                }
            }
            return count;
        }

        static bool TryBuildTerrainLeap(in SimWorld world, in SimServices services, out MacroAction action)
        {
            action = default;
            ref readonly PlayerSim player = ref world.player;
            if (player.combat.hp <= 0 || player.jumpCount != 0 || !player.grounded
                || player.dashTicks != 0 || player.combat.hitStunTicks != 0)
                return false;

            Vector3 escape = ComputeEscapeDirection(in world);
            PathStep step = services.Pathfinder.NextStep(player.pos, player.pos + escape * 12f, -1);
            if (step.kind != MoveKind.JumpUp) return false;
            Vector3 delta = step.next - player.pos;
            if (delta.sqrMagnitude <= 1e-6f) return false;
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            action = MacroAction.TerrainLeapAt(yaw);
            return true;
        }

        static Vector3 ComputeEscapeDirection(in SimWorld world)
        {
            Vector3 pressure = Vector3.zero;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                Vector3 delta = enemy.pos - world.player.pos;
                delta.y = 0f;
                float distanceSq = delta.sqrMagnitude;
                if (distanceSq <= 1e-6f) continue;
                pressure += delta.normalized / Mathf.Max(1f, distanceSq);
            }

            if (pressure.sqrMagnitude > 1e-6f)
                return -pressure.normalized;
            return -CombatMath.Forward(world.player.yaw);
        }

        static bool TryFindAerialPursuitTarget(
            in SimWorld world, in SimServices services, out int targetId)
        {
            targetId = -1;
            float bestDistance = float.MaxValue;
            ref readonly PlayerSim player = ref world.player;
            Vector3 eye = player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0 || enemy.ai.mobility != MobilityType.Flying)
                    continue;

                float height = enemy.pos.y - player.pos.y;
                float flat = CombatMath.FlatDistance(player.pos, enemy.pos);
                if (height <= CombatConfig.AttackHeightTolerance
                    || height > CombatConfig.LungeHeightTolerance
                    || flat > CombatConfig.LungeMaxRange + enemy.radius)
                    continue;

                Vector3 center = enemy.pos + Vector3.up * (enemy.height * 0.5f);
                if (!services.Collision.HasLineOfSight(eye, center)) continue;
                if (flat < bestDistance - 1e-5f
                    || (Mathf.Abs(flat - bestDistance) <= 1e-5f && enemy.id < targetId))
                {
                    bestDistance = flat;
                    targetId = enemy.id;
                }
            }
            return targetId >= 0;
        }

        static bool HasAttackTarget(in SimWorld world)
        {
            Vector3 forward = CombatMath.Forward(world.player.yaw);
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;
                if (Mathf.Abs(enemy.pos.y - world.player.pos.y) > CombatConfig.AttackHeightTolerance) continue;
                if (CombatMath.InCone(world.player.pos, forward, enemy.pos,
                        CombatConfig.AttackConeRange + enemy.radius, CombatConfig.AttackConeHalfAngle))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// JumpStrike 대상: 조준/발사로 고도가 고정된(=점프로 따라잡을 수 있는) 공중 슈터가,
        /// 지상 평타 사거리보다 위(높이차 > AttackHeightTolerance)이면서 단일 점프 도달 높이 안에,
        /// 이미 좌클릭 부채꼴(수평) 안에 들어와 있는 경우. 점프는 수직이라 수평 접근은 못 한다 —
        /// 수평 진입은 일반 이동 후보가 담당하고, 이 후보는 "바로 위로 뛰어 치는" 순간만 만든다.
        /// </summary>
        static bool HasJumpStrikeTarget(in SimWorld world)
        {
            ref readonly PlayerSim player = ref world.player;
            Vector3 forward = CombatMath.Forward(player.yaw);
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive || enemy.combat.gloryStage > 0) continue;
                if (enemy.ai.mobility != MobilityType.Flying) continue;
                if (enemy.ai.state != EnemyState.Aim && enemy.ai.state != EnemyState.Fire) continue;
                float gap = enemy.pos.y - player.pos.y;
                if (gap <= CombatConfig.AttackHeightTolerance || gap > JumpStrikeReachHeight) continue;
                if (CombatMath.InCone(player.pos, forward, enemy.pos,
                        CombatConfig.AttackConeRange + enemy.radius, CombatConfig.AttackConeHalfAngle))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 계약(PREDICTION_CONTRACT.md 10장) "런지 후보 가까운 유효 대상 최대 2" 반영.
        /// 상위 2명을 각도→거리→id로 뽑은 뒤, 버퍼 삽입 순서는 계약이 명시한 대로
        /// targetId 오름차순으로 정렬한다(선정 우선순위와 삽입 순서는 별개).
        /// </summary>
        static int AddLungeCandidates(
            in SimWorld world, in PlayerSim player, in SimServices services,
            MacroAction[] buffer, int count, int cap)
        {
            FindTopLungeTargets(in world, in player, in services, out int first, out int second);
            if (first >= 0 && second >= 0 && second < first)
            {
                int swap = first; first = second; second = swap;
            }
            if (first >= 0 && count < cap) buffer[count++] = MakeLungeAction(in world, first);
            if (second >= 0 && count < cap) buffer[count++] = MakeLungeAction(in world, second);
            return count;
        }

        /// <summary>
        /// 공중(Flying) 대상이면 복합 콤보(LungeStrike: 우클릭 접근 → 착지 직후 좌클릭)를,
        /// 지상 대상이면 기존 단일 런지를 만든다. 공중 대상은 런지 임팩트 피해 1만으론 못 죽이고
        /// 착지 직후 낙하·bind 해제로 후속 좌클릭 창이 매크로 경계를 넘어가 닫히므로, 한 매크로
        /// 안에서 좌클릭까지 묶어야 실제 처치가 성립한다(원인 분석 B). 지상 대상은 착지 후 다음
        /// 매크로의 Attack 후보로 충분해 굳이 콤보로 묶지 않는다(후보 폭증 방지).
        /// </summary>
        static MacroAction MakeLungeAction(in SimWorld world, int targetId)
        {
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (enemy.id != targetId) continue;
                return enemy.ai.mobility == MobilityType.Flying
                    ? MacroAction.LungeStrikeTo(targetId)
                    : MacroAction.LungeTo(targetId);
            }
            return MacroAction.LungeTo(targetId);
        }

        static void FindTopLungeTargets(
            in SimWorld world, in PlayerSim player, in SimServices services,
            out int first, out int second)
        {
            first = -1; second = -1;
            float firstDot = 0f, firstDist = 0f;
            float secondDot = 0f, secondDist = 0f;
            Vector3 forward = CombatMath.Forward(player.yaw);

            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!CanLungeTarget(in world, in player, in enemy, in services)) continue;

                Vector3 direction = CombatMath.FlatDirection(player.pos, enemy.pos);
                float dot = Vector3.Dot(forward, direction);
                float distance = CombatMath.FlatDistance(player.pos, enemy.pos);

                if (IsBetterLungeTarget(dot, distance, enemy.id, first, firstDot, firstDist))
                {
                    second = first; secondDot = firstDot; secondDist = firstDist;
                    first = enemy.id; firstDot = dot; firstDist = distance;
                }
                else if (IsBetterLungeTarget(dot, distance, enemy.id, second, secondDot, secondDist))
                {
                    second = enemy.id; secondDot = dot; secondDist = distance;
                }
            }
        }

        static bool IsBetterLungeTarget(float dot, float distance, int id, int bestId, float bestDot, float bestDist)
        {
            if (bestId < 0) return true;
            if (dot > bestDot + 1e-6f) return true;
            if (Mathf.Abs(dot - bestDot) <= 1e-6f)
            {
                if (distance < bestDist - 1e-5f) return true;
                if (Mathf.Abs(distance - bestDist) <= 1e-5f && id < bestId) return true;
            }
            return false;
        }

        /// <summary>
        /// 런지 유효성 판정을 실제 Sim(PlayerCombat.CanLunge)에 그대로 위임한다 — 예측 전용
        /// 독립 재구현을 없애서 "예측 됨 → 실제 안 됨" 괴리 자체를 구조적으로 제거한다
        /// (docs/shared/AERIAL_LUNGE_SIM_API_PROPOSAL.md, KJH 구현 완료).
        ///
        /// CanLunge는 p.yaw/p.aimPitch(현재 조준) 기준으로 판정하므로, 이 후보를 정확히
        /// 바라보도록 조준만 맞춘 가상 PlayerSim(위치는 그대로)을 만들어 넣는다 — 그래야
        /// 실제 발동 경로(IsLungeable+TryLockDestination)와 완전히 같은 결과가 나온다.
        /// 실제 매크로 실행 시엔 MacroAction이 lungeTargetId를 명시 전달해 PlayerCombat.Step이
        /// FindLungeTarget(자동 조준 탐지)를 건너뛰므로, 사거리·높이차·LOS를 걸러내는 건
        /// 사실상 이 검색 단계가 유일한 관문이다 — 지금 정확히 그 검사를 real Sim 규칙으로
        /// 대체한 것.
        /// </summary>
        static bool CanLungeTarget(in SimWorld world, in PlayerSim player, in EnemySim enemy, in SimServices services)
        {
            if (!enemy.alive || enemy.combat.gloryStage > 0) return false;

            PlayerSim aimed = player;
            Vector3 eye = player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 center = enemy.pos + Vector3.up * (enemy.height * 0.5f);
            Vector3 dir = center - eye;
            if (dir.sqrMagnitude > 1e-8f)
            {
                // Quaternion.Euler(pitch, yaw, 0)*forward와 정확히 왕복하는 유일한 안전한 방법 —
                // 회전 합성 순서를 직접 손으로 유도하면 부호를 틀리기 쉬워, LookRotation을
                // eulerAngles로 분해해 그대로 되돌린다(엔진이 왕복을 보장).
                Vector3 lookEuler = Quaternion.LookRotation(dir).eulerAngles;
                aimed.yaw = lookEuler.y;
                aimed.aimPitch = lookEuler.x;
            }
            return PlayerCombat.CanLunge(in world, in aimed, in services, enemy.id, out _);
        }
    }
}
