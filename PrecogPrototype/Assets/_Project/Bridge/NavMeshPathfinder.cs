using UnityEngine;
using UnityEngine.AI;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// 레거시 씬 모드 폴백 IPathfinder = NavMesh.CalculatePath → Walk 스텝만.
    /// 코드 아레나는 GraphPathfinder(그래프)를 쓴다. 씬 모드엔 그래프가 없어 하강(Jump) 없음.
    /// </summary>
    public class NavMeshPathfinder : IPathfinder
    {
        readonly NavMeshPath path = new NavMeshPath();
        const float SampleRadius = 4f;

        public PathStep NextStep(Vector3 from, Vector3 to)
        {
            var step = new PathStep
            {
                kind = MoveKind.None, next = to,
                currentNodeId = -1, nextNodeId = -1, destinationNodeId = -1, linkId = -1,
                corridorWidth = float.MaxValue
            };
            if (!Calc(from, to)) return step;
            var c = path.corners;
            if (c.Length >= 2) { step.kind = MoveKind.Walk; step.next = c[1]; }
            else if (c.Length == 1) { step.kind = MoveKind.Walk; step.next = c[0]; }
            return step;
        }

        bool Calc(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out var f, SampleRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to,   out var t, SampleRadius, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(f.position, t.position, NavMesh.AllAreas, path);
        }
    }
}
