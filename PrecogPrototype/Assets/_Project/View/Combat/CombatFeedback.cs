using UnityEngine;
using Unity.Cinemachine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 전투 손맛(juice). ★ combat 소유·독립. 읽기 전용(SimWorld 안 씀).
    /// 적 피격/처치를 프레임 간 상태 비교로 감지 → 카메라 셰이크 + 스파크 + 타격음.
    /// 플레이어 대시/런지 시작에도 셰이크.
    /// ※ 히트스톱은 HitStop.cs 담당(A안=sim 틱 스킵, timeScale 안 건드림). 카메라 고정·HUD도 SIM 세션 몫.
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        // 셰이크 = Cinemachine Impulse (방향·세기 파라미터). vcam의 ImpulseListener가 수신.
        // 상황별(타격 각도·런지 방향·착지)로 임펄스 "방향 벡터"를 달리 줘 손맛을 낸다.
        const float ShakeForce = 6f;   // amp(0~) → 임펄스 힘 배율 (전체 쉐이크 세기 튜닝)
        // 착지 충격(수직): 하강 속도 비례. 튜닝 상수.
        const float LandMinSpeed = 3f;     // 이 속도 미만 착지는 무시(잔착지)
        const float LandPerSpeed = 0.018f; // 하강속도 → amp 환산
        CinemachineImpulseSource impulseSource;

        // 착지 감지용 이전 프레임 상태
        bool  prevGrounded = true;
        float prevVelY;

        static CombatFeedback inst;
        /// <summary>다른 연출(플레이어 피격 등)이 같은 셰이크 시스템을 쓰게 하는 정적 진입점.</summary>
        public static void Shake(float amp) { if (inst != null) inst.AddShake(amp); }

        // 적 상태 추적. ★ sim이 죽은 슬롯을 재사용하므로 인덱스가 아니라 id로 점유자를 식별한다
        //   (슬롯 재사용 시 사망 셰이크·음 누락, HP 비교 오작동 방지).
        readonly int[]     prevHp    = new int[SimConfig.MaxEnemies];
        readonly bool[]    prevAlive = new bool[SimConfig.MaxEnemies];
        readonly bool[]    seen      = new bool[SimConfig.MaxEnemies];
        readonly int[]     prevId    = new int[SimConfig.MaxEnemies];
        readonly Vector3[] prevPos   = new Vector3[SimConfig.MaxEnemies];   // 떠난 적의 마지막 자리

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
                bool sameId = seen[i] && e.id == prevId[i];
                if (seen[i])
                {
                    if (sameId && e.alive && e.combat.health < prevHp[i])   // HP 감소 = 새 타격(동일 적일 때만)
                        OnHit(e.pos);
                    // 처치 = 죽어서 남았거나 슬롯이 재사용됨(id 변경). 재사용이면 떠난 적의 마지막 자리.
                    if (prevAlive[i] && (!e.alive || !sameId))
                        OnDeath(sameId ? e.pos : prevPos[i]);
                }
                seen[i]      = true;
                prevHp[i]    = e.combat.health;
                prevAlive[i] = e.alive;
                prevId[i]    = e.id;
                prevPos[i]   = e.pos;
            }

            // ── 플레이어 액션 셰이크(방향성) + 대시 FOV 킥 ──
            bool dash = w.player.dashTicks > 0;
            if (dash && !prevDash) { AxisShake(w.player.dashDir, 0.10f); fovKick = 1f; }   // 대시 방향으로 훅
            prevDash = dash;
            if (fovKick > 0f)
                fovKick = Mathf.MoveTowards(fovKick, 0f, FovKickDecay * Time.unscaledDeltaTime);

            byte lg = w.player.combat.lungePhase;
            if (lg != prevLunge)
            {
                Vector3 lungeDir = w.player.combat.lungeDest - w.player.pos;   // 런지 표적 방향
                if (lg == CombatConfig.LgTravel) AxisShake(lungeDir, 0.10f);   // 발동: 표적 방향 훅
                if (prevLunge == CombatConfig.LgTravel && lg == CombatConfig.LgRecovery)
                {
                    AxisShake(lungeDir, 0.22f); fovKick = CombatConfig.LungeFovKick / FovKickAmount;   // 임팩트: 표적 방향 강한 훅 + FOV 킥
                    ScreenFx.Impact(0.55f);   // 색수차 버스트
                }
                prevLunge = lg;
            }
            // 런지 Travel 동안 렌즈 왜곡 풀백(매 프레임 목표 세팅 → 종료 시 자연 복귀)
            ScreenFx.LungePull(lg == CombatConfig.LgTravel ? 1f : 0f);

            // ── 착지 충격: grounded false→true 전환 시 직전 하강 속도 비례 수직 임펄스 ──
            bool grounded = w.player.grounded;
            if (grounded && !prevGrounded)
            {
                float impact = Mathf.Max(0f, -prevVelY);
                if (impact > LandMinSpeed)
                    AxisShake(Vector3.down, Mathf.Clamp(impact * LandPerSpeed, 0.08f, 0.30f));
            }
            prevGrounded = grounded;
            prevVelY     = w.player.vel.y;
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
            DirectionalShake(pos, 0.12f, 0.35f);   // 타격 각도: 적→카메라 반동 + 약간 위로
            EmitSparks(pos, 14);
            CombatAudio.Hit();        // 칼 타격(금속)
            CombatAudio.EnemyPain();  // 적 신음(유기)
        }

        void OnDeath(Vector3 pos)
        {
            DirectionalShake(pos, 0.18f, 0.5f);    // 처치: 더 강한 반동 + 위로 펀치
            ScreenFx.Impact(0.4f);                 // 색수차 버스트
            EmitSparks(pos, 30);
            CombatAudio.Death();
        }

        // 무방향(정적 진입점 등): DefaultVelocity 방향으로 세기만.
        void AddShake(float amp)
        {
            if (impulseSource != null) impulseSource.GenerateImpulseWithForce(amp * ShakeForce);
        }

        // 타격 각도 셰이크: 타격 지점 → 카메라(반동 방향) + upBias 만큼 위로 튐.
        void DirectionalShake(Vector3 fromWorldPos, float amp, float upBias)
        {
            if (impulseSource == null) return;
            Vector3 dir = Vector3.up;
            var cam = Main.Instance != null ? Main.Instance.Cam : null;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - fromWorldPos;
                toCam.y = 0f;
                dir = toCam.sqrMagnitude > 1e-4f ? toCam.normalized : cam.transform.forward;
            }
            impulseSource.GenerateImpulse((dir + Vector3.up * upBias) * (amp * ShakeForce));
        }

        // 방향 지정 셰이크(런지·대시·착지): 주어진 월드 방향으로 훅.
        void AxisShake(Vector3 worldDir, float amp)
        {
            if (impulseSource == null) return;
            if (worldDir.sqrMagnitude < 1e-6f) { AddShake(amp); return; }
            impulseSource.GenerateImpulse(worldDir.normalized * (amp * ShakeForce));
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
