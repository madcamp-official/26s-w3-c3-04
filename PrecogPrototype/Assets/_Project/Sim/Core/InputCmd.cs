using UnityEngine;

namespace Game.Sim
{
    /// <summary>한 틱분 플레이어 입력. 시뮬레이션이 아는 유일한 입력 형태.</summary>
    public struct InputCmd
    {
        public Vector2 move;   // (x=좌우, y=전후), -1..1
        public float   yaw;    // 바라보는 방향(도, 수평)
        public float   pitch;  // 올려다/내려봄(도) — 질풍참을 카메라 방향으로 쏘는 데 사용
        public bool    jump;
        public bool    dash;   // 질풍참
        public bool    attack; // 좌클릭 (평타 / 막기중=칼등치기) — combat 세션
        public bool    block;  // 우클릭 홀드 (막기) — combat 세션

        public static InputCmd Empty => default;
    }
}
