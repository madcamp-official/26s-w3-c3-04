using Game.Sim;

namespace Game.Prediction
{
    /// <summary>예측 시스템의 유일한 진입점. 내부 구성 요소(BeamSearch 등)를 엮는다.</summary>
    public static class PredictionPlanner
    {
        /// <summary>점수 내림차순 후보 1~3개(계약 10장 "반환 후보 최대 3"). [0]이 최상위 후보.</summary>
        public static CandidatePath[] Plan(in SimWorld snapshot, in SimServices services, in PredictionSettings settings)
            => BeamSearch.Run(in snapshot, in services, in settings);
    }
}
