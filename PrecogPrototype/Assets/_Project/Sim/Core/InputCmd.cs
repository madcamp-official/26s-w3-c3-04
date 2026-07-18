using UnityEngine;

namespace Game.Sim
{
    public enum DashDirection : byte
    {
        Forward,
        Backward,
        Left,
        Right
    }

    /// <summary>한 틱분 플레이어 입력. 시뮬레이션이 아는 유일한 입력 형태.</summary>
    public struct InputCmd
    {
        public Vector2 move;   // (x=좌우, y=전후), -1..1
        public float   yaw;    // 바라보는 방향(도)
        public bool    jump;
        public bool    dash;
        public DashDirection dashDirection;
        public bool    attack;
        public bool    lunge;
        public int     lungeTargetId;

        public static InputCmd Empty => default;
    }
}
