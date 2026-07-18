using UnityEngine;

namespace Game.Sim
{
    /// <summary>전투 판정 헬퍼(구·부채꼴·선분). 좌표 계산만 — 결정론적, 빠름. ★ combat 소유.</summary>
    public static class CombatHit
    {
        /// <summary>부채꼴 안인가: 수평 거리 + 정면 각도.</summary>
        public static bool InCone(Vector3 origin, float yaw, Vector3 target, float range, float halfAngleDeg)
        {
            Vector3 to = target - origin; to.y = 0f;
            if (to.sqrMagnitude > range * range) return false;
            Vector3 fwd = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
            to.Normalize();
            return Vector3.Dot(fwd, to) >= Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
        }

        /// <summary>선분(돌진 경로) vs 구 (질풍참 관통용 — Phase 2).</summary>
        public static bool SegmentHitsSphere(Vector3 p0, Vector3 p1, Vector3 c, float radius)
        {
            Vector3 d = p1 - p0;
            float len2 = d.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(c - p0, d) / len2) : 0f;
            Vector3 closest = p0 + t * d;
            return (c - closest).sqrMagnitude <= radius * radius;
        }
    }
}
