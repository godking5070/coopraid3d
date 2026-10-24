using System;

namespace RaidRogue
{
    public static class CharacterRosterRules
    {
        public const float ComboThirdMultiplier = 2.2f;
        // Baseline-normalized armor: existing enemies had no armor rating. Exposed initial rating 100,
        // curve 300 preserves unchanged damage at zero debuffs and gives penetration meaningful units.
        public const float EnemyArmor = 100, ArmorCurve = 300;
        public static float ArmorFactor(float reduction, float penetration) => (ArmorCurve + EnemyArmor) / (ArmorCurve + EnemyArmor * (1 - Clamp(reduction)) * (1 - Clamp(penetration)));
        public static float RangerNearMultiplier(float boundaryDistance, bool overcome, bool maximize) => !overcome && boundaryDistance <= (maximize ? 6 : 4) ? .75f : 1;
        public static float RangerReceivedMultiplier(bool overcome, bool maximize) => maximize ? .8f : overcome ? 1 : .9f;
        public static float HeavyArmorSpeed(bool shield, bool overcome, bool maximize) => maximize ? .85f : overcome && shield ? 1.15f : .9f;
        public static float OverheatSeconds(bool overcome, bool maximize) => maximize ? 5 : overcome ? 1 : 3;
        public static bool IsCannon(string key) => key == "g2" || key == "a" || key == "z1" || key == "f2" || key == "v1";
        // Same 45/135 degree boundaries as BossController and TargetRingRenderer. Enum: front/flank/rear.
        public static int PositionalZoneFromDot(float dot)
        {
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, dot))) * 180 / Math.PI;
            return angle >= 135 ? 2 : angle >= 45 ? 1 : 0;
        }
        public static float Clamp(float value) => Math.Max(0, Math.Min(1, value));
        public static bool WallCrosses(float attackerSide, float targetSide, float crossingOffset, float halfWidth) => attackerSide > 0 && targetSide <= 0 && Math.Abs(crossingOffset) <= halfWidth;
    }
}
