using System;
using UnityEngine;

namespace Game.Sim
{
    [Flags]
    public enum MapAreaFlags : ushort
    {
        None = 0,
        Playable = 1 << 0,
        Blocked = 1 << 1,
        LungeForbidden = 1 << 2,
        FallHazard = 1 << 3,
        EscapeCandidate = 1 << 4,
    }

    public enum NavTraversalType : byte
    {
        Walk, RampUp, RampDown, StairUp, StairDown, JumpUp, BoostUp, DropDown, Blocked
    }

    [Serializable]
    public struct ArenaNavNode
    {
        public int nodeId;
        public Vector3 position;
        public int floorId;
        public MapAreaFlags areaFlags;
    }

    [Serializable]
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
        public int landingSlotCount;
        public float landingSpread;
    }

    [Serializable]
    public sealed class ArenaMapBake
    {
        public int mapVersion = 1;
        public ArenaNavNode[] nodes = Array.Empty<ArenaNavNode>();
        public ArenaNavLink[] links = Array.Empty<ArenaNavLink>();
    }
}
