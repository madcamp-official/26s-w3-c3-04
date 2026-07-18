namespace Game.Sim
{
    public static class CombatConfig
    {
        public const int PlayerMaxHp = 3;
        public const int AttackWindupTicks = 6;
        public const int AttackActiveTicks = 2;
        public const int AttackRecoveryTicks = 12;
        public const float AttackRange = 1.8f;
        public const float AttackHalfAngleDeg = 55f;
        public const float AttackHeightTolerance = 1.0f;
        public const int LungeWindupTicks = 3;
        public const int LungeTravelTicks = 5;
        public const int LungeRecoveryTicks = 10;
        public const int LungeCooldownTicks = 120;
        public const float LungeMinRange = 1.2f;
        public const float LungeMaxRange = 8f;
        public const float LungeHalfAngleDeg = 30f;
        public const float LungeStopDistance = 0.9f;
        public const float LungeHeightTolerance = 0.8f;
        public const int DamagePerHit = 1;
        public const int StunTicks = 30;
        public const int EnemyAttackWindupTicks = 24;
        public const int EnemyAttackActiveTicks = 3;
        public const int EnemyAttackRecoveryTicks = 30;
        public const int EnemyAttackCooldownTicks = 45;
        public const float EnemyAttackRange = 1.45f;
        public const float EnemyAttackHalfAngleDeg = 55f;
        public const float EnemyAttackHeightTolerance = 1.0f;
        public const float EnemyStopDistance = 1.15f;
    }
}
