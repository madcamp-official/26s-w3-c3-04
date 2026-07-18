using UnityEngine;

namespace Game.Sim
{
    public enum EnemyAIState : byte
    {
        Idle,
        Approach,
        AttackWindup,
        AttackActive,
        AttackRecovery,
        Stunned,
        Traversal,
        LandingRecovery,
        Dead
    }

    /// <summary>테두리 하강 4단계.</summary>
    public enum DescentPhase : byte
    {
        None = 0,
        EdgePause = 1,   // 테두리에서 멈칫 (점프 예고)
        Airborne = 2,    // 포물선 낙하
        Recovery = 3,    // 착지 후 자세 추스림
    }

    /// <summary>
    /// 적 논리 상태. 뼈대 단계에선 "플레이어를 길찾기로 쫓아옴 + 테두리 점프 하강"만 한다.
    /// 전투(HP·스턴·공격)는 다음 단계라 여기 없다.
    /// 경로는 통째로 저장하지 않고 "다음 코너 하나"만 들고 주기적으로 재계산한다.
    /// </summary>
    public struct EnemySim
    {
        public int     id;
        public bool    alive;
        public Vector3 pos;
        public Vector3 vel;
        public float   yaw;
        public bool    grounded;
        public int     enemyTypeId;
        public EnemyAIState aiState;
        public int     stateTicks;
        public int     attackCooldownTicks;
        public Vector3 committedAttackDirection;
        public int     currentNavNodeId;
        public int     destinationNavNodeId;
        public int     nextNavNodeId;
        public int     activeTraversalLinkId;

        public Vector3 waypoint;      // 향하는 다음 경로 코너
        public bool    hasWaypoint;
        public int     repathTicks;   // 재계산까지 남은 틱

        public EnemyCombatState combat;   // ← combat 세션 소유 (health/stun/처치)

        // 테두리 하강
        public DescentPhase descentPhase;
        public int          descentTicks;
        public Vector3      jumpStart, jumpEnd;
        public int          jumpDuration;

        public static EnemySim Spawn(int id, Vector3 at) => new EnemySim
        {
            id = id,
            alive = true,
            pos = at,
            grounded = true,
            aiState = EnemyAIState.Approach,
            currentNavNodeId = -1,
            destinationNavNodeId = -1,
            nextNavNodeId = -1,
            activeTraversalLinkId = -1,
            combat = EnemyCombatState.Spawn(SimConfig.EnemyNormalHp),
        };
    }
}
