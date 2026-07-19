using UnityEngine;

namespace Game.Sim
{
    public struct CastHit
    {
        public bool hit;
        public float distance;
        public Vector3 normal;
    }

    public interface ICollision
    {
        CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist);
        CastHit Raycast(Vector3 origin, Vector3 dir, float maxDist);
        bool SampleGround(Vector3 feet, float maxDown, out float groundY);
        bool HasLineOfSight(Vector3 from, Vector3 to);
        bool CanOccupyCapsule(Vector3 feet, float radius, float height);
    }

    public enum MoveKind : byte { None, Walk, JumpUp, Drop, Boost }

    public struct PathStep
    {
        public MoveKind kind;
        public Vector3 traversalStart;
        public Vector3 next;
        public int currentNodeId;
        public int nextNodeId;
        public int destinationNodeId;
        public int linkId;
        public int floorId;
        public int destinationFloorId;
        public int traversalTicks;
    }

    public interface IPathfinder
    {
        PathStep NextStep(Vector3 from, Vector3 to, int agentMask);
        int FloorIdAt(Vector3 position);

        /// <summary>런타임 NavMesh 안전망. 고정 그래프 구현은 false를 반환한다.</summary>
        bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh);
    }

    public readonly struct SimServices
    {
        public readonly ICollision Collision;
        public readonly IPathfinder Pathfinder;
        public SimServices(ICollision collision, IPathfinder pathfinder)
        {
            Collision = collision;
            Pathfinder = pathfinder;
        }
    }
}
