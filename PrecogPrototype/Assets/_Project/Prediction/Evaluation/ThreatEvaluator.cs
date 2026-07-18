using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>세 갈래 점수(안전·처치·난이도)와 Beam 정렬용 합산치.</summary>
    public struct ScoreBreakdown
    {
        public float safety;
        public float kill;
        public float difficulty;
        public float Total => safety + kill + difficulty;
    }

    /// <summary>
    /// 후보 점수화. docs/shared/PREDICTION_CONTRACT.md 12장 가중치(피격 -200/탈출경로 +80/
    /// 포위 무문턱 -30/남은 대시 +20/동일 행동 반복 -5)를 그대로 넣어봤다가, 시각화 도구로
    /// 재검증하는 과정에서 회귀를 발견했다: 적이 처음부터 사거리 밖(3~6마리 시나리오)이면
    /// Beam Search가 아예 접근을 안 함.
    ///
    /// 원인 두 가지:
    /// 1. 계약이 못박은 행동 순서상 Wait가 제일 앞이라, "아직 아무 일도 안 일어난" 동점
    ///    상황(이동/대기/후퇴가 전부 같은 점수)에서 Wait가 항상 동률 승리.
    /// 2. "남은 대시 1회 +20"은 대시를 쓰는 순간 사라지는 보상이라, 장거리 접근에 대시가
    ///    꼭 필요한 상황에서도 대시 자체를 안 쓰는 쪽으로 유도됨.
    ///
    /// 사용자와 상의 후, 평가 공식만 이전에 실제로 잘 동작했던 버전(HP·처치·피해·안전거리
    /// 포화·포위 문턱)으로 되돌리고 여기서부터 다시 발전시키기로 했다. 행동 생성 순서
    /// (ActionGenerator)와 반환 후보 개수(BeamSearch) 등 계약의 다른 부분은 그대로 둔다 —
    /// 이번 롤백은 평가 쪽에 한정한다.
    ///
    /// 사망 후보 점수는 그대로 유한값(PredictionScoreConfig.PlayerDeath)을 쓴다 — 이건
    /// 전멸 폴백에서 "가장 덜 나쁜" 후보를 고를 수 있게 해준 개선이라 이번 회귀와 무관하다.
    /// </summary>
    public static class ThreatEvaluator
    {
        public static ScoreBreakdown Score(
            in SimWorld world,
            int killCountNormal, int killCountMid, int damageDealt, int hitsTaken,
            bool isRepeatedAction)
        {
            int killCount = killCountNormal + killCountMid;
            var s = new ScoreBreakdown
            {
                kill = killCount * PredictionScoreConfig.KillWeight + damageDealt * PredictionScoreConfig.DamageWeight,
            };

            if (!world.player.alive)
            {
                s.safety = PredictionScoreConfig.PlayerDeath;
                return s;
            }

            s.safety = world.player.health * PredictionScoreConfig.HpWeight
                     + SafetyBonus(in world) * PredictionScoreConfig.SafeDistanceWeight
                     - SurroundedExcess(in world) * PredictionScoreConfig.SurroundedWeight;
            // difficulty는 이번 롤백에서 비움(계약의 대시 보존/반복 페널티가 회귀 원인이라 제외).
            return s;
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
            if (!anyAlive) return PredictionScoreConfig.SafeDistanceCap;
            return Mathf.Min(nearest, PredictionScoreConfig.SafeDistanceCap);
        }

        static int SurroundedExcess(in SimWorld world)
        {
            int count = 0;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                if (CombatMath.FlatDistance(world.player.pos, enemy.pos) <= PredictionScoreConfig.SurroundedRadius)
                    count++;
            }
            return Mathf.Max(0, count - PredictionScoreConfig.SurroundedTolerance);
        }
    }
}
