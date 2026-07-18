# 몹 시스템 총정리 — 로스터·층이동·예측 계약

- 작성일: 2026-07-18
- 적용 브랜치: `prediction-foundation`
- 상태: **구조 계약 확정 / 세부 AI 수치 일부 초안**
- 상위 계약: [PREDICTION_CONTRACT.md](PREDICTION_CONTRACT.md)

이 문서는 몹 로스터 초안과 1·2층 상승/하강 마이그레이션 내용을 하나로 합친
게임 개발자·예측 엔진 개발자 공동 작업 기준이다.

---

## 1. 확정·초안·TBD 구분

### 확정

- 전투·기동·크기 축을 분리한 몹 구성
- 모든 몹 로직은 `SimStep.Run` 안에서 실행
- 지속 상태와 투사체는 `SimWorld`에 저장
- AI 난수 사용 금지
- 공격 선딜에서 방향 확정
- NavMesh는 제작·검증용, 예측은 고정 그래프 사용
- Drop과 Booster는 제작자가 배치한 점→점 유향 링크
- 층이동은 순간이동이 아니라 매 틱 실제 공중 위치를 가짐
- 상승·하강 공용 `TraversalPhase`
- 여러 적의 착지는 ID 기반 슬롯으로 결정론적으로 분산

### 게임 개발자 승인 전 초안

- 몹별 HP·속도·공격 수치
- 돌진 거리·속도·후딜
- 원거리 투사체와 조준 보정
- 층이동 중 피격·스턴 처리
- 비행몹의 3D 장애물 회피
- 공중 적 대상 런지

### 맵 확정 후 TBD

- 실제 Drop 링크 위치
- 실제 Booster 링크 위치
- 링크별 착지 슬롯 수와 간격
- 적 종류별 `agentMask`

---

## 2. 몹 구성 모델

몹은 세 축의 조합으로 정의한다.

```text
전투 타입: Melee | Ranged
기동 타입: Ground | Charge | FloorTraversal | Flying
크기 타입: Grunt | Large
```

### 기본 아키타입 6종

| ID 초안 | 전투 | 기동 | 이름 |
| ---: | --- | --- | --- |
| 0 | 근접 | 지상 | 근접잡몹 |
| 1 | 원거리 | 지상 | 원거리잡몹 |
| 2 | 근접 | 돌진 | 돌진근접몹 |
| 3 | 근접 | 층이동 | 층이동근접몹 |
| 4 | 원거리 | 층이동 | 층이동원거리몹 |
| 5 | 원거리 | 비행 | 공중원거리몹 |

각 아키타입은 Grunt/Large 스탯셋을 가질 수 있어 최대 12개 변형이 된다.
Large는 새 AI가 아니라 HP·크기·속도·피해 등 중앙 스탯 테이블 변형이다.

```text
EnemyArchetypeId
+ EnemySizeId
→ EnemyStatSet
```

아키타입별 별도 Sim 구조체를 만들지 않고 통합 `EnemySim`을 사용한다.

---

## 3. 불변 구현 계약

1. 몹 로직은 `SimStep.Run` 내부의 고정 순서로만 실행한다.
2. 세계 질의는 `ICollision`과 `IPathfinder`를 경유한다.
3. `Physics.*`, `NavMesh.*`, `Time`, `UnityEngine.Random`을 몹 Sim에서 직접 호출하지 않는다.
4. 몹 피해는 Sim 전투 파이프라인에서 처리한다. View·Animator는 판정하지 않는다.
5. 모든 지속 상태는 `EnemySim`, `SimWorld`, 고정 배열에 둔다.
6. 새 상태는 `Snapshot.CopyTo`와 `WorldHash`에 즉시 등록한다.
7. 외부는 EnemySim 내부 상태를 변경하지 않는다.
8. 예측·View에 필요한 상태는 읽기 전용 관측 데이터로 제공한다.

최소 관측 데이터:

```text
id, archetypeId, sizeId
alive, position, health
aiState, stateTicks
committedDirection
activeTraversalLinkId
threatType, threatActiveTick
```

Beam Search 알고리즘과 버퍼 구조는 몹 추가로 교체하지 않는다. 다만 상태 스키마,
위협 평가, 행동 유효성, 충돌 질의, AI LOD는 새 기동과 공격에 맞춰 확장할 수 있다.

---

## 4. 공용 데이터 방향

`EnemySim`이 가져야 할 범주:

- 식별: id, archetypeId, sizeId
- 생존: alive, health, stunTicks
- 이동: pos, vel, yaw, grounded
- AI: aiState, stateTicks, cooldownTicks
- 공격: committedDirection, attackSequenceId
- 내비게이션: current/destination/next node ID, activeTraversalLinkId
- 층이동: traversalPhase, jumpStart, jumpEnd, jumpDuration
- 돌진: chargePhase, chargeDirection, chargeTicks, chargeHit
- 비행: flightBand, avoidanceState

사용하지 않는 필드는 0으로 유지한다.

