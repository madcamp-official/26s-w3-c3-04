using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스폰 실체화 VFX(설계 §4-⑥). 몹 재질을 안 건드리는 <b>디졸브 복제본 오버레이</b> 방식(A').
    ///
    /// ★ 트리거 = <b>플레이어가 이 몹을 처음 본 순간</b>(레이캐스트 가시성). sim의 순간 플래그
    ///   (traversalPhase·launchTicks)에 의존하던 옛 방식은 "낑겨서 1틱 만에 착지 판정"되면 트리거를
    ///   놓쳐 아무것도 안 뜨는 결함이 있었다 → 지속적 사실("봤는가")로 바꿔 그 결함 클래스를 제거.
    ///
    /// 흐름:
    ///  1) 스폰 즉시 오버레이(디졸브 복제본)를 만들어 두고(끈 상태) 진짜 몹 렌더러는 숨긴다.
    ///     → 아직 못 본 몹은 안 보인다.
    ///  2) 카메라 프러스텀 안 + 지형에 안 가려짐(LOS) → "봤다". 그 순간 오버레이를 켜고 진짜 몹도
    ///     다시 켠다(오버레이가 덮으므로 팝 없음). 위→아래로 걷히며 드러난다(X-ray 페이드인).
    ///  3) 안전장치: 오래(SightTimeout) 한 번도 안 보였으면(등 뒤 스폰 등) 그냥 드러낸다 —
    ///     "안 보이는데 때리는 몹"을 막는다.
    ///
    /// 순수 View — sim·결정론엔 영향 0. 진짜 몹 재질은 안 건드린다(A' 안전). 스킨드 메시는
    /// BakeMesh로 현재 포즈를 정적 스냅샷.
    /// </summary>
    public class SpawnMaterialize : MonoBehaviour
    {
        const string ShaderName = "Precog/SpawnMaterialize";
        static Shader shader;
        static bool shaderMissingLogged;

        public static int TotalBuilt;             // 진단
        public static float DurationOverride;     // 진단: >0이면 지속시간 강제
        public static bool DebugLog = false;

        const float RevealDuration = 1.6f;   // 걷히는 데 걸리는 시간(초)
        const float SightTimeout   = 4.0f;   // 이 시간 안에 한 번도 안 보이면 그냥 드러낸다(초)
        const float AimHeight      = 0.9f;   // 발밑 기준 위로 이만큼을 몸 중심으로 겨냥(m)
        const float ViewMargin     = 0.04f;  // 화면 가장자리 여유(0~1). 가장자리에 살짝 걸치면 아직 안 봄 취급
        const float LosBackoff     = 0.35f;  // 몹 바로 앞 지형에 걸려 오판하지 않게 사거리를 줄임(m)

        Transform viewRoot;
        float duration = RevealDuration;
        float t;           // Play 이후 흐른 시간
        float revealT;     // 실제 실체화가 시작된 뒤 흐른 시간
        bool started;

        readonly List<Material> mats = new List<Material>();
        readonly List<MeshRenderer> overlayRenderers = new List<MeshRenderer>();
        readonly List<Mesh> bakedMeshes = new List<Mesh>();
        readonly List<Renderer> hiddenReal = new List<Renderer>();

        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly int MinYId   = Shader.PropertyToID("_MinY");
        static readonly int MaxYId   = Shader.PropertyToID("_MaxY");

        /// <summary>스폰 즉시 호출. 플레이어가 이 몹을 처음 보는 순간(또는 타임아웃) 실체화가 시작된다.</summary>
        public static void Play(Transform viewRoot)
        {
            if (viewRoot == null) return;
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                if (!shaderMissingLogged) { Debug.LogWarning("[SpawnVFX] 셰이더 '" + ShaderName + "' 없음."); shaderMissingLogged = true; }
                return;
            }
            var host = new GameObject("~Materialize");
            host.transform.SetParent(viewRoot, false);
            var sm = host.AddComponent<SpawnMaterialize>();
            sm.viewRoot = viewRoot;
            sm.duration = DurationOverride > 0f ? DurationOverride : RevealDuration;
            sm.BuildHidden();   // 오버레이 미리 생성(끈 상태) + 진짜 몹 숨김
        }

        void BuildHidden()
        {
            int made = 0, skinned = 0;
            foreach (var r in viewRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.transform.IsChildOf(transform)) continue;

                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                { mesh = new Mesh(); smr.BakeMesh(mesh); bakedMeshes.Add(mesh); skinned++; }
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                if (mesh == null) continue;

                var go = new GameObject("~mat_" + r.name);
                var tr = go.transform;
                tr.SetParent(r.transform, false);
                tr.localPosition = Vector3.zero; tr.localRotation = Quaternion.identity; tr.localScale = Vector3.one;

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = false;   // 볼 때까지 안 보이게

                var m = new Material(shader);
                Bounds b = mesh.bounds;
                m.SetFloat(MinYId, b.min.y);
                m.SetFloat(MaxYId, b.max.y);
                m.SetFloat(RevealId, 0f);
                mr.sharedMaterial = m;
                mats.Add(m);
                overlayRenderers.Add(mr);
                made++;
            }

            if (made == 0) { Destroy(gameObject); return; }   // 얹을 게 없으면 정리
            TotalBuilt++;
            if (DebugLog) Debug.Log($"[SpawnVFX] {viewRoot.name}: 오버레이 {made}개 (skinned {skinned}) 첫목격 대기");

            // 진짜 몹 숨김(볼 때까지 아무것도 안 보이게)
            foreach (var r in viewRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.transform.IsChildOf(transform)) continue;
                if (r.enabled) { r.enabled = false; hiddenReal.Add(r); }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;

            if (!started)
            {
                if (!ReadyToStart()) return;        // 대기: 아직 못 봄 → 안 보임
                started = true;
                for (int i = 0; i < overlayRenderers.Count; i++)
                    if (overlayRenderers[i] != null) overlayRenderers[i].enabled = true;  // 오버레이 켬(진짜 몹 덮음)
                RestoreReal();                     // 진짜 몹 켬(오버레이가 덮으므로 팝 없음)
            }

            revealT += dt;
            float reveal = Mathf.Clamp01(revealT / Mathf.Max(0.1f, duration));
            for (int i = 0; i < mats.Count; i++)
                if (mats[i] != null) mats[i].SetFloat(RevealId, reveal);

            if (revealT >= duration) Destroy(gameObject);
        }

        /// <summary>플레이어가 이 몹을 지금 보고 있는가? (프러스텀 + 지형 가림 LOS). 타임아웃이면 무조건 시작.</summary>
        bool ReadyToStart()
        {
            if (t >= SightTimeout) return true;        // 안전장치: 오래 못 봤으면 그냥 드러냄
            if (viewRoot == null) return false;

            Camera cam = Main.Instance != null && Main.Instance.Cam != null ? Main.Instance.Cam : Camera.main;
            if (cam == null) return false;             // 카메라 없으면 타임아웃까지 대기

            Vector3 aim = viewRoot.position + Vector3.up * AimHeight;

            // 1) 카메라 프러스텀 안인가
            Vector3 vp = cam.WorldToViewportPoint(aim);
            if (vp.z <= 0f) return false;              // 뒤쪽
            if (vp.x < ViewMargin || vp.x > 1f - ViewMargin) return false;
            if (vp.y < ViewMargin || vp.y > 1f - ViewMargin) return false;

            // 2) 지형에 안 가려졌는가(LOS). 몹·플레이어엔 콜라이더가 없어 DefaultRaycastLayers는 지형만 친다.
            Vector3 from = cam.transform.position;
            Vector3 delta = aim - from;
            float dist = delta.magnitude;
            if (dist <= LosBackoff) return true;       // 코앞
            if (Physics.Raycast(from, delta / dist, dist - LosBackoff,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;                          // 사이에 지형 → 가려짐

            return true;
        }

        void RestoreReal()
        {
            for (int i = 0; i < hiddenReal.Count; i++)
                if (hiddenReal[i] != null) hiddenReal[i].enabled = true;
            hiddenReal.Clear();
        }

        void OnDestroy()
        {
            RestoreReal();   // 어떤 경로로 끝나도 진짜 몹은 반드시 다시 보이게
            foreach (var m in mats) if (m != null) Destroy(m);
            foreach (var mesh in bakedMeshes) if (mesh != null) Destroy(mesh);
        }
    }
}
