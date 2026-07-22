using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [리듬 재미 실험, 2026-07-22] "예측 경로를 따라가는 게 밋밋하다"는 피드백 대응.
    // 기존 판정 파이프라인(RhythmJudge / PredictionController.TryConsumeFollowingInput)은
    // 그대로 두고, "박자 하나를 어떻게 치게 만들 것인가"만 갈아끼우는 모드 레이어를 얹는다.
    // 숫자키 1~5로 언제든 전환해서 하나씩 체험해보고 마음에 드는 걸 고르면 된다.
    // 마음에 안 드는 모드는 이 파일에서 통째로 지우면 되고, 기존 동작(Classic)은 무손실이다.

    /// <summary>예측 실행(Following) 중 박자를 치는 방식. 숫자키 1~5로 전환.</summary>
    public enum PredictionRhythmMode
    {
        /// <summary>1 — 기존 방식. 박자마다 지정된 키 1번, Miss면 예지 종료.</summary>
        Classic = 0,
        /// <summary>2 — 다다다. 박자 전까지 같은 키를 연타해 게이지를 채우고 마지막 타로 친다.</summary>
        Mash = 1,
        /// <summary>3 — 자유 입력. 아무 액션 키나 박자에 맞추면 통과(정타는 보너스), Miss해도 계속 간다.</summary>
        Freestyle = 2,
        /// <summary>4 — 노트 하이웨이. 다음 박자들이 레일을 타고 판정선으로 내려온다. Miss해도 곡은 계속.</summary>
        Highway = 3,
        /// <summary>5 — 커맨드. 박자 하나가 짧은 입력 시퀀스(예: A→W→좌클릭)로 확장된다.</summary>
        Sequence = 4,
        /// <summary>6 — 자유 주행. 이동은 내가 직접, 액션 잔상에 닿으면 그 액션이 자동으로 터진다.
        /// 위 다섯 모드와 달리 판정축 자체가 시간이 아니라 공간이라, 기록 입력 재생과 RhythmJudge를
        /// 통째로 우회한다(<see cref="PredictionFreerun"/>).</summary>
        Freerun = 5,
    }

    /// <summary>모드 레이어 전용 튜닝값. 전투/이동 수치는 여기 두지 않는다.</summary>
    public static class RhythmModeConfig
    {
        // ── Mash(연타) ──
        /// <summary>박자를 치기 위해 필요한 연타 수(마지막 한 방이 곧 판정 입력).</summary>
        public const int MashRequiredPresses = 5;
        /// <summary>이 실시간(초) 동안 안 누르면 게이지가 새기 시작한다 — "다다다"를 강제.</summary>
        public const float MashGraceSeconds = 0.28f;
        /// <summary>새는 속도(초당 타수).</summary>
        public const float MashDecayPerSecond = 4.5f;
        /// <summary>요구치를 넘겨 더 두들긴 만큼 주는 보너스 점수(1타당).</summary>
        public const int MashOverdriveScore = 15;

        // ── Sequence(커맨드) ──
        /// <summary>한 박자를 구성하는 입력 개수(마지막이 실제 판정 입력).</summary>
        public const int SequenceLength = 3;

        // ── 공통 점수/콤보 ──
        public const int ScorePerfect = 300;
        public const int ScoreGood = 150;
        public const int ScoreExactBonus = 100;      // Freestyle에서 정타를 쳤을 때
        public const int ScoreComboStep = 10;        // 콤보 1당 가산

        // ── HUD ──
        public static readonly Color ModeAccent = new Color(0.35f, 1f, 0.78f);
        public static readonly Color ModeDim = new Color(0.55f, 0.75f, 0.7f, 0.55f);
        public const float HighwaySeconds = 1.6f;    // 하이웨이에 미리 보여주는 시간 폭(초)
        public const int HighwayLookahead = 5;       // 미리 보여주는 노트 수
    }

    /// <summary>
    /// 모드 상태 기계. PredictionController가 소유하고 아래 훅으로만 호출한다.
    ///   BeginRoute / EndRoute / OnBeatChanged / ResolveInput / OnAccepted / OnMissed / Update / Draw*
    /// 판정 자체(Perfect/Good/Miss 창)는 여전히 RhythmJudge 소유 — 여기서는 "무엇을 언제
    /// 판정기에 제출할 것인가"와 연출만 바꾼다.
    /// </summary>
    public sealed class RhythmModeRuntime
    {
        // 전환은 정적 상태로 둬서 예지 세션을 넘어 유지된다(PlayerPrefs로 실행 간에도 유지).
        const string PrefKey = "PredictionRhythmMode";

        // [버그 수정, 2026-07-22] 예전엔 `static ... current = PlayerPrefs.GetInt(...)` 였는데,
        // 정적 초기화자는 타입을 처음 건드리는 순간 돈다 — 그게 Main의 인스턴스 필드 초기화
        // (`new PredictionController()` → `new RhythmModeRuntime()`) 안이었다. Unity는
        // MonoBehaviour 생성자에서 PlayerPrefs 접근을 금지하므로 여기서 예외가 나고,
        // 그 바람에 <b>Main의 나머지 필드 초기화가 통째로 중단</b>돼 prediction이 null로 남았다
        // (증상은 엉뚱하게도 HUD의 NullReferenceException). 실제로 읽을 때까지 미룬다.
        static PredictionRhythmMode stored;
        static bool loaded;

        static PredictionRhythmMode current
        {
            get
            {
                if (!loaded) { loaded = true; stored = (PredictionRhythmMode)PlayerPrefs.GetInt(PrefKey, 0); }
                return stored;
            }
            set { loaded = true; stored = value; }
        }

        public static PredictionRhythmMode Current => current;

        public static void Select(PredictionRhythmMode mode)
        {
            if (current == mode) return;
            current = mode;
            PlayerPrefs.SetInt(PrefKey, (int)mode);
            Debug.Log($"[예측 리듬] 모드 전환 → {ModeName(mode)}  ({ModeHint(mode)})");
        }

        /// <summary>숫자키 1~5 감시. Following 중엔 흐름이 깨지므로 호출하지 않는다.</summary>
        public static bool PollModeSwitch(Keyboard kb)
        {
            if (kb == null) return false;
            if (kb.digit1Key.wasPressedThisFrame) { Select(PredictionRhythmMode.Classic); return true; }
            if (kb.digit2Key.wasPressedThisFrame) { Select(PredictionRhythmMode.Mash); return true; }
            if (kb.digit3Key.wasPressedThisFrame) { Select(PredictionRhythmMode.Freestyle); return true; }
            if (kb.digit4Key.wasPressedThisFrame) { Select(PredictionRhythmMode.Highway); return true; }
            if (kb.digit5Key.wasPressedThisFrame) { Select(PredictionRhythmMode.Sequence); return true; }
            if (kb.digit6Key.wasPressedThisFrame) { Select(PredictionRhythmMode.Freerun); return true; }
            return false;
        }

        public static string ModeName(PredictionRhythmMode m)
        {
            switch (m)
            {
                case PredictionRhythmMode.Classic: return "1 CLASSIC";
                case PredictionRhythmMode.Mash: return "2 MASH";
                case PredictionRhythmMode.Freestyle: return "3 FREESTYLE";
                case PredictionRhythmMode.Highway: return "4 HIGHWAY";
                case PredictionRhythmMode.Sequence: return "5 COMMAND";
                case PredictionRhythmMode.Freerun: return "6 FREERUN";
                default: return m.ToString();
            }
        }

        public static string ModeHint(PredictionRhythmMode m)
        {
            switch (m)
            {
                case PredictionRhythmMode.Classic: return "박자마다 지정 키 1번 · Miss = 예지 해제";
                case PredictionRhythmMode.Mash: return "박자 전까지 연타해서 게이지를 채우고 마지막 타로 친다";
                case PredictionRhythmMode.Freestyle: return "아무 액션 키나 OK(정타는 보너스) · Miss해도 계속 간다";
                case PredictionRhythmMode.Highway: return "다음 박자들이 레일로 내려온다 · Miss해도 계속 간다";
                case PredictionRhythmMode.Sequence: return "박자 하나가 3연 커맨드 · 마지막 입력이 판정";
                case PredictionRhythmMode.Freerun: return "이동은 내가 직접 · 잔상에 닿으면 그 액션이 터진다 (타이밍 없음)";
                default: return "";
            }
        }

        // ── 세션 상태 ──
        int beatIndex = -1;
        PredictedActionType expected;
        float mashCharge;
        float mashLastPressTime;
        int mashPresses;
        readonly PredictedActionType[] sequence = new PredictedActionType[RhythmModeConfig.SequenceLength];
        static readonly PredictedActionType[] SequencePool =
        {
            PredictedActionType.DashLeft, PredictedActionType.DashRight,
            PredictedActionType.DashForward, PredictedActionType.DashBackward,
        };
        int sequenceCursor;
        int combo;
        int maxCombo;
        int score;
        bool lastInputExact = true;
        string modeFeedback = "";
        float modeFeedbackUntil;
        float hitFlashUntil;

        public int Combo => combo;
        public int Score => score;

        /// <summary>Miss가 예지를 통째로 끝내는가. 리듬게임형 모드는 곡처럼 계속 간다.</summary>
        public bool MissEndsFollowing =>
            current != PredictionRhythmMode.Freestyle && current != PredictionRhythmMode.Highway;

        /// <summary>기본 접근링 HUD 대신 모드 전용 HUD를 그리는가.</summary>
        public bool ReplacesDefaultHud => current == PredictionRhythmMode.Highway;

        public void BeginRoute()
        {
            beatIndex = -1;
            combo = 0;
            maxCombo = 0;
            score = 0;
            modeFeedback = "";
            modeFeedbackUntil = 0f;
            ResetBeatState();
        }

        public void EndRoute()
        {
            if (score > 0)
                Debug.Log($"[예측 리듬] {ModeName(current)} 결과 — 점수 {score}, 최대 콤보 {maxCombo}");
            ResetBeatState();
        }

        public void OnBeatChanged(int index, PredictedActionType expectedType)
        {
            beatIndex = index;
            expected = expectedType;
            ResetBeatState();
            if (current == PredictionRhythmMode.Sequence) BuildSequence(index, expectedType);
        }

        void ResetBeatState()
        {
            mashCharge = 0f;
            mashPresses = 0;
            mashLastPressTime = Time.unscaledTime;
            sequenceCursor = 0;
            lastInputExact = true;
        }

        /// <summary>박자 index마다 결정론적으로 같은 커맨드가 나오게(랜덤 아님) 해시로 채운다.</summary>
        void BuildSequence(int index, PredictedActionType expectedType)
        {
            // 마지막 칸은 항상 실제 예측 액션 — 그 앞칸들은 방향키에서 뽑은 "장전" 입력.
            int h = (index + 1) * 7919;
            for (int i = 0; i < sequence.Length - 1; i++)
            {
                h = h * 31 + 17;
                sequence[i] = SequencePool[((h >> 3) & 0x7fffffff) % SequencePool.Length];
            }
            sequence[sequence.Length - 1] = expectedType;
        }

        public void Update()
        {
            if (current != PredictionRhythmMode.Mash) return;
            // 게이지 감쇠 — 느긋하게 누르면 안 차고, 연타해야 찬다.
            if (Time.unscaledTime - mashLastPressTime <= RhythmModeConfig.MashGraceSeconds) return;
            mashCharge = Mathf.Max(0f,
                mashCharge - RhythmModeConfig.MashDecayPerSecond * Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 원시 입력 하나를 "판정기에 제출할 입력"으로 변환한다.
        /// false를 주면 이번 입력은 판정에 올리지 않는다(연타 충전·커맨드 중간 입력 등).
        /// </summary>
        public bool ResolveInput(
            PredictedActionType raw, PredictedActionType expectedType, out PredictedActionType resolved)
        {
            resolved = expectedType;
            switch (current)
            {
                case PredictionRhythmMode.Mash:
                {
                    if (raw != expectedType) return false;
                    mashLastPressTime = Time.unscaledTime;
                    mashPresses++;
                    if (mashCharge < RhythmModeConfig.MashRequiredPresses) mashCharge += 1f;
                    if (mashCharge < RhythmModeConfig.MashRequiredPresses)
                    {
                        Feedback("CHARGE", 0.18f);
                        return false;   // 아직 장전 중 — 판정 대상 아님
                    }
                    return true;        // 게이지가 찼다 → 지금 이 타가 판정 입력
                }

                case PredictionRhythmMode.Freestyle:
                {
                    // 무엇을 눌렀든 박자만 맞으면 통과. 실행되는 액션은 예측대로지만,
                    // "정확한 키"를 골라 친 경우엔 보너스를 준다.
                    lastInputExact = raw == expectedType;
                    return true;
                }

                case PredictionRhythmMode.Sequence:
                {
                    if (sequenceCursor >= sequence.Length) return raw == expectedType;
                    if (raw != sequence[sequenceCursor])
                    {
                        if (sequenceCursor > 0) Feedback("BREAK", 0.3f);
                        sequenceCursor = 0;
                        return false;
                    }
                    if (sequenceCursor < sequence.Length - 1)
                    {
                        sequenceCursor++;
                        Feedback("...", 0.15f);
                        return false;   // 장전 입력 — 판정 대상 아님
                    }
                    sequenceCursor++;
                    return true;        // 마지막 칸 = 실제 판정 입력
                }

                default:   // Classic / Highway
                    return raw == expectedType;
            }
        }

        public void OnAccepted(RhythmJudgement result)
        {
            combo++;
            maxCombo = Mathf.Max(maxCombo, combo);
            int gained = result == RhythmJudgement.Perfect
                ? RhythmModeConfig.ScorePerfect : RhythmModeConfig.ScoreGood;
            gained += combo * RhythmModeConfig.ScoreComboStep;

            if (current == PredictionRhythmMode.Mash)
            {
                int over = Mathf.Max(0, mashPresses - RhythmModeConfig.MashRequiredPresses);
                if (over > 0)
                {
                    gained += over * RhythmModeConfig.MashOverdriveScore;
                    Feedback($"OVERDRIVE x{over}", 0.5f);
                }
            }
            else if (current == PredictionRhythmMode.Freestyle)
            {
                if (lastInputExact) { gained += RhythmModeConfig.ScoreExactBonus; Feedback("EXACT!", 0.5f); }
                else Feedback("IMPROVISE", 0.5f);
            }
            else if (current == PredictionRhythmMode.Sequence)
            {
                Feedback("COMMAND!", 0.5f);
            }

            score += gained;
            hitFlashUntil = Time.unscaledTime + 0.16f;
        }

        public void OnMissed()
        {
            combo = 0;
            ResetBeatState();
        }

        void Feedback(string text, float seconds)
        {
            modeFeedback = text;
            modeFeedbackUntil = Time.unscaledTime + seconds;
        }

        // ───────────────────────── HUD ─────────────────────────

        /// <summary>항상(Idle 포함) 그리는 좌상단 모드 표시.</summary>
        public static void DrawModeBadge(bool following)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                richText = true,
            };
            string accent = ColorUtility.ToHtmlStringRGB(RhythmModeConfig.ModeAccent);
            string body = $"<color=#{accent}>RHYTHM MODE · {ModeName(current)}</color>";
            if (!following) body += $"\n<size=12><color=#8FB3AB>{ModeHint(current)}   (숫자키 1~5 전환)</color></size>";
            GUI.Label(new Rect(18f, 14f, 620f, 52f), body, style);
        }

        /// <summary>Classic/Mash/Freestyle/Sequence에서 기본 링 위에 얹는 모드 전용 표시.</summary>
        public void DrawBeatOverlay(float centerX, float centerY, float beatProgress)
        {
            switch (current)
            {
                case PredictionRhythmMode.Mash: DrawMashGauge(centerX, centerY); break;
                case PredictionRhythmMode.Sequence: DrawSequenceChips(centerX, centerY); break;
                case PredictionRhythmMode.Freestyle: DrawFreestyleHint(centerX, centerY); break;
            }
            DrawScoreAndCombo();
            DrawModeFeedback(centerX, centerY);
        }

        void DrawMashGauge(float centerX, float centerY)
        {
            const int slots = RhythmModeConfig.MashRequiredPresses;
            const float w = 26f, h = 12f, gap = 6f;
            float total = slots * w + (slots - 1) * gap;
            float x0 = centerX - total * 0.5f;
            float y = centerY + 96f;
            Color old = GUI.color;
            for (int i = 0; i < slots; i++)
            {
                bool lit = mashCharge >= i + 1;
                GUI.color = lit
                    ? new Color(0.35f, 1f, 0.78f, 0.95f)
                    : new Color(0.2f, 0.4f, 0.36f, 0.45f);
                GUI.DrawTexture(new Rect(x0 + i * (w + gap), y, w, h), Texture2D.whiteTexture);
            }
            GUI.color = old;

            bool ready = mashCharge >= slots;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 20,
                fontStyle = FontStyle.Bold, richText = true,
            };
            GUI.Label(new Rect(centerX - 200f, y + 16f, 400f, 28f),
                ready ? "<color=#DFFFFF>READY — 박자에 맞춰 마지막 타!</color>"
                      : "<color=#50FF9A>연타해서 게이지를 채워라</color>", style);
        }

        void DrawSequenceChips(float centerX, float centerY)
        {
            const float w = 96f, h = 40f, gap = 12f;
            float total = sequence.Length * w + (sequence.Length - 1) * gap;
            float x0 = centerX - total * 0.5f;
            float y = centerY + 92f;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 20,
                fontStyle = FontStyle.Bold, richText = true,
            };
            Color old = GUI.color;
            for (int i = 0; i < sequence.Length; i++)
            {
                var r = new Rect(x0 + i * (w + gap), y, w, h);
                bool done = i < sequenceCursor;
                bool active = i == sequenceCursor;
                GUI.color = done ? new Color(0.15f, 0.5f, 0.42f, 0.75f)
                    : active ? new Color(0.12f, 0.35f, 0.32f, 0.85f)
                    : new Color(0.08f, 0.18f, 0.16f, 0.5f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = old;
                string key = KeyLabel(sequence[i]);
                string col = done ? "#7FFFD0" : active ? "#FFFFFF" : "#9EC7BE80";
                GUI.Label(r, $"<color={col}>{key}</color>", style);
            }
            GUI.color = old;
        }

        void DrawFreestyleHint(float centerX, float centerY)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 19,
                fontStyle = FontStyle.Bold, richText = true,
            };
            GUI.Label(new Rect(centerX - 300f, centerY + 96f, 600f, 30f),
                "<color=#50FF9A>아무 액션 키나 박자에 맞춰 — 정타는 보너스</color>", style);
        }

        void DrawScoreAndCombo()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperRight, fontSize = 22,
                fontStyle = FontStyle.Bold, richText = true,
            };
            float pop = Time.unscaledTime < hitFlashUntil ? 1f : 0f;
            string comboColor = combo >= 8 ? "#FFD86A" : combo >= 4 ? "#7FFFD0" : "#B7D8CE";
            GUI.Label(new Rect(Screen.width - 268f, 16f, 250f, 90f),
                $"<color=#DFFFFF>{score:N0}</color>\n" +
                $"<size={22 + Mathf.RoundToInt(pop * 10f)}><color={comboColor}>{combo} COMBO</color></size>",
                style);
        }

        void DrawModeFeedback(float centerX, float centerY)
        {
            if (Time.unscaledTime >= modeFeedbackUntil || string.IsNullOrEmpty(modeFeedback)) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 26,
                fontStyle = FontStyle.Bold, richText = true,
            };
            GUI.Label(new Rect(centerX - 250f, centerY - 150f, 500f, 34f),
                $"<color=#FFD86A>{modeFeedback}</color>", style);
        }

        /// <summary>Highway 전용 HUD — 다음 박자들이 판정선으로 내려오는 레일.</summary>
        public void DrawHighway(RhythmJudge judge, int current_, float beatProgress)
        {
            float laneW = Mathf.Clamp(Screen.width * 0.26f, 260f, 420f);
            float laneX = Screen.width * 0.5f - laneW * 0.5f;
            float top = Screen.height * 0.12f;
            float judgeY = Screen.height * 0.72f;
            float height = judgeY - top;

            Color old = GUI.color;
            GUI.color = new Color(0.04f, 0.12f, 0.1f, 0.42f);
            GUI.DrawTexture(new Rect(laneX, top, laneW, height + 40f), Texture2D.whiteTexture);

            // 판정선
            bool near = beatProgress >= 0.88f;
            GUI.color = near ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.35f, 1f, 0.78f, 0.7f);
            GUI.DrawTexture(new Rect(laneX - 16f, judgeY - 3f, laneW + 32f, 6f), Texture2D.whiteTexture);
            GUI.color = old;

            var noteStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 24,
                fontStyle = FontStyle.Bold, richText = true,
            };

            int baseTick = judge.GetEvent(current_).tick;
            int last = Mathf.Min(judge.Count - 1, current_ + RhythmModeConfig.HighwayLookahead);
            for (int i = last; i >= current_; i--)
            {
                // 현재 노트는 beatProgress(0→1)로, 이후 노트는 틱 차이를 초로 환산해 뒤에 붙인다.
                float secondsAway = (judge.GetEvent(i).tick - baseTick) / (float)SimConfig.TickRate;
                float t = i == current_
                    ? beatProgress
                    : Mathf.Clamp01(beatProgress - secondsAway / RhythmModeConfig.HighwaySeconds);
                float y = Mathf.Lerp(top, judgeY, t);
                float alpha = i == current_ ? 1f : Mathf.Lerp(0.75f, 0.25f, (i - current_) / 5f);

                var r = new Rect(laneX + 14f, y - 22f, laneW - 28f, 44f);
                GUI.color = i == current_
                    ? new Color(0.25f, 1f, 0.72f, 0.85f * alpha)
                    : new Color(0.16f, 0.5f, 0.45f, 0.7f * alpha);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Label(r, $"<color=#DFFFFF>{KeyLabel(judge.GetEvent(i).type)}</color>", noteStyle);
            }
            GUI.color = old;

            if (near)
            {
                var hit = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 30,
                    fontStyle = FontStyle.Bold, richText = true,
                };
                GUI.Label(new Rect(Screen.width * 0.5f - 200f, judgeY + 14f, 400f, 40f),
                    "<color=#FFFFFF>HIT!</color>", hit);
            }

            DrawScoreAndCombo();
            DrawModeFeedback(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        static string KeyLabel(PredictedActionType type)
        {
            switch (type)
            {
                case PredictedActionType.Jump: return "SPACE";
                case PredictedActionType.DashForward: return "W";
                case PredictedActionType.DashBackward: return "S";
                case PredictedActionType.DashLeft: return "A";
                case PredictedActionType.DashRight: return "D";
                case PredictedActionType.Attack: return "L-CLICK";
                case PredictedActionType.Lunge: return "R-CLICK";
                default: return type.ToString();
            }
        }
    }
    // <<< [리듬 재미 실험 끝]
}
