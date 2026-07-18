using UnityEngine;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// 노드 그래프 기반 IPathfinder. NavMesh/Physics 런타임 호출 없음 → 예측 친화(표 조회).
    /// 하강(Jump)·부스터(Boost)는 그래프 링크로 표현되어 NextStep이 kind로 알려준다.
    /// </summary>
    public sealed class GraphPathfinder : IPathfinder
    {
        readonly NavGraph graph;
        public GraphPathfinder(NavGraph graph) { this.graph = graph; }
        public PathStep NextStep(Vector3 from, Vector3 to) => graph.Step(from, to);
    }
}
