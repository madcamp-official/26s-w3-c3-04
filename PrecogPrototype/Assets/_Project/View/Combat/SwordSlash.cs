using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 겐지 용검식 베기 이펙트. ★ 칼의 실제 움직임과 무관한 독립 이펙트.
    /// 아주 긴 쐐기(한쪽 두껍고 반대로 갈수록 뾰족) 메시를 절차 생성하고,
    /// 길이 방향으로 훑고 지나가며 나타났다가 사라진다.
    ///
    /// 로컬 축: +X = 길이 방향, ±Y = 폭, +Z = 앞(카메라를 향하게 배치).
    /// 셰이더 Precog/SwordSlash 와 짝(uv.x=길이, uv.y=폭, 0.5=중심선).
    ///
    /// 적중 이펙트(방사형 스트릭·링·섬광)는 별도입니다 — 여기 없음.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SwordSlash : MonoBehaviour
    {
        [Header("형태")]
        [Tooltip("길이(월드 단위). 화면을 가로지를 만큼 길게")]
        public float length = 14f;
        [Tooltip("가장 두꺼운 쪽 폭")]
        public float maxWidth = 2.2f;
        [Tooltip("휨(도). 0=직선, 클수록 곡선")]
        public float bend = 22f;
        [Tooltip("끝으로 갈수록 얇아지는 정도(클수록 빨리 뾰족해짐)")]
        public float taper = 1.35f;
        [Tooltip("시작쪽도 살짝 둥글게 좁힘(0=안 좁힘)")]
        [Range(0f, 0.5f)] public float startRound = 0.08f;
        public int segments = 64;

        [Header("타이밍 (fast in, slow out)")]
        public float revealTime = 0.045f;   // 훑고 지나가는 시간
        public float holdTime   = 0.035f;
        public float fadeTime   = 0.16f;

        // ※ 가산 합성이라 "배경이 밝으면" 전부 흰색으로 클램프된다(겐지도 동일).
        //    어두운~중간 톤 환경에서 색이 제대로 읽힌다. 값은 어두운 배경 기준으로 맞춤.
        [Header("색 (HDR — Bloom이 물어감)")]
        [ColorUsage(true, true)] public Color coreColor = new Color(2.6f, 2.6f, 2.1f, 1f);    // 흰 코어(과노출)
        [ColorUsage(true, true)] public Color midColor  = new Color(1.7f, 1.10f, 0.04f, 1f);  // 노랑 (파랑 죽여야 노랑으로 읽힘)
        [ColorUsage(true, true)] public Color edgeColor = new Color(0.22f, 1.5f, 0.12f, 1f);  // 녹색
        [Range(0f, 1f)] public float coreSize = 0.06f;
        [Range(0f, 1f)] public float midSize  = 0.28f;
        [Range(0f, 1f)] public float edgeSoft = 0.12f;
        [Range(0.001f, 1f)] public float revealSoft = 0.12f;
        [Range(0f, 1f)] public float tailFade = 0.25f;

        [Header("튜닝")]
        [Tooltip("켜면 다 그어진 상태로 남아 사라지지 않음 → Inspector로 조절")]
        public bool hold;

        Mesh mesh;
        MeshRenderer mr;
        MaterialPropertyBlock mpb;
        float age;
        int builtHash;

        static readonly int IdCore = Shader.PropertyToID("_CoreColor");
        static readonly int IdMid  = Shader.PropertyToID("_MidColor");
        static readonly int IdEdge = Shader.PropertyToID("_EdgeColor");
        static readonly int IdCoreS = Shader.PropertyToID("_CoreSize");
        static readonly int IdMidS  = Shader.PropertyToID("_MidSize");
        static readonly int IdEdgeS = Shader.PropertyToID("_EdgeSoft");
        static readonly int IdRev   = Shader.PropertyToID("_Reveal");
        static readonly int IdRevS  = Shader.PropertyToID("_RevealSoft");
        static readonly int IdTail  = Shader.PropertyToID("_TailFade");
        static readonly int IdFade  = Shader.PropertyToID("_Fade");

        /// <summary>베기 하나 생성. which: 1=평타1, 2=평타2(반대 방향), t=찌르기(가늘고 김).</summary>
        public static SwordSlash Spawn(Vector3 pos, Quaternion rot, string which)
        {
            var go = new GameObject("SwordSlash");
            go.transform.SetPositionAndRotation(pos, rot);
            var s = go.AddComponent<SwordSlash>();

            switch (which)
            {
                case "2":   // 평타2 — 반대로 휘고 반대 방향으로 기울임
                    s.bend = -22f;
                    go.transform.rotation = rot * Quaternion.Euler(0f, 0f, 205f);
                    break;
                case "t": case "thrust":   // 찌르기 — 가늘고 더 김
                    s.maxWidth = 1.1f; s.length = 18f; s.bend = 6f; s.taper = 1.8f;
                    s.revealTime = 0.03f; s.fadeTime = 0.12f;
                    go.transform.rotation = rot * Quaternion.Euler(0f, 0f, 15f);
                    break;
                default:    // 평타1 — 좌하 → 우상 대각선
                    s.bend = 22f;
                    go.transform.rotation = rot * Quaternion.Euler(0f, 0f, 25f);
                    break;
            }
            return s;
        }

        void Awake()
        {
            mesh = new Mesh { name = "SwordSlashMesh" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            mr = GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var sh = Shader.Find("Precog/SwordSlash");
            if (sh == null) { Debug.LogError("[SwordSlash] 셰이더 Precog/SwordSlash 없음"); Destroy(gameObject); return; }
            mr.sharedMaterial = new Material(sh) { name = "SwordSlashMat" };

            mpb = new MaterialPropertyBlock();
            BuildMesh();
        }

        /// <summary>중심선을 호로 그리고, 끝으로 갈수록 좁아지는 쐐기 리본을 만든다.</summary>
        void BuildMesh()
        {
            int n = Mathf.Max(4, segments) + 1;
            var verts = new Vector3[n * 2];
            var uvs   = new Vector2[n * 2];
            var cols  = new Color[n * 2];
            var tris  = new int[(n - 1) * 6];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector2 p  = CenterAt(t);
                Vector2 tg = (CenterAt(Mathf.Min(1f, t + 0.001f)) - CenterAt(Mathf.Max(0f, t - 0.001f)));
                if (tg.sqrMagnitude < 1e-8f) tg = Vector2.right;
                tg.Normalize();
                Vector2 nrm = new Vector2(-tg.y, tg.x);

                // 끝으로 갈수록 뾰족 + 시작쪽도 살짝 둥글게
                float w = maxWidth * Mathf.Pow(Mathf.Clamp01(1f - t), taper);
                if (startRound > 0f) w *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / startRound));
                w *= 0.5f;

                Vector2 a = p - nrm * w;
                Vector2 b = p + nrm * w;

                int v0 = i * 2, v1 = v0 + 1;
                verts[v0] = new Vector3(a.x, a.y, 0f);
                verts[v1] = new Vector3(b.x, b.y, 0f);
                uvs[v0] = new Vector2(t, 0f);
                uvs[v1] = new Vector2(t, 1f);
                cols[v0] = Color.white; cols[v1] = Color.white;
            }

            for (int i = 0; i < n - 1; i++)
            {
                int k = i * 6, v = i * 2;
                tris[k + 0] = v;     tris[k + 1] = v + 1; tris[k + 2] = v + 2;
                tris[k + 3] = v + 1; tris[k + 4] = v + 3; tris[k + 5] = v + 2;
            }

            mesh.Clear();
            mesh.vertices  = verts;
            mesh.uv        = uvs;
            mesh.colors    = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            builtHash = ShapeHash();
        }

        /// <summary>중심선: bend가 0이면 직선, 아니면 호.</summary>
        Vector2 CenterAt(float t)
        {
            float br = bend * Mathf.Deg2Rad;
            if (Mathf.Abs(br) < 1e-4f) return new Vector2(t * length, 0f);
            float R = length / br;
            float a = br * t;
            return new Vector2(R * Mathf.Sin(a), R * (1f - Mathf.Cos(a)));
        }

        int ShapeHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + length.GetHashCode();
                h = h * 31 + maxWidth.GetHashCode();
                h = h * 31 + bend.GetHashCode();
                h = h * 31 + taper.GetHashCode();
                h = h * 31 + startRound.GetHashCode();
                h = h * 31 + segments;
                return h;
            }
        }

        void Update()
        {
            if (mesh == null) return;
            if (ShapeHash() != builtHash) BuildMesh();   // Inspector에서 형태 바꾸면 즉시 반영

            float reveal, fade;
            if (hold) { reveal = 1f; fade = 1f; }
            else
            {
                age += Time.deltaTime;
                reveal = revealTime <= 0f ? 1f : Mathf.Clamp01(age / revealTime);
                fade = age <= revealTime + holdTime
                    ? 1f
                    : 1f - Mathf.Clamp01((age - revealTime - holdTime) / Mathf.Max(fadeTime, 0.0001f));
            }

            mpb.SetColor(IdCore, coreColor);
            mpb.SetColor(IdMid,  midColor);
            mpb.SetColor(IdEdge, edgeColor);
            mpb.SetFloat(IdCoreS, coreSize);
            mpb.SetFloat(IdMidS,  midSize);
            mpb.SetFloat(IdEdgeS, edgeSoft);
            mpb.SetFloat(IdRev,   reveal);
            mpb.SetFloat(IdRevS,  revealSoft);
            mpb.SetFloat(IdTail,  tailFade);
            mpb.SetFloat(IdFade,  fade);
            mr.SetPropertyBlock(mpb);

            if (!hold && age >= revealTime + holdTime + fadeTime) Destroy(gameObject);
        }
    }
}
