using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// 전투 화면연출(URP 포스트프로세싱). ★ combat 소유·독립. 읽기 전용(SimWorld 안 씀).
    /// 피격 붉은 비네트 펄스 + 색수차 버스트 + 런지 렌즈 왜곡(풀백). Volume을 런타임 생성한다.
    /// FreezeFx(예측 정지)와 별개 Volume·우선순위(50<100) → 예측 중 겹쳐도 각자 동작.
    /// OnGUI 밴드(구식) 대체: 정적 진입점 Hurt/Impact/LungePull 로 구동.
    /// </summary>
    public class ScreenFx : MonoBehaviour
    {
        static ScreenFx inst;

        Vignette            vig;
        ChromaticAberration chroma;
        LensDistortion      lens;

        // 펄스 상태(스파이크 후 감쇠). lens는 지속형(매 프레임 목표 갱신).
        float hurt;
        float chromaPulse;
        float lensTarget, lensCur;

        // 튜닝 상수 (전부 노출)
        const float HurtVignetteMax = 0.45f;   // 피격 붉은 비네트 최대
        const float HurtDecay       = 2.2f;
        const float ChromaMax       = 0.8f;    // 색수차 버스트 최대
        const float ChromaDecay     = 4f;
        const float LensPullMax     = -0.35f;  // 런지 풀백 배럴 왜곡(음수=오목/땡김)
        const float LensSpeed       = 9f;
        static readonly Color HurtColor = new Color(0.7f, 0f, 0f);

        /// <summary>피격: 붉은 비네트 + 색수차 버스트.</summary>
        public static void Hurt()
        {
            if (inst == null) return;
            inst.hurt = 1f;
            inst.chromaPulse = Mathf.Max(inst.chromaPulse, 0.6f);
        }

        /// <summary>임팩트(처치·런지 명중 등): 색수차 버스트만.</summary>
        public static void Impact(float amp)
        {
            if (inst != null) inst.chromaPulse = Mathf.Max(inst.chromaPulse, amp);
        }

        /// <summary>런지 풀백: 매 프레임 t01(0~1)로 목표 세팅(안 부르면 0으로 복귀).</summary>
        public static void LungePull(float t01)
        {
            if (inst != null) inst.lensTarget = Mathf.Clamp01(t01);
        }

        void Awake()
        {
            inst = this;
            var go = new GameObject("CombatScreenVolume");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 50f;    // FreezeFx(100) 아래
            vol.weight   = 1f;

            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = p;

            vig = p.Add<Vignette>(true);
            vig.color.overrideState      = true; vig.color.value      = HurtColor;
            vig.intensity.overrideState  = true; vig.intensity.value  = 0f;
            vig.smoothness.overrideState = true; vig.smoothness.value = 1f;

            chroma = p.Add<ChromaticAberration>(true);
            chroma.intensity.overrideState = true; chroma.intensity.value = 0f;

            lens = p.Add<LensDistortion>(true);
            lens.intensity.overrideState = true; lens.intensity.value = 0f;

            EnablePostProcessing();
        }

        /// <summary>URP 카메라에 post-processing 활성화(안 켜면 Volume 무시됨).</summary>
        void EnablePostProcessing()
        {
            var cam = Main.Instance != null ? Main.Instance.Cam : Camera.main;
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        void Update()
        {
            EnablePostProcessing();   // 카메라 늦게 생겨도 반영(비용 미미)

            float dt = Time.unscaledDeltaTime;
            if (hurt > 0f)        hurt        = Mathf.MoveTowards(hurt, 0f, HurtDecay * dt);
            if (chromaPulse > 0f) chromaPulse = Mathf.MoveTowards(chromaPulse, 0f, ChromaDecay * dt);
            lensCur    = Mathf.MoveTowards(lensCur, lensTarget, LensSpeed * dt);
            lensTarget = 0f;   // LungePull이 매 프레임 다시 세팅 안 하면 자연 복귀

            if (vig    != null) vig.intensity.value    = hurt        * HurtVignetteMax;
            if (chroma != null) chroma.intensity.value = chromaPulse * ChromaMax;
            if (lens   != null) lens.intensity.value   = lensCur     * LensPullMax;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class ScreenFxBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ScreenFx>() == null)
                new GameObject("[ScreenFx]").AddComponent<ScreenFx>();
        }
    }
}
