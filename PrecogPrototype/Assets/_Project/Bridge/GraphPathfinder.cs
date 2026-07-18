using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// 노드 그래프 기반 IPathfinder(드롭인). NavMesh 런타임 호출 없음 → 예측 친화(표 조회).
    /// 현재 EnemyMovement의 NextCorner/PathLength/NearestDropEdge를 그대로 만족하되,
    /// 내부는 사전계산표. 하강 판단(walk vs jump)은 기존 로직 유지 — 걷기 거리는 그래프,
    /// 드롭은 authoring된 drops 목록. (그래프-네이티브 하강/부스터 링크는 이후 G3에서.)
    /// </summary>
    public sealed class GraphPathfinder : IPathfinder
    {
        readonly NavGraph graph;
        readonly List<(Vector3 edge, Vector3 landing)> drops;

        public GraphPathfinder(NavGraph graph, List<(Vector3 edge, Vector3 landing)> drops)
        {
            this.graph = graph;
            this.drops = drops;
        }

        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 next)
        {
            PathStep s = graph.Step(from, to);
            next = s.next;
            return s.kind != MoveKind.None;
        }

        public float PathLength(Vector3 from, Vector3 to) => graph.Distance(from, to);

        public bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing)
        {
            edge = default; landing = default;
            if (drops == null || drops.Count == 0) return false;
            float best = float.MaxValue; bool found = false;
            for (int i = 0; i < drops.Count; i++)
            {
                float d = (drops[i].edge - from).sqrMagnitude;
                if (d < best) { best = d; edge = drops[i].edge; landing = drops[i].landing; found = true; }
            }
            return found;
        }
    }
}
