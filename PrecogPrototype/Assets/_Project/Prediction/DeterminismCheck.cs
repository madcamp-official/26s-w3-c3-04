using Game.Simulation;

namespace Game.Prediction
{
    /// <summary>
    /// 같은 초기 상태 + 같은 입력이면 항상 같은 결과인가.
    /// 예지의 전제 — 이게 깨지면 "본 미래"와 "실제 재생"이 어긋난다(디싱크).
    ///
    /// 방법: 초기 월드를 스냅샷으로 복제 → 두 벌을 같은 입력으로 N틱 굴림
    ///       → 매 틱 해시 비교. 어긋난 첫 틱을 보고한다.
    /// </summary>
    public static class DeterminismCheck
    {
        public struct Result
        {
            public bool passed;
            public int  divergedAtTick;  // -1이면 끝까지 일치
            public int  ticks;
            public ulong finalHashA;
            public ulong finalHashB;
        }

        public static Result Run(int enemyCount = 12, int ticks = 300)
        {
            SimWorld a = BuildWorld(enemyCount);
            SimWorld b = Snapshot.Clone(in a);   // 완전히 같은 시작점

            // 같은 입력 시퀀스 (결정론적으로 변하는 입력 — 재현 가능해야 하므로 틱 기반)
            for (int t = 0; t < ticks; t++)
            {
                InputCmd cmd = MakeInput(t);
                Kernel.Step(ref a, in cmd);
                Kernel.Step(ref b, in cmd);

                ulong ha = WorldHash.Compute(in a);
                ulong hb = WorldHash.Compute(in b);
                if (ha != hb)
                {
                    return new Result
                    {
                        passed = false,
                        divergedAtTick = t,
                        ticks = ticks,
                        finalHashA = ha,
                        finalHashB = hb,
                    };
                }
            }

            return new Result
            {
                passed = true,
                divergedAtTick = -1,
                ticks = ticks,
                finalHashA = WorldHash.Compute(in a),
                finalHashB = WorldHash.Compute(in b),
            };
        }

        /// <summary>틱에 따라 결정론적으로 변하는 입력 (움직임을 넣어 경로를 다양화).</summary>
        static InputCmd MakeInput(int t)
        {
            InputCmd cmd = InputCmd.Empty;
            // 사인/코사인 대신 틱 기반 규칙 — 완전 결정론
            cmd.move = new UnityEngine.Vector2(
                ((t / 30) % 2 == 0) ? 1f : -1f,
                ((t / 45) % 2 == 0) ? 1f : 0f);
            cmd.yaw = (t % 360);
            cmd.jump = (t % 90) == 0;
            return cmd;
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
