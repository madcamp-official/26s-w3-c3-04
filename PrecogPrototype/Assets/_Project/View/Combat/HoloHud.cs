using UnityEngine;
using UnityEngine.UI;
using Game.Sim;

namespace Game.View
{
    // >>> [홀로그램 HUD, 2026-07-22] "에셋 쓰니까 짜친다 — 아이언맨 홀로그램처럼 코드로"라는
    // 피드백. 생성형 아트 PNG(HudFrame/HudDialRing)를 한 장도 쓰지 않고, 전부 정점 메시로
    // 그린다. 아트 기반 HUD(HudCanvas)는 지우지 않고 남겨뒀다 — HudStyle.UseHologram 하나로
    // 갈아끼운다.
    //
    // 홀로그램 느낌을 만드는 규칙 세 가지:
    //  1. <b>가산 합성</b>(HoloUI.shader) — 겹칠수록 밝아진다. 선 하나를 굵기·투명도가 다른
    //     세 겹으로 깔면 그것만으로 빛나 보인다(GlowLayers).
    //  2. <b>테두리가 아니라 브래킷</b> — 사각형을 다 두르지 않고 모서리만 'ㄱ'자로 집는다.
    //     화면을 가두지 않으면서 "투사된 계기판"으로 읽힌다.
    //  3. <b>미세한 불안정함</b> — 아주 약한 깜빡임과 훑고 지나가는 스캔선. 완전히 고정된
    //     그림은 스티커처럼 보이고, 살짝 흔들리면 공중에 떠 있는 빛으로 보인다.

    /// <summary>어떤 HUD를 띄울지. 아트 프레임으로 되돌리려면 false.</summary>
    public static class HudStyle
    {
        public static bool UseHologram = true;
    }

    /// <summary>
    /// 코드로만 그리는 홀로그램 HUD. 체력/대시/런지는 하우징 없는 순수 세그먼트 바,
    /// 예지는 눈금 링, 화면 가장자리는 모서리 브래킷.
    /// </summary>
    public class HoloHud : MonoBehaviour
    {
        public static HoloHud Instance { get; private set; }

        // ── 색 ───────────────────────────────────────────────────────────
        static readonly Color Cyan   = new Color(0.42f, 0.92f, 1f);
        static readonly Color Health = new Color(1f, 0.44f, 0.30f);
        static readonly Color Dash   = new Color(0.38f, 0.86f, 1f);
        static readonly Color Lunge  = new Color(0.35f, 1f, 0.86f);
        static readonly Color Danger = new Color(1f, 0.22f, 0.16f);

        // ── 배치 (기준 해상도 1920x1080의 픽셀) ──────────────────────────
        const float RefW = 1920f, RefH = 1080f;
        const float BarX = 96f;            // 바 왼쪽 끝
        const float BarW = 420f, BarH = 15f;
        const float BarGapY = 40f;         // 바 사이 세로 간격
        const float BarsBottom = 96f;      // 맨 아래 바의 y(바닥에서)
        const float DialR = 92f;           // 예지 링 반지름
        const float DialMargin = 132f;     // 화면 우상단에서 링 중심까지
        const float LabelSize = 15f;

        Canvas canvas;
        GameObject root;
        HoloFrame frame;
        HoloScan scan;
        HoloBar health, dash, lunge;
        HoloArc dial;
        Text dialText;
        RectTransform dialTextRect;
        Text healthValue;

        // 표시값은 목표치로 부드럽게 따라간다 — 프레임마다 딱딱 끊기지 않게.
        float shownHealth = -1f, shownDash = -1f, shownLunge = -1f, shownDial = -1f;
        const float FillLerpSpeed = 14f;

        int lastHp = int.MinValue, lastDashCharges = int.MinValue, lastLungeStacks = int.MinValue;
        bool wasFull;
        int lastTenths = -1, lastHpShown = -1;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── 조립 ─────────────────────────────────────────────────────────
        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            root = NewRect("Root", transform).gameObject;
            var rootRt = (RectTransform)root.transform;
            Stretch(rootRt);

            frame = NewGraphic<HoloFrame>("Frame", rootRt);
            Stretch(frame.rectTransform);
            frame.color = Cyan;

            scan = NewGraphic<HoloScan>("Scan", rootRt);
            Stretch(scan.rectTransform);
            scan.color = Cyan;

