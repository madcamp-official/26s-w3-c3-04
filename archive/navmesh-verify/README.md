# NavMesh 검증 (아카이브)

`rebuild` 브랜치 재설계 전, "커스텀 시뮬 + 유니티 질의" 구조가 성립하는지 검증한 코드.
**Unity 프로젝트 밖에 둔다** — `Assets/` 안에 있으면 `RuntimeInitializeOnLoadMethod`
자동실행이 매 Play마다 돌아 실제 게임과 충돌하기 때문.

## 무엇이 증명됐나 (2026-07-18)

- **NavMesh.CalculatePath 결정론** — 같은 입력 200회 → 코너 배열 해시 완전 동일.
  세션(재실행)을 넘어서도 동일. → 예측과 실제의 경로가 안 어긋남.
- **벽 돌아가기** — 장애물 있으면 코너가 늘며 우회.
- **램프로 층 연결** — PathComplete. 수직 구조(1층↔발판)를 NavMesh가 이음.
- **다리 아래 걷기** — 천장 높이 > 에이전트 키면 오버패스 아래도 walkable.
  즉 걷는 영역은 몹 크기(에이전트 키/반지름)에 좌우됨. Agent Type으로 몹별 분리 가능.

## 재사용할 교훈 (Bridge 구현 시)

1. **런타임 NavMesh 굽기**: `com.unity.ai.navigation`의 `NavMeshSurface` →
   `collectObjects=All`, `useGeometry=RenderMeshes`, `BuildNavMesh()`.
2. **경로 질의**: `NavMesh.CalculatePath(from, to, AllAreas, path)` — 결정론적.
   시작/끝은 `NavMesh.SamplePosition`으로 NavMesh 위에 스냅.
3. **★ Physics.SyncTransforms() 함정**: 코드로 방금 만든/옮긴 콜라이더는 다음 물리
   프레임 전까지 `Physics.Raycast`/`CapsuleCast`에 반영 안 됨(autoSyncTransforms off).
   같은 프레임에 질의하려면 **질의 전에 `Physics.SyncTransforms()`**. 안 하면 전부 빗나감.
4. 기본 에이전트: 반지름 0.5, 키 2, 오를 수 있는 턱 0.75, 최대경사 45°.
