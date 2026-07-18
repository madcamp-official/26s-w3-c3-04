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

    /// <summary>
    /// 경로 질의. 실제 계산은 Bridge(NavMesh.CalculatePath)가 한다.
    /// 하강 판단은 우리(Sim)가 한다 — 걷는 길 길이 vs 점프 길 길이 비교.
    /// </summary>
    public interface IPathfinder
    {
        /// <summary>from→to 경로의 다음 코너. 경로 없으면 false.</summary>
        bool NextCorner(Vector3 from, Vector3 to, out Vector3 next);

        /// <summary>from→to 경로 전체 길이(코너 합산). 도달 불가면 -1.</summary>
        float PathLength(Vector3 from, Vector3 to);

        /// <summary>from에서 가장 가까운 하강 테두리와 그 착지점. 없으면 false.</summary>
        bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing);
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
