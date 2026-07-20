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
                    Print("종류: grunt pinky soldier caco large");
                    Print("wave list · wave start [n] · wave only <n> · wave stop · wave status");
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
                    for (int w = 0; w < wc; w++) sb.Append($" / W{w}:{a.SpawnCountOf(w)}마리");
                    Print(sb.ToString());
                }
                return;
            }

            ArenaWaves target = Nearest(arenas);
            WaveRunner runner = target.GetComponent<WaveRunner>();
            if (runner == null) runner = target.gameObject.AddComponent<WaveRunner>();   // 없으면 자동 부착

            switch (sub)
            {
                case "start":
                {
                    int n = 0;
                    if (p.Length >= 3) int.TryParse(p[2], out n);
                    Print(runner.StartFrom(n, true));
                    break;
                }
                case "only":
                {
                    if (p.Length < 3) { Print("사용: wave only <번호>"); break; }
                    int n; if (!int.TryParse(p[2], out n)) { Print("번호가 숫자가 아닙니다"); break; }
                    Print(runner.StartFrom(n, false));
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
