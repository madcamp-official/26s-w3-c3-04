using UnityEngine;

namespace Game.Simulation
{
    /// <summary>
    /// 구(sphere) 기반 판정. Unity 물리를 쓰지 않고 좌표로 직접 계산한다 →
    /// 예측이 가상 세계에서도 똑같이 굴릴 수 있고, 가장 빠르다.
    /// 한방컷 게임이라 "스쳤나 안 스쳤나"만 중요하고 정밀도는 불필요.
    /// </summary>
    public static class Hit
    {
        /// <summary>두 구가 겹치는가. sqrMagnitude로 제곱근을 피한다.</summary>
        public static bool SphereOverlap(Vector3 a, float ra, Vector3 b, float rb)
        {
            float rr = ra + rb;
            return (a - b).sqrMagnitude <= rr * rr;
        }

        /// <summary>수평(XZ) 거리 제곱. 높이 차 무시.</summary>
        public static float FlatSqrDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>부채꼴 판정 (평타 광역용, 묶음 B). 거리 + 정면 각도.</summary>
        public static bool InCone(Vector3 origin, Vector3 forward, Vector3 target,
                                  float range, float halfAngleDeg)
        {
            Vector3 to = target - origin;
            to.y = 0f;
            if (to.sqrMagnitude > range * range) return false;

            Vector3 fwd = forward; fwd.y = 0f;
            fwd.Normalize();
            to.Normalize();
            float dot = Vector3.Dot(fwd, to);
            float cosHalf = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
            return dot >= cosHalf;
        }

        /// <summary>선분(질풍참 경로) vs 구. 점-선분 최단거리로 판정 (묶음 B).</summary>
        public static bool SegmentHitsSphere(Vector3 p0, Vector3 p1,
                                             Vector3 center, float radius)
        {
            Vector3 d = p1 - p0;
            float len2 = d.sqrMagnitude;
            float t = len2 > 1e-6f ? Vector3.Dot(center - p0, d) / len2 : 0f;
            t = Mathf.Clamp01(t);
            Vector3 closest = p0 + t * d;
            return (center - closest).sqrMagnitude <= radius * radius;
        }
    }
}
