using System;

namespace RaidRogue
{
    public enum EncounterOutcome { None, Cleared, Wiped }
    public static class PartyBattleRules
    {
        public static EncounterOutcome Resolve(bool bossDead, int living, int partySize) => bossDead
            ? EncounterOutcome.Cleared : partySize >= 4 && living == 0 ? EncounterOutcome.Wiped : EncounterOutcome.None;
    }

    public sealed class PartyLifeState
    {
        public bool Dead { get; private set; }
        public int DeathGeneration { get; private set; }
        public bool Die() { if (Dead) return false; Dead = true; DeathGeneration++; return true; }
        public bool Revive() { if (!Dead) return false; Dead = false; return true; }
        public void Reset() => Dead = false;
    }

    [Serializable]
    public sealed class ResurrectionSettings
    {
        public float castTime = 3f;
        public float range = 20f;
        public float cooldown = 45f;
        public float healthFraction = 0.4f;
    }

    public sealed class ResurrectionState
    {
        public bool Casting { get; private set; }
        public double Remaining { get; private set; }
        public double Duration { get; private set; }
        public double Cooldown { get; private set; }
        public double CooldownTotal { get; private set; }
        private double _pendingCooldown;
        public bool Begin(ResurrectionSettings settings, bool valid)
        {
            if (!valid || Casting || Cooldown > 0 || settings == null || settings.castTime <= 0 || settings.range <= 0 ||
                settings.cooldown < 0 || settings.healthFraction <= 0 || settings.healthFraction > 1 ||
                float.IsNaN(settings.castTime + settings.range + settings.cooldown + settings.healthFraction) ||
                float.IsInfinity(settings.castTime + settings.range + settings.cooldown + settings.healthFraction)) return false;
            Remaining = Duration = settings.castTime;
            _pendingCooldown = settings.cooldown;
            Casting = true;
            return true;
        }
        public bool Advance(double seconds, bool valid)
        {
            if (!Casting) return false;
            if (!valid) { Cancel(); return false; }
            Remaining = Math.Max(0, Remaining - Math.Max(0, seconds));
            return Remaining <= 0;
        }
        public void Complete(bool revived)
        {
            if (!Casting) return;
            if (revived) Cooldown = CooldownTotal = _pendingCooldown;
            Cancel();
        }
        public void Tick(double seconds) => Cooldown = Math.Max(0, Cooldown - Math.Max(0, seconds));
        public void Cancel() { Casting = false; Remaining = 0; }
        public void Reset() { Cancel(); Cooldown = CooldownTotal = 0; }
    }
}
