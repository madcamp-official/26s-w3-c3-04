using UnityEngine;

namespace Game.Sim
{
    /// <summary>플레이어 이동: WASD·시점·더블점프·4방향 대시·런지 이동. 벽은 ICollision이 처리.</summary>
    public static class PlayerMovement
    {
        public static void Step(ref PlayerSim p, in InputCmd cmd, in SimServices svc, float dt)
        {
            if (!p.alive) return;
            p.yaw = cmd.yaw;
            Vector3 fwd = Forward(p.yaw);
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            // 대시 충전 회복
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

            if (p.hitStunTicks > 0)
            {
                CharacterMotor.ResolveVertical(svc.Collision, ref p.pos, ref p.vel, dt, out p.grounded);
                return;
            }

            if (p.combat.phase == PlayerActionPhase.LungeTravel)
                return;

            // 4방향 대시 시작
            if (p.dashTicks == 0 && cmd.dash && p.dashCharges > 0)
            {
                p.dashTicks = SimConfig.DashDurationTicks;
                p.dashDir = DashVector(cmd.dashDirection, fwd, right);
                p.dashCharges--;
                if (p.dashRecharge == 0) p.dashRecharge = SimConfig.DashRechargeTicks;
            }

            // 수평 이동량 결정
            Vector3 horiz;
            if (p.dashTicks > 0)
            {
                int elapsedTicks = SimConfig.DashDurationTicks - p.dashTicks;
                float t0 = (float)elapsedTicks / SimConfig.DashDurationTicks;
                float t1 = (float)(elapsedTicks + 1) / SimConfig.DashDurationTicks;
                float totalDistance = SimConfig.DashSpeed * SimConfig.DashDurationTicks * dt;
                float tickDistance = totalDistance * (SmoothStep01(t1) - SmoothStep01(t0));
                horiz = p.dashDir * tickDistance;
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

        static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static Vector3 DashVector(DashDirection direction, Vector3 forward, Vector3 right)
        {
            switch (direction)
            {
                case DashDirection.Backward: return -forward;
                case DashDirection.Left: return -right;
                case DashDirection.Right: return right;
                default: return forward;
            }
        }
    }
}
