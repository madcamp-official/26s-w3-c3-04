using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 적 논리 상태. 뼈대 단계에선 "플레이어를 길찾기로 쫓아옴"만 한다.
    /// 전투(HP·스턴·공격)는 다음 단계라 여기 없다.
    /// 경로는 통째로 저장하지 않고 "다음 코너 하나"만 들고 주기적으로 재계산한다.
    /// </summary>
    public struct EnemySim
    {
        public int     id;
        public bool    alive;
        public Vector3 pos;
        public Vector3 vel;
        public float   yaw;
        public bool    grounded;

        public Vector3 waypoint;      // 향하는 다음 경로 코너
        public bool    hasWaypoint;
        public int     repathTicks;   // 재계산까지 남은 틱

        public static EnemySim Spawn(int id, Vector3 at) => new EnemySim
        {
            id = id,
            alive = true,
            pos = at,
            grounded = true,
        };
    }
}
