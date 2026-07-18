using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 예측(예지) 연출 컨트롤러 — View 전용, 예측 봇과 독립.
    /// F: 정지 진입(시간 멈춤+흑백+3인칭) / 진입 중 F: 루트 순환 / 마우스: 궤도 회전 / 좌클릭: 확정 / Esc: 취소.
    /// 지금은 이동 전용 플레이스홀더 루트 + 고스트 미리보기. 확정 시 자동실행은 이후 Phase.
    /// </summary>
    public class PredictionController
    {
        public enum State { Idle, Preview }
        public State state = State.Idle;
        public bool Frozen => state != State.Idle;

        Camera cam;
        readonly FreezeFx fx = new FreezeFx();

        List<PredictedRoute> routes = new List<PredictedRoute>();
        int selected, prevSelected = -1;
        float ghostDist;
        float orbitYaw, orbitPitch;   // 미리보기 3인칭 궤도(마우스)

        LineRenderer domeLr;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        Transform ghost;
        Transform startMarker;   // 시작 위치(=나) 표시 캡슐
        readonly List<Transform> killMarks = new List<Transform>();

        public void Init(Camera camera)
        {
            cam = camera;
            fx.Init();
            fx.EnableOnCamera(cam);
            domeLr = MakeDome();
            ghost = MakeGhost();
            startMarker = MakeStartMarker();
            SetVisible(false);
        }

        /// <summary>Main.Update에서 매 프레임 호출.</summary>
        public void Tick(in SimWorld w)
        {
            fx.Update();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (state == State.Idle)
            {
                if (kb.fKey.wasPressedThisFrame) Enter(in w);
                return;
            }

            // ── Preview 중 ──
            if (kb.escapeKey.wasPressedThisFrame) { Exit(); return; }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) { Confirm(); return; }
            if (kb.fKey.wasPressedThisFrame && routes.Count > 0)
            {
                selected = (selected + 1) % routes.Count;
                LogSelected();
            }

            if (mouse != null)   // 마우스로 플레이어 중심 궤도 회전
            {
                Vector2 md = mouse.delta.ReadValue();
                orbitYaw += md.x * PredictionConfig.OrbitSens;
                orbitPitch = Mathf.Clamp(orbitPitch - md.y * PredictionConfig.OrbitSens,
                                         PredictionConfig.OrbitPitchMin, PredictionConfig.OrbitPitchMax);
            }

            Render(in w);
            PlaceCamera(in w);
        }

        void Enter(in SimWorld w)
        {
            routes = RealRoutePreview.Build(in w, Main.Instance.Services, PredictionConfig.RouteColors);
            selected = 0; prevSelected = -1; ghostDist = 0f;
            state = State.Preview;
            fx.SetActive(true);
            CombatAudio.Prediction();   // 예지 발동음 (combat 오디오 훅)
            ToggleSword(false);         // 3인칭이라 1인칭 칼 숨김
            SetVisible(true);
            BuildLines();
            orbitYaw = w.player.yaw; orbitPitch = PredictionConfig.OrbitPitchInit;   // 플레이어 뒤에서 시작
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;        // 마우스=궤도 회전
            Debug.Log($"[예측] 진입 — 사정권 {PredictionConfig.Range}, 루트 {routes.Count}개. (F 순환 · 좌클릭 확정 · Esc 취소)");
            LogSelected();
        }

        void Exit()
        {
            state = State.Idle;
            fx.SetActive(false);
            ToggleSword(true);         // 1인칭 칼 복구
            SetVisible(false);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        void Confirm()
        {
            if (routes.Count > 0)
            {
                var r = routes[selected];
                Debug.Log($"[예측] 루트 {selected} 확정(스텁) — 처치 {r.kills.Count}, {r.seconds:0.0}초. 자동실행은 이후 Phase.");
            }
            Exit();   // 스텁: 지금은 실행 없이 닫음
        }

        void LogSelected()
        {
            if (routes.Count == 0) return;
            var r = routes[selected];
            string[] names = { "최상위", "2순위", "3순위" };   // 실제 예측 점수 순위(더 이상 이동 휴리스틱 아님)
            string nm = selected < names.Length ? names[selected] : selected.ToString();
            Debug.Log($"[예측] 선택 루트 {selected}({nm}) — 처치 {r.kills.Count}, {r.seconds:0.0}초");
        }

        // ── 렌더 ──
        void Render(in SimWorld w)
        {
            UpdateDome(w.player.pos);
            for (int i = 0; i < lines.Count && i < routes.Count; i++)
                StyleLine(lines[i], routes[i].color, i == selected);

            if (selected != prevSelected) { ghostDist = 0f; prevSelected = selected; }

            if (startMarker != null)   // 정지된 플레이어 위치 = 루트 시작점
            {
                startMarker.position = w.player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
                startMarker.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
            }

            var r = routes.Count > 0 ? routes[selected] : null;
            AnimateGhost(r);
            UpdateKillMarks(r);
        }

        void PlaceCamera(in SimWorld w)
        {
            if (cam == null) return;
            Vector3 pivot = w.player.pos + Vector3.up * PredictionConfig.CamLookY;
            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            cam.transform.position = pivot - (rot * Vector3.forward) * PredictionConfig.CamDist;
            cam.transform.rotation = rot;
        }

        // ── 비주얼 요소 ──
        LineRenderer MakeDome()
        {
            var go = new GameObject("PredictDome");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.loop = true;
            lr.widthMultiplier = PredictionConfig.DomeWidth;
            lr.positionCount = 64;
            lr.material = LineMat();
            lr.startColor = lr.endColor = PredictionConfig.DomeColor;
            lr.numCapVertices = 2;
            return lr;
        }

        void UpdateDome(Vector3 center)
        {
            int seg = domeLr.positionCount;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float rr = PredictionConfig.Range;
                domeLr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * rr, 0.12f, Mathf.Sin(a) * rr));
            }
        }

        void BuildLines()
        {
            while (lines.Count < routes.Count)
            {
                var go = new GameObject($"Route_{lines.Count}");
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.numCapVertices = 2; lr.numCornerVertices = 2;
                lr.material = LineMat();
                lines.Add(lr);
            }
            for (int i = 0; i < lines.Count; i++)
            {
                bool used = i < routes.Count;
                lines[i].gameObject.SetActive(used);
                if (!used) continue;
                var r = routes[i];
                lines[i].positionCount = r.path.Count;
                for (int k = 0; k < r.path.Count; k++)
                    lines[i].SetPosition(k, r.path[k] + Vector3.up * 0.15f);
                StyleLine(lines[i], r.color, i == selected);
            }
        }

        static void StyleLine(LineRenderer lr, Color c, bool sel)
        {
            lr.widthMultiplier = sel ? PredictionConfig.RouteWidthSel : PredictionConfig.RouteWidthDim;
            Color col = sel ? c : c * PredictionConfig.RouteDimMul;
            col.a = sel ? PredictionConfig.RouteAlphaSel : PredictionConfig.RouteAlphaDim;   // 반투명 경로선
            lr.startColor = lr.endColor = col;
        }

        Transform MakeGhost()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "PredictGhost";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.PlayerRadius * 2f,
                                                  SimConfig.PlayerHeight * 0.5f,
                                                  SimConfig.PlayerRadius * 2f);
            go.GetComponent<Renderer>().material = GhostMat();
            return go.transform;
        }

        Transform MakeStartMarker()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "PredictStart";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.PlayerRadius * 2f,
                                                  SimConfig.PlayerHeight * 0.5f,
                                                  SimConfig.PlayerRadius * 2f);
            go.GetComponent<Renderer>().material = SolidMat(PredictionConfig.StartMarkerColor);
            return go.transform;
        }

        void AnimateGhost(PredictedRoute r)
        {
            if (r == null || r.path.Count < 2) { ghost.gameObject.SetActive(false); return; }
            ghost.gameObject.SetActive(true);

            float total = PathLength(r.path);
            ghostDist += Time.deltaTime * SimConfig.PlayerMoveSpeed;
            if (ghostDist > total + PredictionConfig.GhostLoopPause) ghostDist = 0f;   // 끝에서 잠깐 멈췄다 반복

            Vector3 pos = PointAtDist(r.path, Mathf.Min(ghostDist, total), out float yaw);
            ghost.position = pos + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
            ghost.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void UpdateKillMarks(PredictedRoute r)
        {
            int need = r != null ? r.kills.Count : 0;
            while (killMarks.Count < need)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "KillMark";
                Object.Destroy(s.GetComponent<Collider>());
                s.transform.localScale = Vector3.one * 0.55f;
                s.GetComponent<Renderer>().material = GhostMat();
                killMarks.Add(s.transform);
            }
            for (int i = 0; i < killMarks.Count; i++)
            {
                bool used = i < need && state == State.Preview;
                killMarks[i].gameObject.SetActive(used);
                if (used) killMarks[i].position = r.kills[i] + Vector3.up * PredictionConfig.KillMarkY;
            }
        }

        void SetVisible(bool on)
        {
            if (domeLr != null) domeLr.gameObject.SetActive(on);
            for (int i = 0; i < lines.Count; i++) lines[i].gameObject.SetActive(on && i < routes.Count);
            if (ghost != null) ghost.gameObject.SetActive(on);
            if (startMarker != null) startMarker.gameObject.SetActive(on);
            for (int i = 0; i < killMarks.Count; i++) killMarks[i].gameObject.SetActive(on);
        }

        /// <summary>1인칭 칼(SwordView가 카메라 자식으로 만든 "SwordPivot") 표시 토글. combat 파일 미편집(이름 결합).</summary>
        void ToggleSword(bool show)
        {
            if (cam == null) return;
            var sp = cam.transform.Find("SwordPivot");
            if (sp != null) sp.gameObject.SetActive(show);
        }

        // ── 경로 유틸 ──
        static float PathLength(List<Vector3> p)
        {
            float len = 0f;
            for (int i = 1; i < p.Count; i++) len += Vector3.Distance(p[i - 1], p[i]);
            return len;
        }

        static Vector3 PointAtDist(List<Vector3> p, float dist, out float yaw)
        {
            yaw = 0f;
            if (p.Count == 0) return Vector3.zero;
            if (p.Count == 1) return p[0];
            float acc = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                float seg = Vector3.Distance(p[i - 1], p[i]);
                if (acc + seg >= dist)
                {
                    float t = seg > 1e-4f ? (dist - acc) / seg : 0f;
                    Vector3 dir = p[i] - p[i - 1];
                    if (dir.sqrMagnitude > 1e-6f) yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    return Vector3.Lerp(p[i - 1], p[i], t);
                }
                acc += seg;
            }
            return p[p.Count - 1];
        }

        // ── 머티리얼 ──
        static Material LineMat()
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh);
            // Unlit로 폴백된 경우 기본이 Opaque라 반투명 경로선의 알파가 무시된다 — 명시적으로 켠다.
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.renderQueue = 3000; }
            return m;
        }

        static Material GhostMat()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh);
            Color c = PredictionConfig.GhostColor;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.renderQueue = 3000; }
            return m;
        }

        static Material SolidMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
