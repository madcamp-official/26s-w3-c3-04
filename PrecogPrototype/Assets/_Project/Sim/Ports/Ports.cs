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
    /// 실제 플레이는 Bridge가 구현한다. 예측에서는 베이크 데이터 기반 구현으로 교체한다.
    /// </summary>
    public interface ICollision
    {
        /// <summary>캡슐(bottom~top 구 중심, 반지름 r)을 dir로 maxDist 쐈을 때 첫 충돌.</summary>
        CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist);

        /// <summary>feet 아래 지면 높이. 없으면 false.</summary>
        bool SampleGround(Vector3 feet, float maxDown, out float groundY);

        bool HasLineOfSight(Vector3 from, Vector3 to);

        bool CanOccupyCapsule(Vector3 feet, float radius, float height);
    }

    /// <summary>다음 이동이 걷기인지, 테두리 점프(NavMesh Link)인지.</summary>
    public enum MoveKind { None, Walk, Jump }

    public struct PathStep
    {
        public MoveKind kind;
        public Vector3 next;
        public int currentNodeId;
        public int destinationNodeId;
        public int nextNodeId;
        public int linkId;
    }

    /// <summary>
    /// 경로 질의. "from에서 to로 가는 경로의 다음 지점?"과 그게 걷기인지 점프인지.
    /// 플레이 씬은 NavMesh 어댑터를 쓸 수 있고, 예측은 고정 그래프 구현을 사용한다.
    /// </summary>
    public interface IPathfinder
    {
        /// <summary>from→to 경로의 다음 코너와 이동 종류. 경로 없으면 None.</summary>
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
