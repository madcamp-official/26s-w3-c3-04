using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 베기 이펙트. ★ 칼의 실제 움직임과 무관한 독립 이펙트.
    ///
    /// 프로 슬래시 VFX 구조:
    ///   · 메시는 <b>정적</b> — 칼이 3D를 훑고 간 곡면(원뿔 단면). 움직임은 노이즈 스크롤이 만든다.
    ///   · 두께감은 기하학이 아니라 <b>부드러운 형태마스크 × 곱해진 노이즈 × 다중 레이어</b>에서 나온다.
    ///   · 레이어를 반경·노이즈 오프셋을 어긋내 겹치면 가산으로 쌓여 볼륨이 생긴다.
    ///
    /// 텍스처: Tools/이펙트/베기 텍스처 굽기 로 생성(Art/VFX/Resources).
    /// 적중 이펙트(방사형 스트릭·링·섬광)는 별도 — 여기 없음.
    /// </summary>
    public class SwordSlash : MonoBehaviour
    {
        [Header("형태 (칼이 훑고 간 3D 곡면)")]
        public float sweepAngle = 130f;
        [Tooltip("회전축(+Z)에서 칼이 벌어진 각도. 90=납작, 작을수록 깊게 휨")]
        [Range(5f, 90f)] public float coneAngle = 52f;
        public float innerRadius = 0.9f;
        public float outerRadius = 4.2f;
        public int segmentsU = 80;
        public int segmentsV = 3;

        [Header("볼륨 (레이어 겹치기)")]
        [Tooltip("겹칠 레이어 수. 많을수록 두툼함")]
        [Range(1, 6)] public int layerCount = 4;
        [Tooltip("레이어마다 반경을 얼마나 어긋낼지")]
        public float layerScaleSpread = 0.055f;
        [Tooltip("바깥 레이어일수록 약하게")]
        [Range(0f, 1f)] public float layerFalloff = 0.45f;

        [Header("타이밍 (fast in, slow out)")]
        public float revealTime = 0.05f;
        public float holdTime   = 0.04f;
        public float fadeTime   = 0.20f;

        [Header("노이즈 (결)")]
        // v(반경) 타일을 낮게 두면 노이즈가 반경 방향으로 늘어나 "결"이 된다.
        public Vector2 noiseTile1   = new Vector2(3f, 0.35f);
        public Vector2 noiseScroll1 = new Vector2(-0.8f, 0.05f);
        public Vector2 noiseTile2   = new Vector2(7f, 0.5f);
        public Vector2 noiseScroll2 = new Vector2(-1.5f, -0.04f);
        [Tooltip("0=매끈한 면, 1=완전히 부서짐")]
        [Range(0f, 1f)] public float noiseAmount = 0.5f;
        [Range(0.2f, 6f)] public float contrast  = 1.35f;
        [Range(0f, 8f)]   public float intensity = 0.80f;

        [Header("색 (강도 기반 램프, HDR)")]
        [ColorUsage(true, true)] public Color colorLow  = new Color(0.12f, 1.0f, 0.10f, 1f);
        [ColorUsage(true, true)] public Color colorMid  = new Color(1.6f, 1.15f, 0.05f, 1f);
        [ColorUsage(true, true)] public Color colorHigh = new Color(3.2f, 3.2f, 2.6f, 1f);
        [Range(0f, 1f)] public float ramp1 = 0.18f;
        [Range(0f, 1f)] public float ramp2 = 0.55f;
        [Range(0f, 1f)] public float ramp3 = 0.85f;

        [Range(0.001f, 1f)] public float revealSoft = 0.12f;
        [Range(0f, 1f)]     public float tailFade   = 0.45f;

        [Header("튜닝")]
        [Tooltip("켜면 다 그어진 상태로 남아 사라지지 않음 → Inspector로 조절")]
        public bool hold;

        Mesh mesh;
        MeshRenderer[] layers;
        MaterialPropertyBlock mpb;
        float age;
        int builtHash;

        static readonly int IdShape = Shader.PropertyToID("_ShapeTex");
        static readonly int IdN1 = Shader.PropertyToID("_Noise1");
        static readonly int IdN2 = Shader.PropertyToID("_Noise2");
        static readonly int IdT1 = Shader.PropertyToID("_NoiseTile1");
        static readonly int IdS1 = Shader.PropertyToID("_NoiseScroll1");
        static readonly int IdT2 = Shader.PropertyToID("_NoiseTile2");
        static readonly int IdS2 = Shader.PropertyToID("_NoiseScroll2");
        static readonly int IdOff = Shader.PropertyToID("_NoiseOffset");
        static readonly int IdNAmt = Shader.PropertyToID("_NoiseAmount");
        static readonly int IdContrast = Shader.PropertyToID("_Contrast");
        static readonly int IdIntensity = Shader.PropertyToID("_Intensity");
        static readonly int IdCLow = Shader.PropertyToID("_ColorLow");
        static readonly int IdCMid = Shader.PropertyToID("_ColorMid");
        static readonly int IdCHigh = Shader.PropertyToID("_ColorHigh");
        static readonly int IdR1 = Shader.PropertyToID("_Ramp1");
        static readonly int IdR2 = Shader.PropertyToID("_Ramp2");
        static readonly int IdR3 = Shader.PropertyToID("_Ramp3");
        static readonly int IdRev = Shader.PropertyToID("_Reveal");
        static readonly int IdRevS = Shader.PropertyToID("_RevealSoft");
        static readonly int IdTail = Shader.PropertyToID("_TailFade");
        static readonly int IdFade = Shader.PropertyToID("_Fade");

        /// <summary>베기 생성. which: 1=평타1, 2=평타2(반대), t=찌르기.</summary>
        public static SwordSlash Spawn(Vector3 pos, Quaternion rot, string which)
        {
            var go = new GameObject("SwordSlash");
            var s = go.AddComponent<SwordSlash>();
            float roll;
            switch (which)
            {
                case "2":
                    roll = 205f; s.sweepAngle = -130f; break;
                case "t": case "thrust":
                    roll = 20f; s.sweepAngle = 62f; s.coneAngle = 26f; s.outerRadius = 5.2f;
                    s.revealTime = 0.035f; s.fadeTime = 0.13f; break;
                default:
                    roll = 20f; s.sweepAngle = 130f; break;
            }
            go.transform.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, 0f, roll));
            return s;
        }

        void Awake()
        {
            var sh = Shader.Find("Precog/SwordSlash");
            if (sh == null) { Debug.LogError("[SwordSlash] 셰이더 Precog/SwordSlash 없음"); Destroy(gameObject); return; }

            var shapeTex = Resources.Load<Texture2D>("Slash_Shape");
            var n1 = Resources.Load<Texture2D>("Slash_Noise1");
            var n2 = Resources.Load<Texture2D>("Slash_Noise2");
            if (shapeTex == null || n1 == null || n2 == null)
                Debug.LogWarning("[SwordSlash] 텍스처 없음 — 메뉴 Tools/이펙트/베기 텍스처 굽기 를 먼저 실행하십시오");

            mesh = new Mesh { name = "SwordSlashMesh" };
            mesh.MarkDynamic();
            BuildMesh();

            var mat = new Material(sh) { name = "SwordSlashMat" };
            if (shapeTex != null) mat.SetTexture(IdShape, shapeTex);
            if (n1 != null) mat.SetTexture(IdN1, n1);
            if (n2 != null) mat.SetTexture(IdN2, n2);

            // 레이어 생성 — 반경을 어긋내 겹치면 가산으로 쌓여 두께감이 생긴다
            int n = Mathf.Clamp(layerCount, 1, 6);
            layers = new MeshRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var lg = new GameObject("Layer" + i);
                lg.transform.SetParent(transform, false);
                float k = n == 1 ? 0f : (i / (float)(n - 1)) * 2f - 1f;   // -1..1
                lg.transform.localScale = Vector3.one * (1f + k * layerScaleSpread);

                lg.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = lg.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                layers[i] = r;
            }

            mpb = new MaterialPropertyBlock();
        }

        /// <summary>칼(축에서 coneAngle 벌어짐)을 +Z축 둘레로 sweepAngle 회전시킨 자취 면.</summary>
        void BuildMesh()
        {
            int nu = Mathf.Max(8, segmentsU) + 1;
            int nv = Mathf.Max(1, segmentsV) + 1;
            var verts = new Vector3[nu * nv];
            var uvs   = new Vector2[nu * nv];
            var cols  = new Color[nu * nv];
            var tris  = new int[(nu - 1) * (nv - 1) * 6];

            float cone = coneAngle * Mathf.Deg2Rad;
            Vector3 baseDir = new Vector3(Mathf.Sin(cone), 0f, Mathf.Cos(cone));

            for (int i = 0; i < nu; i++)
            {
                float u = i / (float)(nu - 1);
                Vector3 dir = Quaternion.AngleAxis(sweepAngle * u, Vector3.forward) * baseDir;
                for (int j = 0; j < nv; j++)
                {
                    float v = j / (float)(nv - 1);
                    int idx = i * nv + j;
                    verts[idx] = dir * Mathf.Lerp(innerRadius, outerRadius, v);
                    uvs[idx]   = new Vector2(u, v);
                    cols[idx]  = Color.white;
                }
            }

            int k2 = 0;
            for (int i = 0; i < nu - 1; i++)
                for (int j = 0; j < nv - 1; j++)
                {
                    int a = i * nv + j, b = (i + 1) * nv + j;
                    tris[k2++] = a; tris[k2++] = a + 1; tris[k2++] = b;
                    tris[k2++] = b; tris[k2++] = a + 1; tris[k2++] = b + 1;
                }

            mesh.Clear();
            mesh.vertices = verts; mesh.uv = uvs; mesh.colors = cols; mesh.triangles = tris;
            mesh.RecalculateBounds();
            builtHash = ShapeHash();
        }

        int ShapeHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + sweepAngle.GetHashCode();
                h = h * 31 + coneAngle.GetHashCode();
                h = h * 31 + innerRadius.GetHashCode();
                h = h * 31 + outerRadius.GetHashCode();
                h = h * 31 + segmentsU; h = h * 31 + segmentsV;
                return h;
            }
        }

        void Update()
        {
            if (mesh == null || layers == null) return;
            if (ShapeHash() != builtHash) BuildMesh();

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

            for (int i = 0; i < layers.Length; i++)
            {
                var r = layers[i];
                if (r == null) continue;
                float k = layers.Length == 1 ? 0f : i / (float)(layers.Length - 1);
                // 바깥 레이어일수록 약하게 + 노이즈를 어긋내 서로 다른 결이 겹치게
                float lay = Mathf.Lerp(1f, 1f - layerFalloff, Mathf.Abs(k * 2f - 1f));

                mpb.Clear();
                mpb.SetVector(IdT1, noiseTile1); mpb.SetVector(IdS1, noiseScroll1);
                mpb.SetVector(IdT2, noiseTile2); mpb.SetVector(IdS2, noiseScroll2);
                mpb.SetVector(IdOff, new Vector4(i * 0.37f, i * 0.19f, i * 0.53f, i * 0.11f));
                mpb.SetFloat(IdNAmt, noiseAmount);
                mpb.SetFloat(IdContrast, contrast);
                mpb.SetFloat(IdIntensity, intensity * lay);
                mpb.SetColor(IdCLow, colorLow); mpb.SetColor(IdCMid, colorMid); mpb.SetColor(IdCHigh, colorHigh);
                mpb.SetFloat(IdR1, ramp1); mpb.SetFloat(IdR2, ramp2); mpb.SetFloat(IdR3, ramp3);
                mpb.SetFloat(IdRev, reveal); mpb.SetFloat(IdRevS, revealSoft);
                mpb.SetFloat(IdTail, tailFade); mpb.SetFloat(IdFade, fade);
                r.SetPropertyBlock(mpb);
            }

            if (!hold && age >= revealTime + holdTime + fadeTime) Destroy(gameObject);
        }
    }
}
