using UnityEngine;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// ICollision 구현 = 유니티 정적 충돌 질의. 물리엔진을 짜는 게 아니라 CapsuleCast/Raycast를 부른다.
    /// 정적 지형이라 같은 입력 → 같은 결과(결정론). 예측이 복제 상태로 물어도 안전.
    /// </summary>
    public class PhysicsCollision : ICollision
    {
        readonly int mask;

        public PhysicsCollision(int layerMask) { mask = layerMask; }

        public CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist)
        {
            if (Physics.CapsuleCast(bottom, top, radius, dir, out var hit, maxDist,
                                    mask, QueryTriggerInteraction.Ignore))
                return new CastHit { hit = true, distance = hit.distance, normal = hit.normal };
            return default;
        }

        public bool SampleGround(Vector3 feet, float maxDown, out float groundY)
        {
            Vector3 origin = feet + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, maxDown + 0.5f,
                                mask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }
            groundY = 0f;
            return false;
        }

        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 1e-5f) return true;
            return !Physics.Raycast(from, delta / distance, distance, mask, QueryTriggerInteraction.Ignore);
        }

        public bool CanOccupyCapsule(Vector3 feet, float radius, float height)
        {
            CharacterMotor.Capsule(feet, radius, height, out Vector3 bottom, out Vector3 top);
            return !Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
