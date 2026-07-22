using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스폰 실체화 VFX(설계 §4-⑥). 몹 재질을 건드리지 않는 <b>디졸브 복제본 오버레이</b> 방식(A').
    ///
    /// 스폰 순간:
    ///  1) 진짜 몹 렌더러를 <b>숨긴다</b>(생성 전엔 안 보여야 한다는 요구).
    ///  2) 몹 메시를 스냅샷한 복제본에 디졸브 셰이더를 얹어 위→아래 X레이 실체화를 재생.
    ///  3) 실체화가 거의 끝나면 진짜 몹을 다시 켜고(등장), 오버레이는 사라진다.
    ///
    /// 진짜 몹 재질·발광은 안 건드리고 <b>렌더러 on/off만</b> 한다(안전, 되돌리기 쉬움).
    /// 어떤 경로로 끝나도 OnDestroy에서 반드시 재활성한다(몹이 영구히 안 보이는 사고 방지).
    ///
    /// 스킨드 메시는 BakeMesh로 현재 포즈를 정적 스냅샷(낙하 ~1.6초라 정지 포즈로 충분).
    /// </summary>
    public class SpawnMaterialize : MonoBehaviour
    {
        const string ShaderName = "Precog/SpawnMaterialize";
        static Shader shader;
        static bool shaderMissingLogged;

        /// <summary>진단용 — Play가 성공적으로 오버레이를 만든 횟수(누적).</summary>
        public static int TotalBuilt;

        float duration = 1.6f;
        float hold = 0.3f;          // 시작에 전신 X레이를 잠깐 유지 후 스캔
        float revealMobAt = 0.85f;  // 이 진행도에서 진짜 몹을 다시 켠다(거의 끝)
        float t;
        bool mobShown;

        readonly List<Material> mats = new List<Material>();
        readonly List<Mesh> bakedMeshes = new List<Mesh>();
        readonly List<Renderer> hiddenReal = new List<Renderer>();   // 껐다 다시 켤 진짜 렌더러

        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly int MinYId   = Shader.PropertyToID("_MinY");
        static readonly int MaxYId   = Shader.PropertyToID("_MaxY");

        public static void Play(Transform viewRoot, float duration = 1.6f)
        {
            if (viewRoot == null) return;
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                if (!shaderMissingLogged)
                { Debug.LogWarning("[SpawnMaterialize] 셰이더 '" + ShaderName + "' 없음 — VFX 생략."); shaderMissingLogged = true; }
                return;
            }
            var host = new GameObject("~Materialize");
            host.transform.SetParent(viewRoot, false);
            var sm = host.AddComponent<SpawnMaterialize>();
            sm.duration = Mathf.Max(0.2f, duration);
            sm.Build(viewRoot);
        }

        void Build(Transform viewRoot)
        {
            foreach (var r in viewRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null) continue;
                if (r.transform.IsChildOf(transform)) continue;   // 자기 오버레이 제외

                Mesh mesh = null;
                Transform anchor = r.transform;

                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    mesh = new Mesh();
                    smr.BakeMesh(mesh);                 // 현재 포즈 → 정적 메시(smr 로컬 공간)
                    bakedMeshes.Add(mesh);
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                if (mesh == null) continue;

                // 오버레이 = 소스 렌더러와 같은 트랜스폼에 얹음
                var go = new GameObject("~mat_" + r.name);
                var tr = go.transform;
                tr.SetParent(anchor, false);
                tr.localPosition = Vector3.zero;
                tr.localRotation = Quaternion.identity;
                tr.localScale = Vector3.one;

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                var m = new Material(shader);
                Bounds b = mesh.bounds;
                m.SetFloat(MinYId, b.min.y);
                m.SetFloat(MaxYId, b.max.y);
                m.SetFloat(RevealId, 0f);
                mr.sharedMaterial = m;
                mats.Add(m);

                // 진짜 렌더러 숨김(생성 전엔 안 보이게)
                if (r.enabled) { r.enabled = false; hiddenReal.Add(r); }
            }

            if (mats.Count == 0) { RestoreReal(); Destroy(gameObject); return; }   // 얹을 게 없으면 즉시 정리
            TotalBuilt++;   // 진단
        }

        void Update()
        {
            t += Time.deltaTime;
            float reveal = Mathf.Clamp01((t - hold) / Mathf.Max(0.1f, duration - hold));
            for (int i = 0; i < mats.Count; i++)
                if (mats[i] != null) mats[i].SetFloat(RevealId, reveal);

            if (!mobShown && reveal >= revealMobAt) { RestoreReal(); mobShown = true; }   // 거의 끝 → 진짜 몹 등장
            if (t >= duration) Destroy(gameObject);
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
