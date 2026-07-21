using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 실제 전투(sim)에 포즈 시퀀스를 연동한다.
    /// 마우스를 직접 읽지 않고 <b>sim의 공격 상태 전이</b>를 보므로,
    /// 쿨다운·선입력·히트판정 등 게임 규칙을 그대로 따른다(= 일반 플레이에서도 동작).
    ///
    ///   공격 시작(PhNone → PhWindup)  → slash1 / slash2 번갈아
    ///   런지 시작(LgNone → 그 외)      → thrust1  (윈드업 0틱이라 LgTravel로 직행)
    ///   글로리                         → glory1 (없으면 무시)
    ///
    /// 애니메이션끼리는 항상 순간이동(각 공격이 새 시퀀스를 처음부터 재생).
    /// </summary>
    public class PoseCombatDriver : MonoBehaviour
    {
        public static PoseCombatDriver Instance { get; private set; }

        [Tooltip("실제 전투에 포즈를 연동한다")]
        public bool active = true;

        [Tooltip("좌클릭(평타)에서 번갈아 쓸 접두어")]
        public string[] attackPrefixes = { "slash1_", "slash2_" };
        [Tooltip("우클릭(찌르기) 접두어")]
        public string   lungePrefix = "thrust1_";
        [Tooltip("글로리킬 접두어 (해당 포즈가 없으면 무시)")]
        public string   gloryPrefix = "glory1_";

        [Tooltip("포즈당 시간(초) — 프로파일이 저장돼 있으면 그쪽이 우선")]
        public float segTime = 0.15f;

        [Tooltip("명중 순간 피격 버스트를 띄운다 (평타1·2·찌르기 공통)")]
        public bool hitBurst = true;
        [Tooltip("피격 지점을 못 찾을 때 카메라 앞 이 거리에 띄운다(m)")]
        public float fallbackHitDist = 2.4f;

        int  nextAttack;
        byte prevAttack, prevLunge, prevGlory;
        bool prevHitDone;             // 평타 명중 전이 감지
        bool primed;                  // 첫 프레임의 상태를 기준으로 잡아 시작하자마자 터지지 않게

        void Awake() { Instance = this; }

        /// <summary>콤보를 처음(slash1)으로.</summary>
        public void ResetCombo() => nextAttack = 0;

        void Update()
        {
            if (!active) return;
            var main = Main.Instance;
            if (main == null) return;

            ref readonly PlayerSim p = ref main.World.player;
            byte atk = p.combat.attackPhase;
            byte lg  = p.combat.lungePhase;
            byte gl  = p.combat.gloryPhase;

            if (!primed) { prevAttack = atk; prevLunge = lg; prevGlory = gl; primed = true; return; }

            // F3에서 시퀀스를 튜닝하는 중에 클릭으로 slash가 끼어들면 작업이 날아간다.
            // 패널이 열려 있으면 상태만 따라가고 재생은 하지 않는다.
            if (DevPanels.AnyOpen) { prevAttack = atk; prevLunge = lg; prevGlory = gl; return; }

            // 글로리 > 런지 > 평타 순으로 우선
            if (gl != prevGlory)
            {
                if (prevGlory == CombatConfig.GlNone && gl != CombatConfig.GlNone) PlayPrefix(gloryPrefix, true);
                prevGlory = gl;
            }
            if (lg != prevLunge)
            {
                // ★ 찌르기는 윈드업이 0틱이라 LgNone → LgTravel 로 바로 간다.
                //   LgWindup 을 기다리면 영영 발동하지 않는다(그래서 우클릭이 먹통이었다).
                if (prevLunge == CombatConfig.LgNone && lg != CombatConfig.LgNone)
                { PlayPrefix(lungePrefix, false); Fx("찌르기"); }
                prevLunge = lg;
            }
            if (atk != prevAttack)
            {
                if (prevAttack == CombatConfig.PhNone && atk == CombatConfig.PhWindup) PlayAttack();
                prevAttack = atk;
            }

            // ── 명중 순간 피격 버스트 (평타1·2·찌르기 공통) ──
            bool hit = p.combat.attackHitDone;
            if (hitBurst && hit && !prevHitDone) Burst(main, p.combat.lungeTargetId);
            prevHitDone = hit;
        }

        /// <summary>피격 지점에서 방사형 참격. 대상 적이 있으면 그 몸통 높이에, 없으면 카메라 앞에.</summary>
        void Burst(Main main, int targetId)
        {
            var fx = SlashFxDriver.Instance;
            if (fx == null) return;

            Vector3 pos;
            if (!TryEnemyPos(main, targetId, out pos))
            {
                var cam = main.Cam != null ? main.Cam : Camera.main;
                if (cam == null) return;
                pos = cam.transform.position + cam.transform.forward * fallbackHitDist;
            }
            fx.BurstAt(pos);
        }

        /// <summary>id로 적을 찾아 가슴 높이 좌표를 돌려준다.</summary>
        static bool TryEnemyPos(Main main, int id, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (id < 0) return false;
            ref readonly SimWorld w = ref main.World;
            for (int i = 0; i < w.enemyCount; i++)
                if (w.enemies[i].id == id && w.enemies[i].alive)
                {
                    pos = w.enemies[i].pos + Vector3.up * (w.enemies[i].height * 0.55f);
                    return true;
                }
            return false;
        }

        void PlayAttack()
        {
            if (attackPrefixes == null || attackPrefixes.Length == 0) return;
            int idx = nextAttack % attackPrefixes.Length;
            string prefix = attackPrefixes[idx];
            nextAttack = (nextAttack + 1) % attackPrefixes.Length;
            PlayPrefix(prefix, false);
            Fx(idx == 0 ? "평타1" : "평타2");      // 베기 이펙트 동반
        }

        /// <summary>베기 이펙트 발동(있을 때만).</summary>
        static void Fx(string slot)
        {
            var fx = SlashFxDriver.Instance;
            if (fx != null) fx.Fire(slot);
        }

        /// <summary>해당 접두어 시퀀스를 처음부터 재생. quiet=true면 포즈가 없어도 조용히 넘어간다.</summary>
        void PlayPrefix(string prefix, bool quiet)
        {
            var pp = PosePlayer.Instance;
            if (pp == null || string.IsNullOrEmpty(prefix)) return;
            int n = pp.Play(prefix, segTime, false);
            if (n < 2 && !quiet)
                Debug.LogWarning($"[PoseCombatDriver] '{prefix}*' 포즈가 2개 미만입니다.");
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PoseCombatDriverBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PoseCombatDriver>() == null)
                new GameObject("[PoseCombatDriver]").AddComponent<PoseCombatDriver>();
        }
    }
}
