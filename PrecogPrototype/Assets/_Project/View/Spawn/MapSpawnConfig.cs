using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>몹 종류 별칭 → 3축 조합. 콘솔·씬 세팅이 공유하는 단일 정의.</summary>
    public enum MobKind : byte { Grunt = 0, Pinky = 1, Soldier = 2, Caco = 3, Large = 4 }

    /// <summary>스폰 지점 하나 = 위치(씬의 빈 오브젝트) + 그 지점에서 나올 종류.</summary>
    [System.Serializable]
    public struct SpawnEntry
    {
        public Transform point;
        public MobKind    kind;
    }

    /// <summary>
    /// 맵별 오토스폰 세팅. 각 테스트 씬에 하나 배치한다. 스폰 지점(빈 오브젝트)마다 종류를 지정.
    /// Main이 Start에서 이걸 찾아 스폰지점·종류·주기·상한·기본 on/off를 가져온다(맵 열면 세팅이 딸려옴).
    /// </summary>
    public class MapSpawnConfig : MonoBehaviour
    {
        [Tooltip("맵 로드 시 자동스폰을 켠 채로 시작할지")]
        public bool autoSpawnOnStart = true;
        [Tooltip("스폰 주기(틱, 60틱=1초)")]
        public int intervalTicks = 45;
        [Tooltip("동시 최대 적 수")]
        public int cap = 40;
        [Tooltip("스폰 지점 + 그 지점에서 나올 종류")]
        public SpawnEntry[] entries;

        /// <summary>종류 별칭 → (전투, 기동, 크기).</summary>
        public static (CombatType, MobilityType, SizeClass) Axes(MobKind k)
        {
            switch (k)
            {
                case MobKind.Pinky:   return (CombatType.Melee,  MobilityType.Charge, SizeClass.Normal);
                case MobKind.Soldier: return (CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);
                case MobKind.Caco:    return (CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
                case MobKind.Large:   return (CombatType.Melee,  MobilityType.Ground, SizeClass.Large);
                default:              return (CombatType.Melee,  MobilityType.Ground, SizeClass.Normal);  // Grunt
            }
        }

        /// <summary>이름 문자열 → MobKind(콘솔 입력용). 실패 시 false.</summary>
        public static bool TryParse(string s, out MobKind kind)
        {
            switch (s.ToLowerInvariant())
            {
                case "grunt":   kind = MobKind.Grunt;   return true;
                case "pinky":   kind = MobKind.Pinky;   return true;
                case "soldier": kind = MobKind.Soldier; return true;
                case "caco":    kind = MobKind.Caco;    return true;
                case "large":   kind = MobKind.Large;   return true;
                default:        kind = MobKind.Grunt;   return false;
            }
        }

        // ── 씬 뷰 시각화: 지점 위치·종류를 색 구체로 표시 ──
        void OnDrawGizmos()
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e.point == null) continue;
                Gizmos.color = KindColor(e.kind);
                Gizmos.DrawWireSphere(e.point.position, 0.6f);
                Gizmos.DrawLine(e.point.position, e.point.position + Vector3.up * 2f);
            }
        }

        static Color KindColor(MobKind k)
        {
            switch (k)
            {
                case MobKind.Pinky:   return new Color(1f, 0.4f, 0.2f);
                case MobKind.Soldier: return new Color(0.3f, 0.7f, 1f);
                case MobKind.Caco:    return new Color(0.8f, 0.3f, 1f);
                case MobKind.Large:   return new Color(1f, 0.85f, 0.2f);
                default:              return Color.white;   // Grunt
            }
        }
    }
}
