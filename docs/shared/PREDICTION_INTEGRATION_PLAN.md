# 전체 예측 시스템 통합을 위한 변경 계획

## 0. 플레이어 경험 계약 — 예측 리듬 실행

예측 후보를 선택한 뒤의 단계는 수동 입력 없는 자동 재생이 아니다.
정식 명칭은 **예측 리듬 실행(Prediction Rhythm Execution)** 이다.

```text
예측 프리뷰
→ 후보 선택
→ 저장된 월드 궤적 적용 시작
→ 0.5초 잔상 노드와 액션 아이콘 제시
→ 플레이어가 판정 창 안에 액션 입력
→ Perfect/Good이면 궤적 유지
→ Miss이면 즉시 종료하고 현재 실제 상태에서 직접 조작
```

상태 궤적은 위치와 전투 결과의 디싱크를 막는 내부 데이터다. 잔상 노드의
대시·점프·평타·타깃 런지 입력은 플레이어가 직접 수행하며, 입력 판정 없이
후보가 끝까지 성공 처리되어서는 안 된다.

코드와 문서에서는 다음 이름을 구분한다.

- `PredictionPreview`: 후보 미래 표시
- `RhythmExecution`: 선택한 미래를 리듬 입력으로 실행
- `TrajectoryPlayback`: 저장된 월드 상태를 적용하는 내부 계층
- `RhythmJudge`: Perfect / Good / Miss 판정

UI 문구와 클래스 이름에서 단독 `AutoReplay`, `자동 재생` 표현을 사용하지 않는다.

작성일: 2026-07-18  
대상 프로젝트: `PrecogPrototype`  
기준 브랜치 조사 시점: `rebuild` (`45f3602`)

관련 문서:

- [게임 런타임 ↔ 예측 엔진 통합 계약](PREDICTION_CONTRACT.md)
- [몹 시스템·층이동·예측 연동 총정리](ENEMY_SYSTEM.md)
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
→ 예측 리듬 실행
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

### D1. 예측 리듬 실행의 내부 방식 — 월드 전체 상태 궤적

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

### 선택과 예측 리듬 실행

선택 직전 현재 월드 해시가 예지 시작 해시와 같은지 검사한다.

예측 리듬 실행의 궤적 적용 방식은 D1 결정에 따라 구현한다.

### Miss

- 예측 리듬 실행과 상태 궤적 적용 즉시 중단
- 예상 위치로 순간이동 보정 금지
- 현재 채택된 실제 SimWorld 상태에서 조작권 반환
- 잔상과 액션 UI 제거
- 스폰 잠금 해제 규칙 명시

---

## 15. 테스트와 통과 기준

### 세 후보 플레이 시나리오와 합격 조건

후보 프로필을 단순 가중치 프리셋이 아니라 다음 플레이 시나리오로 검증한다.

공통 평가축은 `초기 주목 대상 진척`, `종료 위치 품질`, `누적 조작 난이도` 세 가지다.
초기 주목 대상은 시작 스냅샷에서 한 번 고정하고, 종료 위치 보너스는 마지막 Beam 깊이에만
적용한다. 회귀 테스트는 정면/후면 대상 선택, 제자리 무보상, 피해·처치 진척, 안전한 종단 위치,
복합 매크로와 큰 시선 전환의 난이도 증가를 각각 검증한다.

#### 프로필별 진행 순서

아래 5단계는 전체 프로필을 한꺼번에 처리하지 않고 **공격형 → 안전형 → 기회형 각각에 대해
독립적으로 반복**한다. 한 프로필의 1~4단계 증거가 없으면 그 프로필의 가중치 조정으로 넘어가지
않는다.

1. **대표 시나리오 3~5개 확정**
   - 고정된 테스트 맵, 시작 위치/yaw, 적 ID·종류·위치·HP·AI 단계, 이동 자원을 기록한다.
   - 같은 Snapshot과 seed로 반복 실행할 수 있는 Fixture 또는 디버그 프리셋을 만든다.
   - 해당 프로필이 잘해야 하는 정상 사례와, 하지 말아야 할 역행 사례를 함께 포함한다.
