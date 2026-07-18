using System.Collections.Generic;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    /// <summary>
    /// RoutePreviewStub.cs 자신의 주석대로("실제 전투 봇(Sim/Prediction)이 준비되면 이 자리를
    /// 그 산출물로 교체하면 된다") 이 자리를 실제 Game.Prediction(BeamSearch+CandidateReplayer)
    /// 결과로 채운다. 연출(PredictionController)은 PredictedRoute 계약만 소비하므로 그쪽은
    /// 안 건드린다.
    /// </summary>
    public static class RealRoutePreview
    {
        public static List<PredictedRoute> Build(in SimWorld w, in SimServices services, Color[] colors)
        {
            var routes = new List<PredictedRoute>();
            if (w.player.combat.hp <= 0) return routes;

            // F 입력 순간 동기적으로 도는 검색이라 적이 많으면 그만큼 체감 끊김이 생긴다 —
            // 이번 세션에 만든 동적 축소(PredictionSettings.Degrade)를 실제 적 수에 맞춰 적용한다.
            PredictionSettings settings = PredictionSettings.Degrade(PredictionSettings.Full, w.enemyCount);
            CandidatePath[] plans = PredictionPlanner.Plan(in w, in services, settings);

            for (int i = 0; i < plans.Length; i++)
            {
                CandidatePath plan = plans[i];
                bool ok = CandidateReplayer.Replay(in w, in services, plan, settings.macroTicks);
                if (!ok || plan.predictedFrames.Length == 0) continue;

                var route = new PredictedRoute
                {
                    color = colors[i % colors.Length],
                    seconds = plan.durationTicks / (float)SimConfig.TickRate,
                };
                for (int f = 0; f < plan.predictedFrames.Length; f++)
                    route.path.Add(plan.predictedFrames[f].playerPosition);

                CollectKillPositions(in w, in services, plan, settings.macroTicks, route.kills);
                routes.Add(route);
            }
            return routes;
        }

        /// <summary>
        /// 같은 행동열을 한 번 더 재생해서 처치(글로리킬 트리거 포함, BeamSearch와 같은 기준)
        /// 위치만 뽑는다. 결정론이 보장되므로 CandidateReplayer와 항상 같은 결과가 나온다 —
        /// 재생 두 번은 낭비지만, Prediction 쪽 공개 계약(PredictedFrame 등)에 적 처치 위치를
        /// 새로 얹지 않기 위해 View 쪽에서 따로 뽑는 쪽을 택했다.
        /// </summary>
        static void CollectKillPositions(
            in SimWorld initialSnapshot, in SimServices services, CandidatePath candidate,
            int macroTicks, List<Vector3> kills)
        {
            SimWorld world = Snapshot.Clone(in initialSnapshot);
            var wasDefeated = new bool[world.enemyCount];
            for (int i = 0; i < world.enemyCount; i++)
                wasDefeated[i] = !world.enemies[i].alive || world.enemies[i].combat.gloryStage > 0;

            for (int m = 0; m < candidate.actions.Length; m++)
            {
                MacroAction action = candidate.actions[m];
                for (int t = 0; t < macroTicks; t++)
                {
                    float yaw = BeamSearch.ComputeAimYaw(in world);
                    InputCmd cmd = action.ToInputCmd(yaw, t);
                    SimStep.Run(ref world, in cmd, in services);

                    for (int i = 0; i < world.enemyCount; i++)
                    {
                        bool isDefeated = !world.enemies[i].alive || world.enemies[i].combat.gloryStage > 0;
                        if (!wasDefeated[i] && isDefeated)
                            kills.Add(world.enemies[i].pos);
                        wasDefeated[i] = isDefeated;
                    }

                    if (world.player.combat.hp <= 0) return;
                }
            }
        }
    }
}
