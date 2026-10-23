using System;

namespace RaidRogue
{
    [Serializable]
    public sealed class PotionSettings
    {
        // StageManager owns one settings instance shared by the player and all bots.
        public float cooldown = 30f;
        public float healFraction = 0.5f;
        public const float BotUseHpFraction = 0.4f;
        public const float RevivalProtectionSeconds = 3f;
    }

    // Lifetime belongs to the party member, not a life/role. Death and revival never recreate it.
    public sealed class PotionState
    {
        public double CooldownRemaining { get; private set; }
        public double CooldownDuration { get; private set; }
        public double RevivalProtectionRemaining { get; private set; }
        private bool _applying;

        public bool CanUse(PotionSettings settings, float hp, float maxHp, bool alive, bool blocked) =>
            !_applying && alive && !blocked && CooldownRemaining <= 0 && Finite(hp) && Finite(maxHp) &&
            hp > 0 && maxHp > hp && settings != null && Finite(settings.cooldown) && settings.cooldown >= 0 &&
            Finite(settings.healFraction) && settings.healFraction > 0 && settings.healFraction <= 1;

        public static bool NeedsAutomaticPotion(float hp, float maxHp, bool alive) =>
            alive && Finite(hp) && Finite(maxHp) && hp > 0 && maxHp > 0 && hp <= maxHp * PotionSettings.BotUseHpFraction;

        // The callback is the actor's existing HP application path. Only its actual delta commits the item use.
        public float TryUse(PotionSettings settings, float hp, float maxHp, bool alive, bool blocked, Func<float, float> applyHeal)
        {
            if (!CanUse(settings, hp, maxHp, alive, blocked) || applyHeal == null) return 0;
            float cooldown = settings.cooldown;
            _applying = true;
            float actual;
            try { actual = applyHeal(maxHp * settings.healFraction); }
            finally { _applying = false; }
            if (!Finite(actual) || actual <= 0) return 0;
            CooldownDuration = CooldownRemaining = cooldown;
            EndRevivalProtection();
            return actual;
        }

        public void Tick(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            CooldownRemaining = Math.Max(0, CooldownRemaining - seconds);
            RevivalProtectionRemaining = Math.Max(0, RevivalProtectionRemaining - seconds);
        }

        public void GrantRevivalProtection(float seconds)
        {
            if (Finite(seconds) && seconds > 0) RevivalProtectionRemaining = Math.Max(RevivalProtectionRemaining, seconds);
        }
        public void EndRevivalProtection() => RevivalProtectionRemaining = 0;
        public void ResetForBossRetry()
        {
            CooldownRemaining = CooldownDuration = 0;
            EndRevivalProtection();
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