2. **기대 행동을 문장으로 작성**
   - “무엇을 먼저 하고, 무엇을 피하고, 어디서 끝나야 하는가”를 순서가 있는 한 문장으로 쓴다.
   - 특정 매크로 하나를 강제하지 않고, 플레이 의도와 허용 가능한 대체 행동을 명시한다.
   - 관찰 가능한 합격 조건과 실패 조건을 함께 작성한다.
3. **현재 예측 결과 녹화·로그 비교**
   - 평가 항목을 추가하거나 가중치를 바꾸기 전에 기준선 영상을 저장한다.
   - 동일 Snapshot을 각 프로필로 최소 10회 실행하고 후보 1~3위와 선택 이유를 로그로 남긴다.
   - 변경 후 같은 Snapshot을 다시 실행하여 기준선/변경 결과를 나란히 비교한다.
4. **평가 항목 추가 및 무가중 검증**
   - 타깃 중요도, 종료 위치, 입력 난이도 관측값을 먼저 계산하고 로그에 노출한다.
   - 이 단계에서는 새 평가값으로 Beam 순위를 바꾸지 않거나 multiplier를 0으로 두고,
     관측값이 기대 행동과 같은 방향인지 단위 테스트로 검증한다.
   - 후보 루프에서 Physics·런타임 NavMesh를 호출하지 않고 GC 할당과 탐색 시간을 측정한다.
5. **가중치 마지막 조정**
   - 1~4단계가 통과한 평가 항목만 실제 Beam 점수에 연결한다.
   - 한 번에 한 축의 multiplier만 변경하고, 12개 대표 시나리오 전체를 회귀 실행한다.
   - 평균 점수보다 시나리오 합격률, 잘못된 1위 후보 수, 탐색 시간, 결정론을 우선 판단한다.
   - 특정 시나리오만 맞추는 매직넘버는 금지하고, 실패 시 가중치보다 관측식·행동 후보를 먼저
     재검토한다.

#### 공격형 대표 시나리오

| ID | 초기 상황 | 기대 행동 | 합격 조건 | 대표 실패 |
| --- | --- | --- | --- | --- |
| A1 정면 밀집 대 후방 유인 | 정면 부채꼴에 혼합 지상 적 12마리, 카메라 뒤에 더 가까운 적 4마리 | 뒤쪽 유인에 180도 회전하지 않고 정면 주목 대상을 먼저 베며 밀집 군집 안에서 연계한다. | 초기 주목 대상에게 피해가 들어가고 종료 시 적이 남아 있어도 정면 군집 처치가 우세하다. | 가까운 후방 적부터 공격하거나 전멸 후 의미 없는 이동만 남는다. |
| A2 다층 공중·지상 혼합 | 서로 다른 고도의 비행 적 4마리와 좌우 지상 적 8마리 | 정면 공중 위협을 공중 추격으로 먼저 압박하고 착지 또는 런지 연계로 지상 적에게 전환한다. | 비행 주목 대상에게 실제 피해를 주며 AerialPursuit 또는 유효한 공중 콤보가 포함된다. | 가까운 지상 적만 공격하거나 비행 적 아래에서 정지한다. |
| A3 런지 순환 통로 | 중앙 저체력 적 4마리가 통로를 이루고 좌우에 강한 군집 10마리 | 중앙 처치 사슬로 런지·공격 흐름을 이어가고 불필요하게 좌우 군집 깊숙이 새지 않는다. | 첫 처치 뒤 다음 중앙 표적으로 공격이 이어지고 두 번 이상의 표적 연계가 실현된다. | 첫 적 앞에서 이동만 하거나 한쪽 군집에 고립된다. |
| A4 양측 포위 속 오버킬 | 양옆과 후방에 18마리, 중앙에 공격 임박 대형 적과 저체력 적 | 저체력 적을 빠르게 정리하면서 중앙 강적의 확정 공격을 피하고 한쪽 포위선을 공격적으로 뚫는다. | 처치 수가 증가하고 생존하며 종료 시 적이 남아 후보 간 위치·난이도 차이를 비교할 수 있다. | 공격 점수만 보고 중앙에 남아 사망하거나 같은 무효 런지를 반복한다. |

