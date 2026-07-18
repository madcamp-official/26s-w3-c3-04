# 예측 시스템 최적화 계획

작성일: 2026-07-18  
대상: 적 8~64마리 환경에서 2~3초 미래 예측  
목표: 예측 정확성을 유지하면서 게임 내 허용 가능한 시간 안에 후보 경로 생성

관련 문서:

- [전체 개발 통합 계획](PROJECT_MASTER_PLAN.md)
- [예측 시스템 통합 계획](PREDICTION_INTEGRATION_PLAN.md)

---

## 0. 목표와 기본 원칙

```text
총 적: 최대 50~64마리
정밀 위협 적: 최대 8~12마리
동시 공격 적: 최대 4~6마리
예측 길이: 2~3초
Beam: 8~12
평균 행동 후보: 4개 이하
최종 후보: 1~3개
목표 계산 시간: 200ms 내외
하드 리밋: 300ms
GC Alloc: 거의 0
```

> 적을 예측에서 임의로 삭제하지 않는다. 모든 적의 상태는 계속 진행하되, 예측 시간 안에 결과에 영향을 줄 수 있는 적만 비싼 정밀 계산을 수행한다.

AI LOD, 공격 슬롯, 경로 재판단 주기처럼 결과에 영향을 주는 최적화 규칙은 실제 게임과 예측에서 동일하게 사용한다.

---

## 1. 예상 병목

### 런타임 NavMesh

후보마다 반복되는 `NavMesh.SamplePosition`과 `NavMesh.CalculatePath`는 가장 큰 병목 후보이며 예측 루프에서 제거한다.

### Unity Physics

`Physics.CapsuleCast`, `Physics.Raycast`를 모든 적과 모든 후보 틱에 사용하지 않는다.

### 모든 적 쌍 분리

현재 적 분리는 `N × (N - 1) / 2` 쌍을 검사한다.

```text
8마리  → 28쌍
32마리 → 496쌍
50마리 → 1,225쌍
64마리 → 2,016쌍
```

### 스냅샷 할당

후보마다 `EnemySim[]`을 생성하면 GC와 복사 비용이 증가한다.

### 행동 분기

적 50마리를 모두 `Lunge(targetId)` 후보로 만들면 탐색 공간이 폭발한다.

---

## 2. 계산량 기준

초기 설정:

```text
180틱 / 매크로 15틱 / 깊이 12
Beam 12 / 평균 행동 4개
```

대략:

```text
12 × 12 × 4 × 15 = 8,640 월드 틱
8,640 × 적 50마리 = 약 432,000회 적 업데이트
```

단순 구조체 상태 머신 43만 회보다 각 업데이트 안의 NavMesh, Physics, 할당이 더 큰 문제다.

---

## 3. 적 시뮬레이션 등급

```csharp
public enum EnemySimulationTier : byte
{
    Precise,
    Coarse,
    Dormant
}
```

### Precise

대상:

- 공격 Windup/Active 상태
- 공격 범위 근처
- 예측 시간 안에 플레이어를 공격 가능
- 런지 후보가 될 수 있음
- 플레이어 예상 이동 영역과 경로가 겹침
- 같은 또는 인접 Navigation 구역

수행:

- 정확한 공격·이동·LOS
- 공격 거리·각도·높이
- 충돌·분리
- 런지 타깃 평가

상한: 8~12마리.

### Coarse

당장 공격하지 못하지만 예측 시간 안에 가까워질 수 있는 적.

- 정적 Navigation Graph 이동
- 쿨타임·스턴·상태 진행
- 간소화 위치 이동
- 위험 범위 진입 시 Precise 승격
- 매 틱 LOS·정밀 Physics 생략

### Dormant

예측 시간 안에 영향을 줄 수 없는 적.

- 필수 시간 상태만 진행
- 필요 시 Navigation 구간의 간단한 위치 진행
- Physics·LOS·공격 판단 생략
- 영향 가능 상태가 되면 Coarse 승격

### 상한 동률 해소

Precise 후보가 상한을 넘으면:

```text
공격 진행 중
→ 공격까지 남은 틱
→ 플레이어까지 경로 비용
→ 공격 위험도
→ entityId
```

같은 월드 상태에서 항상 같은 등급 집합이 나와야 한다.

---

## 4. 영향 가능 범위

거리만 보지 않고 대시와 런지를 포함한다.

```text
플레이어 최대 이동
= 걷기 최대 거리
 + 가능한 대시 거리
 + 가능한 런지 거리

적 최대 영향 거리
= 적 속도 × 예측 시간
 + 공격 거리
 + 공격 전진 거리
```

