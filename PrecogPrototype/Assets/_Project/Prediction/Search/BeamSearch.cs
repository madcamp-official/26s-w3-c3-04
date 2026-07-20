using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// Beam Search 본체. `PredictionSettings.Mini`(G3 미니 탐색)와 `Full`(3초/Beam-12
    /// 본탐색) 모두 이 하나로 돈다. 사망 후보 즉시 폐기(빔 확장에선 제외하되, 전멸
    /// 폴백을 위해 점수는 계산해둔다), 반복 실행 시 같은 후보 반환, StateDeduplicator로
    /// 빔이 비슷한 상태로 몰리지 않도록 한다. docs/shared/PREDICTION_CONTRACT.md 10장의
    /// "반환 후보 최대 3"에 따라 점수 내림차순 최대 3개를 반환한다. 후보 확장 중
    /// GameObject/LINQ/List 없이 고정 배열과 WorldBufferPool만 사용한다.
    /// </summary>
    public static class BeamSearch
    {
        public static CandidatePath[] Run(in SimWorld snapshot, in SimServices services, in PredictionSettings settings)
        {
            ulong initialHash = WorldHash.Compute(in snapshot);

            if (snapshot.player.combat.hp <= 0)
            {
                ScoreBreakdown deadBreakdown = ThreatEvaluator.Score(in snapshot, 0, 0, 0, 0, false);
                return new[]
                {
                    new CandidatePath
                    {
                        candidateId = 0,
                        actions = System.Array.Empty<MacroAction>(),
                        killCount = 0,
                        damageDealt = 0,
                        dashCount = 0,
                        lungeCount = 0,
                        expectedHits = 0,
                        durationTicks = 0,
                        safetyScore = deadBreakdown.safety,
                        killScore = deadBreakdown.kill,
                        difficultyScore = deadBreakdown.difficulty,
                        initialSnapshotHash = initialHash,
                        mapVersion = snapshot.mapVersion,
                        isDeadFallback = true,
                    },
                };
            }

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
            ScoreBreakdown rootBreakdown = ThreatEvaluator.Score(in snapshot, 0, 0, 0, 0, false);
            nodes[nodeCount] = new SearchNode
            {
                worldBufferIndex = rootBuffer,
                parentIndex = -1,
                actionTaken = default,
                score = rootBreakdown.Total,
                safetyScore = rootBreakdown.safety,
                killScore = rootBreakdown.kill,
                difficultyScore = rootBreakdown.difficulty,
                depth = 0,
                alive = true,
            };
            int rootIndex = nodeCount;
            nodeCount++;
            currentBeam[0] = rootIndex;
            int currentBeamCount = 1;

            int bestOverallNode = rootIndex;
            int bestDeadNode = -1;
            float bestDeadScore = float.NegativeInfinity;

            for (int depth = 0; depth < settings.macroDepth && currentBeamCount > 0; depth++)
            {
                int expansionCount = 0;

                for (int slot = 0; slot < currentBeamCount; slot++)
                {
                    int parentIndex = currentBeam[slot];
                    SearchNode parentNode = nodes[parentIndex];
                    ref readonly SimWorld parentWorld = ref pool.Get(parentNode.worldBufferIndex);

                    int actionCount = ActionGenerator.Generate(in parentWorld, in services, in settings, actionBuffer);

                    for (int a = 0; a < actionCount; a++)
                    {
                        bool isWait = actionBuffer[a].type == MacroActionType.Wait;
                        // 계약 1.2: 전술적 근거가 아직 없는 연속 Wait는 1회를 초과하지 않는다.
                        if (isWait && parentNode.consecutiveWaitCount >= 1) continue;

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
                            if (childWorld.player.combat.hp <= 0) { survived = false; ticks++; break; }
                        }

                        ref readonly SimWorld finalWorld = ref pool.Get(childBuffer);

                        int killedNormalThisStep = 0, killedMidThisStep = 0;
                        for (int i = 0; i < parentWorld.enemyCount; i++)
                        {
                            // 대형몹은 처형(글로리킬) 트리거 시 gloryStage>0로 즉시 결과가 잠기고,
                            // alive=false는 ~77틱 뒤(컷신 종료)에야 뒤따른다 — 트리거 시점에 바로 인정한다.
                            bool wasDefeated = !parentWorld.enemies[i].alive || parentWorld.enemies[i].combat.gloryStage > 0;
                            bool isDefeated = !finalWorld.enemies[i].alive || finalWorld.enemies[i].combat.gloryStage > 0;
                            if (!wasDefeated && isDefeated)
                            {
                                if (finalWorld.enemies[i].ai.size == SizeClass.Large) killedMidThisStep++;
                                else killedNormalThisStep++;
                            }
                        }
                        int damageThisStep = Mathf.Max(0, TotalHp(in parentWorld) - TotalHp(in finalWorld));
                        int hitsThisStep = Mathf.Max(0, parentWorld.player.combat.hp - finalWorld.player.combat.hp);
                        bool isRepeated = parentNode.depth > 0 && actionBuffer[a].type == parentNode.actionTaken.type;

                        int childKillNormal = parentNode.killCountNormal + killedNormalThisStep;
                        int childKillMid = parentNode.killCountMid + killedMidThisStep;
                        int childDamageDealt = parentNode.damageDealt + damageThisStep;
                        int childHitsTaken = parentNode.hitsTaken + hitsThisStep;
                        int childDashCount = parentNode.dashCount + (IsDash(actionBuffer[a].type) ? 1 : 0);
                        int childLungeCount = parentNode.lungeCount + (IsLunge(actionBuffer[a].type) ? 1 : 0);
                        int childWaitCount = parentNode.waitCount + (isWait ? 1 : 0);
                        int childConsecutiveWaitCount = isWait ? parentNode.consecutiveWaitCount + 1 : 0;

                        ScoreBreakdown breakdown = ThreatEvaluator.Score(
                            in finalWorld, childKillNormal, childKillMid, childDamageDealt, childHitsTaken, isRepeated);

                        var childNode = new SearchNode
                        {
                            worldBufferIndex = childBuffer,
                            parentIndex = parentIndex,
                            actionTaken = actionBuffer[a],
                            depth = parentNode.depth + 1,
                            killCountNormal = childKillNormal,
                            killCountMid = childKillMid,
                            damageDealt = childDamageDealt,
                            hitsTaken = childHitsTaken,
                            dashCount = childDashCount,
                            lungeCount = childLungeCount,
                            waitCount = childWaitCount,
                            consecutiveWaitCount = childConsecutiveWaitCount,
                            ticksSurvived = parentNode.ticksSurvived + ticks,
                            alive = survived,
                            score = breakdown.Total,
                            safetyScore = breakdown.safety,
                            killScore = breakdown.kill,
                            difficultyScore = breakdown.difficulty,
                            stateKey = survived ? StateDeduplicator.ComputeKey(in finalWorld, childKillNormal + childKillMid) : 0UL,
                        };
                        nodes[nodeCount] = childNode;

                        if (survived)
                        {
                            expansionIndices[expansionCount++] = nodeCount;
                        }
                        else
                        {
                            if (childNode.score > bestDeadScore)
                            {
                                bestDeadScore = childNode.score;
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
                        if (bestLocal < 0 || IsBetterCandidate(
                                in nodes[expansionIndices[e]], in nodes[expansionIndices[bestLocal]]))
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
                        if (bestLocal < 0 || IsBetterCandidate(
                                in nodes[expansionIndices[e]], in nodes[expansionIndices[bestLocal]]))
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

                if (nextBeamCount > 0 &&
                    IsBetterCandidate(in nodes[nextBeam[0]], in nodes[bestOverallNode]))
                    bestOverallNode = nextBeam[0];

                System.Array.Copy(nextBeam, currentBeam, nextBeamCount);
                currentBeamCount = nextBeamCount;
            }

            var resultNodes = new int[3];
            int resultCount = 0;
            bool deadFallback = false;

            if (currentBeamCount > 0)
            {
                // currentBeam은 2-pass 선별 때문에 전체가 점수순 정렬임을 보장 못 하므로,
                // 반환 직전에만 상위 최대 3개를 명시적으로 뽑는다(배열 크기 작아 비용 무시).
                System.Array.Clear(used, 0, currentBeamCount);
                int topN = Mathf.Min(3, currentBeamCount);
                for (int r = 0; r < topN; r++)
                {
                    int bestLocal = -1;
                    for (int e = 0; e < currentBeamCount; e++)
                    {
                        if (used[e]) continue;
                        if (bestLocal < 0 || IsBetterCandidate(
                                in nodes[currentBeam[e]], in nodes[currentBeam[bestLocal]]))
                            bestLocal = e;
                    }
                    used[bestLocal] = true;
                    resultNodes[resultCount++] = currentBeam[bestLocal];
                }
            }
            else if (bestOverallNode != rootIndex)
            {
                resultNodes[resultCount++] = bestOverallNode; // 더 얕은 depth에서 살아남은 최선
            }
            else if (bestDeadNode >= 0)
            {
                resultNodes[resultCount++] = bestDeadNode; // 전부 사망 — 점수가 가장 덜 나쁜 후보로 폴백
                deadFallback = true;
            }
            else
            {
                resultNodes[resultCount++] = rootIndex; // 확장 자체가 없었음(행동 후보 0개)
            }

            var results = new CandidatePath[resultCount];
            for (int i = 0; i < resultCount; i++)
                results[i] = BuildCandidate(nodes, resultNodes[i], settings.macroDepth, initialHash, deadFallback, i, snapshot.mapVersion);
            return results;
        }

        static CandidatePath BuildCandidate(
            SearchNode[] nodes, int resultNode, int macroDepth, ulong initialHash,
            bool deadFallback, int candidateId, int mapVersion)
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
                candidateId = candidateId,
                actions = actions,
                killCount = result.killCountNormal + result.killCountMid,
                damageDealt = result.damageDealt,
                dashCount = result.dashCount,
                lungeCount = result.lungeCount,
                expectedHits = result.hitsTaken,
                durationTicks = result.ticksSurvived,
                safetyScore = result.safetyScore,
                killScore = result.killScore,
                difficultyScore = result.difficultyScore,
                initialSnapshotHash = initialHash,
                mapVersion = mapVersion,
                isDeadFallback = deadFallback,
            };
        }

        static bool IsDash(MacroActionType type) =>
            type == MacroActionType.DashForward || type == MacroActionType.DashBackward ||
            type == MacroActionType.DashLeft || type == MacroActionType.DashRight;

        /// <summary>런지 계열(단일 런지 + 공중 마무리 콤보) — 통계(lungeCount)에 함께 집계한다.</summary>
        static bool IsLunge(MacroActionType type) =>
            type == MacroActionType.Lunge || type == MacroActionType.LungeStrike;

        static bool IsBetterCandidate(in SearchNode candidate, in SearchNode current)
        {
            const float ScoreEpsilon = 1e-5f;
            if (candidate.score > current.score + ScoreEpsilon) return true;
            if (candidate.score < current.score - ScoreEpsilon) return false;
            if (candidate.consecutiveWaitCount != current.consecutiveWaitCount)
                return candidate.consecutiveWaitCount < current.consecutiveWaitCount;
            if (candidate.waitCount != current.waitCount)
                return candidate.waitCount < current.waitCount;
            // 완전 동률은 생성 순서가 승리하도록 false를 유지한다.
            return false;
        }

        static bool ContainsKey(ulong[] keys, int count, ulong key)
        {
            for (int i = 0; i < count; i++)
                if (keys[i] == key) return true;
            return false;
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
        /// 이번 마일스톤의 단순화이며, 없으면 현재 yaw를 유지한다. CandidateReplayer가
        /// 최종 후보를 재실행할 때도 그대로 재사용한다(같은 조준 규칙이어야 궤적이 일치).
        /// </summary>
        public static float ComputeAimYaw(in SimWorld world)
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
