# Fan 스폰 시스템 — 설계 · 구현 계획 (확정본)

천장 Fan을 "몹이 튀어나오는 실제 출구"로 쓰는 스폰 구조·연출. 모든 결정 확정됨. 구현 순서·파일·검증까지 포함.

---

## 0. 확정된 설계 결정

| # | 결정 |
|---|---|
| 1 | **펄스(launchTicks) 폐기.** 스폰 = SpawnDrop 링크 타기로 일원화. 링크가 여러 개면 **스폰 순번 순환**(`index % linkCount`)으로 결정론 선택. 착지 슬롯도 순번으로 결정론 배정. |
| 2 | **예측 스킬 사용 중엔 스폰 정지**(기존 `spawnLocked` 유지·활용). 예측 창은 몹 집합이 고정 → 결정론 문제 없음. |
| 3 | **Fan 애니메이션 = 순수 연출(View).** 몹의 sim 스폰 지점은 **Fan의 고정 입 위치**. 애니 하강과 무관. |
| 4 | **층이동몹 로직 살려둠**(MobilityType.Traversal · 근층/원층 · AscendRestricted 삭제 안 함). |
| 5 | **VFX = "발생/실체화"**("벌어짐"은 오타). 외곽선이 위에서부터 X레이처럼 나타남 → 진짜 메시가 실체화되며 낙하. |

---

## 1. 현재 상태

- `Arena_3 / _SpawnBox / _Ceiling` = **Fan 10개** (LODGroup·BoxCollider·PointLight 완비). 배치 완료.
- **끊긴 것**: 원래 SpawnMarker 큐브 삭제됨 → 스폰 시스템이 Fan을 출구로 인식 못 함.

## 2. 기존 데이터 모델 (다른 세션 소유 — 재사용)

```
ArenaWaves → Wave[] → PipeEmission[] pipes
PipeEmission = { Transform marker(=Fan), float startDelay(="준비"), float interval, MobEmit[] mobs(="엔트리") }
MobEmit = { MobKind kind, float intervalOverride }
```
- 스폰 스케줄·순서·간격·번갈아는 **이미 WaveRunner가 처리**. 여기에 공백·링크·연출을 얹는다.

---

## 3. Sim / View 분리

| Sim (결정론) | View (자유) |
|---|---|
| 스폰 스케줄·엔트리·공백 타이밍 | Fan 애니(준비·또잉·수납) |
| SpawnDrop 링크 즉시 타기·착지 | 스폰 실체화 VFX |
| 예측 중 스폰 정지 | |

Fan 애니·VFX는 **스폰 이벤트를 관찰해 재생만**. sim 되먹임 없음. "준비"는 sim의 `startDelay`를 그대로 쓴다.

---

## 4. 구현 항목 (확정 스펙)

### ① Fan 스폰 컴포넌트 + 붙이는 툴  *(신규 · 안전)*
- **`FanSpawn`** (Fan 루트에 붙임):
  - `Vector3 exitEuler` — 출구 방향(기본 `0,90,90`, 조절).
  - `List<TraversalLink> dropLinks` — 이 Fan의 SpawnDrop 링크들(⑤가 채움).
  - 입 위치(mouth) = Fan의 특정 로컬 지점(몹 스폰 원점).
- **붙이는 툴** `Tools/스폰/Fan 스폰 설정`:
  - 선택 Fan들에 `FanSpawn` 부착 + `exitEuler` 일괄 조절.
  - `ArenaWaves`의 `PipeEmission.marker`로 자동 등록(어느 웨이브에 넣을지 선택).

### ② Fan 애니메이션 `FanSpawnActor`  *(신규 · View · 비결정)*
`WaveRunner` 상태를 관찰. **담당 Fan만 움직임.**
1. **담당 웨이브 시작 → 준비(≈`startDelay`)**: 90° 회전 + Y 하강 **동시**. **기계식**: 목표 오버슈트 → 스프링 안착(우웅철컥).
2. **몹 소환마다 약한 또잉**(공백 엔트리엔 안 함).
3. **웨이브 소환 종료 → 90° 복귀 + 기계식 수납.**
4. 담당 아닌 Fan 정지.
- 담당 판정: `ArenaWaves`에서 이 Fan을 marker로 쓰는 `PipeEmission`이 현재 웨이브에 있으면 담당.

### ③ 공백(대기) 엔트리  *(MobEmit·WaveRunner — 다른 세션 ⚠️)*
- `MobEmit`에 **`bool isGap`** 추가(명시적 항목).
  - 공백 = 몹 안 나옴 + 또잉 안 함, **간격 시간만 소비**.
- 담당 규칙: `PipeEmission`이 있으면(공백만 있어도) **참여**. 없으면 **정지**.

