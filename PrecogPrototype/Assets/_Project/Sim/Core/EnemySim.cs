using UnityEngine;

namespace Game.Sim
{
    /// <summary>절벽 도약: off-mesh link를 태우면 가장자리에서 착지점까지 스크립트 포물선으로 뛰어내린다(둠식 "쿵").</summary>
    public enum DescentPhase : byte
    {
        None = 0,
        Leaping = 1,   // 포물선 도약 중(시작→착지 보간 + apex). 완료 후 짧은 착지 홀드 뒤 종료
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

        // 절벽 도약 (스크립트 포물선)
        public DescentPhase descentPhase;
        public int          descentTicks;    // 도약 진행 틱(0→Duration→+Hold)
        public Vector3      descentStart;     // 도약 시작점(가장자리)
        public Vector3      descentLanding;   // off-mesh link 착지점

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

        /// <summary>점유 중인 착지 슬롯 번호(-1 = 없음). 링크 정원·겹침 방지 판정을 이 값 훑기로 한다.</summary>
        public int traversalSlot;

        /// <summary>이번 도약의 주저·멈칫 틱(도약 시작 시 링크에서 복사). repathTicks 등 기존 필드와 의미가 달라 별도로 둔다.</summary>
        public int traversalPauseTicks;
        public int traversalRecoverTicks;

        /// <summary>
        /// 개체 고정 개성값 0~1. ★ 스폰 시 1회 결정, 이후 불변 → 예측 포크에 안전(ADR-0004 개정).
        /// 판단(무엇을 할지)에는 쓰지 않고 "얼마나 세게" 같은 연속값에만 관여한다.
        /// 지금 용도: 분리(뭉치기 방지) 세기 배율 — 전부 같은 가중치라 정면 대칭 진동이 나던 문제 해소.
        /// </summary>
        public float personality;

        public static EnemySim Spawn(int id, Vector3 at, CombatType combat, MobilityType mobility, SizeClass size)
        {
            bool large = size == SizeClass.Large;
            float scale = large ? SimConfig.EnemyLargeScale : SimConfig.EnemyNormalScale;
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
                traversalSlot = -1,
                personality = Personality(id),
            };
        }

        /// <summary>
        /// id → 0~1 고정값. 난수 스트림이 아니라 <b>순수 해시</b>라 상태가 없고 포크에 안전하다.
        /// (ADR-0004 개정: 판단 시 난수는 금지, 스폰 시 고정 상수는 허용.)
        /// </summary>
        public static float Personality(int id)
        {
            unchecked
            {
                uint x = (uint)id * 2654435761u;   // Knuth 승수
                x ^= x >> 15; x *= 2246822519u;
                x ^= x >> 13; x *= 3266489917u;
                x ^= x >> 16;
                return (x & 0xFFFFu) / 65535f;
            }
        }
    }
}
