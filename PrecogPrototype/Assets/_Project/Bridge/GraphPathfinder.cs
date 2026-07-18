using UnityEngine;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>고정 아레나용 결정론적 경로표. 예측 루프에서 NavMesh를 호출하지 않는다.</summary>
    public sealed class GraphPathfinder : IPathfinder
    {
        readonly Vector3[] nodes;
        readonly int[,] next;
        readonly int[,] linkIds;
        readonly MoveKind[,] kinds;

        GraphPathfinder(Vector3[] nodes, int[,] next, int[,] linkIds, MoveKind[,] kinds)
        {
            this.nodes = nodes;
            this.next = next;
            this.linkIds = linkIds;
            this.kinds = kinds;
        }

        public PathStep NextStep(Vector3 from, Vector3 to)
        {
            int current = NearestNode(from);
            int destination = NearestNode(to);
            if (current == destination)
            {
                return new PathStep
                {
                    kind = MoveKind.Walk,
                    next = to,
                    currentNodeId = current,
                    destinationNodeId = destination,
                    nextNodeId = current,
                    linkId = -1
                };
            }
            int nextId = next[current, destination];
            return new PathStep
            {
                kind = nextId < 0 ? MoveKind.None : kinds[current, nextId],
                next = nextId < 0 ? from : nodes[nextId],
                currentNodeId = current,
                destinationNodeId = destination,
                nextNodeId = nextId,
                linkId = nextId < 0 ? -1 : linkIds[current, nextId]
            };
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
            const int Infinity = 1_000_000;
            int[,] distance = new int[count, count];
            int[,] first = new int[count, count];
            int[,] links = new int[count, count];
            MoveKind[,] moveKinds = new MoveKind[count, count];
            for (int i = 0; i < count; i++)
            for (int j = 0; j < count; j++)
            {
                distance[i, j] = i == j ? 0 : Infinity;
                first[i, j] = i == j ? i : -1;
                links[i, j] = -1;
            }

            AddEdge(0, 1, MoveKind.Walk, 0);
            AddEdge(1, 0, MoveKind.Walk, 1);
            AddEdge(2, 1, MoveKind.Jump, 2);
            AddEdge(2, 3, MoveKind.Walk, 3);
            AddEdge(3, 2, MoveKind.Walk, 4);
            AddEdge(3, 4, MoveKind.Walk, 5);
            AddEdge(4, 3, MoveKind.Walk, 6);
            AddEdge(3, 5, MoveKind.Walk, 7);
            AddEdge(5, 3, MoveKind.Walk, 8);
            AddEdge(3, 6, MoveKind.Walk, 9);
            AddEdge(6, 3, MoveKind.Walk, 10);

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

            return new GraphPathfinder(positions, first, links, moveKinds);

            void AddEdge(int from, int to, MoveKind kind, int linkId)
            {
                int cost = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(positions[from], positions[to]) * 100f));
                distance[from, to] = cost;
                first[from, to] = to;
                links[from, to] = linkId;
                moveKinds[from, to] = kind;
            }
        }
    }
}
