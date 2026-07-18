using NUnit.Framework;
using UnityEngine;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// 예측(Beam Search)이 Sim 위에 쌓이려면 "같은 스냅샷 + 같은 입력 = 같은 결과"가
    /// 항상 성립해야 한다. 이 스위트는 그 최소 전제를 씬·Physics 없이 확인한다.
    /// 전체 180틱×100회 벤치마크는 별도 성능 작업으로 남긴다.
    /// </summary>
    public class DeterminismTests
    {
        const int Ticks = 40;
        const int Repeats = 20;

        static SimWorld BuildWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 20f));
            world.AddEnemy(new Vector3(0f, 0f, 10f));
            world.AddEnemy(new Vector3(1.5f, 0f, 9f));
            world.AddEnemy(new Vector3(-1.5f, 0f, 11f));
            return world;
        }

        /// <summary>결정론적 고정 입력 시퀀스. tick마다 같은 값을 반환한다(난수 없음).</summary>
        static InputCmd GetInput(int tick)
        {
            var cmd = new InputCmd { yaw = 180f, move = new Vector2(0f, 1f) };
            switch (tick)
            {
                case 10:
                    cmd.dash = true;
                    cmd.dashDirection = DashDirection.Forward;
                    break;
                case 19:
                    cmd.attack = true;
                    break;
                case 30:
                    cmd.lunge = true;
                    cmd.lungeTargetId = -1;
                    break;
            }
            return cmd;
        }

        static ulong RunAndHash(int ticks)
        {
            SimWorld world = BuildWorld();
            SimServices services = StubServices.Create();
            for (int t = 0; t < ticks; t++)
            {
                InputCmd cmd = GetInput(t);
                SimStep.Run(ref world, in cmd, in services);
            }
            return WorldHash.Compute(in world);
        }

        [Test]
        public void FixedInputSequence_ProducesSameHash_EveryRepeat()
        {
            ulong expected = RunAndHash(Ticks);
            for (int i = 0; i < Repeats; i++)
            {
                ulong actual = RunAndHash(Ticks);
                Assert.AreEqual(expected, actual, $"반복 {i}번째에서 WorldHash가 달라짐 (비결정적 동작 의심)");
            }
        }

        [Test]
        public void EmptyInputSequence_ProducesSameHash_EveryRepeat()
        {
            // 입력이 전부 비어있어도(적 AI만 굴러가도) 결정론이 성립해야 한다.
            SimWorld world = BuildWorld();
            SimServices services = StubServices.Create();
            InputCmd empty = InputCmd.Empty;
            for (int t = 0; t < Ticks; t++)
                SimStep.Run(ref world, in empty, in services);
            ulong expected = WorldHash.Compute(in world);

            for (int i = 0; i < Repeats; i++)
            {
                SimWorld w2 = BuildWorld();
                for (int t = 0; t < Ticks; t++)
                    SimStep.Run(ref w2, in empty, in services);
                Assert.AreEqual(expected, WorldHash.Compute(in w2));
            }
        }

        [Test]
        public void Snapshot_CopiesProjectileState_Independently()
        {
            SimWorld source = BuildWorld();
            source.SpawnProjectile(new Vector3(1f, 2f, 3f), new Vector3(4f, 0f, 0f));

            SimWorld copy = Snapshot.Clone(in source);
            Assert.AreEqual(WorldHash.Compute(in source), WorldHash.Compute(in copy));
            Assert.AreNotSame(source.enemies, copy.enemies);
            Assert.AreNotSame(source.projectiles, copy.projectiles);

            copy.projectiles[0].pos += Vector3.right;
            Assert.AreNotEqual(WorldHash.Compute(in source), WorldHash.Compute(in copy));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), source.projectiles[0].pos);
        }

        [Test]
        public void ProjectileSimulation_AdvancesFixedState_Deterministically()
        {
            SimWorld first = BuildWorld();
            first.SpawnProjectile(Vector3.zero, new Vector3(12f, 0f, 0f));
            SimWorld second = Snapshot.Clone(in first);
            SimServices services = StubServices.Create();

            for (int i = 0; i < 5; i++)
            {
                InputCmd empty = InputCmd.Empty;
                SimStep.Run(ref first, in empty, in services);
                SimStep.Run(ref second, in empty, in services);
            }

            Assert.AreEqual(WorldHash.Compute(in first), WorldHash.Compute(in second));
            Assert.AreEqual(1f, first.projectiles[0].pos.x, 1e-5f);
        }
    }
}