공격형의 문장형 기대 행동은 “현재 화면에서 가장 중요한 적에게 즉시 접근해 피해를 실현하고,
자원 초기화와 인접 표적을 이용해 공격 흐름을 끊지 않되 확정 사망은 피한다”로 고정한다.

#### 안전형 대표 시나리오

| ID | 초기 상황 | 기대 행동 | 합격 조건 | 대표 실패 |
| --- | --- | --- | --- | --- |
| S1 포위 탈출 | 플레이어 주변 5마리, 한쪽 탈출 방향을 저체력 적이 차단 | 차단 적을 제거하고 열린 방향으로 점프·대시하여 포위망 밖에서 끝낸다. | 종료 근접 적 수가 줄고 시작점보다 최근접 적 거리가 증가한다. | 적을 죽인 자리에서 회복 상태로 끝난다. |
| S2 고지대 도약 | 가까운 JumpUp 링크와 지상 추격 적 다수 | TerrainLeap 또는 점프·더블점프로 높은 지형으로 이동해 추격 각을 끊는다. | 종료 고도가 증가하고 유효 그래프 링크를 따라 이동한다. | 평지에서 뒤로 걷기만 하거나 도달 불가 지형을 시도한다. |
| S3 투사체 회피 | 정면 원거리 적이 발사 준비, 측면에 안전 공간 | 공격 욕심보다 충돌 예상선 밖으로 대시하고 다음 조작이 가능한 상태로 끝낸다. | 예상 피격 수가 감소하고 공격/런지 Recovery가 종료된다. | 적에게 접근하며 확정 투사체를 맞는다. |
| S4 공중 위협 제거 후 이탈 | 위에서 조준 중인 비행 적과 지상 포위 적 | 공중 위협을 제거해야 탈출이 가능하면 공중 추격으로 제거한 뒤 거리나 고도를 확보한다. | 위협 제거와 종료 안전도 개선이 둘 다 나타난다. | 비행 적 처치 직후 적 무리 중앙에 정지한다. |

안전형의 문장형 기대 행동은 “탈출을 방해하는 핵심 위협만 필요한 만큼 제거하고, 점프·대시·
지형 링크를 사용해 포위와 예상 공격선에서 벗어난 뒤 즉시 조작 가능한 위치에서 끝난다”로
고정한다.

2026-07-20 구현 메모:

- `S-v1-complex` fixture로 S1 양측 포위, S2 JumpUp 고지대, S3 교차 투사체·돌진,
  S4 조준 중 공중 위협·지상 포위를 고정했다.
- Unity 메뉴 `Precog/프로필 검증/안정형 S1-S4 단계별 가중치 비교 내보내기`는
  `0-baseline → 1-terminal → 2-difficulty → 3-current-final`을 각 10회 실행한다.
- 안전형 감사 순서는 재생 유효성, 예상 피격, 종료 위치 품질, 종료 근접 적 수,
  즉시 행동 가능, 필요한 처치, 입력 난이도다.
- `2-difficulty`는 복합 탈출 가지가 중간 Beam에서 잘리는지 확인하기 위해 난이도 감점을
  마지막 깊이에만 적용한다. 실제 `Safe` 적용 시점과 가중치는 로그 비교 뒤 확정한다.
