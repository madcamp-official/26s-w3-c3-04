using System.Collections.Generic;
using UnityEngine;
using Game.Simulation;

namespace Game.Runtime
{
    /// <summary>
    /// 시뮬레이션 상태를 원기둥 GameObject로 "비추는" 거울.
    /// 판단은 하지 않는다 — Sim이 정한 위치로 원기둥을 옮길 뿐.
    /// 나중에 원기둥을 사무라이 모델로 갈아끼워도 시뮬레이션은 안 바뀐다.
    /// </summary>
    public class EntityViews
    {
        Transform playerView;
        readonly List<Transform> enemyViews = new List<Transform>();

        Material playerMat;
        Material enemyMat;

        public void Init()
        {
            playerMat = MakeMat(new Color(0.3f, 0.7f, 1f));
            enemyMat  = MakeMat(new Color(1f, 0.4f, 0.3f));

            playerView = MakeCapsule("PlayerView", playerMat,
                                     SimConfig.PlayerRadius);
        }

        public void Sync(in SimWorld w, float alpha, in SimWorld prev)
        {
            // 플레이어 (이전 틱과 보간해 부드럽게)
            Vector3 p = Vector3.Lerp(prev.player.pos, w.player.pos, alpha);
            playerView.position = p + Vector3.up * 1f;   // 캡슐 높이 보정
            playerView.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);

            // 적 수에 맞춰 뷰 개수 조정
            while (enemyViews.Count < w.enemyCount)
            {
                var t = MakeCapsule($"EnemyView_{enemyViews.Count}", enemyMat,
                                    SimConfig.MeleeRadius);
                enemyViews.Add(t);
            }

            for (int i = 0; i < enemyViews.Count; i++)
            {
                bool active = i < w.enemyCount && w.enemies[i].alive;
                enemyViews[i].gameObject.SetActive(active);
                if (!active) continue;

                Vector3 ep = Vector3.Lerp(prev.enemies[i].pos, w.enemies[i].pos, alpha);
                enemyViews[i].position = ep + Vector3.up * 1f;
                enemyViews[i].rotation = Quaternion.Euler(0f, w.enemies[i].yaw, 0f);
            }
        }

        static Transform MakeCapsule(string name, Material mat, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            // 콜라이더 제거 — 판정은 시뮬레이션이 구로 직접 한다
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
            go.GetComponent<Renderer>().material = mat;
            return go.transform;
        }

        static Material MakeMat(Color c)
        {
            // URP 기본 리트 셰이더
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader);
            m.color = c;
            return m;
        }
    }
}
