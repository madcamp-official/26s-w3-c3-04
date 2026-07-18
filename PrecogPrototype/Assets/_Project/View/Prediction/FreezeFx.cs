using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// 예측 정지 분위기: 흑백(채도 -100) + 청록 틴트 + 비네트. URP Volume을 런타임 생성한다.
    /// 카메라의 post-processing을 켜야 반영되므로 EnableOnCamera로 활성화한다.
    /// </summary>
    public class FreezeFx
    {
        Volume vol;
        float target, current;

        public void Init()
        {
            var go = new GameObject("PredictFreezeVolume");
            vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            vol.weight = 0f;

            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = p;

            var ca = p.Add<ColorAdjustments>(true);
            ca.saturation.overrideState = true;  ca.saturation.value = PredictionConfig.FxSaturation;  // 흑백
            ca.colorFilter.overrideState = true; ca.colorFilter.value = PredictionConfig.FxTint;        // 청록 틴트
            ca.postExposure.overrideState = true; ca.postExposure.value = PredictionConfig.FxExposure;  // 살짝 어둡게

            var vig = p.Add<Vignette>(true);
            vig.intensity.overrideState = true;  vig.intensity.value = PredictionConfig.FxVignette;
            vig.smoothness.overrideState = true; vig.smoothness.value = PredictionConfig.FxVignetteSmooth;
            vig.color.overrideState = true;      vig.color.value = PredictionConfig.FxVignetteColor;
        }

        /// <summary>카메라에 post-processing 활성화(안 켜면 Volume이 안 보임).</summary>
        public void EnableOnCamera(Camera cam)
        {
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        public void SetActive(bool on) => target = on ? 1f : 0f;

        /// <summary>매 프레임 가중치를 목표로 부드럽게 이동.</summary>
        public void Update()
        {
            if (vol == null) return;
            current = Mathf.MoveTowards(current, target, PredictionConfig.FxWeightSpeed * Time.deltaTime);
            vol.weight = current;
        }
    }
}
