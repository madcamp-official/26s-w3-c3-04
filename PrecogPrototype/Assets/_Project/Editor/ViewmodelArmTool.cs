using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Game.EditorTools
{
    /// <summary>
    /// 1인칭 뷰모델용 — 스킨드 캐릭터에서 "팔만" 남기고 나머지 메시를 잘라낸다.
    ///
    /// 원리: 스킨드 메시는 각 정점이 어떤 뼈에 얼마나 묶였는지(본 가중치) 데이터를 갖고 있다.
    ///   팔 계열 뼈(Shoulder/Arm/ForeArm/Hand/손가락)에 묶인 삼각형만 남기면 어깨에서 깔끔히 잘린다.
    ///   → 버텍스를 손으로 고를 필요가 없다.
    ///
    /// 안전장치:
    ///   - 정점·뼈·바인드포즈는 그대로 두고 <b>삼각형만</b> 걸러낸다 → 스켈레톤 온전, 리그 설정 유지
    ///   - 원본 FBX는 안 건드리고 <b>새 메시 에셋</b>으로 저장 → 언제든 되돌리기 가능
    ///
    /// 사용: 씬에 캐릭터를 놓고 SkinnedMeshRenderer를 선택 → Tools/뷰모델/팔만 남기기
    /// </summary>
    public class ViewmodelArmTool : EditorWindow
    {
        public enum Side { 양팔, 오른팔만, 왼팔만 }

        SkinnedMeshRenderer target;
        Side side = Side.양팔;
        bool includeShoulder = true;      // 어깨 포함(팔꿈치에서 자르면 스윙 때 단면이 보임 → 기본 켬)
        float weightThreshold = 0.5f;     // 정점이 팔 뼈에 이만큼 이상 묶여야 "팔"로 인정
        bool strictTriangles = true;      // 삼각형의 세 정점이 모두 팔이어야 유지(경계가 깔끔)

        Mesh originalMesh;                // 되돌리기용

        [MenuItem("Tools/뷰모델/팔만 남기기")]
        static void Open() => GetWindow<ViewmodelArmTool>("팔만 남기기").minSize = new Vector2(360f, 260f);

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "스킨드 캐릭터에서 팔 계열 뼈에 묶인 삼각형만 남깁니다.\n" +
                "정점·뼈·바인드포즈는 건드리지 않고 삼각형만 걸러내므로 스켈레톤은 온전합니다.\n" +
                "원본은 보존되고 새 메시 에셋이 만들어집니다.", MessageType.Info);

            EditorGUILayout.Space();

            // 선택에서 자동 채우기
            if (target == null && Selection.activeGameObject != null)
                target = Selection.activeGameObject.GetComponentInChildren<SkinnedMeshRenderer>();

            target = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "대상 (SkinnedMeshRenderer)", target, typeof(SkinnedMeshRenderer), true);

            side            = (Side)EditorGUILayout.EnumPopup("남길 쪽", side);
            includeShoulder = EditorGUILayout.Toggle(new GUIContent("어깨 포함", "끄면 위팔부터. 스윙 때 단면이 보일 수 있어 켜는 걸 권장"), includeShoulder);
            weightThreshold = EditorGUILayout.Slider(new GUIContent("가중치 임계값", "낮출수록 더 많이 남음"), weightThreshold, 0.05f, 0.95f);
            strictTriangles = EditorGUILayout.Toggle(new GUIContent("경계 엄격", "세 정점이 모두 팔일 때만 유지(경계가 깔끔)"), strictTriangles);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(target == null || target.sharedMesh == null))
                if (GUILayout.Button("팔만 남기기 실행", GUILayout.Height(30f)))
                    Cut();

            using (new EditorGUI.DisabledScope(originalMesh == null || target == null))
                if (GUILayout.Button("원본 메시로 되돌리기"))
                {
                    Undo.RecordObject(target, "원본 메시 복구");
                    target.sharedMesh = originalMesh;
                    Debug.Log("[팔만 남기기] 원본 복구 완료");
                }

            if (target != null && target.sharedMesh != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"본 {target.bones.Length}개 · 정점 {target.sharedMesh.vertexCount} · 서브메시 {target.sharedMesh.subMeshCount}");
            }
        }

        /// <summary>이 뼈가 "팔 계열"인가. 이름 기반(Mixamo 규격: RightForeArm, LeftHandIndex1 …).</summary>
        bool IsArmBone(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return false;
            string n = rawName.ToLowerInvariant();

            // 좌우 필터
            if (side == Side.오른팔만 && !n.Contains("right")) return false;
            if (side == Side.왼팔만  && !n.Contains("left"))  return false;

            if (n.Contains("shoulder")) return includeShoulder;

            // "arm"이 ForeArm·Arm을 모두 잡는다. hand는 손가락(HandIndex1 등)까지 포함.
            return n.Contains("arm")   || n.Contains("hand")
                || n.Contains("thumb") || n.Contains("index")
                || n.Contains("middle")|| n.Contains("ring")
                || n.Contains("pinky");
        }

        void Cut()
        {
            Mesh src = target.sharedMesh;
            Transform[] bones = target.bones;
            if (bones == null || bones.Length == 0)
            { Debug.LogError("[팔만 남기기] 본이 없습니다. 스킨드 메시가 맞는지 확인하십시오."); return; }

            if (originalMesh == null) originalMesh = src;   // 첫 실행 때 원본 기억

            // 1) 어떤 본이 팔인지 표시
            var keepBone = new bool[bones.Length];
            int keptBoneCount = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                keepBone[i] = bones[i] != null && IsArmBone(bones[i].name);
                if (keepBone[i]) keptBoneCount++;
            }
            if (keptBoneCount == 0)
            { Debug.LogError("[팔만 남기기] 팔 계열 본을 못 찾았습니다. 본 이름 규칙이 다를 수 있습니다."); return; }

            // 2) 정점별로 "팔 가중치 합"을 구해 팔 정점인지 판정
            BoneWeight[] bw = src.boneWeights;
            if (bw == null || bw.Length != src.vertexCount)
            { Debug.LogError("[팔만 남기기] 본 가중치를 읽을 수 없습니다."); return; }

            var vertIsArm = new bool[src.vertexCount];
            for (int v = 0; v < src.vertexCount; v++)
            {
                BoneWeight w = bw[v];
                float sum = 0f;
                if (keepBone[w.boneIndex0]) sum += w.weight0;
                if (keepBone[w.boneIndex1]) sum += w.weight1;
                if (keepBone[w.boneIndex2]) sum += w.weight2;
                if (keepBone[w.boneIndex3]) sum += w.weight3;
                vertIsArm[v] = sum >= weightThreshold;
            }

            // 3) 삼각형만 걸러 새 메시 구성 (정점·본·바인드포즈는 원본 그대로 복제)
            Mesh dst = Object.Instantiate(src);
            dst.name = src.name + "_ArmsOnly";

            int kept = 0, total = 0;
            var buf = new List<int>();
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                int[] tris = src.GetTriangles(sm);
                buf.Clear();
                for (int t = 0; t < tris.Length; t += 3)
                {
                    total++;
                    bool a = vertIsArm[tris[t]], b = vertIsArm[tris[t + 1]], c = vertIsArm[tris[t + 2]];
                    bool ok = strictTriangles ? (a && b && c) : ((a ? 1 : 0) + (b ? 1 : 0) + (c ? 1 : 0)) >= 2;
                    if (!ok) continue;
                    buf.Add(tris[t]); buf.Add(tris[t + 1]); buf.Add(tris[t + 2]);
                    kept++;
                }
                dst.SetTriangles(buf, sm, false);
            }
            dst.RecalculateBounds();

            if (kept == 0)
            { Debug.LogError("[팔만 남기기] 남은 삼각형이 없습니다. 가중치 임계값을 낮춰 보십시오."); return; }

            // 4) 에셋으로 저장 + 적용
            string dir = "Assets/_Project/Prefabs/Viewmodel";
            EnsureFolder(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{dst.name}.asset");
            AssetDatabase.CreateAsset(dst, path);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(target, "팔만 남기기");
            target.sharedMesh = dst;
            EditorUtility.SetDirty(target);

            Debug.Log($"[팔만 남기기] 완료 — 삼각형 {kept}/{total} 유지 (팔 본 {keptBoneCount}개 기준)\n  저장: {path}");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf   = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
