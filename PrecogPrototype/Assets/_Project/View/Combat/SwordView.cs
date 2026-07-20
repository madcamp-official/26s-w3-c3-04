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
                if (prevLunge == CombatConfig.LgNone && lg == CombatConfig.LgTravel)
                    CombatAudio.Backstrike();   // 런지 발동음(구 칼등치기 사운드 재사용)
                prevLunge = lg;
            }
        }

        // ── 포즈 결정 (우선순위: 처형 > 런지 > 대시 > 평타 > idle) ──
        void ApplyPose(in PlayerSim p)
        {
            Vector3 pos; Quaternion rot;

            if (p.combat.gloryPhase != CombatConfig.GlNone)
                Glory(in p.combat, out pos, out rot);
            else if (p.combat.lungePhase != CombatConfig.LgNone)
                Lunge(in p.combat, out pos, out rot);
            else if (p.dashTicks > 0)
                Dash(out pos, out rot);
            else if (p.combat.attackPhase != CombatConfig.PhNone)
                Attack(in p.combat, out pos, out rot);
            else
                Idle(out pos, out rot);

            // 평타/런지/처형은 크리스프하게(직접), 나머지는 부드럽게 보간
            bool crisp = p.combat.attackPhase != CombatConfig.PhNone
                         || p.combat.lungePhase != CombatConfig.LgNone
                         || p.combat.gloryPhase != CombatConfig.GlNone;
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

        /// <summary>타깃 런지(글로리킬식): 블링크 도착과 함께 아래에서 위로 올려베기(어퍼컷 슬래시).</summary>
        static void Lunge(in PlayerCombatState c, out Vector3 pos, out Quaternion rot)
        {
            // 낮게 아래(칼끝 내림) → 위로 크게 올려벰
            Vector3 lowPos  = new Vector3(0.10f, -0.42f, 0.55f);
            Vector3 lowEul  = new Vector3(70f, -20f, 20f);    // 칼끝 아래로
            Vector3 highPos = new Vector3(-0.10f, 0.30f, 0.55f);
            Vector3 highEul = new Vector3(-70f, 25f, -35f);   // 위로 쳐올림

            // Travel(블링크) 동안 low→high로 빠르게 올려베고, Recovery(0틱)/그 외엔 high 유지→idle 복귀
            float t = c.lungePhase == CombatConfig.LgTravel
                ? Frac(c.lungeTicks, c.lungeTravelTicks)
                : 1f;
            pos = Vector3.Lerp(lowPos, highPos, t);
            rot = Quaternion.Euler(Vector3.Lerp(lowEul, highEul, t));
        }

        /// <summary>
        /// 대형몹 처형 컷신: 화면 중앙에서 크게 X자 2번 베고 → 아래에서 위로 올려베기 피니시.
        /// 카메라가 적을 중앙 고정하므로 칼도 중앙 앞에서 크게 휘둘러야 보인다(구석 idle 금지).
        /// </summary>
        static void Glory(in PlayerCombatState c, out Vector3 pos, out Quaternion rot)
        {
            // 평타 스윙(치켜듦→베어내림)을 화면 중앙 앞에서 크게 2번(좌우 대칭), 피니시는 런지 어퍼컷.
            // 카메라가 적을 중앙 고정하므로 칼도 중앙 앞에서 크게 휘둘러야 보인다.
            Vector3 atkPos = new Vector3(0.04f, 0.02f, 0.55f);                                   // 평타는 중앙 앞 고정
            Vector3 raise1 = new Vector3(-68f, 42f, 38f), slash1 = new Vector3(60f, -54f, -40f); // ↘ 평타
            Vector3 raise2 = new Vector3(-68f, -42f, -38f), slash2 = new Vector3(60f, 54f, 40f); // ↙ 평타(반대손)
            // 런지 어퍼컷(아래→위) — 돌진과 한 비트(우클 모션 재사용)
            Vector3 lungeLow  = new Vector3(0.08f, -0.44f, 0.55f), lungeLowEul  = new Vector3(75f, -18f, 18f);
            Vector3 lungeHigh = new Vector3(-0.08f, 0.34f, 0.62f), lungeHighEul = new Vector3(-75f, 22f, -32f);

            switch (c.gloryPhase)
            {
                case CombatConfig.GlSlash1:   // 평타 1
                {
                    float t = Ease(Frac(c.gloryTicks, CombatConfig.GlorySlashTicks));
                    pos = atkPos;
                    rot = Quaternion.Euler(Vector3.Lerp(raise1, slash1, t));
                    break;
                }
                case CombatConfig.GlSlash2:   // 평타 2 (반대 방향)
                {
                    float t = Ease(Frac(c.gloryTicks, CombatConfig.GlorySlashTicks));
                    pos = atkPos;
                    rot = Quaternion.Euler(Vector3.Lerp(raise2, slash2, t));
                    break;
                }
                default: // GlDash — 런지 어퍼컷 + 돌진(한 비트)
                {
                    float t = Ease(Frac(c.gloryTicks, CombatConfig.GloryDashTicks));
                    pos = Vector3.Lerp(lungeLow, lungeHigh, t);
                    rot = Quaternion.Euler(Vector3.Lerp(lungeLowEul, lungeHighEul, t));
                    break;
                }
            }
        }

        /// <summary>스무드스텝(양끝 부드럽고 중간 빠름) — 짧은 처형 컷을 "부드럽지만 빠르게".</summary>
        static float Ease(float t) => t * t * (3f - 2f * t);

        static float Frac(int ticks, int total) => total <= 0 ? 1f : Mathf.Clamp01((float)ticks / total);

        // Magic_Sword_03 모델 실측(루트 로컬 합산 바운드): 긴 축 Z=15.036, center=(0.259,1.564,-0.235).
        // 이미 +Z(전방)로 누워 있어 회전 불필요. 스케일로 줄이고 중심 보정 후 앞으로 내민다.
        const float SwordModelLenZ = 15.036f;
        static readonly Vector3 SwordModelCenter = new Vector3(0.259f, 1.564f, -0.235f);

        void BuildSword()
        {
            var pv = new GameObject("SwordPivot");
            pivot = pv.transform;
            pivot.SetParent(cam.transform, false);
            pivot.localPosition = IdlePos;

            var model = Resources.Load<GameObject>("Magic_Sword_03");
            if (model == null) { BuildCubeFallback(); return; }   // 에셋 없으면 구식 큐브

            var sword = Object.Instantiate(model).transform;
            sword.name = "Blade";
            sword.SetParent(pivot, false);

            float scale = 0.75f / SwordModelLenZ;                 // 날 길이 ≈0.75로
            sword.localScale = Vector3.one * scale;
            sword.localRotation = Quaternion.identity;            // 긴 축이 이미 +Z
            // 바운드 중심을 원점으로 당긴 뒤 살짝 앞으로 내밀어 손에서 뻗어나오게
            sword.localPosition = -SwordModelCenter * scale + new Vector3(0f, 0f, 0.30f);

            var mat = Mat(new Color(0.82f, 0.86f, 0.95f));        // 금속 칼날 색
            foreach (var r in sword.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        /// <summary>에셋 로드 실패 시 구식 직육면체 날(안전망).</summary>
        void BuildCubeFallback()
        {
            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            Object.Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(pivot, false);
            blade.transform.localScale    = new Vector3(0.05f, 0.05f, 0.7f);
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
