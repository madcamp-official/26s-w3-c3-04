using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>마커 종류. 하강은 항상 전 지상몹 허용, 변수는 "상승 권한"뿐이라 3종으로 줄어든다.</summary>
    public enum TraversalLinkKind : byte
    {
        Normal = 0,             // ① 일반      — 하강·상승 모두 전 지상몹
        AscendRestricted = 1,   // ② 상승제한  — 하강 전부, 상승은 Traversal 특성만
        DescendOnly = 2,        // ③ 하강전용  — 하강만(아무도 못 올라옴)
    }

    /// <summary>
    /// 층이동 마커. ★ 층 전환의 <b>유일한 권위</b>(NavMesh는 연속 표면=경사로까지만 담당).
    /// 씬에 손으로 배치하고, Bake가 이걸 읽어 NavMeshLink(평상시) + 그래프 링크(예측)를 만든다.
    ///
    /// 표시·길이는 <b>직선</b>, 실제 이동은 <b>정점 보장 탄도</b>(TraversalBallistics).
    /// 주저·멈칫은 직선 길이 비례(데드존 없음 — 짧아도 최소값).
    ///
    /// 좌표: 이 오브젝트의 transform = A점, endOffset(로컬) = B점.
    /// 오브젝트를 옮기면 링크 전체가 따라오고, 핸들로 B점만 따로 조정한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class TraversalLink : MonoBehaviour
    {
        [Header("종류")]
        public TraversalLinkKind kind = TraversalLinkKind.Normal;

        [Header("끝점 (A = 이 오브젝트 위치, B = 아래 오프셋)")]
        public Vector3 endOffset = new Vector3(0f, -3f, 6f);

        [Header("궤적")]
        [Tooltip("체크 해제하면 clearance를 직접 지정(수동)")]
        public bool  clearanceAuto = true;
        [Tooltip("두 끝점 중 높은 쪽보다 얼마나 더 솟을지(m)")]
        public float clearance = 1.5f;
        [Tooltip("0이면 SimConfig.TraversalGravity 사용")]
        public float gravity = 0f;

        [Header("주저·멈칫 (음수 = 길이 비례 자동)")]
        public int pauseTicksOverride   = -1;
        public int recoverTicksOverride = -1;

        [Header("착지 슬롯 (동시 도약 혼잡 방지)")]
        public bool  slotsAuto  = true;
        public int   slotCount  = 3;
        public float slotSpread = 1.4f;

        // ── 파생값 ──
        public Vector3 PointA => transform.position;
        public Vector3 PointB => transform.TransformPoint(endOffset);

        /// <summary>높은 쪽 / 낮은 쪽. 하강 = High→Low, 상승 = Low→High.</summary>
        public Vector3 High => PointA.y >= PointB.y ? PointA : PointB;
        public Vector3 Low  => PointA.y >= PointB.y ? PointB : PointA;

        public float Length => Vector3.Distance(PointA, PointB);

        /// <summary>상승(낮은 곳 → 높은 곳)이 허용되는가. 종류로만 결정된다.</summary>
        public bool AscendAllowed => kind != TraversalLinkKind.DescendOnly;
        /// <summary>상승이 Traversal 특성 몹으로 제한되는가.</summary>
        public bool AscendTraversalOnly => kind == TraversalLinkKind.AscendRestricted;

        public float EffectiveClearance
        {
            get
            {
                if (!clearanceAuto) return Mathf.Max(SimConfig.TraversalMinClearance, clearance);
                float auto = Length * SimConfig.TraversalClearanceRatio;
                return Mathf.Clamp(auto, SimConfig.TraversalMinClearance, SimConfig.TraversalMaxClearance);
            }
        }

        public int PauseTicks => pauseTicksOverride >= 0 ? pauseTicksOverride
            : TraversalBallistics.LengthToTicks(Length, SimConfig.TraversalPauseMin, SimConfig.TraversalPauseMax,
                                                SimConfig.TraversalLengthRef, SimConfig.TraversalLengthExp);

        public int RecoverTicks => recoverTicksOverride >= 0 ? recoverTicksOverride
            : TraversalBallistics.LengthToTicks(Length, SimConfig.TraversalRecoverMin, SimConfig.TraversalRecoverMax,
                                                SimConfig.TraversalLengthRef, SimConfig.TraversalLengthExp);

        /// <summary>하강 궤적(High→Low). 항상 존재한다.</summary>
        public BallisticArc DescendArc => TraversalBallistics.Solve(High, Low, EffectiveClearance, gravity);
        /// <summary>상승 궤적(Low→High). 종류가 허용할 때만 의미 있다.</summary>
        public BallisticArc AscendArc  => TraversalBallistics.Solve(Low, High, EffectiveClearance, gravity);

        /// <summary>착지 슬롯 위치들. 착지점 둘레 링에 균등 배치(개수 0이면 착지점 하나).</summary>
        public void GetSlots(Vector3 landing, System.Collections.Generic.List<Vector3> outSlots)
        {
            outSlots.Clear();
            int n = Mathf.Clamp(slotsAuto ? 3 : slotCount, 1, SimConfig.TraversalSlotMax);
            if (n == 1) { outSlots.Add(landing); return; }
            float r = slotsAuto ? Mathf.Max(0.8f, SimConfig.EnemyRadius * SimConfig.TraversalSlotGapMul) : slotSpread;
            for (int i = 0; i < n; i++)
            {
                float ang = (360f / n) * i * Mathf.Deg2Rad;
                outSlots.Add(landing + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r);
            }
        }

        // ── 기즈모 ──
        static readonly System.Collections.Generic.List<Vector3> slotBuf = new System.Collections.Generic.List<Vector3>();

        void OnDrawGizmos() => Draw(false);
        void OnDrawGizmosSelected() => Draw(true);

        void Draw(bool selected)
        {
            Vector3 a = PointA, b = PointB;
            if ((a - b).sqrMagnitude < 1e-6f) return;

            // 직선(저작 표현) — 길이의 기준
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, selected ? 0.9f : 0.45f);
            Gizmos.DrawLine(a, b);

            // 끝점
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(a, 0.25f);
            Gizmos.DrawWireSphere(b, 0.25f);

            // 하강 궤적(항상 허용)
            DrawArc(DescendArc, KindColor(true), selected);
            // 상승 궤적(허용 시)
            if (AscendAllowed) DrawArc(AscendArc, KindColor(false), selected);

            if (!selected) return;

            // 착지 슬롯
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
            GetSlots(Low, slotBuf);
            foreach (var s in slotBuf) Gizmos.DrawWireSphere(s, 0.28f);
            if (AscendAllowed)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
                GetSlots(High, slotBuf);
                foreach (var s in slotBuf) Gizmos.DrawWireSphere(s, 0.28f);
            }
        }

        Color KindColor(bool descend)
        {
            if (descend) return new Color(0.35f, 0.95f, 0.45f, 0.95f);          // 하강 = 초록(항상 전부)
            switch (kind)
            {
                case TraversalLinkKind.AscendRestricted:
                    return new Color(1f, 0.75f, 0.15f, 0.95f);                  // 상승제한 = 주황
                default:
                    return new Color(0.35f, 0.8f, 1f, 0.95f);                   // 일반 상승 = 파랑
            }
        }

        static void DrawArc(BallisticArc arc, Color c, bool selected)
        {
            if (!arc.IsValid) return;
            Gizmos.color = c;
            int seg = selected ? 28 : 14;
            Vector3 prev = arc.At(0);
            for (int i = 1; i <= seg; i++)
            {
                int tick = Mathf.RoundToInt((float)arc.flightTicks * i / seg);
                Vector3 cur = arc.At(tick);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }
    }
}
