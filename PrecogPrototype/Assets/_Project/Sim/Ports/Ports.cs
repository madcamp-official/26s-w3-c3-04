using UnityEngine;

namespace Game.Sim
{
    /// <summary>충돌 질의 결과.</summary>
    public struct CastHit
    {
        public bool    hit;
        public float   distance;
        public Vector3 normal;
    }

    /// <summary>
    /// 지형 충돌 질의. Sim은 "이 캡슐이 이쪽으로 가면 막히나?"만 묻고,
    /// 실제 계산은 Bridge(유니티 Physics)가 한다. 정적 지형이라 결정론적.
    /// </summary>
    public interface ICollision
    {
        /// <summary>캡슐(bottom~top 구 중심, 반지름 r)을 dir로 maxDist 쐈을 때 첫 충돌.</summary>
        CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist);

        /// <summary>origin에서 dir로 maxDist 쏜 얇은 레이의 첫 충돌(질풍참 조준 등).</summary>
        CastHit Raycast(Vector3 origin, Vector3 dir, float maxDist);

        /// <summary>feet 아래 지면 높이. 없으면 false.</summary>
        bool SampleGround(Vector3 feet, float maxDown, out float groundY);

        // >>> [예측 세션 추가, 2026-07-18] 아래 두 메서드는 인터페이스에 원래 없었다.
        // Bridge/PhysicsCollision.cs와 Prediction 쪽 스텁 구현체엔 이미 같은 이름의 메서드가
        // 있었어서(양쪽이 각자 만들어놨던 것) 시그니처만 여기 인터페이스에 다시 선언한
        // 추가다 — 기존 구현을 바꾸지 않는 순수 추가라 위험 낮음. 되돌리려면 이 두 줄과
        // ActionGenerator.cs의 호출부만 정리하면 된다.
        /// <summary>from에서 to가 지형에 가리지 않고 보이는지(예측 런지 타겟팅용).</summary>
        bool HasLineOfSight(Vector3 from, Vector3 to);

        /// <summary>이 위치에 이 캡슐이 지형과 겹치지 않고 들어갈 수 있는지(예측 런지 착지점 검증용).</summary>
        bool CanOccupyCapsule(Vector3 feet, float radius, float height);
        // <<< [예측 세션 추가 끝]
    }

    /// <summary>다음 스텝의 이동 종류. Walk=지상 걷기, Jump=절벽 낙하(off-mesh link), None=경로 없음.</summary>
    public enum MoveKind : byte { None = 0, Walk = 1, Jump = 2 }

    /// <summary>from→to 경로의 다음 스텝(다음 코너 + 이동 종류).</summary>
    public struct PathStep
    {
        public MoveKind kind;
        public Vector3  next;   // 향할 다음 코너(경로 없으면 from/to 그대로)
    }

    /// <summary>
    /// 경로 질의. NavMesh 런타임 길찾기(연속 메시). 절벽 낙하는 off-mesh link를 큰 낙차로 감지해
    /// kind=Jump로 알린다(경사로는 Walk). 정적 지형이라 같은 입력→같은 경로(예측·현실 일치).
    /// </summary>
    public interface IPathfinder
    {
        /// <summary>from→to 경로의 다음 스텝(다음 코너 + 이동 종류). 경로 없으면 kind=None.</summary>
        PathStep NextStep(Vector3 from, Vector3 to);

        /// <summary>
        /// pos를 걷기 가능 표면으로 당긴 위치(navmesh)를 준다 — 얇은 다리 낙하 방지용.
        /// maxDist 안에 걷기 가능 표면이 없으면 false(그대로 둠). 그래프 모드는 미지원(false).
        /// </summary>
        bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh);
    }

    /// <summary>Sim이 한 틱 도는 데 필요한 바깥 서비스 묶음.</summary>
    public readonly struct SimServices
    {
        public readonly ICollision  Collision;
        public readonly IPathfinder Pathfinder;
        public SimServices(ICollision collision, IPathfinder pathfinder)
        {
            Collision = collision;
            Pathfinder = pathfinder;
        }
    }
}
