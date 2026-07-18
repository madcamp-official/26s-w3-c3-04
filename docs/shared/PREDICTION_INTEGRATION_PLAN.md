# 전체 예측 시스템 통합을 위한 변경 계획

작성일: 2026-07-18  
대상 프로젝트: `PrecogPrototype`  
기준 브랜치 조사 시점: `rebuild` (`45f3602`)

관련 문서:

- [전체 개발 통합 계획](PROJECT_MASTER_PLAN.md)
- [예측 시스템 최적화 계획](OPTIMIZATION.md)

## 0. 문서 목적

현재 프로젝트의 `Game.Sim / Game.Bridge / Game.View` 구조 위에 다음 전체 루프를 안전하게 올리기 위해 필요한 변경 사항을 정리한다.

```text
예지 발동
→ 월드 정지 및 스냅샷
→ 미래 행동 탐색
→ 후보 경로 평가
→ 잔상·경로·액션 이벤트 표시
→ 후보 선택
→ 자동 재생과 리듬 입력
→ 완료 또는 Miss 복귀
```

이 문서의 우선순위는 다음과 같다.

1. 예측과 실제 결과의 일치
2. 3초 예측을 게임 내에서 실행 가능한 성능
3. 전투 규칙의 명확성
4. 후보 경로의 다양성과 연출

---

## 1. 이번 통합에서 전제로 삼을 전투안

### 플레이어 행동

- 일반 이동
- 더블 점프
- 4방향 대시
- 근접 평타
- 우클릭 타깃 런지

### 제거 대상

- 막기
- 가드 게이지
- 칼등치기
- 기존 복합 질풍참

### 4방향 대시

- 카메라 기준 `Forward / Backward / Left / Right`
- 이동 전용
- 피해 없음
- 1회 충전 권장
- 약 1초 내외 쿨타임에서 시작
- 시작 순간 방향 고정

### 타깃 런지

- 우클릭으로 발동
- 일정 거리 이내 정면 적만 대상
- 시야가 확보된 같은 층 적만 대상으로 시작
- 타깃 선정 순서: 화면 중앙과의 각도 → 거리 → `entityId`
- 시작 순간 `targetId`와 도착 위치 고정
- 타깃 앞 유효 지점으로 빠르게 이동
- 피해 1과 짧은 스턴
- 무적 없음
- 약 2초 내외 쿨타임에서 시작

### 기본 처치 흐름

```text
런지 피해 1 + 스턴
→ 평타 피해 1
→ 일반 적 처치
```

이 전투안은 아직 코드와 기존 ADR에 완전히 반영되지 않았다. 구현 전에 GDD와 기존 전투 ADR을 갱신해야 한다.

---

## 2. 현재 프로젝트에서 유지할 기반

다음 구조는 예측 시스템의 기반으로 유지한다.

- 60Hz 고정 틱
- `SimStep.Run(ref SimWorld, in InputCmd, in SimServices)` 단일 시뮬레이션 진입점
- `Sim / Bridge / View` 어셈블리 분리
- 값 타입 중심의 플레이어·적 상태
- GameObject가 시뮬레이션 결과만 표현하는 구조
- 플레이어와 적의 키네마틱 이동
- 적 배열의 고정 순서
- `entityId` 기반 겹침 동률 해소
- `Snapshot`과 `WorldHash`
- 정적 맵과 예지 중 스폰 잠금 원칙

표현 계층의 사운드, 카메라 흔들림, 사지절단에 사용하는 랜덤은 시뮬레이션 결과에 영향을 주지 않는 한 허용한다.

---

## 3. 확정된 공통 결정

### D1. 재생 방식 — 월드 전체 상태 궤적

ADR-0003에 따라 탐색 중 저장한 매 틱 `SimWorld` 전체 상태를 재생에 적용한다.

- 플레이어·적·전투 상태를 함께 적용한다.
- 플레이어만 궤적 적용하고 적만 다시 계산하는 혼합 방식은 금지한다.
- Miss가 발생한 틱까지 적용된 `SimWorld`를 실제 월드로 채택하고 일반 조작으로 복귀한다.
- `InputCmd`는 탐색과 액션 이벤트 생성에 저장하지만 재생 위치를 다시 계산하는 용도로 사용하지 않는다.
- 같은 스냅샷에서 같은 후보 궤적이 생성되는지는 WorldHash 반복 테스트로 검증한다.

