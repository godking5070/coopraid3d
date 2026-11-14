using System;

namespace RaidRogue
{
    [Serializable] public sealed class HealerSongSnapshot
    {
        public float remaining, interval;
        public int completed, maximum;
        public HealerSongSnapshot Copy() => (HealerSongSnapshot)MemberwiseClone();
    }
    public sealed class HealerSongState
    {
        private readonly NineKeyHealerResources _resources;
        private HealerSongSnapshot _state = new HealerSongSnapshot();
        public HealerSongState(NineKeyHealerResources resources) { _resources = resources; }
        public HealerSongSnapshot Snapshot => _state.Copy();
        public bool Active => _state.remaining > 0;
        public bool Begin(float interval, int maximum)
        {
            if (Active || !HealerTime.Positive(interval) || maximum < 1 || maximum > 3 || !_resources.BeginVerse()) return false;
            _state = new HealerSongSnapshot { interval = interval, remaining = interval, maximum = maximum }; return true;
        }
        public void Cancel()
        {
            if (!Active) return;
            _resources.CancelVerse(); _state = new HealerSongSnapshot();
        }
        // Returns completed verse numbers, allowing the last-verse trait to use
        // its own coefficient. State and costs commit before effects are applied.
        public int[] Tick(float delta)
        {
            if (!Active || !HealerTime.Positive(delta)) return Array.Empty<int>();
            var results = new System.Collections.Generic.List<int>();
            while (Active && delta >= _state.remaining)
            {
                delta -= _state.remaining;
                if (!_resources.CompleteVerse()) { _state = new HealerSongSnapshot(); break; }
                results.Add(++_state.completed);
                if (_state.completed >= _state.maximum || !_resources.BeginVerse()) { _state = new HealerSongSnapshot(); break; }
                _state.remaining = _state.interval;
            }
            if (Active) _state.remaining -= delta;
            return results.ToArray();
        }
        // Restore the matching resource snapshot first, then this state.
        public bool Restore(HealerSongSnapshot value)
        {
            if (value == null || !HealerTime.Nonnegative(value.remaining) || !HealerTime.Nonnegative(value.interval) || value.completed < 0 || value.maximum < 0 || value.maximum > 3) return false;
            if (value.remaining > 0)
            {
                if (!_resources.Snapshot.verseReserved || value.interval <= 0 || value.remaining > value.interval || value.completed >= value.maximum) return false;
            }
            else if (_resources.Snapshot.verseReserved || value.interval != 0 || value.completed != 0 || value.maximum != 0) return false;
            _state = value.Copy(); return true;
        }
    }
    [Serializable] public sealed class HealerSurgerySnapshot
    {
        public float remaining, inputDelay;
        public int nextStep, firstTarget, previousTarget;
        public bool samePatient;
        public HealerSurgerySnapshot Copy() => (HealerSurgerySnapshot)MemberwiseClone();
    }
    public readonly struct HealerTreatment
    {
        public readonly int Step, Target;
        public readonly bool SamePatient, ChangedPatient;
        public HealerTreatment(int step, int target, bool same, bool changed) { Step = step; Target = target; SamePatient = same; ChangedPatient = changed; }
    }
    public sealed class HealerSurgeryState
    {
        private HealerSurgerySnapshot _state = new HealerSurgerySnapshot();
        public HealerSurgerySnapshot Snapshot => _state.Copy();
        public bool Active => _state.remaining > 0;
        public bool Ready => !Active || _state.inputDelay <= 0;
        // Initial cost is paid by caller only when inactive. Invalid patients
        // never advance the sequence or silently redirect a treatment to self.
        public bool Treat(int validTarget, float duration, float delay, out HealerTreatment treatment)
        {
            treatment = default;
            if (validTarget == 0 || !Ready || !HealerTime.Positive(duration) || !HealerTime.Nonnegative(delay)) return false;
            if (!Active)
            {
                _state = new HealerSurgerySnapshot { remaining = duration, inputDelay = delay, nextStep = 2, firstTarget = validTarget, previousTarget = validTarget, samePatient = true };
                treatment = new HealerTreatment(1, validTarget, true, false); return true;
            }
            bool changed = validTarget != _state.previousTarget;
            _state.samePatient &= validTarget == _state.firstTarget;
            treatment = new HealerTreatment(_state.nextStep++, validTarget, _state.samePatient, changed);
            _state.previousTarget = validTarget; _state.inputDelay = delay;
            if (_state.nextStep > 3) _state = new HealerSurgerySnapshot();
            return true;
        }
        public void Tick(float delta)
        {
            if (!Active || !HealerTime.Positive(delta)) return;
            _state.remaining = Math.Max(0, _state.remaining - delta);
            _state.inputDelay = Math.Max(0, _state.inputDelay - delta);
            if (!Active) _state = new HealerSurgerySnapshot();
        }
        public bool Restore(HealerSurgerySnapshot value)
        {
            if (value == null || !HealerTime.Nonnegative(value.remaining) || !HealerTime.Nonnegative(value.inputDelay)) return false;
            if (value.remaining > 0)
            {
                if (value.nextStep < 2 || value.nextStep > 3 || value.firstTarget == 0 || value.previousTarget == 0 || (value.samePatient && value.firstTarget != value.previousTarget)) return false;
            }
            else if (value.inputDelay != 0 || value.nextStep != 0 || value.firstTarget != 0 || value.previousTarget != 0 || value.samePatient) return false;
            _state = value.Copy(); return true;
        }
    }
}
