using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 1인칭 칼(직육면체) 뷰모델. ★ combat 소유·독립 — Main/EntityViews 안 건드림.
    /// 클릭 반응이 아니라 SIM 상태(Main.Instance.World.player)를 매 프레임 읽어 포즈를 구동한다.
    /// → 칼 번쩍임과 실제 판정 틱이 정확히 일치. 상태 전이에서 CombatAudio 원샷 재생.
    /// 포즈: 평타(사선베기) · 4방향 대시(낮춰 들기) · 타깃 런지(찌르기 러쉬).
    /// </summary>
    public class SwordView : MonoBehaviour
    {
        Transform pivot;
        Camera cam;

        // 이전 프레임 상태(전이 감지)
        byte prevAttack;
        bool prevDash;
        byte prevLunge;

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;

            if (pivot == null)
            {
                cam = main.Cam;
                if (cam == null) return;
                BuildSword();
            }

            ref readonly PlayerSim p = ref main.World.player;
            DetectAndSound(in p);
            ApplyPose(in p);
        }

        // ── 사운드 전이 감지 ──
        void DetectAndSound(in PlayerSim p)
        {
            byte atk = p.combat.attackPhase;
            if (atk != prevAttack)
            {
                if (prevAttack == CombatConfig.PhNone && atk == CombatConfig.PhWindup)
                    CombatAudio.Swing();
                prevAttack = atk;
            }

            bool dash = p.dashTicks > 0;
            if (dash && !prevDash) CombatAudio.Dash();
            prevDash = dash;

            byte lg = p.combat.lungePhase;
            if (lg != prevLunge)
            {
                if (prevLunge == CombatConfig.LgNone && lg == CombatConfig.LgWindup)
                    CombatAudio.Backstrike();   // 런지 발동음(구 칼등치기 사운드 재사용)
                prevLunge = lg;
            }
        }

        // ── 포즈 결정 (우선순위: 런지 > 대시 > 평타 > idle) ──
        void ApplyPose(in PlayerSim p)
        {
            Vector3 pos; Quaternion rot;

            if (p.combat.lungePhase != CombatConfig.LgNone)
                Lunge(in p.combat, out pos, out rot);
            else if (p.dashTicks > 0)
                Dash(out pos, out rot);
            else if (p.combat.attackPhase != CombatConfig.PhNone)
                Attack(in p.combat, out pos, out rot);
            else
                Idle(out pos, out rot);

            // 평타/런지는 크리스프하게(직접), 나머지는 부드럽게 보간
            bool crisp = p.combat.attackPhase != CombatConfig.PhNone
                         || p.combat.lungePhase != CombatConfig.LgNone;
            float k = crisp ? 1f : 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime);
            pivot.localPosition = Vector3.Lerp(pivot.localPosition, pos, k);
            pivot.localRotation = Quaternion.Slerp(pivot.localRotation, rot, k);
        }

        // 화면 우하단 앞, 날 살짝 세운 대기
        static readonly Vector3 IdlePos = new Vector3(0.28f, -0.22f, 0.5f);
        static readonly Vector3 IdleEul = new Vector3(12f, 0f, 0f);

        static void Idle(out Vector3 pos, out Quaternion rot)
        { pos = IdlePos; rot = Quaternion.Euler(IdleEul); }

        /// <summary>평타: windup 치켜듦 → active 순간 베기 → recovery 복귀. 실제 틱에 동기화.</summary>
        static void Attack(in PlayerCombatState c, out Vector3 pos, out Quaternion rot)
        {
            pos = IdlePos;
            Vector3 raise = new Vector3(-55f, 35f, 25f);    // 치켜든 자세
            Vector3 slash = new Vector3(48f, -50f, -32f);   // 베어내린 자세

            switch (c.attackPhase)
            {
                case CombatConfig.PhWindup:
                {
                    float t = Frac(c.attackPhaseTicks, CombatConfig.AttackWindupTicks);
                    rot = Quaternion.Euler(Vector3.Lerp(IdleEul, raise, t));
                    break;
                }
                case CombatConfig.PhActive:
                {
                    float t = Frac(c.attackPhaseTicks, CombatConfig.AttackActiveTicks);
                    rot = Quaternion.Euler(Vector3.Lerp(raise, slash, t));   // 빠른 베기
                    break;
                }
                default: // Recovery
                {
                    float t = Frac(c.attackPhaseTicks, CombatConfig.AttackRecoveryTicks);
                    rot = Quaternion.Euler(Vector3.Lerp(slash, IdleEul, t));
                    break;
                }
            }
        }

        /// <summary>4방향 대시: 칼을 살짝 낮춰 몸에 붙인 자세(이동 전용 티).</summary>
        static void Dash(out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(0.24f, -0.28f, 0.44f);
            rot = Quaternion.Euler(22f, 8f, 0f);
        }

        /// <summary>타깃 런지: 칼을 정면으로 쭉 뻗는 찌르기. Windup 준비 → Travel 최대 → Recovery 복귀.</summary>
        static void Lunge(in PlayerCombatState c, out Vector3 pos, out Quaternion rot)
        {
            rot = Quaternion.Euler(-4f, 0f, 0f);   // 날이 +Z 정면
            Vector3 ready  = new Vector3(0.20f, -0.16f, 0.40f);   // 당겨 잡음
            Vector3 thrust = new Vector3(0.10f, -0.08f, 0.85f);   // 최대 찌름

            switch (c.lungePhase)
            {
                case CombatConfig.LgWindup:
                {
                    float t = Frac(c.lungeTicks, CombatConfig.LungeWindupTicks);
                    pos = Vector3.Lerp(IdlePos, ready, t);
                    break;
                }
                case CombatConfig.LgTravel:
                {
                    float t = Frac(c.lungeTicks, CombatConfig.LungeTravelTicks);
                    pos = Vector3.Lerp(ready, thrust, t);
                    break;
                }
                default: // Recovery
                {
                    float t = Frac(c.lungeTicks, CombatConfig.LungeRecoveryTicks);
                    pos = Vector3.Lerp(thrust, IdlePos, t);
                    break;
                }
            }
        }

        static float Frac(int ticks, int total) => total <= 0 ? 1f : Mathf.Clamp01((float)ticks / total);

        void BuildSword()
        {
            var pv = new GameObject("SwordPivot");
            pivot = pv.transform;
            pivot.SetParent(cam.transform, false);
            pivot.localPosition = IdlePos;

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            Object.Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(pivot, false);
            blade.transform.localScale    = new Vector3(0.05f, 0.05f, 0.7f);   // 앞으로 뻗은 날
            blade.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            blade.GetComponent<Renderer>().material = Mat(new Color(0.85f, 0.9f, 1f));
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
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
