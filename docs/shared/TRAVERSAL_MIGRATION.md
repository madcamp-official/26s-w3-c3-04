# 하강·상승(층이동 기동) — rebuild 대비 변경점 정리

작성일: 2026-07-18
기준: `rebuild`(45f3602 + 로컬 작업) ↔ `origin/prediction-foundation`(e367cb8)
관련 문서: `docs/shared/ENEMY_SYSTEM.md` 6장(나브 링크), `docs/shared/PREDICTION_INTEGRATION_PLAN.md` 7장(Navigation 교체)

## 0. 문서 목적

rebuild의 하강(테두리 낙하) 구현은 prediction-foundation의 그래프 기반 구조로 대체된다.
또한 상승(부스터)이 신규로 추가된다. 이 문서는 **무엇이 버려지고, 무엇이 유지되고,
무엇이 옮겨지는지**를 코드 수준에서 정리해, 마이그레이션 시 누락·중복 작업을 막는다.

---

## 1. 현재 rebuild 방식 (기준선)

### 데이터
- `MapBuilder`가 `MapResult.drops`에 **(edge, landing) 점 쌍**을 authoring
  (현재 3층 아레나 v2 기준 12쌍).
- `NavMeshPathfinder`(Bridge)가 이 목록을 보관하고 `NearestDropEdge`로 노출.

### 판단 — 적 자체 판단 (핵심 특징)
`EnemyMovement`가 매번 스스로 비교한다:

```text
walk     = PathLength(적, 플레이어)          ← NavMesh.CalculatePath
toEdge   = PathLength(적, 가장 가까운 테두리)  ← NavMesh.CalculatePath
fromLand = PathLength(착지점, 플레이어)       ← NavMesh.CalculatePath
jump = toEdge + 수평거리(테두리→착지) + fromLand

조건: 적이 플레이어보다 DescentMinHeight(2) 이상 높고,
      (걷는 길 없음 || jump < walk × DescentThreshold(0.8)) 이면 하강
```

- 판정 1회 = `CalculatePath` 3회. 이 방식이 생긴 이유:
  **NavMesh.CalculatePath가 NavMeshLink를 경로에 태우지 않는 것을 검증으로 확인**
  → NavMesh에 맡길 수 없어 직접 비교하는 우회를 만들었다.

### 실행 — 순간이동식
```text
ApproachEdge(테두리로 걸어감)
→ EdgePause(12틱 멈칫, 예고)
→ 순간이동: e.pos = descentLanding  ← 착지점에 id별 분산 오프셋(LandingSpread 1.2)
→ Recovery(15틱 착지 회복)
```
- Sim 상에서 공중 구간이 **존재하지 않는다**(한 틱에 점프). 화면은 View 보간이
  떨어지는 것처럼 보여줄 뿐이다. → **낙하 중 피격 판정 불가**.
- `SimStep.Separate`는 `descentPhase != None`인 적 전체를 분리에서 제외.

### 상승
- **없음.** 올라가는 수단은 경사로 걷기(NavMesh) 뿐.

---

## 2. prediction-foundation에서 달라지는 점

### 2.1 판단 주체가 이동한다 — 가장 큰 변화

자체 비교 로직이 **통째로 삭제**된다. 대신:

- 수동 authoring된 노드 그래프에서 **Drop 링크가 1급 엣지**다.
- 경로표(`nextNodeTable`/`distanceTable`) 최단경로가 Drop 링크를 자연스럽게 태운다.
- `IPathfinder.NextStep(from, to)`이 `PathStep{ kind, next, nodeId들, linkId }`를 반환하고,
  적은 **`kind == MoveKind.Jump`이면 하강을 실행**할 뿐이다.

```text
rebuild:            적이 "돌아갈까 vs 떨어질까"를 매번 계산 (CalculatePath 3회)
prediction-found.:  경로표가 이미 답을 안다 (표 조회 1회, 다음 홉이 Jump인지만 확인)
```

rebuild의 자체 판단은 "NavMesh가 링크를 못 태우는" 문제의 **우회책**이었다.
자기 그래프에서는 그 문제가 없으므로 우회책도 함께 사라진다.

### 2.2 인터페이스 교체 (`IPathfinder`)

| rebuild | prediction-foundation |
|---|---|
| `bool NextCorner(from, to, out corner)` | `PathStep NextStep(from, to)` 하나로 통합 |
| `float PathLength(from, to)` | 삭제 (경로표가 내부 보유) |
| `bool NearestDropEdge(pos, out edge, out landing)` | 삭제 (Drop = 그래프 링크) |

- 구현체: `NavMeshPathfinder`(런타임 NavMesh) → `GraphPathfinder`(고정 그래프 + 사전계산 표).
- NavMesh는 제작·검증 도구로만 남는다.

### 2.3 실행 변화 — 순간이동 → 포물선 낙하

prediction-foundation의 `StepDescent`:

```text
EdgePause(멈칫, 예고)
→ Airborne: jumpStart→jumpEnd를 Lerp + sin 아치(DescentJumpArcHeight)로
            매 틱 위치 갱신. 소요틱 = 수평거리/속도 (최소 DescentJumpMinTicks)
→ Recovery(착지 회복)
```

의미 변화:
- Sim이 **낙하 중 실제 공중 위치를 가진다** → **공중 피격 판정이 가능**해진다
  (rebuild 순간이동식에서는 원천 불가였음).
- View는 특별한 연출 없이 Sim 위치를 그대로 보간하면 된다.
- `Separate` 제외 조건이 `descentPhase != None`(전 단계) → `Airborne`(공중만)으로 좁아짐.

### 2.4 상태 필드 교체 (`EnemySim`)

