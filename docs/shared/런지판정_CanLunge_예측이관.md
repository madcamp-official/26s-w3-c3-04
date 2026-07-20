# 런지 판정 통합: 예측 → Sim `CanLunge` 이관 (예측 담당 요청)

**작성:** KJH(Sim/게임) → 예측 담당
**상태:** Sim 쪽 공개 API(`CanLunge`) **추가·머지 완료**(core-integrated, pushed). **예측 이관 대기.**

---

## 1. 요약

런지 "이 대상을 칠 수 있나 + 어디 착지하나" 판정이 지금 **두 곳에 따로** 있다.

| | 실제 게임(권위) | 예측(독립 재구현) |
|---|---|---|
| 위치 | `PlayerCombat` (private) | `ActionGenerator.CanTargetForLunge` |
| 착지 높이 | 대상 고도 `enemy.y + LungeAimUp` | 지면 스냅 `SampleGround → groundY` |
| 조준 기준 | 3D 조준 레이(yaw + aimPitch) | 수평 앞부채꼴(yaw만) |
| 착지 가능 | 없음(고도로 붙음) | `CanOccupyCapsule` 요구 |

Sim에 **공개 판정 API `CanLunge`** 를 추가했으니(내부는 실제 발동과 동일 경로), 예측이 자기 재구현을 버리고 이걸 호출하면 이중 구현이 사라진다.

---

## 2. Sim이 제공하는 것 (완료됨)

`PrecogPrototype/Assets/_Project/Sim/Combat/PlayerCombat.cs`

```csharp
// 런지로 targetId를 칠 수 있으면 true + 실제 착지점(대상 고도 반영)을 out으로 준다.
// 실제 발동(Step)이 쓰는 IsLungeable + TryLockDestination과 동일 경로 — 판정 규칙은 Sim 한 곳뿐.
public static bool CanLunge(in SimWorld w, in PlayerSim p, in SimServices svc,
                            int targetId, out Vector3 destination);
```

- 순수 함수(월드 상태 + 충돌 질의) → 결정론 불변.
- 실제 발동과 같은 코드를 타므로 "예측 됨 → 실제 안 됨" 괴리가 원천 소멸.
- **판정은 `p.yaw`/`p.aimPitch`(현재 조준) 기준**이다 → 아래 3번 규약 주의.

---

## 3. 예측이 해야 할 것

`ActionGenerator.cs`의 `CanTargetForLunge`(독립 판정) **삭제**하고, `FindTopLungeTargets`가 후보마다 `CanLunge`를 호출.

### 핵심 규약 — 대상을 향해 조준을 세팅한 PlayerSim을 넣을 것

`CanLunge`는 **플레이어의 현재 조준 기준**으로 판정한다. 예측은 "만약 이 대상을 조준한다면 칠 수 있나"를 알고 싶은 것이므로, 후보 대상을 **향하도록 `yaw`/`aimPitch`를 맞춘 가상 PlayerSim** 을 만들어 넣어야 실제와 일치한다. 그냥 현재 조준으로 부르면 다른 결과가 나온다.

```csharp
// 예시(개념) — 실제 AimDir 규약(Quaternion.Euler(pitch, yaw, 0) * forward)에 맞출 것
PlayerSim aimed = player;
Vector3 eye = player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
Vector3 to  = (enemy.pos + Vector3.up * (enemy.height * 0.5f)) - eye;
aimed.yaw      = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
aimed.aimPitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg; // 부호는 AimDir 규약 확인
if (PlayerCombat.CanLunge(in world, in aimed, in services, enemy.id, out Vector3 dest))
{
    // dest = 실제 착지점(대상 고도). 후보 채택.
}
```

- 상위 2대상 선정 정렬(각도 → 거리 → id)은 예측에 그대로 남긴다(선정 heuristic과 유효성 판정은 별개).
- `PREDICTION_CONTRACT.md`의 "예측 전용 판정 새로 안 만든다" 원칙과 합치.

---

## 4. 이관하면 바뀌는 동작

1. **공중 적 버그 수정** — 지면 스냅/캡슐 점유 게이트가 사라져, 낭떠러지·구덩이 위 부유 공중 적도 런지 후보가 된다(실제처럼).
   > ⚠️ **현재 core-integrated의 `ActionGenerator`엔 아직 옛 `SampleGround`+`CanOccupyCapsule`(190~193줄)이 남아 있다.** 즉 이 버그가 이 브랜치에 현존한다. 이관이 곧 이 수정이다.
2. **착지점·높이가 실제와 정확히 일치** — `groundY` → `enemy.y + LungeAimUp`. LOS 기준점·캡슐 체크도 실제와 동일.
3. **판정 기준: 수평 앞부채꼴 → 3D 조준** — 예측이 대상을 향해 조준을 맞추므로 각도 게이트가 사실상 통과되고, 유효 조건이 **사거리 + 높이허용 + 시야**로 단순해진다.

---

## 5. 설계 결정 필요 — "돌아서서 런지"

4-3의 귀결: **현재 안 보고 있는(옆·뒤) 사거리 내 적도 유효 후보**가 된다. 지금은 앞부채꼴 밖이면 후보를 아예 안 만들지만, 이관 후엔 "플레이어가 돌아서 조준하면 닿는다"고 보고 후보로 만든다.

- **전방향 허용(기본 이관)**: 실제로 조준만 맞추면 즉시 런지 가능하니 더 충실. 단 한 스텝 안에서 "즉시 180° 회전 조준"을 가정하는 과대근사.
- **앞부채꼴 유지**: 원치 않으면 예측이 `CanLunge` 호출 **전에 자체 각도 필터**를 하나 둔다(현재 정면 기준 dot/lateral 컷).

→ 이 선택은 예측 담당이 정한다. 선정 정렬은 어차피 정면 각도 우선이라, 앞에 적이 있으면 실무 차이는 작다(앞이 비었을 때만 갈림).

---

## 6. 리스크 / 결정론

- `CanLunge`는 순수 함수 → **결정론 불변**. 재현성 테스트는 그대로 통과.
- 단 **후보 집합이 바뀌므로 예측 결과(빔서치 경로)가 달라진다.** 특정 예측 결과를 못박은 골든 테스트가 있으면 재기준 필요.
- 후보는 top-2 캡이라 성능 영향 작음.
- Sim 공개 표면 +1(`CanLunge`) — 이미 `FindLungeTarget`/`FindEnemyIndex`가 public이라 관례 내.

---

## 7. 관련 위치

- Sim API: `PrecogPrototype/Assets/_Project/Sim/Combat/PlayerCombat.cs` — `CanLunge`(신규), 내부 `IsLungeable`/`TryLockDestination`(private, 규칙 원본).
- 예측 대상: `PrecogPrototype/Assets/_Project/Prediction/Actions/ActionGenerator.cs` — `CanTargetForLunge`(삭제 대상), `FindTopLungeTargets`(호출부 교체).
- 계약: `docs/shared/PREDICTION_CONTRACT.md`.
