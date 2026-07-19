using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 플레이어 피격 연출. ★ combat 소유·독립. 읽기 전용.
    /// player.combat.hp 감소를 프레임 간 비교로 감지 → 붉은 비네트(ScreenFx) + 셰이크 + 피격음.
    /// 근접·투사체 어느 쪽에 맞아도 hp가 깎이므로 한 곳에서 커버. 막기 성공(무피해)은 hp 안 줆 → 안 뜸.
    /// 화면 비네트는 URP 포스트프로세싱(ScreenFx)로 이관 — 구식 OnGUI 밴드 제거.
    /// </summary>
    public class PlayerHitFeedback : MonoBehaviour
    {
        int prevHp = int.MinValue;

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            int hp = main.World.player.combat.hp;

            if (prevHp != int.MinValue && hp < prevHp)   // 피격(막혔으면 hp 안 줆)
            {
                ScreenFx.Hurt();
                CombatFeedback.Shake(0.16f);
                CombatAudio.PlayerHurt();
            }
            prevHp = hp;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PlayerHitFeedbackBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PlayerHitFeedback>() == null)
                new GameObject("[PlayerHitFeedback]").AddComponent<PlayerHitFeedback>();
        }
    }
}
