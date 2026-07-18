using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// Beam Search 본체. `PredictionSettings.Mini`(G3 미니 탐색)와 `Full`(3초/Beam-12
    /// 본탐색) 모두 이 하나로 돈다. 사망 후보 즉시 폐기, 반복 실행 시 같은 후보 반환,
    /// StateDeduplicator로 빔이 비슷한 상태로 몰리지 않도록 한다. 후보 확장 중
    /// GameObject/LINQ/List 없이 고정 배열과 WorldBufferPool만 사용한다.
    /// </summary>
    public static class BeamSearch
    {
        public static CandidatePath Run(in SimWorld snapshot, in SimServices services, in PredictionSettings settings)
        {
            ulong initialHash = WorldHash.Compute(in snapshot);

            if (!snapshot.player.alive)
                return new CandidatePath
                {
                    actions = System.Array.Empty<MacroAction>(),
                    killCount = 0,
                    durationTicks = 0,
                    score = float.NegativeInfinity,
                    initialSnapshotHash = initialHash,
                    isDeadFallback = true,
                };

            int maxPerDepth = settings.beamWidth * settings.maxActionsPerNode;
            int poolCapacity = settings.beamWidth * (settings.maxActionsPerNode + 1) + 1;
            var pool = new WorldBufferPool(poolCapacity);

            int nodeCapacity = 1 + settings.macroDepth * maxPerDepth;
            var nodes = new SearchNode[nodeCapacity];
            int nodeCount = 0;

            var actionBuffer = new MacroAction[settings.maxActionsPerNode];
            var expansionIndices = new int[maxPerDepth];
            var used = new bool[maxPerDepth];
            var currentBeam = new int[settings.beamWidth];
            var nextBeam = new int[settings.beamWidth];
            var chosenKeys = new ulong[settings.beamWidth];

            // 루트: 스냅샷 그대로.
            int rootBuffer = pool.Rent();
            pool.CopyInto(rootBuffer, in snapshot);
            nodes[nodeCount] = new SearchNode
            {
                worldBufferIndex = rootBuffer,
                parentIndex = -1,
                actionTaken = default,
                score = ThreatEvaluator.Score(in snapshot, 0, 0),
                depth = 0,
                killCount = 0,
                damageDealt = 0,
                ticksSurvived = 0,
                alive = true,
            };
            int rootIndex = nodeCount;
            nodeCount++;
            currentBeam[0] = rootIndex;
            int currentBeamCount = 1;

            int bestOverallNode = rootIndex;
            int bestDeadNode = -1;
            int bestDeadTicks = -1;

            for (int depth = 0; depth < settings.macroDepth && currentBeamCount > 0; depth++)
            {
                int expansionCount = 0;

                for (int slot = 0; slot < currentBeamCount; slot++)
                {
                    int parentIndex = currentBeam[slot];
                    SearchNode parentNode = nodes[parentIndex];
                    ref readonly SimWorld parentWorld = ref pool.Get(parentNode.worldBufferIndex);

                    int actionCount = ActionGenerator.Generate(in parentWorld, in services, in settings, actionBuffer);
                    int enemiesAliveBefore = CountAlive(in parentWorld);
                    int totalHpBefore = TotalHp(in parentWorld);

                    for (int a = 0; a < actionCount; a++)
                    {
                        int childBuffer = pool.Rent();
                        if (childBuffer < 0) continue; // 풀 고갈 — 용량 산정상 발생하면 안 되지만 방어적으로 스킵

                        pool.CopyInto(childBuffer, in parentWorld);
                        bool survived = true;
                        int ticks = 0;
                        for (; ticks < settings.macroTicks; ticks++)
                        {
                            ref SimWorld childWorld = ref pool.Get(childBuffer);
                            float yaw = ComputeAimYaw(in childWorld);
                            InputCmd cmd = actionBuffer[a].ToInputCmd(yaw, ticks);
                            SimStep.Run(ref childWorld, in cmd, in services);
                            if (!childWorld.player.alive) { survived = false; ticks++; break; }
                        }

                        ref readonly SimWorld finalWorld = ref pool.Get(childBuffer);
                        int killedThisStep = Mathf.Max(0, enemiesAliveBefore - CountAlive(in finalWorld));
                        int childKillCount = parentNode.killCount + killedThisStep;
                        int damageThisStep = Mathf.Max(0, totalHpBefore - TotalHp(in finalWorld));
                        int childDamageDealt = parentNode.damageDealt + damageThisStep;

                        var childNode = new SearchNode
                        {
                            worldBufferIndex = childBuffer,
                            parentIndex = parentIndex,
                            actionTaken = actionBuffer[a],
                            depth = parentNode.depth + 1,
                            killCount = childKillCount,
                            damageDealt = childDamageDealt,
                            ticksSurvived = parentNode.ticksSurvived + ticks,
                            alive = survived,
                            score = survived ? ThreatEvaluator.Score(in finalWorld, childKillCount, childDamageDealt) : float.NegativeInfinity,
                            stateKey = survived ? StateDeduplicator.ComputeKey(in finalWorld, childKillCount) : 0UL,
                        };
                        nodes[nodeCount] = childNode;

                        if (survived)
                        {
                            expansionIndices[expansionCount++] = nodeCount;
                        }
                        else
                        {
                            if (childNode.ticksSurvived > bestDeadTicks)
                            {
                                bestDeadTicks = childNode.ticksSurvived;
                                bestDeadNode = nodeCount;
                            }
                            pool.Return(childBuffer);
                        }
                        nodeCount++;
                    }
                }

                // 이번 depth의 부모 버퍼는 전부 자식으로 복사됐으니 반환.
                for (int slot = 0; slot < currentBeamCount; slot++)
                    pool.Return(nodes[currentBeam[slot]].worldBufferIndex);

                // 상위 beamWidth만 결정론적으로 선별. 2-pass: 서로 다른 상태(StateDeduplicator 키)를
                // 우선하고, 그래도 자리가 남으면 중복을 허용해 빔을 억지로 줄이지 않는다.
                // 동률은 항상 생성 순서(=먼저 만들어진 쪽이 승리, 엄격한 > 비교로 보장).
                System.Array.Clear(used, 0, expansionCount);
                int nextBeamCount = 0;

                for (int p = 0; p < settings.beamWidth && nextBeamCount < expansionCount; p++)
                {
                    int bestLocal = -1;
                    for (int e = 0; e < expansionCount; e++)
                    {
                        if (used[e]) continue;
                        ulong key = nodes[expansionIndices[e]].stateKey;
                        if (ContainsKey(chosenKeys, nextBeamCount, key)) continue;
                        if (bestLocal < 0 || nodes[expansionIndices[e]].score > nodes[expansionIndices[bestLocal]].score)
                            bestLocal = e;
                    }
                    if (bestLocal < 0) break; // 서로 다른 키 후보 소진
                    used[bestLocal] = true;
                    chosenKeys[nextBeamCount] = nodes[expansionIndices[bestLocal]].stateKey;
                    nextBeam[nextBeamCount++] = expansionIndices[bestLocal];
                }
                while (nextBeamCount < settings.beamWidth && nextBeamCount < expansionCount)
                {
                    int bestLocal = -1;
                    for (int e = 0; e < expansionCount; e++)
                    {
                        if (used[e]) continue;
                        if (bestLocal < 0 || nodes[expansionIndices[e]].score > nodes[expansionIndices[bestLocal]].score)
                            bestLocal = e;
                    }
                    if (bestLocal < 0) break;
                    used[bestLocal] = true;
                    nextBeam[nextBeamCount++] = expansionIndices[bestLocal];
                }
                // 선별 안 된 생존 후보의 버퍼도 반환.
                for (int e = 0; e < expansionCount; e++)
                {
                    if (used[e]) continue;
                    pool.Return(nodes[expansionIndices[e]].worldBufferIndex);
                }

                if (nextBeamCount > 0 && nodes[nextBeam[0]].score > nodes[bestOverallNode].score)
                    bestOverallNode = nextBeam[0];

                System.Array.Copy(nextBeam, currentBeam, nextBeamCount);
                currentBeamCount = nextBeamCount;
            }

            int resultNode;
            bool deadFallback = false;
            if (currentBeamCount > 0)
            {
                resultNode = currentBeam[0]; // 마지막 depth 최상위 = 최종 최선
            }
            else if (bestOverallNode != rootIndex)
            {
                resultNode = bestOverallNode; // 더 얕은 depth에서 살아남은 최선
            }
            else if (bestDeadNode >= 0)
            {
                resultNode = bestDeadNode; // 전부 사망 — 가장 오래 생존한 후보로 폴백
                deadFallback = true;
            }
            else
            {
                resultNode = rootIndex; // 확장 자체가 없었음(행동 후보 0개)
            }

            return BuildCandidate(nodes, resultNode, settings.macroDepth, initialHash, deadFallback);
        }

        static CandidatePath BuildCandidate(SearchNode[] nodes, int resultNode, int macroDepth, ulong initialHash, bool deadFallback)
        {
            var reversed = new MacroAction[macroDepth];
            int count = 0;
            int cursor = resultNode;
            while (nodes[cursor].parentIndex >= 0 && count < macroDepth)
            {
                reversed[count++] = nodes[cursor].actionTaken;
                cursor = nodes[cursor].parentIndex;
            }

            var actions = new MacroAction[count];
            for (int i = 0; i < count; i++)
                actions[i] = reversed[count - 1 - i];

            SearchNode result = nodes[resultNode];
            return new CandidatePath
            {
                actions = actions,
                killCount = result.killCount,
                damageDealt = result.damageDealt,
                durationTicks = result.ticksSurvived,
                score = result.score,
                initialSnapshotHash = initialHash,
                isDeadFallback = deadFallback,
            };
        }

        static bool ContainsKey(ulong[] keys, int count, ulong key)
        {
            for (int i = 0; i < count; i++)
                if (keys[i] == key) return true;
            return false;
        }

        static int CountAlive(in SimWorld world)
        {
            int count = 0;
            for (int i = 0; i < world.enemyCount; i++)
                if (world.enemies[i].alive) count++;
            return count;
        }

        /// <summary>생존 적의 HP 합. 매크로 스텝 전후 차이로 "죽이진 못했지만 때린" 피해를 잡아낸다.</summary>
        static int TotalHp(in SimWorld world)
        {
            int sum = 0;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim e = ref world.enemies[i];
                if (e.alive) sum += e.combat.health;
            }
            return sum;
        }

        /// <summary>
        /// 탐색용 간이 조준: 가장 가까운 생존 적을 바라본다. 실제 카메라 입력을 대신하는
        /// 이번 마일스톤의 단순화이며, 없으면 현재 yaw를 유지한다.
        /// </summary>
        static float ComputeAimYaw(in SimWorld world)
        {
            float best = float.MaxValue;
            Vector3 target = default;
            bool found = false;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                float distance = CombatMath.FlatDistance(world.player.pos, enemy.pos);
                if (distance < best) { best = distance; target = enemy.pos; found = true; }
            }
            if (!found) return world.player.yaw;

            Vector3 direction = target - world.player.pos;
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
    }
}
