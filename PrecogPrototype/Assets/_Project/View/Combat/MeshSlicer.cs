using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 평면 메시 슬라이서. ★ combat 소유·독립. 순수 유틸(정적).
    /// 메시를 평면(점+법선, 메시 로컬 공간)으로 두 조각으로 가른다.
    /// 걸치는 삼각형은 교점에서 쪼개고, 단면(cross-section)을 채워 "잘린 속살"을 만든다.
    /// 출력 메시는 서브메시 2개: 0=겉면(shell), 1=단면(cap) → 각기 다른 머티리얼.
    /// 볼록 메시(캡슐 등)에서 단면이 단일 볼록 폴리곤이라 부채꼴 삼각화로 충분.
    /// </summary>
    public static class MeshSlicer
    {
        class Frag
        {
            public readonly List<Vector3> verts = new();
            public readonly List<Vector3> norms = new();
            public readonly List<int> shell = new();   // 서브메시0
            public readonly List<int> cap   = new();   // 서브메시1

            public int Add(Vector3 v, Vector3 n) { verts.Add(v); norms.Add(n); return verts.Count - 1; }
            public bool Empty => shell.Count == 0;

            public Mesh Build()
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(verts);
                m.SetNormals(norms);
                m.subMeshCount = 2;
                m.SetTriangles(shell, 0);
                m.SetTriangles(cap, 1);
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>메시를 로컬 평면으로 슬라이스. 실제로 갈리면 true.</summary>
        public static bool Slice(Mesh src, Vector3 planePoint, Vector3 planeNormal,
                                 out Mesh above, out Mesh below)
        {
            above = below = null;
            if (src == null) return false;
            planeNormal = planeNormal.normalized;

            Vector3[] sv = src.vertices;
            Vector3[] sn = src.normals;
            int[] tri = src.triangles;
            bool hasN = sn != null && sn.Length == sv.Length;

            var A = new Frag();   // planeNormal 쪽(+)
            var B = new Frag();   // 반대쪽(-)
            var cut = new List<Vector3>();

            for (int t = 0; t < tri.Length; t += 3)
            {
                int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
                ClipTriangle(sv[i0], sv[i1], sv[i2],
                             hasN ? sn[i0] : Vector3.up, hasN ? sn[i1] : Vector3.up, hasN ? sn[i2] : Vector3.up,
                             planePoint, planeNormal, A, B, cut);
            }

            BuildCaps(cut, planeNormal, A, B);

            if (A.Empty || B.Empty) return false;   // 평면이 메시를 실제로 안 가름
            above = A.Build();
            below = B.Build();
            return true;
        }

        static float SD(Vector3 v, Vector3 p, Vector3 n) => Vector3.Dot(v - p, n);

        /// <summary>삼각형을 평면으로 클립 → A/B 각각의 폴리곤을 부채꼴 삼각화. 교점은 cut에 수집.</summary>
        static void ClipTriangle(Vector3 a, Vector3 b, Vector3 c,
                                 Vector3 na, Vector3 nb, Vector3 nc,
                                 Vector3 p, Vector3 n,
                                 Frag A, Frag B, List<Vector3> cut)
        {
            Vector3[] vs = { a, b, c };
            Vector3[] ns = { na, nb, nc };
            float[] d = { SD(a, p, n), SD(b, p, n), SD(c, p, n) };

            var aPoly = new List<(Vector3 v, Vector3 nn)>(4);
            var bPoly = new List<(Vector3 v, Vector3 nn)>(4);

            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                float di = d[i], dj = d[j];
                if (di >= 0f) aPoly.Add((vs[i], ns[i])); else bPoly.Add((vs[i], ns[i]));

                if ((di >= 0f) != (dj >= 0f))   // 에지가 평면을 가로지름
                {
                    float tt = di / (di - dj);
                    Vector3 ip = Vector3.Lerp(vs[i], vs[j], tt);
                    Vector3 inn = Vector3.Lerp(ns[i], ns[j], tt).normalized;
                    aPoly.Add((ip, inn));
                    bPoly.Add((ip, inn));
                    cut.Add(ip);
                }
            }

            FanShell(aPoly, A);
            FanShell(bPoly, B);
        }

        static void FanShell(List<(Vector3 v, Vector3 nn)> poly, Frag f)
        {
            if (poly.Count < 3) return;
            int i0 = f.Add(poly[0].v, poly[0].nn);
            for (int k = 1; k < poly.Count - 1; k++)
            {
                int ia = f.Add(poly[k].v, poly[k].nn);
                int ib = f.Add(poly[k + 1].v, poly[k + 1].nn);
                f.shell.Add(i0); f.shell.Add(ia); f.shell.Add(ib);
            }
        }

        /// <summary>단면 캡: 교점들을 평면상 각도순 정렬 → 중심 부채꼴. 양면 머티리얼이라 와인딩 무관.</summary>
        static void BuildCaps(List<Vector3> cut, Vector3 n, Frag A, Frag B)
        {
            // 근접 중복 제거
            var pts = new List<Vector3>();
            foreach (var c in cut)
            {
                bool dup = false;
                for (int i = 0; i < pts.Count; i++)
                    if ((pts[i] - c).sqrMagnitude < 1e-6f) { dup = true; break; }
                if (!dup) pts.Add(c);
            }
            if (pts.Count < 3) return;

            Vector3 ctr = Vector3.zero;
            foreach (var c in pts) ctr += c;
            ctr /= pts.Count;

            // 평면 기저
            Vector3 u = Vector3.Cross(n, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(n, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(n, u);

            pts.Sort((x, y) =>
            {
                float ax = Mathf.Atan2(Vector3.Dot(x - ctr, v), Vector3.Dot(x - ctr, u));
                float ay = Mathf.Atan2(Vector3.Dot(y - ctr, v), Vector3.Dot(y - ctr, u));
                return ax.CompareTo(ay);
            });

            AddCap(B, ctr, pts,  n);   // -측 조각의 단면은 +n 향함
            AddCap(A, ctr, pts, -n);   // +측 조각의 단면은 -n 향함
        }

        static void AddCap(Frag f, Vector3 ctr, List<Vector3> pts, Vector3 capN)
        {
            int c = f.Add(ctr, capN);
            int m = pts.Count;
            for (int i = 0; i < m; i++)
            {
                int a = f.Add(pts[i], capN);
                int b = f.Add(pts[(i + 1) % m], capN);
                f.cap.Add(c); f.cap.Add(a); f.cap.Add(b);
            }
        }
    }
}