- 첫 `S-v1-complex` 로그에서는 개발용 체력 `1,000,000`이 safety 점수를 약 2천만으로
  포화시키고 S1/S2의 모든 적이 전멸해 탈출 품질을 비교할 수 없었다. `S-v2-dense`부터는
  예측 HP 기여를 원래 3칸으로 포화하고 실제 피격 횟수는 별도로 반영하며, S1/S2/S4의
  적 밀도를 높여 종료 시 적이 남는 조건에서 비교한다.
- `S-v2-dense` 로그에서 종료 안전도는 S1의 최근접 적 거리를 `1.055 → 7.171`, S3의
  종료 품질을 `40.573 → 49.357`로 개선했다. S2의 TerrainLeap 후보는 종료 품질
  `46.364`로 최고였지만 1킬 차이와 난이도 비용으로 3위였고, S4도 종료 품질 `40.639`
  후보보다 1킬 많은 `37.345` 후보를 선택했다. 이에 안전형 v1.10은 kill `0.3`,
  종료 위치 `2.8`, 입력 난이도 `0.8` 종단 전용으로 확정한다.
- 안정형이 공격형보다 일반 적을 더 많이 처치하는 프로필 역전이 관측되어, 일반 첫 1킬
  보너스 `50`은 제거했다. 같은 값은 예측 시작 시 고정한 핵심 위협 한 명을 제거할 때만
  지급한다. 핵심 위협이 아닌 추가 처치는 낮은 kill 배율만 받고 반복 보너스를 받지 않는다.

#### 기회형 대표 시나리오

| ID | 초기 상황 | 기대 행동 | 합격 조건 | 대표 실패 |
| --- | --- | --- | --- | --- |
| O1 치고 빠지기 | 정면에 공격 가능한 적 1마리, 그 뒤에 증원 3마리 | 첫 적에게 피해를 준 뒤 적 무리 중앙이 아닌 측면/후퇴 위치로 이동한다. | 실제 피해가 1 이상이고 종료 위치 품질이 시작보다 좋아진다. | 공격 없이 자세만 보존하거나 적 중앙으로 런지한다. |
| O2 쉬운 클릭 | 비슷한 가치의 표적 2마리가 좌우에 있고 한쪽은 큰 시선 전환 필요 | 작은 시선 전환과 적은 입력으로 공격 가능한 표적을 선택한다. | 공격형보다 누적 입력 난이도가 낮고 실제 피해를 준다. | 매 스텝 표적을 바꾸거나 180도 회전을 반복한다. |
| O3 다음 공격 준비 | 현재 적을 한 번 공격하면 다음 적에게 런지 가능한 측면 위치가 열림 | 현재 피해를 실현하고 다음 런지/공격이 가능한 각도와 자원을 남긴다. | 종료 시 행동 가능 상태이며 유효한 다음 표적이 존재한다. | 자원 보존만 하고 첫 공격을 하지 않는다. |
| O4 제한 자원 | 대시 1회, 런지 1회만 남고 근접 적과 원거리 적이 함께 존재 | 한 자원으로 공격 또는 이탈을 해결하고 다른 자원을 비상용으로 남긴다. | 피해·생존·자원 보존 중 최소 두 항목이 개선되고 사망하지 않는다. | 모든 자원을 한 표적에 사용한 뒤 위험한 Recovery로 끝난다. |

기회형의 문장형 기대 행동은 “클릭하기 쉬운 가까운 기회를 실제 피해로 전환한 뒤, 과도한
시선 전환과 복합 입력을 피하면서 다음 공격 또는 이탈을 즉시 선택할 수 있는 위치와 자원을
남긴다”로 고정한다.

#### 녹화·로그 비교 형식

각 실행은 `scenarioId`, `profile`, `contractVersion`, `mapVersion`, `initialSnapshotHash`,
`candidateRank`, `actionSequence`, `salientEnemyId`, `salientDamage`, `killCount`,
`terminalNearestEnemyDistance`, `terminalNearbyEnemyCount`, `terminalHeightDelta`,
`terminalActionable`, `executionDifficulty`, `expectedHits`, `durationTicks`,
`safetyScore`, `killScore`, `difficultyScore`, `totalScore`, `elapsedMs`, `worldHashAfterReplay`를
CSV 또는 JSON 한 줄로 남긴다.

