using System.Text;
using UnityEditor;
using UnityEngine;
using Game.Sim;

namespace Game.Prediction.Editor
{
    /// <summary>
    /// 예측 결과를 텍스트로 확인하는 개발용 도구. 실제 씬/Physics 없이 평지 스텁으로
    /// 미리 정한 다양한 상황을 돌려 Beam Search가 실제로 어떤 행동 시퀀스와 궤적을
    /// 찾아내는지 콘솔에 출력한다. 회귀 검증용이 아니라 눈으로 확인하는 용도.
    /// </summary>
    public static class PredictionVisualizer
    {
        [MenuItem("Precog/예측 시각화 (텍스트, 콘솔)")]
        static void RunDemo()
        {
            RunScenario("1. 여유 있는 거리 (적 3마리)", BuildRoomyWorld());
            Separator();
            RunScenario("2. 코앞에 적 (런지 사거리 안, 적 2마리)", BuildCloseQuartersWorld());
            Separator();
            RunScenario("3. 체력 위험 (HP 1, 적 2마리 근접)", BuildLowHpWorld());
            Separator();
            RunScenario("4. 적 공격 임박 (Windup 중, 회피 시급)", BuildImminentAttackWorld());
            Separator();
            RunScenario("5. 대시 소진 상태 (도보 이동만 가능)", BuildDashExhaustedWorld());
            Separator();
            RunScenario("6. 좁은 구석 (한쪽 부채꼴에 몰림, 적 4마리)", BuildCorneredWorld());
            Separator();
            RunScenario("7. 혼합 거리 (근접 3마리 + 낙오자 원거리 1마리)", BuildMixedRangeWorld());
            Separator();
            RunScenario("8. 대칭 포위 (적 6마리)", BuildSymmetricWorld());
            Separator();
            RunScenario("9. 완전 원형 포위 (적 12마리)", BuildCircleWorld(12, 9f, 3f));
            Separator();
            RunScenario("10. 대규모 웨이브 (적 30마리, 스트레스 테스트)", BuildCircleWorld(30, 10f, 2f));
        }

        static void Separator()
        {
            Debug.Log(new string('=', 60));
        }

