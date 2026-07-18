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
        public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
        {
            var step = new PathStep { kind = MoveKind.None, next = to, currentNodeId = -1,
                nextNodeId = -1, destinationNodeId = -1, linkId = -1, floorId = -1, destinationFloorId = -1 };
            if (!Calc(from, to)) return step;
            var c = path.corners;
            if (c.Length < 1) return step;
            Vector3 nc = c.Length >= 2 ? c[1] : c[0];
            step.next = nc;
            float drop = from.y - nc.y;
            float dx = nc.x - from.x, dz = nc.z - from.z;
            float horiz = Mathf.Sqrt(dx * dx + dz * dz);
            step.kind = drop > SimConfig.DropDetectMinHeight && drop > horiz * SimConfig.DropDetectRatio
                ? MoveKind.Drop : MoveKind.Walk;
            step.traversalStart = from;
            return step;
        }

        public int FloorIdAt(Vector3 position) => -1;
        readonly NavMeshPath path = new NavMeshPath();
        const float SampleRadius = 4f;

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
