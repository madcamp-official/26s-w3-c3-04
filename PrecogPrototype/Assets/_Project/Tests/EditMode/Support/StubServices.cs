using UnityEngine;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// 평지·벽 없음 가정의 최소 ICollision. 실제 지형 충돌을 검증하는 용도가 아니라,
    /// Sim/Prediction 로직 자체의 결정론을 씬·Physics 없이 EditMode에서 확인하기 위한 스텁이다.
    /// </summary>
    public sealed class StubCollision : ICollision
    {
        public CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist)
            => default; // 항상 안 막힘

        public bool SampleGround(Vector3 feet, float maxDown, out float groundY)
        {
            groundY = 0f;
            return true; // 바닥은 어디서나 y=0
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to) => true;

        public bool CanOccupyCapsule(Vector3 feet, float radius, float height) => true;
    }

    /// <summary>from→to 직선만 반환하는 최소 IPathfinder. 정적 그래프 대신 테스트용.</summary>
    public sealed class StubPathfinder : IPathfinder
    {
        public PathStep NextStep(Vector3 from, Vector3 to) => new PathStep
        {
            kind = MoveKind.Walk,
            next = to,
            currentNodeId = 0,
            destinationNodeId = 0,
            nextNodeId = 0,
            linkId = -1
        };
    }

    public static class StubServices
    {
        public static SimServices Create() => new SimServices(new StubCollision(), new StubPathfinder());
    }
}
