using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 플레이어 이동: WASD·더블점프(버퍼+공중 임펄스)·4방향 대시(둠식 임펄스).
    /// 벽은 CharacterMotor가 처리.
    /// </summary>
    public static class PlayerMovement
    {
        public static void Step(ref PlayerSim p, in InputCmd cmd, in SimServices svc, float dt)
        {
            p.yaw = cmd.yaw;
            Vector3 fwd = Forward(p.yaw);
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            // 대시 충전 회복 (스택별 순차)
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

            // 점프 버퍼: 누른 순간 기록 → 착지/가능 시점에 소비 (선입력 손맛)
            if (p.jumpBufferTicks > 0) p.jumpBufferTicks--;
            if (cmd.jump) p.jumpBufferTicks = SimConfig.JumpBufferTicks;

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
                // 둠식 임펄스: 첫 틱이 가장 크고 지수 감쇠. 틱별 거리 = 총거리 × d^i × (1-d)/(1-d^N)
                int elapsed = SimConfig.DashDurationTicks - p.dashTicks;   // 0..N-1
                float d = Mathf.Clamp(SimConfig.DashDecay, 0.01f, 0.99f);
                int n = SimConfig.DashDurationTicks;
                float norm = (1f - d) / (1f - Mathf.Pow(d, n));
                float tickDist = SimConfig.DashDistance * Mathf.Pow(d, elapsed) * norm;
                horiz = p.dashDir * tickDist;
                p.dashTicks--;
            }
            else
            {
                Vector3 wish = right * cmd.move.x + fwd * cmd.move.y;
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                horiz = wish * SimConfig.PlayerMoveSpeed * dt;

                // 점프 (버퍼 소비). 2단 점프는 입력 방향 수평 임펄스 동반(둠식 공중 방향전환)
                if (p.jumpBufferTicks > 0 && p.jumpCount < 2)
                {
                    bool airJump = !p.grounded && p.jumpCount >= 1;
                    p.vel.y = SimConfig.PlayerJumpSpeed;
                    p.jumpCount++;
                    p.grounded = false;
                    p.jumpBufferTicks = 0;
                    if (airJump && wish.sqrMagnitude > 1e-4f)
                    {
                        p.jumpBoostTicks = SimConfig.AirJumpBoostTicks;
                        p.jumpBoostDir = wish.normalized;
                    }
                }
            }

            // 2단 점프 수평 임펄스(선형 감쇠) — 일반 이동에 덧셈
            if (p.jumpBoostTicks > 0)
            {
                float k = (float)p.jumpBoostTicks / SimConfig.AirJumpBoostTicks;
                horiz += p.jumpBoostDir * (SimConfig.AirJumpBoost * k * dt);
                p.jumpBoostTicks--;
            }

            // 수평(벽 슬라이드) → 수직(중력·착지)
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

        static Vector3 Forward(float yaw)
            => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    }
}
