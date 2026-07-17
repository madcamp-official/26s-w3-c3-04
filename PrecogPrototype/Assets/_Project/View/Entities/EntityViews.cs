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

        static readonly Color ChaseColor    = new Color(1f, 0.4f, 0.3f);   // 추격 (빨강)
        static readonly Color EdgePauseColor = new Color(1f, 0.9f, 0.2f);  // 멈칫 (노랑)
        static readonly Color AirborneColor = new Color(0.3f, 1f, 0.4f);   // 점프 (초록)
        static readonly Color RecoveryColor = new Color(0.4f, 0.6f, 1f);   // 회복 (파랑)

        public void Init()
        {
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
                ref readonly EnemySim e = ref w.enemies[i];
                Vector3 ep = Vector3.Lerp(prev.enemies[i].pos, e.pos, alpha);
                // 캡슐 원점은 중심이라 몸 절반 올림 (Sim pos는 발밑)
                enemyViews[i].position = ep + Vector3.up * (SimConfig.EnemyHeight * 0.5f);
                enemyViews[i].rotation = Quaternion.Euler(0f, e.yaw, 0f);
                enemyViews[i].GetComponent<Renderer>().material.color = PhaseColor(e.descentPhase);
            }
        }

        static Color PhaseColor(DescentPhase p)
        {
            switch (p)
            {
                case DescentPhase.EdgePause: return EdgePauseColor;
                case DescentPhase.Airborne:  return AirborneColor;
                case DescentPhase.Recovery:  return RecoveryColor;
                default:                     return ChaseColor;
            }
        }

        Transform MakeCapsule(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(SimConfig.EnemyRadius * 2f, SimConfig.EnemyHeight * 0.5f,
                                                  SimConfig.EnemyRadius * 2f);
            go.GetComponent<Renderer>().material = Mat(ChaseColor);  // 개별 인스턴스
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