        static SimWorld BuildRoomyWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 10f));
            world.AddEnemy(new Vector3(0f, 0f, 4f));
            world.AddEnemy(new Vector3(2f, 0f, 3f));
            world.AddEnemy(new Vector3(-6f, 0f, 5f));
            return world;
        }

        static SimWorld BuildCloseQuartersWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 4f));
            world.AddEnemy(new Vector3(0f, 0f, 1f));
            world.AddEnemy(new Vector3(1.5f, 0f, 1.5f));
            return world;
        }

        /// <summary>HP 1 — 즉사는 아니지만 한 대만 더 맞으면 끝. 신중하게 노는지 확인.</summary>
        static SimWorld BuildLowHpWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 5f));
            world.player.health = 1;
            world.AddEnemy(new Vector3(0f, 0f, 2f));
            world.AddEnemy(new Vector3(2f, 0f, 3f));
            return world;
        }

        /// <summary>적 하나가 이미 Windup 중(명중까지 8틱) — 그 자리에서 맞을지 피할지 시급하게 판단해야 함.</summary>
        static SimWorld BuildImminentAttackWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 3f));
            world.AddEnemy(new Vector3(0f, 0f, 1f));
            world.AddEnemy(new Vector3(3f, 0f, 4f));

            ref EnemySim urgent = ref world.enemies[0];
            urgent.aiState = EnemyAIState.AttackWindup;
            urgent.stateTicks = CombatConfig.EnemyAttackWindupTicks - 8;
            urgent.committedAttackDirection = new Vector3(0f, 0f, -1f);
            return world;
        }

        /// <summary>대시 충전 0, 재충전 중 — 대시 없이 순수 도보 이동으로만 대응해야 함.</summary>
        static SimWorld BuildDashExhaustedWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 6f));
            world.player.dashCharges = 0;
            world.player.dashRecharge = SimConfig.DashRechargeTicks;
            world.AddEnemy(new Vector3(0f, 0f, 3f));
            world.AddEnemy(new Vector3(-2f, 0f, 4f));
            return world;
        }

        /// <summary>적 4마리가 한쪽 부채꼴(+x 쪽)을 덮고 있어 그쪽으로는 못 빠짐 — 반대쪽(-x)만 열려있음.</summary>
        static SimWorld BuildCorneredWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(2f, 0f, 4f));
            world.AddEnemy(new Vector3(4f, 0f, 2f));
            world.AddEnemy(new Vector3(4f, 0f, -2f));
            world.AddEnemy(new Vector3(2f, 0f, -4f));
            return world;
        }

        /// <summary>가까운 3마리 + 멀리 떨어진 낙오자 1마리 — 가까운 위협에 집중하고 낙오자는 무시하는지 확인.</summary>
        static SimWorld BuildMixedRangeWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(2f, 0f, 2f));
            world.AddEnemy(new Vector3(2.5f, 0f, 1.5f));
            world.AddEnemy(new Vector3(1.5f, 0f, 2.5f));
            world.AddEnemy(new Vector3(0f, 0f, -15f));
            return world;
        }

        /// <summary>거울대칭 쌍을 포함한 6마리, 전부 한 방향(-z)에 몰려있음.</summary>
        static SimWorld BuildSymmetricWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 30f));
            world.AddEnemy(new Vector3(2f, 0f, 10f));
            world.AddEnemy(new Vector3(-2f, 0f, 10f));
            world.AddEnemy(new Vector3(5f, 0f, 8f));
            world.AddEnemy(new Vector3(-5f, 0f, 8f));
            world.AddEnemy(new Vector3(0f, 0f, 12f));
            world.AddEnemy(new Vector3(0f, 0f, 6f));
            return world;
        }

        /// <summary>플레이어를 중심으로 count마리를 원형으로 배치. radiusJitter로 거리에 약간 변화를 줌.</summary>
        static SimWorld BuildCircleWorld(int count, float radius, float radiusJitter)
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)i / count * Mathf.PI * 2f;
                float r = radius + (i % 3 - 1) * radiusJitter;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                world.AddEnemy(pos);
            }
            return world;
        }

        static void RunScenario(string title, SimWorld world)
        {
            var sb = new StringBuilder();
            SimServices services = new SimServices(new FlatGroundCollision(), new StraightLinePathfinder());

            sb.AppendLine($"### {title} ###");
            sb.AppendLine("--- 초기 상태 (tick 0) ---");
            PrintWorld(sb, in world);

            PredictionSettings settings = PredictionSettings.Full;
            sb.AppendLine();
            sb.AppendLine($"설정: macroTicks={settings.macroTicks} macroDepth={settings.macroDepth} beamWidth={settings.beamWidth} " +
                          $"(총 {settings.macroTicks * settings.macroDepth}틱 = {settings.macroTicks * settings.macroDepth / 60f:F2}초 예측)");

            CandidatePath[] plans = PredictionPlanner.Plan(in world, in services, settings);
            CandidatePath plan = plans[0];

            sb.AppendLine();
            sb.AppendLine($"--- 예측 결과 ({plans.Length}개 후보 중 최상위 표시) ---");
            sb.AppendLine($"score={plan.TotalScore:F1} (안전{plan.safetyScore:F0}/처치{plan.killScore:F0}/난이도{plan.difficultyScore:F0})  " +
                          $"kills={plan.killCount}  damageDealt={plan.damageDealt}  dash={plan.dashCount}  lunge={plan.lungeCount}  " +
                          $"hitsTaken={plan.expectedHits}  durationTicks={plan.durationTicks}  deadFallback={plan.isDeadFallback}");
            sb.AppendLine($"행동 시퀀스 ({plan.actions.Length}개 매크로 스텝, 각 {settings.macroTicks}틱={settings.macroTicks / 60f:F2}초):");
            for (int i = 0; i < plan.actions.Length; i++)
            {
                MacroAction a = plan.actions[i];
                string label = a.type.ToString();
                if (a.type == MacroActionType.Lunge) label += $" -> 적 id={a.lungeTargetId}";
                sb.AppendLine($"  [{i}] {label}");
            }

            sb.AppendLine();
            sb.AppendLine("--- 실제로 틱마다 재생한 궤적 (찾은 행동을 SimStep.Run으로 그대로 실행) ---");
            Replay(sb, world, services, plan, settings);

            Debug.Log(sb.ToString());
        }

        static void Replay(StringBuilder sb, SimWorld world, SimServices services, CandidatePath plan, PredictionSettings settings)
        {
            int tick = 0;
            for (int m = 0; m < plan.actions.Length; m++)
            {
                MacroAction action = plan.actions[m];
                sb.AppendLine($"  매크로 {m}: {action.type}{(action.type == MacroActionType.Lunge ? " (id=" + action.lungeTargetId + ")" : "")}");
                for (int t = 0; t < settings.macroTicks; t++)
                {
                    float yaw = AimAtNearestEnemy(in world);
                    InputCmd cmd = action.ToInputCmd(yaw, t);
                    SimStep.Run(ref world, in cmd, in services);
                    tick++;

                    bool lastTickOfMacro = t == settings.macroTicks - 1;
                    if (tick % 15 == 0 || lastTickOfMacro || !world.player.alive)
                        PrintTick(sb, tick, in world);

                    if (!world.player.alive)
                    {
                        sb.AppendLine("    ** 플레이어 사망 — 재생 중단 **");
                        return;
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine("--- 재생 종료 (계획 완주) ---");
            PrintWorld(sb, in world);
        }

        static float AimAtNearestEnemy(in SimWorld world)
        {
            float best = float.MaxValue;
            Vector3 target = default;
            bool found = false;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim e = ref world.enemies[i];
                if (!e.alive) continue;
                float d = CombatMath.FlatDistance(world.player.pos, e.pos);
                if (d < best) { best = d; target = e.pos; found = true; }
            }
            if (!found) return world.player.yaw;
            Vector3 dir = target - world.player.pos;
            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        static void PrintWorld(StringBuilder sb, in SimWorld world)
        {
            sb.AppendLine($"  플레이어 pos=({world.player.pos.x:F1},{world.player.pos.z:F1}) hp={world.player.health} alive={world.player.alive}");
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim e = ref world.enemies[i];
                sb.AppendLine($"  적{e.id} pos=({e.pos.x:F1},{e.pos.z:F1}) hp={e.combat.health} alive={e.alive} state={e.aiState}");
            }
        }

        static void PrintTick(StringBuilder sb, int tick, in SimWorld world)
        {
            float nearest = float.MaxValue;
            int aliveCount = 0;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim e = ref world.enemies[i];
                if (!e.alive) continue;
                aliveCount++;
                float d = CombatMath.FlatDistance(world.player.pos, e.pos);
                if (d < nearest) nearest = d;
            }
            string nearestStr = aliveCount > 0 ? nearest.ToString("F1") : "-";
            sb.AppendLine($"    [t={tick,4}] player=({world.player.pos.x,6:F1},{world.player.pos.z,6:F1}) " +
                          $"hp={world.player.health} phase={world.player.combat.phase,-14} 생존적={aliveCount} 최근접={nearestStr}");
        }
    }

    /// <summary>평지·벽 없음 가정의 최소 ICollision. Tests/EditMode/Support/StubServices와 같은
    /// 개념이지만, 에디터 툴이 Tests 어셈블리에 의존하지 않도록 여기서 따로 둔다.</summary>
    sealed class FlatGroundCollision : ICollision
    {
        public CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist) => default;
        public bool SampleGround(Vector3 feet, float maxDown, out float groundY) { groundY = 0f; return true; }
        public bool HasLineOfSight(Vector3 from, Vector3 to) => true;
        public bool CanOccupyCapsule(Vector3 feet, float radius, float height) => true;
    }

    sealed class StraightLinePathfinder : IPathfinder
    {
        public PathStep NextStep(Vector3 from, Vector3 to) => new PathStep
        {
            kind = MoveKind.Walk,
            next = to,
            currentNodeId = 0,
            destinationNodeId = 0,
            nextNodeId = 0,
            linkId = -1
        };
    }
}
