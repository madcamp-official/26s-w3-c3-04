using UnityEngine;

namespace Game.Simulation
{
    /// <summary>평타 공격 단계.</summary>
    public enum AttackPhase : byte
    {
        None = 0,
        Windup = 1,
        Active = 2,
        Recovery = 3,
    }

    /// <summary>
    /// 플레이어의 모든 논리 상태. Transform이 아니라 여기가 "진짜".
    /// 참조 타입 필드가 없어야 통째로 값 복사(스냅샷)가 된다.
    /// </summary>
    public struct PlayerState
    {
        public Vector3 pos;
        public Vector3 vel;
        public float   yaw;
        public bool    grounded;
        public int     jumpCount;   // 더블점프용 (0,1,2)
        public bool    alive;
        public int     hp;

        // 평타
        public AttackPhase attackPhase;
        public int         attackPhaseTicks;
        public int         attackCooldownTicks;
        public int         iFrameTicks;        // 보정 중 짧은 무적 (관통 방지)
        public Vector3     lungeDir;           // 보정 전진 방향

        // 질풍참
        public int     dashCharges;            // 남은 충전 (0..2)
        public int     dashRechargeTicks;      // 다음 충전까지 남은 틱
        public int     dashTicksRemaining;     // >0이면 돌진 중
        public Vector3 dashDir;
        public Vector3 dashStartPos;           // 관통 판정용 선분 시작

        // 막기
        public bool blocking;                  // 이번 틱 막기 홀드 중
        public int  guardGauge;                // 가드 게이지 (틱 단위)
        public int  guardIdleTicks;            // 막기 안 한 지 지난 틱 (회복 지연용)

        public static PlayerState Spawn(Vector3 at) => new PlayerState
        {
            pos = at,
            vel = Vector3.zero,
            yaw = 0f,
            grounded = true,
            jumpCount = 0,
            alive = true,
            hp = SimConfig.PlayerMaxHp,

            attackPhase = AttackPhase.None,
            dashCharges = SimConfig.DashMaxCharges,
            guardGauge  = SimConfig.GuardMaxTicks,
        };

        /// <summary>지금 정면 방어 상태인가 (막기 홀드 중이거나 질풍참 돌진 중).</summary>
        public bool IsGuardingFront => (blocking && guardGauge > 0) || dashTicksRemaining > 0;
    }
}
