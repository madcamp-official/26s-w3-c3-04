# 잔상(예측 고스트) 아바타 작업 인수인계

> 이 문서는 Cowork(클라우드) 세션에서 진행하던 작업을 Claude Code(로컬, Unity MCP 연결)로
> 넘기기 위한 요약이다. 코드 변경은 이미 디스크에 반영돼 있다.

## 목표

예측(예지) 연출의 **잔상(afterimage/ghost)** 을 기본 캡슐에서 **주인공 히어로 아바타**로 바꾸고,
나아가 걷기·베기·점프처럼 **실제로 움직이는 잔상**으로 만든다. 대시·런지는 정지 스냅샷으로 충분.

## 1단계 — 잔상 메시 교체 (완료)

수정 파일: `PrecogPrototype/Assets/_Project/View/Prediction/PredictionController.cs`

- `PredictionController`는 **MonoBehaviour가 아님**(순수 C# 클래스, `Main.cs`가 `new`로 생성).
  따라서 인스펙터 슬롯이 없어서, 프리팹은 `Resources.Load<GameObject>("PlayerGhost")`로 로드한다.
- 잔상 생성이 `MakeGhostBody(name, capsuleScale)` 하나로 통합됨. 프리팹 있으면 Instantiate,
  없으면 기존 캡슐로 폴백. `MakeGhostMark()` / `MakeRevealGhost()`가 이걸 호출.
- 프리팹의 모든 자식 Renderer에 GhostMat 적용, Collider 제거.
- **레이어**: 잔상은 accent 카메라가 `PredictionAccentLayer(30)`만 렌더링한다.
  프리팹은 메시가 자식에 있으므로 `SetLayerRecursive`로 전 자식 레이어를 30으로.
- **SkinnedMesh 컬링 방지**: `smr.updateWhenOffscreen = true` (루트만 옮기면 바운즈가
  원점에 남아 프러스텀 컬링돼 안 보이는 문제).
- `SetMarkColor`는 `GetComponentsInChildren<Renderer>()`로 전체에 색 적용.
- `PivotYOffset` / `ghostHeightOffset(=0)` 으로 발 높이 보정.
- 진단 로그 `[예측/진단]` 1회 출력(렌더러 수·SkinnedMesh 여부·스케일·바운즈). 나중에 제거 가능.

### 프리팹/리그 상태 (완료)

- `PlayerGhost` 프리팹 = 빈 루트 + Meshy 히어로 FBX 자식. 아무 `Resources` 폴더에 저장됨.
  루트 스케일 **~90** (원본 메시가 2cm라 사람 크기로 키움). 코드는 위치·회전만 덮어쓰고
  **스케일은 안 건드리므로** 프리팹 스케일이 유지된다.
- 히어로 FBX(`Art/Characters/Meshy_AI_Hooded_Mech_Character_0720153246_image-to-3d-texture_obj.fbx`)를
  **Generic → Humanoid로 변환 완료.** 스켈레톤이 `mixamorig:*` (Mixamo 리그)라 매핑이 깨끗하게 잡힘.
  생성된 아바타 서브에셋 이름: `..._objAvatar`.

## 2단계 — 움직이는 잔상 (구현 완료, 2026-07-22 · 플레이 검증 대기)

구현 결과 요약:

- 에셋(Unity MCP로 처리): `PlayerGhost` 프리팹의 FBX 자식에 **Animator**(히어로 Avatar,
  `applyRootMotion=false`, `cullingMode=AlwaysAnimate`, `updateMode=UnscaledTime`) 추가.
  Mixamo 클립 4종을 `Assets/_Project/Prefabs/Resources/`에 `GhostRun/GhostWalk/GhostJump/GhostSlash.anim`
  으로 복제(전부 `isHumanMotion=true` 확인).
- 코드: `PredictionController`에 `LoadGhostClips` / `RegisterGhostRig` / `PoseGhost` /
  `PoseGhostByTravel` / `PoseGhostByAction` / `BuildRouteDistances` 추가.
  `AnimationClip.SampleAnimation`으로 잔상마다 다른 시점 한 프레임을 구워 얼린다.
  이동 트레일(헤드+잔상 32개)은 **경로 누적 거리 기반 위상**(제자리 회전 구간엔 다리가 안 돔),
  액션 잔상은 종류별 클립·특징 시점. 대시=Run 고정 시점, 런지=Slash 준비 자세(T포즈 회피).
- 튜닝 값은 전부 `PredictionConfig`의 `Ghost*` 상수(스트라이드 3.4m, 각 normalizedTime,
  샘플 양자화 `GhostPoseSampleRate=30`). 성능이 문제면 샘플레이트를 12쯤으로 낮추면
  재샘플 횟수가 크게 준다(포즈가 살짝 스텝지지만 정지 연출이라 어색하지 않음).
- 이동 클립은 Run 채택(Walk는 로드해 두고 Run 없을 때 폴백으로만 사용).

### 3단계 — 잔상 유지 + 산데비스탄 밀도 (2026-07-22)

- 이동 트레일이 "헤드를 따라오다 페이드되는 꼬리"에서 **경로상 고정 지점에 한 번 찍히고
  그대로 남는 분신**으로 바뀜(`PreviewAfterimageStepMeters` 간격, `PathIndexAtDistance` 역함수).
  위치가 고정이라 포즈 샘플링도 잔상당 1회뿐.
- 밀도: 간격 0.28m / 최대 190장 — 스트라이드 3.4m 기준 걷기 한 사이클당 12장이라
  옆으로 훑으면 연속 동작으로 읽힌다. 알파는 겹침을 감안해 0.16.
- **성능 주의**: `updateWhenOffscreen=true`(매 프레임 CPU 바운즈 재계산)를 버리고
  `localBounds`를 크게 잡아 컬링을 사실상 끄는 방식으로 교체했다 — 잔상 수백 개에서
  이건 필수. 되돌리지 말 것.
- 잔상 풀 145개는 `PreWarmRoutePools`가 게임 시작 시 한꺼번에 만든다(로딩 비용, F 누를 때
  프레임 스파이크를 피하기 위한 기존 설계 그대로).
- 경로 표시 선(LineRenderer)은 `PredictionConfig.ShowRoutePathLine = false`로 껐다(코드는 유지).

### 4단계 — 대시 포즈 클립 자작 (2026-07-22)

- Mixamo에 대시/회피 클립이 없어서 **방향별 대시 포즈를 직접 구웠다.**
  `Editor/GhostDashPoseBaker.cs` → 메뉴 `Tools/예측/대시 잔상 포즈 굽기`.
  Run 클립 t=0.13(스트라이드 프레임)의 휴머노이드 머슬 값을 뽑아 손으로 수정 → 1프레임
  휴머노이드 클립으로 저장. 결과: Resources의 `GhostDashForward/Backward/Left/Right`.
- **머슬 부호는 이 리그에서 렌더로 실측 확인함**: Front-Back(− 앞/+ 뒤), In-Out(+ 바깥),
  Left-Right(− 왼/+ 오른), Down-Up(+ 위), Lower Leg Stretch(+ 폄/− 굽힘).
- 포즈 확인 방법(추측 금지, 반드시 눈으로 볼 것): `UnityEditor.PreviewRenderUtility`로
  `BeginStaticPreview` → `AddSingleGO` → `Render` → `EndStaticPreview` → `EncodeToPNG`.
  실루엣만 보이는 마젠타 렌더라 포즈 판단에 오히려 좋다. 여러 후보를 아틀라스로 한 장에
  모아 비교하면 반복이 빠르다.
- **액션 잔상은 진행 방향이 아니라 플레이어 yaw를 향한다**(`GhostPlacement.useFacingYaw`).
  옆/뒤 대시 포즈는 "정면을 본 채 옆으로 민다"를 전제로 구웠기 때문에, 이동 방향으로
  돌려버리면 전부 앞 대시처럼 보인다. 되돌리지 말 것.
- **몸통 눕힘은 클립이 아니라 배치 회전이 담당한다.** `AnimationClip.SampleAnimation`은
  휴머노이드 클립의 루트 회전(RootQ)을 적용하지 않는다(실측 확인) — 클립에 넣어봐야 무시된다.
  그래서 각도는 `PredictionConfig.GhostDashForwardPitch(55) / BackwardPitch(-40) / SideRoll(30)`에
  두고, 런타임은 `GhostPlacement.pitch/roll`로, 에디터 미리보기는 `GhostDashPoseBaker.BodyTiltFor`로
  같은 값을 쓴다. **포즈를 다시 구우면 이 각도도 같이 맞출 것.**
- 포즈는 사용자 제공 컨셉아트 3장 기준으로 다시 잡았다(2026-07-22 2차):
  앞=스프린트 발진(깊게 눕고 뒷다리 길게), 뒤=몸 젖히고 두 다리 앞으로 뻗어 밀어냄,
  옆=**얼굴은 정면인 채 몸만 roll**(목·머리를 반대로 세워 정면 유지 — 앞 대시와의 결정적 차이).
- 확인용 메뉴: `Tools/예측/대시 잔상 포즈 씬에 세우기` / `치우기`. 미리보기도 배치 회전을
  적용하므로 게임에서 보이는 모습과 같다.
- 3차 수정(피드백 반영):
  · 뒤 대시 — 눕힘 −40° → **−20°**. 두 다리를 수평으로 뻗던 걸 "앞다리는 앞으로 디디고
    뒷다리는 몸 아래서 지지"로 바꿔 **두 발이 땅 가까이** 남게 했다(넘어지는 그림 방지).
  · 옆 대시 — 다리 좌우 벌림(IO 0.55/0.35 → **0.15/0.15**)을 없애 뜬 다리를 디딤발에 붙였고,
    목·머리 반대굽힘을 0.60 → **0.90**으로 올려 머리가 거의 서 있게 했다.
    디딤발 쪽 팔은 뒤로(`pushArmFB = +0.40`).
  · 앞 대시는 그대로.
- **좌우 굽힘(Left-Right) 부호 주의**: 이름과 달리 이 리그에선 roll이 +(왼쪽)일 때
  반대굽힘이 **−쪽**이다(렌더로 실측). 직관으로 뒤집지 말 것 — 뒤집으면 머리가 대시 방향으로
  더 꺾인다(실제로 한 번 그렇게 틀렸다).

### (원래 계획 — 참고용)

### 핵심 사실

- 잔상 데이터: `PredictedRoute.ghostFrames`(위치·yaw만) + 평행 리스트 `actionMarkers`(각자 `type`).
- `PredictedActionType`(= `Game.Prediction`, `Prediction/Results/PredictedFrame.cs`):
  `Jump, DashForward, DashBackward, DashLeft, DashRight, Attack, Lunge`.
- 두 잔상 경로:
  - `UpdateGhostMarks` — 액션 지점 정지 잔상(actionMarkers와 인덱스 대응, 단 마지막 프레임은 액션 없어도 추가).
  - `UpdateRevealTrails` — 경로를 따라 움직이는 트레일 헤드 + 뒤따르는 잔상(= 이동/걷기 구간).
- 팔 포즈 시스템(`PosePlayer`/`PoseCombatDriver`)은 **1인칭 KatanaViewmodel(팔+칼)** 전용이라
  전신 잔상 아바타와 **골격이 다르다.** 재활용 불가.

### 동작 → 클립 매핑 (설계)

| 잔상 | 소스 | 클립 | 방식 |
|---|---|---|---|
| 이동 트레일(걷기/달리기) | `UpdateRevealTrails` | Run 또는 Walk | 거리 기반 위상으로 샘플 |
| Attack 잔상 | `UpdateGhostMarks` type=Attack | Slash | 스윙 중간~임팩트 |
| Jump 잔상 | type=Jump | Jump | 점프 특징 프레임 |
| Dash 4종 | type=Dash* | (정지 포즈) | — |
| Lunge 잔상 | type=Lunge | (정지 포즈) | — |

### 재사용할 Mixamo 클립 (전부 존재, 골격 동일 mixamorig)

- Run: `Art/FootSoldier/FootSoldier_Run.fbx`
- Walk: `Art/ObsidianSentinel/ObsidianSentinel_Walk.fbx` (또는 GunSoldier_Walk, MonolithSentinel_Walk)
- Jump: `Art/ObsidianSentinel/ObsidianSentinel_Jump.fbx`
- Slash: `Art/FootSoldier/FootSoldier_Slash.fbx`

### 세팅 (Unity MCP로 자동화 가능 — Claude Code에서 직접)

1. `PlayerGhost` 프리팹(자식 FBX)에 **Animator** + 히어로 Avatar(`..._objAvatar`) 지정.
   `applyRootMotion=false`, `cullingMode=AlwaysAnimate` (accent 카메라만 렌더 → 메인 카메라
   컬링에 안 죽게).
2. 위 4개 클립을 각각 독립 `.anim`으로 복제해 아무 `Resources` 폴더에 넣고 이름:
   `GhostRun`, `GhostWalk`, `GhostJump`, `GhostSlash`.
   (Humanoid 클립 복제본은 muscle 커브 유지 → 리타게팅 샘플 가능)

### 런타임 코드 계획 (PredictionController.cs)

- `Resources.Load<AnimationClip>("GhostRun")` 등으로 클립 로드(없으면 정지 포즈 폴백).
- 각 잔상 인스턴스마다 `AnimationClip.SampleAnimation(ghostGO, timeSec)`로 특정 시점 자세 굽기.
  - Humanoid 샘플은 대상 GO에 Animator+유효 Avatar 필요(위 세팅). 잔상마다 다른 time으로 얼려
    "움직이는 잔상" 구현.
  - 위치·회전은 샘플 후 기존 배치 코드(`PlaceRevealBody`/`UpdateGhostMarks`)로 덮어써 배치 유지.
- 이동 트레일: 경로 진행 거리 → 클립 정규화 시간(다리 사이클). 액션 잔상: type별 클립 + 특징 시점.
- **먼저 이동 트레일 걷기/달리기 한 종류만** 넣어 파이프라인(Animator+샘플+레이어+머티리얼) 검증 후
  Slash·Jump로 확장.

## 열려있는 결정

- 이동 트레일: Run vs Walk (예측 이동이 빠른 편 → 기본 Run 추천).
- Attack/Jump 잔상의 "특징 시점"(normalizedTime) 값 튜닝.

## 참고 파일

- `View/Prediction/PredictionController.cs` (수정 중)
- `View/Prediction/PredictedRoute.cs`, `RealRoutePreview.cs`
- `Prediction/Results/PredictedFrame.cs` (PredictedActionType)
- `View/Combat/PosePlayer.cs`, `PoseCombatDriver.cs` (1인칭 팔 — 참고용, 골격 다름)
- `Sim/Core/PlayerSim.cs`, `Sim/Player/PlayerMovement.cs` (jumpCount·dashTicks 등 상태)
- `Editor/ClipBuildTool.cs` 등 (에디터 툴 관례, 메뉴는 `Tools/...`)
