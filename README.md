# PRECOG

> **Predictive Real-time Evasion & Combat Guidance**
>
> 결정론적 미래 예측 기반 1인칭 액션 게임

PRECOG는 난전의 현재 상태를 복제하고, 여러 미래를 실제 게임 규칙으로 시뮬레이션한 뒤 플레이어에게 세 가지 돌파 경로를 제안합니다. 플레이어는 미래를 확인하고 원하는 경로를 선택하지만, 마지막 실행은 직접 입력해야 합니다.

- [프로젝트 소개 Notion](https://app.notion.com/p/3a58074b31f58069a0d0fc08243ac288)
- [GitHub 저장소](https://github.com/madcamp-official/26s-w3-c3-04)

## 핵심 기능

### 미래 예측

- 현재 플레이어·적·투사체·쿨타임·맵 상태를 `SimWorld` Snapshot으로 복제
- 안전형·기회형·공격형 세 가지 미래 경로 계산
- Ghost Avatar의 위치와 자세로 이동·점프·대시·공격 시각화
- Slow-Aim에서 경로를 비교하고 선택
- Follow Mode에서 Perfect / Good / Miss 판정을 받으며 직접 실행
- 실행에 실패하면 되감지 않고 현재 실제 상태에서 일반 조작으로 복귀

### 전투와 게임 진행

- 1인칭 이동, 2단 점프, 4방향 대시
- 카타나 평타와 타깃 런지
- 근접·원거리·돌진·공중형 적과 보스
- 팬 낙하 스폰, 웨이브 전투, 아레나 게이트
- 타이틀 화면, 인트로 컷신, 다중 아레나, 보스 사망 연출
- 전투 HUD, 피격 피드백, 검격 VFX와 전투·예측 오디오

## 예측 엔진

예측과 실제 플레이는 별도의 규칙을 사용하지 않습니다. Live Game, Beam Search 후보, 최종 후보 재실행이 모두 동일한 결정론적 `SimStep.Run()`을 호출합니다.

```text
현재 SimWorld Snapshot
        ↓
조건에 맞는 행동 후보 생성
        ↓
15틱 동안 동일한 게임 규칙으로 실행
        ↓
안전·기회·공격 관점으로 점수 계산
        ↓
Beam Search로 상위 후보 유지
        ↓
최종 후보 재실행 및 WorldHash 검증
        ↓
Ghost Avatar와 HUD로 출력
```

최종 탐색 설정은 매크로 행동당 15틱, 후보 최대 16종, Beam Width 12, 최대 Depth 20입니다. 약 3,665개 노드만 평가하며, 적이 많아지면 Beam Width와 Depth를 결정론적으로 축소합니다.

## 조작법

| 입력 | 동작 |
|---|---|
| `WASD` | 이동 |
| 마우스 | 시점 이동 |
| `Space` | 점프 |
| `Shift + WASD` | 4방향 대시 |
| 마우스 왼쪽 | 일반 공격 |
| 마우스 오른쪽 | 타깃 런지 |
| `F` | 예측 시작 / 경로 전환 |
| 예측 중 마우스 왼쪽 | 경로 확정 |
| `Esc` | 예측 취소 / 메뉴 |

Follow Mode에서는 HUD에 표시되는 `WASD`, `Shift + W`, `Space`, 마우스 입력을 타이밍에 맞춰 수행합니다.

## 실행 방법

### 빌드 실행

저장소 루트의 `PrecogPrototype.exe`를 실행합니다.

### Unity Editor

1. Unity Hub에서 `PrecogPrototype` 폴더를 엽니다.
2. Unity `6000.5.4f1`을 사용합니다.
3. `Assets/_Project/Scenes/Level_Main.unity` 씬을 엽니다.
4. Play를 눌러 실행합니다.

## 기술 스택

- Unity 6.5 / C#
- Universal Render Pipeline / Shader Graph / Post Processing
- Cinemachine / Timeline / Animator
- Unity Input System / AI Navigation
- Deterministic 60Hz Simulation
- Snapshot / WorldHash / Beam Search / State Deduplication
- NavMesh / Prediction Baked Map Graph
- Unity Test Framework / PredictionLogs

## 프로젝트 구조

```text
PrecogPrototype/Assets/_Project
├─ Sim/          # 결정론적 이동·전투·적 AI·충돌
├─ Prediction/   # 행동 생성·Beam Search·평가·후보 재실행
├─ View/         # 입력·Ghost·HUD·카메라·VFX·오디오
├─ Prefabs/      # 플레이어·적·맵·무기 프리팹
├─ Art/          # 캐릭터·환경·머티리얼·애니메이션
└─ Tests/        # 결정론·재현성·경계 조건 회귀 테스트
```

## 개발 문서

게임 시뮬레이션이나 예측 시스템을 수정하기 전에 다음 문서를 확인해야 합니다.

1. [예측 통합 계약](docs/shared/PREDICTION_CONTRACT.md)
2. [적 시스템](docs/shared/ENEMY_SYSTEM.md)
3. [예측 최적화](docs/shared/OPTIMIZATION.md)

`PREDICTION_CONTRACT.md`가 구현의 최우선 기준입니다. Sim 상태가 변경되면 계약 버전, Snapshot, WorldHash와 회귀 테스트도 함께 갱신해야 합니다.

## 핵심 설계 원칙

> 미래를 보여주되, 마지막 실행은 플레이어에게 남긴다.

PRECOG는 자동 플레이가 아니라 판단 보조 시스템입니다. 복잡한 난전을 읽을 수 있는 선택지로 바꾸고, 플레이어가 직접 선택하고 수행하도록 설계했습니다.
