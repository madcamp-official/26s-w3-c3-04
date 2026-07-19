using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    public enum MacroActionType : byte
    {
        Wait,
        MoveForward,
        MoveLeft,
        MoveRight,
        Retreat,
        Jump,
        DashForward,
        DashBackward,
        DashLeft,
        DashRight,
        Attack,
        Lunge,
    }

    /// <summary>
    /// 탐색 후보 1개 = "macroTicks 동안 유지하는 InputCmd 패턴" 하나.
    /// 대시·공격·런지처럼 한 번만 트리거하면 되는 행동은 매크로의 첫 틱에만 펄스를 준다
    /// (이후는 Sim이 자체 상태머신으로 지속시킨다 — PlayerMovement/PlayerCombat 참고).
    /// </summary>
    public struct MacroAction
    {
        public MacroActionType type;

        /// <summary>Lunge일 때만 유효. 매크로 생성 시점에 확정해 Travel 중 재계산하지 않는다.</summary>
        public int lungeTargetId;

        public static MacroAction Simple(MacroActionType type) => new MacroAction { type = type, lungeTargetId = -1 };

        public static MacroAction LungeTo(int targetId) => new MacroAction { type = MacroActionType.Lunge, lungeTargetId = targetId };

        public InputCmd ToInputCmd(float yaw, int tickWithinMacro)
        {
            var cmd = new InputCmd { yaw = yaw };
            bool first = tickWithinMacro == 0;

            switch (type)
            {
                case MacroActionType.Wait:
                    break;
                case MacroActionType.MoveForward:
                    cmd.move = new Vector2(0f, 1f);
                    break;
                case MacroActionType.MoveLeft:
                    cmd.move = new Vector2(-1f, 0f);
                    break;
                case MacroActionType.MoveRight:
                    cmd.move = new Vector2(1f, 0f);
                    break;
                case MacroActionType.Retreat:
                    cmd.move = new Vector2(0f, -1f);
                    break;
                case MacroActionType.Jump:
                    if (first) cmd.jump = true;
                    break;
                case MacroActionType.DashForward:
                    if (first) { cmd.dash = true; cmd.dashDirection = DashDirection.Forward; }
                    break;
                case MacroActionType.DashBackward:
                    if (first) { cmd.dash = true; cmd.dashDirection = DashDirection.Backward; }
                    break;
                case MacroActionType.DashLeft:
                    if (first) { cmd.dash = true; cmd.dashDirection = DashDirection.Left; }
                    break;
                case MacroActionType.DashRight:
                    if (first) { cmd.dash = true; cmd.dashDirection = DashDirection.Right; }
                    break;
                case MacroActionType.Attack:
                    if (first) cmd.attack = true;
                    break;
                case MacroActionType.Lunge:
                    if (first) { cmd.lunge = true; cmd.lungeTargetId = lungeTargetId; }
                    break;
            }
            return cmd;
        }
    }
}
