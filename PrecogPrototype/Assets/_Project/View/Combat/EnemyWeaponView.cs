using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 적 무기(직육면체) 뷰 + 절차적 공격 스윙. ★ combat 소유·독립. 읽기 전용.
    /// EntityViews가 그린 적 캡슐(이름 "Enemy_i")을 이름으로 찾아, 그 몸에 무기를 물려
    /// e.ai.state/stateTicks에 맞춰 스윙한다. 근접=직육면체 칼, 원거리=직육면체 두 개(활).
    /// 캡슐이 비균등 스케일이라 자식으로 붙이면 찌그러짐 → 월드 좌표로 배치(스케일 무시),
    /// LateUpdate에서 EntityViews 보간 뒤에 얹어 몸과 정확히 일치.
    /// 상태 전이에서 몹 SFX(EnemyWindup/Melee/Aim/Fire)도 재생. 몸통 본 애니는 없음(무기만).
    /// </summary>
    public class EnemyWeaponView : MonoBehaviour
    {
        class Slot
        {
            public Transform  capsule;      // 적 몸(EntityViews)
            public Transform  pivot;        // 무기 회전축(월드 배치, scale 1)
            public Archetype  archetype;
            public bool       built;
            public EnemyState prevState;
            public bool       prevSeen;
            public float      recoilT;      // 발사 반동(0..1, 감쇠)
        }

        readonly Slot[] slots = new Slot[SimConfig.MaxEnemies];
        Material meleeMat, bowMat;

        const float SoundRange = 18f;   // 이 거리 안 적만 공격음(스팸 방지)

        void Awake()
        {
            meleeMat = Mat(new Color(0.72f, 0.72f, 0.78f));   // 금속 칼
            bowMat   = Mat(new Color(0.45f, 0.28f, 0.14f));   // 나무 활
        }

        void LateUpdate()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            for (int i = 0; i < slots.Length; i++)
            {
                Slot s = slots[i];
                bool active = i < w.enemyCount && w.enemies[i].alive;
                if (!active)
                {
                    if (s != null && s.pivot != null) s.pivot.gameObject.SetActive(false);
                    continue;
                }

                ref readonly EnemySim e = ref w.enemies[i];
                if (s == null) s = slots[i] = new Slot();

                if (s.capsule == null)
                {
                    var go = GameObject.Find($"Enemy_{i}");
                    if (go == null) continue;   // 아직 안 생김 — 다음 프레임 재시도
                    s.capsule = go.transform;
                }

                if (!s.built || s.archetype != e.ai.archetype)
                    BuildWeapon(s, e.ai.archetype);

                s.pivot.gameObject.SetActive(true);
                HandleTransitions(s, in e, in w.player);
                Pose(s, in e);
            }
        }

        // ── 상태 전이 → SFX + 반동 ──
        void HandleTransitions(Slot s, in EnemySim e, in PlayerSim p)
        {
            EnemyState st = e.ai.state;
            if (s.prevSeen && st != s.prevState)
            {
                Vector3 d = e.pos - p.pos; d.y = 0f;
                bool near = d.sqrMagnitude < SoundRange * SoundRange;

                if (near)
                {
                    if (st == EnemyState.Windup)      CombatAudio.EnemyWindup();
                    else if (st == EnemyState.Active) CombatAudio.EnemyMelee();
                    else if (st == EnemyState.Aim)    CombatAudio.EnemyAim();
                }
                if (s.prevState == EnemyState.Aim && st != EnemyState.Aim)   // 발사됨
                {
                    if (near) CombatAudio.EnemyFire();
                    s.recoilT = 1f;
                }
            }
            s.prevState = st;
            s.prevSeen  = true;
        }

        // ── 포즈 + 월드 배치 ──
        void Pose(Slot s, in EnemySim e)
        {
            if (s.recoilT > 0f) s.recoilT = Mathf.MoveTowards(s.recoilT, 0f, Time.deltaTime / 0.18f);

            Vector3 hand; Quaternion swing;
            if (s.archetype == Archetype.MeleeGrunt) MeleePose(in e.ai, out hand, out swing);
            else                                     RangedPose(s, in e.ai, out hand, out swing);

            Quaternion bodyRot = s.capsule.rotation;   // Euler(0,yaw,0) — 스케일 무시됨
            s.pivot.SetPositionAndRotation(s.capsule.position + bodyRot * hand, bodyRot * swing);
        }

        static readonly Vector3 MeleeRest   = new Vector3(25f, 0f, -10f);
        static readonly Vector3 MeleeRaise  = new Vector3(-80f, 0f, 20f);
        static readonly Vector3 MeleeStrike = new Vector3(55f, 0f, -5f);

        static void MeleePose(in EnemyAI ai, out Vector3 hand, out Quaternion swing)
        {
            hand = new Vector3(0.2f, 0f, 0.08f);
            Vector3 e;
            switch (ai.state)
            {
                case EnemyState.Windup:
                    e = Vector3.Lerp(MeleeRest, MeleeRaise, Frac(ai.stateTicks, AIConfig.MeleeWindupTicks));
                    break;
                case EnemyState.Active:
                    e = Vector3.Lerp(MeleeRaise, MeleeStrike, Frac(ai.stateTicks, AIConfig.MeleeActiveTicks));
                    break;
                case EnemyState.Recovery:
                    e = Vector3.Lerp(MeleeStrike, MeleeRest, Frac(ai.stateTicks, AIConfig.MeleeRecoveryTicks));
                    break;
                default:
                    e = MeleeRest;
                    break;
            }
            swing = Quaternion.Euler(e);
        }

        static readonly Vector3 RangedRest = new Vector3(12f, 0f, 0f);
        static readonly Vector3 RangedAim  = new Vector3(0f, 0f, 0f);

        static void RangedPose(Slot s, in EnemyAI ai, out Vector3 hand, out Quaternion swing)
        {
            hand = new Vector3(0.16f, 0.05f, 0.1f);
            Vector3 e = ai.state == EnemyState.Aim
                ? Vector3.Lerp(RangedRest, RangedAim, Frac(ai.stateTicks, AIConfig.RangedAimTicks))
                : RangedRest;

            hand += new Vector3(0f, 0f, -0.08f * s.recoilT);   // 발사 반동: 뒤로 당김
            e    += new Vector3(-15f * s.recoilT, 0f, 0f);
            swing = Quaternion.Euler(e);
        }

        static float Frac(int ticks, int total) => total <= 0 ? 1f : Mathf.Clamp01((float)ticks / total);

        // ── 무기 생성 ──
        void BuildWeapon(Slot s, Archetype arch)
        {
            if (s.pivot != null) Destroy(s.pivot.gameObject);

            var pv = new GameObject($"EnemyWeapon_{arch}");
            pv.transform.SetParent(transform, false);   // 매니저(scale 1) 아래
            s.pivot = pv.transform;

            if (arch == Archetype.MeleeGrunt)
                Box(s.pivot, "Blade", new Vector3(0.05f, 0.05f, 0.45f), new Vector3(0f, 0f, 0.22f),
                    Quaternion.identity, meleeMat);
            else
            {
                Box(s.pivot, "BowUpper", new Vector3(0.025f, 0.22f, 0.025f), new Vector3(0f, 0.1f, 0.05f),
                    Quaternion.Euler(30f, 0f, 0f), bowMat);
                Box(s.pivot, "BowLower", new Vector3(0.025f, 0.22f, 0.025f), new Vector3(0f, -0.1f, 0.05f),
                    Quaternion.Euler(-30f, 0f, 0f), bowMat);
            }

            s.archetype = arch;
            s.built = true;
        }

        static void Box(Transform parent, string name, Vector3 scale, Vector3 pos, Quaternion rot, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());   // 순수 시각 — 충돌 없음
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.GetComponent<Renderer>().material = m;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class EnemyWeaponBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<EnemyWeaponView>() == null)
                new GameObject("[EnemyWeaponView]").AddComponent<EnemyWeaponView>();
        }
    }
}
