using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 플레이어 전투 상태. ★ combat 소유. PlayerSim이 품기만 하므로 여기 필드 늘려도
    /// 공유 파일 안 건드림. 해시는 CombatHash.MixPlayer 담당.
    /// </summary>
    public struct PlayerCombatState
    {
        // 평타 (좌클릭)
        public byte attackPhase;        // 0 None / 1 Windup / 2 Active / 3 Recovery
        public int  attackPhaseTicks;
        public bool attackHitDone;

        // 체력 (적 공격으로 감소 — 적용은 CombatResolve가 방어판정 후)
        public int  hp;

        // 막기 (우클릭 홀드)
        public bool blocking;
        public int  guardGauge;
        public int  guardIdleTicks;     // 비홀드 지속 틱(회복 지연용)

        // 칼등치기 (막기 중 좌클릭) — 글로리킬식 러쉬 + 넉백
        public byte    backstrikePhase; // 0 None / 1 Lunge / 2 Recovery
        public int     backstrikeTicks;
        public Vector3 backstrikeTarget;// 대상 위치(카메라 고정·러쉬)
        public bool    hasBackstrikeTarget;
        public bool    backstrikeHitDone;// 임팩트 1회 처리 플래그

        // 질풍참 관통 (이동은 PlayerMovement, 판정만 combat)
        public bool dashPierceDone;

        // 대형몹 글로리킬 처형 (컷신). 진행 중 무적·조작잠금.
        public byte    gloryPhase;      // GlNone/GlSlash1/GlSlash2/GlDash
        public int     gloryTicks;
        public int     gloryTargetId;   // 처형 대상 적 인덱스
        public Vector3 gloryDir;        // 질풍참 피니시 관통 방향(고정)

        // 보정/돌진 중 전면 방어 남은 틱
        public int frontGuardTicks;

        public static PlayerCombatState Initial => new PlayerCombatState
        {
            hp = CombatConfig.PlayerMaxHp,
            guardGauge = SimConfig.GuardMax,
        };
    }
}
