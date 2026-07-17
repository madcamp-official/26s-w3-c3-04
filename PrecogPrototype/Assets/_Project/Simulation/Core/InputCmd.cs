using UnityEngine;

namespace Game.Simulation
{
    /// <summary>
    /// 한 틱분의 플레이어 입력. 시뮬레이션이 아는 유일한 입력 형태.
    /// 표현 계층(Runtime)이 키보드/마우스를 읽어 이걸 채워서 넘긴다.
    /// 예측은 이 구조를 직접 만들어 가상 미래를 굴린다.
    /// </summary>
    public struct InputCmd
    {
        public Vector2 move;      // (x=좌우, y=전후), -1..1
        public float   yaw;       // 바라보는 방향 (도)

        public bool jump;
        public bool dash;         // 질풍참 (묶음 B)
        public bool attack;       // 평타 (묶음 B)
        public bool block;        // 막기 (묶음 B)

        public int attackTargetId; // 보정 대상 (묶음 B). 없으면 -1

        public static InputCmd Empty => new InputCmd { attackTargetId = -1 };
    }
}
