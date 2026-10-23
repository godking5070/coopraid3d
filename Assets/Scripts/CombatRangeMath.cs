using System;

namespace RaidRogue
{
    public enum MeleeSkill { TankPrimary, BladeFlank, BladeRear, BladeGale }

    [Serializable]
    public sealed class MeleeSkillRange
    {
        public MeleeSkill skill;
        // 0 applies to all combo stages; a matching 1/2/3 override takes precedence.
        public int comboStage;
        public float range = 2.5f;
    }

    [Serializable]
    public sealed class MeleeRangeSettings
    {
        public float tankDefault = 2.0f;
        public float bladeDefault = 2.5f;
        public MeleeSkillRange[] overrides = { new MeleeSkillRange { skill = MeleeSkill.BladeGale, range = 3.0f } };

        public float Get(MeleeSkill skill, int comboStage = 1)
        {
            float value = skill == MeleeSkill.TankPrimary ? tankDefault : bladeDefault;
            if (overrides != null)
            {
                foreach (var entry in overrides)
                    if (entry != null && entry.skill == skill && entry.comboStage == 0 && entry.range >= 0) value = entry.range;
                foreach (var entry in overrides)
                    if (entry != null && entry.skill == skill && entry.comboStage == comboStage && comboStage > 0 && entry.range >= 0) value = entry.range;
            }
            return Math.Max(0, value);
        }
    }

    // World units on the XZ plane. No model scale, collider bounds, or height enters these equations.
    public static class CombatRangeMath
    {
        // Contact with the boss's visible hitbox, allowing 5 cm of motion jitter.
        public const float MeleeContactTolerance = 0.05f;

        // Original wide positional rings. Legacy serialized body radii must not shrink them.
        public const float Stage1BossRingRadius = 3.2f;
        public const float Stage2BossRingRadius = 3.648f;
        public const float Stage3BossRingRadius = 4.32f;

        public static float ResolveBossCombatRadius(int stage, float configuredRadius)
        {
            float minimum = stage == 1 ? Stage1BossRingRadius : stage == 2 ? Stage2BossRingRadius : Stage3BossRingRadius;
            return Math.Max(minimum, configuredRadius);
        }

        public static double CenterDistance(double ax, double az, double bx, double bz)
        {
            double dx = ax - bx, dz = az - bz;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        public static double BoundaryDistance(double distance, double attackerRadius, double targetRadius) =>
            Math.Max(0, distance - Math.Max(0, attackerRadius) - Math.Max(0, targetRadius));

        public static bool CircleTouches(double distance,double targetRadius,double circleRadius)=>distance>=0&&circleRadius>=0&&distance<=circleRadius+Math.Max(0,targetRadius)+.0001;

        public static double RingRadius(double attackerRadius, double range) => Math.Max(0, attackerRadius) + Math.Max(0, range);

        public static double AttackCenterDistance(double attackerRadius, double targetRadius, double range) =>
            Math.Max(0, attackerRadius) + Math.Max(0, targetRadius) + Math.Max(0, range) * 0.65;

        public static bool InRange(double distance, double attackerRadius, double targetRadius, double range) =>
            range >= 0 && BoundaryDistance(distance, attackerRadius, targetRadius) <= range + 0.0001;

        // Positive = toward target, negative = back out of overlapping combat bodies.
        // Restart never exceeds attack range, so a stopped boss cannot be stranded outside attack range.
        public static double ChaseStep(double distance, double radii, double attackRange, double hysteresis,
            double maxStep, ref bool chasing)
        {
            maxStep = Math.Max(0, maxStep);
            radii = Math.Max(0, radii);
            attackRange = Math.Max(0, attackRange);
            double gap = distance - radii;
            if (gap < 0) { chasing = false; return -Math.Min(-gap, maxStep); }
            double stopGap = Math.Max(0, attackRange - Math.Max(0.01, hysteresis));
            if (gap > attackRange) chasing = true;
            else if (gap <= stopGap + 0.0001) chasing = false;
            return chasing ? Math.Min(maxStep, Math.Max(0, gap - stopGap)) : 0;
        }
    }
}
