using UnityEngine;
using UnityEngine.AI;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// 평상시 길찾기 = NavMesh.CalculatePath → 다음 코너. 연속 메시라 기둥·벽 우회.
    ///
    /// 층이동 개편(docs/shared/층이동_개편_설계.md):
    ///  - 층 전환은 <b>기하 자동판정이 아니라 마커(NavMeshLink)</b>가 담당한다 → DropDetect 폐기.
    ///  - 게이팅은 <b>몹별 areaMask</b>로. 못 쓰는 링크는 경로 계산 단계에서 원천 배제된다.
    ///  - "지금 마커 링크에 진입하는가"는 <b>코너 좌표 매칭</b>으로 판정한다.
    ///    NavMeshPath가 corners/status만 노출해 링크 메타데이터가 <b>원천적으로 없기 때문</b>이다.
    ///    (실측: 링크 구간은 시작점→끝점이 연속 코너 쌍으로 나오고, 출발점과 겹치면 좌표가 중복된다.)
    /// </summary>
    public class NavMeshPathfinder : IPathfinder
    {
        /// <summary>Bake가 채워 주는 마커 링크 표(진입 판정·탄도 파라미터 조회용).</summary>
        public ArenaNavLink[] links = System.Array.Empty<ArenaNavLink>();

        /// <summary>큰 상승 링크에 붙는 Area 이름. 이 Area는 Traversal 특성 몹만 통과한다.</summary>
        public const string AreaLeapTraversal = "LeapTraversal";
        static int restrictedArea = -2;   // -2 = 아직 조회 안 함, -1 = 프로젝트에 없음

        const float SampleRadius = 4f;
        const float MatchEpsilon = 0.35f;   // 코너 ↔ 마커 출발점 좌표 일치 허용 오차(m)

        readonly NavMeshPath path = new NavMeshPath();

        public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
        {
            var step = new PathStep { kind = MoveKind.None, next = to, currentNodeId = -1,
                nextNodeId = -1, destinationNodeId = -1, linkId = -1, floorId = -1, destinationFloorId = -1 };
            int areaMask = AreaMaskFor(agentMask);
            if (!Calc(from, to, areaMask)) return step;

            Vector3[] c = path.corners;
            if (c.Length < 1) return step;

            // 연속 중복 코너 병합 — 출발점과 링크 시작점이 겹치면 같은 좌표가 두 번 나온다(실측).
            int nextIdx = 1;
            while (nextIdx < c.Length && (c[nextIdx] - c[0]).sqrMagnitude < 1e-4f) nextIdx++;
            Vector3 nc = nextIdx < c.Length ? c[nextIdx] : c[c.Length - 1];

            step.next = nc;
            step.traversalStart = from;

            // ── 마커 진입 판정 ──
            int li = FindLinkStartingNear(nc, agentMask);
            if (li >= 0)
            {
                ArenaNavLink l = links[li];
                step.kind = l.traversalType == NavTraversalType.JumpUp ? MoveKind.JumpUp : MoveKind.Drop;
                step.traversalStart = l.traversalStartPosition;   // 발판까지 걸어간 뒤 도약
                step.next = l.landingPosition;
                step.linkId = l.linkId;
                step.traversalTicks = l.traversalTicks;
                step.clearance = l.clearance;
                step.gravity = l.gravity;
                step.pauseTicks = l.pauseTicks;
                step.recoverTicks = l.recoverTicks;
                return step;
            }

            step.kind = MoveKind.Walk;
            return step;
        }

        /// <summary>좌표가 어떤 마커 링크의 출발점과 (오차 내) 일치하고 그 몹이 쓸 수 있으면 그 인덱스.</summary>
        int FindLinkStartingNear(Vector3 p, int agentMask)
        {
            float bestSq = MatchEpsilon * MatchEpsilon;
            int best = -1;
            for (int i = 0; i < links.Length; i++)
            {
                if ((links[i].agentMask & agentMask) == 0) continue;
                float sq = (links[i].traversalStartPosition - p).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        /// <summary>
        /// 몹 기동타입(agentMask) → 통과 가능한 NavMesh areaMask.
        /// Traversal이 아니면 큰 상승 링크(Area = LeapTraversal)를 배제 → NavMesh가 알아서 우회한다.
        /// 게이팅이 <b>경로 계산 단계</b>에서 끝나므로 실행 중 재검사가 필요 없다.
        /// </summary>
        static int AreaMaskFor(int agentMask)
        {
            if ((agentMask & (1 << (int)MobilityType.Traversal)) != 0) return NavMesh.AllAreas;
            if (restrictedArea == -2) restrictedArea = NavMesh.GetAreaFromName(AreaLeapTraversal);
            return restrictedArea > 0 ? (NavMesh.AllAreas & ~(1 << restrictedArea)) : NavMesh.AllAreas;
        }

        public int FloorIdAt(Vector3 position) => -1;

        bool Calc(Vector3 from, Vector3 to, int areaMask)
        {
            // 시작점이 폴리곤 모서리에 걸리면 질의가 실패할 수 있어(실측) 항상 면 위로 당긴다.
            if (!NavMesh.SamplePosition(from, out var f, SampleRadius, areaMask)) return false;
            if (!NavMesh.SamplePosition(to,   out var t, SampleRadius, areaMask)) return false;
            return NavMesh.CalculatePath(f.position, t.position, areaMask, path);
        }

        /// <summary>가장 가까운 navmesh 점(반경 안). navmesh는 에이전트 반경만큼 가장자리가 깎여 있어
        /// 이 점으로 당기면 몹이 얇은 다리 밖으로 안 나간다.</summary>
        public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
        {
            if (NavMesh.SamplePosition(pos, out var hit, maxDist, NavMesh.AllAreas))
            { onMesh = hit.position; return true; }
            onMesh = pos; return false;
        }
    }
}
