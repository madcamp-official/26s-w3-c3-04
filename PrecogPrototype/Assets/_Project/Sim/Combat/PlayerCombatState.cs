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

        // 체력·피격 (적용은 CombatResolve가)
        public int  hp;
        public int  hitStunTicks;       // >0이면 피격 경직(수평 조작 제한)

        // 타깃 런지 (우클릭) — 시작 순간 targetId·도착점·Travel 틱 고정, 재추적 없음
        public byte    lungePhase;      // LgNone/Windup/Travel/Recovery
        public int     lungeTicks;
        public int     lungeTargetId;   // 대상 적 id (-1=없음)
        public Vector3 lungeStart;
        public Vector3 lungeDest;       // 적 앞 지점(고정)
        public int     lungeTravelTicks; // 거리 비례 Travel 틱(시작 시 계산·고정)
        public bool    lungeHitDone;    // 임팩트 1회 처리 플래그
        public int     lungeCooldown;   // 남은 쿨타임 틱(0.25초 연발 제한)
        public int     lungeStacks;     // 런지 자원(처치로 충전, 발동 1 소모). 시작 2
        public int     lungeBufferTicks; // 쿨 막판 예약(>0이면 쿨 끝나는 즉시 발동)

        // 대형몹 글로리킬 처형 (컷신). 진행 중 무적·조작잠금.
        public byte    gloryPhase;      // GlNone/GlSlash1/GlSlash2/GlDash
        public int     gloryTicks;
        public int     gloryTargetId;   // 처형 대상 적 인덱스
        public Vector3 gloryDir;        // 피니시 러쉬 방향(고정)

        public static PlayerCombatState Initial => new PlayerCombatState
        {
            hp = CombatConfig.PlayerMaxHp,
            lungeTargetId = -1,
            lungeStacks = 2,     // 시작 시 스택 꽉 채움
        };
    }
}
