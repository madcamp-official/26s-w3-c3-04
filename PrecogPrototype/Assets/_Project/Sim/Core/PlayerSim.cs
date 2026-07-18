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
        public int     health;
        public bool    alive;
        public int     hitStunTicks;

        // 4방향 대시
        public int     dashTicks;      // >0이면 돌진 중
        public Vector3 dashDir;
        public int     dashCharges;
        public int     dashRecharge;   // 다음 충전까지 남은 틱

        public PlayerCombatState combat;   // ← combat 세션 소유 (필드는 그쪽 파일에서 늘림)

        public static PlayerSim Spawn(Vector3 at) => new PlayerSim
        {
            pos = at,
            grounded = true,
            health = CombatConfig.PlayerMaxHp,
            alive = true,
            dashCharges = SimConfig.DashMaxCharges,
            combat = PlayerCombatState.Initial,
        };
    }
}
