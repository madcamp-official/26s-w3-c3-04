# 26s-w3-c3-04
몰입캠프 26s-w3-c3-04 프로젝트 repository

## 개발 정본

게임·예측·맵·리듬 실행 코드를 변경하기 전에 반드시 다음 문서를 확인한다.

1. [게임 런타임 ↔ 예측 엔진 통합 계약](docs/shared/PREDICTION_CONTRACT.md)
2. [몹 시스템·층이동·예측 연동](docs/shared/ENEMY_SYSTEM.md)
3. [예측 최적화 계획](docs/shared/OPTIMIZATION.md)

`PREDICTION_CONTRACT.md`가 최우선 구현 기준이다. 계약과 다른 코드를 먼저 작성하지 않고,
필요한 변경이면 계약 버전·Snapshot·WorldHash·회귀 테스트를 함께 갱신한다.
