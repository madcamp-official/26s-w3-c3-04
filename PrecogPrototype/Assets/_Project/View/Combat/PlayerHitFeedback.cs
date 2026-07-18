using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 플레이어 피격 연출. ★ combat 소유·독립. 읽기 전용.
    /// player.combat.hp 감소를 프레임 간 비교로 감지 → 붉은 비네트 + 셰이크 + 피격음.
    /// 근접·투사체 어느 쪽에 맞아도 hp가 깎이므로 한 곳에서 커버. 막기 성공(무피해)은 hp 안 줆 → 안 뜸.
    /// HUD·데미지수치는 다른 세션 몫. 여기선 "맞았다"는 화면 피드백만.
    /// </summary>
    public class PlayerHitFeedback : MonoBehaviour
    {
        int   prevHp = int.MinValue;
        float flash;                 // 0..1 비네트 세기(감쇠)
        const float FlashDecay = 2.5f;

        static Texture2D _px;
        static Texture2D Px
        {
            get
            {
                if (_px == null) { _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply(); }
                return _px;
            }
        }

        void Update()
        {
            if (flash > 0f) flash = Mathf.MoveTowards(flash, 0f, FlashDecay * Time.unscaledDeltaTime);

            var main = Main.Instance;
            if (main == null) return;
            int hp = main.World.player.health;

            if (prevHp != int.MinValue && hp < prevHp)   // 피격(막혔으면 hp 안 줆)
            {
                flash = 1f;
                CombatFeedback.Shake(0.16f);
                CombatAudio.PlayerHurt();
            }
            prevHp = hp;
        }

        void OnGUI()
        {
            if (flash <= 0f) return;
            // 화면 가장자리 붉은 비네트(가운데는 투명, 테두리로 갈수록 진함) — 네 변 그라데이션 근사.
            float a = Mathf.Clamp01(flash) * 0.55f;
            var col = new Color(0.6f, 0f, 0f, a);
            int w = Screen.width, h = Screen.height;
            int band = Mathf.RoundToInt(Mathf.Min(w, h) * 0.18f);

            var prev = GUI.color; GUI.color = col;
            GUI.DrawTexture(new Rect(0, 0, w, band), Px);              // 상
            GUI.DrawTexture(new Rect(0, h - band, w, band), Px);       // 하
            GUI.DrawTexture(new Rect(0, 0, band, h), Px);             // 좌
            GUI.DrawTexture(new Rect(w - band, 0, band, h), Px);      // 우
            GUI.color = prev;
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
