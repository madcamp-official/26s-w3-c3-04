using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 상황별 행동 후보 생성. 실전과 같은 공개 Sim 규칙(CombatMath, CombatConfig,
    /// PlayerCombat.FindLungeTarget)만 재사용한다 — 예측 전용 판정을 새로 만들지 않는다.
    /// 순서 고정: Lunge → Attack → DashForward → Retreat → MoveForward → Wait →
    /// DashBackward → DashLeft → DashRight → MoveLeft → MoveRight.
    /// 앞의 6개는 원래 미니 탐색(G3, maxActionsPerNode=4)이 그대로 상위 4개(Lunge/Attack/
    /// DashForward/Retreat)를 고르도록 순서를 보존한 것이고, 나머지 방향들은 뒤에 붙여
    /// 본탐색(maxActionsPerNode를 늘린 프리셋)에서만 실질적으로 후보에 들어간다.
    /// 이 순서 자체가 Beam Search 동률 해소 기준이 된다. LINQ/할당 없이 버퍼에 채운다.
    /// </summary>
    public static class ActionGenerator
    {
        static readonly MacroActionType[] Priority =
        {
            MacroActionType.Lunge,
            MacroActionType.Attack,
            MacroActionType.DashForward,
            MacroActionType.Retreat,
            MacroActionType.MoveForward,
            MacroActionType.Wait,
            MacroActionType.DashBackward,
            MacroActionType.DashLeft,
            MacroActionType.DashRight,
            MacroActionType.MoveLeft,
            MacroActionType.MoveRight,
        };

        /// <summary>buffer에 후보를 채우고 개수를 반환한다. buffer는 호출자가 재사용(풀링)한다.</summary>
        public static int Generate(in SimWorld world, in SimServices services, in PredictionSettings settings, MacroAction[] buffer)
        {
            int count = 0;
            int cap = Mathf.Min(settings.maxActionsPerNode, buffer.Length);
            ref readonly PlayerSim player = ref world.player;
            bool canStartAction = player.alive && player.combat.phase == PlayerActionPhase.None;

            for (int p = 0; p < Priority.Length && count < cap; p++)
            {
                switch (Priority[p])
                {
                    case MacroActionType.Lunge:
                        if (canStartAction && player.combat.lungeCooldownTicks == 0)
                        {
                            int targetId = PlayerCombat.FindLungeTarget(in world, in player, in services);
                            if (targetId >= 0) buffer[count++] = MacroAction.LungeTo(targetId);
                        }
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
                if (!enemy.alive) continue;
                if (Mathf.Abs(enemy.pos.y - world.player.pos.y) > CombatConfig.AttackHeightTolerance) continue;
                if (CombatMath.InCone(world.player.pos, forward, enemy.pos,
                        CombatConfig.AttackRange, CombatConfig.AttackHalfAngleDeg))
                    return true;
            }
            return false;
        }
    }
}
