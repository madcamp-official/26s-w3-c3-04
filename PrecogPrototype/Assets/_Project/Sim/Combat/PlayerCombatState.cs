using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 플레이어 전투 상태. ★ combat 세션 소유 — 필드 추가/수정은 여기서.
    /// PlayerSim이 이걸 품기만 하므로, 여기에 필드를 늘려도 공유 파일(PlayerSim/WorldHash)은
    /// 안 건드린다. 해시는 CombatHash.MixPlayer가 담당(그것도 combat 소유).
    ///
    /// 아래는 pre-carve 초기 뼈대. GDD 2장(평타/막기/칼등치기/콤보) 기준 자리만 뚫음.
    /// </summary>
    public struct PlayerCombatState
    {
        // 평타 (좌클릭): 부채꼴 광역, 이동보정 없음
        public byte attackPhase;        // 0 None / 1 Windup / 2 Active / 3 Recovery
        public int  attackPhaseTicks;
        public int  comboCancelTicks;   // 후딜 캔슬 가능 창 (겐지 용검식)

        // 막기 (우클릭 홀드): 정면
        public bool blocking;
        public int  guardGauge;

        // 칼등치기 (막기 중 좌클릭): 이동보정 + 큰 넉백
        public byte backstrikePhase;
        public int  backstrikeTicks;

        // 보정 이동 중 전면 방어 남은 틱 (질풍참/칼등치기 공용)
        public int  frontGuardTicks;

        public static PlayerCombatState Initial => new PlayerCombatState
        {
            guardGauge = SimConfig.GuardMax,
        };
    }
}