투사체는 몹 내부 GameObject가 아니라 월드 고정 배열에 둔다.

```text
SimWorld.projectiles[MaxProjectiles]
projectileCount
```

투사체 필수 상태:

```text
id, ownerId, alive
position, velocity
radius, remainingTicks
damage, hitMask
```

---

## 5. 몹별 AI 초안

이 장의 수치는 게임 개발자가 플레이테스트 후 승인해야 한다.

### 5.1 근접 지상

```text
감지 및 LOS
→ 접근
→ 공격 거리 진입
→ Windup: 방향 확정, 정지
→ Active: 고정 방향 부채꼴 1회 판정
→ Recovery
→ 접근
```

| 항목 | 개발자 제안값 | 현재 코드값 | 계약 상태 |
| --- | ---: | ---: | --- |
| Aggro | 40 | 40 | 승인 필요 |
| 이동 속도 | 4 | 4 | 승인 필요 |
| Windup | 24틱 | 24틱 | 일치 |
| Active | 6틱 | 3틱 | **충돌, 선택 필요** |
| Recovery | 24틱 | 30틱 | **충돌, 선택 필요** |
| 공격 범위 | 약 1.7 | 1.45 | **충돌, 선택 필요** |
| 공격 반각 | 50도 | 55도 | **충돌, 선택 필요** |

피격·스턴 시 진행 중 공격을 즉시 취소하는 안을 초안으로 둔다.

### 5.2 원거리 지상

- 선호 거리: 8~16m 초안
- 8m 미만 후퇴, 16m 초과 또는 LOS 없음 접근
- 밴드 안에서는 정지하고 Aim
- Aim 36틱 후 무유도 투사체 발사
- 투사체 초안: 속도 12, 반경 0.25, 수명 300틱, 피해 1
- 조준 초안: 비행 시간의 50%만큼 부분 리드
- 피격·스턴 시 Aim 취소

대시 중 18도 빗맞힘을 유지한다면 RNG를 쓰지 않고 다음처럼 고정한다.

```text
enemyId 짝수: 조준 방향 기준 왼쪽 18도
enemyId 홀수: 조준 방향 기준 오른쪽 18도
```

이 보정 자체를 사용할지는 개발자 승인이 필요하다.

### 5.3 돌진 근접

확정 구조:

```text
Windup
→ 시작 방향 확정
→ 직선 Charge
→ 플레이어 또는 벽 접촉 시 정지
→ 성공/실패 Recovery
```

개발자 확정 필요:

- 최소·최대 개시 거리
- Windup 틱
- 속도와 최대 지속 틱
- 충돌 반경과 피해
- 성공·실패 후딜
- 벽 충돌 시 자기 스턴
- 절벽 진입과 낙하 허용 여부

### 5.4 층이동 근접·원거리

공격 AI는 대응하는 지상형과 공유한다. 층이동은 별도 공격이 아니라 추격 기동이다.

- Drop: 위층→아래층 일방
- Boost: 아래층→위층 일방
- 고정 링크의 다음 홉을 실행
- 자체적으로 걷는 길과 낙하 길을 비교하지 않음
- 상승·하강 중 매 틱 실제 위치 보유
- 공중 피격 가능

### 5.5 공중 원거리

- 낮고 느린 3D 부유
- 지상 NavGraph 미사용
- 원거리 지상형과 Aim·투사체 공유
- 플레이어 공격이 도달 가능한 높이 밴드 유지

개발자 확정 필요:

- 최소·최대 고도
- 수평 선호 거리
- 상승·하강 속도
- 벽·천장 회피와 맵 경계 처리
- 공중 적 런지 허용 여부
- 공중 런지 도착점·충돌·낙하·후딜 규칙

현재 런지는 지면과 높이 차 0.8m를 요구하므로 공중 런지는 아직 구현 계약이 아니다.

---

## 6. 1·2층 이동 그래프

### 6.1 그래프가 판단한다

폐기할 rebuild 방식:

```text
적이 매번 걷는 경로와 낙하 경로를 NavMesh.CalculatePath 3회로 비교
→ 더 짧으면 낙하
```

채택할 방식:

```text
사전 베이크된 nextNodeTable 조회
→ 다음 PathStep 획득
→ Walk / JumpUp / Drop / Boost를 그대로 실행
```

적은 “떨어질지” 자체 판단하지 않는다. 그래프가 이미 최단 유효 경로를 제공한다.

### 6.2 이동 종류

```csharp
public enum MoveKind : byte
{
    None,
    Walk,
    JumpUp,
    Drop,
    Boost
}
```

- `Drop`: 위→아래 일방 링크
- `Boost`: 아래→위 일방 링크, 층이동몹 전용
- `JumpUp`: 플레이어 또는 허용된 에이전트만 실제 점프 성능으로 사용
- 올라갈 수 없는 절벽: 연결 없음

`Jump` 하나로 Drop과 Boost를 뭉치지 않는다.

### 6.3 공용 실행 상태

