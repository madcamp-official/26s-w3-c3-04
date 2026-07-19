using UnityEngine;
using UnityEngine.AI;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// NavMesh 런타임 길찾기 = NavMesh.CalculatePath → 다음 코너. 연속 메시라 기둥·벽 우회.
    /// 다음 코너가 큰 낙차(off-mesh link 절벽)면 kind=Jump로 알린다 → 몹이 걸어 나가 낙하.
    /// 경사로는 낙차/수평비가 작아 Walk로 구분(SimConfig.DropDetect*).
    /// </summary>
    public class NavMeshPathfinder : IPathfinder
    {
        readonly NavMeshPath path = new NavMeshPath();
        const float SampleRadius = 4f;

        public PathStep NextStep(Vector3 from, Vector3 to)
        {
            var step = new PathStep { kind = MoveKind.None, next = to };
            if (!Calc(from, to)) return step;
            var c = path.corners;
            if (c.Length < 2)
            {
                if (c.Length == 1) { step.kind = MoveKind.Walk; step.next = c[0]; }
                return step;
            }

            // 다음 코너가 이미 급낙차 = 링크 위 → 즉시 낙하
            if (IsDrop(from, c[1])) { step.kind = MoveKind.Jump; step.next = c[1]; return step; }

            // 다음 코너는 완만(=off-mesh link 입구)인데 '그 다음'이 급낙차이고 입구에 근접했으면
            // 낙하 커밋(문턱에서 오락가락 낑김 방지 — 착지점으로 바로 떨어짐).
            if (c.Length >= 3 && HorizDist(from, c[1]) < SimConfig.DropCommitDist && IsDrop(c[1], c[2]))
            { step.kind = MoveKind.Jump; step.next = c[2]; return step; }

            step.kind = MoveKind.Walk; step.next = c[1];
            return step;
        }

        /// <summary>a→b가 절벽 낙차인가(가파른 아래). 경사로는 완만해서 false.</summary>
        static bool IsDrop(Vector3 a, Vector3 b)
        {
            float drop = a.y - b.y;
            return drop > SimConfig.DropDetectMinHeight && drop > HorizDist(a, b) * SimConfig.DropDetectRatio;
        }

        static float HorizDist(Vector3 a, Vector3 b)
        { float dx = b.x - a.x, dz = b.z - a.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        bool Calc(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out var f, SampleRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to,   out var t, SampleRadius, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(f.position, t.position, NavMesh.AllAreas, path);
        }

        /// <summary>가장 가까운 navmesh 점(반경 안). navmesh는 에이전트 반경만큼 가장자리가 깎여 있어
        /// 이 점으로 당기면 몹이 얇은 다리 밖으로 안 나간다.</summary>
        public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
        {
            if (NavMesh.SamplePosition(pos, out var hit, maxDist, NavMesh.AllAreas))
            { onMesh = hit.position; return true; }
            onMesh = pos; return false;
        }
    }
}
