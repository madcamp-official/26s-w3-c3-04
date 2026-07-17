using UnityEngine;

namespace Game.Sim
{
    /// <summary>플레이어 논리 상태. pos = 발밑(캡슐 바닥 중심).</summary>
    public struct PlayerSim
    {
        public Vector3 pos;
        public Vector3 vel;
        public float   yaw;
        public bool    grounded;
        public int     jumpCount;

        // 질풍참
        public int     dashTicks;      // >0이면 돌진 중
        public Vector3 dashDir;
        public int     dashCharges;
        public int     dashRecharge;   // 다음 충전까지 남은 틱

        public static PlayerSim Spawn(Vector3 at) => new PlayerSim
        {
            pos = at,
            grounded = true,
            dashCharges = SimConfig.DashMaxCharges,
        };
    }
}
