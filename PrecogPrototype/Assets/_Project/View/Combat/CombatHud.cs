using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 전투 HUD. ★ combat 소유·독립(Main 안 건드림). Main.Instance.World만 읽음.
    /// 세로 막대: 런지 쿨타임(하양) + 옆에 대시 충전(빨강). 위에 HP 바.
    /// IMGUI(OnGUI) — 캔버스/프리팹 없이 자기완결. 순수 연출.
    /// </summary>
    public class CombatHud : MonoBehaviour
    {
        static Texture2D _tex;
        static Texture2D Px
        {
            get
            {
                if (_tex == null)
                {
                    _tex = new Texture2D(1, 1);
                    _tex.SetPixel(0, 0, Color.white);
                    _tex.Apply();
                }
                return _tex;
            }
        }

        // 막대 배치 (화면 좌하단, 세로)
        const float BarW = 22f, BarH = 200f, Gap = 8f, Pad = 28f;

        void OnGUI()
        {
            if (Main.Instance == null) return;
            ref readonly PlayerCombatState c = ref Main.Instance.World.player.combat;
            ref readonly PlayerSim p = ref Main.Instance.World.player;

            float x = Pad;
            float y = Screen.height - Pad - BarH;

            DrawHp(x, y - 24f, in c);
            DrawLunge(x, y, in c);
            DrawDash(x + BarW + Gap, y, in p);
        }

        void DrawHp(float x, float y, in PlayerCombatState c)
        {
            const float w = 120f, h = 14f;
            Fill(x - 2, y - 2, w + 4, h + 4, new Color(0.08f, 0.04f, 0.04f, 0.85f));
            float f = Mathf.Clamp01(c.hp / (float)CombatConfig.PlayerMaxHp);
            var col = f > 0.3f ? new Color(0.85f, 0.2f, 0.2f) : new Color(1f, 0.5f, 0.1f);  // 낮으면 주황 경고
            Fill(x, y, w * f, h, col);
            Frame(x, y, w, h, new Color(0f, 0f, 0f, 0.6f));
        }

        void DrawLunge(float x, float y, in PlayerCombatState c)
        {
            var bg = new Color(0.08f, 0.08f, 0.10f, 0.85f);
            Fill(x - 2, y - 2, BarW + 4, BarH + 4, bg);

            // 쿨타임 회복 진행(가득 = 사용 가능). 사용 가능하면 밝은 하양, 충전 중엔 어둡게.
            float f = 1f - Mathf.Clamp01(c.lungeCooldown / (float)CombatConfig.LungeCooldownTicks);
            float fh = BarH * f;
            var col = c.lungeCooldown == 0 ? new Color(0.92f, 0.92f, 0.95f) : new Color(0.55f, 0.58f, 0.65f);
            Fill(x, y + (BarH - fh), BarW, fh, col);   // 아래에서 위로 차오름

            Frame(x, y, BarW, BarH, new Color(0f, 0f, 0f, 0.6f));
        }

        void DrawDash(float x, float y, in PlayerSim p)
        {
            var bg = new Color(0.10f, 0.06f, 0.06f, 0.85f);
            Fill(x - 2, y - 2, BarW + 4, BarH + 4, bg);

            int max = SimConfig.DashMaxCharges;
            float segH = (BarH - (max - 1) * 3f) / max;   // 스택당 칸 (3px 간격)
            var red = new Color(0.85f, 0.18f, 0.15f);
            var dim = new Color(0.30f, 0.10f, 0.10f);

            // 아래 칸부터 채움. 채워진 스택 = 빨강, 다음 스택 = 충전 진행분만.
            float charging = 0f;
            if (p.dashCharges < max && p.dashRecharge > 0)
                charging = 1f - Mathf.Clamp01(p.dashRecharge / (float)SimConfig.DashRechargeTicks);

            for (int i = 0; i < max; i++)
            {
                float sy = y + BarH - segH - i * (segH + 3f);
                if (i < p.dashCharges) Fill(x, sy, BarW, segH, red);
                else
                {
                    Fill(x, sy, BarW, segH, dim);
                    if (i == p.dashCharges && charging > 0f)
                        Fill(x, sy + segH * (1f - charging), BarW, segH * charging, red);
                }
            }
            Frame(x, y, BarW, BarH, new Color(0f, 0f, 0f, 0.6f));
        }

        static void Fill(float x, float y, float w, float h, Color c)
        {
            var prev = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), Px);
            GUI.color = prev;
        }

        static void Frame(float x, float y, float w, float h, Color c)
        {
            Fill(x, y, w, 1, c); Fill(x, y + h - 1, w, 1, c);
            Fill(x, y, 1, h, c); Fill(x + w - 1, y, 1, h, c);
        }
    }

    /// <summary>Play 시 HUD 자동 부착.</summary>
    public static class CombatHudBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<CombatHud>() == null)
                new GameObject("[CombatHud]").AddComponent<CombatHud>();
        }
    }
}
