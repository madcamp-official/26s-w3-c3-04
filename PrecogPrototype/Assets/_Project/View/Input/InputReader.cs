using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 키/마우스 → InputCmd. 표현 계층에만 존재.
    /// 순간입력(점프/대시)은 Update에서 버퍼링, FixedUpdate에서 소비.
    /// Yaw(좌우)는 시뮬레이션에, Pitch(상하)는 카메라 전용.
    /// </summary>
    public class InputReader
    {
        public float Yaw   { get; set; }
        public float Pitch { get; private set; }

        const float Sens = 0.08f;
        bool jumpBuf, dashBuf, attackBuf, lungeBuf;

        public void PollFrame()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null) return;
            if (mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue();
                Yaw += d.x * Sens;
                Pitch = Mathf.Clamp(Pitch - d.y * Sens, -85f, 85f);
                if (mouse.leftButton.wasPressedThisFrame) attackBuf = true;   // 평타/칼등치기
                if (mouse.rightButton.wasPressedThisFrame) lungeBuf = true;
            }
            if (kb.spaceKey.wasPressedThisFrame)     jumpBuf = true;
            if (kb.leftShiftKey.wasPressedThisFrame) dashBuf = true;
        }

        public InputCmd Consume()
        {
            InputCmd cmd = InputCmd.Empty;
            cmd.yaw = Yaw;
            var kb = Keyboard.current;
            if (kb != null)
            {
                Vector2 m = Vector2.zero;
                if (kb.aKey.isPressed) m.x -= 1f;
                if (kb.dKey.isPressed) m.x += 1f;
                if (kb.wKey.isPressed) m.y += 1f;
                if (kb.sKey.isPressed) m.y -= 1f;
                cmd.move = m;
            }
            var mouse = Mouse.current;
            cmd.jump = jumpBuf;
            cmd.dash = dashBuf;
            cmd.attack = attackBuf;
            cmd.lunge = lungeBuf;
            cmd.lungeTargetId = -1;
            cmd.dashDirection = ResolveDashDirection(cmd.move);
            jumpBuf = dashBuf = attackBuf = lungeBuf = false;
            return cmd;
        }

        static DashDirection ResolveDashDirection(Vector2 move)
        {
            if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
                return move.x < 0f ? DashDirection.Left : DashDirection.Right;
            return move.y < 0f ? DashDirection.Backward : DashDirection.Forward;
        }

        public bool EscapePressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }
    }
}
