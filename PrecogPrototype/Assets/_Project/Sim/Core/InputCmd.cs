using UnityEngine;

namespace Game.Sim
{
    /// <summary>한 틱분 플레이어 입력. 시뮬레이션이 아는 유일한 입력 형태.</summary>
    public struct InputCmd
    {
        public Vector2 move;   // (x=좌우, y=전후), -1..1
        public float   yaw;    // 바라보는 방향(도)
        public bool    jump;
        public bool    dash;   // 질풍참 (이동만)

        public static InputCmd Empty => default;
    }
}
