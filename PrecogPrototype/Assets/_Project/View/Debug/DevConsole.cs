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

        // ── 몹 애니메이션 상태 관찰 오버레이(anim 명령) ──
        bool animWatch;

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
            if (animWatch) DrawAnimWatch();
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

        /// <summary>anim 명령으로 켠 관찰 오버레이 — Enemy_0의 애니메이터 상태를 매 프레임 표시.</summary>
        void DrawAnimWatch()
        {
            var main = Main.Instance;
            if (main == null) return;

            var sb = new StringBuilder();
            sb.Append("[anim watch] Enemy_0: ");
            var go = GameObject.Find("Enemy_0");
            if (go == null)
            {
                sb.Append("없음(죽었거나 아직 생성 안 됨)");
            }
            else
            {
                var anim = go.GetComponentInChildren<Animator>();
                if (anim == null)
                {
                    sb.Append("Animator 없음(공중/캡슐 유닛)");
                }
                else
                {
                    var st = anim.GetCurrentAnimatorStateInfo(0);
                    sb.Append("t=" + st.normalizedTime.ToString("0.00") + " speed=" + anim.speed.ToString("0.00"));
                    foreach (var pname in new[] { "IsAttacking", "IsAiming", "IsCharging", "IsAirborne" })
                    {
                        bool has = false;
                        foreach (var pp in anim.parameters) if (pp.name == pname) { has = true; break; }
                        if (has) sb.Append("  " + pname + "=" + anim.GetBool(pname));
                    }
                }

                ref readonly SimWorld w = ref main.World;
                if (w.enemyCount > 0)
                    sb.Append("\nsim: ai.state=" + w.enemies[0].ai.state + "  grounded=" + w.enemies[0].grounded);
            }

            const float boxW = 480f, boxH = 54f;
            GUI.Box(new Rect(6, 6, boxW, boxH), GUIContent.none);
            GUI.Label(new Rect(12, 8, boxW - 12, boxH - 4), sb.ToString());
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
                    Print("spawn <종류> · solo <종류> · anim <종류>|off · clear · autospawn on|off|reload · count");
                    Print("종류: grunt pinky soldier caco large gruntt(근층) soldiert(원층)");
                    Print("[디버그] anim <종류> = 그 몹 하나만 소환하고 화면에 애니메이터 상태 실시간 표시. anim off로 끔");
                    Print("wave list · wave start [n] · wave only <n> · wave stop · wave status  (n은 1부터)");
                    Print("  예) wave only 2 = 웨이브2만 실행 · wave start 2 = 웨이브2부터 순차");
                    Print("[이펙트] vfx <이름> [거리] [pitch] [yaw] [roll] [상하] · vfx list · vfx reload");
                    Print("  pitch=위아래로 눕히기 · yaw=좌우로 돌리기 · roll=화면 안 각도 (전부 0=원래 방향)");
                    Print("[이펙트] slash 1|2|t|down [거리] [pitch] [yaw] [roll] [상하]");
                    Print("[컷신] cut · cut stop   (C 키와 동일. 리그는 Tools/컷신/①로 설치)");
                    Print("[이펙트] swing 1|2|t [loop] · swing stop   (칼 애니메이션 재생)");
                    Print("[절차] proc land [강도] · proc hit · proc breathe [0~1] · proc grip [0~1] · proc pulse · proc reset");
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

                case "anim":
                    if (p.Length < 2) { Print("사용: anim <종류> | anim off"); break; }
                    if (p[1].ToLowerInvariant() == "off") { animWatch = false; Print("애니메이션 관찰 꺼짐"); break; }
                    if (TryType(p[1], out var c3, out var m3, out var s3))
                    {
                        main.AutoSpawn = false;
                        main.DevClear();
                        main.DevSpawn(c3, m3, s3);
                        animWatch = true;
                        Print("애니메이션 관찰: " + p[1] + " (Enemy_0, 화면 좌상단 표시. 끄려면 anim off)");
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

                // ── 절차 애니메이션 테스트 (전투 없이 즉시 발동) ──
                case "proc":
                {
                    var sv = SwordView.Instance;
                    var fp = FingerPoser.Instance;
                    string sub = p.Length >= 2 ? p[1].ToLowerInvariant() : "";
                    float arg = 0f;
                    bool hasArg = p.Length >= 3 && float.TryParse(p[2], out arg);

                    switch (sub)
                    {
                        case "land":
                            if (sv == null) { Print("SwordView 없음"); break; }
                            sv.KickLand(hasArg ? arg : 8f);
                            Print("착지 딥 발동 (강도 " + (hasArg ? arg : 8f) + ")");
                            break;

                        case "hit":
                            if (sv == null) { Print("SwordView 없음"); break; }
                            sv.KickHit();
                            Print("피격 움찔 발동");
                            break;

                        case "breathe":
                            if (sv == null) { Print("SwordView 없음"); break; }
                            sv.breatheHpOverride = hasArg ? Mathf.Clamp01(arg) : -1f;
                            Print(hasArg ? $"숨고르기 HP={arg:0.00} 로 강제 (0=빈사, 1=멀쩡)" : "숨고르기 실제 HP로 복귀");
                            break;

                        case "grip":
                            if (fp == null) { Print("FingerPoser 없음 — 손 뼈에 붙이십시오"); break; }
                            fp.grip = hasArg ? Mathf.Clamp01(arg) : 0f;
                            Print("손가락 그립 = " + fp.grip.ToString("0.00"));
                            break;

                        case "pulse":
                            if (fp == null) { Print("FingerPoser 없음 — 손 뼈에 붙이십시오"); break; }
                            fp.PulseGrip(hasArg ? arg : 0.5f);
                            Print("손가락 확 쥠 (세기 " + (hasArg ? arg : 0.5f) + ")");
                            break;

                        case "reset":
                            if (sv != null) sv.ResetProcedural();
                            if (fp != null) { fp.ResetGrip(); fp.grip = 0f; }
                            Print("절차 오프셋 리셋");
                            break;

                        default:
                            Print("사용: proc land [강도] · proc hit · proc breathe [0~1]");
                            Print("      proc grip [0~1] · proc pulse [세기] · proc reset");
                            Print("  튜닝은 SwordView / FingerPoser 컴포넌트 Inspector에서 실시간 조절");
                            break;
                    }
                    break;
                }

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
                    // 평타1/평타2/찌르기 매핑 + 위→아래(down) 프리셋. 바꾸려면 여기만 수정.
                    // 기본 방향 보정(pitch 90) 위에서 roll로 화면상 베는 각도를 정한다.
                    string vname; float spitch = VfxBasePitch, syaw = 0f, sroll = 0f, sup = 0f;
                    switch (p.Length >= 2 ? p[1] : "")
                    {
                        case "1":    vname = "Slash_Basic";  sroll = -45f; break;   // 우상 → 좌하
                        case "2":    vname = "Slash_Double"; sroll =  45f; break;   // 좌상 → 우하
                        case "t": case "thrust": vname = "Slash_Multi"; sroll = 0f; break;   // 수평
                        case "down": vname = "Slash_Basic";  sroll = 90f; sup = 0.35f; break; // 위 → 아래
                        default:
                            Print("사용: slash 1|2|t|down [거리] [pitch] [yaw] [roll] [상하]");
                            Print("  1=우상→좌하 · 2=좌상→우하 · t=수평 · down=위→아래");
                            Print("  roll이 화면상 각도(0=수평, 90=수직). pitch 90은 이 에셋의 기본 보정");
                            vname = null; break;
                    }
                    if (vname == null) break;

                    float sd = 2.2f;
                    if (p.Length >= 3) float.TryParse(p[2], out sd);
                    if (p.Length >= 4) float.TryParse(p[3], out spitch);
                    if (p.Length >= 5) float.TryParse(p[4], out syaw);
                    if (p.Length >= 6) float.TryParse(p[5], out sroll);
                    if (p.Length >= 7) float.TryParse(p[6], out sup);

                    Print(SpawnVfxInView(main, vname, sd, spitch, syaw, sroll, sup)
                        ? "베기: " + p[1] + " → " + vname + "  pitch=" + spitch + " yaw=" + syaw + " roll=" + sroll
                        : "프리팹 없음: " + vname + " (vfx list 로 확인)");
                    break;
                }

                case "cut":
                {
                    // System.Object / UnityEngine.Object 이름 충돌(using System + using UnityEngine) → 명시
                    var cm = UnityEngine.Object.FindFirstObjectByType<CutsceneManager>();
                    if (cm == null) { Print("CutsceneManager 없음 — Tools/컷신/① 리그 설치 필요"); break; }
                    if (p.Length >= 2 && p[1] == "stop") { cm.StopFromConsole(); Print("컷신 중단"); break; }
                    Print(cm.PlayFromConsole() ? "컷신 재생" : "재생 불가 (이미 재생 중이거나 리그 미설치)");
                    break;
                }

                case "vfx":
                {
                    if (p.Length >= 2 && p[1] == "list")
                    {
                        var names = VfxLibrary.Names();
                        if (names.Count == 0) Print("VFX 없음 — Assets/_Project/Prefabs/Resources/VFX/ 에 프리팹을 넣으십시오");
                        else foreach (var n in names) Print("  " + n);
                        break;
                    }
                    if (p.Length >= 2 && p[1] == "reload") { VfxLibrary.Reload(); Print("VFX 폴더 다시 읽음"); break; }
                    if (p.Length < 2)
                    {
                        Print("사용: vfx <이름> [거리] [pitch] [yaw] [roll] [상하]");
                        Print("  기본 pitch=" + VfxBasePitch + " (이 에셋 팩 방향 보정). roll이 화면상 각도");
                        Print("  예) vfx Slash_Basic 2.2 90 0 90   ← 위→아래 수직");
                        Print("  vfx list · vfx reload");
                        break;
                    }

                    float d = 2.2f, vpitch = VfxBasePitch, vyaw = 0f, vroll = 0f, vup = 0f;
                    if (p.Length >= 3) float.TryParse(p[2], out d);
                    if (p.Length >= 4) float.TryParse(p[3], out vpitch);
                    if (p.Length >= 5) float.TryParse(p[4], out vyaw);
                    if (p.Length >= 6) float.TryParse(p[5], out vroll);
                    if (p.Length >= 7) float.TryParse(p[6], out vup);
                    Print(SpawnVfxInView(main, p[1], d, vpitch, vyaw, vroll, vup)
                        ? "재생: " + p[1] + "  pitch=" + vpitch + " yaw=" + vyaw + " roll=" + vroll + " 상하=" + vup
                        : "없는 VFX: " + p[1] + "  (vfx list 로 확인)");
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

        /// <summary>
        /// 1인칭 시선 기준으로 VFX를 재생한다 — 실제 카메라 위치·회전(피치 포함) 앞에 내고,
        /// 카메라에 붙여 시점을 돌려도 화면에 남게 한다(테스트·튜닝 편의).
        /// </summary>
        /// <summary>
        /// 1인칭 시선 기준 VFX 재생. 회전은 카메라 기준 3축을 전부 노출한다
        /// (프리팹마다 authoring 방향이 달라 맞는 축을 직접 찾아야 함).
        ///   pitch = 카메라 오른쪽 축(X) 기준 — 위아래로 눕히기
        ///   yaw   = 카메라 위쪽 축(Y) 기준   — 좌우로 돌리기
        ///   roll  = 시선 축(Z) 기준          — 화면 안에서 각도만 바꾸기
        /// </summary>
        /// <summary>
        /// 이 슬래시 에셋 팩(Matthew Guz)의 기본 방향 보정.
        /// 프리팹이 눕혀진 채로 authoring돼 있어서 pitch 90을 줘야 화면을 가로지른다.
        /// 이 값을 기준으로 roll이 화면상 베는 각도가 된다(0=수평, 90=수직).
        /// </summary>
        public const float VfxBasePitch = 90f;

        static bool SpawnVfxInView(Main main, string name, float dist,
                                   float pitch = VfxBasePitch, float yaw = 0f, float roll = 0f,
                                   float up = 0f, float right = 0f)
        {
            var cam = main != null ? main.Cam : null;
            if (cam == null) return false;
            Transform ct = cam.transform;
            Vector3 pos = ct.position + ct.forward * dist + ct.up * up + ct.right * right;
            Quaternion rot = ct.rotation * Quaternion.Euler(pitch, yaw, roll);
            var inst = VfxLibrary.Play(name, pos, rot);
            if (inst == null) return false;
            inst.transform.SetParent(ct, true);
            return true;
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
