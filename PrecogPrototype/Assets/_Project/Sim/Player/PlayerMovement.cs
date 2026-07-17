using UnityEngine;

namespace Game.Sim
{
    /// <summary>플레이어 이동: WASD·시점·더블점프·질풍참(이동만). 벽은 CharacterMotor가 처리.</summary>
    public static class PlayerMovement
    {
        public static void Step(ref PlayerSim p, in InputCmd cmd, in SimServices svc, float dt)
        {
            p.yaw = cmd.yaw;
            Vector3 fwd = Forward(p.yaw);
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            // 질풍참 충전 회복
            if (p.dashRecharge > 0)
            {
                p.dashRecharge--;
                if (p.dashRecharge == 0 && p.dashCharges < SimConfig.DashMaxCharges)
                {
                    p.dashCharges++;
                    if (p.dashCharges < SimConfig.DashMaxCharges)
                        p.dashRecharge = SimConfig.DashRechargeTicks;
                }
            }

            // 질풍참 시작
            if (p.dashTicks == 0 && cmd.dash && p.dashCharges > 0)
            {
                p.dashTicks = SimConfig.DashDurationTicks;
                Vector3 wish = right * cmd.move.x + fwd * cmd.move.y;
                p.dashDir = wish.sqrMagnitude > 1e-4f ? wish.normalized : fwd;
                p.dashCharges--;
                if (p.dashRecharge == 0) p.dashRecharge = SimConfig.DashRechargeTicks;
            }

            // 수평 이동량 결정
            Vector3 horiz;
            if (p.dashTicks > 0)
            {
                horiz = p.dashDir * SimConfig.DashSpeed * dt;
                p.dashTicks--;
            }
            else
            {
                Vector3 wish = right * cmd.move.x + fwd * cmd.move.y;
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                horiz = wish * SimConfig.PlayerMoveSpeed * dt;

                if (cmd.jump && p.jumpCount < 2)
                {
                    p.vel.y = SimConfig.PlayerJumpSpeed;
                    p.jumpCount++;
                    p.grounded = false;
                }
            }

            // 수평(벽 슬라이드) → 수직(중력·착지)
            p.pos = CharacterMotor.MoveHorizontal(svc.Collision, p.pos, horiz,
                                                  SimConfig.PlayerRadius, SimConfig.PlayerHeight);
            CharacterMotor.ResolveVertical(svc.Collision, ref p.pos, ref p.vel, dt, out bool grounded);
            p.grounded = grounded;
            if (grounded) p.jumpCount = 0;
        }

        static Vector3 Forward(float yaw)
            => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    }
}