### D2. 적 체력

일반 적 HP 2, 중형 적 HP 3을 유지한다. 예측기는 적별 HP와 스턴을 모든 후보 상태에 포함한다. MVP는 일반 적부터 구현한다.

### D3. 런지의 이동 처리

Sim에서 5틱 동안 시작점과 확정 도착점 사이의 고정 궤적으로 이동한다. View는 Sim 상태 사이를 보간한다. 시작 순간 타깃과 도착점을 고정한다.

---

## 4. 시뮬레이션 데이터 계약 변경

### 4.1 `InputCmd`

현재 `block`을 제거하고 런지 입력과 대상을 추가한다.

```csharp
public struct InputCmd
{
    public Vector2 move;
    public float yaw;

    public bool jump;
    public bool dash;
    public DashDirection dashDirection;

    public bool attack;
    public bool lunge;
    public int lungeTargetId;
}
```

일반 플레이의 타깃 선택과 예측 재생의 타깃 선택이 같은 `lungeTargetId` 계약을 사용해야 한다.

### 4.2 `PlayerSim`

추가할 최소 상태:

```csharp
public int health;
public bool alive;
public int invulnerabilityTicks;
public int hitStunTicks;

public int dashCooldownTicks;
public DashDirection committedDashDirection;
```

플레이어가 죽었는지, 몇 대 맞았는지, 대시가 가능한지를 `SimWorld`만 보고 판단할 수 있어야 한다.

### 4.3 `PlayerCombatState`

막기·가드·칼등치기 상태를 제거하고 평타와 런지 상태로 교체한다.

```csharp
public enum PlayerActionPhase : byte
{
    None,
    AttackWindup,
    AttackActive,
    AttackRecovery,
    LungeWindup,
    LungeTravel,
    LungeRecovery
}

public struct PlayerCombatState
{
    public PlayerActionPhase phase;
    public int phaseTicks;

    public int attackCooldownTicks;

    public int lungeCooldownTicks;
    public int lungeTargetId;
    public Vector3 lungeStart;
    public Vector3 lungeDestination;
    public int lungeTravelTicks;
    public int lungeElapsedTicks;

    public int hitSequence;
}
```

### 4.4 `EnemySim`

현재 추격·하강 상태만으로는 위험 예측을 할 수 없다. 다음을 추가한다.

```csharp
public enum EnemyAIState : byte
{
    Idle,
    Approach,
    AttackWindup,
    AttackActive,
    AttackRecovery,
    Stunned,
    Traversal,
    Falling,
    LandingRecovery,
    Dead
}
```

필요 필드:

```csharp
public int enemyTypeId;
public EnemyAIState aiState;
public int stateTicks;

public int attackCooldownTicks;
public int attackTargetId;
public Vector3 committedAttackDirection;

public int currentNavNodeId;
public int destinationNavNodeId;
public int nextNavNodeId;
public int activeTraversalLinkId;

public bool hasLineOfSight;
public float horizontalDistanceToTarget;
public float verticalDistanceToTarget;
```

### 4.5 `SimWorld`

추가할 최소 상태:

```csharp
public bool spawnLocked;
public int waveId;
public int mapVersion;

public DamageEvent[] pendingDamage;
public int pendingDamageCount;
```

원거리 적을 도입한다면 다음도 필요하다.

```csharp
public ProjectileSim[] projectiles;
public int projectileCount;
```

난수 없는 적 AI를 확정한다면 `rngState`와 `DetRng`는 Sim 계약에서 제거한다.

---

## 5. 전투 시스템 완성

예측을 시작하기 전에 다음 전투 상황이 모두 `SimStep.Run`만으로 실행되어야 한다.

### 5.1 평타

```text
Windup
→ Active
→ Recovery
```

판정에 포함할 값:

- 공격 거리
- 정면 각도
- 높이 허용치
- 공격별 중복 타격 방지
- 적 HP 및 스턴

### 5.2 타깃 런지

```text
정면 적 탐색
→ targetId 고정
→ 도착 위치 검증 및 고정
→ 이동
→ 도착 틱 피해
→ Recovery
```

런지 가능 조건:

- 적 생존
- 최소·최대 거리 안
- 정면 원뿔 안
- 같은 층
- 시야 확보
- 경로에 벽 없음
- 도착 지점에 플레이어 캡슐 배치 가능
- 도착 지점 아래에 유효한 지면 존재

