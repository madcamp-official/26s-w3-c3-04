using UnityEngine;

namespace Game.Sim
{
    public enum PlayerActionPhase : byte
    {
        None,
        AttackWindup,
        AttackActive,
        AttackRecovery,
        LungeWindup,
        LungeTravel,
        LungeRecovery
    }

    public struct PlayerCombatState
    {
        public PlayerActionPhase phase;
        public int phaseTicks;
        public int attackSequence;
        public int lungeCooldownTicks;
        public int lungeTargetId;
        public Vector3 lungeStart;
        public Vector3 lungeDestination;
        public int lungeElapsedTicks;

        public static PlayerCombatState Initial => new PlayerCombatState { lungeTargetId = -1 };
    }
}
