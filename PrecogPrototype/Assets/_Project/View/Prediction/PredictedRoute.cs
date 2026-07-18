using System.Collections.Generic;
using UnityEngine;

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
    }
}