타깃 동률 해소:

```text
각도 작은 순
→ 거리 가까운 순
→ entityId 작은 순
```

### 5.3 적 공격 상태 머신

```text
Approach
→ AttackWindup
→ AttackActive
→ AttackRecovery
```

공격 시작 순간 `committedAttackDirection`을 저장한다. Windup 이후 플레이어를 계속 추적하지 않는다.

### 5.4 피해 이벤트 처리

공격 판정 중 상태를 즉시 임의 순서로 변경하지 않는다. 피해 이벤트를 고정 배열에 모아 정해진 순서로 처리한다.

```csharp
public struct DamageEvent
{
    public int sourceEntityId;
    public int targetEntityId;
    public int damage;
    public int stunTicks;
    public int attackSequence;
}
```

정렬 기준:

```text
발생 tick
→ targetEntityId
→ sourceEntityId
→ attackSequence
```

---

## 6. `SimStep` 업데이트 순서 고정

권장 순서:

```text
1. InputCmd 적용
2. 플레이어 행동 상태 전이
3. 플레이어 이동·점프·대시·런지
4. 적 ID 오름차순 감지 정보 계산
5. 적 ID 오름차순 행동 상태 전이
6. 적 이동·회전·하강
7. 플레이어 공격 판정
8. 적 공격 판정
9. 투사체 이동 및 판정(존재할 경우)
10. 피해·스턴·사망 처리
11. 플레이어·적 분리
12. 쿨타임 및 상태 틱 감소
13. world.tick 증가
```

실제 게임, 탐색, 최종 후보 검증, 입력 재생이 모두 이 순서를 공유한다.

---

## 7. Navigation 교체

### 7.1 현재 문제

현재 `NavMeshPathfinder.NextCorner`는 다음을 런타임에 호출한다.

```text
NavMesh.SamplePosition
NavMesh.CalculatePath
```

Beam Search 후보마다 반복하면 성능이 나쁘고, 경로 동률 해소를 통제하기 어렵고, Job/Burst 병렬화도 어렵다.

### 7.2 목표 구조

Unity NavMesh는 제작·검증 도구로만 사용하고 Sim은 고정 그래프를 사용한다.

```csharp
public struct ArenaNavNode
{
    public int nodeId;
    public Vector3 position;
    public int levelId;
    public NavNodeFlags flags;
}

public struct ArenaNavLink
{
    public int linkId;
    public int fromNodeId;
    public int toNodeId;
    public NavTraversalType traversalType;
    public int traversalCost;
    public bool oneWay;
}
```

초기 큐브맵에는 10~30개 노드를 수동 배치한다.

```text
1층 중앙·좌·우
계단/경사 입구
계단/경사 출구
2층 중앙·좌·우
DropStart
DropLanding
대기 노드
```

### 7.3 경로 사전 계산

맵 로드 시 다음 표를 만든다.

```csharp
nextNodeTable[currentNodeId, destinationNodeId]
distanceTable[currentNodeId, destinationNodeId]
```

경로 비용이 같으면 다음 순서로 해소한다.

```text
낮은 총 비용
→ 낮은 nextNodeId
→ 낮은 linkId
```

### 7.4 단기 대안

노드 그래프 전환이 늦어지면 최소한 `(fromZoneId, toZoneId)` 기반 경로 캐시를 사용한다. 다만 최종 목표는 명시적 노드 그래프다.

---

## 8. Unity Physics 의존 축소

### 8.1 허용 범위

MVP에서는 정적 지형에 대한 읽기 전용 쿼리를 유지할 수 있다.

```text
Physics.CapsuleCast
Physics.Raycast
Physics.CheckCapsule
```

조건:

- 동적 Rigidbody 없음
- 이동 발판 없음
- 문·파괴 지형 없음
- 정적 Cube/단순 Collider 중심
- 레이어 마스크 고정
- 호출 순서 고정

### 8.2 `ICollision` 확장

```csharp
public interface ICollision
{
    CastHit CapsuleCast(...);
    bool SampleGround(...);

    bool HasLineOfSight(Vector3 from, Vector3 to);
    bool CanOccupyCapsule(Vector3 feet, float radius, float height);
    bool HasGround(Vector3 feet, float maxDown, out float groundY);
}
```

### 8.3 런지 쿼리 최소화

