using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 보스 처치 엔딩 감독. 순수 View — sim에서 보스(Orb) 사망 전이를 감지해 아래 시퀀스를 튼다.
    ///
    ///   ① <b>슬로모</b> — 막타 순간(평타·찌르기는 모션 시작 직후 즉발 판정이라 보스 사망 = 모션 초반).
    ///      배속을 낮추고, 사라진 오브 자리에 발광 잔해 프롭을 세운다(뷰가 죽은 적을 즉시 숨기므로).
    ///   ② <b>암전</b> — 평타(attackPhase)·찌르기(lungePhase) 모션이 끝나는 순간 화면을 즉시 검게.
    ///      Cutscene.Active로 입력·sim 정지, 배속 원복. 1초 유지.
    ///   ③ <b>구도 전환</b> — 암전이 '팍' 풀리며 BossDeathCam 마커 포즈·FOV의 고우선순위 vcam으로 컷.
    ///   ④ <b>맵 폭발 = 끝</b> — 아레나 콜라이더 전부 비활성(충돌판정 제거), 렌더러 조각 전부를
    ///      떼어내 <b>랜덤 방향 × 랜덤 세기</b>로 날린다(Rigidbody 없이 직접 적분 — convex·물리비용 무관).
    ///      발광 플래시 + 셰이크를 얹고, 수 초 뒤 서서히 암전한 채 종료(크레딧 없음).
    ///
    /// 배속 소유권: PredictionController가 매 프레임 Time.timeScale을 쓰므로, 슬로모는
    /// PredictionController.CutsceneTimeScaleOverride 훅으로 잡는다(직접 쓰면 즉시 1로 되돌려짐).
    /// </summary>
    [DisallowMultipleComponent]
    public class BossDeathDirector : MonoBehaviour
    {
        [Header("배선")]
        [Tooltip("폭발시킬 아레나 콘텐츠 루트(Arena_4_Content). 비우면 이름으로 찾는다.")]
        public Transform arenaRoot;
        [Tooltip("엔딩 시 정지시킬 웨이브 러너(A4 _Waves). 비우면 arenaRoot에서 찾는다.")]
        public WaveRunner runner;

        [Header("① 슬로모")]
        [Tooltip("막타 순간 배속.")]
        public float slowMoScale = 0.15f;
        [Tooltip("모션 끝 감지 실패 대비 슬로모 최대 시간(실시간 초).")]
        public float slowMoMaxSeconds = 3f;

        [Header("② 암전")]
        public float blackoutSeconds = 1f;

        [Header("④ 폭발 — 조각")]
        [Tooltip("조각 초기 속도 최소/최대(m/s). 조각마다 이 사이 랜덤.")]
        public float pieceSpeedMin = 15f;
        public float pieceSpeedMax = 60f;
        [Tooltip("조각에 걸 중력(m/s²). 0 = 직선 비행.")]
        public float pieceGravity = 9.8f;
        [Tooltip("조각 회전 속도 상한(도/초).")]
        public float pieceSpinMax = 540f;
        [Tooltip("0 = 순수 랜덤 방향(요청 기본). 올리면 위쪽으로 치우침.")]
        [Range(0f, 1f)] public float upwardBias = 0f;
        [Tooltip("0 = 순수 랜덤 방향. 올리면 폭심(아레나 중심)에서 바깥으로 치우침.")]
        [Range(0f, 1f)] public float outwardBias = 0f;

        [Header("④ 폭발 — 연출·마무리")]
        [Tooltip("발광 플래시 개수(폭발 초반에 시차를 두고 터짐).")]
        public int flashCount = 14;
        [Tooltip("셰이크 세기(m)·시간(초).")]
        public float shakeAmp = 0.5f;
        public float shakeSeconds = 1.5f;
        [Tooltip("폭발 감상 시간(초) — 이후 서서히 암전.")]
        public float explosionSeconds = 4f;
        public float endFadeSeconds = 2.5f;

        enum Phase { Idle, SlowMo, Blackout, Explosion, End }
        Phase phase = Phase.Idle;
        float phaseStartReal;      // 실시간 기준(슬로모·암전 동안 배속 무관)
        bool  bossSeenAlive;
        bool  fired;               // 1회성
        Vector3 bossDeathPos;
        float bossVisualSize = 8f;

        Transform dyingOrb;        // 슬로모 동안 오브 잔해
        Material  dyingOrbMat;
        Transform vcamGo;          // 죽음 카메라(생성 후 파괴 안 함 — 엔딩 화면 유지)
        Vector3   vcamBasePos;

        Transform scrapRoot;
        struct Piece { public Transform t; public Vector3 vel; public Vector3 axis; public float spin; }
        readonly List<Piece> pieces = new List<Piece>();
        struct Flash { public Transform t; public float start; public float life; public Material mat; }
        readonly List<Flash> flashes = new List<Flash>();
        System.Random rng;

        float blackAlpha;          // OnGUI 오버레이(0~1)

        void Awake()
        {
            if (arenaRoot == null)
            {
                var go = GameObject.Find("Arena_4_Content");
                if (go != null) arenaRoot = go.transform;
            }
            if (runner == null && arenaRoot != null)
                runner = arenaRoot.GetComponentInChildren<WaveRunner>(true);
            rng = new System.Random(20260723);
        }

        void OnDestroy()
        {
            // 안전 원복 — 씬 전환·정지 시 배속/잠금이 남지 않게.
            PredictionController.CutsceneTimeScaleOverride = -1f;
            if (dyingOrbMat != null) Destroy(dyingOrbMat);
        }

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            if (phase == Phase.Idle)
            {
                WatchBoss(in w);
                return;
            }

            float t = Time.unscaledTime - phaseStartReal;
            switch (phase)
            {
                case Phase.SlowMo:
                {
                    // 잔해 오브 깜빡임(죽어가는 코어).
                    if (dyingOrbMat != null)
                    {
                        float flicker = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 24f));
                        dyingOrbMat.SetColor("_BaseColor", new Color(6f * flicker, 0.5f * flicker, 0.18f * flicker));
                    }
                    bool motionDone = w.player.combat.attackPhase == CombatConfig.PhNone
                                   && w.player.combat.lungePhase == CombatConfig.LgNone;
                    if (motionDone || t >= slowMoMaxSeconds) BeginBlackout();
                    break;
                }

                case Phase.Blackout:
                    if (t >= blackoutSeconds) BeginExplosion();
                    break;

                case Phase.Explosion:
                {
                    StepPieces(Time.unscaledDeltaTime);
                    StepFlashes();
                    // 셰이크 — 죽음 카메라를 직접 흔든다(내가 만든 vcam이라 안전).
                    if (vcamGo != null)
                    {
                        float k = Mathf.Clamp01(1f - t / Mathf.Max(0.01f, shakeSeconds));
                        vcamGo.position = vcamBasePos + (k > 0f
                            ? new Vector3(NextSym(), NextSym(), NextSym()) * (shakeAmp * k)
                            : Vector3.zero);
                    }
                    if (t >= explosionSeconds) { phase = Phase.End; phaseStartReal = Time.unscaledTime; }
                    break;
                }

                case Phase.End:
                    StepPieces(Time.unscaledDeltaTime);   // 페이드 동안에도 조각은 계속 날아감
                    blackAlpha = Mathf.Clamp01((Time.unscaledTime - phaseStartReal) / Mathf.Max(0.01f, endFadeSeconds));
                    break;
            }
        }

        /// <summary>보스(Orb) 사망 전이 감지 — 살아 있는 걸 본 뒤 죽으면 1회 발동.</summary>
        void WatchBoss(in SimWorld w)
        {
            if (fired) return;
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (e.ai.mobility != MobilityType.Orb) continue;
                if (e.alive) { bossSeenAlive = true; return; }
                if (bossSeenAlive)
                {
                    bossDeathPos = e.pos;
                    bossVisualSize = e.radius * 2f;   // EntityViews와 동일 — 구 지름 = sim 히트박스
                    BeginSlowMo();
                    return;
                }
            }
        }

        /// <summary>
        /// 콘솔용: 보스 유무·막타와 무관하게 엔딩 시퀀스를 즉시 시작(테스트용).
        /// 살아 있거나 방금 죽은 보스가 있으면 그 위치를, 없으면 플레이어 앞을 사망 지점으로 잡는다.
        /// <paramref name="skipSlowMo"/>=true면 슬로모를 건너뛰고 곧장 암전→폭발로 간다.
        /// </summary>
        public bool TriggerFromConsole(bool skipSlowMo)
        {
            if (fired) return false;
            var main = Main.Instance;
            if (main == null) return false;

            ref readonly SimWorld w = ref main.World;
            bool found = false;
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (e.ai.mobility != MobilityType.Orb) continue;
                bossDeathPos = e.pos;
                bossVisualSize = e.radius * 2f;
                found = true;
                break;
            }
            if (!found) { bossDeathPos = w.player.pos + Vector3.up * 2f; bossVisualSize = 8f; }

            if (skipSlowMo) { fired = true; BeginBlackout(); }
            else            { bossSeenAlive = true; BeginSlowMo(); }
            return true;
        }

        void BeginSlowMo()
        {
            fired = true;
            phase = Phase.SlowMo;
            phaseStartReal = Time.unscaledTime;
            PredictionController.CutsceneTimeScaleOverride = Mathf.Clamp(slowMoScale, 0.02f, 1f);

            // 죽은 오브 잔해 — 뷰가 죽은 적을 즉시 숨기므로 같은 자리에 발광 구를 세운다.
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "~BossDyingOrb";
            Destroy(orb.GetComponent<Collider>());
            dyingOrbMat = MakeGlow(new Color(6f, 0.5f, 0.18f));
            var r = orb.GetComponent<Renderer>();
            r.sharedMaterial = dyingOrbMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orb.transform.position = bossDeathPos;
            orb.transform.localScale = Vector3.one * bossVisualSize;
            dyingOrb = orb.transform;
        }

        void BeginBlackout()
        {
            phase = Phase.Blackout;
            phaseStartReal = Time.unscaledTime;
            blackAlpha = 1f;                                          // 즉시 암전
            PredictionController.CutsceneTimeScaleOverride = -1f;     // 배속 원복
            Cutscene.Active = true;                                   // 입력·sim 정지(이후 계속 유지 = 엔딩)
            if (runner != null) runner.Stop();
            if (dyingOrb != null) Destroy(dyingOrb.gameObject);
        }

        void BeginExplosion()
        {
            phase = Phase.Explosion;
            phaseStartReal = Time.unscaledTime;
            SetupDeathCam();
            blackAlpha = 0f;                                          // 암전 '팍' 해제 → 전경 구도

            if (arenaRoot == null) return;

            // 1) 충돌판정 완전 제거 — 조각은 아무것와도 안 부딪히고 날아간다.
            foreach (var c in arenaRoot.GetComponentsInChildren<Collider>(true))
                c.enabled = false;

            // 2) 조각 수집·분리 — 렌더러 트랜스폼을 각자 독립 조각으로 떼어낸다(활성만).
            scrapRoot = new GameObject("~ArenaScrap").transform;
            var seen = new HashSet<Transform>();
            Vector3 center = Vector3.zero; int n = 0;
            var rs = arenaRoot.GetComponentsInChildren<Renderer>(false);
            foreach (var r in rs) { if (seen.Add(r.transform)) { center += r.transform.position; n++; } }
            if (n > 0) center /= n;

            foreach (var t in seen)
            {
                t.SetParent(scrapRoot, true);
                // 방향 = 순수 랜덤(요청 기본) + 선택 편향(위쪽/폭심 바깥).
                Vector3 dir = RandomUnit();
                if (upwardBias > 0f) dir = Vector3.Slerp(dir, Vector3.up, upwardBias);
                if (outwardBias > 0f)
                {
                    Vector3 outw = t.position - center;
                    if (outw.sqrMagnitude > 1e-4f) dir = Vector3.Slerp(dir, outw.normalized, outwardBias);
                }
                pieces.Add(new Piece
                {
                    t = t,
                    vel = dir.normalized * Mathf.Lerp(pieceSpeedMin, pieceSpeedMax, (float)rng.NextDouble()),
                    axis = RandomUnit(),
                    spin = (float)rng.NextDouble() * pieceSpinMax,
                });
            }

            // 3) 발광 플래시 — 아레나 영역 곳곳에 시차를 두고.
            for (int i = 0; i < flashCount; i++)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                f.name = "~ExplosionFlash";
                Destroy(f.GetComponent<Collider>());
                var mat = MakeGlow(new Color(8f, 4.5f, 1.2f));
                var rr = f.GetComponent<Renderer>();
                rr.sharedMaterial = mat;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector3 off = new Vector3(NextSym() * 60f, NextSym() * 12f, NextSym() * 60f);
                f.transform.position = center + off;
                f.transform.localScale = Vector3.zero;
                flashes.Add(new Flash
                {
                    t = f.transform,
                    start = Time.unscaledTime + (float)rng.NextDouble() * 1.5f,   // 시차
                    life = 0.7f,
                    mat = mat,
                });
            }
            Debug.Log($"[BossDeathDirector] 폭발 — 조각 {pieces.Count}개 발사, 플래시 {flashes.Count}개.");
        }

        void SetupDeathCam()
        {
            var marker = BossDeathCam.Find();
            var main = Main.Instance;
            var go = new GameObject("~BossDeathVcam");
            var vcam = go.AddComponent<CinemachineCamera>();
            if (marker != null)
            {
                go.transform.SetPositionAndRotation(marker.transform.position, marker.transform.rotation);
                if (main != null && main.GameplayVcam != null) vcam.Lens = main.GameplayVcam.Lens;
                var lens = vcam.Lens; lens.FieldOfView = marker.fov; vcam.Lens = lens;
            }
            else
            {
                // 마커가 없으면 현재 카메라 자리에서 그대로(구도만 못 잡을 뿐 진행은 한다).
                Debug.LogWarning("[BossDeathDirector] BossDeathCam 마커가 없음 — 현재 시점에서 폭발을 보여줍니다.");
                if (main != null && main.Cam != null)
                    go.transform.SetPositionAndRotation(main.Cam.transform.position, main.Cam.transform.rotation);
            }
            vcam.Priority.Value = 200;   // 컷신(100)보다 높게 — 즉시 컷 전환
            vcamGo = go.transform;
            vcamBasePos = go.transform.position;
        }

        void StepPieces(float dt)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                if (p.t == null) continue;
                p.vel += Vector3.down * (pieceGravity * dt);
                p.t.position += p.vel * dt;
                p.t.Rotate(p.axis, p.spin * dt, Space.World);
                pieces[i] = p;
            }
        }

        void StepFlashes()
        {
            for (int i = 0; i < flashes.Count; i++)
            {
                Flash f = flashes[i];
                if (f.t == null) continue;
                float u = (Time.unscaledTime - f.start) / f.life;
                if (u < 0f) continue;
                if (u >= 1f) { Destroy(f.t.gameObject); continue; }
                f.t.localScale = Vector3.one * Mathf.Lerp(2f, 26f, 1f - (1f - u) * (1f - u));
                float dim = 1f - u;
                f.mat.SetColor("_BaseColor", new Color(8f * dim, 4.5f * dim, 1.2f * dim));
            }
        }

        // ── 유틸 ──

        float NextSym() => (float)(rng.NextDouble() * 2.0 - 1.0);

        Vector3 RandomUnit()
        {
            // 구면 균등에 가까운 랜덤 단위벡터(간단 리젝션 없이 정규화 — 연출용으로 충분).
            Vector3 v = new Vector3(NextSym(), NextSym(), NextSym());
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.up;
        }

        static Material MakeGlow(Color hdr)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh);
            m.SetColor("_BaseColor", hdr);
            m.SetColor("_Color", hdr);
            return m;
        }

        void OnGUI()
        {
            if (blackAlpha <= 0f) return;
            // 엔딩 오버레이 — UI 숨김(UiVisibility)과 무관하게 최상단에 그린다.
            GUI.depth = -1000;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, blackAlpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