직선 거리보다 Navigation Graph 경로 비용을 우선 사용한다.

---

## 5. 동시 공격 슬롯

실제 게임과 예측 양쪽에서 동일하게 적용한다.

```text
동시 공격 허용: 최대 4~6마리
```

우선순위:

```text
이미 공격 상태
→ 공격 가능 조건
→ 공격까지 남은 틱
→ 거리 또는 경로 비용
→ entityId
```

슬롯을 얻지 못한 적은 접근·측면 배치·대기한다. 이는 성능뿐 아니라 난전의 가독성을 위한 게임 규칙이다.

---

## 6. 런지 후보 제한

싼 조건부터 검사한다.

```text
생존
→ 거리
→ 정면 각도
→ 같은 층
→ 그래프 연결
→ LOS
→ 도착점 유효
```

후보 상한:

```text
MVP: 1명
기본: 2명
최종 최대: 3명
```

선택 순서:

```text
정면 각도
→ 처치 가능성
→ 위협도
→ 거리
→ entityId
```

---

## 7. Navigation 최적화

예측 루프에서 다음 호출을 제거한다.

```text
NavMesh.CalculatePath
NavMesh.SamplePosition
```

정적 경로표를 사용한다.

```csharp
nextNodeTable[currentNodeId, destinationNodeId]
distanceTable[currentNodeId, destinationNodeId]
```

매 틱:

- 현재 링크 이동
- 공격 단계
- 낙하
- 스턴

6~15틱마다:

- 목적지 갱신
- 다음 노드 선택
- 시뮬레이션 등급 재평가

---

## 8. Physics 최적화

### Beam Search

- Navigation 구역과 링크로 이동 가능성 판단
- 간단한 점유 셀 또는 AABB
- 먼 적 Physics 생략
- Precise 적만 필요한 충돌 처리

### 런지

시작 순간 한 번만 수행한다.

```text
LOS
전체 구간 CapsuleCast
도착점 CheckCapsule
도착점 Ground 검사
```

성공하면 타깃과 도착점을 상태에 고정한다.

### 최종 후보

상위 3~6개만 실제 `CharacterMotor + ICollision`로 60Hz 정밀 재시뮬레이션한다. 실패 후보는 다음 순위로 교체한다.

---

## 9. 적 분리 최적화

초기에는 Precise 적끼리만 정밀 분리한다.

적 32마리 이상에서 `SimStep.Separate`가 병목이면 Spatial Hash를 도입한다.

```text
셀 크기: 적 지름의 2~4배
현재 셀 + 주변 8개 셀만 검사
```

결정론 조건:

- 셀 ID 순서 고정
- 셀 내부 `entityId` 오름차순
- Dictionary 열거 순서에 의존하지 않음
- 고정 배열 또는 정렬 인덱스 사용

---

## 10. 메모리와 할당

금지:

- 후보마다 `new SimWorld`
- 후보마다 `new EnemySim[]`
- LINQ와 JSON 복사
- GameObject 생성
- `Instantiate` / `Destroy`
- 탐색 후보마다 전체 `PredictedFrame` 저장

사용:

- `WorldBufferPool`
- `SearchNodePool`
- 고정 크기 적·행동·피해 배열
- 명시적 `CopyTo`
- 최종 후보만 프레임 기록

Beam 12, 행동 4 기준 약 70~100개 월드 버퍼로 시작한다.

---

## 11. 상태 중복 제거

초기 양자화:

```text
위치: 0.5m
속도 방향: 8방향
yaw: 15~30도
쿨타임: 6~15틱 단위
```

키에 포함:

- 플레이어 위치·속도·HP
- 대시 가능 여부
- 런지 쿨타임
- 생존 적 집합
- Precise 적의 공격 단계
- 주변 위협 서명

실제로 다른 위험 상태를 과도하게 병합하지 않는다.

---

## 12. 시간 예산과 동적 축소

```text
목표: 200ms
하드 리밋: 300ms
프레임 분할: 프레임당 2~4ms
```

시간 부족 시:

1. 후보 3개 → 1개
2. Beam 12 → 8 → 6
3. 런지 후보 2명 → 1명
4. 예측 3초 → 2초
5. Precise 상한 12명 → 8명
6. 점프·벽타기 탐색 제외

이미 공격 Windup/Active인 적은 Precise에서 제외하지 않는다.

---

## 13. 프로파일링 지표

예측마다 출력:

