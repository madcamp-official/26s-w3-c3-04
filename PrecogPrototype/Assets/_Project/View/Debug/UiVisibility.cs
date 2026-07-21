using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 게임 UI 표시 스위치. 스크린샷·뷰모델 작업처럼 화면을 가리면 곤란할 때 끈다.
    ///
    /// 개발 패널(콘솔·F1·F2·F3)은 <b>끄지 않는다</b> — 그것들로 조작해야 하므로.
    /// 끄는 대상은 전투 HUD·예측 표시 같은 <b>게임 UI</b>다.
    ///
    /// 각 UI는 OnGUI 첫 줄에서 <see cref="Hidden"/>을 보고 스스로 빠진다.
    /// </summary>
    public static class UiVisibility
    {
        /// <summary>게임 UI를 숨기는가.</summary>
        public static bool Hidden { get; private set; }

        public static void Set(bool hidden) => Hidden = hidden;
        public static bool Toggle() { Hidden = !Hidden; return Hidden; }

        /// <summary>OnGUI 맨 앞에서 호출 — true면 그리지 말 것.</summary>
        public static bool Skip => Hidden;
    }

    /// <summary>
    /// 개발 패널(F1~F4)이 하나라도 열려 있는가.
    ///
    /// 패널을 조작하려면 커서를 풀어야 하는데, 그 클릭이 그대로 게임 입력으로도 들어가
    /// 슬라이더를 만질 때마다 공격이 발동하는 문제가 있었다.
    /// Main은 콘솔과 동일하게 이 값을 보고 플레이어 입력을 막는다.
    /// </summary>
    public static class DevPanels
    {
        public static bool AnyOpen =>
            PoseTunePanel.AnyOpen || PoseSeqPanel.AnyOpen || SlashFxPanel.AnyOpen ||
            MotionTunePanel.AnyOpen || TuningPanelOpen;

        /// <summary>F1(전투 튜닝) 등 AnyOpen 플래그가 없는 패널이 직접 설정.</summary>
        public static bool TuningPanelOpen;
    }
}
