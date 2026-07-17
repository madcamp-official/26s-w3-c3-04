using System.Collections.Generic;
using UnityEngine;
using Game.Simulation;

namespace Game.Prediction
{
    /// <summary>
    /// 예지 시각화용. 하나의 정해진 행동(왼쪽 이동 + 주기적 점프)으로 미래를 굴려,
    /// 0.5초 간격 시점마다 플레이어와 모든 적의 위치를 통째로 기록한다.
    ///
    /// 결과(타임라인)를 화살표로 넘기면, 각 시점의 플레이어 잔상과 적 위치를
    /// 볼 수 있다 → "미래가 결정론적으로 계산된다"를 프레임 단위로 확인.
    ///
    /// 시뮬레이션만 참조(GameObject 없음). 그리기는 Runtime이 한다.
    /// </summary>
    public static class PathPreview
    {
        public struct EnemyMark
        {
            public Vector3 pos;
            public bool    alive;
            public bool    stunned;
        }

        /// <summary>한 시점(0.5초 간격)의 월드 스냅샷 요약.</summary>
        public class Frame
        {
            public int         tick;
            public float       time;        // 초
            public Vector3     playerPos;
            public bool        playerAlive;
            public EnemyMark[] enemies;
        }

        /// <summary>
        /// 왼쪽으로 계속 이동 + 주기적 점프. horizonTicks 동안 굴리며
        /// sampleEvery 틱마다(30틱=0.5초) 전체 상태를 기록.
        /// </summary>
        public static List<Frame> BuildTimeline(in SimWorld root,
                                                int horizonTicks = 300,
                                                int sampleEvery = 30,
                                                int jumpEvery = 45)
        {
            var frames = new List<Frame>();
            SimWorld sim = Snapshot.Clone(in root);

            frames.Add(Capture(in sim, 0));

            for (int t = 1; t <= horizonTicks; t++)
            {
                InputCmd cmd = InputCmd.Empty;
                cmd.yaw  = sim.player.yaw;         // 현재 시선 유지
                cmd.move = new Vector2(-1f, 0f);   // 무조건 왼쪽
                cmd.jump = (t % jumpEvery == 0);   // 점프 섞기

                Kernel.Step(ref sim, in cmd);

                if (t % sampleEvery == 0)
                    frames.Add(Capture(in sim, t));
            }

            return frames;
        }

        static Frame Capture(in SimWorld w, int tick)
        {
            var marks = new EnemyMark[w.enemyCount];
            for (int i = 0; i < w.enemyCount; i++)
            {
                marks[i] = new EnemyMark
                {
                    pos     = w.enemies[i].pos,
                    alive   = w.enemies[i].alive,
                    stunned = w.enemies[i].stunTicks > 0,
                };
            }

            return new Frame
            {
                tick        = tick,
                time        = tick * SimConfig.TickDelta,
                playerPos   = w.player.pos,
                playerAlive = w.player.alive,
                enemies     = marks,
            };
        }
    }
}