기존 `DescentPhase`를 상승과 하강 공용 `TraversalPhase`로 교체한다.

```text
None
Pause
Airborne
Recovery
```

하강:

```text
DropStart 접근
→ Pause 12틱
→ Lerp + sin 아치로 jumpEnd까지 매 틱 이동
→ Recovery 15틱
```

상승:

```text
BoosterStart 접근
→ Pause
→ 수직 중심 보간으로 상층 BoosterLanding까지 매 틱 이동
→ Recovery
```

상승의 Pause·이동·Recovery 틱은 맵 높이와 플레이테스트 후 중앙 Config에서 확정한다.

### 6.4 공중 판정

- 순간이동 금지
- Airborne 중 위치·충돌·피격 판정 유지
- View는 Sim 위치만 보간
- 엔티티 분리는 Airborne 중에만 제외
- Airborne 중 스턴 처리 방식은 AI 승인 필요

### 6.5 결정론적 착지 분산

여러 적이 같은 점에 겹치지 않도록 링크에 검증된 착지 슬롯을 베이크한다.

```text
slotIndex = enemyId % landingSlotCount
landing = landingSlots[slotIndex]
```

- 런타임 랜덤 오프셋 금지
- 각 슬롯은 캡슐 점유 가능 검사 통과
- 슬롯이 모두 점유된 경우에도 결과가 고정되도록 낮은 슬롯부터 검사
- 유효 슬롯이 없으면 링크 진입을 대기하고 다음 틱 다시 검사

---

## 7. 코드 마이그레이션

### 삭제

- 적 하강 자체 판단과 PathLength 3회 비교
- `NearestDropEdge`, `PathLength`
- `DescentThreshold`, `DescentMinHeight`, `DescentEdgeReach`
- 순간이동식 착지
- `DescentPhase.ApproachEdge`

### 유지

- Pause → Airborne → Recovery의 3박자 텔레그래프
- 제작자가 정한 Drop 점 쌍
- 현재 Drop authoring 위치
- 공중 실제 위치와 착지 회복

### 변경·추가

- 기존 Drop 쌍을 그래프 노드·링크로 이전
- `MoveKind.Drop`, `MoveKind.Boost`
- `TraversalPhase`
- Booster 노드·링크 authoring
- 결정론적 착지 슬롯
- 상승·하강 공용 실행 모듈
- WorldHash와 Snapshot 필드 등록

---

## 8. 예측 엔진 연동

몹 추가 시 확인할 접점:

1. 새 지속 상태를 Snapshot·WorldHash에 등록
2. 새 플레이어 대응 행동이 필요한지 검토
3. 몹 중립적 위협 신호를 ThreatEvaluator에 제공
4. 새로운 이동 종류와 충돌 질의 반영
5. 최종 후보 60Hz 정밀 재실행

위협 신호 예:

```text
attackWindup
committedAttackDirection
attackActiveTick
activeProjectile
chargeCorridor
traversalAirborne
```

기존 MacroAction의 숫자값과 정렬 순서는 변경하지 않는다. 새 행동이 필요하면 뒤에 추가한다.

공중 적 런지는 단순 타깃 필터 변경이 아니라 이동·충돌·도착·낙하 계약이므로,
별도 승인 전까지 ActionGenerator에 추가하지 않는다.

---

## 9. 개발자에게 남은 확정 요청

### 우선순위 A — 구현을 막는 항목

- [ ] 근접 적 충돌 수치 네 가지 선택
- [ ] 일반 지상몹도 Drop을 사용하는지, 층이동몹만 사용하는지
- [ ] Booster 실제 위치와 사용 가능한 적
- [ ] 상승·하강 중 스턴 시 취소·낙하·완주 중 하나 선택
- [ ] 공중 적 런지 허용 여부

### 우선순위 B — 몹별 튜닝

- [ ] 원거리 Aim·투사체·부분 리드 승인
- [ ] 대시 중 18도 빗맞힘 사용 여부
- [ ] 돌진 적 전체 수치
- [ ] 비행 높이·장애물 회피 규칙
- [ ] Large HP를 3 또는 4로 확정

### 우선순위 C — 맵 완료 시

- [ ] Drop 링크와 Booster 링크 배치
- [ ] 링크별 landingSlots 베이크
- [ ] agentMask
- [ ] 층·금지·낙사·런지 금지 영역

---

## 10. 완료 조건

- 같은 스냅샷·입력으로 180틱 × 100회 WorldHash 일치
- 예측 후보 루프에서 NavMesh·Physics 직접 호출 없음
- 아래→위 역링크 없는 Drop을 거꾸로 사용하지 않음
- Booster를 사용할 수 없는 적이 상승 후보를 만들지 않음
- Drop·Boost 중 매 틱 실제 위치와 피격 판정 존재
- 여러 적이 같은 링크를 사용해도 착지 슬롯 선택 반복 일치
- 투사체 포함 Snapshot 복원 후 결과 일치
- 8·16·32·50마리 성능 측정

