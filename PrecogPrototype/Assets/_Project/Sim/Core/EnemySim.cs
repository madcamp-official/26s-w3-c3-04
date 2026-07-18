using UnityEngine;

namespace Game.Sim
{
    /// <summary>절벽 낙하: off-mesh link를 태우면 착지점을 향해 걸어 나가 중력으로 떨어진다(순간이동 아님).</summary>
    public enum DescentPhase : byte
    {
        None = 0,
        Falling = 1,   // 착지점 XZ로 이동 + 중력 낙하 중 (착지하면 종료)
    }

    public enum TraversalPhase : byte { None, Pause, Airborne, Recovery }

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

        public Vector3 waypoint;      // 향하는 다음 경로 코너(NavMesh)
        public bool    hasWaypoint;
        public int     repathTicks;   // 재계산까지 남은 틱

        public EnemyCombatState combat;   // ← combat 세션 소유 (health/stun/처치)
        public EnemyAI          ai;       // ← AI 세션 소유 (상태머신/아키타입)

        // 절벽 낙하 (자연 낙하)
        public DescentPhase descentPhase;
        public int          descentTicks;    // 낙하 안전장치(무한 방지)
        public Vector3      descentLanding;   // off-mesh link 착지점(향해 걸어 나가며 떨어짐)

        // 고정 유향 그래프 및 Drop/Boost 공용 실행 상태.
        public int currentNavNodeId;
        public int destinationNavNodeId;
        public int nextNavNodeId;
        public int activeTraversalLinkId;
        public int currentFloorId;
        public TraversalPhase traversalPhase;
        public MoveKind activeMoveKind;
        public int traversalTicks;
        public int jumpDuration;
        public Vector3 jumpStart;
        public Vector3 jumpEnd;

        public static EnemySim Spawn(int id, Vector3 at, CombatType combat, MobilityType mobility, SizeClass size)
        {
            bool large = size == SizeClass.Large;
            float scale = large ? SimConfig.EnemyLargeScale : 1f;
            int hp = large ? SimConfig.EnemyLargeHp : SimConfig.EnemyNormalHp;
            float radiusMul = mobility == MobilityType.Charge ? AIConfig.ChargeRadiusMul : 1f;   // 돌진몹 반경 1.5배(높이는 그대로)
            return new EnemySim
            {
                id = id,
                alive = true,
                pos = at,
                grounded = true,
                radius = SimConfig.EnemyRadius * scale * radiusMul,
                height = SimConfig.EnemyHeight * scale,
                combat = EnemyCombatState.Spawn(hp),
                ai = EnemyAI.Spawn(combat, mobility, size),
            };
        }
    }
}