영상 파일명은 `<scenarioId>_<profile>_<baseline|changed>_<snapshotHash>.mp4`로 통일한다.
영상에는 시작 배치, 후보 1~3위, 선택 후보의 잔상, 액션 이벤트, 종료 상태가 보여야 한다.
로그 비교표에는 각 시나리오별로 `기준선 1위`, `변경 후 1위`, `기대 행동 일치 여부`,
`달라진 평가축`, `회귀 여부`를 기록한다.

#### 단계별 완료 체크리스트

| 프로필 | 1. 시나리오 확정 | 2. 기대 행동 문장 | 3. 기준선 녹화·로그 | 4. 평가축 무가중 검증 | 5. 최종 가중치 |
| --- | --- | --- | --- | --- | --- |
| 공격형 | [x] A1~A4 | [x] | [x] `A-v2-complex` | [x] audit 통과 | [x] v1.9 확정 |
| 안전형 | [x] S1~S4 | [x] | [ ] | [ ] | [ ] |
| 기회형 | [x] O1~O4 | [x] | [ ] | [ ] | [ ] |

현재 구현에 평가축과 임시 multiplier가 이미 연결되어 있으므로, 기준선 수집 단계에서는
`salientTargetMul`, `terminalPositionMul`, `difficultyPenaltyMul`을 0으로 둔 별도 진단
프로필을 사용한다. 기준선과 무가중 관측 검증이 끝나기 전에는 현재 수치를 최종값으로 확정하지
않는다. 최종 조정 PR에는 12개 시나리오 비교 로그와 변경 전후 영상을 첨부한다.

공격형 Fixture는 `A-v2-complex`부터 12~18마리의 복합 분포를 사용한다. 로그에는
`fixtureVersion`, `initialEnemyCount`, `finalAliveEnemyCount`를 포함하며 서로 다른 Fixture
버전의 결과를 같은 기준선으로 직접 비교하지 않는다.

구현된 Editor 진입점:

- `Precog/프로필 검증/공격형 A1-A4 기준선 로그 내보내기`
  - A1~A4를 같은 Snapshot으로 각각 10회 실행한다.
  - 무가중 진단 프로필로 후보 1~3위를 수집해 `PredictionLogs` 폴더에 JSON·CSV를 함께 저장한다.
  - `auditRank`는 Beam 점수에 영향을 주지 않는 후처리 순위다. 주목 대상 진척, 처치, 피해,
    피격, 종료 행동 가능, 종료 위치, 입력 난이도를 사전식으로 비교한다.
  - `auditRecommended`, `auditDiffersFromBeamWinner`, `auditReason`으로 기존 1위와의 차이를 기록한다.
- `Precog/프로필 검증/공격형 A1-A4 콘솔 재생`
  - 영상 캡처 전에 네 시나리오의 시작 배치, 선택 행동, 15틱 간격 상태와 종료 상태를 확인한다.

공격형 3단계는 아래 `A-v2-complex` CSV·JSON을 기준선 증거로 사용한다. 영상은 정성 검토
자료이며, 수치 비교의 정본은 동일 Snapshot 해시가 기록된 CSV·JSON이다.

`A-v2-complex` 기준선은 `aggressive_baseline_20260720_145650.csv/json`으로 확정했다.
각 시나리오 10회와 후보 1~3위가 동일 행동·동일 WorldHash로 반복되어 결정론을 통과했고,
모든 후보 재생이 유효했다. 관찰 결과는 다음과 같다.