런지는 시작 순간 다음을 한 번 검사하고 결과를 상태에 고정한다.

```text
타깃 LOS
전체 이동 구간 CapsuleCast
도착 지점 CheckCapsule
도착 지점 Ground 검사
```

Travel 중 매 틱 타깃이나 경로를 다시 계산하지 않는다.

### 8.4 탐색과 정밀 검증 분리

Beam Search 중:

- Navigation Graph 기반 이동 가능성
- 간단한 구역·AABB 판정
- 최소한의 Physics 쿼리

최종 후보 3~6개:

- 실제 `CharacterMotor`
- 실제 `ICollision`
- 60Hz 정밀 재시뮬레이션

정밀 검증에 실패한 후보는 폐기하고 다음 순위 후보로 교체한다.

---

## 9. 스냅샷과 메모리 구조 변경

### 9.1 현재 문제

현재 `Snapshot.Clone`은 호출마다 `EnemySim[]`을 새로 생성한다. `Main.FixedUpdate`에서도 매 틱 호출하므로 GC 할당이 발생한다.

### 9.2 목표

명시적 복사와 고정 버퍼 풀을 사용한다.

```csharp
public static void CopyTo(in SimWorld source, ref SimWorld destination)
{
    destination.tick = source.tick;
    destination.player = source.player;
    destination.enemyCount = source.enemyCount;

    Array.Copy(
        source.enemies,
        destination.enemies,
        source.enemyCount);
}
```

필요한 풀:

- `SimWorld` 버퍼 풀
- SearchNode 풀
- 확장 행동 배열
- 피해 이벤트 배열
- 최종 프레임 배열

목표:

```text
예측 1회 GC Alloc ≈ 0
```

---

## 10. `WorldHash` 보강

해시에 포함할 최소 상태:

### 플레이어

- 위치·속도·yaw
- grounded·jumpCount
- HP·alive·피격 경직
- 대시 방향·쿨타임
- 행동 단계와 남은 틱
- 런지 targetId·시작점·도착점

### 적

- ID·종류·alive·HP·스턴
- 위치·속도·yaw
- AI 상태와 남은 틱
- 공격 단계·쿨타임·확정 공격 방향
- current/next/destination NavNode
- activeTraversalLinkId

### 월드

- tick
- enemyCount
- 활성 투사체 집합
- waveId
- spawnLocked
- mapVersion

다음 틱 결과에 영향을 주는 필드는 반드시 해시에 포함한다.

---

## 11. 예측 어셈블리 추가

```text
Assets/_Project/Prediction
├─ Game.Prediction.asmdef
├─ Core
│  ├─ PredictionPlanner.cs
│  ├─ PredictionRequest.cs
│  └─ PredictionSettings.cs
├─ Actions
│  ├─ MacroAction.cs
│  └─ ActionGenerator.cs
├─ Search
│  ├─ SearchNode.cs
│  ├─ BeamSearch.cs
│  ├─ WorldBufferPool.cs
│  └─ StateDeduplicator.cs
├─ Evaluation
│  ├─ ThreatEvaluator.cs
│  └─ PathEvaluator.cs
├─ Results
│  ├─ CandidatePath.cs
│  ├─ PredictedFrame.cs
│  └─ PredictedActionEvent.cs
└─ Replay
   └─ ReplayPlan.cs
```

의존성:

```text
Game.Prediction → Game.Sim
Game.View → Game.Sim, Game.Bridge, Game.Prediction
```

`Game.Prediction`은 `Game.View`, GameObject, Transform, Animator를 참조하지 않는다.

---

## 12. Beam Search 초기 사양

### 초기 설정

```text
예측 길이: 180틱 / 3초
매크로 길이: 15틱 / 0.25초
Beam 폭: 12
평균 행동 후보: 4개 이하
최종 후보: 1개부터 시작, 이후 최대 3개
시간 제한: 200ms
적 수: 초기 4~8마리
```

### 행동 후보

```text
ForwardMove
LeftMove
RightMove
Retreat
Wait
ForwardDash
BackwardDash
LeftDash
RightDash
Attack
Lunge(targetId)
```

항상 전부 생성하지 않는다.

- 대시 쿨타임이면 대시 제외
- 공격 범위 밖이면 Attack 제외
- 유효한 정면 적이 없으면 Lunge 제외
- 가까운 유효 적 최대 2마리만 Lunge 후보
- 공중 상태에서는 불가능한 지상 행동 제외

