using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 절차적 전투 SFX. ★ combat 소유·독립. 오디오 에셋 0개 — AudioClip.Create로 합성.
    /// 정적 접근자(CombatAudio.Swing() 등)를 SwordView·CombatFeedback이 호출.
    /// 임시방편 아님(이 세션 확정 방식) — 다만 진짜 폴리 사운드보단 거칠다.
    /// </summary>
    public class CombatAudio : MonoBehaviour
    {
        const int SR = 44100;

        static CombatAudio inst;
        AudioSource src;
        AudioClip swing, hit, dash, guardRaise, block, backstrike, death;
        // 몹 SFX (러프·잠정). 대부분 몹 공격 SIM 상태 생기면 그쪽에서 호출.
        AudioClip enWindup, enMelee, enAim, enFire, enPain;
        AudioClip enStep;   // 발 딛는 소리 — 지금은 임시 합성음, 에셋이 오면 교체
        AudioClip playerHurt;   // 플레이어 피격("억" + 저음 임팩트)
        AudioClip prediction;   // 예지 발동(시간정지식 상승 시머)

        void Awake()
        {
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;

            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D (1인칭이라 위치 무관)

            // 씬에 리스너 없으면 카메라에 부착
            if (Object.FindFirstObjectByType<AudioListener>() == null)
            {
                var cam = Camera.main;
                if (cam != null && cam.GetComponent<AudioListener>() == null)
                    cam.gameObject.AddComponent<AudioListener>();
            }

            swing      = BuildSwing();
            hit        = BuildHit();
            dash       = BuildDash();
            guardRaise = BuildGuardRaise();
            block      = BuildBlock();
            backstrike = BuildBackstrike();
            death      = BuildDeath();

            enWindup = BuildEnemyWindup();
            enMelee  = BuildEnemyMelee();
            enAim    = BuildEnemyAim();
            enFire   = BuildEnemyFire();
            enPain   = BuildEnemyPain();
            enStep   = BuildEnemyStep();

            playerHurt = BuildPlayerHurt();
            prediction = BuildPrediction();
        }

        // ── 정적 접근자 (null 안전) ──
        public static void Swing()      => Play(inst?.swing,      0.55f, 0.06f);
        public static void Hit()        => Play(inst?.hit,        0.75f, 0.10f);
        public static void Dash()       => Play(inst?.dash,       0.6f,  0.05f);
        public static void GuardRaise() => Play(inst?.guardRaise, 0.35f, 0.06f);  // 막기 켜는 소리(스윽)
        public static void Block()      => Play(inst?.block,      0.45f, 0.05f);  // 실제 방어 성공(챙) — 적 공격 생기면 사용
        public static void Backstrike() => Play(inst?.backstrike, 0.9f,  0.05f);
        public static void Death()      => Play(inst?.death,      0.8f,  0.08f);

        // ── 몹 SFX (러프). 몹 공격 SIM 상태 생기면 호출: ──
        //  근접 그런트: Windup 진입 → EnemyWindup(), Active 판정 → EnemyMelee()
        //  원거리 솔저: Aim 진입 → EnemyAim(), Fire → EnemyFire()
        //  피격 신음(EnemyPain)은 CombatFeedback이 이미 연결.
        public static void EnemyWindup() => Play(inst?.enWindup, 0.5f,  0.05f);
        public static void EnemyMelee()  => Play(inst?.enMelee,  0.6f,  0.06f);
        public static void EnemyAim()    => Play(inst?.enAim,    0.4f,  0.03f);
        public static void EnemyFire()   => Play(inst?.enFire,   0.6f,  0.05f);
        public static void EnemyPain()   => Play(inst?.enPain,   0.45f, 0.10f);
        /// <summary>몹이 발을 딛는 소리. 자주 나므로 볼륨을 낮게, 피치 편차를 넓게 준다.</summary>
        public static void EnemyStep()   => Play(inst?.enStep,   0.22f, 0.18f);
        public static void PlayerHurt()  => Play(inst?.playerHurt, 0.75f, 0.06f);  // 플레이어 피격
        public static void Prediction()  => Play(inst?.prediction, 0.5f,  0.02f);  // 예지 발동

        static void Play(AudioClip clip, float vol, float pitchJitter)
        {
            if (inst == null || clip == null) return;
            inst.src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            inst.src.PlayOneShot(clip, vol);
        }

        // ── 합성 ──

        /// <summary>평타 스윙 휙: 노이즈 로우패스 스윕 + 빠른 어택/감쇠.</summary>
        static AudioClip BuildSwing()
        {
            const float dur = 0.20f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(1);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.5f, 0.04f, t);        // 밝음→둔탁
                lp += (white - lp) * a;
                float env = Mathf.Clamp01(t * 18f) * Mathf.Pow(1f - t, 1.6f);
                s[i] = lp * env * 0.6f;
            }
            return Clip("cai_swing", s);
        }

        /// <summary>타격 퍽: 짧은 메탈릭 트랜지언트 + 노이즈 버스트.</summary>
        static AudioClip BuildHit()
        {
            const float dur = 0.14f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(2);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float env = Mathf.Exp(-t * 34f);
                float metal = Mathf.Sin(6.2832f * 190f * ti)
                            + 0.6f * Mathf.Sin(6.2832f * 330f * ti)
                            + 0.4f * Mathf.Sin(6.2832f * 92f * ti);
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 60f);
                s[i] = (metal * 0.45f + noise * 0.7f) * env;
            }
            return Clip("cai_hit", s);
        }

        /// <summary>질풍참 샤악: 밝은 노이즈 휘슬 + 지속.</summary>
        static AudioClip BuildDash()
        {
            const float dur = 0.28f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.35f, 0.12f, t);
                lp += (white - lp) * a;
                float env = Mathf.Clamp01(t * 12f) * Mathf.Pow(1f - t, 1.1f);
                s[i] = lp * env * 0.55f;
            }
            return Clip("cai_dash", s);
        }

        /// <summary>막기 켜는 소리(스윽): 부드러운 저음 노이즈 휘슬. 금속 트랜지언트 없음, 조용함.</summary>
        static AudioClip BuildGuardRaise()
        {
            const float dur = 0.14f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.25f, 0.06f, t);         // 둔탁하게
                lp += (white - lp) * a;
                float env = Mathf.Sin(Mathf.PI * t);           // 부드러운 인/아웃(날카로운 어택 없음)
                s[i] = lp * env * 0.35f;
            }
            return Clip("cai_guardraise", s);
        }

        /// <summary>막기 챙(실제 방어 성공용): 고음 메탈 링잉. 지금은 미사용(적 공격 생기면).</summary>
        static AudioClip BuildBlock()
        {
            const float dur = 0.30f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(4);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float env = Mathf.Exp(-t * 9f);
                float ring = Mathf.Sin(6.2832f * 1300f * ti)
                           + 0.5f * Mathf.Sin(6.2832f * 1950f * ti)
                           + 0.3f * Mathf.Sin(6.2832f * 2600f * ti);
                float atk = t < 0.02f ? (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 200f) : 0f;
                s[i] = ring * env * 0.32f + atk * 0.5f;
            }
            return Clip("cai_block", s);
        }

        /// <summary>칼등치기 쾅: 육중한 저음 바디 + 노이즈.</summary>
        static AudioClip BuildBackstrike()
        {
            const float dur = 0.35f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float env = Mathf.Exp(-t * 10f);
                float body = Mathf.Sin(6.2832f * 88f * ti) + 0.6f * Mathf.Sin(6.2832f * 140f * ti);
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 24f);
                s[i] = (body * 0.7f + noise * 0.6f) * env;
            }
            return Clip("cai_backstrike", s);
        }

        /// <summary>처치 크런치: 하강 노이즈.</summary>
        static AudioClip BuildDeath()
        {
            const float dur = 0.22f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(6);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.3f, 0.05f, t);
                lp += (white - lp) * a;
                float env = Mathf.Exp(-t * 17f);
                s[i] = lp * env * 0.7f;
            }
            return Clip("cai_death", s);
        }

        // ── 몹 합성 (전부 러프·잠정) ──

        /// <summary>근접 그런트 선딜 텔레그래프: 저음 그르렁이 점점 커지며 상승(읽기 쉬움).</summary>
        static AudioClip BuildEnemyWindup()
        {
            const float dur = 0.40f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(7);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(70f, 150f, t);          // 상승
                phase += 6.2832f * f / SR;
                float tone = Mathf.Sin(phase) + 0.4f * Mathf.Sin(phase * 2f);
                float noise = (float)(rng.NextDouble() * 2 - 1) * 0.3f;
                float env = Mathf.Clamp01(t * 3f) * Mathf.Clamp01((1f - t) * 4f) * Mathf.Lerp(0.5f, 1f, t);
                s[i] = (tone * 0.5f + noise) * env * 0.5f;
            }
            return Clip("cai_en_windup", s);
        }

        /// <summary>근접 타격 스윙: 둔탁한 휙 + 저음 툭.</summary>
        static AudioClip BuildEnemyMelee()
        {
            const float dur = 0.20f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(8);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.3f, 0.03f, t);
                lp += (white - lp) * a;
                float thud = Mathf.Sin(6.2832f * 80f * ti) * Mathf.Exp(-t * 15f);
                float env = Mathf.Clamp01(t * 15f) * Mathf.Pow(1f - t, 1.3f);
                s[i] = (lp * env + thud * 0.5f) * 0.6f;
            }
            return Clip("cai_en_melee", s);
        }

        /// <summary>원거리 조준 차징: 전기 휘파람이 가속 상승(큰 텔레그래프).</summary>
        static AudioClip BuildEnemyAim()
        {
            const float dur = 0.60f;
            int n = (int)(dur * SR);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float f = Mathf.Lerp(300f, 900f, t * t);     // 가속 상승
                phase += 6.2832f * f / SR;
                float tone = (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 1.5f))
                             * (1f + 0.1f * Mathf.Sin(6.2832f * 18f * ti));  // 미세 트레몰로
                float env = Mathf.Clamp01(t * 2f) * Mathf.Lerp(0.3f, 1f, t) * Mathf.Clamp01((1f - t) * 8f);
                s[i] = tone * env * 0.25f;
            }
            return Clip("cai_en_aim", s);
        }

        /// <summary>플라즈마 발사: 하강 피치 지잽 + 노이즈 버스트.</summary>
        static AudioClip BuildEnemyFire()
        {
            const float dur = 0.25f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(9);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(1200f, 200f, Mathf.Sqrt(t));   // 하강
                phase += 6.2832f * f / SR;
                float tone = Mathf.Sin(phase);
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 30f);
                float env = Mathf.Exp(-t * 10f);
                s[i] = (tone * 0.6f + noise * 0.5f) * env;
            }
            return Clip("cai_en_fire", s);
        }

        /// <summary>피격 신음: 짧은 유기적 그런트(하강).</summary>
        /// <summary>
        /// 발 딛는 소리 — 무거운 금속 발이 바닥을 치는 짧은 "쿵". 저음 임팩트 + 금속 잔향.
        /// ★ 임시 합성음이다. 실제 에셋이 오면 이 함수 대신 클립을 물리면 된다.
        /// </summary>
        static AudioClip BuildEnemyStep()
        {
            const float dur = 0.13f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(31);
            float ph = 0f, ph2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                // 저음 쿵 — 빠르게 떨어지는 사인
                float f = Mathf.Lerp(120f, 55f, t);
                ph += 6.2832f * f / SR;
                float thud = Mathf.Sin(ph) * Mathf.Exp(-t * 26f);
                // 금속 잔향 — 높은 배음이 짧게
                ph2 += 6.2832f * 1900f / SR;
                float ring = Mathf.Sin(ph2) * Mathf.Exp(-t * 55f) * 0.18f;
                // 접촉 순간의 잡음
                float grit = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 90f) * 0.22f;
                s[i] = (thud + ring + grit) * 0.8f;
            }
            return Clip("cai_en_step", s);
        }

        static AudioClip BuildEnemyPain()
        {
            const float dur = 0.15f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(10);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(220f, 150f, t);         // 하강
                phase += 6.2832f * f / SR;
                float am = 0.6f + 0.4f * (float)(rng.NextDouble() * 2 - 1);   // 거친 진폭변조
                float env = Mathf.Clamp01(t * 20f) * Mathf.Exp(-t * 12f);
                s[i] = Mathf.Sin(phase) * am * env * 0.6f;
            }
            return Clip("cai_en_pain", s);
        }

        /// <summary>플레이어 피격: 낮은 "억" 신음 + 저음 임팩트(적 신음보다 더 낮고 묵직).</summary>
        static AudioClip BuildPlayerHurt()
        {
            const float dur = 0.18f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(12);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float f = Mathf.Lerp(180f, 110f, t);         // 하강(적 신음 220→150보다 낮음)
                phase += 6.2832f * f / SR;
                float am = 0.6f + 0.4f * (float)(rng.NextDouble() * 2 - 1);
                float thud = Mathf.Sin(6.2832f * 70f * ti) * Mathf.Exp(-t * 20f);   // 묵직한 저음
                float env = Mathf.Clamp01(t * 20f) * Mathf.Exp(-t * 10f);
                s[i] = (Mathf.Sin(phase) * am * 0.6f + thud * 0.5f) * env;
            }
            return Clip("cai_player_hurt", s);
        }

        /// <summary>예지 발동: 살짝 상승하는 화음 스웰 + 고음 시머(시간정지 느낌). 전투음과 구분.</summary>
        static AudioClip BuildPrediction()
        {
            const float dur = 0.45f;
            int n = (int)(dur * SR);
            var s = new float[n];
            float p1 = 0f, p2 = 0f, p3 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float bend = Mathf.Lerp(1f, 1.06f, t);          // 살짝 상승
                p1 += 6.2832f * 392f * bend / SR;
                p2 += 6.2832f * 587f * bend / SR;
                p3 += 6.2832f * 784f * bend / SR;
                float chord = Mathf.Sin(p1) + 0.7f * Mathf.Sin(p2) + 0.5f * Mathf.Sin(p3);
                float shimmer = 0.15f * Mathf.Sin(6.2832f * 40f * t) * Mathf.Sin(p3 * 1.5f);
                float env = t < 0.7f ? Mathf.Pow(t / 0.7f, 0.7f)      // 스웰 인
                                     : Mathf.Lerp(1f, 0f, (t - 0.7f) / 0.3f);
                s[i] = (chord * 0.22f + shimmer) * env;
            }
            return Clip("cai_prediction", s);
        }

        static AudioClip Clip(string name, float[] samples)
        {
            // 클릭 방지: 끝 2ms 페이드아웃
            int fade = Mathf.Min(samples.Length, (int)(0.002f * SR));
            for (int i = 0; i < fade; i++)
                samples[samples.Length - 1 - i] *= (float)i / fade;
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Clamp(samples[i], -1f, 1f);

            var c = AudioClip.Create(name, samples.Length, 1, SR, false);
            c.SetData(samples, 0);
            return c;
        }
    }

    /// <summary>Play 시 오디오 자동 부착.</summary>
    public static class CombatAudioBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<CombatAudio>() == null)
                new GameObject("[CombatAudio]").AddComponent<CombatAudio>();
        }
    }
}
