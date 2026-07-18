using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// docs/shared/PREDICTION_INTEGRATION_PLAN.md 15장 G3 미니 탐색 기준:
    /// 사망 후보 제거, 생존 경로 1개 이상 반환, 반복 실행 시 같은 후보 반환.
    /// docs/shared/PREDICTION_CONTRACT.md 10장 "반환 후보 최대 3"도 함께 확인한다.
    /// </summary>
    public class BeamSearchTests
    {
        static SimWorld BuildSafeWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 20f));
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            world.AddEnemy(new Vector3(3f, 0f, 4f));
            return world;
        }

        /// <summary>플레이어 HP 1 + 적이 이미 공격 Active 직전(다음 틱 확정 명중)인, 회피 불가능한 상황.</summary>
        static SimWorld BuildLethalWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.combat.hp = 1;
            world.AddEnemy(new Vector3(0f, 0f, 0.3f));

            ref EnemySim enemy = ref world.enemies[0];
            enemy.ai.state = EnemyState.Windup;
            enemy.ai.stateTicks = AIConfig.MeleeWindupTicks - 1;
            enemy.ai.committedDir = new Vector3(0f, 0f, -1f);
            return world;
        }

        [Test]
        public void MiniSearch_SurvivesAndReturnsPlan_WhenNoImmediateThreat()
        {
            SimWorld world = BuildSafeWorld();
            SimServices services = StubServices.Create();
            CandidatePath[] results = PredictionPlanner.Plan(in world, in services, PredictionSettings.Mini);
            CandidatePath result = results[0];

            Assert.IsNotNull(result);
            Assert.IsFalse(result.isDeadFallback, "위협이 먼 상황에서 사망 폴백이 나오면 안 됨");
            Assert.Greater(result.actions.Length, 0, "생존 가능한 상황이면 최소 1개 행동은 계획돼야 함");
            Assert.AreNotEqual(float.NegativeInfinity, result.TotalScore);
        }

        [Test]
        public void Plan_ReturnsUpToThreeCandidates_SortedByScoreDescending()
        {
            SimWorld world = BuildSafeWorld();
            SimServices services = StubServices.Create();
            CandidatePath[] results = PredictionPlanner.Plan(in world, in services, PredictionSettings.Mini);

            Assert.GreaterOrEqual(results.Length, 1);
            Assert.LessOrEqual(results.Length, 3, "계약 10장 \"반환 후보 최대 3\"");
            for (int i = 1; i < results.Length; i++)
                Assert.GreaterOrEqual(results[i - 1].TotalScore, results[i].TotalScore, "점수 내림차순이어야 함");
        }

        [Test]
        public void MiniSearch_IsDeterministic_AcrossRepeatedRuns()
        {
            SimWorld world = BuildSafeWorld();
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Mini;

            CandidatePath first = PredictionPlanner.Plan(in world, in services, settings)[0];

            for (int i = 0; i < 5; i++)
            {
                CandidatePath repeat = PredictionPlanner.Plan(in world, in services, settings)[0];
                Assert.AreEqual(first.actions.Length, repeat.actions.Length, $"반복 {i}: 행동 개수 불일치");
                for (int a = 0; a < first.actions.Length; a++)
                {
                    Assert.AreEqual(first.actions[a].type, repeat.actions[a].type, $"반복 {i}, 인덱스 {a}: 행동 종류 불일치");
                    Assert.AreEqual(first.actions[a].lungeTargetId, repeat.actions[a].lungeTargetId, $"반복 {i}, 인덱스 {a}: 런지 타깃 불일치");
                }
                Assert.AreEqual(first.TotalScore, repeat.TotalScore, 1e-6f, $"반복 {i}: 점수 불일치");
            }
        }

        [Test]
        public void MiniSearch_FallsBackToBestScoringDead_WhenAllCandidatesDie()
        {
            SimWorld world = BuildLethalWorld();
            SimServices services = StubServices.Create();
            CandidatePath result = PredictionPlanner.Plan(in world, in services, PredictionSettings.Mini)[0];

            Assert.IsNotNull(result);
            Assert.IsTrue(result.isDeadFallback, "회피 불가능한 즉사 상황이면 사망 폴백이어야 함");
            Assert.Greater(result.actions.Length, 0);
            Assert.Greater(result.durationTicks, 0);
        }

        /// <summary>대형몹 처형(글로리킬)은 트리거 즉시 gloryStage>0로 결과가 잠기고 alive=false는
        /// ~77틱 뒤(컷신 종료)에야 따라온다. Mini 탐색(45틱 지평)은 컷신 완료를 못 보므로,
        /// 트리거 시점에 즉시 킬로 인정하는지 확인한다.</summary>
        [Test]
        public void MiniSearch_CreditsGloryKill_AssoonAsTriggered()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 1f));
            world.enemies[0] = EnemySim.Spawn(0, new Vector3(0f, 0f, 1f), Archetype.LargeMelee);
            world.enemies[0].combat.health = 1; // 다음 평타 한 대로 처형 트리거

            SimServices services = StubServices.Create();
            CandidatePath result = PredictionPlanner.Plan(in world, in services, PredictionSettings.Mini)[0];

            Assert.GreaterOrEqual(result.killCount, 1,
                "짧은 탐색 지평 안에서도 글로리킬 트리거는 즉시 킬로 인정돼야 함");
        }

        [Test]
        public void FullSearch_DoesNotReturnConsecutiveWait_WhenScoresTie()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            SimServices services = StubServices.Create();

            CandidatePath[] results = PredictionPlanner.Plan(
                in world, in services, PredictionSettings.Full);

            for (int r = 0; r < results.Length; r++)
            {
                bool previousWait = false;
                for (int i = 0; i < results[r].actions.Length; i++)
                {
                    bool currentWait = results[r].actions[i].type == MacroActionType.Wait;
                    Assert.IsFalse(previousWait && currentWait,
                        "계약 1.2: 전술적 근거 없는 연속 Wait는 반환하면 안 된다.");
                    previousWait = currentWait;
                }
            }
        }
    }
}
