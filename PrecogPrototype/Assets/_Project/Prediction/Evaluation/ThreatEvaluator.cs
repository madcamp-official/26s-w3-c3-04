using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 최소 후보 점수화. 문서 12장 "초기 평가값" 중 이번 마일스톤에 필요한
    /// 생존·잔여 HP·최근접 위협 거리·포위 압박·처치 수·누적 피해량을 반영한다.
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
    ///
    /// SafetyBonus는 "가장 가까운 적 1마리"만 본다 — 여러 마리에게 완전히 포위된 상태에서
    /// 한쪽으로 뚫고 나가면 그 순간 "가장 가까운 적"만 멀어져 보여서, 등 뒤에 남은 다수를
    /// 무시하고 "안전하다"고 착각한다(적 12마리로 포위한 시나리오에서 실제 관찰: 1마리만
    /// 처치하고 나머지 11마리를 내버려 둔 채 이탈). SurroundedPenalty는 위협 반경 안의 생존 적
    /// 수 전체에 비례해 감점해서, 다수에게 둘러싸인 상태 자체를 계속 나쁘게 평가한다.
    ///
    /// SurroundedTolerance(=4)만큼은 감점하지 않는다 — OPTIMIZATION.md 5장의 "동시 공격 허용
    /// 최대 4~6마리"를 정상 교전 범위로 보고 맞췄다. 문턱을 낮게 두면(2 등) 적 2~3마리가
    /// 뭉쳐있는 흔한 상황까지 위험해 보여서 다시 무조건 후퇴만 하는 겁쟁이로 돌아간다(실제
    /// 관찰: 적 3마리·6마리 시나리오가 전부 후퇴로 바뀜).
    ///
    /// 알려진 한계: 이 값은 "반경 안 개수"만 보므로, 적이 촘촘히 뭉친 포위(예: 적 6마리가
    /// 좁은 구역에 모여있음)는 잘 잡아내지만, 적이 넓게 퍼진 원형 포위(예: 적 12마리가 9m
    /// 반경에 고르게 흩어짐)는 교전 중에도 반경 안 개수가 문턱을 잘 안 넘어서 이 페널티가
    /// 거의 작동하지 않는다. 후자를 제대로 잡으려면 "몇 마리가 가까운가"가 아니라 "여러
    /// 방향에서 동시에 위협받는가"(각도 분포) 같은 다른 지표가 필요 — 다음 튜닝 과제로 남긴다.
    /// </summary>
    public static class ThreatEvaluator
    {
        const float HpWeight = 10f;
        const float KillWeight = 30f;
        const float DamageWeight = 8f;
        const float SafeDistanceWeight = 1f;
        const float SafeDistanceCap = 6f;
        const float SurroundedRadius = 5f;
        const int SurroundedTolerance = 4;
        const float SurroundedWeight = 2f;

        public static float Score(in SimWorld world, int killCount, int damageDealt)
        {
            if (!world.player.alive) return float.NegativeInfinity;

            float score = world.player.health * HpWeight;
            score += killCount * KillWeight;
            score += damageDealt * DamageWeight;
            score += SafetyBonus(in world) * SafeDistanceWeight;
            score -= SurroundedExcess(in world) * SurroundedWeight;
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

        /// <summary>위협 반경(SurroundedRadius) 안에 있는 생존 적 수 중 SurroundedTolerance를
        /// 넘는 초과분. 적 2~3마리가 뭉쳐있는 평범한 교전은 0을 반환해 감점하지 않는다.</summary>
        static int SurroundedExcess(in SimWorld world)
        {
            int count = 0;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                if (CombatMath.FlatDistance(world.player.pos, enemy.pos) <= SurroundedRadius) count++;
            }
            return Mathf.Max(0, count - SurroundedTolerance);
        }
    }
}
