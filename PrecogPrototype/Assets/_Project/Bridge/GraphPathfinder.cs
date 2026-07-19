using System;
using System.Collections.Generic;
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

        /// <summary>실제 3층 아레나 그래프(17노드). 경사로·2F·3F·반층 Walk(양방향) + 다리→1F북 절벽 드롭.
        /// 손 authoring(기본). corridor 폭·agentMask 정밀 튜닝은 후속(전부 -1=모든 몹, Walk).</summary>
        public static GraphPathfinder CreateArena()
        {
            var nodes = new[]
            {
                N(0, 0f,0f,0f, 0),      N(1, 14f,0f,-6f, 0),    N(2, -14f,0f,6f, 0),
                N(3, 10f,0f,-2f, 0),    N(4, -10f,0f,2f, 0),    N(5, -6f,0f,17f, 0),
                N(6, 6f,0f,-17f, 0),    N(7, 10f,4.5f,6f, 2),   N(8, 10f,4.5f,18f, 2),
                N(9, -10f,4.5f,-6f, 2), N(10, -10f,4.5f,-18f, 2),
                N(11, 18f,9f,20f, 3),   N(12, -18f,9f,-20f, 3), N(13, 0f,9f,22f, 3),
                N(14, -14f,3f,17f, 1),  N(15, 14f,3f,-17f, 1),  N(16, 0f,0f,20f, 0),
            };
            var links = new List<ArenaNavLink>();
            int id = 0;
            void W(int a, int b)
            {
                links.Add(new ArenaNavLink { linkId = id++, fromNodeId = a, toNodeId = b, traversalType = NavTraversalType.Walk, traversalTicks = 30, agentMask = -1 });
                links.Add(new ArenaNavLink { linkId = id++, fromNodeId = b, toNodeId = a, traversalType = NavTraversalType.Walk, traversalTicks = 30, agentMask = -1 });
            }
            W(0,1); W(0,2); W(0,3); W(0,4); W(0,5); W(0,6); W(1,3); W(2,4);   // 1F
            W(3,7); W(4,9);            // 1F→2F 경사로
            W(7,8); W(9,10);           // 2F
            W(8,11); W(10,12);         // 2F→3F 경사로
            W(11,13); W(12,13);        // 3F 다리
            W(5,14); W(6,15);          // 1F→반층 경사로
            W(0,16); W(5,16);          // 1F 북
            links.Add(new ArenaNavLink { linkId = id++, fromNodeId = 13, toNodeId = 16,
                traversalType = NavTraversalType.DropDown, traversalTicks = 30, agentMask = -1,
                landingPosition = nodes[16].position, landingSlotCount = 1 });   // 다리→1F북 절벽
            return FromBake(new ArenaMapBake { nodes = nodes, links = links.ToArray() });
        }

        static ArenaNavNode N(int id,float x,float y,float z,int floor) => new ArenaNavNode { nodeId=id, position=new Vector3(x,y,z), floorId=floor, areaFlags=MapAreaFlags.Playable };
        static ArenaNavLink L(int id,int a,int b,NavTraversalType t) => new ArenaNavLink { linkId=id, fromNodeId=a, toNodeId=b, traversalType=t, traversalTicks=30, agentMask=-1, landingPosition=default };
    }
}
