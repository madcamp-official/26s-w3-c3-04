using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 1인칭 칼 뷰모델. ★ combat 소유·독립 — Main/EntityViews 안 건드림. 읽기 전용(SimWorld).
    ///
    /// 구조(하이브리드): 스윙 = 사장님이 Animation 창에서 authoring한 클립,
    ///   재생 시점은 Sim phase가 몬다(틱 동기). 그 위에 절차 레이어(착지·숨·피격)를 additive로 얹는다.
    ///
    /// 계층: KatanaViewmodel(root, 절차 오프셋 여기) > PoseTarget(클립이 애니메이션) > Katana(메시).
    ///   - 클립(Katana_Idle/Slash1/Slash2/Thrust)은 PoseTarget의 Transform만 애니메이션.
    ///   - Animator를 자동재생이 아니라 매 프레임 normalizedTime으로 수동 샘플링 → 틱 동기.
    ///   - 절차 오프셋은 root에 적용(클립이 root를 안 건드리므로 안 싸움).
    /// </summary>
    public class SwordView : MonoBehaviour
    {
        const string ViewmodelPrefab = "KatanaViewmodel";   // Resources/Prefabs 하위

        Transform vmRoot;    // 절차 오프셋 적용 대상(클립 미관여)
        Animator  anim;
        Camera    cam;

        // 클립 state 해시(컨트롤러의 state 이름 = 클립 이름)
        static readonly int HIdle   = Animator.StringToHash("Katana_Idle");
        static readonly int HSlash1 = Animator.StringToHash("Katana_Slash1");
        static readonly int HSlash2 = Animator.StringToHash("Katana_Slash2");
        static readonly int HThrust = Animator.StringToHash("Katana_Thrust");

        // 사운드 전이 감지
        byte prevAttack;
        bool prevDash;
        byte prevLunge;

        // ── 절차 레이어 상태 ──
        bool  prevGrounded = true;
        float prevVelY;
        int   prevHp = int.MinValue;
        float breathePhase;
        Vector3 posOff, posVel;   // 위치 스프링(착지·피격)
        Vector3 rotOff, rotVel;   // 회전 스프링(피격 움찔), euler(도)

        // ── 절차 튜닝 상수 ──
        const float LandKick     = 0.06f;  // 착지 하강속도 → 아래 임펄스
        const float LandMinSpeed = 3f;
        const float PosStiff     = 180f, PosDamp = 18f;   // 위치 스프링(클수록 빠름/딱딱)
        const float RotStiff     = 220f, RotDamp = 20f;   // 회전 스프링
        const float HitPosKick   = 0.05f, HitRotKick = 8f;
        // 숨고르기: HP 낮을수록 진폭·속도↑ (숨 가빠짐)
        const float BreatheAmpFull = 0.004f, BreatheAmpLow = 0.018f;
        const float BreatheSpdFull = 1.6f,   BreatheSpdLow = 4.5f;

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
            DriveClip(in p.combat);
            Procedural(in p);
        }

        /// <summary>
        /// 씬에 이미 뷰모델이 있으면 "그걸" 쓴다(애니메이션 authoring용으로 카메라 밑에 둔 인스턴스).
        /// 없을 때만 프리팹에서 생성. → 작업 화면 = 게임 화면이 일치하고 칼이 2개가 되지 않는다.
        /// </summary>
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
            vmRoot.localPosition = Vector3.zero;   // 이후 절차 레이어가 매 프레임 덮음
            vmRoot.localRotation = Quaternion.identity;

            anim = go.GetComponent<Animator>();
            if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return true;
        }

        /// <summary>카메라 자식 우선, 없으면 씬 전체에서 같은 이름을 찾는다.</summary>
        GameObject FindExistingViewmodel()
        {
            Transform t = cam.transform.Find(ViewmodelPrefab);
            if (t != null) return t.gameObject;
            return GameObject.Find(ViewmodelPrefab);
        }

        // ── 스윙: Sim phase → 클립 + normalizedTime 수동 샘플(틱 동기) ──
        void DriveClip(in PlayerCombatState c)
        {
            if (anim == null) return;
            int hash; float nt;

            if (c.gloryPhase != CombatConfig.GlNone)        // 처형(TODO: 전용 클립)
            { hash = HSlash1; nt = 0.5f; }
            else if (c.lungePhase != CombatConfig.LgNone)   // 찌르기(= 런지)
            { hash = HThrust; nt = LungeNt(in c); }
            else if (c.attackPhase != CombatConfig.PhNone)  // 평타(TODO: 콤보로 Slash1/2 교대)
            { hash = HSlash1; nt = AttackNt(in c); }
            else
            { hash = HIdle; nt = 0f; }

            anim.Play(hash, 0, nt);
            anim.Update(0f);   // 즉시 그 시점 포즈로 샘플(시간 전진 X)
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

        // ── 절차 레이어: 클립 포즈 위에 additive(root 오프셋) ──
        void Procedural(in PlayerSim p)
        {
            float dt = Time.deltaTime;

            // 착지 딥
            bool grounded = p.grounded;
            if (grounded && !prevGrounded)
            {
                float impact = Mathf.Max(0f, -prevVelY);
                if (impact > LandMinSpeed) posVel += Vector3.down * (impact * LandKick);
            }
            prevGrounded = grounded;
            prevVelY = p.vel.y;

            // 피격 움찔
            if (prevHp != int.MinValue && p.combat.hp < prevHp)
            {
                posVel += Random.insideUnitSphere * HitPosKick;
                rotOff += Random.insideUnitSphere * HitRotKick;
            }
            prevHp = p.combat.hp;

            // 숨고르기(HP 낮을수록 진폭·속도↑)
            float hpFrac = Mathf.Clamp01(p.combat.hp / (float)Mathf.Max(1, CombatConfig.PlayerMaxHp));
            float amp = Mathf.Lerp(BreatheAmpLow, BreatheAmpFull, hpFrac);
            float spd = Mathf.Lerp(BreatheSpdLow, BreatheSpdFull, hpFrac);
            breathePhase += dt * spd;
            Vector3 breathe = new Vector3(0f, Mathf.Sin(breathePhase) * amp, 0f);

            // 스프링 감쇠(목표 0으로)
            Spring(ref posOff, ref posVel, PosStiff, PosDamp, dt);
            Spring(ref rotOff, ref rotVel, RotStiff, RotDamp, dt);

            // root에 합산 적용(클립은 PoseTarget을 몰고, 여긴 그 위 additive)
            vmRoot.localPosition = posOff + breathe;
            vmRoot.localRotation = Quaternion.Euler(rotOff);
        }

        /// <summary>감쇠 스프링: x를 0으로 당기며 오버슛→정착.</summary>
        static void Spring(ref Vector3 x, ref Vector3 v, float stiff, float damp, float dt)
        {
            v += (-stiff * x - damp * v) * dt;
            x += v * dt;
        }

        // ── 사운드 전이 감지(기존 유지) ──
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
