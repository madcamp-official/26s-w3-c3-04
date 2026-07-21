using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Sim 상태를 캡슐 GameObject로 비추는 거울. 1인칭이라 플레이어 몸은 렌더 끔.
    /// 콜라이더는 전부 제거 — 충돌 판정은 Sim이 CapsuleCast로 지형에만 한다.
    /// </summary>
    public class EntityViews
    {
        public Transform PlayerAnchor { get; private set; }
        readonly List<Transform> enemyViews = new List<Transform>();
        readonly List<ViewKind>  viewKinds     = new List<ViewKind>();
        readonly List<Renderer>  viewRenderers = new List<Renderer>();   // 틴트용, 캐시(Charge는 자식에 있음)
        readonly List<Animator>  viewAnimators = new List<Animator>();   // Charge만 채워짐, 그 외 null
        readonly List<float>     viewYaw       = new List<float>();      // Charge 몸통 회전 스무딩용(요철 방지)

        static readonly Color ChaseColor     = new Color(1f, 0.4f, 0.3f);   // 추격 (빨강)
        static readonly Color LeapColor      = new Color(1f, 0.9f, 0.2f);   // 절벽 도약 중 (노랑)
        static readonly Color HitColor       = new Color(0.5f, 0.05f, 0.05f); // 피격/스턴 (검붉은)
        static readonly Color WindupColor    = new Color(1f, 0.95f, 0.4f);    // 공격 선딜 텔레그래프 (밝은 노랑)
        static readonly Color AttackColor    = new Color(1f, 0.15f, 0.05f);   // 타격 순간 (강렬 빨강)

        // 몹 시각 종류. Ground/Traversal은 아직 실제 모델이 없어 캡슐 유지.
        enum ViewKind { Capsule, Flying, Charge }

        // FlyingEnemy/ChargeEnemy 프리팹: 래퍼 루트(스케일 1, yaw만 회전) + 자식이 임포트 시 축변환·스케일을 그대로 들고 1m 기준으로 맞춰져 있음.
        // Sync에서 wrapper.localScale = Vector3.one * e.height 로 개체별 크기(대형몹 3배 등)를 반영한다.
        static GameObject flyingPrefab, chargePrefab;
        static bool prefabsLoaded;

        // 돌진몹 원점은 모델 피벗이 발밑보다 살짝 위라 그냥 두면 발이 땅속에 파묻힘.
        // ★ Play 모드에서 Walk+Run 전체 사이클을 프레임별로 샘플링해 실측한 값(최저점 -0.182, 편차 거의 없음).
        //   이전의 0.24는 여유를 과하게 잡아 반대로 살짝 공중에 뜨는 원인이 됐다.
        const float ChargeFeetLift = 0.182f;

        // 돌진몹만 시각적으로 더 크게(가독성/위협감) — 히트박스(e.radius/e.height)는 그대로, 렌더 크기만 배율.
        // ★ 1.8배까지 키웠더니 일반 크기 몹이 실제 키 3m를 넘어 충돌 캡슐(지름 ~1.15m)보다 훨씬 넓어져
        //   벽 옆을 지날 때 팔/어깨가 뚫고 나가는 문제가 생겼다 — 히트박스는 그대로 두기로 하고 시각 배율을 낮춤.
        //   Dismemberment.ChargeVisualScaleMul과 반드시 같은 값으로 유지(시체 크기와 안 맞으면 어색해짐).
        const float ChargeVisualScaleMul = 1.4f;

        // Walk/Run 클립은 제자리 걸음(루트 모션 없음)이라 "이 클립이 가정하는 실제 보폭 속도"를 몰라
        // 임의로 가정한 값(m/s) — 실제 이동 속도와 비교해 재생 속도를 맞추는 데만 쓴다. 미끄러져 보이면 이 값을 낮추고,
        // 다리가 너무 빨리 움직이면 값을 올린다.
        const float WalkClipPace = 1.6f;
        const float RunClipPace  = 4.0f;

        // 걷는 동안 실제 속도 벡터를 그대로 몸통 방향에 쓰면 분리(separation) 스티어링의 매 틱 잔떨림이
        // 그대로 회전으로 튀어나와 "대각선으로 홱홱 도는" 느낌이 났다. 초당 최대 회전각을 제한해 부드럽게 돈다.
        const float BodyTurnDegPerSec = 420f;

        static void LoadPrefabsOnce()
        {
            if (prefabsLoaded) return;
            flyingPrefab = Resources.Load<GameObject>("Enemies/FlyingEnemy");
            chargePrefab = Resources.Load<GameObject>("Enemies/ChargeEnemy");
            prefabsLoaded = true;
        }

        public void Init()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerAnchor";
            Object.Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().enabled = false;  // 1인칭
            PlayerAnchor = body.transform;
        }

        public void Sync(in SimWorld w, in SimWorld prev, float alpha)
        {
            Vector3 pp = Vector3.Lerp(prev.player.pos, w.player.pos, alpha);
            PlayerAnchor.position = pp;
            PlayerAnchor.rotation = Quaternion.Euler(
                0f, Mathf.LerpAngle(prev.player.yaw, w.player.yaw, alpha), 0f);

            LoadPrefabsOnce();

            while (enemyViews.Count < w.enemyCount)
            {
                int idx = enemyViews.Count;
                MobilityType mobility = idx < w.enemyCount ? w.enemies[idx].ai.mobility : MobilityType.Ground;
                AddView($"Enemy_{idx}", KindFor(mobility), w.enemies[idx].yaw);
            }

            for (int i = 0; i < enemyViews.Count; i++)
            {
                // 처형 중(gloryStage>0)엔 몸 숨김 → Dismemberment 조각만 보이게
                bool active = i < w.enemyCount && w.enemies[i].alive && w.enemies[i].combat.gloryStage == 0;
                if (i < w.enemyCount)
                {
                    // 개체 풀 재사용으로 이동방식이 바뀐 슬롯 → 시각을 다시 만든다.
                    ViewKind wantKind = KindFor(w.enemies[i].ai.mobility);
                    if (viewKinds[i] != wantKind) ReplaceView(i, $"Enemy_{i}", wantKind, w.enemies[i].yaw);
                }
                enemyViews[i].gameObject.SetActive(active);
                if (!active) continue;
                ref readonly EnemySim e = ref w.enemies[i];
                Vector3 ep = Vector3.Lerp(prev.enemies[i].pos, e.pos, alpha);
                ViewKind kind = viewKinds[i];
                if (kind == ViewKind.Capsule)
                {
                    // 캡슐 원점은 중심이라 몸 절반 올림 (Sim pos는 발밑). 개별 크기 반영(대형몹 3배).
                    enemyViews[i].position = ep + Vector3.up * (e.height * 0.5f);
                    enemyViews[i].localScale = new Vector3(e.radius * 2f, e.height * 0.5f, e.radius * 2f);
                }
                else
                {
                    // 실물 모델은 프리팹을 1m 기준으로 미리 맞춰뒀으므로 wrapper 스케일 = e.height 하나로 충분.
                    // Flying은 원점이 모델 중심(=캡슐과 동일하게 절반 올림), Charge는 원점이 발 근처지만
                    // 살짝 위라 chargeFeetLift만큼 더 들어올려야 발이 바닥에 파묻히지 않는다.
                    float visualScale = e.height * (kind == ViewKind.Charge ? ChargeVisualScaleMul : 1f);
                    // 발 오프셋은 실제로 그려지는 크기(visualScale) 기준이어야 커진 만큼 같이 들어올려진다.
                    enemyViews[i].position = kind == ViewKind.Flying
                        ? ep + Vector3.up * (e.height * 0.5f)
                        : ep + Vector3.up * (ChargeFeetLift * visualScale);
                    enemyViews[i].localScale = new Vector3(visualScale, visualScale, visualScale);
                }
                bool isChargeRun = kind == ViewKind.Charge && e.ai.state == EnemyState.ChargeRun;
                float bodyYaw = e.yaw;
                if (kind == ViewKind.Charge && !isChargeRun)
                {
                    // e.yaw는 sim에서 "플레이어를 응시"(텔레그래프용)라 그대로 쓰면 대각선/옆으로 걷는 것처럼
                    // 보임 — 걷는 동안만은 실제 이동 방향(속도 벡터)을 봐야 자연스럽다. 돌진 중엔 committedDir과
                    // e.yaw가 같은 방향이라 그대로 써도 된다. e.yaw 자체(사거리/명중 판정용)는 건드리지 않는다.
                    Vector3 horizVel = new Vector3(e.vel.x, 0f, e.vel.z);
                    if (horizVel.sqrMagnitude > 0.01f) bodyYaw = Mathf.Atan2(horizVel.x, horizVel.z) * Mathf.Rad2Deg;
                }
                if (kind == ViewKind.Charge)
                {
                    // 분리 스티어링 잔떨림을 걸러내는 회전 속도 제한(시각 전용, e.yaw/전투 판정엔 영향 없음).
                    viewYaw[i] = Mathf.MoveTowardsAngle(viewYaw[i], bodyYaw, BodyTurnDegPerSec * Time.deltaTime);
                    bodyYaw = viewYaw[i];
                }
                enemyViews[i].rotation = Quaternion.Euler(0f, bodyYaw, 0f);

                if (kind == ViewKind.Charge)
                {
                    Animator anim = viewAnimators[i];
                    // ★ TraversalPhase.Airborne/DescentPhase.Leaping은 특정 도약 메커니즘 하나만 감지해서
                    // 턱을 넘거나 일반 낙하로 뜰 때는 안 걸려 Jump가 안 나가는 원인이었다. e.grounded는
                    // Charge 몹의 모든 이동 경로(추격/도약/돌진)에서 매 틱 실측되는 값이라 이걸로 통일한다.
                    anim.SetBool("IsAirborne", !e.grounded);
                    anim.SetBool("IsCharging", isChargeRun);

                    // 실제 이동 속도에 맞춰 재생 속도를 맞춰 "미끄러지는" 느낌을 없앤다.
                    // ★ 이전엔 분모로 SimConfig.EnemyMoveSpeed/AIConfig.ChargeSpeed(=현재 속도 그 자체)를 써서
                    //   비율이 항상 ~1로 나와 사실상 아무 효과가 없었다. 여기선 모션 클립이 "제자리 걸음"이라
                    //   실제 보폭 속도를 알 수 없으므로, 자연스러워 보이는 걷기/달리기 기준 속도를 따로 가정해
                    //   실제 이동 속도와의 비율만큼 재생 속도를 올린다.
                    float horizSpeed = new Vector3(e.vel.x, 0f, e.vel.z).magnitude;
                    float clipPace = isChargeRun ? RunClipPace : WalkClipPace;
                    anim.speed = Mathf.Clamp(horizSpeed / clipPace, 0.5f, 4f);
                }

                // 우선순위: 피격/스턴 > 공격 선딜(경고) > 타격 > 하강 단계 색
                // 실물 모델(Flying/Charge)은 원래 텍스처 색을 그대로 유지 — 캡슐만 상태별로 틴트.
                if (kind == ViewKind.Capsule)
                {
                    Color col;
                    if (e.combat.stunTicks > 0)                 col = HitColor;
                    else if (e.ai.state == EnemyState.Windup)   col = WindupColor;
                    else if (e.ai.state == EnemyState.Active)   col = AttackColor;
                    else                                        col = PhaseColor(e.descentPhase);
                    Renderer r = viewRenderers[i];
                    if (r != null) r.material.color = col;
                }
            }
        }

        static Color PhaseColor(DescentPhase p)
            => p == DescentPhase.Leaping ? LeapColor : ChaseColor;

        static ViewKind KindFor(MobilityType m) => m switch
        {
            MobilityType.Flying => ViewKind.Flying,
            MobilityType.Charge => ViewKind.Charge,
            _                   => ViewKind.Capsule,
        };

        void AddView(string name, ViewKind kind, float initialYaw)
        {
            enemyViews.Add(null);
            viewKinds.Add(ViewKind.Capsule);      // ReplaceView가 실제 값으로 덮어씀
            viewRenderers.Add(null);
            viewAnimators.Add(null);
            viewYaw.Add(initialYaw);
            ReplaceView(enemyViews.Count - 1, name, kind, initialYaw);
        }

        void ReplaceView(int i, string name, ViewKind kind, float initialYaw)
        {
            if (enemyViews[i] != null) Object.Destroy(enemyViews[i].gameObject);
            viewYaw[i] = initialYaw;   // 슬롯 재사용 시 이전 개체의 회전에서 이어 도는 것 방지

            // 프리팹이 없으면(Resources 미배치 등) 캡슐로 대체 — 크래시 대신 예전 모습으로 폴백.
            if (kind == ViewKind.Flying && flyingPrefab == null) kind = ViewKind.Capsule;
            if (kind == ViewKind.Charge && chargePrefab == null) kind = ViewKind.Capsule;

            Transform t;
            Renderer  r;
            Animator  a = null;
            switch (kind)
            {
                case ViewKind.Flying:
                    t = Object.Instantiate(flyingPrefab).transform;
                    r = t.GetComponentInChildren<Renderer>();
                    break;
                case ViewKind.Charge:
                    t = Object.Instantiate(chargePrefab).transform;
                    r = t.GetComponentInChildren<Renderer>();
                    a = t.GetComponentInChildren<Animator>();
                    break;
                default:
                    t = MakeCapsule().transform;
                    r = t.GetComponent<Renderer>();
                    break;
            }
            t.name = name;

            enemyViews[i]     = t;
            viewKinds[i]      = kind;
            viewRenderers[i]  = r;
            viewAnimators[i]  = a;
        }

        GameObject MakeCapsule()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.EnemyRadius * 2f, SimConfig.EnemyHeight * 0.5f,
                                                  SimConfig.EnemyRadius * 2f);
            go.GetComponent<Renderer>().material = Mat(ChaseColor);  // 개별 인스턴스
            return go;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
