using UnityEngine;
using Game.Sim;

namespace Game.View
{
    public class SwordView : MonoBehaviour
    {
        static readonly Vector3 IdlePosition = new Vector3(0.28f, -0.22f, 0.5f);
        static readonly Vector3 IdleEuler = new Vector3(12f, 0f, 0f);

        Transform pivot;
        PlayerActionPhase previousPhase;
        bool previousDash;

        void Update()
        {
            Main main = Main.Instance;
            if (main == null || main.Cam == null) return;
            if (pivot == null) BuildSword(main.Cam);

            ref readonly PlayerSim player = ref main.World.player;
            DetectAudio(in player);
            Pose(in player, out Vector3 position, out Quaternion rotation);
            bool crisp = player.combat.phase != PlayerActionPhase.None || player.dashTicks > 0;
            float blend = crisp ? 1f : 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime);
            pivot.localPosition = Vector3.Lerp(pivot.localPosition, position, blend);
            pivot.localRotation = Quaternion.Slerp(pivot.localRotation, rotation, blend);
        }

        void DetectAudio(in PlayerSim player)
        {
            PlayerActionPhase phase = player.combat.phase;
            if (phase != previousPhase)
            {
                if (phase == PlayerActionPhase.AttackWindup) CombatAudio.Swing();
                if (phase == PlayerActionPhase.LungeTravel) CombatAudio.Backstrike();
                previousPhase = phase;
            }

            bool dash = player.dashTicks > 0;
            if (dash && !previousDash) CombatAudio.Dash();
            previousDash = dash;
        }

        static void Pose(in PlayerSim player, out Vector3 position, out Quaternion rotation)
        {
            switch (player.combat.phase)
            {
                case PlayerActionPhase.AttackWindup:
                    position = IdlePosition;
                    rotation = Quaternion.Euler(-55f, 35f, 25f);
                    return;
                case PlayerActionPhase.AttackActive:
                    position = IdlePosition;
                    rotation = Quaternion.Euler(48f, -50f, -32f);
                    return;
                case PlayerActionPhase.LungeWindup:
                case PlayerActionPhase.LungeTravel:
                    position = new Vector3(0.14f, -0.10f, 0.72f);
                    rotation = Quaternion.Euler(-4f, 0f, 0f);
                    return;
                default:
                    position = IdlePosition;
                    rotation = Quaternion.Euler(IdleEuler);
                    return;
            }
        }

        void BuildSword(Camera camera)
        {
            pivot = new GameObject("SwordPivot").transform;
            pivot.SetParent(camera.transform, false);
            pivot.localPosition = IdlePosition;

            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            Object.Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(pivot, false);
            blade.transform.localScale = new Vector3(0.05f, 0.05f, 0.7f);
            blade.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { color = new Color(0.85f, 0.9f, 1f) };
            blade.GetComponent<Renderer>().material = material;
        }
    }

    public static class SwordBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<SwordView>() == null)
                new GameObject("[SwordView]").AddComponent<SwordView>();
        }
    }
}
