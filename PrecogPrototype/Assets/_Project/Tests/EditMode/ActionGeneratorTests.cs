using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// docs/shared/PREDICTION_CONTRACT.md 10장 순서에서 Wait만 맨 뒤로 옮긴 버전을 확인한다
    /// — Wait가 맨 앞이면 "아직 아무 일도 안 일어난" 동점 상황에서 항상 이겨서, 접근이
    /// 필요한 상황에서 Beam Search가 아예 안 다가가는 회귀가 실제로 있었다(ActionGenerator.cs 주석 참고).
    /// </summary>
    public class ActionGeneratorTests
    {
        static readonly MacroActionType[] ExpectedOrder =
        {
            MacroActionType.MoveForward,
            MacroActionType.MoveLeft,
            MacroActionType.MoveRight,
            MacroActionType.Retreat,
            MacroActionType.Jump,
            MacroActionType.DashForward,
            MacroActionType.DashBackward,
            MacroActionType.DashLeft,
            MacroActionType.DashRight,
            MacroActionType.Attack,
        };

        [Test]
        public void Generate_FollowsWaitLastOrder_WhenEverythingValid()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 1.5f)); // 정면, 평타·런지 사거리 둘 다 안
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            int i = 0;
            foreach (MacroActionType expected in ExpectedOrder)
            {
                Assert.Less(i, count, $"'{expected}'가 나와야 하는데 후보가 부족함");
                Assert.AreEqual(expected, buffer[i].type, $"인덱스 {i} 행동 순서가 예상과 다름");
                i++;
            }
            Assert.Less(i, count, "런지 후보가 하나도 없음(유효 타깃 1명 있는 상황)");
            Assert.AreEqual(MacroActionType.Lunge, buffer[i].type, "Attack 다음은 Lunge여야 함");
            i++;
            Assert.AreEqual(count - 1, i, "Wait는 맨 마지막이어야 함");
            Assert.AreEqual(MacroActionType.Wait, buffer[i].type, "Wait는 맨 마지막이어야 함");
        }

        [Test]
        public void Generate_IncludesJump_OnGround_AndDoubleJumpInAir()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int groundedCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsTrue(Contains(buffer, groundedCount, MacroActionType.Jump), "지상에서 점프 후보가 있어야 함");

            world.player.grounded = false;
            world.player.jumpCount = 1;
            int airborneCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsTrue(Contains(buffer, airborneCount, MacroActionType.Jump), "공중 1회 점프 후 더블 점프 후보가 있어야 함");

            world.player.jumpCount = 2;
            int exhaustedCount = ActionGenerator.Generate(in world, in services, in settings, buffer);
            Assert.IsFalse(Contains(buffer, exhaustedCount, MacroActionType.Jump), "더블 점프 소진 뒤에는 점프 후보가 없어야 함");
        }

        [Test]
        public void JumpMacro_PulsesJumpOnlyOnFirstTick()
        {
            MacroAction jump = MacroAction.Simple(MacroActionType.Jump);
            Assert.IsTrue(jump.ToInputCmd(0f, 0).jump);
            Assert.IsFalse(jump.ToInputCmd(0f, 1).jump);
        }

        [Test]
        public void JumpMacro_UsesRealSimForFirstAndDoubleJump()
        {
            SimWorld world = SimWorld.Create();
            SimServices services = StubServices.Create();
            MacroAction jump = MacroAction.Simple(MacroActionType.Jump);

            InputCmd first = jump.ToInputCmd(0f, 0);
            SimStep.Run(ref world, in first, in services);
            Assert.AreEqual(1, world.player.jumpCount);
            Assert.IsFalse(world.player.grounded);

            InputCmd second = jump.ToInputCmd(0f, 0);
            SimStep.Run(ref world, in second, in services);
            Assert.AreEqual(2, world.player.jumpCount);
            Assert.Greater(world.player.vel.y, 0f);
        }

        static bool Contains(MacroAction[] actions, int count, MacroActionType type)
        {
            for (int i = 0; i < count; i++) if (actions[i].type == type) return true;
            return false;
        }

        [Test]
        public void Generate_ProducesUpToTwoLungeCandidates_WhenTwoValidTargetsExist()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 3f));
            world.AddEnemy(new Vector3(1f, 0f, 3f));
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            int lungeCount = 0;
            for (int i = 0; i < count; i++)
                if (buffer[i].type == MacroActionType.Lunge) lungeCount++;
            Assert.AreEqual(2, lungeCount, "유효 타깃 2명이면 런지 후보도 2개 나와야 함(계약 10장)");
        }

        [Test]
        public void Generate_OrdersLungeCandidatesByAscendingTargetId()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(0f, 0f, 3f));  // id 0
            world.AddEnemy(new Vector3(1f, 0f, 3f));  // id 1
            SimServices services = StubServices.Create();
            PredictionSettings settings = PredictionSettings.Full;
            var buffer = new MacroAction[settings.maxActionsPerNode];

            int count = ActionGenerator.Generate(in world, in services, in settings, buffer);

            int firstLungeIndex = -1;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].type != MacroActionType.Lunge) continue;
                firstLungeIndex = i;
                break;
            }
            Assert.GreaterOrEqual(firstLungeIndex, 0);
            Assert.AreEqual(0, buffer[firstLungeIndex].lungeTargetId);
            Assert.AreEqual(1, buffer[firstLungeIndex + 1].lungeTargetId);
        }
    }
}
