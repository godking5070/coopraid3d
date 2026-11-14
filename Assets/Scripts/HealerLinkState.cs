using System;

namespace RaidRogue
{
    [Serializable] public sealed class HealerLinkSnapshot
    {
        public int first, second;
        public float remaining, switchDelay;
        public bool branched;
        public HealerLinkSnapshot Copy() => (HealerLinkSnapshot)MemberwiseClone();
    }
    // Target validity/range is checked at emission by the host. Losing a patient
    // pauses only that patient's share; it never gives the other share a bonus.
    public sealed class HealerLinkState
    {
        private HealerLinkSnapshot _state = new HealerLinkSnapshot();
        public HealerLinkSnapshot Snapshot => _state.Copy();
        public bool Active => _state.remaining > 0;
        public bool Begin(int target, bool branched, float duration)
        {
            if (Active || target == 0 || !HealerTime.Positive(duration)) return false;
            _state = new HealerLinkSnapshot { first = target, branched = branched, remaining = duration }; return true;
        }
        public bool CanMove(int target, bool second)
        {
            if (!Active || target == 0 || _state.switchDelay > 0 || (second && !_state.branched)) return false;
            return second ? target != _state.first && target != _state.second : target != _state.second && target != _state.first;
        }
        public bool Move(int target, bool second, float delay)
        {
            if (!CanMove(target, second) || !HealerTime.Nonnegative(delay)) return false;
            if (second) _state.second = target; else _state.first = target;
            _state.switchDelay = delay; return true;
        }
        public void Tick(float delta)
        {
            if (!Active || !HealerTime.Positive(delta)) return;
            _state.remaining = Math.Max(0, _state.remaining - delta);
            _state.switchDelay = Math.Max(0, _state.switchDelay - delta);
            if (!Active) _state = new HealerLinkSnapshot();
        }
        public bool Restore(HealerLinkSnapshot value)
        {
            if (value == null || !HealerTime.Nonnegative(value.remaining) || !HealerTime.Nonnegative(value.switchDelay)) return false;
            if (value.remaining > 0)
            {
                if (value.first == 0 || value.first == value.second || (!value.branched && value.second != 0)) return false;
            }
            else if (value.first != 0 || value.second != 0 || value.switchDelay != 0 || value.branched) return false;
            _state = value.Copy(); return true;
        }
    }
}
