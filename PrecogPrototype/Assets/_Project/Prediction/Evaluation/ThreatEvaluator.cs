using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 최소 후보 점수화. 문서 12장 "초기 평가값" 중 이번 마일스톤에 필요한
    /// 생존·잔여 HP·최근접 위협 거리·처치 수·누적 피해량을 반영한다.
    ///
    /// 안전 거리 보너스는 SafeDistanceCap에서 포화시킨다 — 이미 충분히 안전한 거리(공격/런지
    /// 사거리 밖으로 여유 있게 벗어난 정도)면 더 멀어져도 추가 점수가 없다. 그렇지 않으면
    /// "한없이 멀어지기"가 언제나 "처치"보다 유리해져서, 위협이 없을 때 예측이 무조건 후퇴만
    /// 추천하는 겁쟁이가 된다(Milestone 2 시각화 도구로 실제 관찰됨).
    ///
    /// 누적 피해량(damageDealt)은 별도로 보상한다 — 그렇지 않으면 Beam Search가 근시안적으로
    /// 군다: 런지 한 방으로 적을 못 죽이고 회복 중인 상태는 "아직 처치 못 한 상태"라 즉시 점수가
    /// 낮게 보여서, 다음 매크로 스텝에서 평타로 마무리했을 더 높은 점수를 보기도 전에 Beam에서
    /// 잘려나간다(시각화 도구로 실제 관찰됨). 적중한 피해에 즉시 점수를 줘서 "때렸지만 아직
    /// 안 죽인" 상태도 후퇴보다 낫게 만들어 그 가지가 살아남게 한다.
    /// </summary>
    public static class ThreatEvaluator
    {
        const float HpWeight = 10f;
        const float KillWeight = 30f;
        const float DamageWeight = 8f;
        const float SafeDistanceWeight = 1f;
        const float SafeDistanceCap = 6f;

        public static float Score(in SimWorld world, int killCount, int damageDealt)
        {
            if (!world.player.alive) return float.NegativeInfinity;

            float score = world.player.health * HpWeight;
            score += killCount * KillWeight;
            score += damageDealt * DamageWeight;
            score += SafetyBonus(in world) * SafeDistanceWeight;
            return score;
        }

        static float SafetyBonus(in SimWorld world)
        {
            float nearest = float.MaxValue;
            bool anyAlive = false;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                anyAlive = true;
                float distance = CombatMath.FlatDistance(world.player.pos, enemy.pos);
                if (distance < nearest) nearest = distance;
            }
            if (!anyAlive) return SafeDistanceCap;
            return Mathf.Min(nearest, SafeDistanceCap);
        }
    }
}