### ④ SpawnDrop 링크 + 즉시 타기  *(TraversalLink·EnemyMovement·Main·그래프 — 다른 세션 ⚠️ · Sim 결정론)*
- `TraversalLinkKind.SpawnDrop = 3` **추가**(기존 유지).
- 스폰 흐름: WaveRunner가 몹 소환 요청 → **펄스 대신** 즉시 SpawnDrop 링크 타기 시작(`StartTraversal`→Airborne 재사용).
  - 링크 선택 = `PipeEmission` 소환 순번 `% dropLinks.Count`.
  - 착지 슬롯 = 순번 기반 결정론 배정.
- NavMeshLink(평상시) + 그래프(예측) 양쪽 반영. **결정론 테스트로 검증.**
- `Main.SpawnEnemyLaunched`(펄스) → `SpawnEnemyDropping(pos, kind, link, slot)` 로 교체.

### ⑤ 링크 자동배치 툴  *(신규 · 안전)*
- `Tools/스폰/Fan 드롭 링크 자동배치`:
  - 선택 Fan마다 **SpawnDrop 링크 N개** 생성 → 착지점을 **여러 깊이·위치로 분산**(단차 섞기).
  - 착지점은 콜라이더 레이캐스트로 바닥 찾아 스냅. `FanSpawn.dropLinks`에 등록.

### ⑥ 스폰 실체화 VFX  *(신규 · View · 별도 대형작업)*
- 몹 소환 순간: **외곽선(테두리)이 위에서부터 X레이처럼 나타남 → 진짜 메시 실체화**, 낙하와 동기.
- 디졸브/실체화 셰이더(오브젝트 Y 임계값 애니). 몹 뷰(EntityViews)에서 스폰 시 재생.
- ①~⑤와 완전 분리, 마지막.

---

## 5. 소유권 / 충돌 주의

| 항목 | 소유 | 위험 |
|---|---|---|
| ① Fan 컴포넌트·툴 | 신규 | 없음 |
| ② FanSpawnActor | 신규 | WaveRunner 상태 읽기 훅 필요(소량) |
| ③ 공백 엔트리 | **다른 세션**(MobEmit·WaveRunner) | 병합 충돌 |
| ④ SpawnDrop·타기 | **다른 세션**(TraversalLink·EnemyMovement·Main·그래프) | 병합 + 결정론 |
| ⑤ 링크 자동배치 | 신규 | 없음 |
| ⑥ VFX | 신규(EntityViews 훅) | 없음 |

③④는 다른 세션 파일 수정 → **작게·명확히**, 커밋 메시지에 명시.

---

## 6. 구현 계획 (처음 → 끝)

### 단계 1 — 데이터 뼈대 (Sim·안전한 추가만)
- `TraversalLinkKind.SpawnDrop = 3` 추가.
- `MobEmit.isGap` 추가.
- `FanSpawn` 컴포넌트 생성.
- **검증**: 컴파일 그린. 기존 동작 불변(추가만).

### 단계 2 — ① Fan 붙이는 툴
- `Tools/스폰/Fan 스폰 설정` — FanSpawn 부착·방향·PipeEmission 등록.
- **검증**: Fan 선택→실행→FanSpawn 붙고 방향 기즈모 보임.

### 단계 3 — ⑤ 링크 자동배치 툴
- `Tools/스폰/Fan 드롭 링크 자동배치` — SpawnDrop 링크 N개 분산 생성.
- **검증**: 링크 기즈모가 Fan에서 여러 착지점으로. NavMesh 위 착지 확인.

### 단계 4 — ④ SpawnDrop 즉시 타기 (Sim 핵심)
- 펄스 제거 → `SpawnEnemyDropping`. 링크 순번 선택·슬롯 결정론.
- 그래프 베이커가 SpawnDrop 링크 반영.
- **검증**: 결정론 테스트 통과 + 플레이(몹이 Fan에서 여러 깊이로 낙하).

### 단계 5 — ③ 공백 엔트리
- WaveRunner가 isGap 항목은 소환 건너뛰고 간격만 소비.
- 담당 규칙 반영.
- **검증**: 공백 섞은 엔트리로 번갈아 소환되는지.

### 단계 6 — ② Fan 애니메이션
- `FanSpawnActor` — 준비(오버슈트 스프링)·또잉·수납. WaveRunner 상태 관찰.
- **검증**: 담당 Fan만 준비→소환마다 또잉→수납. 공백엔 또잉 없음.

### 단계 7 — ⑥ 스폰 VFX
- 실체화 셰이더 + EntityViews 스폰 훅.
- **검증**: 외곽선 위→아래 실체화가 낙하와 동기.

---

## 7. 튜닝 파라미터 (구현 후 인게임 조절)
Fan 하강량·오버슈트 폭·스프링 강성/감쇠·또잉 세기·링크 개수·착지 분산 반경·VFX 실체화 속도. 전부 Inspector/콘솔 노출.

---

## 8. 다른 세션에 공유할 것
- `MobEmit.isGap` 추가(단계 5) — WaveRunner 소환 루프 수정.
- 펄스 → SpawnDrop 교체(단계 4) — EnemyMovement/Main 스폰 경로 변경, 결정론 재검증.
- `TraversalLinkKind.SpawnDrop` 추가 — 그래프 베이커·NavMeshLink 반영.
