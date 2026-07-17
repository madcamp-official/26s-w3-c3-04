using System.Collections.Generic;
using UnityEngine;
using Game.Simulation;

namespace Game.Runtime
{
    /// <summary>
    /// 시뮬레이션 상태를 원기둥/직육면체로 "비추는" 거울.
    /// 판단은 하지 않는다 — Sim이 정한 위치로 옮길 뿐.
    ///
    /// 1인칭이라 플레이어 자기 몸(캡슐)은 렌더러를 끄고, 대신 칼(직육면체)만
    /// 시야 앞에 보이게 한다. 칼은 순수 연출 — 판정은 시뮬레이션이 부채꼴/선분으로
    /// 직접 한다. 칼을 사무라이 메시로 갈아끼워도 게임 로직은 안 바뀐다.
    /// </summary>
    public class EntityViews
    {
        public Transform PlayerAnchor { get; private set; }  // 카메라가 따라올 기준

        Transform swordPivot;   // 이걸 회전시켜 칼을 휘두른다
        readonly List<Transform> enemyViews = new List<Transform>();

        Material enemyMat;
        Material swordMat;

        public void Init()
        {
            enemyMat = MakeMat(new Color(1f, 0.4f, 0.3f));
            swordMat = MakeMat(new Color(0.85f, 0.9f, 1f));

            // 플레이어 앵커 (캡슐이지만 1인칭이라 렌더러 끔)
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerAnchor";
            Object.Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().enabled = false;   // 1인칭: 내 몸 숨김
            PlayerAnchor = body.transform;

            // 칼: 손잡이 피벗 + 날(직육면체). 피벗을 돌리면 날이 호를 그린다.
            var pivot = new GameObject("SwordPivot");
            swordPivot = pivot.transform;
            swordPivot.SetParent(PlayerAnchor, false);
            swordPivot.localPosition = new Vector3(0.35f, 1.4f, 0.4f); // 눈높이 앞·오른쪽

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            Object.Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(swordPivot, false);
            blade.transform.localScale = new Vector3(0.08f, 0.08f, 1.1f); // 앞으로 뻗은 날
            blade.transform.localPosition = new Vector3(0f, 0f, 0.55f);   // 손잡이 기준 앞으로
            blade.GetComponent<Renderer>().material = swordMat;
        }

        public void Sync(in SimWorld w, float alpha, in SimWorld prev)
        {
            // 플레이어 앵커: 위치 보간 + yaw (카메라가 여기 붙는다)
            Vector3 p = Vector3.Lerp(prev.player.pos, w.player.pos, alpha);
            PlayerAnchor.position = p;
            PlayerAnchor.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);

            AnimateSword(in w.player);

            // 적: 수에 맞춰 뷰 생성/토글
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

        /// <summary>공격 단계·돌진에 따라 칼 피벗을 회전시킨다 (순수 연출).</summary>
        void AnimateSword(in PlayerState p)
        {
            // 기본 자세: 살짝 안쪽으로 든 상태
            Vector3 euler = new Vector3(10f, 0f, 0f);

            if (p.dashTicksRemaining > 0)
            {
                // 질풍참: 칼을 앞으로 쭉 찌름 (수평)
                euler = new Vector3(80f, 0f, 0f);
            }
            else switch (p.attackPhase)
            {
                case AttackPhase.Windup:
                {
                    float t = p.attackPhaseTicks / (float)SimConfig.AttackWindupTicks;
                    euler = new Vector3(10f - 70f * t, -40f * t, 0f); // 치켜듦
                    break;
                }
                case AttackPhase.Active:
                {
                    float t = p.attackPhaseTicks / (float)SimConfig.AttackActiveTicks;
                    // 오른쪽 위 → 왼쪽 아래로 베어내림
                    euler = new Vector3(-60f + 120f * t, -40f + 110f * t, 0f);
                    break;
                }
                case AttackPhase.Recovery:
                {
                    float t = p.attackPhaseTicks / (float)SimConfig.AttackRecoveryTicks;
                    euler = Vector3.Lerp(new Vector3(60f, 70f, 0f),
                                         new Vector3(10f, 0f, 0f), t); // 원위치
                    break;
                }
            }

            swordPivot.localRotation = Quaternion.Euler(euler);
        }

        static Transform MakeCapsule(string name, Material mat, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
            go.GetComponent<Renderer>().material = mat;
            return go.transform;
        }

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
