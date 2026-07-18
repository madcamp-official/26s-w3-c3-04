using UnityEngine;

namespace Game.Sim
{
    [System.Flags]
    public enum MapAreaFlags : ushort
    {
        None = 0,
        Playable = 1 << 0,
        Blocked = 1 << 1,
        LungeForbidden = 1 << 2,
        FallHazard = 1 << 3,
        EscapeCandidate = 1 << 4,
        CoverLow = 1 << 5,
        CoverHigh = 1 << 6,
        ProjectileBlocked = 1 << 7,
        NoFly = 1 << 8
    }

    public enum NavTraversalType : byte
    {
        Walk,
        RampUp,
        RampDown,
        StairUp,
        StairDown,
        JumpUp,
        DropDown,
        BoostUp,
        Fly,
        Blocked
    }

    public struct ArenaNavNode
    {
        public int nodeId;
        public Vector3 position;
        public int floorId;
        public MapAreaFlags areaFlags;
        public int capacity;
        public float coverScore;
    }

    public struct ArenaNavLink
    {
        public int linkId;
        public int fromNodeId;
        public int toNodeId;
        public NavTraversalType traversalType;
        public int traversalTicks;
        public float heightDelta;
        public float dropHeight;
        public int agentMask;
        public Vector3 landingPosition;
        public int landingSlotStart;
        public int landingSlotCount;
    }

    /// <summary>런타임 NavMesh 대신 예측에서 참조할 불변 맵 데이터 스키마.</summary>
    public sealed class ArenaMapBake
    {
        public int mapVersion;
        public ArenaNavNode[] nodes;
        public ArenaNavLink[] links;
        public Vector3[] landingSlots;
        public int[,] nextNodeTable;
        public int[,] distanceTable;
    }
}
