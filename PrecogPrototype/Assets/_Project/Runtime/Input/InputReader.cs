using UnityEngine;
using UnityEngine.InputSystem;
using Game.Simulation;

namespace Game.Runtime
{
    /// <summary>
    /// 키보드/마우스를 읽어 시뮬레이션용 InputCmd로 변환한다.
    /// 표현 계층에만 존재한다 — 시뮬레이션은 Input을 모른다.
    ///
    /// 순간 입력(점프/대시/공격)은 매 프레임(Update)에서 눌림을 버퍼에 쌓고,
    /// FixedUpdate에서 Consume할 때 비운다 → 60Hz 틱과 프레임률이 달라도
    /// 눌림이 누락/중복되지 않는다.
    /// </summary>
    public class InputReader
    {
        float yaw;
        const float MouseSens = 0.1f;

        // Update에서 쌓이는 순간 입력 버퍼
        bool jumpBuffered;
        bool dashBuffered;
        bool attackBuffered;

        /// <summary>매 프레임(Update)에서 호출. 순간 입력 눌림을 버퍼에 쌓는다.</summary>
        public void PollFrame()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (mouse != null)
                yaw += mouse.delta.ReadValue().x * MouseSens;

            if (kb.spaceKey.wasPressedThisFrame)     jumpBuffered = true;
            if (kb.leftShiftKey.wasPressedThisFrame) dashBuffered = true;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) attackBuffered = true;
        }

        /// <summary>FixedUpdate에서 호출. 지속 입력 + 버퍼된 순간 입력을 합쳐 cmd 생성 후 버퍼 비움.</summary>
        public InputCmd Consume()
        {
            InputCmd cmd = InputCmd.Empty;
            cmd.yaw = yaw;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null)
            {
                Vector2 move = Vector2.zero;
                if (kb.aKey.isPressed) move.x -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                cmd.move = move;

                cmd.block = (mouse != null) && mouse.rightButton.isPressed;
            }

            cmd.jump   = jumpBuffered;
            cmd.dash   = dashBuffered;
            cmd.attack = attackBuffered;

            jumpBuffered = dashBuffered = attackBuffered = false;
            return cmd;
        }
    }
}
