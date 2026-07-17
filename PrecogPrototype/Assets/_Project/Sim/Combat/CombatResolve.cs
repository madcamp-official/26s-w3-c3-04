namespace Game.Sim
{
    /// <summary>
    /// 대미지·스턴·HP·처치 정리. ★ combat 세션 소유 — 스텁.
    /// SimStep이 적 이동 다음에 호출한다(자리만 뚫어둠).
    /// 모든 공격 = 대미지 1 + 스턴 0.5초 통일 적용, HP 감소, 처치 마킹, 넉백 끝점 처리 등.
    /// 지금은 stunTicks 감소만(적이 영원히 안 굳게).
    /// </summary>
    public static class CombatResolve
    {
        public static void Run(ref SimWorld w, in SimServices svc, float dt)
        {
            // 최소 동작: 스턴 타이머 감소 (combat 로직 붙기 전까지 적이 안 굳게)
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (w.enemies[i].combat.stunTicks > 0)
                    w.enemies[i].combat.stunTicks--;
            }
            // TODO(combat 세션): 평타/질풍참/칼등치기 판정, 대미지·스턴 부여, HP·처치, 넉백
        }
    }
}