| rebuild | prediction-foundation |
|---|---|
| `descentEdge` (걸어갈 테두리) | 삭제 (그래프 노드가 대체) |
| `descentLanding` (+ id별 분산) | `jumpStart`, `jumpEnd`, `jumpDuration` |
| `DescentPhase.ApproachEdge` | 삭제 (일반 Approach 이동이 테두리 노드로 걸어감) |
| — | `currentNavNodeId`, `destinationNavNodeId`, `nextNavNodeId`, `activeTraversalLinkId` |
| — | `aiState`: `Traversal` / `Falling` / `LandingRecovery` |

### 2.5 사라지는 것 목록

- `SimConfig.DescentThreshold`, `DescentMinHeight`, `DescentEdgeReach`,
  `DescentLandingSpread` (자체 판단·순간이동 전용 상수)
- `EnemyMovement`의 하강 판단 블록 전체 (PathLength 3회 비교)
- `NavMeshPathfinder.NearestDropEdge` / `PathLength`
- **착지 분산(id별 오프셋)**: prediction-foundation `StartDescent`에는 없다.
  여러 몹이 같은 드롭을 쓰면 같은 점에 착지 → 5장 열린 질문.
- `EntityViews`의 `ApproachEdge` 색 상태 (aiState 기반 색으로 재매핑 필요)

---

## 3. 상승(부스터) — 신규 설계 (어느 브랜치에도 없음)

`ENEMY_SYSTEM.md` 2.4·6장에서 확정: 층이동몹의 상행 수단 = **부스터로 쭉 올라감**,
하강과 대칭 구조.

### 3.1 그래프 표현
```text
Booster 링크: 부스터 노드(아래층) → 상층 노드, 일방(oneWay), 점 → 점
```
Drop 링크와 완전히 대칭. 경로표가 자동으로 태운다.

### 3.2 실행 (하강 StepDescent와 대칭)
```text
부스터 노드로 걸어감 (일반 Approach)
→ LaunchPause(멈칫, 예고 — EdgePause 대응)
→ Rising: jumpStart→jumpEnd 보간 상승 (아치는 불필요하거나 약하게 — 수직 위주)
→ Recovery(착지 회복 재사용)
```
- `jumpStart`/`jumpEnd`/`jumpDuration` 필드를 **그대로 재사용**한다 (신규 필드 최소화).
- 상승 중에도 Airborne처럼 공중 위치가 실재 → 공중 피격 가능.

### 3.3 필요한 코드 확장

1. **`MoveKind` 확장**: 현재 `None | Walk | Jump` → **`Boost` 추가** 권장.
   - 대안: `Jump` 재사용 + `PathStep.linkId`로 링크 타입 조회. 그러나 적 코드가
     링크 테이블을 다시 뒤져야 하므로, kind에 명시하는 쪽이 단순하다.
2. `EnemyMovement`(또는 후속 기동 모듈)에 Rising 분기 추가.
3. `DescentPhase` 명명이 상승과 안 맞음 → `TraversalPhase`(Pause/Airborne/Recovery)로
   개명 검토 (하강·상승 공용 상태머신).
4. 상승 속도·예고 틱 등 config 추가 (`DescentJumpSpeed` 대응).

---

## 4. 마이그레이션 체크리스트

### 버린다
- [x] `EnemyMovement` 하강 자체 판단 블록 (PathLength 3회 비교)
- [ ] `NearestDropEdge` / `PathLength` (인터페이스·구현 모두)
- [ ] `DescentThreshold` / `DescentMinHeight` / `DescentEdgeReach` / `DescentLandingSpread`
- [x] 순간이동 실행 (`e.pos = descentLanding`)
- [ ] `DescentPhase.ApproachEdge` 상태와 `EntityViews`의 해당 색

### 유지한다 (개념)
- [x] 예고(멈칫) → 이동 → 회복의 3박자 텔레그래프 구조
- [ ] EdgePause/Recovery 틱 상수 (이름 유지 또는 Traversal로 개명)
- [x] "드롭·부스터는 점 → 점의 authoring된 쌍" 원칙

### 옮긴다
- [ ] `MapBuilder`의 drops 12쌍 → 그래프의 DropStart/DropLanding 노드 + Drop 링크로 이전
      (authoring 데이터는 버려지지 않는다 — 표현만 바뀜)
- [ ] 부스터 위치 authoring → Booster 노드 + 링크 (ENEMY_SYSTEM TBD)

### 새로 등록한다 (계약 6조)
- [x] `WorldHash`: `jumpStart`/`jumpEnd`/`jumpDuration`, navNode id 4종, `aiState`
- [x] `Snapshot.CopyTo`: EnemySim은 배열 통복사라 필드 추가만으로 충분 — 확인만
- [ ] `DeterminismTests` 초록 유지 (180틱 × 100회 해시 일치)

---

## 5. 열린 질문

1. **착지 분산 재도입 여부** — rebuild의 id별 오프셋이 사라져, 여러 몹이 같은
   드롭/부스터를 연달아 쓰면 같은 점에 착지해 뭉친다. Separate가 밀어내긴 하나
   착지 순간 겹침이 보기 싫으면 결정론적 오프셋(id 기반)을 그래프 실행부에 재도입.
2. **`MoveKind.Boost` vs `Jump` 재사용** — 3.3의 1번. Boost 신설 권장.
3. **`DescentPhase` → `TraversalPhase` 개명 시점** — 상승 구현과 동시에 할지,
   하강 마이그레이션 때 미리 할지.
4. **부스터 위치 authoring** — 맵 확정 후 (ENEMY_SYSTEM.md 7장 TBD와 동일 항목).