- A1: 16마리 중 8처치, 1위 종료 주변 적 7마리. 평균 341.1ms로 300ms 하드리밋 초과.
- A2: 12마리 중 7처치. 공중 주목 대상은 먼저 제거하지만 AerialPursuit 대신 LungeStrike 사용.
  후보 1~3위 점수가 동점이며 raw 입력 난이도와 종료 거리는 서로 다름.
- A3: 중앙 연계 공격으로 9처치. 후보 1~3위 점수가 동점이고 종료 행동·입력 난이도가 다름.
  Large glory 상태는 `alive=true`일 수 있으므로 `finalAliveEnemyCount`와 killCount를 단순 차감해
  비교하지 않는다.
- A4: 18마리 중 9처치, 3피격. 1·2위가 동점이지만 2위는 종료 즉시 행동 가능,
  3위는 최근접 적 5.41m·주변 적 0으로 종료 위치가 뚜렷하게 다름.

따라서 4단계 무가중 검증에서는 A2/A3 동점을 타깃 중요도·입력 난이도로, A4 동점을 종료 위치·
즉시 행동 가능으로 분리할 수 있어야 한다. 가중치 조정 전 A1 성능 초과 원인을 별도 측정한다.

4단계 판정은 새 축의 multiplier를 계속 0으로 유지한 상태에서 수행한다. `auditRank`는 실제
후보 선택에 사용하지 않으며, 다음 조건을 모두 만족해야 관측식 검증을 통과한다.

- 같은 Snapshot 10회에서 auditRank와 auditReason이 동일하다.
- A1은 정면 주목 대상 진척과 처치 수를 보존하며 기존 1위를 불필요하게 뒤집지 않는다.
- A2 동률 후보는 공중 주목 대상 진척을 보존한 뒤 종료 위치·입력 난이도로 분리된다.
- A3 동률 후보는 중앙 연계 처치를 보존하고 입력 난이도가 낮은 후보를 우선한다.
- A4 동률 후보는 종료 즉시 행동 가능과 포위 해소가 더 좋은 후보를 우선한다.
- auditRank 계산은 후보 생성·Beam 확장 밖의 로그 후처리이며 탐색 시간에 포함하지 않는다.

공격형 4단계는 `aggressive_baseline_20260720_150352.csv/json`으로 통과했다.

- 120행 모두 재생 유효, 10회 반복에서 auditRank·추천 후보·WorldHash가 동일했다.
- A1은 처치 수가 더 많은 기존 1위를 그대로 추천했다.
- A2는 처치·피해·피격이 같은 후보 중 종료 위치 품질 16.097, 입력 난이도 12.060인
  기존 3위를 audit 1위로 추천했다.
- A3는 중앙 연계 결과가 같은 후보 중 종료 위치 품질이 가장 높고 입력 난이도 6.215로 가장
  낮은 기존 1위를 유지했다.
- A4는 같은 처치·피해·피격 결과 중 즉시 행동 가능하고 종료 위치 품질 21.819인 기존 3위를
  audit 1위로 추천했다.
- 총 40회 중 A2·A4의 20회에서 audit 추천이 기존 Beam 1위와 달랐고, 시나리오별 결과는
  매 반복 완전히 동일했다.

따라서 타깃 중요도·종료 위치·입력 난이도 관측식의 방향성은 확정한다. 다음 단계에서는
관측식을 변경하지 않고 multiplier만 한 축씩 연결한다. 단, A1 평균 341.1ms 성능 초과를 먼저
해결하거나 프로필 가중치 실험과 별도의 성능 기준으로 명시해야 한다.

KJH가 적을 `Precise / Coarse / Dormant`로 분류하고 실제 게임과 예측이 같은 LOD 규칙을
공유하기로 했으므로 A1의 300ms 성능 판정은 해당 연동 뒤로 이관한다. 가중치 검증은 현재
결정론과 후보 품질을 기준으로 계속 진행하며, 이 이관은 성능 목표를 삭제하거나 현재 전 적
정밀 시뮬레이션 결과를 합격 처리한다는 뜻이 아니다.

