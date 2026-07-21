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
                    Print("[이펙트] vfx <이름> [거리] [pitch] [yaw] [roll] [상하] · vfx list · vfx reload");
                    Print("  pitch=위아래로 눕히기 · yaw=좌우로 돌리기 · roll=화면 안 각도 (전부 0=원래 방향)");
                    Print("[이펙트] slash 1|2|t|down [거리] [pitch] [yaw] [roll] [상하]");
                    Print("[포즈] pose list · pose <이름>   (JSON 포즈 즉시 적용)");
                    Print("[포즈] play <접두어> [초] [loop] [linear|spring] · play stop(기본포즈로) · play release(제어 해제)");
                    Print("[포즈] combo <접두어...> [초] [loop]   예) combo 평타1_ 평타2_ 0.35 loop");
                    Print("[포즈] seq <이름...> [loop]   예) seq 평타1_0 평타1_1 평타1_4  (간격은 F3에서 각각 조절)");
                    Print("[포즈] bind [on|off|<초>]   전투 연동 — 평타 slash1↔slash2, 찌르기 thrust1");
                    Print("[화면] ui [off|on]   게임 UI(HP바 등) 숨기기 — 개발 패널은 유지");
                    Print("[베기] fx <평타1|평타2|찌르기> · fx off|on   (각도·위치는 F4에서 조절)");
                    Print("[뷰모델] vm · vm info   뷰모델 소환·재생성 (프리팹 저장은 Tools/뷰모델/③)");
                    Print("[뷰모델] vcam · vcam auto · vcam back <m> · vcam near <m>   팔 뚫림(후퇴량·근평면)");
                    Print("[컷신] cut · cut stop   (C 키와 동일. 리그는 Tools/컷신/①로 설치)");
                    Print("[이펙트] swing 1|2|t [loop] · swing stop   (칼 애니메이션 재생)");
                    Print("[절차] proc land [강도] · proc hit · proc breathe [0~1] · proc grip [0~1] · proc pulse · proc reset");
                    Print("[전투] lunge [on|off|<거리m>]   찌르기 자유 시전 — 몹 없어도 발동·스택 무한");
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

                // ── 찌르기 자유 시전 (몹 없이 애니메이션 확인) ──
                case "lunge":
                {
                    if (p.Length >= 2 && float.TryParse(p[1], out float ldist))
                    {
                        CombatConfig.DevLungeBlinkDist = Mathf.Max(0f, ldist);
                        Print($"찌르기 전진 거리 = {CombatConfig.DevLungeBlinkDist:0.0}m (0=제자리)");
                        break;
                    }
                    if (p.Length >= 2 && p[1] == "off")      CombatConfig.DevLungeFree = false;
                    else if (p.Length >= 2 && p[1] == "on")  CombatConfig.DevLungeFree = true;
                    else if (p.Length >= 2) { Print("사용: lunge [on|off|<거리m>]"); break; }
                    else CombatConfig.DevLungeFree = !CombatConfig.DevLungeFree;

                    Print($"찌르기 자유 시전 {(CombatConfig.DevLungeFree ? "켬" : "끔")}" +
                          (CombatConfig.DevLungeFree
                            ? $" — 몹 없어도 발동 · 스택/쿨 무시 · 전진 {CombatConfig.DevLungeBlinkDist:0.0}m\n" +
                              "  ★ Sim 동작이 바뀌므로 예지 결과도 달라집니다. 테스트용으로만 쓰십시오."
                            : ""));
                    break;
                }

                // ── 포즈 JSON 재생 (클립 굽기 전 미리보기) ──
                case "pose":
                {
                    var pp = PosePlayer.Instance;
                    if (pp == null) { Print("PosePlayer 없음"); break; }
                    if (p.Length < 2 || p[1] == "list")
                    {
                        var names = PosePlayer.ListPoses();
                        if (names.Count == 0) Print("포즈 없음 — Tools/뷰모델/포즈 JSON 으로 저장하십시오");
                        else { Print("포즈 " + names.Count + "개:"); foreach (var n in names) Print("  " + n); }
                        break;
                    }
                    Print(pp.ApplySingle(p[1]) ? "포즈 적용: " + p[1] : "그런 포즈 없음: " + p[1]);
                    break;
                }

                case "vcam":
                {
                    // 뷰모델 카메라 — 후퇴량·근평면을 숫자로 직접 지정
                    var vcm = ViewmodelCamera.Instance;
                    if (vcm == null) { Print("ViewmodelCamera 없음"); break; }

                    if (p.Length >= 2)
                    {
                        if (p[1] == "auto")
                        {
                            Print(vcm.AutoFitPullBack()
                                ? $"후퇴량 자동 설정 → {vcm.pullBack:0.000}m"
                                : "측정 실패 — 뷰모델이 없습니다");
                            break;
                        }
                        if (p[1] == "layers") { vcm.RefreshLayers(); Print("레이어 다시 입힘"); break; }
                        if (p[1] == "save")
                        { Print(vcm.Save() ? "저장 완료 — 다음 Play에도 적용됩니다" : "저장 실패"); break; }
                        if (p[1] == "default" || p[1] == "reset")
                        {
                            vcm.ResetToDefaults();
                            Print($"권장 기본값 적용 — 근평면 {vcm.nearClip:0.000} · 후퇴 {vcm.pullBack:0.00}m\n" +
                                  "  씬에 저장하려면 Inspector에서 확인 후 씬을 저장하십시오.");
                            break;
                        }
                        if (p[1] == "back" && p.Length >= 3 && float.TryParse(p[2], out float pb))
                        { vcm.pullBack = Mathf.Clamp(pb, 0f, 3f); Print($"후퇴량 = {vcm.pullBack:0.000}m"); break; }
                        if (p[1] == "near" && p.Length >= 3 && float.TryParse(p[2], out float ncv))
                        { vcm.nearClip = Mathf.Clamp(ncv, 0.001f, 0.5f); Print($"근평면 = {vcm.nearClip:0.000}"); break; }
                    }

                    float z = vcm.NearestZ();
                    Print($"{vcm.Status}\n" +
                          $"  후퇴량 {vcm.pullBack:0.000}m · 근평면 {vcm.nearClip:0.000}\n" +
                          $"  최근접 깊이 {(float.IsNaN(z) ? "측정불가" : z.ToString("0.000") + "m" + (z < 0f ? " (카메라 뒤)" : ""))}\n" +
                          $"  레이어 안 맞는 렌더러 {vcm.CountWrongLayer()}개\n" +
                          "  사용: vcam auto · vcam back 0.5 · vcam near 0.01 · vcam layers");
                    break;
                }

                case "vm":
                {
                    // 뷰모델 소환·재생성 — 어느 씬에서든 Resources 프리팹으로 만든다
                    var sv = SwordView.Instance;
                    if (sv == null) { Print("SwordView 없음"); break; }

                    if (p.Length >= 2 && p[1] == "info")
                    {
                        var cam0 = main.Cam;
                        var cur = cam0 != null ? cam0.transform.Find("KatanaViewmodel") : null;
                        if (cur == null) cur = GameObject.Find("KatanaViewmodel")?.transform;
                        Print(cur != null
                            ? $"뷰모델 있음 — 오브젝트 {cur.GetComponentsInChildren<Transform>(true).Length}개\n" +
                              $"  루트 localPos {cur.localPosition} · 부모 {(cur.parent != null ? cur.parent.name : "없음")}"
                            : "뷰모델 없음 — vm 으로 소환하십시오");
                        var vcam = ViewmodelCamera.Instance;
                        if (vcam != null) Print("  카메라: " + vcam.Status);
                        break;
                    }

                    if (!sv.enabled) { sv.enabled = true; Print("SwordView 다시 켬"); }
                    Print(sv.RebuildViewmodel()
                        ? "뷰모델 재생성 요청 — Resources/KatanaViewmodel 에서 다음 프레임에 생성됩니다.\n" +
                          "  씬 튜닝이 반영되지 않으면 Tools/뷰모델/③ 으로 프리팹에 저장하십시오."
                        : "실패 — Main 또는 카메라가 없습니다");
                    break;
                }

                case "fx":
                {
                    // 베기 이펙트 — fx 평타1 / fx 평타2 / fx 찌르기 / fx off|on
                    var sf = SlashFxDriver.Instance;
                    if (sf == null) { Print("SlashFxDriver 없음"); break; }
                    if (p.Length < 2)
                    {
                        Print("사용: fx <평타1|평타2|찌르기> · fx off|on   (각도 조절은 F4)");
                        break;
                    }
                    if (p[1] == "off")  { sf.active = false; Print("베기 이펙트 끔"); break; }
                    if (p[1] == "on")   { sf.active = true;  Print("베기 이펙트 켬"); break; }
                    if (p[1] == "save") { Print(sf.Save() ? "저장 완료 — 다음 Play에도 적용됩니다" : "저장 실패"); break; }
                    if (p[1] == "burst")
                    {
                        float bd = 2.4f;
                        if (p.Length >= 3) float.TryParse(p[2], out bd);
                        var bc = main.Cam != null ? main.Cam : Camera.main;
                        if (bc == null) { Print("카메라 없음"); break; }
                        sf.BurstAt(bc.transform.position + bc.transform.forward * bd);
                        Print($"피격 버스트 — 카메라 앞 {bd:0.0}m  (갈래 수·퍼짐은 F5 버스트 탭)");
                        break;
                    }
                    var slot = sf.Find(p[1]);
                    if (slot == null) { Print("그런 슬롯 없음: " + p[1] + "  (평타1 / 평타2 / 찌르기)"); break; }

                    // fx <슬롯> follow <0~3>  — 0 월드 · 1 카메라 · 2 지연 · 3 단계
                    if (p.Length >= 4 && p[2] == "follow" && int.TryParse(p[3], out int fmode))
                    {
                        slot.follow = Mathf.Clamp(fmode, 0, 3);
                        string[] fn = { "월드 고정", "카메라 고정", "지연 추종", "단계 전환" };
                        Print($"{slot.name} 추종 = {fn[slot.follow]}  (저장하려면 fx save)");
                        break;
                    }
                    sf.SpawnNow(slot);
                    Print($"이펙트: {slot.name}  roll {slot.roll:0}° pitch {slot.pitch:0}° yaw {slot.yaw:0}° · F4에서 조절");
                    break;
                }

                case "ui":
                {
                    // 게임 UI(HP바 등) 숨기기. 개발 패널(콘솔·F1~F3)은 그대로 둔다.
                    if      (p.Length >= 2 && (p[1] == "off"  || p[1] == "hide")) UiVisibility.Set(true);
                    else if (p.Length >= 2 && (p[1] == "on"   || p[1] == "show")) UiVisibility.Set(false);
                    else UiVisibility.Toggle();
                    Print($"게임 UI {(UiVisibility.Hidden ? "숨김" : "표시")}  (HP·스태미나·예측 표시)\n" +
                          "  개발 패널(콘솔·F1·F2·F3)은 영향 없음");
                    break;
                }

                case "bind":
                {
                    // 실제 전투 연동 on/off (좌클릭=slash1·2 번갈아, 우클릭=thrust)
                    var dv = PoseCombatDriver.Instance;
                    if (dv == null) { Print("PoseCombatDriver 없음"); break; }
                    if (p.Length >= 2)
                    {
                        if      (p[1] == "on")  dv.active = true;
                        else if (p[1] == "off") dv.active = false;
                        else if (float.TryParse(p[1], out float bt)) dv.segTime = Mathf.Max(0.02f, bt);
                    }
                    else dv.active = !dv.active;
                    dv.ResetCombo();
                    Print($"전투 연동 {(dv.active ? "켬" : "끔")} · 포즈당 {dv.segTime:0.00}초 (저장된 타이밍이 있으면 그쪽 우선)\n" +
                          "  평타 = slash1 ↔ slash2 번갈아 · 찌르기 = thrust1 · 실제 공격 입력에 반응");
                    break;
                }

                case "seq":
                {
                    // 정확한 포즈 이름들을 순서대로(중간 건너뛰기). 예) seq 평타1_0 평타1_1 평타1_4 loop
                    var pp = PosePlayer.Instance;
                    if (pp == null) { Print("PosePlayer 없음"); break; }
                    if (p.Length < 3) { Print("사용: seq <포즈이름> <포즈이름> [...] [loop]   예) seq 평타1_0 평타1_1 평타1_4"); break; }
                    var names = new System.Collections.Generic.List<string>();
                    bool slp = false;
                    for (int si = 1; si < p.Length; si++)
                    {
                        if (p[si] == "loop")   { slp = true; continue; }
                        if (p[si] == "linear") { pp.springBetweenPoses = false; continue; }
                        if (p[si] == "spring") { pp.springBetweenPoses = true;  continue; }
                        if (float.TryParse(p[si], out float sdv)) { pp.segTime = Mathf.Max(0.05f, sdv); continue; }
                        names.Add(p[si]);
                    }
                    int sn = pp.PlayNames(names, slp);
                    Print(sn >= 2 ? "재생: " + pp.SequenceSummary + (slp ? " (반복)" : "") + "\n  간격은 F3 패널에서 각각 조절"
                                  : "포즈를 2개 이상 찾지 못했습니다 (pose list 로 이름 확인)");
                    break;
                }

                case "combo":
                {
                    // 여러 접두어를 이어서 재생. 예) combo 평타1_ 평타2_ 0.35 loop
                    var pp = PosePlayer.Instance;
                    if (pp == null) { Print("PosePlayer 없음"); break; }
                    if (p.Length < 3) { Print("사용: combo <접두어1> <접두어2> [...] [초] [loop]   예) combo 평타1_ 평타2_ 0.35 loop"); break; }

                    var prefixes = new System.Collections.Generic.List<string>();
                    float cdur = 0.4f; bool clp = false;
                    for (int ci = 1; ci < p.Length; ci++)
                    {
                        if (p[ci] == "loop")   { clp = true; continue; }
                        if (p[ci] == "linear") { pp.springBetweenPoses = false; continue; }
                        if (p[ci] == "spring") { pp.springBetweenPoses = true;  continue; }
                        if (float.TryParse(p[ci], out float dv)) { cdur = dv; continue; }
                        prefixes.Add(p[ci]);
                    }
                    if (prefixes.Count < 1) { Print("접두어가 없습니다"); break; }
                    int cn = pp.Play(prefixes.ToArray(), cdur, clp);
                    Print(cn >= 2 ? "콤보 재생: " + pp.SequenceSummary + (clp ? " (반복)" : "")
                                  : "포즈를 못 찾았습니다");
                    break;
                }

                case "play":
                {
                    var pp = PosePlayer.Instance;
                    if (pp == null) { Print("PosePlayer 없음"); break; }
                    if (p.Length >= 2 && p[1] == "stop")    { pp.Stop();    Print("재생 중지 — 기본포즈로"); break; }
                    if (p.Length >= 2 && p[1] == "release") { pp.Release(); Print("포즈 제어 해제 — Animator·IK·SwordView 복구"); break; }
                    if (p.Length < 2) { Print("사용: play <접두어> [초] [loop] [linear|spring] · play stop · play release   예) play slash1_ 0.2 linear"); break; }
                    float dur = pp.segTime; bool lp = false;
                    for (int pi = 2; pi < p.Length; pi++)
                    {
                        if (p[pi] == "loop")   { lp = true; continue; }
                        if (p[pi] == "linear") { pp.springBetweenPoses = false; continue; }
                        if (p[pi] == "spring") { pp.springBetweenPoses = true;  continue; }
                        if (float.TryParse(p[pi], out float dv)) dur = dv;
                    }
                    int n = pp.Play(p[1], dur, lp);
                    Print(n >= 2 ? "재생: " + pp.SequenceSummary + (lp ? " (반복)" : "") + (pp.springBetweenPoses ? " [스프링]" : " [선형]")
                                 : $"포즈가 2개 미만입니다 ({p[1]}* 로 시작하는 포즈를 확인하십시오)");
                    break;
                }

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

                        // ── 레이어 on/off ──
                        case "layer":
                        {
                            var vm = ViewmodelMotion.Instance;
                            if (vm == null) { Print("ViewmodelMotion 없음"); break; }
                            if (p.Length < 3)
                            {
                                Print($"숨={vm.enableBreathe} 달리기={vm.enableBob} 기울임={vm.enableStrafe} 공중={vm.enableAir}");
                                Print($"착지={vm.enableLand} 피격={vm.enableHit} 시선={vm.enableSway}");
                                Print("사용: proc layer <이름> [0|1]   이름: breathe bob strafe air land hit sway all");
                                break;
                            }
                            bool on = p.Length < 4 || p[3] != "0";
                            switch (p[2].ToLowerInvariant())
                            {
                                case "breathe": vm.enableBreathe = on; break;
                                case "bob":     vm.enableBob     = on; break;
                                case "strafe":  vm.enableStrafe  = on; break;
                                case "air":     vm.enableAir     = on; break;
                                case "land":    vm.enableLand    = on; break;
                                case "hit":     vm.enableHit     = on; break;
                                case "sway":    vm.enableSway    = on; break;
                                case "all":
                                    vm.enableBreathe = vm.enableBob = vm.enableStrafe =
                                    vm.enableAir = vm.enableLand = vm.enableHit = vm.enableSway = on;
                                    break;
                                default: Print("모르는 레이어: " + p[2]); break;
                            }
                            Print($"레이어 {p[2]} = {(on ? "켜짐" : "꺼짐")}");
                            break;
                        }

                        default:
                            Print("사용: proc land [강도] · proc hit · proc breathe [0~1]");
                            Print("      proc grip [0~1] · proc pulse [세기] · proc reset");
                            Print("      proc layer [이름] [0|1]   (breathe bob strafe air land hit sway all)");
                            Print("  ★ 전체 튜닝은 F4 패널에서 실시간 조절");
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
