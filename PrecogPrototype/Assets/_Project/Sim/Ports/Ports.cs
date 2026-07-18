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
    }

    /// <summary>
    /// 경로 질의. 그래프 사전계산표 조회(예측 친화). 하강/부스터는 링크 종류(MoveKind)로 표현 —
    /// 자체 판단 없이 최단경로가 Jump/Boost 링크를 태우면 몹이 그 기동을 실행한다.
    /// (레거시 씬 모드는 NavMesh 어댑터가 Walk만 반환.)
    /// </summary>
    public interface IPathfinder
    {
        /// <summary>from→to 경로의 다음 스텝(다음 노드 + 이동 종류). 경로 없으면 kind=None.</summary>
        PathStep NextStep(Vector3 from, Vector3 to);
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
