using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Sim 상태를 캡슐 GameObject로 비추는 거울. 1인칭이라 플레이어 몸은 렌더 끔.
    /// 콜라이더는 전부 제거 — 충돌 판정은 Sim이 CapsuleCast로 지형에만 한다.
    /// </summary>
    public class EntityViews
    {
        public Transform PlayerAnchor { get; private set; }
        readonly List<Transform> enemyViews = new List<Transform>();
        Material enemyMat;

        public void Init()
        {
            enemyMat = Mat(new Color(1f, 0.4f, 0.3f));

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerAnchor";
            Object.Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().enabled = false;  // 1인칭
            PlayerAnchor = body.transform;
        }

        public void Sync(in SimWorld w, in SimWorld prev, float alpha)
        {
            Vector3 pp = Vector3.Lerp(prev.player.pos, w.player.pos, alpha);
            PlayerAnchor.position = pp;
            PlayerAnchor.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);

            while (enemyViews.Count < w.enemyCount)
                enemyViews.Add(MakeCapsule($"Enemy_{enemyViews.Count}"));

            for (int i = 0; i < enemyViews.Count; i++)
            {
                bool active = i < w.enemyCount && w.enemies[i].alive;
                enemyViews[i].gameObject.SetActive(active);
                if (!active) continue;
                Vector3 ep = Vector3.Lerp(prev.enemies[i].pos, w.enemies[i].pos, alpha);
                // 캡슐 원점은 중심이라 몸 절반 올림 (Sim pos는 발밑)
                enemyViews[i].position = ep + Vector3.up * (SimConfig.EnemyHeight * 0.5f);
                enemyViews[i].rotation = Quaternion.Euler(0f, w.enemies[i].yaw, 0f);
            }
        }

        Transform MakeCapsule(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.EnemyRadius * 2f, SimConfig.EnemyHeight * 0.5f,
                                                  SimConfig.EnemyRadius * 2f);
            go.GetComponent<Renderer>().material = enemyMat;
            return go.transform;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
