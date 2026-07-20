using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 웨이브 런타임. ArenaWaves(데이터)를 읽어 스폰 진행·진행조건 감시·다음 웨이브를 돌린다.
    /// 한 웨이브의 여러 배관은 **각자 독립적으로 동시에** 자기 몹 목록을 뱉는다.
    /// View 계층 — 스폰은 Main을 통해 sim에 주입한다(기존 스폰 경로와 동일).
    /// 웨이브 소속 추적은 Sim을 건드리지 않고 "스폰한 적 id 집합"으로 한다(결정론 영향 0).
    /// 설계 문서: docs/shared/웨이브_시스템_설계.md
    /// </summary>
    [DisallowMultipleComponent]
    public class WaveRunner : MonoBehaviour
    {
        public enum State { Idle, WaitStart, Spawning, Watching, Done }

        [Tooltip("비워두면 같은 오브젝트의 ArenaWaves를 자동으로 쓴다.")]
        public ArenaWaves config;

        public State CurrentState { get; private set; } = State.Idle;
        public int   CurrentWave  { get; private set; } = -1;

        /// <summary>배관 하나의 진행 커서 — 배관마다 독립적으로 돈다.</summary>
        struct PipeCursor
        {
            public int index;       // 다음에 뱉을 몹 번호
            public int waitTicks;   // 남은 대기 틱
            public bool done;
        }

        // 웨이브별로 "이 웨이브가 스폰한 적 id" — 잔당이 다음 웨이브로 넘어가도 소속이 유지된다.
        readonly List<HashSet<int>> spawnedIds = new List<HashSet<int>>();
        PipeCursor[] cursors = new PipeCursor[0];
        int  waveWaitTicks;
        int  spawnedSoFar;
        bool sequential;   // true = 조건 충족 시 다음 웨이브로, false = 이 웨이브만

        void Awake()
        {
            if (config == null) config = GetComponent<ArenaWaves>();
        }

        // ── 외부(개발 콘솔) 진입점 ──

        /// <summary>index 웨이브부터 시작. runAll=true면 이후 웨이브까지 순차 진행.</summary>
        public string StartFrom(int index, bool runAll)
        {
            if (config == null) config = GetComponent<ArenaWaves>();
            if (config == null) return "ArenaWaves 컴포넌트가 없습니다";
            int count = config.waves != null ? config.waves.Length : 0;
            if (!config.HasWave(index)) return $"웨이브 {index} 없음 (유효: 0~{count - 1})";

            sequential = runAll;
            BeginWave(index);
            return $"[{name}] 웨이브 {index} 시작" + (runAll ? " (이후 순차 진행)" : " (단일)");
        }

        public void Stop()
        {
            CurrentState = State.Idle;
            CurrentWave  = -1;
        }

        public string Status()
        {
            if (config == null) return $"[{name}] ArenaWaves 없음";
            if (CurrentState == State.Idle)
                return $"[{name}] 대기 중 (웨이브 {(config.waves != null ? config.waves.Length : 0)}개)";
            int alive = AliveOfWave(CurrentWave);
            int total = config.SpawnCountOf(CurrentWave);
            return $"[{name}] 웨이브 {CurrentWave} · {CurrentState} · 배관 {config.PipeCountOf(CurrentWave)}개 · " +
                   $"스폰 {spawnedSoFar}/{total} · 생존 {alive}";
        }

        // ── 진행 ──

        void BeginWave(int index)
        {
            CurrentWave  = index;
            spawnedSoFar = 0;
            while (spawnedIds.Count <= index) spawnedIds.Add(new HashSet<int>());
            spawnedIds[index].Clear();

            Wave w = config.waves[index];
            int pipeCount = w.pipes != null ? w.pipes.Length : 0;
            cursors = new PipeCursor[pipeCount];
            for (int i = 0; i < pipeCount; i++)
            {
                PipeEmission p = w.pipes[i];
                bool empty = p == null || p.marker == null || p.MobCount == 0;
                cursors[i] = new PipeCursor
                {
                    index = 0,
                    waitTicks = empty ? 0 : Sec2Ticks(p.startDelay),   // 배관별 시작 지연
                    done = empty,
                };
            }

            waveWaitTicks = Sec2Ticks(w.startDelay);
            CurrentState  = waveWaitTicks > 0 ? State.WaitStart : State.Spawning;
        }

        // sim과 같은 주기(고정 틱)에서 돌려 스폰 타이밍을 틱에 정확히 맞춘다.
        void FixedUpdate()
        {
            switch (CurrentState)
            {
                case State.WaitStart:
                    if (--waveWaitTicks <= 0) CurrentState = State.Spawning;
                    break;
                case State.Spawning:
                    TickSpawning();
                    break;
                case State.Watching:
                    TickWatching();
                    break;
            }
        }

        /// <summary>모든 배관을 이번 틱에 각자 한 번씩 진행시킨다(동시 진행).</summary>
        void TickSpawning()
        {
            Wave w = config.waves[CurrentWave];
            bool allDone = true;

            for (int i = 0; i < cursors.Length; i++)
            {
                if (cursors[i].done) continue;
                allDone = false;

                if (cursors[i].waitTicks > 0) { cursors[i].waitTicks--; continue; }

                PipeEmission pipe = w.pipes[i];

                // 간격 0인 몹은 같은 틱에 이어서 뱉는다(동시 방출 묶음).
                while (cursors[i].index < pipe.MobCount)
                {
                    SpawnOne(pipe, pipe.mobs[cursors[i].index]);
                    cursors[i].index++;
                    if (cursors[i].index >= pipe.MobCount) break;

                    MobEmit next = pipe.mobs[cursors[i].index];
                    float iv = next.intervalOverride < 0f ? pipe.interval : next.intervalOverride;
                    int t = Sec2Ticks(iv);
                    if (t > 0) { cursors[i].waitTicks = t; break; }   // 다음 마리는 나중 틱에
                }

                if (cursors[i].index >= pipe.MobCount) cursors[i].done = true;
            }

            if (allDone) CurrentState = State.Watching;
        }

        void SpawnOne(PipeEmission pipe, MobEmit emit)
        {
            Main main = Main.Instance;
            if (main == null) return;

            var (c, m, s) = MapSpawnConfig.Axes(emit.kind);

            // 【임시방편】 마커가 벽·천장에 붙어 있어 몹이 끼는 것을 피하려고 아래로 조금 내려서 스폰한다.
            // 근본 해결(마커를 벽면에서 띄우기 / 스폰 시 충돌 밀어내기 / §4 펄스 도입) 후 0으로 되돌릴 것.
            Vector3 at = pipe.marker.position + Vector3.down * config.spawnDropOffset;

            int id = main.SpawnEnemyAt(at, c, m, s);
            if (id >= 0) { spawnedIds[CurrentWave].Add(id); spawnedSoFar++; }

            SpawnPipeFx.Play(pipe.marker);   // 배관 꿀렁 연출(View 전용)
            // TODO(설계 §4): 여기서 marker.forward 방향 펄스 + 착지까지 무공격 상태를 부여한다(Sim 확장 후).
        }

        void TickWatching()
        {
            Wave w = config.waves[CurrentWave];
            int alive = AliveOfWave(CurrentWave);
            int total = config.SpawnCountOf(CurrentWave);

            bool advance;
            switch (w.advance)
            {
                case WaveAdvanceMode.RemainingCount:
                    advance = alive <= Mathf.Max(0, Mathf.RoundToInt(w.advanceValue));
                    break;
                case WaveAdvanceMode.RemainingPercent:
                    advance = total <= 0 || alive <= total * (w.advanceValue / 100f);
                    break;
                default:   // KillAll
                    advance = alive <= 0;
                    break;
            }
            if (!advance) return;

            int next = CurrentWave + 1;
            if (sequential && config.HasWave(next))
            {
                Debug.Log($"[WaveRunner] {name} 웨이브 {CurrentWave} 조건 충족(생존 {alive}/{total}) → 웨이브 {next}");
                BeginWave(next);
            }
            else
            {
                CurrentState = State.Done;
                Debug.Log($"[WaveRunner] {name} 웨이브 {CurrentWave} 완료 — 아레나 클리어(문 해제 훅 자리).");
                // TODO(설계 §5): 마무리 정리(시야각+LOS·거리·지속) 및 문 해제 훅.
            }
        }

        int AliveOfWave(int index)
        {
            if (index < 0 || index >= spawnedIds.Count) return 0;
            Main main = Main.Instance;
            return main != null ? main.AliveCountAmong(spawnedIds[index]) : 0;
        }

        /// <summary>초 → 틱(60틱 = 1초). Inspector는 초로 입력, 내부는 틱으로 정확히 돈다.</summary>
        static int Sec2Ticks(float seconds)
            => seconds <= 0f ? 0 : Mathf.Max(0, Mathf.RoundToInt(seconds / SimConfig.TickDelta));
    }
}
