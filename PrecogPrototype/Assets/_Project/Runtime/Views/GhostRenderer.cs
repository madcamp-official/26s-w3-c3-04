using System.Collections.Generic;
using UnityEngine;
using Game.Prediction;

namespace Game.Runtime
{
    /// <summary>
    /// 예지 타임라인을 화면에 그린다.
    /// - 플레이어 잔상: 전체 경로의 0.5초 노드를 항상 표시. 선택 시점은 강조.
    /// - 적 위치: 선택된 시점(화살표로 이동)의 적들만 표시.
    ///
    /// 순수 표현 — Prediction이 준 좌표를 구로 찍을 뿐.
    /// </summary>
    public class GhostRenderer
    {
        readonly List<GameObject> playerPool = new List<GameObject>();
        readonly List<GameObject> enemyPool  = new List<GameObject>();

        static readonly Color PathColor     = new Color(0.35f, 0.8f, 1f);   // 잔상 경로
        static readonly Color SelectedColor = new Color(0.3f,  1f,  0.4f);  // 선택 시점 플레이어
        static readonly Color DeadPlayer    = new Color(1f,    0.2f, 0.15f);
        static readonly Color EnemyColor    = new Color(1f,    0.5f, 0.2f); // 미래 적 위치
        static readonly Color EnemyStunned  = new Color(0.4f,  0.5f, 1f);

        public void Render(List<PathPreview.Frame> frames, int selected)
        {
            // 플레이어 잔상: 전체 경로
            int pUsed = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                bool sel = (i == selected);
                float size = sel ? 0.6f : 0.32f;
                Color col = !f.playerAlive ? DeadPlayer : (sel ? SelectedColor : PathColor);

                var go = Get(playerPool, pUsed++);
                go.transform.position = f.playerPos + Vector3.up * 1f;
                go.transform.localScale = Vector3.one * size;
                SetColor(go, col);
                go.SetActive(true);
            }
            HideRest(playerPool, pUsed);

            // 적 위치: 선택된 시점만
            int eUsed = 0;
            if (selected >= 0 && selected < frames.Count)
            {
                var f = frames[selected];
                for (int i = 0; i < f.enemies.Length; i++)
                {
                    if (!f.enemies[i].alive) continue;
                    var go = Get(enemyPool, eUsed++);
                    go.transform.position = f.enemies[i].pos + Vector3.up * 1f;
                    go.transform.localScale = Vector3.one * 0.9f;
                    SetColor(go, f.enemies[i].stunned ? EnemyStunned : EnemyColor);
                    go.SetActive(true);
                }
            }
            HideRest(enemyPool, eUsed);
        }

        public void Hide()
        {
            HideRest(playerPool, 0);
            HideRest(enemyPool, 0);
        }

        static GameObject Get(List<GameObject> pool, int i)
        {
            while (pool.Count <= i)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Ghost";
                Object.Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().material = MakeMat(Color.white);
                go.SetActive(false);
                pool.Add(go);
            }
            return pool[i];
        }

        static void HideRest(List<GameObject> pool, int from)
        {
            for (int i = from; i < pool.Count; i++)
                pool[i].SetActive(false);
        }

        static void SetColor(GameObject go, Color c)
            => go.GetComponent<Renderer>().material.color = c;

        static Material MakeMat(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader);
            m.color = c;
            return m;
        }
    }
}
