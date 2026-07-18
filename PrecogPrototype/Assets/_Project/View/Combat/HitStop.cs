using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 히트스톱: 타격이 들어간 순간 아주 잠깐 timeScale=0(약 0.05초)로 얼려 손맛을 준다.
    /// ★ combat 소유·독립. 적 총 HP가 줄어든 프레임을 "타격"으로 감지.
    /// Sim(FixedUpdate)도 같이 멈추므로 그게 곧 정지 효과. SwordView는 unscaled라 계속 움직임.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        const float FreezeDur = 0.05f;
        int   prevHealthSum = int.MinValue;
        byte  prevLunge;
        float freezeLeft;

        void Update()
        {
            if (Main.Instance == null) return;

            ref readonly SimWorld w = ref Main.Instance.World;
            int sum = 0;
            for (int i = 0; i < w.enemyCount; i++) sum += w.enemies[i].combat.health;

            // HP 총합이 줄었으면 타격(스폰으로 늘어난 건 무시). 첫 프레임은 기준만 잡음.
            if (prevHealthSum != int.MinValue && sum < prevHealthSum)
                freezeLeft = Mathf.Max(freezeLeft, FreezeDur);
            prevHealthSum = sum;

            // 런지 임팩트(Travel→Recovery) = 전용 긴 프리즈(글로리킬 쫀득)
            byte lg = w.player.combat.lungePhase;
            if (prevLunge == CombatConfig.LgTravel && lg == CombatConfig.LgRecovery)
                freezeLeft = Mathf.Max(freezeLeft, CombatConfig.LungeHitStopTicks * SimConfig.TickDelta);
            prevLunge = lg;

            if (freezeLeft > 0f)
            {
                Time.timeScale = 0f;
                freezeLeft -= Time.unscaledDeltaTime;
                if (freezeLeft <= 0f) Time.timeScale = 1f;
            }
        }

        void OnDisable() { Time.timeScale = 1f; }   // Play 정지·비활성 시 원복
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class HitStopBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<HitStop>() == null)
                new GameObject("[HitStop]").AddComponent<HitStop>();
        }
    }
}
