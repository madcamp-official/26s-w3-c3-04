using UnityEngine;
using UnityEngine.AI;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// IPathfinder 구현 = NavMesh.CalculatePath. 결정론 검증 완료(같은 입력 → 같은 경로).
    /// 전체 경로를 재계산해 "다음 코너"만 돌려준다(적이 매 재계산마다 다음 지점을 갱신).
    /// </summary>
    public class NavMeshPathfinder : IPathfinder
    {
        readonly NavMeshPath path = new NavMeshPath();
        const float SampleRadius = 4f;

        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 next)
        {
            next = to;

            if (!NavMesh.SamplePosition(from, out var fromHit, SampleRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to,   out var toHit,   SampleRadius, NavMesh.AllAreas)) return false;

            if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path))
                return false;

            var c = path.corners;
            if (c.Length >= 2) { next = c[1]; return true; }  // 다음 코너
            if (c.Length == 1) { next = c[0]; return true; }  // 목표 구역
            return false;
        }
    }
}
