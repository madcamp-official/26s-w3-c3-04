using System.Diagnostics;
using Game.Simulation;

namespace Game.Prediction
{
    /// <summary>
    /// Step() 1회의 실측 비용을 잰다. 이 숫자가 프로젝트의 사활을 가른다 —
    /// 탐색이 수만 틱을 도는데 Step이 무거우면 예지가 불가능해진다.
    /// (LOS Raycast는 아직 없음 → 실제 비용은 이보다 늘 수 있음. 별도 재측정 대상)
    /// </summary>
    public static class Benchmark
    {
        public struct Result
        {
            public int   enemyCount;
            public int   iterations;
            public double usPerStep;      // Step 1회 마이크로초
            public double projected5sMs;  // 5초 예지 탐색 추정 (ms)
        }

        public static Result Run(int enemyCount = 12, int iterations = 100000)
        {
            SimWorld world = BuildWorld(enemyCount);
            InputCmd cmd = InputCmd.Empty;

            // 워밍업 (JIT 비용 제거)
            for (int i = 0; i < 2000; i++)
                Kernel.Step(ref world, in cmd);

            // 측정 (매 반복 같은 상태에서 시작하지 않고 연속 진행 —
            //  Step 자체의 평균 비용만 보면 되므로 연속 진행으로 충분)
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                Kernel.Step(ref world, in cmd);
            sw.Stop();

            double us = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;

            // 5초 탐색 추정: 깊이 20 × Beam 18 × 행동 6 × 15틱 = 32,400 틱
            const double searchTicks = 20 * 18 * 6 * 15;

            return new Result
            {
                enemyCount = enemyCount,
                iterations = iterations,
                usPerStep = us,
                projected5sMs = us * searchTicks / 1000.0,
            };
        }

        static SimWorld BuildWorld(int enemyCount)
        {
            SimWorld w = SimWorld.Create();
            w.player = PlayerState.Spawn(UnityEngine.Vector3.zero);

            // 적을 원 둘레에 배치해 전부 추적 상태가 되게 한다 (최악 비용 근접)
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
