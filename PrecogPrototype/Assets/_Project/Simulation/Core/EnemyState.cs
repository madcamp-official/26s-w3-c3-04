using UnityEngine;

namespace Game.Simulation
{
    public enum EnemyType : byte
    {
        Melee = 0,   // 일반 근접 (묶음 A)
        Ranged = 1,  // 원거리 투사체 (묶음 C)
    }

    /// <summary>적 AI 상태 머신. 난수 없음 (ADR-0004).</summary>
    public enum AiState : byte
    {
        Idle = 0,
        Chase = 1,
        Windup = 2,   // 공격 선딜
        Active = 3,   // 공격 판정
        Recovery = 4, // 공격 후딜
    }

    /// <summary>
    /// 적 하나의 논리 상태. health 필드가 없다 — 전부 한방컷 (ADR-0002).
    /// alive 플래그만으로 생존/사망을 표현한다.
    /// </summary>
    public struct EnemyState
    {
        public int       id;
        public EnemyType type;
        public bool      alive;

        public Vector3 pos;
        public Vector3 vel;
        public float   yaw;

        public AiState aiState;
        public int     stateTicks;         // 현재 상태에서 지난 틱 (또는 남은 틱)
        public int     attackCooldownTicks;
        public int     stunTicks;          // 질풍참 경직 (ADR-0005). >0이면 AI 정지

        public static EnemyState SpawnMelee(int id, Vector3 at) => new EnemyState
        {
            id = id,
            type = EnemyType.Melee,
            alive = true,
            pos = at,
            vel = Vector3.zero,
            yaw = 0f,
            aiState = AiState.Idle,
            stateTicks = 0,
            attackCooldownTicks = 0,
            stunTicks = 0,
        };
    }
}
