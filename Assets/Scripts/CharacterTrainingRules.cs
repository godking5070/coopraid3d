using System;
using System.Linq;

namespace RaidRogue
{
    // A practice loadout is a fresh actor state, never a fabricated stage-clear reward.
    public static class CharacterTrainingRules
    {
        public static bool TryBuild(CharacterDefinition job, string route, string active, string weave, string ultimate,
            int evolution, out CharacterRuntimeState state)
        {
            state = null;
            if (job == null || !job.playable || job.legacyRole != 0 || evolution < 0 || evolution > 2 ||
                !job.routes.Any(r => r.id == route) || CharacterDefinitionValidation.Check(new[] { job }).Length != 0) return false;
            var choices = new[] { active, weave, ultimate };
            var slots = new[] { CharacterSlot.Active, CharacterSlot.OffGcd2, CharacterSlot.Ultimate };
            for (int i = 0; i < slots.Length; i++)
                if (!job.unlocks.Any(r => r.slot == slots[i] && r.choices.Contains(choices[i])) || job.Skill(choices[i])?.implemented != true) return false;
            var next = new CharacterRuntimeState(job);
            if (!next.ChooseRoute(route, true)) return false;
            foreach (string choice in choices) if (!next.PreviewSkillChoice(choice)) return false;
            if (evolution != 0) next.SetBuildEffect(evolution == 1 ? "evolution.overcome" : "evolution.maximize", 1, 0);
            state = next;
            return true;
        }
    }

    public enum TrainingAttack { Normal, Heavy, Area, Counter, Dispel, Knockback, Projectile, System }

    // One telegraphed stimulus per update; a hitch never catches up by firing a burst of attacks.
    public sealed class TrainingAttackClock
    {
        public bool Running { get; private set; }
        public bool Telegraphing { get; private set; }
        public double Due { get; private set; }
        public int Revision { get; private set; }
        private double _interval;
        private bool _repeat;
        public const double WarningSeconds = 1.5;
        public bool Start(double now, double interval, bool repeat)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || double.IsNaN(interval) || double.IsInfinity(interval) || interval < 2 || interval > 30) return false;
            Stop(); Running = Telegraphing = true; Due = now + WarningSeconds; _interval = interval; _repeat = repeat; return true;
        }
        public bool Advance(double now, out bool warn)
        {
            warn = false;
            if (!Running || now < Due) return false;
            if (!Telegraphing) { Telegraphing = true; Due = now + WarningSeconds; warn = true; return false; }
            if (_repeat) { Telegraphing = false; Due = now + _interval - WarningSeconds; }
            else Running = Telegraphing = false;
            return true;
        }
        public void Stop() { Revision++; Running = Telegraphing = false; Due = 0; }
    }
}
