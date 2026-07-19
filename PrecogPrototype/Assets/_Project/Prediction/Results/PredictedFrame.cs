using UnityEngine;

namespace Game.Prediction
{
    /// <summary>
    /// 최종 후보를 최초 스냅샷에서 60Hz(매 틱)로 정밀 재실행해 얻은 프레임 1개.
    /// docs/shared/PREDICTION_CONTRACT.md 11장·3.1.1절 — View의 경로선·잔상은 이 배열에서만
    /// 샘플링한다(View가 직접 보간·생성하지 않는다).
    /// </summary>
    public struct PredictedFrame
    {
        public int tick;
        public Vector3 playerPosition;
        public float playerYaw;
        public bool playerAlive;
        public int floorId;
    }

    /// <summary>계약 3.1.1절 잔상 액션 아이콘 종류.</summary>
    public enum PredictedActionType : byte
    {
        Jump,
        DashForward,
        DashBackward,
        DashLeft,
        DashRight,
        Attack,
        Lunge
    }

    /// <summary>
    /// 매크로 행동이 아니라 실제 Sim 상태머신에서 행동이 "시작된" 정확한 틱.
    /// 리듬 판정(Perfect/Good/Miss, 계약 3.2절)은 이 이벤트의 tick만 기준으로 삼는다.
    /// </summary>
    public struct PredictedActionEvent
    {
        public int tick;
        public PredictedActionType type;

        /// <summary>Lunge 전용 대상 적 id. 그 외는 -1.</summary>
        public int targetId;
    }
}
