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

        static readonly Color ChaseColor     = new Color(1f, 0.4f, 0.3f);   // 추격 (빨강)
        static readonly Color LeapColor      = new Color(1f, 0.9f, 0.2f);   // 절벽 도약 중 (노랑)
        static readonly Color HitColor       = new Color(0.5f, 0.05f, 0.05f); // 피격/스턴 (검붉은)
        static readonly Color WindupColor    = new Color(1f, 0.95f, 0.4f);    // 공격 선딜 텔레그래프 (밝은 노랑)
        static readonly Color AttackColor    = new Color(1f, 0.15f, 0.05f);   // 타격 순간 (강렬 빨강)

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
            PlayerAnchor.rotation = Quaternion.Euler(
                0f, Mathf.LerpAngle(prev.player.yaw, w.player.yaw, alpha), 0f);

            while (enemyViews.Count < w.enemyCount)
                enemyViews.Add(MakeCapsule($"Enemy_{enemyViews.Count}"));

            for (int i = 0; i < enemyViews.Count; i++)
            {
                // 처형 중(gloryStage>0)엔 캡슐 숨김 → Dismemberment 조각만 보이게
                bool active = i < w.enemyCount && w.enemies[i].alive && w.enemies[i].combat.gloryStage == 0;
                enemyViews[i].gameObject.SetActive(active);
                if (!active) continue;
                ref readonly EnemySim e = ref w.enemies[i];
                Vector3 ep = Vector3.Lerp(prev.enemies[i].pos, e.pos, alpha);
                // 캡슐 원점은 중심이라 몸 절반 올림 (Sim pos는 발밑). 개별 크기 반영(대형몹 3배).
                enemyViews[i].position = ep + Vector3.up * (e.height * 0.5f);
                enemyViews[i].localScale = new Vector3(e.radius * 2f, e.height * 0.5f, e.radius * 2f);
                enemyViews[i].rotation = Quaternion.Euler(0f, e.yaw, 0f);
                // 우선순위: 피격/스턴 > 공격 선딜(경고) > 타격 > 하강 단계 색
                Color col;
                if (e.combat.stunTicks > 0)                 col = HitColor;
                else if (e.ai.state == EnemyState.Windup)   col = WindupColor;
                else if (e.ai.state == EnemyState.Active)   col = AttackColor;
                else                                        col = PhaseColor(e.descentPhase);
                enemyViews[i].GetComponent<Renderer>().material.color = col;
            }
        }

        static Color PhaseColor(DescentPhase p)
            => p == DescentPhase.Leaping ? LeapColor : ChaseColor;

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
