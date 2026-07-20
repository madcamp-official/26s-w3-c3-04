# 제안: 런지 유효성/착지점 판정의 Sim 공개 API 화 (예측·Sim 이중 구현 제거)

상태: **완료.** KJH가 `PlayerCombat.CanLunge` 공개 API를 커밋(`85cfd10`, core-integrated)했고,
예측 쪽(`ActionGenerator.CanTargetForLunge` 독립 재구현)을 삭제해 그 API로 완전히 교체했다
(`CanLungeTarget` 헬퍼가 후보를 바라보도록 조준을 세팅한 가상 `PlayerSim`을 만들어 위임).
아래 내용은 그 과정의 기록으로 남긴다.

**2026-07-20 갱신**: 아래에서 "코너 케이스"라고 적었던 착지 높이 불일치가, 실제로는 **표준
hover 고도(FlyHoverOffset=2m)인 공중 적 전반에서 거의 항상 실패하는 심각한 버그**였음이
실전(F키 자동실행) 확인으로 드러났다 — `ActionGenerator.CanTargetForLunge`가 착지점 지면
스냅(`SampleGround(destination, 2f, ...)`)을 요구했는데, 실제 필요 레이 거리가 정확히
그 한계치(2.5m)라 다층 지형에서 거의 항상 실패해 **런지 후보 자체가 생성 안 됨**(공중 유닛에
접근 시도조차 안 하는 원인이었음). 이 조건은 real Sim(`TryLockDestination`)엔 애초에 없던
조건이라, **예측 쪽 임시 수정으로 지면 스냅/캡슐 점유 체크를 완전히 삭제**하고 착지 높이도
`enemy.y+LungeAimUp`로 맞췄다(`ActionGenerator.cs`, 회귀 테스트
`ActionGenerator_ProducesLungeStrike_EvenWhenNoGroundBeneathLandingSpot` 추가). 이 문서가
제안하는 근본 통합(Sim 공개 API)은 여전히 유효하고 바람직하지만, **긴급 차단 버그는 이미
예측 쪽 단독 수정으로 해소됨** — 아래 제안은 "이중 구현 자체를 없앤다"는 장기 정리 목적으로 유지.

예측 쪽 공중 타겟팅 보완(Phase 0~4)은 이 제안 없이도 동작한다. 아래 이중 구현으로 인한
잠재 불일치를 근본적으로 없애려면 Sim 쪽 공개 API가 필요하다. 예측 코드는 이 인터페이스에
맞춰 리팩터할 수 있게 준비돼 있다.

## 문제: 런지 판정이 두 곳에 다르게 존재한다

우클릭(런지)의 "이 대상을 칠 수 있는가 + 어디에 착지하는가" 판정이 현재 두 군데에 있다.

| | 실제 게임(권위) | 예측(독립 재구현) |
|---|---|---|
| 유효성 | `PlayerCombat.IsLungeable` (private) | `ActionGenerator.CanTargetForLunge` |
| 착지점 | `PlayerCombat.TryLockDestination` (private) | 위 함수 내부 |
| 착지 높이 | **대상 고도** `enemy.pos.y + LungeAimUp(0.4)` | **지면 스냅** `SampleGround → groundY` |
| 조준 기준 | 3D 조준 레이(`yaw + aimPitch`) 수직거리 | 수평 평면 cone(along/lateral) |

핵심 불일치는 **착지 높이**다. 실제 런지는 공중 대상과 같은 고도에 붙지만(그래서
"우클릭 → 좌클릭"이 성립), 예측의 유효성 게이트는 대상 아래 **지면**이 점유 가능한지를
본다. 지상 위 공중 적은 대개 문제없지만, **낭떠러지·구덩이 위를 부유하는 공중 적**은
아래에 설 지면이 없어 예측이 런지 후보를 **부당하게 탈락**시킨다(실제로는 대상 고도로
붙을 수 있는데도). 반대로 조준 레이(pitch) 차이 때문에 실제로는 못 잡는 각을 예측이
잡는다고 판단할 수도 있다.

## 제안: Sim에 공개 판정 API 하나를 추가

`Game.Sim.PlayerCombat`에 아래를 공개한다(내부 private 로직 재사용, 규칙 중복 X):

```csharp
// 런지로 targetId를 칠 수 있으면 true + 실제 착지점(대상 고도 반영)을 돌려준다.
// 실제 발동(PlayerCombat.Step)이 쓰는 IsLungeable + TryLockDestination과 같은 경로.
public static bool CanLunge(in SimWorld w, in PlayerSim p, in SimServices svc,
                            int targetId, out Vector3 destination);
```

- 내부적으로 기존 `IsLungeable`(레이 기준 perp/along) + `TryLockDestination`(대상 고도 착지)
  을 그대로 호출한다. 즉 **판정 규칙은 Sim 한 곳에만** 남는다.
- 예측은 후보 생성 시 이 함수만 부른다 — 착지점을 실제와 같은 기준(대상 고도)으로 얻는다.

## 예측 쪽 마이그레이션(이 제안 승인 후)

- `ActionGenerator.CanTargetForLunge` / `FindTopLungeTargets`의 독립 판정을 삭제하고
  `PlayerCombat.CanLunge`로 대체. 상위 2대상 선정 정렬(각도→거리→id)만 예측에 남긴다.
- `docs/shared/PREDICTION_CONTRACT.md`의 "예측 전용 판정 새로 안 만든다" 원칙과 합치.
- `ActionGenerator.cs`의 기존 주석(“근본 해결은 게임 개발자 승인 필요”)이 가리키는 항목이 이것.

## 리스크 / 결정론

- `CanLunge`는 순수 함수(월드 상태 + 충돌 질의)라 기존 결정론 불변.
- 실제 발동 경로와 같은 코드를 타므로 "예측이 된다 했는데 실제로는 안 됨" 류의 괴리가 사라진다.
- Sim 공개 표면이 하나 늘지만, 이미 `FindLungeTarget`/`FindEnemyIndex`가 public이라 관례 내.

## 범위 밖(이번 보완에 포함되지 않음)

착지 높이/지면 요구 관련 긴급 버그는 위 2026-07-20 갱신에서 예측 쪽 단독 수정으로 이미
해소됐다. 이 제안이 남겨두는 범위는 순전히 "판정 로직이 두 곳에 따로 존재한다"는 유지보수
리스크 제거(조준 레이 방식 등 세부 판정 방식까지 완전히 일치시키는 것)다.
