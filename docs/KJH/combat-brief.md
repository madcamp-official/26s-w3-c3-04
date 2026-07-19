# 전투 세션 브리프 — 대체된 작업 기록

> 이 문서의 막기·칼등치기·질풍참 지시는 ADR-0007에 의해 대체되었다. 현재 구현 기준은 `docs/shared/PROJECT_MASTER_PLAN.md`와 ADR-0007이다.

이 문서는 **병렬 combat 세션**을 위한 지시문이다. 이 세션은 `rebuild` 세션(예측·몹 AI·나브)과
worktree로 분리되어 병렬 작업한다. **여기 적힌 경계를 지켜야 머지 충돌이 안 난다.**

## 0. 대전제

- 프로젝트는 **1인칭 칼 액션 + 예측(예지) 시스템**. 설계 정본은 [design/GDD.md](design/GDD.md) 2장(전투).
- 아키텍처: **Sim(복제 가능한 순수 로직) / Bridge(유니티 질의) / View(표현)** 분리.
  Sim은 GameObject·Rigidbody·NavMeshAgent 안 씀 (예측이 복제해서 굴려야 하므로).
- 결정론 필수: 시드 고정 `DetRng`만 사용, `UnityEngine.Random` 금지. 상태는 struct.

## 1. 이 세션이 소유하는 파일 (여기만 건드림)

```
Sim/Combat/         PlayerCombat, CombatResolve, PlayerCombatState, EnemyCombatState,
                    CombatHash + 신규 파일(CombatConfig, Attack, Block, Backstrike, Iaijutsu, Knockback 등)
View/Combat/        SwordView, CombatFx, WaveSpawner 등 신규
```

## 2. 절대 건드리지 말 것 (rebuild 소유·동결)

`Sim/Core/*` (SimConfig, PlayerSim, EnemySim, SimWorld, SimStep, WorldHash, InputCmd 등),
`Sim/Enemies/*`, `Sim/Player/PlayerMovement`, `Bridge/*`, `View/Bootstrap`, `View/Entities`, `View/Input`.

**필드가 더 필요하면**: `PlayerCombatState`/`EnemyCombatState`(네 소유)에 추가하고, 해시는
`CombatHash`(네 소유)에 반영. 공유 struct(PlayerSim/EnemySim)엔 손대지 마라 — 이미 `combat` 필드로 품고 있다.

## 3. 이미 뚫려 있는 접점 (pre-carve 완료)

- `InputCmd.attack`(좌클릭), `InputCmd.block`(우클릭 홀드) — InputReader가 이미 채워줌.
- `PlayerSim.combat` (PlayerCombatState), `EnemySim.combat` (EnemyCombatState) — 품고 있음.
- `SimStep`이 매 틱 호출: `PlayerCombat.Step(ref w, in cmd, in svc, dt)` (플레이어 이동 뒤),
  `CombatResolve.Run(ref w, in svc, dt)` (적 이동 뒤). **이 두 함수 채우는 게 핵심.**
- `EnemyMovement`가 `e.combat.stunTicks > 0`이면 AI 정지 — 너는 stunTicks에 값만 쓰면 된다.
- `SimServices` = `ICollision`(CapsuleCast) + `IPathfinder`(NavMesh). 넉백 벽 체크에 ICollision 써라.

## 4. 만들 것 (GDD 2장 기준, 전부 잠정 수치)

**Phase 1 (먼저)**: 평타 + 대미지/스턴/HP/처치 + 대량 소환 + 히트스톱
- 평타(좌클릭): 부채꼴 광역, 이동보정 없음, 대미지 1 + 스턴 0.5초(30틱), 후딜 스킬 캔슬 가능
- 적 HP: 일반 2 / 중형 3. 스턴 0.5초. 2대에 처치.
- View: 칼(직육면체) 휘두르기, 적 상태색, 처치 연출, **히트스톱**(짧은 timeScale 0), WaveSpawner(30~50)

**Phase 2**: 막기(우클릭 홀드, 정면, 가드게이지) + 칼등치기(막기중 좌클릭, 큰 넉백=순간이동+화면보간,
게이지 1/4) + 질풍참 대미지(관통) + 콤보 캔슬 리듬.

## 5. 넉백·질풍참은 "순간이동 + 화면 보간" (rebuild 세션 합의)

Sim에선 궤적 계산 없이 **끝점으로 순간이동**(벽/낭떠러지면 거기서 멈춤/낙하). 화면은 View의
보간(EntityViews가 prev→now Lerp)이 부드럽게. 앞뒤 딜레이(멈칫/회복)는 남긴다.
`Knockback.cs`는 "끝점 계산 + 도중 벽 체크(ICollision)"만.

## 6. 테스트

- 큐브맵(`SampleScene`)에서 Play → AutoBoot이 [Main] 생성. 적 수를 늘려 난전 테스트.
- Synty Demo 씬은 NavMesh read 경고 있음(에디터는 됨) — 전투 손맛엔 큐브맵이 편하다.

## 7. 진행 규칙

- 마일스톤마다 커밋. `combat` → `rebuild` 머지는 KJH가 결정.
- 공유 파일을 꼭 고쳐야 하는 상황이 오면 **멈추고 KJH에게 보고**(임의 수정 금지).
