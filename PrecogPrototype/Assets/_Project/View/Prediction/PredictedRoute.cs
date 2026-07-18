using System.Collections.Generic;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    /// <summary>
    /// 연출이 소비하는 루트 데이터. 생성 방식(스텁이든 실제 전투 봇이든)과 무관한 고정 계약.
    /// 지금은 RoutePreviewStub이 채우고, 예측 봇이 준비되면 Sim/Prediction 산출물로 교체한다.
    /// </summary>
    public class PredictedRoute
    {
        public List<Vector3> path  = new List<Vector3>();   // 경로 점(월드 좌표)
        public List<Vector3> kills = new List<Vector3>();   // 처치 위치 마커
        public float seconds;                               // 예상 소요 시간(초)
        public Color color = Color.white;

        /// <summary>계약 3.1.1절 "0.5초(30틱) 간격 정지 잔상"용 샘플 프레임(시작·종료 포함).
        /// RoutePreviewStub은 못 채운다(실제 재시뮬레이션이 있어야 함) — 그때는 비어있다.</summary>
        public List<PredictedFrame> ghostFrames = new List<PredictedFrame>();

        /// <summary>확정 시 실제 플레이어를 자동 재생하기 위한 매 틱 기록 입력. RoutePreviewStub은
        /// 못 채운다 — 그때는 null이고, PredictionController가 자동실행을 건너뛴다.</summary>
        public InputCmd[] controls;
    }
}