행동 생성 순서와 동률 해소 순서를 고정한다.

### 즉시 폐기

- 플레이어 사망
- 맵 이탈
- 유효하지 않은 런지
- 벽 내부
- NaN/Infinity
- 예측 중 허용되지 않은 스폰

### 초기 평가값

- 플레이어 생존
- 남은 HP
- 누적 피격 위험
- 가장 가까운 적과의 거리
- 포위 정도
- 남은 대시
- 런지·공격 쿨타임
- 적 처치 수
- 종료 위치의 탈출 가능성

---

## 13. 후보 결과 데이터

```csharp
public sealed class CandidatePath
{
    public int candidateId;

    public InputCmd[] controls;
    public PredictedFrame[] predictedFrames;
    public PredictedActionEvent[] actionEvents;

    public int killCount;
    public int expectedHits;
    public int dashCount;
    public int lungeCount;
    public int durationTicks;

    public float safetyScore;
    public float killScore;
    public float difficultyScore;

    public ulong initialSnapshotHash;
}
```

샘플링:

```text
PredictedFrame: 매 틱
경로선: 6틱 / 0.1초
잔상: 30틱 / 0.5초
ActionEvent: 실제 대시·점프·평타·런지 발생 틱
```

Beam Search 중에는 매 틱 프레임을 저장하지 않는다. 최종 후보만 초기 스냅샷부터 다시 실행해 정밀 데이터를 만든다.

---

## 14. 예지 런타임 흐름

### 발동

```text
예지 입력
→ 플레이어 생존·게이지·쿨타임 검사
→ spawnLocked = true
→ Time.timeScale = 0
→ SimWorld 스냅샷
→ initialSnapshotHash 저장
→ PredictionPlanner 시작
```

예측 계산은 `FixedUpdate`를 기다리지 않고 복제 상태에서 `SimStep.Run`을 직접 반복한다.

### 탐색 중 화면

- `Time.unscaledDeltaTime` 사용
- 프레임당 2~4ms 예산으로 `planner.Step()` 실행 가능
- 총 하드 리밋 200~300ms
- 예지 진입 VFX로 대기 시간 가림

### 후보 표시

- 후보 1개부터 시작
- 경로선
- 0.5초 잔상
- 대시·점프·평타·런지 아이콘
- 예상 처치 수, 피격 수, 난이도, 소요 시간

### 선택과 재생

선택 직전 현재 월드 해시가 예지 시작 해시와 같은지 검사한다.

재생 방식은 D1 결정에 따라 구현한다.

### Miss

- 자동 재생 즉시 중단
- 예상 위치로 순간이동 보정 금지
- 현재 채택된 실제 SimWorld 상태에서 조작권 반환
- 잔상과 액션 UI 제거
- 스폰 잠금 해제 규칙 명시

---

## 15. 테스트와 통과 기준

### G0. 전투 준비

- 적의 Windup 방향이 시작 순간 고정된다.
- 4방향 대시로 적 공격을 회피할 수 있다.
- 런지가 같은 상태에서 항상 같은 적을 선택한다.
- 런지 도착점이 항상 동일하다.
- `런지 → 평타 → 처치`가 `SimStep.Run`만으로 완주된다.

### G1. 결정론

같은 스냅샷과 같은 입력 180틱을 최소 100회 실행한다.

완전 일치:

- 모든 틱 `WorldHash`
- 플레이어 위치·속도·행동 단계
- 적 위치·AI·공격 단계
- HP·스턴·생존 집합
- NavNode·TraversalLink

### G2. 성능 기반

```text
적 8마리
180틱 단일 경로
100회 반복
GC Alloc 거의 0
```

상태 복사, 시뮬레이션, Navigation, Physics 시간을 각각 측정한다.

### G3. 미니 탐색

```text
행동 4개
깊이 3
Beam 4
```

- 사망 후보 제거
- 최소 1개 생존 경로 반환
- 반복 실행 시 같은 후보 반환

### G4. 전체 탐색

```text
3초
Beam 12
적 4~8마리
200ms 목표
```

- 후보 최소 1개
- 최종 후보 정밀 검증 통과
- 잔상과 이벤트 생성

### G5. 재생

- 선택 직전 해시 일치
- 대시·평타·런지 이벤트가 계획 틱과 일치
- 완료 후 정상 조작 복귀
- Miss 후 현재 상태에서 정상 조작 복귀