공격형 5단계의 초기 연결값과 실험 순서는 다음과 같다.

| 단계 | 타깃 중요도 | 종료 위치 | 입력 난이도 감점 | 목적 |
| --- | ---: | ---: | ---: | --- |
| 0 기준선 | 0 | 0 | 0 | 기존 공격형 결과 |
| 1 타깃 | 0.1 | 0 | 0 | 정면 주목 대상 접근·피해 보존 |
| 2 종료 | 0.1 | 0.15 | 0 | 같은 처치 결과에서 종료 위치 분리 |
| 3 최종 | 0.1 | 0.15 | 0.1 | 같은 결과에서 과도한 입력만 약하게 감점 |

종료 위치와 난이도는 한 번의 일반 처치 이득을 역전하지 못하는 보조 크기로 시작한다.
`Precog/프로필 검증/공격형 단계별 가중치 비교 내보내기`는 네 단계를 A1~A4에서 각각
10회 실행하고 후보 1~3위, multiplier, raw 관측값과 재생 WorldHash를 JSON·CSV로 저장한다.

첫 단계별 결과 `aggressive_weight_stages_20260720_151157.csv/json`에서 타깃 중요도 `1.0`은
A1을 8처치·0피격에서 6처치·5피격으로 회귀시켰다. 타깃을 먼저 처리한 중간 가지가 Beam
가지치기에서 과도하게 살아남은 결과이므로 `1.0`은 폐기한다. 종료 위치 `0.15`는 A2/A4를
의도대로 분리했고, 입력 난이도 `0.1`은 추가 회귀를 만들지 않았으므로 유지한다. 타깃 중요도만
`0.1`로 낮춰 같은 단계별 검증을 다시 수행한다.

두 번째 결과 `aggressive_weight_stages_20260720_151819.csv/json`에서 타깃 `0.1`은 A1의
8처치·0피격을 유지했고 종료 위치 `0.15`도 A2/A4 개선을 유지했다. 그러나 난이도 `0.1`을
중간 깊이마다 적용하자 A1이 다시 6처치로 회귀하고 A2 종료 위치 개선이 사라졌다. 따라서
난이도 수치는 유지하되 공격형에서는 마지막 Beam 깊이에만 적용한다. 이는 공격 콤보 가지를
중간에 제거하지 않고 최종 후보의 과도한 입력만 동점 정리하기 위한 구조 변경이다.

최종 결과 `aggressive_weight_stages_20260720_152553.csv/json`으로 공격형 5단계를 통과했다.

- 480행 모두 재생 유효, 모든 단계·시나리오에서 10회 행동열과 WorldHash가 동일했다.
- A1은 기준선부터 최종까지 8처치·20피해·0피격을 유지했다.
- A2는 7처치·15피해·0피격을 유지하면서 종료 위치 품질이 14.570에서 16.097로 개선됐다.
- A3는 9처치·16피해·1피격과 최저 입력 난이도 6.215를 유지했다.
- A4는 9처치·17피해·3피격을 유지하면서 종료 즉시 행동 불가에서 가능으로 바뀌고,
  종료 위치 품질이 2.952에서 10.880으로 개선됐다.
- 마지막 깊이 난이도 감점은 어느 시나리오에서도 종료 위치 단계의 행동열을 다시 바꾸지 않았다.

따라서 공격형 v1.9는 타깃 중요도 `0.1`, 종료 위치 `0.15`, 입력 난이도 감점 `0.1`,
난이도 마지막 깊이 전용으로 확정한다. 성능 합격은 KJH의 적 LOD 연동 후 별도로 재검증한다.

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

### G5. 예측 리듬 실행

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

### Phase 5 — 화면과 예측 리듬 실행

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
→ 예측 리듬 실행
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
- 선택 상태와 예측 리듬 실행 진행 틱

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
- [ ] 예측 리듬 실행과 상태 궤적 적용 방식 D1 확정
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
