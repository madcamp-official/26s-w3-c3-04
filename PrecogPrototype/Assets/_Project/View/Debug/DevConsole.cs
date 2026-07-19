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
