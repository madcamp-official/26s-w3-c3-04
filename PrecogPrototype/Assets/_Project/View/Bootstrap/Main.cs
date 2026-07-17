using UnityEngine;
using Game.Sim;
using Game.Bridge;

namespace Game.View
{
    /// <summary>
    /// 심장. 빈 GameObject에 이 컴포넌트 하나만 붙이면 동작한다.
    /// Start: 맵·NavMesh·서비스·월드 구성. Update: 입력·뷰·1인칭 카메라. FixedUpdate: SimStep.
    /// 전투 없음(뼈대) — 이동·지형충돌·적 길찾기·겹침밀치기만.
    /// </summary>
    public class Main : MonoBehaviour
    {
        [SerializeField] float eyeHeight = 1.6f;

        SimWorld world, prevWorld;
        readonly InputReader input = new InputReader();
        readonly EntityViews views = new EntityViews();
        SimServices services;
        Camera cam;
        float fixedAccum;

        void Start()
        {
            Time.fixedDeltaTime = SimConfig.TickDelta;

            MapBuilder.Build();
            services = new SimServices(new PhysicsCollision(Physics.DefaultRaycastLayers),
                                       new NavMeshPathfinder());

            world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 6f));
            world.AddEnemy(new Vector3(-20f, 0f,  0f));   // 장애물 뒤 → 돌아와야
            world.AddEnemy(new Vector3(-18f, 0f,  6f));
            world.AddEnemy(new Vector3(-18f, 0f, -6f));
            world.AddEnemy(new Vector3( 16f, 3f,  4f));   // 발판 위 → 램프로 내려와야
            world.AddEnemy(new Vector3( 18f, 3f, -4f));
            world.AddEnemy(new Vector3(  0f, 0f, -18f));
            prevWorld = Snapshot.Clone(in world);

            views.Init();
            SetupCamera();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Debug.Log($"[Main] 뼈대 시작. 적 {world.enemyCount}. " +
                      "WASD 이동 · 마우스 시점 · Space 더블점프 · Shift 질풍참 · Esc 커서해제");
        }

        void Update()
        {
            input.PollFrame();

            fixedAccum += Time.deltaTime;
            float alpha = Mathf.Clamp01(fixedAccum / Time.fixedDeltaTime);
            views.Sync(in world, in prevWorld, alpha);

            if (cam != null && views.PlayerAnchor != null)
            {
                cam.transform.position = views.PlayerAnchor.position + Vector3.up * eyeHeight;
                cam.transform.rotation = Quaternion.Euler(input.Pitch, input.Yaw, 0f);
            }

            if (input.EscapePressed()) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        void FixedUpdate()
        {
            prevWorld = Snapshot.Clone(in world);
            InputCmd cmd = input.Consume();
            SimStep.Run(ref world, in cmd, in services);
            fixedAccum = 0f;
        }

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
        }

        void OnDrawGizmos()
        {
            if (Application.isPlaying) SimGizmos.Draw(in world);
        }
    }

    /// <summary>Play 누르면 자동 실행. 씬에 Main이 없으면 하나 만든다(수동 추가해도 중복 안 됨).</summary>
    public static class AutoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<Main>() == null)
                new GameObject("[Main]").AddComponent<Main>();
        }
    }
}
