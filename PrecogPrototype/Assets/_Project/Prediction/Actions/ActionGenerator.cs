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
            MacroActionType.DashForward,
            MacroActionType.DashBackward,
            MacroActionType.DashLeft,
            MacroActionType.DashRight,
            MacroActionType.Attack,
            MacroActionType.Lunge,
            MacroActionType.Wait,
        };

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
            if (first >= 0 && count < cap) buffer[count++] = MacroAction.LungeTo(first);
            if (second >= 0 && count < cap) buffer[count++] = MacroAction.LungeTo(second);
            return count;
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
                if (!CanTargetForLunge(in player, in enemy, in services, out _)) continue;

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
        /// PlayerCombat.CanTarget(Sim, private)과 동일한 규칙의 독립 구현.
        /// 알려진 한계: 런지 유효성 판정이 Sim과 Prediction 두 곳에 존재한다 — 실제 판정
        /// 규칙이 바뀌면 양쪽을 함께 고쳐야 한다. 근본 해결(Sim 쪽 공개 API 추가)은
        /// 게임 개발자 승인이 필요해 별도 요청 사항으로 남긴다.
        /// </summary>
        static bool CanTargetForLunge(in PlayerSim player, in EnemySim enemy, in SimServices services, out Vector3 destination)
        {
            destination = player.pos;
            if (!enemy.alive || enemy.combat.gloryStage > 0) return false;
            if (Mathf.Abs(enemy.pos.y - player.pos.y) > CombatConfig.LungeHeightTolerance) return false;

            float distance = CombatMath.FlatDistance(player.pos, enemy.pos);
            if (distance < CombatConfig.LungeMinRange || distance > CombatConfig.LungeMaxRange + enemy.radius) return false;

            Vector3 forward = CombatMath.Forward(player.yaw);
            if (!CombatMath.InCone(
                player.pos, forward, enemy.pos,
                CombatConfig.LungeMaxRange + enemy.radius, CombatConfig.LungeHalfAngle))
                return false;

            Vector3 eye = player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 target = enemy.pos + Vector3.up * (enemy.height * 0.6f);
            if (!services.Collision.HasLineOfSight(eye, target)) return false;

            Vector3 direction = CombatMath.FlatDirection(player.pos, enemy.pos);
            destination = enemy.pos - direction * (CombatConfig.LungeStopDistance + enemy.radius);
            if (!services.Collision.SampleGround(destination, 2f, out float groundY)) return false;
            destination.y = groundY;
            return services.Collision.CanOccupyCapsule(
                destination, SimConfig.PlayerRadius, SimConfig.PlayerHeight);
        }
    }
}
