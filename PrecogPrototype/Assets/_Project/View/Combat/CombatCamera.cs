using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 런지·글로리킬 중 카메라를 대상에 고정. ★ combat 소유·독립. 화면연출 계층.
    /// Main이 Update에서 매 프레임 카메라를 생 입력각으로 세팅한다. 그래서 여기서 슬러프의
    /// "출발점"을 cam.transform.rotation으로 삼으면 매 프레임 리셋에 휘둘려 끊기고 안 잠긴다.
    ///
    /// 설계:
    ///  - 지속 상태 curYaw를 들고 목표(lockYaw)로 부드럽게 이은 뒤 rotation을 "절대값"으로 세팅.
    ///    → Main의 리셋·마우스·프레임 지터에 안 휘둘리고 실제로 대상에 도달·고정된다.
    ///  - 대상 yaw는 잠금 시작 시 1회만 확정(러쉬는 직진이라 이후 거의 안 변함) → 막판 급회전 제거.
    ///  - pitch는 input.Pitch를 그대로 써 해제 시 pitch 튐이 없다(input에 pitch 쓰기가 없어서).
    ///  - 해제 시 최종 yaw를 input.Yaw로 인계 → 원복(스냅백) 없음.
    /// </summary>
    public class CombatCamera : MonoBehaviour
    {
        const float TurnRate = 18f;    // 클수록 빠르게 대상 조준

        float lockYaw;   // 목표 yaw — 잠금 시작 시 1회 확정, 이후 고정
        float curYaw;    // 부드럽게 따라가는 현재 yaw (지속 상태 — 매 프레임 절대 세팅)
        bool  locked;

        void LateUpdate()
        {
            var main = Main.Instance;
            if (main == null) return;
            Camera cam = main.Cam;
            if (cam == null) return;

            ref readonly SimWorld w = ref main.World;
            ref readonly PlayerCombatState c = ref w.player.combat;

            // 대상: 글로리킬(처형 대상 몹) 우선, 아니면 런지 도착점(고정)
            bool glory = c.gloryPhase != CombatConfig.GlNone;
            bool lunge = c.lungePhase != CombatConfig.LgNone && c.lungeTargetId >= 0;
            if (!glory && !lunge)
            {
                if (locked) { main.SetLookYaw(curYaw); locked = false; }  // 해제: 최종 yaw 인계(원복 방지)
                return;
            }

            Vector3 targetPos = glory ? w.enemies[c.gloryTargetId].pos : c.lungeDest;

            if (!locked)
            {
                // 잠금 시작: 대상 방향 yaw 1회 확정 + 현재 시점에서 출발
                Vector3 flat = targetPos - cam.transform.position; flat.y = 0f;
                lockYaw = flat.sqrMagnitude > 1e-4f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : main.LookYaw;
                curYaw = main.LookYaw;
                locked = true;
            }

            // 지속 상태를 목표로 이은 뒤 절대값 세팅(리셋에 안 휘둘림) — pitch는 입력 유지
            float k = 1f - Mathf.Exp(-TurnRate * Time.unscaledDeltaTime);
            curYaw = Mathf.LerpAngle(curYaw, lockYaw, k);
            cam.transform.rotation = Quaternion.Euler(main.LookPitch, curYaw, 0f);
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class CombatCameraBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<CombatCamera>() == null)
                new GameObject("[CombatCamera]").AddComponent<CombatCamera>();
        }
    }
}
