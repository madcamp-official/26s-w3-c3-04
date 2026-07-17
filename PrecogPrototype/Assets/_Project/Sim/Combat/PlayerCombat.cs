namespace Game.Sim
{
    /// <summary>
    /// 플레이어 전투 상태머신 (평타/막기/칼등치기/콤보 캔슬). ★ combat 세션 소유 — 스텁.
    /// SimStep이 PlayerMovement 다음에 이걸 호출한다(자리만 뚫어둠).
    /// combat 세션이 여기 로직을 채운다. 지금은 빈 함수.
    /// </summary>
    public static class PlayerCombat
    {
        public static void Step(ref SimWorld w, in InputCmd cmd, in SimServices svc, float dt)
        {
            // TODO(combat 세션): 좌클릭=평타, 우클릭홀드=막기, 막기중 좌클릭=칼등치기, 후딜 캔슬
        }
    }
}
