using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// IPathfinder 구현 = NavMesh.CalculatePath. 결정론 검증 완료.
    /// 하강 테두리(edge,landing) 목록을 들고, "가장 가까운 테두리"와 "경로 길이"를 제공.
    /// 하강할지 말지 판단은 Sim(EnemyMovement)이 이 길이들을 비교해서 한다.
    /// </summary>
    public class NavMeshPathfinder : IPathfinder
    {
        readonly NavMeshPath path = new NavMeshPath();
        readonly List<(Vector3 edge, Vector3 landing)> drops;
        const float SampleRadius = 4f;

        public NavMeshPathfinder(List<(Vector3, Vector3)> dropEdges)
        {
            drops = dropEdges ?? new List<(Vector3, Vector3)>();
        }

        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 next)
        {
            next = to;
            if (!Calc(from, to)) return false;
            var c = path.corners;
            if (c.Length >= 2) { next = c[1]; return true; }
            if (c.Length == 1) { next = c[0]; return true; }
            return false;
        }

        public float PathLength(Vector3 from, Vector3 to)
        {
            if (!Calc(from, to)) return -1f;
            if (path.status != NavMeshPathStatus.PathComplete) return -1f;
            var c = path.corners;
            float len = 0f;
            for (int i = 0; i < c.Length - 1; i++) len += Vector3.Distance(c[i], c[i + 1]);
            return len;
        }

        public bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing)
        {
            edge = default; landing = default;
            if (drops.Count == 0) return false;
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < drops.Count; i++)
            {
                float d = (drops[i].edge - from).sqrMagnitude;
                if (d < best) { best = d; edge = drops[i].edge; landing = drops[i].landing; found = true; }
            }
            return found;
        }

        bool Calc(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out var f, SampleRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to,   out var t, SampleRadius, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(f.position, t.position, NavMesh.AllAreas, path);
        }
    }
}
