using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    /// <summary>
    /// 예측(예지) 연출 컨트롤러 — View 전용, 예측 봇과 독립.
    /// F: 정지 진입(시간 멈춤+흑백+3인칭) / 진입 중 F: 루트 순환 / 마우스: 궤도 회전 / 좌클릭: 확정 / Esc: 취소.
    /// 확정하면 실제 플레이어가 기록된 입력(PredictedRoute.controls)으로 자동 실행되고
    /// (Main.FixedUpdate가 TryConsumeFollowingInput을 통해 구동), 카메라는 1인칭으로 그
    /// 실제 위치·시선을 따라간다(Following). 경로에는 계약 3.1.1절대로 0.5초 간격 정지
    /// 잔상을 남긴다. 리듬 판정(Perfect/Good/Miss)은 아직 없음 — 그건 이후 Phase.
    /// </summary>
    public class PredictionController
    {
        public enum State { Idle, Preview, Following }
        public State state = State.Idle;
        public bool Frozen => state != State.Idle;

        Camera cam;
        readonly FreezeFx fx = new FreezeFx();

        List<PredictedRoute> routes = new List<PredictedRoute>();
        int selected;
        float orbitYaw, orbitPitch;   // 미리보기 3인칭 궤도(마우스)

        InputCmd[] followingControls;
        int followingIndex;

        LineRenderer domeLr;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        readonly List<Transform> ghostMarks = new List<Transform>();   // 0.5초 간격 정지 잔상(계약 3.1.1절)
        Transform startMarker;   // 시작 위치(=나) 표시 캡슐
        readonly List<Transform> killMarks = new List<Transform>();

        public void Init(Camera camera)
        {
            cam = camera;
            fx.Init();
            fx.EnableOnCamera(cam);
            domeLr = MakeDome();
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

            if (state == State.Following)
            {
                if (kb.escapeKey.wasPressedThisFrame || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                { Exit(); return; }   // 건너뛰기

                // 실제 이동·전투는 Main.FixedUpdate가 TryConsumeFollowingInput으로 구동한다.
                // 여기서는 그 결과(실제로 움직인 w)를 카메라·잔상 표시에만 반영한다.
                PredictedRoute r = routes.Count > 0 ? routes[selected] : null;
                UpdateGhostMarks(r);
                UpdateKillMarks(r);
                PlaceCameraFirstPerson(in w);
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
            selected = 0;
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
            if (routes.Count == 0) { Exit(); return; }
            var r = routes[selected];
            if (r.controls == null || r.controls.Length == 0)
            {
                Debug.LogWarning("[예측] 이 루트엔 재생할 입력이 없음(스텁 데이터?) — 자동실행 없이 닫음.");
                Exit();
                return;
            }
            state = State.Following;
            followingControls = r.controls;
            followingIndex = 0;
            ToggleSword(true);      // 1인칭 복귀 — 칼도 다시 보이게
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            Debug.Log($"[예측] 루트 {selected} 확정 — 자동 실행 ({r.seconds:0.0}초). Esc/좌클릭으로 건너뛰기.");
        }

        /// <summary>
        /// Main.FixedUpdate 전용. Following 중이면 이번 틱에 실제로 넣을 기록된 입력을 반환하고
        /// true를 준다. 재생이 끝났으면(또는 Following이 아니면) false를 주고 — 재생이 끝나서
        /// false가 된 경우엔 자동으로 Idle로 돌아간다(Exit 호출).
        /// </summary>
        public bool TryConsumeFollowingInput(out InputCmd cmd)
        {
            if (state != State.Following || followingControls == null || followingIndex >= followingControls.Length)
            {
                cmd = default;
                if (state == State.Following) Exit();   // 재생 끝 — 정상 종료
                return false;
            }
            cmd = followingControls[followingIndex++];
            return true;
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

            if (startMarker != null)   // 정지된 플레이어 위치 = 루트 시작점
            {
                startMarker.position = w.player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
                startMarker.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
            }

            var r = routes.Count > 0 ? routes[selected] : null;
            UpdateGhostMarks(r);
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

        /// <summary>Following 단계: 실제로 자동 실행 중인 플레이어의 위치·시선을 1인칭으로 따라간다.</summary>
        void PlaceCameraFirstPerson(in SimWorld w)
        {
            if (cam == null) return;
            cam.transform.position = w.player.pos + Vector3.up * PredictionConfig.CamLookY;
            cam.transform.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
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

        /// <summary>계약 3.1.1절: 0.5초(30틱) 간격 정지 잔상. route.ghostFrames(RealRoutePreview가
        /// 샘플링)에서 위치·yaw를 그대로 가져와 풀링된 캡슐로 배치한다 — 움직이는 고스트 대신
        /// 경로 위에 고정된 잔상들로 보여준다.</summary>
        void UpdateGhostMarks(PredictedRoute r)
        {
            int need = r != null ? r.ghostFrames.Count : 0;
            while (ghostMarks.Count < need) ghostMarks.Add(MakeGhostMark());

            for (int i = 0; i < ghostMarks.Count; i++)
            {
                bool used = i < need && state != State.Idle;
                ghostMarks[i].gameObject.SetActive(used);
                if (!used) continue;
                PredictedFrame f = r.ghostFrames[i];
                ghostMarks[i].position = f.playerPosition + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
                ghostMarks[i].rotation = Quaternion.Euler(0f, f.playerYaw, 0f);
            }
        }

        Transform MakeGhostMark()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "PredictGhostMark";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.PlayerRadius * 2f,
                                                  SimConfig.PlayerHeight * 0.5f,
                                                  SimConfig.PlayerRadius * 2f);
            go.GetComponent<Renderer>().material = GhostMat();
            return go.transform;
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
                bool used = i < need && state != State.Idle;
                killMarks[i].gameObject.SetActive(used);
                if (used) killMarks[i].position = r.kills[i] + Vector3.up * PredictionConfig.KillMarkY;
            }
        }

        void SetVisible(bool on)
        {
            if (domeLr != null) domeLr.gameObject.SetActive(on);
            for (int i = 0; i < lines.Count; i++) lines[i].gameObject.SetActive(on && i < routes.Count);
            for (int i = 0; i < ghostMarks.Count; i++) ghostMarks[i].gameObject.SetActive(on);
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
