using UnityEngine;

namespace Game.Sim
{
    /// <summary>테두리 하강 (순간이동식): 테두리로 이동 → 멈칫 → 순간이동 → 회복.</summary>
    public enum DescentPhase : byte
    {
        None = 0,
        ApproachEdge = 1,  // 절벽 테두리로 이동
        EdgePause = 2,     // 테두리에서 멈칫 (점프 예고)
        Recovery = 3,      // 착지 후 자세 추스림 (순간이동은 EdgePause→Recovery 전환 때)
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

        // 개별 크기(대형몹은 3배). 이동·분리·판정·뷰가 이 값을 쓴다.
        public float   radius;
        public float   height;

        public Vector3 waypoint;      // 향하는 다음 경로 코너
        public bool    hasWaypoint;
        public int     repathTicks;   // 재계산까지 남은 틱

        public EnemyCombatState combat;   // ← combat 세션 소유 (health/stun/처치)
        public EnemyAI          ai;       // ← AI 세션 소유 (상태머신/아키타입)

        // 테두리 하강 (순간이동식)
        public DescentPhase descentPhase;
        public int          descentTicks;
        public Vector3      descentEdge;     // 걸어갈 절벽 테두리
        public Vector3      descentLanding;  // 순간이동 착지점 (id별 분산 포함)

        public static EnemySim Spawn(int id, Vector3 at, Archetype archetype)
        {
            bool large = archetype == Archetype.LargeMelee;
            float scale = large ? SimConfig.EnemyLargeScale : 1f;
            int hp = large ? SimConfig.EnemyLargeHp : SimConfig.EnemyNormalHp;
            return new EnemySim
            {
                id = id,
                alive = true,
                pos = at,
                grounded = true,
                radius = SimConfig.EnemyRadius * scale,
                height = SimConfig.EnemyHeight * scale,
                combat = EnemyCombatState.Spawn(hp),
                ai = EnemyAI.Spawn(archetype),
            };
        }
    }
}
