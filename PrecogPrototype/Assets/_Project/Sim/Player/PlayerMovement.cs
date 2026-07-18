using UnityEngine;

namespace Game.Sim
{
    /// <summary>플레이어 이동: WASD·더블점프·4방향 대시(카메라 기준, 이동 전용). 벽은 CharacterMotor가 처리.</summary>
    public static class PlayerMovement
    {
        public static void Step(ref PlayerSim p, in InputCmd cmd, in SimServices svc, float dt)
        {
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

            // 피격 경직: 수평 조작 제한(중력·착지만)
            if (p.combat.hitStunTicks > 0)
            {
                CharacterMotor.ResolveVertical(svc.Collision, ref p.pos, ref p.vel, dt, out p.grounded);
                if (p.grounded) p.jumpCount = 0;
                return;
            }

            // 런지 Travel 중엔 PlayerCombat이 pos를 구동 — 여기선 손대지 않음
            if (p.combat.lungePhase == CombatConfig.LgTravel) return;

            // 4방향 대시 시작 — 카메라 기준 방향, 시작 순간 고정. 이동 전용(피해·무적 없음).
            if (p.dashTicks == 0 && cmd.dash && p.dashCharges > 0)
            {
                p.dashTicks = SimConfig.DashDurationTicks;
                p.dashDir = DashVector(cmd.dashDirection, fwd, right);
                p.dashCharges--;
                if (p.dashRecharge == 0) p.dashRecharge = SimConfig.DashRechargeTicks;
            }

            Vector3 horiz;
            if (p.dashTicks > 0)
            {
                // SmoothStep 이징: 총거리를 틱별 구간으로 분배(부드러운 가감속, 총합은 정확)
                int elapsed = SimConfig.DashDurationTicks - p.dashTicks;
                float t0 = (float)elapsed / SimConfig.DashDurationTicks;
                float t1 = (float)(elapsed + 1) / SimConfig.DashDurationTicks;
                float total = SimConfig.DashSpeed * SimConfig.DashDurationTicks * dt;
                horiz = p.dashDir * (total * (SmoothStep01(t1) - SmoothStep01(t0)));
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

            // 수평(벽 슬라이드) → 수직(중력·착지). 대시도 수평 전용이라 동일 경로.
            p.pos = CharacterMotor.MoveHorizontal(svc.Collision, p.pos, horiz,
                                                  SimConfig.PlayerRadius, SimConfig.PlayerHeight);
            CharacterMotor.ResolveVertical(svc.Collision, ref p.pos, ref p.vel, dt, out bool grounded);
            p.grounded = grounded;
            if (grounded) p.jumpCount = 0;
        }

        static Vector3 DashVector(DashDirection d, Vector3 fwd, Vector3 right)
        {
            switch (d)
            {
                case DashDirection.Backward: return -fwd;
                case DashDirection.Left:     return -right;
                case DashDirection.Right:    return right;
                default:                     return fwd;
            }
        }

        static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static Vector3 Forward(float yaw)
            => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    }
}
