using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// docs/shared/PREDICTION_CONTRACT.md 11장: 최종 후보를 최초 스냅샷에서 60Hz로
    /// 다시 실행해 PredictedFrame/PredictedActionEvent/controls를 만드는 CandidateReplayer 검증.
    /// </summary>
    public class CandidateReplayerTests
    {
        [Test]
        public void Replay_ProducesOneFramePerTick_PlusInitialFrame()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 20f));
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            SimServices services = StubServices.Create();

            var settings = PredictionSettings.Mini;
            CandidatePath candidate = PredictionPlanner.Plan(in world, in services, settings)[0];

            bool ok = CandidateReplayer.Replay(in world, in services, candidate, settings.macroTicks);

            Assert.IsTrue(ok, "정상 후보는 재생이 성공해야 함");
            Assert.AreEqual(candidate.durationTicks + 1, candidate.predictedFrames.Length,
                "프레임 수 = 실제 진행 틱 수 + 시작(tick 0) 프레임");
            Assert.AreEqual(candidate.predictedFrames.Length - 1, candidate.controls.Length,
                "controls는 시작 프레임 없이 틱당 1개");
            Assert.AreEqual(0, candidate.predictedFrames[0].tick);
            Assert.AreEqual(candidate.durationTicks, candidate.predictedFrames[candidate.predictedFrames.Length - 1].tick);
        }

        [Test]
        public void Replay_IsDeterministic_AcrossRepeatedCalls()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 20f));
            world.AddEnemy(new Vector3(0f, 0f, 5f));
            world.AddEnemy(new Vector3(3f, 0f, 4f));
            SimServices services = StubServices.Create();

            var settings = PredictionSettings.Mini;
            CandidatePath candidate = PredictionPlanner.Plan(in world, in services, settings)[0];

            var first = new CandidatePath { candidateId = candidate.candidateId, actions = candidate.actions, mapVersion = candidate.mapVersion, isDeadFallback = candidate.isDeadFallback };
            var second = new CandidatePath { candidateId = candidate.candidateId, actions = candidate.actions, mapVersion = candidate.mapVersion, isDeadFallback = candidate.isDeadFallback };

            CandidateReplayer.Replay(in world, in services, first, settings.macroTicks);
            CandidateReplayer.Replay(in world, in services, second, settings.macroTicks);

            Assert.AreEqual(first.predictedFrames.Length, second.predictedFrames.Length);
            for (int i = 0; i < first.predictedFrames.Length; i++)
            {
                Assert.AreEqual(first.predictedFrames[i].playerPosition, second.predictedFrames[i].playerPosition, $"틱 {i}: 위치 불일치");
                Assert.AreEqual(first.predictedFrames[i].playerYaw, second.predictedFrames[i].playerYaw, $"틱 {i}: yaw 불일치");
            }
        }

        [Test]
        public void Replay_RecordsAttackEvent_AtRealTriggerTick()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 1f));
            world.enemies[0] = EnemySim.Spawn(0, new Vector3(0f, 0f, 1f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Large);
            world.enemies[0].combat.health = 1;
            SimServices services = StubServices.Create();

            var settings = new PredictionSettings { macroTicks = 15, macroDepth = 1, beamWidth = 4, maxActionsPerNode = 12 };
            CandidatePath candidate = PredictionPlanner.Plan(in world, in services, settings)[0];

            bool ok = CandidateReplayer.Replay(in world, in services, candidate, settings.macroTicks);

            Assert.IsTrue(ok);
            bool hasAttackEvent = false;
            foreach (PredictedActionEvent e in candidate.actionEvents)
                if (e.type == PredictedActionType.Attack) hasAttackEvent = true;
            Assert.IsTrue(hasAttackEvent, "글로리킬을 트리거한 평타 이벤트가 기록돼야 함");
        }

        [Test]
        public void Replay_RecordsLungeEvent_AtRealTriggerTick()
        {
            // LungeWindupTicks=0이라 PlayerCombat.Step은 LgNone에서 곧장 LgTravel로 넘어간다
            // (LgWindup을 절대 거치지 않는다) — DetectEvents가 이 실제 전이를 잡는지 검증.
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 3f));
            SimServices services = StubServices.Create();

            var candidate = new CandidatePath
            {
                actions = new[] { MacroAction.LungeTo(world.enemies[0].id) },
                mapVersion = world.mapVersion,
            };

            bool ok = CandidateReplayer.Replay(in world, in services, candidate, 15);

            Assert.IsTrue(ok);
            bool hasLungeEvent = false;
            foreach (PredictedActionEvent e in candidate.actionEvents)
                if (e.type == PredictedActionType.Lunge) hasLungeEvent = true;
            Assert.IsTrue(hasLungeEvent, "런지(우클릭) 발동 이벤트가 기록돼야 함 — LgNone→LgTravel 전이를 잡아야 함");
        }

        [Test]
        public void Replay_Fails_WhenMapVersionMismatches()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            SimServices services = StubServices.Create();

            var candidate = new CandidatePath
            {
                actions = System.Array.Empty<MacroAction>(),
                mapVersion = world.mapVersion + 1, // 일부러 불일치
            };

            bool ok = CandidateReplayer.Replay(in world, in services, candidate, 15);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, candidate.predictedFrames.Length);
        }
    }
}
