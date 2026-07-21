using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 1인칭 칼 뷰모델. ★ combat 소유·독립 — Main/EntityViews 안 건드림. 읽기 전용(SimWorld).
    ///
    /// 구조(하이브리드): 스윙 = Animation 창에서 authoring한 클립,
    ///   재생 시점은 Sim phase가 몬다(틱 동기). 그 위에 절차 레이어(착지·숨·피격)를 additive.
    ///   (이펙트는 별도 — 현재 없음)
    ///
    /// 계층: KatanaViewmodel(root, 절차 오프셋) > PoseTarget(클립) > Katana(메시) > Tip/Root(궤적 앵커)
    /// </summary>
    public class SwordView : MonoBehaviour
    {
        const string ViewmodelPrefab = "KatanaViewmodel";

        public static SwordView Instance { get; private set; }   // 콘솔 프리뷰 진입점

        Transform vmRoot;
        Animator  anim;
        Camera    cam;

        // 클립 state 해시(state 이름 = 클립 이름)
        static readonly int HIdle   = Animator.StringToHash("Katana_Idle");
        static readonly int HSlash1 = Animator.StringToHash("Katana_Slash1");
        static readonly int HSlash2 = Animator.StringToHash("Katana_Slash2");
        static readonly int HThrust = Animator.StringToHash("Katana_Thrust");

        int   prevHash;
        float prevNt;

        // ── 콘솔 프리뷰(전투 없이 스윙 재생. Sim 안 건드림 → 결정론 무관) ──
        bool  previewOn, previewLoop;
        int   previewHash;
        float previewT;
        public float previewDuration = 0.5f;

        // 사운드 전이 감지
        byte prevAttack; bool prevDash; byte prevLunge;

        // ── 절차 레이어 ──
        bool  prevGrounded = true;
        float prevVelY;
        int   prevHp = int.MinValue;
        float breathePhase;
        Vector3 posOff, posVel, rotOff, rotVel;

        // ── 절차 레이어 튜닝(Play 중 Inspector에서 실시간 조절) ──
        [Header("착지 딥")]
        public float landKick = 0.06f;      // 하강속도 → 아래 임펄스
        public float landMinSpeed = 3f;     // 이 속도 미만 착지는 무시
        [Header("스프링 (클수록 빠르고 딱딱)")]
        public float posStiff = 180f, posDamp = 18f;
        public float rotStiff = 220f, rotDamp = 20f;
        [Header("피격 움찔")]
        public float hitPosKick = 0.05f, hitRotKick = 8f;
        [Header("숨고르기 (HP 낮을수록 크고 빨라짐)")]
        public float breatheAmpFull = 0.004f, breatheAmpLow = 0.018f;
        public float breatheSpdFull = 1.6f,   breatheSpdLow = 4.5f;
        [Tooltip("0~1이면 그 HP 비율로 강제(숨 연출 테스트용). 음수면 실제 HP 사용")]
        [Range(-1f, 1f)] public float breatheHpOverride = -1f;

        // ── 콘솔/외부에서 절차 효과를 즉시 발동 (전투 없이 테스트) ──
        /// <summary>착지 딥을 강제로 발동. impact = 가상의 하강 속도.</summary>
        public void KickLand(float impact) => posVel += Vector3.down * (Mathf.Abs(impact) * landKick);

        /// <summary>피격 움찔을 강제로 발동.</summary>
        public void KickHit()
        {
            posVel += Random.insideUnitSphere * hitPosKick;
            rotOff += Random.insideUnitSphere * hitRotKick;
        }

        /// <summary>절차 오프셋을 즉시 0으로(테스트 리셋).</summary>
        public void ResetProcedural()
        {
            posOff = posVel = rotOff = rotVel = Vector3.zero;
            breatheHpOverride = -1f;
        }

        void Awake() { Instance = this; }

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;

            if (vmRoot == null)
            {
                cam = main.Cam;
                if (cam == null) return;
                if (!BuildViewmodel()) return;
            }

            ref readonly PlayerSim p = ref main.World.player;
            DetectAndSound(in p);

            if (previewOn) DrivePreview();
            else           DriveFromSim(in p.combat);

            Procedural(in p);
        }

        // ── 뷰모델 준비 ──
        bool BuildViewmodel()
        {
            GameObject go = FindExistingViewmodel();
            if (go == null)
            {
                var prefab = Resources.Load<GameObject>(ViewmodelPrefab);
                if (prefab == null) { Debug.LogError("[SwordView] KatanaViewmodel 프리팹 없음"); enabled = false; return false; }
                go = Object.Instantiate(prefab);
                go.name = ViewmodelPrefab;
            }

            vmRoot = go.transform;
            if (vmRoot.parent != cam.transform) vmRoot.SetParent(cam.transform, false);
            vmRoot.localPosition = Vector3.zero;
            vmRoot.localRotation = Quaternion.identity;

            anim = go.GetComponent<Animator>();
            if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            return true;
        }

        GameObject FindExistingViewmodel()
        {
            Transform t = cam.transform.Find(ViewmodelPrefab);
            if (t != null) return t.gameObject;
            return GameObject.Find(ViewmodelPrefab);
        }

        // ── Sim 구동 ──
        void DriveFromSim(in PlayerCombatState c)
        {
            int hash; float nt;
            if (c.gloryPhase != CombatConfig.GlNone)        { hash = HSlash1; nt = 0.5f; }
            else if (c.lungePhase != CombatConfig.LgNone)   { hash = HThrust; nt = LungeNt(in c); }
            else if (c.attackPhase != CombatConfig.PhNone)  { hash = HSlash1; nt = AttackNt(in c); }   // TODO 콤보로 Slash1/2 교대
            else                                           { hash = HIdle;   nt = 0f; }
            Sample(hash, nt);
        }

        void DrivePreview()
        {
            previewT += Time.deltaTime / Mathf.Max(0.01f, previewDuration);
            if (previewT >= 1f)
            {
                if (previewLoop) previewT = 0f;
                else { previewOn = false; Sample(HIdle, 0f); return; }
            }
            Sample(previewHash, Mathf.Clamp01(previewT));
        }

        /// <summary>클립을 nt(0~1) 시점으로 수동 샘플 — 틱 동기 유지.</summary>
        void Sample(int hash, float nt)
        {
            if (anim == null) return;
            anim.Play(hash, 0, nt);
            anim.Update(0f);
            prevHash = hash; prevNt = nt;
        }

        static float AttackNt(in PlayerCombatState c)
        {
            int wu = CombatConfig.AttackWindupTicks, ac = CombatConfig.AttackActiveTicks, re = CombatConfig.AttackRecoveryTicks;
            float total = Mathf.Max(1, wu + ac + re);
            float e = c.attackPhase == CombatConfig.PhWindup ? c.attackPhaseTicks
                    : c.attackPhase == CombatConfig.PhActive ? wu + c.attackPhaseTicks
                    :                                          wu + ac + c.attackPhaseTicks;
            return Mathf.Clamp01(e / total);
        }

        static float LungeNt(in PlayerCombatState c)
        {
            int wu = CombatConfig.LungeWindupTicks, tr = Mathf.Max(1, c.lungeTravelTicks), re = CombatConfig.LungeRecoveryTicks;
            float total = Mathf.Max(1, wu + tr + re);
            float e = c.lungePhase == CombatConfig.LgWindup ? c.lungeTicks
                    : c.lungePhase == CombatConfig.LgTravel ? wu + c.lungeTicks
                    :                                         wu + tr + c.lungeTicks;
            return Mathf.Clamp01(e / total);
        }

        // ── 콘솔 API ──
        /// <summary>전투 없이 스윙 재생(궤적 튜닝용). which: 1=평타1, 2=평타2, t=찌르기.</summary>
        public bool PreviewSwing(string which, bool loop)
        {
            int h;
            switch (which)
            {
                case "1": h = HSlash1; break;
                case "2": h = HSlash2; break;
                case "t": case "thrust": h = HThrust; break;
                default: return false;
            }
            previewHash = h; previewT = 0f; previewLoop = loop; previewOn = true;
            return true;
        }

        public void StopPreview()
        {
            previewOn = false;
        }

        // ── 절차 레이어 ──
        void Procedural(in PlayerSim p)
        {
            float dt = Time.deltaTime;

            bool grounded = p.grounded;
            if (grounded && !prevGrounded)
            {
                float impact = Mathf.Max(0f, -prevVelY);
                if (impact > landMinSpeed) KickLand(impact);
            }
            prevGrounded = grounded; prevVelY = p.vel.y;

            if (prevHp != int.MinValue && p.combat.hp < prevHp) KickHit();
            prevHp = p.combat.hp;

            // 숨고르기: override(테스트)가 있으면 그 값, 없으면 실제 HP 비율
            float hpFrac = breatheHpOverride >= 0f
                ? breatheHpOverride
                : Mathf.Clamp01(p.combat.hp / (float)Mathf.Max(1, CombatConfig.PlayerMaxHp));
            float amp = Mathf.Lerp(breatheAmpLow, breatheAmpFull, hpFrac);
            float spd = Mathf.Lerp(breatheSpdLow, breatheSpdFull, hpFrac);
            breathePhase += dt * spd;
            Vector3 breathe = new Vector3(0f, Mathf.Sin(breathePhase) * amp, 0f);

            Spring(ref posOff, ref posVel, posStiff, posDamp, dt);
            Spring(ref rotOff, ref rotVel, rotStiff, rotDamp, dt);

            vmRoot.localPosition = posOff + breathe;
            vmRoot.localRotation = Quaternion.Euler(rotOff);
        }

        static void Spring(ref Vector3 x, ref Vector3 v, float stiff, float damp, float dt)
        {
            v += (-stiff * x - damp * v) * dt;
            x += v * dt;
        }

        // ── 사운드 전이 ──
        void DetectAndSound(in PlayerSim p)
        {
            byte atk = p.combat.attackPhase;
            if (atk != prevAttack)
            {
                if (prevAttack == CombatConfig.PhNone && atk == CombatConfig.PhWindup) CombatAudio.Swing();
                prevAttack = atk;
            }
            bool dash = p.dashTicks > 0;
            if (dash && !prevDash) CombatAudio.Dash();
            prevDash = dash;
            byte lg = p.combat.lungePhase;
            if (lg != prevLunge)
            {
                if (prevLunge == CombatConfig.LgNone && lg == CombatConfig.LgTravel)
                    CombatAudio.Backstrike();   // 런지 발동음(구 칼등치기 사운드 재사용) — LgWindup은 죽은 경로라 LgTravel에서 감지
                prevLunge = lg;
            }
        }
    }

    /// <summary>Play 시 칼 뷰모델 자동 부착.</summary>
    public static class SwordBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<SwordView>() == null)
                new GameObject("[SwordView]").AddComponent<SwordView>();
        }
    }
}
