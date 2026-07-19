using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 히트스톱(A안 = sim 틱 스킵). 타격 순간 아주 잠깐 Sim만 얼려 손맛을 준다.
    /// ★ combat 소유·독립. 적 총 HP 감소 프레임을 "타격"으로 감지.
    ///
    /// timeScale=0(구식)과 달리 <b>timeScale은 1로 유지</b>한다. 얼릴 틱 수(FrozenTicks)만
    /// 요청하고, Main.FixedUpdate가 그만큼 SimStep.Run 호출을 건너뛴다. 그래서:
    ///  - Sim(적·플레이어)만 멈추고, 파티클·카메라 셰이크·화면효과·칼 애니메이션은 계속 재생
    ///    → "얼어붙은 세계에 타격만 꽂히는" 둠식 느낌.
    ///  - 결정론 안전: 틱을 스킵(지연)할 뿐 틱 내용·순서를 안 바꿔 WorldHash 시퀀스 불변.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        const int HitFreezeTicks = 3;   // 일반 타격 프리즈(≈0.05s @60Hz)

        /// <summary>남은 스킵 틱. Main.FixedUpdate가 매 틱 1씩 소비하며 sim을 건너뛴다.</summary>
        public static int FrozenTicks;

        int  prevHealthSum = int.MinValue;
        byte prevLunge;

        /// <summary>얼릴 틱 요청(더 긴 요청이 우선). 여러 타격이 겹쳐도 최댓값 유지.</summary>
        static void Freeze(int ticks) { if (ticks > FrozenTicks) FrozenTicks = ticks; }

        void Update()
        {
            if (Main.Instance == null) return;

            ref readonly SimWorld w = ref Main.Instance.World;
            int sum = 0;
            for (int i = 0; i < w.enemyCount; i++) sum += w.enemies[i].combat.health;

            // HP 총합이 줄었으면 타격(스폰으로 늘어난 건 무시). 첫 프레임은 기준만 잡음.
            if (prevHealthSum != int.MinValue && sum < prevHealthSum)
                Freeze(HitFreezeTicks);
            prevHealthSum = sum;

            // 런지 임팩트(Travel→Recovery) = 전용 긴 프리즈(글로리킬 쫀득)
            byte lg = w.player.combat.lungePhase;
            if (prevLunge == CombatConfig.LgTravel && lg == CombatConfig.LgRecovery)
                Freeze(CombatConfig.LungeHitStopTicks);
            prevLunge = lg;
        }

        void OnDisable() { FrozenTicks = 0; }   // Play 정지·비활성 시 프리즈 해제
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class HitStopBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<HitStop>() == null)
                new GameObject("[HitStop]").AddComponent<HitStop>();
        }
    }
}
