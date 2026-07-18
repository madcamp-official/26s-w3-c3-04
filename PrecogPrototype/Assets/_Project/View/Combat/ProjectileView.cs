using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 투사체를 구(sphere)로 비추는 거울. ★ 독립 뷰 — Main.Instance.World만 읽음.
    /// 외형은 잠정(그냥 동그라미) — 연출은 다른 곳에서. 콜라이더 없음(판정은 Sim).
    /// </summary>
    public class ProjectileView : MonoBehaviour
    {
        readonly List<Transform> balls = new List<Transform>();

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            while (balls.Count < w.projectileCount) balls.Add(MakeBall(balls.Count));

            for (int i = 0; i < balls.Count; i++)
            {
                bool active = i < w.projectileCount && w.projectiles[i].alive;
                balls[i].gameObject.SetActive(active);
                if (!active) continue;
                balls[i].position = w.projectiles[i].pos;
            }
        }

        Transform MakeBall(int idx)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Projectile_{idx}";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * (AIConfig.ProjectileRadius * 2f);
            go.GetComponent<Renderer>().material = Mat(new Color(0.5f, 0.85f, 1f));  // 플라즈마 하늘색(잠정)
            return go.transform;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class ProjectileViewBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ProjectileView>() == null)
                new GameObject("[ProjectileView]").AddComponent<ProjectileView>();
        }
    }
}
