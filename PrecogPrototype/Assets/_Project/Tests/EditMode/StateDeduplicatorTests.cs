using NUnit.Framework;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.Sim.Tests
{
    /// <summary>
    /// StateDeduplicator의 0.5m 위치 격자 경계 및 상태 필드 민감도를 확인한다.
    /// 격자는 반올림(RoundToInt(x/0.5))이라 경계가 0.25m 배수에서 생긴다 — 두 지점이
    /// 격자 폭(0.5m) 미만 떨어져 있어도 경계에 걸치면 다른 키가 나올 수 있으므로,
    /// "같은 칸" 케이스는 경계에서 충분히 떨어진 값으로 검증한다.
    /// </summary>
    public class StateDeduplicatorTests
    {
        static SimWorld BuildWorld(Vector3 playerPos, bool dashReady = true)
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(playerPos);
            if (!dashReady) world.player.dashCharges = 0;
            return world;
        }

        [Test]
        public void PositionsWithinSameGridCell_ProduceSameKey()
        {
            SimWorld a = BuildWorld(new Vector3(0f, 0f, 0f));
            SimWorld b = BuildWorld(new Vector3(0.2f, 0f, 0.2f)); // 격자 경계(±0.25)에서 충분히 안쪽
            Assert.AreEqual(StateDeduplicator.ComputeKey(in a, 0), StateDeduplicator.ComputeKey(in b, 0));
        }

        [Test]
        public void PositionsAcrossGridBoundary_ProduceDifferentKeys()
        {
            SimWorld a = BuildWorld(new Vector3(0f, 0f, 0f));
            SimWorld b = BuildWorld(new Vector3(0.6f, 0f, 0f)); // 0.5m 격자 하나 너머
            Assert.AreNotEqual(StateDeduplicator.ComputeKey(in a, 0), StateDeduplicator.ComputeKey(in b, 0));
        }

        [Test]
        public void DashAvailabilityDifference_ChangesKey_EvenWithSamePosition()
        {
            SimWorld a = BuildWorld(Vector3.zero, dashReady: true);
            SimWorld b = BuildWorld(Vector3.zero, dashReady: false);
            Assert.AreNotEqual(StateDeduplicator.ComputeKey(in a, 0), StateDeduplicator.ComputeKey(in b, 0));
        }

        [Test]
        public void KillCountDifference_ChangesKey_EvenWithIdenticalWorld()
        {
            SimWorld world = BuildWorld(Vector3.zero);
            Assert.AreNotEqual(
                StateDeduplicator.ComputeKey(in world, 0),
                StateDeduplicator.ComputeKey(in world, 1));
        }
    }
}