---

## 16. 구현 순서

### Phase 1 — 전투 계약 정리

1. GDD와 ADR에 새 전투안 반영
2. `InputCmd` 변경
3. `PlayerSim`, `PlayerCombatState`, `EnemySim` 변경
4. `WorldHash` 동시 갱신

### Phase 2 — 예측 가능한 전투 완성

1. 4방향 대시
2. 평타 상태 머신과 피해
3. 결정론적 런지 타기팅
4. 런지 이동과 피해
5. 적 공격 Windup/Active/Recovery
6. 플레이어 피격·사망

### Phase 3 — 인프라 교체

1. 정적 Navigation Graph
2. 경로표 사전 계산
3. `ICollision` 확장
4. 런지 Physics 쿼리 1회화
5. `Snapshot.Clone` 할당 제거
6. 월드 버퍼 풀

### Phase 4 — 예측 코어

1. `Game.Prediction` 어셈블리
2. 매크로 행동
3. Beam Search
4. 상태 중복 제거
5. 안전 점수
6. 최종 후보 정밀 재검증

### Phase 5 — 화면과 재생

1. 예지 시간 정지
2. 후보 경로선
3. 0.5초 잔상
4. 액션 아이콘
5. 후보 선택
6. 재생
7. Perfect/Good/Miss

### Phase 6 — 최적화와 안정화

1. Profiler 구간 측정
2. Physics 호출 수 감소
3. 행동 후보 상황별 축소
4. Beam 동적 축소
5. 시간 제한과 부분 결과 반환
6. 디싱크 첫 발생 틱 로그

---

## 17. 일정이 밀릴 때 축소 순서

다음 순서로 줄인다.

1. 후보 3개 → 안전 후보 1개
2. 예측 5초 → 3초 → 2초
3. Beam 18 → 12 → 6
4. 런지 후보 적 2마리 → 가장 가까운 1마리
5. 벽타기 탐색 제외
6. 점프 탐색 제외, 일반 플레이에는 유지
7. 원거리 적 제외
8. 잔상 캐릭터 → 반투명 캡슐

끝까지 유지할 코어:

```text
시간 정지
→ 미래 계산
→ 잔상·액션 표시
→ 경로 선택
→ 재생
→ Miss 복귀
```

---

## 18. 담당자 간 필수 인터페이스

### 게임·전투 담당 → 예측 담당

- 확정된 `InputCmd`
- `SimWorld` 전체 상태
- `SimStep.Run`
- `Snapshot.CopyTo`
- `WorldHash.Compute`
- 정적 Navigation Graph
- `ICollision`
- 공격·대시·런지 설정값

### 예측 담당 → 게임·표현 담당

- `CandidatePath`
- 경로선 포인트
- 잔상 데이터
- 액션 이벤트
- 예상 처치·피격·난이도
- 선택 상태와 재생 진행 틱

### 공통 금지

- View에서 별도의 전투 판정
- Animator Event로 공격 판정
- 예측 전용 적 AI
- 재생 중 예상 좌표로 임의 보정
- 후보 확장 중 GameObject 생성
- 후보 확장 중 `Instantiate`, `Destroy`, LINQ, JSON 복사

---

## 19. 예측 개발 착수 체크리스트

- [ ] 새 전투안이 GDD와 ADR에 반영됨
- [ ] 재생 방식 D1 확정
- [ ] `InputCmd.lungeTargetId` 확정
- [ ] 플레이어 HP·생존 상태 존재
- [ ] 4방향 대시 방향과 쿨타임 고정
- [ ] 런지 타깃 선정 동률 규칙 고정
- [ ] 런지 도착점 검증 규칙 고정
- [ ] 적 공격 Windup/Active/Recovery 구현
- [ ] `committedAttackDirection` 구현
- [ ] 피해·스턴·사망이 Sim에서 처리됨
- [ ] 정적 Navigation Graph 또는 최소 경로 캐시 존재
- [ ] `Snapshot.Clone` 반복 할당 제거
- [ ] 다음 틱에 영향을 주는 모든 상태가 WorldHash에 포함됨
- [ ] 180틱 × 100회 결정론 테스트 통과
- [ ] 적 8마리 단일 경로 벤치마크 완료

위 체크리스트가 통과되면 전체 예측 시스템을 안정적으로 얹을 수 있다.
