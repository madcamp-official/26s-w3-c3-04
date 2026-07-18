using Game.Sim;
using UnityEngine;

namespace Game.View
{
    /// <summary>결정론적 EnemyState(AI 상태)를 읽어 근접/원거리 무기 포즈와 공격음을 표현한다.</summary>
    public sealed class EnemyWeaponView : MonoBehaviour
    {
        sealed class Slot
        {
            public Transform capsule;
            public Transform pivot;
            public EnemyState previous;
            public bool previousSeen;
        }

        readonly Slot[] slots = new Slot[SimConfig.MaxEnemies];
        Material bladeMaterial;

        void Awake()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            bladeMaterial = new Material(shader) { color = new Color(0.72f, 0.72f, 0.78f) };
        }

        void LateUpdate()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld world = ref main.World;

            for (int i = 0; i < slots.Length; i++)
            {
                bool active = i < world.enemyCount && world.enemies[i].alive;
                var slot = slots[i];
                if (!active)
                {
                    if (slot?.pivot != null) slot.pivot.gameObject.SetActive(false);
                    continue;
                }

                ref readonly EnemySim enemy = ref world.enemies[i];
                if (slot == null) slot = slots[i] = new Slot();
                if (slot.capsule == null)
                {
                    var body = GameObject.Find($"Enemy_{i}");
                    if (body == null) continue;
                    slot.capsule = body.transform;
                    BuildWeapon(slot, i);
                }

                slot.pivot.gameObject.SetActive(true);
                PlayTransition(slot, enemy.ai.state, enemy.pos, world.player.pos);
                Pose(slot, in enemy);
            }
        }

        static void PlayTransition(Slot slot, EnemyState state, Vector3 enemyPos, Vector3 playerPos)
        {
            if (slot.previousSeen && state != slot.previous &&
                (enemyPos - playerPos).sqrMagnitude < 18f * 18f)
            {
                if (state == EnemyState.Windup || state == EnemyState.Aim) CombatAudio.EnemyWindup();
                else if (state == EnemyState.Active || state == EnemyState.Fire) CombatAudio.EnemyMelee();
            }

            slot.previous = state;
            slot.previousSeen = true;
        }

        static void Pose(Slot slot, in EnemySim enemy)
        {
            Vector3 rest = new Vector3(25f, 0f, -10f);
            Vector3 raised = new Vector3(-80f, 0f, 20f);
            Vector3 strike = new Vector3(55f, 0f, -5f);
            Vector3 euler;

            switch (enemy.ai.state)
            {
                case EnemyState.Windup:
                    euler = Vector3.Lerp(rest, raised,
                        Frac(enemy.ai.stateTicks, AIConfig.MeleeWindupTicks));
                    break;
                case EnemyState.Active:
                    euler = Vector3.Lerp(raised, strike,
                        Frac(enemy.ai.stateTicks, AIConfig.MeleeActiveTicks));
                    break;
                case EnemyState.Recovery:
                    euler = Vector3.Lerp(strike, rest,
                        Frac(enemy.ai.stateTicks, AIConfig.MeleeRecoveryTicks));
                    break;
                case EnemyState.Aim:
                    euler = Vector3.Lerp(rest, raised,
                        Frac(enemy.ai.stateTicks, AIConfig.RangedAimTicks));
                    break;
                case EnemyState.Fire:
                    euler = strike;
                    break;
                default:
                    euler = rest;
                    break;
            }

            var bodyRotation = slot.capsule.rotation;
            slot.pivot.SetPositionAndRotation(
                slot.capsule.position + bodyRotation * new Vector3(0.2f, 0f, 0.08f),
                bodyRotation * Quaternion.Euler(euler));
        }

        void BuildWeapon(Slot slot, int index)
        {
            var pivotObject = new GameObject($"EnemyWeapon_{index}");
            pivotObject.transform.SetParent(transform, false);
            slot.pivot = pivotObject.transform;

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(slot.pivot, false);
            blade.transform.localScale = new Vector3(0.05f, 0.05f, 0.45f);
            blade.transform.localPosition = new Vector3(0f, 0f, 0.22f);
            blade.GetComponent<Renderer>().material = bladeMaterial;
        }

        static float Frac(int ticks, int total) =>
            total <= 0 ? 1f : Mathf.Clamp01((float)ticks / total);
    }
}
