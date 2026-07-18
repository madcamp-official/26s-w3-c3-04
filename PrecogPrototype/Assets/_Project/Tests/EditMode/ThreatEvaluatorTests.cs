using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// core-controls의 원거리 솔저(투사체) 인지 여부를 확인한다.
    /// FutureThreatObserver가 실제로 ThreatEvaluator.Score에 반영되는지가 핵심.
    /// </summary>
    public class ThreatEvaluatorTests
    {
        [Test]
        public void Score_PenalizesImminentProjectileImpact()
        {
            SimWorld safe = SimWorld.Create();
            safe.player = PlayerSim.Spawn(Vector3.zero);

            SimWorld threatened = SimWorld.Create();
            threatened.player = PlayerSim.Spawn(Vector3.zero);
            // 플레이어 몸통을 향해 곧장 날아오는 투사체 (명중까지 수 틱 이내).
            threatened.SpawnProjectile(new Vector3(0f, 0.7f, 3f), new Vector3(0f, 0f, -30f));

            ScoreBreakdown safeScore = ThreatEvaluator.Score(in safe, 0, 0, 0, 0, false);
            ScoreBreakdown threatScore = ThreatEvaluator.Score(in threatened, 0, 0, 0, 0, false);

            Assert.Less(threatScore.safety, safeScore.safety,
                "명중 궤도의 투사체는 안전 점수를 깎아야 함 (원거리 솔저 인지)");
        }

        [Test]
        public void Score_IgnoresProjectile_WhenNotOnHitCourse()
        {
            SimWorld safe = SimWorld.Create();
            safe.player = PlayerSim.Spawn(Vector3.zero);

            SimWorld sideways = SimWorld.Create();
            sideways.player = PlayerSim.Spawn(Vector3.zero);
            // 플레이어와 무관하게 옆으로 지나가는 투사체 — 위협으로 잡히면 안 됨.
            sideways.SpawnProjectile(new Vector3(20f, 0.7f, 0f), new Vector3(0f, 0f, 30f));

            ScoreBreakdown safeScore = ThreatEvaluator.Score(in safe, 0, 0, 0, 0, false);
            ScoreBreakdown sidewaysScore = ThreatEvaluator.Score(in sideways, 0, 0, 0, 0, false);

            Assert.AreEqual(safeScore.safety, sidewaysScore.safety, 1e-4f,
                "명중 궤도가 아닌 투사체는 안전 점수에 영향을 주면 안 됨");
        }
    }
}
