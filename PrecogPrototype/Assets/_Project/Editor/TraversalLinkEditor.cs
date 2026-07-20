using UnityEditor;
using UnityEngine;
using Game.Sim;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 층이동 링크 에디터. 끝점을 Scene 뷰에서 직접 끌고, 정점(apex)을 드래그해 궤적을 눈으로 맞춘다.
    /// 자동이 기본 — 손대면 수동으로 전환되고, "자동으로 되돌리기"로 언제든 복귀한다.
    /// </summary>
    [CustomEditor(typeof(TraversalLink))]
    [CanEditMultipleObjects]
    public class TraversalLinkEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var link = (TraversalLink)target;

            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("계산 결과", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("직선 길이(m)", link.Length);
                EditorGUILayout.FloatField("적용 clearance(m)", link.EffectiveClearance);
                EditorGUILayout.IntField("주저(틱)", link.PauseTicks);
                EditorGUILayout.IntField("멈칫(틱)", link.RecoverTicks);
                var d = link.DescendArc;
                EditorGUILayout.IntField("하강 비행(틱)", d.flightTicks);
                EditorGUILayout.FloatField("착지 수직속도(m/s)", d.LandingVy());
                int total = TraversalBallistics.TotalTicks(link.PauseTicks, d.flightTicks, link.RecoverTicks);
                EditorGUILayout.IntField("총 소요(틱)", total);
                EditorGUILayout.FloatField("링크 비용 환산(m)",
                    TraversalBallistics.CostDistance(total, SimConfig.EnemyMoveSpeed));
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "하강은 항상 전 지상몹 허용. 상승 권한만 종류로 정해집니다.\n" +
                "clearance는 자동(길이 비례)이 기본이며, Scene 뷰에서 궤적 꼭대기를 끌면 수동으로 바뀝니다.",
                MessageType.None);

            if (!link.clearanceAuto && GUILayout.Button("clearance 자동으로 되돌리기"))
            {
                Undo.RecordObject(link, "clearance 자동 복귀");
                link.clearanceAuto = true;
                EditorUtility.SetDirty(link);
            }
            if ((link.pauseTicksOverride >= 0 || link.recoverTicksOverride >= 0) &&
                GUILayout.Button("주저·멈칫 자동으로 되돌리기"))
            {
                Undo.RecordObject(link, "주저·멈칫 자동 복귀");
                link.pauseTicksOverride = -1;
                link.recoverTicksOverride = -1;
                EditorUtility.SetDirty(link);
            }
        }

        void OnSceneGUI()
        {
            var link = (TraversalLink)target;

            // ── B 끝점 핸들 ──
            EditorGUI.BeginChangeCheck();
            Vector3 b = Handles.PositionHandle(link.PointB, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(link, "링크 끝점 이동");
                link.endOffset = link.transform.InverseTransformPoint(b);
                EditorUtility.SetDirty(link);
            }

            // ── 정점(apex) 핸들 — 끌면 clearance 수동 전환 ──
            var arc = link.DescendArc;
            if (arc.IsValid)
            {
                int apexTick = Mathf.RoundToInt(arc.launchVy / arc.gravity / SimConfig.TickDelta);
                Vector3 apex = arc.At(Mathf.Clamp(apexTick, 0, arc.flightTicks));
                EditorGUI.BeginChangeCheck();
                float size = HandleUtility.GetHandleSize(apex) * 0.12f;
                Handles.color = new Color(1f, 0.9f, 0.3f, 1f);
                Vector3 moved = Handles.Slider(apex, Vector3.up, size, Handles.SphereHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(link, "clearance 조정");
                    float baseY = Mathf.Max(link.PointA.y, link.PointB.y);
                    link.clearanceAuto = false;
                    link.clearance = Mathf.Clamp(moved.y - baseY,
                        SimConfig.TraversalMinClearance, SimConfig.TraversalMaxClearance);
                    EditorUtility.SetDirty(link);
                }
            }

            // ── 라벨 ──
            Handles.color = Color.white;
            Vector3 mid = (link.PointA + link.PointB) * 0.5f;
            Handles.Label(mid + Vector3.up * 0.4f,
                $"{KindLabel(link.kind)}\n길이 {link.Length:0.0}m · 주저 {link.PauseTicks} · 멈칫 {link.RecoverTicks}");
        }

        static string KindLabel(TraversalLinkKind k)
        {
            switch (k)
            {
                case TraversalLinkKind.AscendRestricted: return "② 상승제한 (상승=Traversal만)";
                case TraversalLinkKind.DescendOnly:      return "③ 하강전용";
                default:                                 return "① 일반";
            }
        }
    }
}