```text
Total prediction time
World copy time
Action generation time
Enemy simulation time
Navigation time
Physics time
Evaluation time
Deduplication time
Final resimulation time

Expanded nodes
Simulated world ticks
Precise / Coarse / Dormant updates
Path queries
Physics queries
Duplicate states removed
Dead candidates removed
Final valid candidates
GC Alloc
```

Profiler Marker:

```text
Prediction.CopyWorld
Prediction.GenerateActions
Prediction.Simulate
Prediction.EnemyPrecise
Prediction.EnemyCoarse
Prediction.Navigation
Prediction.Physics
Prediction.Evaluate
Prediction.Deduplicate
Prediction.FinalResimulation
```

---

## 14. 벤치마크

### 단일 경로

```text
180틱
적 8 / 16 / 32 / 50 / 64마리
```

### 탐색 목표

| 적 수 | Beam | 예측 | 목표 |
| ---: | ---: | ---: | ---: |
| 8 | 6 | 2초 | 100ms 이하 |
| 16 | 8 | 3초 | 150ms 내외 |
| 32 | 12 | 3초 | 200ms 내외 |
| 50 | 12 | 3초 | 200~300ms |
| 64 | 8 | 2초 | 300ms 이하 |

### 최악 상황

- 6명이 동시에 Windup
- 플레이어 주변 적 12명 밀집
- 런지 후보 여러 명
- Drop Link 근처
- 여러 적이 같은 Navigation 목적지를 선택
- 같은 틱에 스턴·사망·등급 변경

---

## 15. 정확성 테스트

같은 스냅샷에서 다음이 일치해야 한다.

- Precise/Coarse/Dormant 분류
- 공격 슬롯 ID 목록
- 런지 후보 ID 목록
- 다음 Navigation Node
- 공격 시작 틱과 방향
- HP·스턴·사망
- 최종 WorldHash

반복:

```text
적 8 / 16 / 32 / 50마리
각각 동일 입력 180틱
최소 100회
```

---

## 16. 구현 순서

### O1 측정

- Profiler Marker
- 적 수별 180틱 벤치마크
- Snapshot GC 측정
- NavMesh·Physics·분리 비중 확인

### O2 필수 저비용 개선

- Snapshot 할당 제거
- 월드 버퍼 풀
- 런지 후보 1~2명 제한
- 상황별 행동 생성

### O3 Navigation

- 정적 Node/Link
- 경로표 사전 계산
- 런타임 `NavMesh.CalculatePath` 제거

### O4 적 AI 비용

- 공격 슬롯 4~6명
- 판단 6~15틱 주기
- Precise/Coarse/Dormant
- 영향 가능 범위

### O5 Physics

- 런지 쿼리 1회화
- 탐색 간이 충돌
- 최종 후보 정밀 검증

### O6 밀집 상황

- `SimStep.Separate` 측정
- 필요 시 Spatial Hash

### O7 동적 품질

- 200ms 목표
- 300ms 하드 리밋
- 초과 시 Beam·길이·후보 수 축소

---

## 17. 완료 체크리스트

- [ ] 예측 루프에서 `NavMesh.CalculatePath` 호출 없음
- [ ] 후보 확장 중 GameObject 생성 없음
- [ ] 후보 확장 중 월드 배열 신규 할당 없음
- [ ] 런지 후보 최대 수 고정
- [ ] Precise 적 최대 수 고정
- [ ] 공격 슬롯 최대 수 고정
- [ ] 이미 공격 중인 적은 항상 Precise
- [ ] 등급 승격 규칙 결정론적
- [ ] 실제 게임과 예측이 같은 AI LOD 규칙 사용
- [ ] 적 50마리 벤치마크 존재
- [ ] 최악 밀집 상황 벤치마크 존재
- [ ] GC Alloc 거의 0
- [ ] 최종 후보 정밀 재검증
- [ ] 100회 반복 WorldHash 일치

---

## 18. 완료 판단

다음 조건이면 수십 마리 예측을 지원한다고 판단한다.

```text
모든 적의 기본 상태는 계속 진행
정밀 위협 적은 8~12명
동시 공격은 4~6명
런지 후보는 1~2명
경로는 정적 테이블 조회
Physics는 정밀 대상에만 제한
최종 후보는 실제 규칙으로 재검증
3초 탐색이 300ms 하드 리밋 안에 완료
```

현재의 런타임 NavMesh, 모든 적 정밀 Physics, 모든 적 쌍 분리를 후보마다 그대로 실행하는 방식은 수십 마리 예측의 최종 구조로 사용하지 않는다.
