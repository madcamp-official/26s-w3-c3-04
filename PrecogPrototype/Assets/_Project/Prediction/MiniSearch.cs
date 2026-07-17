using System.Diagnostics;
using Game.Simulation;

namespace Game.Prediction
{
    /// <summary>
    /// 예지의 축소판. "현재를 복제해서 여러 미래를 가상으로 굴려보고 고른다"가
    /// 실제로 되는지, 그리고 시간 안에 끝나는지를 증명한다.
    ///
    /// 완전한 Beam Search가 아니다 — 몇 개의 행동 시퀀스를 깊이만큼 굴려
    /// 생존/거리로 점수 매기고, 걸린 시간을 잰다. 이게 되면 본격 탐색은
    /// 규모 확장 문제일 뿐이다.
    /// </summary>
    public static class MiniSearch
    {
        public struct Result
        {
            public int    candidates;
            public int    depthTicks;
            public double elapsedMs;
            public int    bestCandidate;
            public float  bestScore;
        }

        // 후보 행동: (좌우, 전후) 이동 방향 몇 가지
        static readonly UnityEngine.Vector2[] Actions =
        {
            new UnityEngine.Vector2( 0f,  1f),  // 전진
            new UnityEngine.Vector2( 1f,  0f),  // 우
            new UnityEngine.Vector2(-1f,  0f),  // 좌
            new UnityEngine.Vector2( 0f, -1f),  // 후진
            new UnityEngine.Vector2( 0f,  0f),  // 정지
        };

        public static Result Run(int enemyCount = 12, int depthTicks = 180)
        {
            SimWorld root = BuildWorld(enemyCount);

            var sw = Stopwatch.StartNew();

            float bestScore = float.NegativeInfinity;
            int bestIdx = -1;

            for (int a = 0; a < Actions.Length; a++)
            {
                // 현재를 복제해서 이 행동으로 미래를 굴린다
                SimWorld sim = Snapshot.Clone(in root);
                InputCmd cmd = InputCmd.Empty;
                cmd.move = Actions[a];

                for (int t = 0; t < depthTicks; t++)
                    Kernel.Step(ref sim, in cmd);

                float score = Evaluate(in sim);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = a;
                }
            }

            sw.Stop();

            return new Result
            {
                candidates = Actions.Length,
                depthTicks = depthTicks,
                elapsedMs = sw.Elapsed.TotalMilliseconds,
                bestCandidate = bestIdx,
                bestScore = bestScore,
            };
        }

        /// <summary>생존 최우선, 그다음 적과 멀수록 좋음 (묶음 A용 단순 점수).</summary>
        static float Evaluate(in SimWorld w)
        {
            if (!w.player.alive) return -1000f;

            float nearest = float.PositiveInfinity;
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive) continue;
                float d = Hit.FlatSqrDist(w.player.pos, w.enemies[i].pos);
                if (d < nearest) nearest = d;
            }
            if (float.IsPositiveInfinity(nearest)) nearest = 0f;
            return UnityEngine.Mathf.Sqrt(nearest); // 가장 가까운 적까지 거리
        }

        static SimWorld BuildWorld(int enemyCount)
        {
            SimWorld w = SimWorld.Create();
            w.player = PlayerState.Spawn(UnityEngine.Vector3.zero);
            for (int i = 0; i < enemyCount; i++)
            {
                float ang = (float)i / enemyCount * 6.2831853f;
                var pos = new UnityEngine.Vector3(
                    UnityEngine.Mathf.Cos(ang) * 6f, 0f,
                    UnityEngine.Mathf.Sin(ang) * 6f);
                w.AddMeleeEnemy(pos);
            }
            return w;
        }
    }
}
