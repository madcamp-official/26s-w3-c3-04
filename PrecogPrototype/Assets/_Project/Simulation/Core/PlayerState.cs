using UnityEngine;

namespace Game.Simulation
{
    /// <summary>
    /// 플레이어의 모든 논리 상태. Transform이 아니라 여기가 "진짜".
    /// 참조 타입 필드가 없어야 통째로 값 복사(스냅샷)가 된다.
    /// </summary>
    public struct PlayerState
    {
        public Vector3 pos;
        public Vector3 vel;
        public float   yaw;
        public bool    grounded;
        public int     jumpCount;   // 더블점프용 (0,1,2)
        public bool    alive;
        public int     hp;

        // 질풍참/평타/막기 상태는 묶음 B에서 추가.

        public static PlayerState Spawn(Vector3 at) => new PlayerState
        {
            pos = at,
            vel = Vector3.zero,
            yaw = 0f,
            grounded = true,
            jumpCount = 0,
            alive = true,
            hp = SimConfig.PlayerMaxHp,
        };
    }
}
