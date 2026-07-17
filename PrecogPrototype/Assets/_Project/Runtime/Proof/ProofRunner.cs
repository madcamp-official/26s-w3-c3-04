using UnityEngine;
using Game.Prediction;

namespace Game.Runtime
{
    /// <summary>
    /// 세 가지 증명을 실행하고 콘솔에 찍는다. 이게 묶음 A의 목표다.
    /// UI 없이 Console 창의 숫자로 "이 프로젝트를 잡아도 되는가"를 판정한다.
    /// </summary>
    public static class ProofRunner
    {
        public static void RunAll()
        {
            Debug.Log("========== 프로토타입 증명 시작 ==========");
            RunBenchmark();
            RunDeterminism();
            RunMiniSearch();
            Debug.Log("========== 증명 끝 ==========");
        }

        public static void RunBenchmark()
        {
            var r = Benchmark.Run(enemyCount: 12);
            Debug.Log(
                $"[벤치마크] Step 1회 = {r.usPerStep:F3}us " +
                $"(적 {r.enemyCount}, {r.iterations}회 평균)\n" +
                $"  → 5초 예지 탐색(32,400틱) 추정: {r.projected5sMs:F1}ms " +
                $"{Verdict(r.projected5sMs)}\n" +
                $"  ※ LOS Raycast 미포함 — 실제 비용은 이보다 늘 수 있음(에디터 값은 빌드보다 느림)");
        }

        public static void RunDeterminism()
        {
            var r = DeterminismCheck.Run(enemyCount: 12, ticks: 300);
            if (r.passed)
                Debug.Log($"[결정론] 통과 ✓ {r.ticks}틱 전부 해시 일치 " +
                          $"(final={r.finalHashA:X16})");
            else
                Debug.LogError($"[결정론] 실패 ✗ {r.divergedAtTick}틱에서 갈라짐 " +
                               $"(A={r.finalHashA:X16} B={r.finalHashB:X16})");
        }

        public static void RunMiniSearch()
        {
            var r = MiniSearch.Run(enemyCount: 12, depthTicks: 180);
            Debug.Log(
                $"[미니탐색] 후보 {r.candidates}개 × 깊이 {r.depthTicks}틱(3초) " +
                $"= {r.elapsedMs:F2}ms\n" +
                $"  → 최적 후보 #{r.bestCandidate} (점수 {r.bestScore:F2})\n" +
                $"  ※ 복제→가상 시뮬→평가가 실제로 동작함을 증명");
        }

        static string Verdict(double ms)
        {
            if (ms < 200)  return "✓ 여유";
            if (ms < 700)  return "△ 아슬아슬(단타/축소로 대응)";
            return "✗ 위험(구조 재검토 필요)";
        }
    }
}
