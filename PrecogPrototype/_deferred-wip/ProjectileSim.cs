using UnityEngine;

namespace Game.Sim
{
    [System.Flags]
    public enum ProjectileHitMask : byte
    {
        None = 0,
        Player = 1 << 0,
        Enemies = 1 << 1,
        World = 1 << 2
    }

    public struct ProjectileConfig
    {
        public int typeId;
        public float speed;
        public float radius;
        public int lifetimeTicks;
        public int damage;
        public int hitStunTicks;
        public float gravity;
        public ProjectileHitMask hitMask;
    }

    /// <summary>GameObject/Rigidbody와 분리된 결정론적 투사체 상태.</summary>
    public struct ProjectileSim
    {
        public int id;
        public int typeId;
        public int ownerId;
        public bool alive;
        public Vector3 position;
        public Vector3 previousPosition;
        public Vector3 velocity;
        public float radius;
        public int remainingTicks;
        public int damage;
        public int hitStunTicks;
        public ProjectileHitMask hitMask;
        public int targetId;
        public int pierceRemaining;
        public int bounceRemaining;
        public int spawnTick;
    }

    /// <summary>
    /// 고정 속도 투사체의 공용 시간 진행 뼈대. 충돌·유도·반사는 게임 규칙 승인 후
    /// 별도 resolver에서 처리하며 여기서는 상태 진행과 수명만 책임진다.
    /// </summary>
    public static class ProjectileSimulation
    {
        public static void Step(ref SimWorld world, float dt)
        {
            for (int i = 0; i < world.projectileCount; i++)
            {
                ref ProjectileSim projectile = ref world.projectiles[i];
                if (!projectile.alive) continue;
                projectile.previousPosition = projectile.position;
                projectile.position += projectile.velocity * dt;
                if (--projectile.remainingTicks <= 0)
                    projectile.alive = false;
            }
        }
    }
}
