using UnityEngine;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>고정 아레나용 결정론적 경로표. 예측 루프에서 NavMesh를 호출하지 않는다.</summary>
    public sealed class GraphPathfinder : IPathfinder
    {
        const int Infinity = 1_000_000;

        readonly Vector3[] nodes;
        readonly int[,] next;       // Floyd-Warshall 첫 홉 노드 id
        readonly int[,] distance;   // 노드 간 최단 경로 비용(×100, 정수)
        readonly bool[,] isDrop;    // [from,to] 간선이 하강(테두리 점프)인지

        GraphPathfinder(Vector3[] nodes, int[,] next, int[,] distance, bool[,] isDrop)
        {
            this.nodes = nodes;
            this.next = next;
            this.distance = distance;
            this.isDrop = isDrop;
        }

        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 nextPos)
        {
            int current = NearestNode(from);
            int destination = NearestNode(to);
            if (current == destination) { nextPos = to; return true; }
            int nextId = next[current, destination];
            if (nextId < 0) { nextPos = from; return false; }
            nextPos = nodes[nextId];
            return true;
        }

        public float PathLength(Vector3 from, Vector3 to)
        {
            int current = NearestNode(from);
            int destination = NearestNode(to);
            if (current == destination) return Vector3.Distance(from, to);
            int cost = distance[current, destination];
            return cost >= Infinity ? -1f : cost / 100f;
        }

        public bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing)
        {
            int current = NearestNode(from);
            for (int j = 0; j < nodes.Length; j++)
            {
                if (!isDrop[current, j]) continue;
                edge = nodes[current];
                landing = nodes[j];
                return true;
            }
            edge = from; landing = from;
            return false;
        }

        int NearestNode(Vector3 position)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                float distance = (nodes[i] - position).sqrMagnitude;
                if (distance < bestDistance - 1e-5f ||
                    (Mathf.Abs(distance - bestDistance) <= 1e-5f && i < best))
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public static GraphPathfinder CreatePrototypeArena()
        {
            Vector3[] positions =
            {
                new Vector3(-14f, 0f, 14f),
                new Vector3(-12f, 0f, 14f),
                new Vector3(-6.5f, 4f, 14f),
                new Vector3(0f, 4f, 14f),
                new Vector3(5f, 4f, 14f),
                new Vector3(-4f, 4f, 16f),
                new Vector3(4f, 4f, 12f)
            };

            int count = positions.Length;
            int[,] distance = new int[count, count];
            int[,] first = new int[count, count];
            bool[,] isDrop = new bool[count, count];
            for (int i = 0; i < count; i++)
            for (int j = 0; j < count; j++)
            {
                distance[i, j] = i == j ? 0 : Infinity;
                first[i, j] = i == j ? i : -1;
            }

            AddEdge(0, 1, false);
            AddEdge(1, 0, false);
            AddEdge(2, 1, true);   // 테두리 하강
            AddEdge(2, 3, false);
            AddEdge(3, 2, false);
            AddEdge(3, 4, false);
            AddEdge(4, 3, false);
            AddEdge(3, 5, false);
            AddEdge(5, 3, false);
            AddEdge(3, 6, false);
            AddEdge(6, 3, false);

            for (int k = 0; k < count; k++)
            for (int i = 0; i < count; i++)
            for (int j = 0; j < count; j++)
            {
                int candidate = distance[i, k] + distance[k, j];
                int candidateFirst = first[i, k];
                if (candidate < distance[i, j] ||
                    (candidate == distance[i, j] && candidateFirst >= 0 &&
                     (first[i, j] < 0 || candidateFirst < first[i, j])))
                {
                    distance[i, j] = candidate;
                    first[i, j] = candidateFirst;
                }
            }

            return new GraphPathfinder(positions, first, distance, isDrop);

            void AddEdge(int from, int to, bool drop)
            {
                int cost = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(positions[from], positions[to]) * 100f));
                distance[from, to] = cost;
                first[from, to] = to;
                isDrop[from, to] = drop;
            }
        }
    }
}
