using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// IPathfinder 구현 = NavMesh.CalculatePath (+ NavMeshLink 하강 감지).
    /// 등록된 하강 링크(start→end)와 경로 코너를 대조해, 다음 이동이 점프인지 판단한다.
    /// </summary>
    public class NavMeshPathfinder : IPathfinder
    {
        readonly NavMeshPath path = new NavMeshPath();
        readonly List<(Vector3 start, Vector3 end)> links;
        const float SampleRadius = 4f;
        const float LinkTol = 2.5f;   // 링크 지점 매칭 허용 거리

        public NavMeshPathfinder(List<(Vector3, Vector3)> dropLinks)
        {
            links = dropLinks ?? new List<(Vector3, Vector3)>();
        }

        public MoveKind NextCorner(Vector3 from, Vector3 to, out Vector3 next)
        {
            next = to;

            if (!NavMesh.SamplePosition(from, out var fromHit, SampleRadius, NavMesh.AllAreas)) return MoveKind.None;
            if (!NavMesh.SamplePosition(to,   out var toHit,   SampleRadius, NavMesh.AllAreas)) return MoveKind.None;
            if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path)) return MoveKind.None;

            var c = path.corners;
            if (c.Length == 0) return MoveKind.None;
            if (c.Length == 1) { next = c[0]; return MoveKind.Walk; }

            next = c[1];

            // 이번 구간(from → next)이 등록된 하강 링크와 일치하면 점프
            for (int i = 0; i < links.Count; i++)
            {
                if (Near(from, links[i].start) && Near(next, links[i].end))
                {
                    next = links[i].end;
                    return MoveKind.Jump;
                }
            }
            return MoveKind.Walk;
        }

        static bool Near(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz < LinkTol * LinkTol;
        }
    }
}
