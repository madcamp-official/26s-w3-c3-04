using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 겐지 용검식 베기 이펙트. ★ 칼의 실제 움직임과 무관한 독립 이펙트.
    ///
    /// 형태: 평면이 아니라 <b>칼이 3D 공간을 훑고 지나간 곡면</b>(원뿔 단면)이다.
    ///   스윙 축(로컬 +Z)을 중심으로, 축과 coneAngle만큼 벌어진 칼을 sweepAngle만큼 회전시킨
    ///   자취 면을 만든다. → 3인칭에서 보면 휘어진 부채꼴로 깊이감이 생기고,
    ///     1인칭에서는 화면을 가로지르며 훑고 지나간다.
    ///
    /// 로컬 축: +Z = 스윙 회전축(대략 시선 방향), 칼은 축에서 coneAngle 벌어져 회전.
    /// 셰이더 Precog/SwordSlash 와 짝 (uv.x=스윙, uv.y=반경).
    ///
    /// 적중 이펙트(방사형 스트릭·링·섬광)는 별도입니다 — 여기 없음.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SwordSlash : MonoBehaviour
    {
        [Header("형태 (3D 곡면)")]
        [Tooltip("스윙이 훑는 각도(도)")]
        public float sweepAngle = 115f;
        [Tooltip("회전축(+Z)에서 칼이 벌어진 각도(도). 90=납작한 원판, 작을수록 깊게 휜 원뿔")]
        [Range(5f, 90f)] public float coneAngle = 48f;
        [Tooltip("안쪽 반경(손잡이 쪽)")]
        public float innerRadius = 1.1f;
        [Tooltip("바깥 반경(칼끝)")]
        public float outerRadius = 4.4f;
        [Tooltip("스윙 방향 분할(곡면 매끄러움)")]
        public int segmentsU = 72;
        [Tooltip("반경 분할(원뿔은 직선이라 2로 충분)")]
        public int segmentsV = 3;

        [Header("타이밍 (fast in, slow out)")]
        public float revealTime = 0.05f;
        public float holdTime   = 0.03f;
        public float fadeTime   = 0.17f;

        [Header("색 (HDR — Bloom이 물어감)")]
        [ColorUsage(true, true)] public Color coreColor = new Color(3.0f, 3.0f, 2.4f, 1f);    // 최외곽 흰 테두리
        [ColorUsage(true, true)] public Color midColor  = new Color(1.8f, 1.25f, 0.06f, 1f);  // 노랑
        [ColorUsage(true, true)] public Color edgeColor = new Color(0.25f, 1.5f, 0.15f, 1f);  // 안쪽 녹색
        [Range(0f, 1f)] public float rimStart  = 0.38f;
        [Range(0f, 1f)] public float coreStart = 0.88f;
        [Range(0f, 1f)] public float innerFade = 0.50f;

        [Header("결(줄무늬)")]
        [Range(0f, 80f)] public float streakFreq = 44f;
        [Range(0f, 1f)]  public float streakAmt  = 0.72f;

        [Range(0.001f, 1f)] public float revealSoft = 0.10f;
        [Range(0f, 1f)]     public float tailFade   = 0.45f;

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
        static readonly int IdRimS = Shader.PropertyToID("_RimStart");
        static readonly int IdCoreS = Shader.PropertyToID("_CoreStart");
        static readonly int IdInner = Shader.PropertyToID("_InnerFade");
        static readonly int IdSFreq = Shader.PropertyToID("_StreakFreq");
        static readonly int IdSAmt  = Shader.PropertyToID("_StreakAmt");
        static readonly int IdRev   = Shader.PropertyToID("_Reveal");
        static readonly int IdRevS  = Shader.PropertyToID("_RevealSoft");
        static readonly int IdTail  = Shader.PropertyToID("_TailFade");
        static readonly int IdFade  = Shader.PropertyToID("_Fade");

        /// <summary>베기 하나 생성. which: 1=평타1, 2=평타2(반대), t=찌르기(좁게).</summary>
        public static SwordSlash Spawn(Vector3 pos, Quaternion rot, string which)
        {
            var go = new GameObject("SwordSlash");
            var s = go.AddComponent<SwordSlash>();

            // rot의 +Z를 스윙 축으로 삼는다. Z축 롤로 스윙이 시작되는 각도를 정함.
            float roll;
            switch (which)
            {
                case "2":                   // 평타2 — 반대 방향(우상 → 좌하)
                    roll = 200f;
                    s.sweepAngle = -150f;
                    break;
                case "t": case "thrust":    // 찌르기 — 좁고 깊게 찌르는 원뿔
                    roll = 20f;
                    s.sweepAngle = 70f;
                    s.coneAngle  = 26f;
                    s.outerRadius = 5.0f;
                    s.revealTime = 0.035f; s.fadeTime = 0.12f;
                    break;
                default:                    // 평타1 — 좌하 → 우상
                    roll = 20f;
                    s.sweepAngle = 150f;
                    break;
            }
            go.transform.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, 0f, roll));
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

        /// <summary>
        /// 칼(축에서 coneAngle 벌어진 방향)을 +Z축 둘레로 sweepAngle만큼 돌린 자취 면.
        /// coneAngle=90이면 납작한 원판, 작을수록 앞으로 휜 원뿔이 되어 3D 깊이감이 생긴다.
        /// </summary>
        void BuildMesh()
        {
            int nu = Mathf.Max(8, segmentsU) + 1;
            int nv = Mathf.Max(1, segmentsV) + 1;

            var verts = new Vector3[nu * nv];
            var uvs   = new Vector2[nu * nv];
            var cols  = new Color[nu * nv];
            var tris  = new int[(nu - 1) * (nv - 1) * 6];

            float cone = coneAngle * Mathf.Deg2Rad;
            // 축(+Z)에서 cone만큼 벌어진 기준 칼 방향
            Vector3 baseDir = new Vector3(Mathf.Sin(cone), 0f, Mathf.Cos(cone));

            for (int i = 0; i < nu; i++)
            {
                float u = i / (float)(nu - 1);
                float ang = sweepAngle * u;
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.forward) * baseDir;

                for (int j = 0; j < nv; j++)
                {
                    float v = j / (float)(nv - 1);
                    float r = Mathf.Lerp(innerRadius, outerRadius, v);
                    int idx = i * nv + j;
                    verts[idx] = dir * r;
                    uvs[idx]   = new Vector2(u, v);
                    cols[idx]  = Color.white;
                }
            }

            int k = 0;
            for (int i = 0; i < nu - 1; i++)
                for (int j = 0; j < nv - 1; j++)
                {
                    int a = i * nv + j;
                    int b = (i + 1) * nv + j;
                    tris[k++] = a;     tris[k++] = a + 1; tris[k++] = b;
                    tris[k++] = b;     tris[k++] = a + 1; tris[k++] = b + 1;
                }

            mesh.Clear();
            mesh.vertices  = verts;
            mesh.uv        = uvs;
            mesh.colors    = cols;
            mesh.triangles = tris;
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
                h = h * 31 + segmentsU;
                h = h * 31 + segmentsV;
                return h;
            }
        }

        void Update()
        {
            if (mesh == null) return;
            if (ShapeHash() != builtHash) BuildMesh();   // Inspector로 형태 바꾸면 즉시 반영

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
            mpb.SetFloat(IdRimS,  rimStart);
            mpb.SetFloat(IdCoreS, coreStart);
            mpb.SetFloat(IdInner, innerFade);
            mpb.SetFloat(IdSFreq, streakFreq);
            mpb.SetFloat(IdSAmt,  streakAmt);
            mpb.SetFloat(IdRev,   reveal);
            mpb.SetFloat(IdRevS,  revealSoft);
            mpb.SetFloat(IdTail,  tailFade);
            mpb.SetFloat(IdFade,  fade);
            mr.SetPropertyBlock(mpb);

            if (!hold && age >= revealTime + holdTime + fadeTime) Destroy(gameObject);
        }
    }
}
