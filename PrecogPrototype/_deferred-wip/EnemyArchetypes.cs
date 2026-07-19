using UnityEngine;

namespace Game.Sim
{
    public enum EnemyArchetypeId : byte
    {
        GroundMelee,
        GroundRanged,
        ChargeMelee,
        TraversalMelee,
        TraversalRanged,
        FlyingRanged
    }

    public enum EnemySizeId : byte
    {
        Grunt,
        Large,
        Elite,
        Boss
    }

    public enum EnemyThreatType : byte
    {
        None,
        MeleeArc,
        Projectile,
        ChargeCorridor,
        TraversalAirborne
    }

    public enum ChargePhase : byte
    {
        None,
        Windup,
        Travel,
        Recovery
    }

    public enum TraversalPhase : byte
    {
        None,
        Pause,
        Airborne,
        Recovery
    }

    public enum FlightAvoidanceState : byte
    {
        None,
        AvoidObstacle,
        RecoverAltitude
    }

    /// <summary>
    /// 몹별 수치를 담는 데이터 스키마. 미승인 수치를 Sim 행동으로 하드코딩하지 않기 위해
    /// 현재는 데이터 컨테이너와 보수적인 근접 기본값만 제공한다.
    /// </summary>
    public struct EnemyArchetypeConfig
    {
        public EnemyArchetypeId archetypeId;
        public EnemySizeId sizeId;
        public int maxHealth;
        public float moveSpeed;
        public float collisionRadius;
        public float collisionHeight;
        public float aggroRange;
        public float preferredMinRange;
        public float preferredMaxRange;
        public int aimTicks;
        public int attackCooldownTicks;
        public int projectileTypeId;
        public int agentMask;
        public bool canUseDrop;
        public bool canUseBoost;
        public bool canUseJumpUp;
        public bool canFly;

        public static EnemyArchetypeConfig GroundMeleeDefault => new EnemyArchetypeConfig
        {
            archetypeId = EnemyArchetypeId.GroundMelee,
            sizeId = EnemySizeId.Grunt,
            maxHealth = SimConfig.EnemyNormalHp,
            moveSpeed = SimConfig.EnemyMoveSpeed,
            collisionRadius = SimConfig.EnemyRadius,
            collisionHeight = SimConfig.EnemyHeight,
            aggroRange = SimConfig.EnemyAggroRange,
            attackCooldownTicks = CombatConfig.EnemyAttackCooldownTicks,
            canUseDrop = true
        };
    }
}
