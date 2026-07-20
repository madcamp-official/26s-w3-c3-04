using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 개발용 커맨드 콘솔. ` 로 토글. 몹을 조합 지정해 소환하고 패턴을 관찰한다.
    /// IMGUI(개발툴 표준) — 릴리스에서 빼려면 이 컴포넌트 생성부를 DEVELOPMENT_BUILD로 감싸면 된다.
    /// 콘솔이 열려 있는 동안 Main이 플레이어 입력을 막는다(sim은 계속 돎).
    /// </summary>
    public class DevConsole : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        const string ControlName = "devconsole_input";
        string input = "";
        readonly StringBuilder log = new StringBuilder();
        Vector2 scroll;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.backquoteKey.wasPressedThisFrame) { if (IsOpen) Close(); else Open(); }
            else if (IsOpen && kb.escapeKey.wasPressedThisFrame) Close();
        }

        void Open()
        {
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (log.Length == 0) Print("명령어: help  (예: solo pinky)");
        }

        void Close()
        {
            IsOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            float w = Screen.width;
            const float h = 220f;
            GUI.Box(new Rect(0, 0, w, h), GUIContent.none);

            scroll = GUI.BeginScrollView(new Rect(6, 6, w - 12, h - 38), scroll,
                                         new Rect(0, 0, w - 40, Mathf.Max(2000f, 0f)));
            GUI.Label(new Rect(0, 0, w - 40, 2000f), log.ToString());
            GUI.EndScrollView();

            Event e = Event.current;
            bool enter = e.type == EventType.KeyDown &&
                         (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

            GUI.SetNextControlName(ControlName);
            input = GUI.TextField(new Rect(6, h - 30, w - 12, 24), input);
            input = input.Replace("`", "");        // 토글 키가 입력창에 안 들어가게
            GUI.FocusControl(ControlName);         // 열자마자 바로 타이핑

            if (enter && !string.IsNullOrWhiteSpace(input))
            {
                Execute(input.Trim());
                input = "";
                e.Use();
            }
        }

        void Execute(string line)
        {
            Print("> " + line);
            string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Main main = Main.Instance;
            if (main == null) { Print("Main 없음"); return; }

            switch (p[0].ToLowerInvariant())
            {
                case "help":
                    Print("spawn <종류> · solo <종류> · clear · autospawn on|off|reload · count");
                    Print("종류: grunt pinky soldier caco large gruntt(근층) soldiert(원층)");
                    Print("wave list · wave start [n] · wave only <n> · wave stop · wave status  (n은 1부터)");
                    Print("  예) wave only 2 = 웨이브2만 실행 · wave start 2 = 웨이브2부터 순차");
                    Print("[이펙트] swing 1|2|t [loop] · swing stop   (칼 애니메이션 재생)");
                    Print("[이펙트] vfx list · vfx <이름> [here] · vfx reload");
                    Print("[이펙트] timescale 0.15  (슬로우모션. 1로 원복)");
                    break;

                case "wave":
                    Wave(p);
                    break;

                case "spawn":
                    if (p.Length < 2) { Print("사용: spawn <종류>"); break; }
                    if (TryType(p[1], out var c, out var m, out var s))
                    { main.DevSpawn(c, m, s); Print("소환: " + p[1]); }
                    else Print("모르는 종류: " + p[1]);
                    break;

                case "solo":
                    if (p.Length < 2) { Print("사용: solo <종류>"); break; }
                    if (TryType(p[1], out var c2, out var m2, out var s2))
                    {
                        main.AutoSpawn = false;
                        main.DevClear();
                        main.DevSpawn(c2, m2, s2);
                        Print("솔로: " + p[1] + " (자동소환 off)");
                    }
                    else Print("모르는 종류: " + p[1]);
                    break;

                case "clear":
                    main.DevClear();
                    Print("전부 제거");
                    break;

                case "autospawn":
                    if (p.Length >= 2 && p[1] == "on")  { main.AutoSpawn = true;  Print("자동소환 on"); }
                    else if (p.Length >= 2 && p[1] == "off") { main.AutoSpawn = false; Print("자동소환 off"); }
                    else if (p.Length >= 2 && p[1] == "reload") { main.ReloadSpawnConfig(); Print("씬 스폰 세팅 다시 읽음"); }
                    else Print("사용: autospawn on|off|reload");
                    break;

                case "count":
                    Print("생존: " + main.AliveEnemyCount());
                    break;

                // ── 이펙트 튜닝용 ──
                case "swing":
                {
                    var sv = SwordView.Instance;
                    if (sv == null) { Print("SwordView 없음"); break; }
                    if (p.Length >= 2 && p[1] == "stop") { sv.StopPreview(); Print("스윙 프리뷰 중지"); break; }
                    if (p.Length < 2) { Print("사용: swing 1|2|t [loop] · swing stop"); break; }
                    bool loop = p.Length >= 3 && p[2] == "loop";
                    if (sv.PreviewSwing(p[1], loop)) Print("스윙 재생: " + p[1] + (loop ? " (반복)" : ""));
                    else Print("사용: swing 1|2|t [loop]");
                    break;
                }

                case "slash":
                {
                    if (p.Length < 2) { Print("사용: slash 1|2|t [hold]   (베기 이펙트. 칼 움직임과 무관)"); break; }
                    float syr = main.LookYaw * Mathf.Deg2Rad;
                    Vector3 sfwd = new Vector3(Mathf.Sin(syr), 0f, Mathf.Cos(syr));
                    Vector3 spos = main.World.player.pos + Vector3.up * 1.3f + sfwd * 3f;
                    var sl = SwordSlash.Spawn(spos, Quaternion.LookRotation(sfwd), p[1]);
                    bool shold = p.Length >= 3 && p[2] == "hold";
                    if (sl != null && shold) sl.hold = true;
                    Print("베기: " + p[1] + (shold ? " (hold — Hierarchy의 SwordSlash 선택해 튜닝)" : ""));
                    break;
                }

                case "vfx":
                {
                    if (p.Length >= 2 && p[1] == "list")
                    {
                        var names = VfxLibrary.Names();
                        if (names.Count == 0) Print("VFX 없음 — Resources/VFX/ 에 프리팹을 넣으십시오");
                        else foreach (var n in names) Print("  " + n);
                        break;
                    }
                    if (p.Length >= 2 && p[1] == "reload") { VfxLibrary.Reload(); Print("VFX 폴더 다시 읽음"); break; }
                    if (p.Length < 2) { Print("사용: vfx list | vfx reload | vfx <이름> [here]"); break; }

                    float yr = main.LookYaw * Mathf.Deg2Rad;
                    Vector3 fwd = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
                    Vector3 at = main.World.player.pos + Vector3.up * 1.2f;
                    if (!(p.Length >= 3 && p[2] == "here")) at += fwd * 3f;   // 기본: 정면 3m
                    var inst = VfxLibrary.Play(p[1], at, Quaternion.LookRotation(-fwd));
                    Print(inst != null ? "재생: " + p[1] : "없는 VFX: " + p[1] + "  (vfx list 로 확인)");
                    break;
                }

                case "ts":
                case "timescale":
                    if (p.Length < 2) { Print("현재 timescale=" + Time.timeScale + " (사용: timescale 0.15)"); break; }
                    if (float.TryParse(p[1], out float tsv)) { Time.timeScale = Mathf.Clamp(tsv, 0f, 4f); Print("timescale=" + Time.timeScale); }
                    else Print("숫자를 입력하십시오");
                    break;

                default:
                    Print("모르는 명령: " + p[0]);
                    break;
            }
        }

        // ── 웨이브 명령 ──
        // 아레나를 씬에서 런타임에 찾으므로, 지금 테스트 씬이든 나중에 프리팹을 이어붙인
        // 최종 맵(아레나 여러 개)이든 그대로 동작한다. 여러 개면 플레이어에게 가장 가까운 아레나.
        void Wave(string[] p)
        {
            var arenas = UnityEngine.Object.FindObjectsByType<ArenaWaves>(FindObjectsSortMode.None);
            if (arenas.Length == 0) { Print("씬에 ArenaWaves가 없습니다. (Tools/맵 부속/테스트 웨이브 생성)"); return; }

            string sub = p.Length >= 2 ? p[1].ToLowerInvariant() : "status";

            if (sub == "list")
            {
                Print($"아레나 {arenas.Length}개:");
                for (int i = 0; i < arenas.Length; i++)
                {
                    ArenaWaves a = arenas[i];
                    int wc = a.waves != null ? a.waves.Length : 0;
                    var sb = new StringBuilder($"  [{i}] {a.name} — 웨이브 {wc}개");
                    for (int w = 0; w < wc; w++) sb.Append($" / W{w + 1}:{a.SpawnCountOf(w)}마리");
                    Print(sb.ToString());
                }
                return;
            }

            ArenaWaves target = Nearest(arenas);
            WaveRunner runner = target.GetComponent<WaveRunner>();
            if (runner == null) runner = target.gameObject.AddComponent<WaveRunner>();   // 없으면 자동 부착

            switch (sub)
            {
                // 번호는 1부터(표시 이름 W1·W2·W3과 일치). 내부 인덱스는 0부터라 -1 한다.
                case "start":
                {
                    int n = 1;
                    if (p.Length >= 3) int.TryParse(p[2], out n);
                    Print(runner.StartFrom(n - 1, true));
                    break;
                }
                case "only":
                case "go":
                {
                    if (p.Length < 3) { Print("사용: wave only <번호>  (1부터)"); break; }
                    int n; if (!int.TryParse(p[2], out n)) { Print("번호가 숫자가 아닙니다"); break; }
                    Print(runner.StartFrom(n - 1, false));
                    break;
                }
                case "stop":
                    runner.Stop();
                    Print($"[{target.name}] 웨이브 정지");
                    break;
                case "status":
                    Print(runner.Status());
                    break;
                default:
                    Print("사용: wave list | start [n] | only <n> | stop | status");
                    break;
            }
        }

        /// <summary>플레이어에게 가장 가까운 아레나(하나뿐이면 그것).</summary>
        static ArenaWaves Nearest(ArenaWaves[] arenas)
        {
            if (arenas.Length == 1 || Main.Instance == null) return arenas[0];
            Vector3 p = Main.Instance.World.player.pos;
            ArenaWaves best = arenas[0];
            float bestSq = float.MaxValue;
            foreach (ArenaWaves a in arenas)
            {
                float sq = (a.transform.position - p).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = a; }
            }
            return best;
        }

        static bool TryType(string alias, out CombatType c, out MobilityType m, out SizeClass s)
        {
            c = CombatType.Melee; m = MobilityType.Ground; s = SizeClass.Normal;
            if (!MapSpawnConfig.TryParse(alias, out var k)) return false;
            (c, m, s) = MapSpawnConfig.Axes(k);   // 종류 매핑은 MapSpawnConfig 한 곳에서
            return true;
        }

        void Print(string msg)
        {
            log.Append(msg).Append('\n');
            scroll.y = float.MaxValue;   // 항상 최신으로 스크롤
        }
    }
}
