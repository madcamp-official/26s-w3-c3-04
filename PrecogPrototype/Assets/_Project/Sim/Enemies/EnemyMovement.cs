using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 적 이동: NavMesh 경로 추격(NextStep 다음 코너) + 절벽 낙하(Jump). 이동은 분리 스티어링(sep)을
    /// 섞어 뭉침을 막고, 시선(yaw)은 이동과 분리해 항상 플레이어를 응시한다(밀림 방향 회전 버그 방지).
    /// 얇은 다리 낙하 방지는 SimStep 틱 끝의 ClampToNavMesh(navmesh 되당김)가 담당한다.
    /// </summary>
    public static class EnemyMovement
    {
        public static void Step(ref EnemySim e, in PlayerSim player, Vector3 sep, in SimServices svc, float dt)
        {
            if (!e.alive) return;

            // 스턴 중이면 정지(스턴 부여는 폐기됐지만 필드는 남아 있어 방어적으로 처리)
            if (e.combat.stunTicks > 0) { Move(ref e, Vector3.zero, svc, dt); return; }

            // 하강 진행 중이면 상태머신만
            if (e.descentPhase != DescentPhase.None) { StepDescent(ref e, in svc, dt); return; }

            if (FlatSqrDist(e.pos, player.pos) > SimConfig.EnemyAggroRange * SimConfig.EnemyAggroRange)
            { Move(ref e, Vector3.zero, svc, dt); return; }

            // 경로 재계산(주기적) — NavMesh 다음 코너 조회
            if (e.repathTicks > 0) e.repathTicks--;
            if (e.repathTicks == 0 || !e.hasWaypoint)
            {
                PathStep step = svc.Pathfinder.NextStep(e.pos, player.pos);
                e.waypoint = step.next;
                e.hasWaypoint = step.kind != MoveKind.None;
                e.repathTicks = SimConfig.EnemyRepathTicks;
                if (step.kind == MoveKind.Jump) { StartDescent(ref e, step.next); return; }  // 절벽 낙하(off-mesh link)
            }

            // 시선: 이동과 분리 — 항상 플레이어를 응시(밀림 방향으로 도는 회전 방지)
            Vector3 face = player.pos - e.pos; face.y = 0f;
            if (face.sqrMagnitude > 1e-6f) e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;

            Vector3 target = e.hasWaypoint ? e.waypoint : player.pos;
            Vector3 to = target - e.pos; to.y = 0f;
            float d = to.magnitude;
            if (d < SimConfig.EnemyArriveDist) e.repathTicks = 0;   // 웨이포인트 도달 → 다음 홉 재계산

            Vector3 horiz = Vector3.zero;
            if (d > 1e-4f)
            {
                Vector3 dir = to / d;
                Vector3 steer = dir + sep * AIConfig.SeparationWeight;   // 분리 스티어링(뭉침 방지)
                if (steer.sqrMagnitude > 1e-6f) dir = steer.normalized;
                horiz = dir * SimConfig.EnemyMoveSpeed * dt;
            }
            Move(ref e, horiz, svc, dt);
            // 다리 낙하 방지는 SimStep 틱 끝의 ClampToNavMesh가 담당(navmesh 되당김).
        }

        /// <summary>절벽 도약 시작: 가장자리(현재 위치)에서 착지점까지 스크립트 포물선으로 뛰어내린다.
        /// ClampToNavMesh는 descentPhase != None을 제외하므로, 도약 중엔 navmesh 밖으로 나가도 안 당겨진다.</summary>
        static void StartDescent(ref EnemySim e, Vector3 landing)
        {
            e.descentPhase = DescentPhase.Leaping;
            e.descentTicks = 0;
            e.descentStart = e.pos;
            e.descentLanding = landing;
            e.vel = Vector3.zero;
            Vector3 face = landing - e.pos; face.y = 0f;
            if (face.sqrMagnitude > 1e-4f) e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 절벽 도약 상태머신(전부 descentTicks 기준):
        ///  [0~Windup)          주저 — 가장자리서 착지점 응시하며 멈칫(텔레그래프)
        ///  [Windup~+Duration)  도약 — 솟구침(ease-out) → 가속 하강(ease-in) + 런치 수평(ease-out)
        ///  [+Duration~+Hold)   착지 — "쿵" 그 자리 경직 뒤 추격 재개
        /// </summary>
        static void StepDescent(ref EnemySim e, in SimServices svc, float dt)
        {
            e.descentTicks++;
            int t = e.descentTicks;
            int W = SimConfig.DropWindupTicks;
            int T = SimConfig.DropDurationTicks;

            if (t <= W)   // 주저(윈드업)
            {
                e.pos = e.descentStart;
                e.vel = Vector3.zero;
                e.grounded = true;
            }
            else if (t <= W + T)   // 도약
            {
                float u = (t - W) / (float)T;                 // 0→1
                float apex = e.descentStart.y + SimConfig.DropArcHeight;
                float lf = SimConfig.DropLaunchFrac;
                float y;
                if (u < lf)   // 상승: ease-out(감속하며 솟음)
                {
                    float w = u / lf;
                    y = Mathf.Lerp(e.descentStart.y, apex, 1f - (1f - w) * (1f - w));
                }
                else          // 하강: ease-in(가속 — 중력 느낌)
                {
                    float w = (u - lf) / (1f - lf);
                    y = Mathf.Lerp(apex, e.descentLanding.y, w * w);
                }
                float he = 1f - (1f - u) * (1f - u);          // 수평: ease-out(런치)
                e.pos = new Vector3(
                    Mathf.Lerp(e.descentStart.x, e.descentLanding.x, he),
                    y,
                    Mathf.Lerp(e.descentStart.z, e.descentLanding.z, he));
                e.vel = Vector3.zero;
                e.grounded = false;
            }
            else   // 착지 "쿵" + 경직
            {
                e.pos = e.descentLanding;
                e.vel = Vector3.zero;
                e.grounded = true;
                if (t >= W + T + SimConfig.DropLandHoldTicks)
                {
                    e.descentPhase = DescentPhase.None;
                    e.descentTicks = 0;
                    e.repathTicks = 0;
                    e.hasWaypoint = false;
                }
            }
        }

        static void Move(ref EnemySim e, Vector3 horiz, in SimServices svc, float dt)
        {
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool grounded);
            e.grounded = grounded;
        }

        static float FlatSqrDist(Vector3 a, Vector3 b)
        { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }
    }
}
