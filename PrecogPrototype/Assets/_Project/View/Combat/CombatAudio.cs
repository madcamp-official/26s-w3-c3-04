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
        AudioClip swing, hit, dash, block, backstrike, death;

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
            block      = BuildBlock();
            backstrike = BuildBackstrike();
            death      = BuildDeath();
        }

        // ── 정적 접근자 (null 안전) ──
        public static void Swing()      => Play(inst?.swing,      0.55f, 0.06f);
        public static void Hit()        => Play(inst?.hit,        0.75f, 0.10f);
        public static void Dash()       => Play(inst?.dash,       0.6f,  0.05f);
        public static void Block()      => Play(inst?.block,      0.45f, 0.05f);
        public static void Backstrike() => Play(inst?.backstrike, 0.9f,  0.05f);
        public static void Death()      => Play(inst?.death,      0.8f,  0.08f);

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

        /// <summary>막기 챙: 고음 메탈 링잉.</summary>
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
