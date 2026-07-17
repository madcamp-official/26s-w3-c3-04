using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 캐릭터 이동의 공통 뼈대. 벽 충돌은 ICollision(유니티 CapsuleCast)에 묻고,
    /// "막히면 벽면을 따라 미끄러진다"는 슬라이드 로직만 여기서 한다.
    /// 물리엔진을 짜는 게 아니라 질의를 조합하는 것.
    /// </summary>
    public static class CharacterMotor
    {
        const float Skin = 0.03f;

        /// <summary>수평 이동 + 벽 슬라이드(최대 3회). 새 위치 반환.</summary>
        public static Vector3 MoveHorizontal(ICollision col, Vector3 pos, Vector3 delta,
                                             float radius, float height)
        {
            delta.y = 0f;
            for (int iter = 0; iter < 3 && delta.sqrMagnitude > 1e-8f; iter++)
            {
                float dist = delta.magnitude;
                Vector3 dir = delta / dist;
                Capsule(pos, radius, height, out var b, out var t);
                CastHit hit = col.CapsuleCast(b, t, radius, dir, dist + Skin);

                // 안 막히거나, 걸을 수 있는 경사(윗면)면 그대로 진행 → 높이는 지면 스냅이 처리
                if (!hit.hit || hit.normal.y > 0.6f) { pos += delta; break; }

                float travel = Mathf.Max(0f, hit.distance - Skin);
                pos += dir * travel;

                Vector3 remaining = dir * (dist - travel);
                Vector3 n = hit.normal; n.y = 0f;
                if (n.sqrMagnitude < 1e-6f) break;
                n.Normalize();
                delta = remaining - Vector3.Dot(remaining, n) * n;  // 벽면에 투영 = 슬라이드
            }
            return pos;
        }

        /// <summary>중력 적용 + 낙하/착지. grounded 갱신.</summary>
        public static void ResolveVertical(ICollision col, ref Vector3 pos, ref Vector3 vel,
                                           float dt, out bool grounded)
        {
            vel.y += SimConfig.Gravity * dt;
            pos.y += vel.y * dt;

            if (col.SampleGround(pos, 100f, out float gy) && pos.y <= gy + 0.02f)
            {
                pos.y = gy;
                vel.y = 0f;
                grounded = true;
            }
            else grounded = false;
        }

        public static void Capsule(Vector3 feet, float radius, float height,
                                   out Vector3 bottom, out Vector3 top)
        {
            bottom = feet + Vector3.up * radius;
            top    = feet + Vector3.up * (height - radius);
        }
    }
}
