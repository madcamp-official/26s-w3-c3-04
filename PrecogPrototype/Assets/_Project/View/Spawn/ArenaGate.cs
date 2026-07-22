using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 아레나 게이트 — 문을 슬라이드로 여닫는다.
    /// <see cref="ArenaRoom"/>의 onLock에 <see cref="Close"/>, onUnlock에 <see cref="Open"/>을 연결해 쓴다.
    ///
    /// 배치 규칙: 문은 <b>닫힌 위치</b>로 배치하고, openOffset(로컬)으로 열렸을 때의 오프셋을 지정한다.
    /// 시작 상태가 Open이면 시작 시 연출 없이 열림 위치로 스냅한다(입구 게이트용).
    /// 콜라이더는 문에 붙어 같이 움직이므로 닫히면 물리적으로 막는다
    /// (sim 이동·레이캐스트가 Unity 콜라이더를 조회하므로 sim에서도 막힌다).
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaGate : MonoBehaviour
    {
        public enum StartState { Open, Closed }

        [Tooltip("움직일 문. 비우면 자기 자신을 움직인다.")]
        public Transform door;
        [Tooltip("닫힌 위치 기준, 열렸을 때의 로컬 오프셋(예: 바닥 아래로 (0,-4,0), 옆으로 (4,0,0)).")]
        public Vector3 openOffset = new Vector3(0f, -4f, 0f);
        [Tooltip("개폐에 걸리는 시간(초).")]
        public float moveSeconds = 0.8f;
        [Tooltip("시작 상태. 보통 입구(뒤) 게이트=Open, 출구(앞) 게이트=Closed.")]
        public StartState startState = StartState.Open;

        Vector3 closedPos;   // 배치된(닫힌) 로컬 위치
        float t;             // 0=닫힘, 1=열림 (진행도)
        float target;

        public bool IsOpen => target > 0.5f;

        void Awake()
        {
            if (door == null) door = transform;
            closedPos = door.localPosition;
            t = target = startState == StartState.Open ? 1f : 0f;
            Apply();
        }

        /// <summary>문 열기 — ArenaRoom.onUnlock에 연결.</summary>
        public void Open()  => target = 1f;
        /// <summary>문 닫기 — ArenaRoom.onLock에 연결.</summary>
        public void Close() => target = 0f;
        public void Toggle() => target = IsOpen ? 0f : 1f;

        void Update()
        {
            if (Mathf.Approximately(t, target)) return;
            float speed = moveSeconds > 1e-3f ? 1f / moveSeconds : 1000f;
            t = Mathf.MoveTowards(t, target, speed * Time.deltaTime);
            Apply();
        }

        void Apply()
        {
            float s = Mathf.SmoothStep(0f, 1f, t);   // 가감속
            // ★ 임시방편: 아레나 높이차로 문이 출입구에 걸리는 문제 회피. 열림 상태를 무조건 확실히 비운다.
            //   원래는 openOffset 그대로 사용(door.localPosition = closedPos + openOffset * s). 나중에 원복 필요.
            Vector3 off = openOffset.sqrMagnitude > 1e-6f ? openOffset.normalized * 1000f : new Vector3(0f, 1000f, 0f);
            door.localPosition = closedPos + off * s;
        }

        void OnDrawGizmosSelected()
        {
            var d = door != null ? door : transform;
            // 에디터(배치 상태 = 닫힘)에서 열림 위치를 미리 보여준다.
            Vector3 worldOff = d.parent != null ? d.parent.TransformVector(openOffset) : openOffset;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(d.position, d.position + worldOff);
            Gizmos.DrawWireSphere(d.position + worldOff, 0.3f);
        }
    }
}
