using System;
using UnityEngine;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>불변 ArenaMapBake에서 만든 결정론적 유향 경로표.</summary>
    public sealed class GraphPathfinder : IPathfinder
    {
        const int Infinity = 1_000_000_000;
        readonly ArenaNavNode[] nodes;
        readonly ArenaNavLink[] links;
        readonly int[,] next;
        readonly int[,] distance;
        readonly int[,] directLink;

        GraphPathfinder(ArenaNavNode[] nodes, ArenaNavLink[] links, int[,] next, int[,] distance, int[,] directLink)
        {
            this.nodes = nodes;
            this.links = links;
            this.next = next;
            this.distance = distance;
            this.directLink = directLink;
        }

        public static GraphPathfinder FromBake(ArenaMapBake bake, int agentMask = -1)
        {
            if (bake == null) throw new ArgumentNullException(nameof(bake));
            ArenaNavNode[] ns = (ArenaNavNode[])bake.nodes.Clone();
            ArenaNavLink[] ls = (ArenaNavLink[])bake.links.Clone();
            Array.Sort(ns, (a, b) => a.nodeId.CompareTo(b.nodeId));
            int n = ns.Length;
            var dist = new int[n, n];
            var first = new int[n, n];
            var direct = new int[n, n];
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                dist[i, j] = i == j ? 0 : Infinity;
                first[i, j] = i == j ? i : -1;
                direct[i, j] = -1;
            }

            for (int li = 0; li < ls.Length; li++)
            {
                ArenaNavLink l = ls[li];
                if (l.traversalType == NavTraversalType.Blocked || (l.agentMask & agentMask) == 0) continue;
                int a = IndexOf(ns, l.fromNodeId), b = IndexOf(ns, l.toNodeId);
                if (a < 0 || b < 0) continue;
                int cost = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(ns[a].position, ns[b].position) * 100f));
                if (cost < dist[a, b] || (cost == dist[a, b] && (direct[a, b] < 0 || l.linkId < ls[direct[a, b]].linkId)))
                {
                    dist[a, b] = cost;
                    first[a, b] = b;
                    direct[a, b] = li;
                }
            }

            for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (dist[i, k] >= Infinity || dist[k, j] >= Infinity) continue;
                int candidate = dist[i, k] + dist[k, j];
                int hop = first[i, k];
                if (candidate < dist[i, j] ||
                    (candidate == dist[i, j] && hop >= 0 &&
                     (first[i, j] < 0 || ns[hop].nodeId < ns[first[i, j]].nodeId)))
                {
                    dist[i, j] = candidate;
                    first[i, j] = hop;
                }
            }
            return new GraphPathfinder(ns, ls, first, dist, direct);
        }

        public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
        {
            if (nodes.Length == 0) return default;
            int a = NearestNode(from), destination = NearestNode(to);
            if (a == destination)
                return MakeStep(MoveKind.Walk, to, a, a, destination, -1, 0);
            int b = next[a, destination];
            if (b < 0) return new PathStep { kind = MoveKind.None, currentNodeId = nodes[a].nodeId, destinationNodeId = nodes[destination].nodeId, floorId = nodes[a].floorId, destinationFloorId = nodes[destination].floorId, linkId = -1 };
            int li = directLink[a, b];
            if (li < 0) return default;
            ArenaNavLink link = links[li];
            if ((link.agentMask & agentMask) == 0) return default;
            MoveKind kind = ToMoveKind(link.traversalType);
            Vector3 target = kind == MoveKind.Drop || kind == MoveKind.Boost ? link.landingPosition : nodes[b].position;
            return MakeStep(kind, target, a, b, destination, link.linkId, link.traversalTicks);
        }

        PathStep MakeStep(MoveKind kind, Vector3 target, int a, int b, int destination, int linkId, int ticks)
            => new PathStep { kind = kind, traversalStart = nodes[a].position, next = target, currentNodeId = nodes[a].nodeId,
                nextNodeId = nodes[b].nodeId, destinationNodeId = nodes[destination].nodeId,
                linkId = linkId, floorId = nodes[a].floorId, destinationFloorId = nodes[b].floorId,
                traversalTicks = ticks };

        public int FloorIdAt(Vector3 position) => nodes.Length == 0 ? -1 : nodes[NearestNode(position)].floorId;

        public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
        {
            onMesh = pos;
            return false;
        }

        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 nextPos)
        {
            PathStep step = NextStep(from, to, -1);
            nextPos = step.next;
            return step.kind != MoveKind.None;
        }

        public float PathLength(Vector3 from, Vector3 to)
        {
            if (nodes.Length == 0) return -1f;
            int a = NearestNode(from), b = NearestNode(to);
            return distance[a, b] >= Infinity ? -1f : distance[a, b] / 100f;
        }

        public bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing)
        {
            int a = NearestNode(from);
            for (int b = 0; b < nodes.Length; b++)
            {
                int li = directLink[a, b];
                if (li >= 0 && links[li].traversalType == NavTraversalType.DropDown)
                { edge = nodes[a].position; landing = links[li].landingPosition; return true; }
            }
            edge = landing = from; return false;
        }

        int NearestNode(Vector3 p)
        {
            int best = 0; float bestSq = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                float sq = (nodes[i].position - p).sqrMagnitude;
                if (sq < bestSq - 1e-5f || (Mathf.Abs(sq - bestSq) <= 1e-5f && nodes[i].nodeId < nodes[best].nodeId))
                { best = i; bestSq = sq; }
            }
            return best;
        }

        static int IndexOf(ArenaNavNode[] ns, int id)
        {
            for (int i = 0; i < ns.Length; i++) if (ns[i].nodeId == id) return i;
            return -1;
        }

        static MoveKind ToMoveKind(NavTraversalType t)
        {
            switch (t)
            {
                case NavTraversalType.DropDown: return MoveKind.Drop;
                case NavTraversalType.BoostUp: return MoveKind.Boost;
                case NavTraversalType.JumpUp: return MoveKind.JumpUp;
                case NavTraversalType.Blocked: return MoveKind.None;
                default: return MoveKind.Walk;
            }
        }

        public static GraphPathfinder CreatePrototypeArena()
        {
            var bake = new ArenaMapBake
            {
                nodes = new[]
                {
                    N(0,-14,0,14,0), N(1,-12,0,14,0), N(2,-6.5f,4,14,1),
                    N(3,0,4,14,1), N(4,5,4,14,1), N(5,-4,4,16,1), N(6,4,4,12,1)
                },
                links = new[]
                {
                    L(0,0,1,NavTraversalType.Walk), L(1,1,0,NavTraversalType.Walk),
                    L(2,2,1,NavTraversalType.DropDown),
                    L(3,2,3,NavTraversalType.Walk), L(4,3,2,NavTraversalType.Walk),
                    L(5,3,4,NavTraversalType.Walk), L(6,4,3,NavTraversalType.Walk),
                    L(7,3,5,NavTraversalType.Walk), L(8,5,3,NavTraversalType.Walk),
                    L(9,3,6,NavTraversalType.Walk), L(10,6,3,NavTraversalType.Walk)
                }
            };
            bake.links[2].landingPosition = bake.nodes[1].position;
            return FromBake(bake);
        }

        static ArenaNavNode N(int id,float x,float y,float z,int floor) => new ArenaNavNode { nodeId=id, position=new Vector3(x,y,z), floorId=floor, areaFlags=MapAreaFlags.Playable };
        static ArenaNavLink L(int id,int a,int b,NavTraversalType t) => new ArenaNavLink { linkId=id, fromNodeId=a, toNodeId=b, traversalType=t, traversalTicks=30, agentMask=-1, landingPosition=default };
    }
}
