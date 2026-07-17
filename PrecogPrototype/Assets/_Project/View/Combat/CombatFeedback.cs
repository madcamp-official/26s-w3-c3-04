using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 전투 손맛(juice). ★ combat 소유·독립. 읽기 전용(SimWorld 안 씀).
    /// 적 피격/처치를 프레임 간 상태 비교로 감지 → 카메라 셰이크 + 스파크 + 타격음.
    /// 플레이어 질풍참/칼등치기 시작에도 셰이크.
    /// ※ 히트스톱은 HitStop.cs 담당(중복 timeScale 조작 방지). 카메라 고정·HUD도 SIM 세션 몫.
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        // 셰이크
        const float ShakeDecay = 6f;
        float shakeAmp;

        // 적 상태 추적 (인덱스 안정: append-only, 처치해도 배열 유지)
        readonly int[]  prevStun  = new int[SimConfig.MaxEnemies];
        readonly bool[] prevAlive = new bool[SimConfig.MaxEnemies];
        readonly bool[] seen      = new bool[SimConfig.MaxEnemies];

        // 플레이어 상태 추적
        bool prevDash;
        byte prevBackstrike;

        ParticleSystem sparks;

        void Awake() => BuildSparks();

        void Update()
        {
            // 셰이크 감쇠
            if (shakeAmp > 0f)
                shakeAmp = Mathf.MoveTowards(shakeAmp, 0f, ShakeDecay * Time.unscaledDeltaTime);

            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            // ── 적 피격/처치 감지 ──
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (seen[i])
                {
                    if (e.combat.stunTicks > prevStun[i])            // 스턴 상승 = 새 타격
                        OnHit(e.pos);
                    if (prevAlive[i] && !e.alive)                    // 처치
                        OnDeath(e.pos);
                }
                seen[i]      = true;
                prevStun[i]  = e.combat.stunTicks;
                prevAlive[i] = e.alive;
            }

            // ── 플레이어 액션 셰이크 ──
            bool dash = w.player.dashTicks > 0;
            if (dash && !prevDash) AddShake(0.10f);
            prevDash = dash;

            byte bs = w.player.combat.backstrikePhase;
            if (bs != prevBackstrike && bs == CombatConfig.BsLunge) AddShake(0.14f);
            prevBackstrike = bs;
        }

        void LateUpdate()
        {
            // Main.Update가 카메라 위치·회전을 세팅한 "뒤"에 셰이크를 덧씌운다(안 싸움).
            if (shakeAmp <= 0f) return;
            var cam = Main.Instance != null ? Main.Instance.Cam : null;
            if (cam == null) return;

            Vector3 off = Random.insideUnitSphere * shakeAmp;
            off.z *= 0.3f;
            cam.transform.position += off;
            cam.transform.rotation *= Quaternion.Euler(off.y * 30f, off.x * 30f, 0f);
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

        void AddShake(float amp) => shakeAmp = Mathf.Max(shakeAmp, amp);

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
