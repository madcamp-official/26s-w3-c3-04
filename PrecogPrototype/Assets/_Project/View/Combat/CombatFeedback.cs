using UnityEngine;
using Unity.Cinemachine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 전투 손맛(juice). ★ combat 소유·독립. 읽기 전용(SimWorld 안 씀).
    /// 적 피격/처치를 프레임 간 상태 비교로 감지 → 카메라 셰이크 + 스파크 + 타격음.
    /// 플레이어 대시/런지 시작에도 셰이크.
    /// ※ 히트스톱은 HitStop.cs 담당(중복 timeScale 조작 방지). 카메라 고정·HUD도 SIM 세션 몫.
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        // 셰이크 = Cinemachine Impulse (방향·세기 파라미터). vcam의 ImpulseListener가 수신.
        const float ShakeForce = 6f;   // amp(0~) → 임펄스 힘 배율 (전체 쉐이크 세기 튜닝)
        CinemachineImpulseSource impulseSource;

        static CombatFeedback inst;
        /// <summary>다른 연출(플레이어 피격 등)이 같은 셰이크 시스템을 쓰게 하는 정적 진입점.</summary>
        public static void Shake(float amp) { if (inst != null) inst.AddShake(amp); }

        // 적 상태 추적 (인덱스 안정: append-only, 처치해도 배열 유지)
        readonly int[]  prevHp    = new int[SimConfig.MaxEnemies];
        readonly bool[] prevAlive = new bool[SimConfig.MaxEnemies];
        readonly bool[] seen      = new bool[SimConfig.MaxEnemies];

        // 플레이어 상태 추적
        bool prevDash;
        byte prevLunge;

        // 대시 FOV 킥 (둠식 속도감)
        const float FovKickAmount = 10f;
        const float FovKickDecay  = 7f;
        float fovKick, baseFov = -1f;

        ParticleSystem sparks;

        void Awake()
        {
            inst = this;
            BuildSparks();
            impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
            impulseSource.DefaultVelocity = new Vector3(0.4f, 0.4f, 0.15f);   // 기본 쉐이크 방향(힘으로 스케일)
        }

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            // ── 적 피격/처치 감지 (스턴 폐기 → HP 감소 기반) ──
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (seen[i])
                {
                    if (e.alive && e.combat.health < prevHp[i])      // HP 감소 = 새 타격
                        OnHit(e.pos);
                    if (prevAlive[i] && !e.alive)                    // 처치
                        OnDeath(e.pos);
                }
                seen[i]      = true;
                prevHp[i]    = e.combat.health;
                prevAlive[i] = e.alive;
            }

            // ── 플레이어 액션 셰이크 + 대시 FOV 킥 ──
            bool dash = w.player.dashTicks > 0;
            if (dash && !prevDash) { AddShake(0.10f); fovKick = 1f; }
            prevDash = dash;
            if (fovKick > 0f)
                fovKick = Mathf.MoveTowards(fovKick, 0f, FovKickDecay * Time.unscaledDeltaTime);

            byte lg = w.player.combat.lungePhase;
            if (lg != prevLunge)
            {
                if (lg == CombatConfig.LgTravel) AddShake(0.10f);                      // 발동
                if (prevLunge == CombatConfig.LgTravel && lg == CombatConfig.LgRecovery)
                { AddShake(0.22f); fovKick = CombatConfig.LungeFovKick / FovKickAmount; }  // 임팩트: 강한 셰이크 + FOV 킥(0~1 모델)
                prevLunge = lg;
            }
        }

        void LateUpdate()
        {
            // FOV 킥은 vcam 렌즈에 얹는다(Brain이 실카메라에 반영). 쉐이크는 Impulse가 처리(여기 없음).
            var vcam = Main.Instance != null ? Main.Instance.GameplayVcam : null;
            if (vcam == null) return;
            if (baseFov < 0f) baseFov = vcam.Lens.FieldOfView;
            vcam.Lens.FieldOfView = baseFov + FovKickAmount * fovKick;
        }

        void OnHit(Vector3 pos)
        {
            AddShake(0.12f);
            EmitSparks(pos, 14);
            CombatAudio.Hit();        // 칼 타격(금속)
            CombatAudio.EnemyPain();  // 적 신음(유기)
        }

        void OnDeath(Vector3 pos)
        {
            AddShake(0.18f);
            EmitSparks(pos, 30);
            CombatAudio.Death();
        }

        void AddShake(float amp)   // amp 세기로 Impulse 발생 → vcam ImpulseListener가 카메라 흔듦
        {
            if (impulseSource != null) impulseSource.GenerateImpulseWithForce(amp * ShakeForce);
        }

        void EmitSparks(Vector3 pos, int count)
        {
            if (sparks == null) return;
            sparks.transform.position = pos + Vector3.up * 0.6f;
            sparks.Emit(count);
        }

        void BuildSparks()
        {
            var go = new GameObject("HitSparks");
            go.transform.SetParent(transform, false);
            sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop();

            var m = sparks.main;
            m.startLifetime   = 0.35f;
            m.startSpeed      = 6f;
            m.startSize       = 0.12f;
            m.startColor      = new Color(0.5f, 0.05f, 0.05f);   // 검붉은
            m.gravityModifier = 1.2f;
            m.maxParticles    = 300;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.playOnAwake     = false;

            var em = sparks.emission; em.enabled = false;   // 수동 Emit만
            var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;

            var r = sparks.GetComponent<ParticleSystemRenderer>();
            var sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                   ?? Shader.Find("Sprites/Default");
            if (sh2 != null) r.material = new Material(sh2);
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class CombatFeedbackBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<CombatFeedback>() == null)
                new GameObject("[CombatFeedback]").AddComponent<CombatFeedback>();
        }
    }
}
