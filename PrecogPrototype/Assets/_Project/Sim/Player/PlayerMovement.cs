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

            // 질풍참 시작 — 겐지 질풍참: 카메라 방향(pitch 포함)으로, "조준한 첫 표면까지"만 돌진.
            // 눈높이에서 레이를 쏴 벽/바닥/천장까지 거리를 구한다. 위로 보면 공중으로,
            // 아래로 얕게 보면 멀리, 가파르게 보면 짧게(OW와 동일) 이동 후 그 지점에서 정지.
            if (p.dashTicks == 0 && cmd.dash && p.dashCharges > 0)
            {
                p.dashDir = Quaternion.Euler(cmd.pitch, cmd.yaw, 0f) * Vector3.forward;  // 3D 시선
                float maxDist = SimConfig.DashSpeed * SimConfig.DashDurationTicks * dt;
                Vector3 eye = p.pos + Vector3.up * SimConfig.DashAimHeight;
                CastHit rh = svc.Collision.Raycast(eye, p.dashDir, maxDist);
                p.dashDist = rh.hit ? Mathf.Max(0f, rh.distance - SimConfig.PlayerRadius) : maxDist;

                p.dashTicks = SimConfig.DashDurationTicks;
                p.dashCharges--;
                if (p.dashRecharge == 0) p.dashRecharge = SimConfig.DashRechargeTicks;
            }

            // ── 돌진 중: 중력 없이 남은 거리만큼만 이동. 다 가면 즉시 정지(미끄러짐 없음) ──
            if (p.dashTicks > 0)
            {
                float step = Mathf.Min(SimConfig.DashSpeed * dt, p.dashDist);
                Vector3 d = p.dashDir * step;

                // 수평은 벽 슬라이드(안전망), 수직은 바닥 아래로만 클램프(머리 위에서 탐지)
                p.pos = CharacterMotor.MoveHorizontal(svc.Collision, p.pos,
                                                      new Vector3(d.x, 0f, d.z),
                                                      SimConfig.PlayerRadius, SimConfig.PlayerHeight);
                float newY = p.pos.y + d.y;
                Vector3 probe = new Vector3(p.pos.x, p.pos.y + SimConfig.PlayerHeight, p.pos.z);
                if (svc.Collision.SampleGround(probe, 500f, out float gy) && newY < gy)
                    newY = gy;
                p.pos.y = newY;

                p.dashDist -= step;
                p.vel.y = 0f;
                p.grounded = false;
                if (p.dashDist <= 1e-4f) p.dashTicks = 0;   // 목표 도달 → 정지
                else p.dashTicks--;
                return;
            }

            // ── 일반 이동: WASD → 수평(벽 슬라이드) → 수직(중력·착지) ──
            Vector3 wish = right * cmd.move.x + fwd * cmd.move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            Vector3 horiz = wish * SimConfig.PlayerMoveSpeed * dt;

            if (cmd.jump && p.jumpCount < 2)
            {
                p.vel.y = SimConfig.PlayerJumpSpeed;
                p.jumpCount++;
                p.grounded = false;
            }

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
