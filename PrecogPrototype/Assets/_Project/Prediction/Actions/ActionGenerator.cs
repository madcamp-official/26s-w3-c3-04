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
            MacroActionType.DashForward,
            MacroActionType.DashBackward,
            MacroActionType.DashLeft,
            MacroActionType.DashRight,
            MacroActionType.JumpStrike,
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
        /// 규칙이 바뀌면 양쪽을 함께 고쳐야 한다. 근본 해결(Sim 쪽 공개 CanLunge API로 통합)은
        /// 게임 개발자 승인이 필요해 docs/shared/AERIAL_LUNGE_SIM_API_PROPOSAL.md로 남긴다.
        ///
        /// 예전엔 여기서 착지점을 "지면 스냅+캡슐 점유 가능"으로 추가 검증했는데, 실제 Sim
        /// (PlayerCombat.TryLockDestination/IsLungeable)엔 그런 조건이 아예 없다 — 항상
        /// "대상 고도(enemy.y+LungeAimUp)"에 무조건 붙는다. 그 결과 표준 hover 고도
        /// (AIConfig.FlyHoverOffset=2m)에 뜬 공중 적조차, 지면까지의 하향 레이가 딱 그
        /// 한계치(SampleGround의 maxDown=2f+원점오프셋0.5=2.5m ≈ 필요거리 2.5m)에 걸려
        /// 실전에서 거의 항상 실패해 런지 후보 자체가 생성되지 않는 버그였다(공중 유닛
        /// 타겟팅이 접근 시도조차 안 되는 원인). 실제 규칙에 없는 조건이라 삭제하고,
        /// 착지 높이도 real Sim과 동일하게 계산한다.
        /// </summary>
        static bool CanTargetForLunge(in PlayerSim player, in EnemySim enemy, in SimServices services, out Vector3 destination)
        {
            destination = player.pos;
            if (!enemy.alive || enemy.combat.gloryStage > 0) return false;
            if (Mathf.Abs(enemy.pos.y - player.pos.y) > CombatConfig.LungeHeightTolerance) return false;

            float distance = CombatMath.FlatDistance(player.pos, enemy.pos);
            if (distance < CombatConfig.LungeMinRange || distance > CombatConfig.LungeMaxRange + enemy.radius) return false;

            Vector3 forward = CombatMath.Forward(player.yaw);
            Vector3 delta = enemy.pos - player.pos; delta.y = 0f;
            float along = Vector3.Dot(delta, forward);
            Vector3 lateral = delta - forward * along;
            if (along < CombatConfig.LungeMinRange || along > CombatConfig.LungeMaxRange + enemy.radius)
                return false;
            if (lateral.magnitude > CombatConfig.LungeAimRadius + enemy.radius)
                return false;

            Vector3 eye = player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 target = enemy.pos + Vector3.up * (enemy.height * 0.6f);
            if (!services.Collision.HasLineOfSight(eye, target)) return false;

            Vector3 direction = CombatMath.FlatDirection(player.pos, enemy.pos);
            destination = enemy.pos - direction * (CombatConfig.LungeStopDistance + enemy.radius);
            destination.y = enemy.pos.y + CombatConfig.LungeAimUp;   // real Sim과 동일: 대상 고도에 붙음
            return true;
        }
    }
}