            health = NewBar("Health", rootRt, 2, Health);
            dash   = NewBar("Dash",   rootRt, 1, Dash);
            lunge  = NewBar("Lunge",  rootRt, 0, Lunge);
            health.segments = 24; dash.segments = 8; lunge.segments = 6;

            NewLabel("HEALTH", rootRt, 2);
            NewLabel("DASH",   rootRt, 1);
            NewLabel("LUNGE",  rootRt, 0);

            // 체력 수치는 바 오른쪽 끝에 붙여 숫자로도 읽히게 한다.
            healthValue = NewText("HealthValue", rootRt, 20, TextAnchor.MiddleRight);
            SetRect((RectTransform)healthValue.transform,
                BarX + BarW - 150f, BarsBottom + 2 * (BarH + BarGapY) + BarH + 6f, 150f, 24f);
            healthValue.color = new Color(Health.r, Health.g, Health.b, 0.85f);

            BuildDial(rootRt);
        }

        HoloBar NewBar(string name, RectTransform parent, int row, Color c)
        {
            var bar = NewGraphic<HoloBar>(name, parent);
            SetRect(bar.rectTransform, BarX, BarsBottom + row * (BarH + BarGapY), BarW, BarH);
            bar.color = c;
            return bar;
        }

        void NewLabel(string text, RectTransform parent, int row)
        {
            var t = NewText(text + "Label", parent, LabelSize, TextAnchor.LowerLeft);
            SetRect((RectTransform)t.transform,
                BarX, BarsBottom + row * (BarH + BarGapY) + BarH + 4f, 260f, 22f);
            t.text = text;
            t.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f);
        }

        void BuildDial(RectTransform parent)
        {
            dial = NewGraphic<HoloArc>("Prediction", parent);
            var rt = dial.rectTransform;
            // 우상단 고정 — 앵커를 오른쪽 위에 붙여 화면비가 바뀌어도 모서리에서 같은 거리.
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-DialMargin, -DialMargin);
            rt.sizeDelta = new Vector2(DialR * 2f, DialR * 2f);
            dial.color = Cyan;

            dialTextRect = (RectTransform)NewText("Seconds", parent, 40, TextAnchor.MiddleCenter).transform;
            dialText = dialTextRect.GetComponent<Text>();
            dialTextRect.anchorMin = dialTextRect.anchorMax = new Vector2(1f, 1f);
            dialTextRect.pivot = new Vector2(0.5f, 0.5f);
            dialTextRect.anchoredPosition = new Vector2(-DialMargin, -DialMargin);
            dialTextRect.sizeDelta = new Vector2(DialR * 1.6f, DialR * 0.9f);
            dialText.color = Cyan;

            var label = NewText("PredLabel", parent, 14, TextAnchor.MiddleCenter);
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = new Vector2(-DialMargin, -DialMargin - DialR - 16f);
            lrt.sizeDelta = new Vector2(240f, 20f);
            label.text = "PRECOGNITION";
            label.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.45f);
        }

        // ── 갱신 ─────────────────────────────────────────────────────────
        void LateUpdate()
        {
            Main main = Main.Instance;
            bool show = !UiVisibility.Skip && main != null;
            if (root.activeSelf != show) root.SetActive(show);
            if (!show) return;

            // 예측이 timeScale을 소유하므로 HUD 연출은 전부 실시간(unscaled)으로 센다.
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            ref readonly PlayerCombatState c = ref main.World.player.combat;
            ref readonly PlayerSim p = ref main.World.player;

            UpdateHealth(in c, dt, now);
            UpdateDash(in p, dt);
            UpdateLunge(in c, dt);
            UpdateDial(main.PredictionCharge01, dt, now);

            // 프레임·스캔선은 값과 무관하게 계속 살아 움직인다.
            frame.Flicker = 0.92f + 0.08f * Mathf.Sin(now * 11.3f) * Mathf.Sin(now * 3.7f);
            frame.SetVerticesDirty();
            scan.Progress = Mathf.Repeat(now * 0.22f, 1f);
            scan.SetVerticesDirty();
        }

        static float Approach(ref float shown, float target, float dt)
        {
            if (shown < 0f) shown = target;
            shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-FillLerpSpeed * dt));
            if (Mathf.Abs(shown - target) < 0.0015f) shown = target;
            return shown;
        }

        void UpdateHealth(in PlayerCombatState c, float dt, float now)
        {
            float f = Mathf.Clamp01(c.hp / (float)CombatConfig.PlayerMaxHp);
            if (c.hp < lastHp) health.Punch();
            lastHp = c.hp;

            if (c.hp != lastHpShown)
            {
                lastHpShown = c.hp;
                healthValue.text = Mathf.Max(0, c.hp).ToString();
            }

            // 낮을수록 붉게, 위험 수위 아래에선 맥박이 뛴다(밝기로).
            float danger = 1f - Mathf.InverseLerp(0.16f, 0.34f, f);
            health.color = Color.Lerp(Health, Danger, danger);
            health.Intensity = f <= 0.16f ? 0.75f + 0.5f * Mathf.Abs(Mathf.Sin(now * 4.2f)) : 1f;
            health.Value = Approach(ref shownHealth, f, dt);
            health.Tick(dt);
        }

        void UpdateDash(in PlayerSim p, float dt)
        {
            int max = Mathf.Max(1, SimConfig.DashMaxCharges);
            float v = p.dashCharges;
            if (p.dashCharges < max && p.dashRecharge > 0 && SimConfig.DashRechargeTicks > 0)
                v += 1f - Mathf.Clamp01(p.dashRecharge / (float)SimConfig.DashRechargeTicks);

            if (p.dashCharges != lastDashCharges && lastDashCharges != int.MinValue) dash.Punch();
            lastDashCharges = p.dashCharges;

            dash.Intensity = p.dashCharges > 0 ? 1f : 0.3f;
            dash.Value = Approach(ref shownDash, Mathf.Clamp01(v / max), dt);
            dash.Tick(dt);
        }

        void UpdateLunge(in PlayerCombatState c, float dt)
        {
            int max = Mathf.Max(1, CombatConfig.LungeMaxStacks);
            float v = c.lungeStacks;
            if (c.lungeCooldown > 0 && CombatConfig.LungeCooldownTicks > 0 && c.lungeStacks < max)
                v += 1f - Mathf.Clamp01(c.lungeCooldown / (float)CombatConfig.LungeCooldownTicks);

            if (c.lungeStacks != lastLungeStacks && lastLungeStacks != int.MinValue) lunge.Punch();
            lastLungeStacks = c.lungeStacks;

            lunge.Intensity = c.lungeStacks > 0 ? 1f : 0.3f;
            lunge.Value = Approach(ref shownLunge, Mathf.Clamp01(v / max), dt);
            lunge.Tick(dt);
        }

        void UpdateDial(float charge01, float dt, float now)
        {
            float charge = Mathf.Clamp01(charge01);
            bool usable = charge >= PredictionConfig.ChargeMinToUse;
            bool full = charge >= 1f;

            if (full && !wasFull) dial.Punch();
            wasFull = full;

            // 다 찼으면 링이 숨쉬듯 밝아진다 — 시야 구석에서도 "쓸 수 있다"가 읽히게.
            float breathe = full ? 1f + 0.4f * Mathf.Sin(now * 3.1f) : 1f;
            dial.Intensity = (usable ? 1f : 0.28f) * breathe;
            dial.Value = Approach(ref shownDial, charge, dt);
            dial.Sweep = Mathf.Repeat(now * 0.35f, 1f);   // 훑고 도는 스캔 눈금
            dial.Tick(dt);

            int tenths = Mathf.RoundToInt(PredictionConfig.ChargeToSeconds(charge) * 10f);
            if (tenths != lastTenths) { lastTenths = tenths; dialText.text = SecondsText(tenths); }
            dialText.color = usable
                ? Color.Lerp(Cyan, Color.white, full ? 0.4f : 0.15f)
                : new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f);

            float s = full ? 1f + 0.04f * Mathf.Sin(now * 3.1f) : 1f;
            dialTextRect.localScale = new Vector3(s, s, 1f);
        }

        const int MaxTenths = 60;
        static string[] SecondsCache;

        static string SecondsText(int tenths)
        {
            if (SecondsCache == null)
            {
                SecondsCache = new string[MaxTenths + 1];
                for (int i = 0; i <= MaxTenths; i++)
                    SecondsCache[i] = (i / 10) + "." + (i % 10) + "<size=20><color=#7FD4E8>s</color></size>";
            }
            return SecondsCache[Mathf.Clamp(tenths, 0, MaxTenths)];
        }

        // ── UGUI 유틸 ────────────────────────────────────────────────────
        static T NewGraphic<T>(string name, Transform parent) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<T>();
            g.raycastTarget = false;
            g.material = HoloGraphic.HoloMaterial;
            return g;
        }

        static Text NewText(string name, Transform parent, float size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = Mathf.RoundToInt(size);
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>기준 해상도 픽셀(좌하단 원점)로 배치한다.</summary>
        static void SetRect(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>가산 합성 재질과 정점 유틸을 공유하는 베이스.</summary>
    public abstract class HoloGraphic : MaskableGraphic
    {
        static Material cached;
        static bool tried;

        /// <summary>Resources/HoloUI.shader로 만든 가산 재질. 없으면 null(기본 UI 재질로 폴백).</summary>
        public static Material HoloMaterial
        {
            get
            {
                if (tried) return cached;
                tried = true;
                var shader = Resources.Load<Shader>("HoloUI");
                if (shader == null)
                {
                    Debug.LogWarning("[HoloHud] Resources/HoloUI.shader 를 못 찾아 기본 UI 재질로 그립니다 " +
                                     "(가산 글로우 없음).");
                    return null;
                }
                cached = new Material(shader) { name = "HoloUI (runtime)", hideFlags = HideFlags.DontSave };
                return cached;
            }
        }

        /// <summary>겹쳐 그리는 글로우 겹 수(코어 + 바깥으로 번지는 겹).</summary>
        protected const int GlowLayers = 3;

        /// <summary>같은 선을 굵기·투명도를 달리해 여러 겹 그린다 — 가산이라 이것만으로 빛난다.</summary>
        protected static void GlowRect(VertexHelper vh, float x, float y, float w, float h, Color c, float slant = 0f)
        {
            for (int i = 0; i < GlowLayers; i++)
            {
                float k = i == 0 ? 0f : i * 1.9f;              // 바깥으로 번지는 양(px)
                float a = i == 0 ? c.a : c.a * (0.3f / i);     // 번질수록 옅게
                Quad(vh, x - k, y - k, w + k * 2f, h + k * 2f,
                    new Color(c.r, c.g, c.b, a), slant);
            }
        }

        protected static void Quad(VertexHelper vh, float x, float y, float w, float h, Color c, float slant = 0f)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = new Vector3(x, y);                    vh.AddVert(v);
            v.position = new Vector3(x + slant, y + h);        vh.AddVert(v);
            v.position = new Vector3(x + slant + w, y + h);    vh.AddVert(v);
            v.position = new Vector3(x + w, y);                vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        /// <summary>네 점을 직접 주는 사각형(링 눈금처럼 회전한 도형용).</summary>
        protected static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c2, Vector2 d, Color c)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = a;  vh.AddVert(v);
            v.position = b;  vh.AddVert(v);
            v.position = c2; vh.AddVert(v);
            v.position = d;  vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 화면 모서리 브래킷. 사각형을 다 두르지 않고 네 모서리만 'ㄱ'자로 집는다 —
    /// 시야를 가두지 않으면서 "투사된 계기판" 인상을 준다.
    /// </summary>
    public class HoloFrame : HoloGraphic
    {
        public float Flicker = 1f;

        const float Margin = 26f;      // 화면 가장자리에서 띄우는 거리
        const float Arm = 210f;        // 브래킷 팔 길이
        const float Thin = 2f;         // 선 두께
        const float InnerGap = 9f;     // 두 번째(안쪽) 선까지 간격
        const float InnerArm = 84f;    // 안쪽 짧은 선 길이
        const float EdgeTick = 12f;    // 변 중앙 눈금 길이

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float x0 = r.xMin + Margin, x1 = r.xMax - Margin;
            float y0 = r.yMin + Margin, y1 = r.yMax - Margin;
            Color c = new Color(color.r, color.g, color.b, color.a * Flicker);
            Color dim = new Color(c.r, c.g, c.b, c.a * 0.45f);

            // 모서리 4개 — (부호로 방향만 뒤집어 같은 코드를 재사용)
            Bracket(vh, x0, y0,  1f,  1f, c, dim);
            Bracket(vh, x1, y0, -1f,  1f, c, dim);
            Bracket(vh, x0, y1,  1f, -1f, c, dim);
            Bracket(vh, x1, y1, -1f, -1f, c, dim);

            // 변 중앙 눈금 — 상하좌우 각 3개씩, 가운데가 길다.
            for (int i = -1; i <= 1; i++)
            {
                float t = i * 46f;
                float len = i == 0 ? EdgeTick * 1.9f : EdgeTick;
                float cx = (x0 + x1) * 0.5f + t, cy = (y0 + y1) * 0.5f + t;
                GlowRect(vh, cx, y0, Thin, len, dim);
                GlowRect(vh, cx, y1 - len, Thin, len, dim);
                GlowRect(vh, x0, cy, len, Thin, dim);
                GlowRect(vh, x1 - len, cy, len, Thin, dim);
            }
        }

        /// <summary>모서리 하나. sx/sy는 안쪽으로 향하는 부호(+1/-1).</summary>
        static void Bracket(VertexHelper vh, float px, float py, float sx, float sy, Color c, Color dim)
        {
            // 바깥 'ㄱ'자
            GlowRect(vh, px + (sx > 0 ? 0f : -Arm), py, Arm, Thin, c);
            GlowRect(vh, px, py + (sy > 0 ? 0f : -Arm), Thin, Arm, c);
            // 안쪽 짧은 선 한 겹 더 — 두 줄이 되면 단번에 "계기판"처럼 읽힌다.
            float ix = px + sx * InnerGap, iy = py + sy * InnerGap;
            GlowRect(vh, ix + (sx > 0 ? 0f : -InnerArm), iy, InnerArm, Thin * 0.7f, dim);
            GlowRect(vh, ix, iy + (sy > 0 ? 0f : -InnerArm), Thin * 0.7f, InnerArm, dim);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>화면을 아래에서 위로 훑고 지나가는 아주 옅은 스캔선.</summary>
    public class HoloScan : HoloGraphic
    {
        public float Progress;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float y = Mathf.Lerp(r.yMin, r.yMax, Progress);
            // 가장자리에서 서서히 사라지게 — 위아래 끝에서 튀어나오는 티를 없앤다.
            float edge = Mathf.Sin(Progress * Mathf.PI);
            Color c = new Color(color.r, color.g, color.b, 0.05f * edge);
            Quad(vh, r.xMin, y, r.width, 1.5f, c);
            Quad(vh, r.xMin, y - 26f, r.width, 26f, new Color(c.r, c.g, c.b, c.a * 0.35f));
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 하우징 없는 순수 홀로그램 바. 기울어진 세그먼트가 왼쪽부터 켜지고, 채움 경계에는
    /// 밝은 선두가 선다. 꺼진 칸도 아주 옅게 남겨 "총량"이 읽히게 한다.
    /// </summary>
    public class HoloBar : HoloGraphic
    {
        public float Value;        // 0~1
        public float Intensity = 1f;
        public int segments = 20;

        float flash;

        public void Punch() => flash = 1f;

        public void Tick(float dt)
        {
            flash = Mathf.Max(0f, flash - dt * 3.4f);
            SetVerticesDirty();
        }

        const float Slant = 5f;    // 위쪽이 오른쪽으로 밀린 평행사변형
        const float GapRatio = 0.34f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            int n = Mathf.Max(1, segments);
            float pitch = r.width / n;
            float w = pitch * (1f - GapRatio);
            Color lit = new Color(color.r, color.g, color.b, color.a * Intensity);
            Color off = new Color(color.r, color.g, color.b, 0.07f * Intensity);

            for (int i = 0; i < n; i++)
            {
                float x = r.xMin + i * pitch;
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                float k = Mathf.Clamp01((Value - t0) / Mathf.Max(0.0001f, t1 - t0));
                if (k <= 0f)
                {
                    // 꺼진 칸 — 바닥에 실선만 남긴다.
                    Quad(vh, x, r.yMin, w, 1.5f, off, Slant * 0.2f);
                    continue;
                }

                Color c = lit;
                if (k < 1f) c.a *= 0.35f + 0.65f * k;    // 경계 칸은 차오르는 중
                GlowRect(vh, x, r.yMin, w, r.height, c, Slant);
            }

            // 채움 선두 — 한 칸 굵기의 밝은 세로선. 눈이 "지금 어디까지"를 잡는 지점이다.
            if (Value > 0.001f && Value < 0.999f)
            {
                float hx = r.xMin + r.width * Value;
                GlowRect(vh, hx - 1f, r.yMin - 3f, 2f, r.height + 6f,
                    new Color(1f, 1f, 1f, 0.55f * Intensity), Slant);
            }

            // 피격·소모 순간의 흰 번쩍임
            if (flash > 0.001f)
                Quad(vh, r.xMin, r.yMin, r.width, r.height,
                    new Color(1f, 1f, 1f, flash * 0.5f), Slant);

            // 바 아래 기준선 — 게이지가 비어도 자리를 잃지 않게.
            Quad(vh, r.xMin, r.yMin - 5f, r.width, 1f,
                new Color(color.r, color.g, color.b, 0.16f));
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 예지 게이지 링. 12시에서 시계방향으로 눈금이 켜지고, 그 위를 스캔 눈금이 훑는다.
    /// </summary>
    public class HoloArc : HoloGraphic
    {
        public float Value;
        public float Intensity = 1f;
        public float Sweep;

        float flash;

        public void Punch() => flash = 1f;

        public void Tick(float dt)
        {
            flash = Mathf.Max(0f, flash - dt * 2.2f);
            SetVerticesDirty();
        }

        const int Ticks = 44;
        const float InnerFrac = 0.80f;   // 눈금 안쪽 반지름 비율
        const float OuterFrac = 0.97f;
        const float TickGapDeg = 1.6f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Vector2 c0 = r.center;
            float half = Mathf.Min(r.width, r.height) * 0.5f;
            float ri = half * InnerFrac, ro = half * OuterFrac;
            Color lit = new Color(color.r, color.g, color.b, color.a * Intensity);
            Color off = new Color(color.r, color.g, color.b, 0.09f * Intensity);

            float step = 360f / Ticks;
            for (int i = 0; i < Ticks; i++)
            {
                float t0 = i / (float)Ticks;
                float k = Mathf.Clamp01((Value - t0) * Ticks);
                Color c = k <= 0f ? off : lit;
                if (k > 0f && k < 1f) c.a *= 0.35f + 0.65f * k;

                // 스캔 눈금이 지나가는 자리는 잠깐 밝아진다.
                float d = Mathf.Abs(Mathf.DeltaAngle(t0 * 360f, Sweep * 360f));
                if (d < 26f) c.a += (1f - d / 26f) * 0.35f * Intensity;

                float a0 = i * step + TickGapDeg * 0.5f;
                float a1 = (i + 1) * step - TickGapDeg * 0.5f;
                RadialQuad(vh, c0, ri, ro, a0, a1, c);
                if (k > 0f) RadialQuad(vh, c0, ri - 3f, ro + 3f, a0, a1,
                    new Color(c.r, c.g, c.b, c.a * 0.22f));   // 번짐 한 겹
            }

            // 바깥 얇은 원 — 눈금이 다 꺼져도 링의 자리가 남는다.
            Ring(vh, c0, half, 1.2f, new Color(color.r, color.g, color.b, 0.18f));
            // 안쪽 짧은 눈금 4개(12·3·6·9시) — 계기판 느낌의 기준점.
            for (int q = 0; q < 4; q++)
                RadialQuad(vh, c0, half * 0.60f, half * 0.68f, q * 90f - 0.9f, q * 90f + 0.9f,
                    new Color(color.r, color.g, color.b, 0.4f * Intensity));

            if (flash > 0.001f)
                Ring(vh, c0, half * 0.985f, 4f, new Color(1f, 1f, 1f, flash * 0.45f));
        }

        /// <summary>12시 기준 시계방향 각도(도)로 잘라낸 부채꼴 조각.</summary>
        static void RadialQuad(VertexHelper vh, Vector2 c0, float ri, float ro, float a0, float a1, Color c)
        {
            Vector2 d0 = Dir(a0), d1 = Dir(a1);
            Quad(vh, c0 + d0 * ri, c0 + d0 * ro, c0 + d1 * ro, c0 + d1 * ri, c);
        }

        static void Ring(VertexHelper vh, Vector2 c0, float radius, float thick, Color c)
        {
            const int steps = 72;
            for (int i = 0; i < steps; i++)
            {
                float a0 = i * 360f / steps, a1 = (i + 1) * 360f / steps;
                RadialQuad(vh, c0, radius - thick * 0.5f, radius + thick * 0.5f, a0, a1, c);
            }
        }

        static Vector2 Dir(float deg)
        {
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));   // 12시에서 시계방향
        }
    }
    // <<< [홀로그램 HUD 끝]
}
