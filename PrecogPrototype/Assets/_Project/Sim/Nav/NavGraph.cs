using UnityEngine;

namespace Game.Sim
{
    /// <summary>노드 간 이동 종류. Walk=지상, Jump=하강(드롭), Boost=부스터 상행.</summary>
    public enum MoveKind : byte { None = 0, Walk = 1, Jump = 2, Boost = 3 }

    public struct NavNode { public Vector3 pos; public byte level; }

    public struct NavLink
    {
        public int from, to;
        public MoveKind kind;
        public float width;   // 복도 폭(분리 구속용 — 넓은 구역은 크게, 좁은 다리는 작게)
        public NavLink(int from, int to, MoveKind kind, float width)
        { this.from = from; this.to = to; this.kind = kind; this.width = width; }
    }

    /// <summary>from→to 다음 홉 질의 결과.</summary>
    public struct PathStep
    {
        public MoveKind kind;
        public Vector3  next;             // 다음 노드 위치(같은 노드면 목적지)
        public int      currentNodeId, nextNodeId, destinationNodeId, linkId;
        public float    corridorWidth;    // 현재 링크 폭(복도 구속용)
    }

    /// <summary>
    /// 고정 노드 그래프 + 사전계산 최단경로표(Floyd-Warshall). 예측이 NavMesh 대신 이걸 조회한다.
    /// 전부 배열(순수 데이터) → 포크·결정론·Burst 가능. 동률은 "낮은 next 노드"로 결정론 해소.
    /// 런타임 Physics/NavMesh 호출 0 → 예측 루프에서 표 조회(O(1))만.
    /// </summary>
    public sealed class NavGraph
    {
        const int Inf = 1_000_000;

        public readonly NavNode[] nodes;
        readonly int[,]      next;     // next[from,dest] = 다음 홉 노드 (-1=경로 없음)
        readonly int[,]      dist;     // ×100 정수 거리 (Inf=도달 불가)
        readonly int[,]      adjLink;  // from→to 직접 인접 링크 id (-1=인접 아님)
        readonly MoveKind[,] adjKind;  // 인접 이동 종류
        readonly float[,]    adjWidth; // 인접 링크 폭

        NavGraph(NavNode[] nodes, int[,] next, int[,] dist,
                 int[,] adjLink, MoveKind[,] adjKind, float[,] adjWidth)
        {
            this.nodes = nodes; this.next = next; this.dist = dist;
            this.adjLink = adjLink; this.adjKind = adjKind; this.adjWidth = adjWidth;
        }

        /// <summary>노드·링크 → 사전계산표 굽기. 로드 시 1회.</summary>
        public static NavGraph Create(NavNode[] nodes, NavLink[] links)
        {
            int n = nodes.Length;
            var dist = new int[n, n];
            var next = new int[n, n];
            var adjLink = new int[n, n];
            var adjKind = new MoveKind[n, n];
            var adjWidth = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                { dist[i, j] = i == j ? 0 : Inf; next[i, j] = i == j ? i : -1; adjLink[i, j] = -1; }

            for (int L = 0; L < links.Length; L++)
            {
                NavLink lk = links[L];
                int cost = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(nodes[lk.from].pos, nodes[lk.to].pos) * 100f));
                if (cost < dist[lk.from, lk.to]) { dist[lk.from, lk.to] = cost; next[lk.from, lk.to] = lk.to; }
                adjLink[lk.from, lk.to] = L;
                adjKind[lk.from, lk.to] = lk.kind;
                adjWidth[lk.from, lk.to] = lk.width;
            }

            for (int k = 0; k < n; k++)
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        int cand = (dist[i, k] >= Inf || dist[k, j] >= Inf) ? Inf : dist[i, k] + dist[k, j];
                        int candNext = next[i, k];
                        if (cand < dist[i, j] ||
                            (cand == dist[i, j] && candNext >= 0 && (next[i, j] < 0 || candNext < next[i, j])))
                        { dist[i, j] = cand; next[i, j] = candNext; }
                    }

            return new NavGraph(nodes, next, dist, adjLink, adjKind, adjWidth);
        }

        /// <summary>위치에서 가장 가까운 노드(3D 거리 — 층 구분 유지). 동률은 낮은 id.</summary>
        public int NearestNode(Vector3 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                float d = (nodes[i].pos - p).sqrMagnitude;
                if (d < bd - 1e-5f || (Mathf.Abs(d - bd) <= 1e-5f && i < best)) { best = i; bd = d; }
            }
            return best;
        }

        /// <summary>from→to 최단경로의 다음 홉(표 조회). 같은 노드면 목적지 직행.</summary>
        public PathStep Step(Vector3 from, Vector3 to)
        {
            int cur = NearestNode(from), dst = NearestNode(to);
            if (cur == dst)
                return new PathStep { kind = MoveKind.Walk, next = to, currentNodeId = cur,
                    nextNodeId = cur, destinationNodeId = dst, linkId = -1, corridorWidth = float.MaxValue };
            int nx = next[cur, dst];
            if (nx < 0)
                return new PathStep { kind = MoveKind.None, next = from, currentNodeId = cur,
                    nextNodeId = -1, destinationNodeId = dst, linkId = -1, corridorWidth = 0f };
            return new PathStep {
                kind = adjKind[cur, nx], next = nodes[nx].pos,
                currentNodeId = cur, nextNodeId = nx, destinationNodeId = dst,
                linkId = adjLink[cur, nx], corridorWidth = adjWidth[cur, nx]
            };
        }

        /// <summary>from→to 최단 거리(월드 단위). 도달 불가면 -1.</summary>
        public float Distance(Vector3 from, Vector3 to)
        {
            int d = dist[NearestNode(from), NearestNode(to)];
            return d >= Inf ? -1f : d / 100f;
        }
    }
}
