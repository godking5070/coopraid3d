using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    // Pure preparation states. Callers validate direct inputs/targets and pay the
    // resource before activation. Returned events are consumed once by the host.
    [Serializable] public sealed class HealerSeed
    {
        public int target, stage;
        public float nextGrowth;
        public HealerSeed Copy() => (HealerSeed)MemberwiseClone();
    }
    public sealed class HealerGardenState
    {
        private readonly List<HealerSeed> _seeds = new List<HealerSeed>();
        public HealerSeed[] Snapshot => _seeds.Select(s => s.Copy()).ToArray();
        public bool CanPlant(int target, int maximum) => target != 0 && maximum > 0 && _seeds.Count < maximum && !_seeds.Any(s => s.target == target);
        public bool Plant(int target, int maximum, float interval)
        {
            if (!CanPlant(target, maximum) || !HealerTime.Positive(interval)) return false;
            _seeds.Add(new HealerSeed { target = target, nextGrowth = interval }); return true;
        }
        // Only the primary recipient of an accepted 2 input can accelerate.
        public bool Accelerate(int target)
        {
            var seed = _seeds.FirstOrDefault(s => s.target == target);
            if (seed == null) return false;
            if (++seed.stage < 3) return false;
            _seeds.Remove(seed); return true;
        }
        public int[] Tick(float delta, float interval)
        {
            if (!HealerTime.Positive(delta) || !HealerTime.Positive(interval)) return Array.Empty<int>();
            var blooms = new List<int>();
            foreach (var seed in _seeds.ToArray())
            {
                seed.nextGrowth -= delta;
                while (seed.nextGrowth <= 0 && seed.stage < 3) { seed.stage++; seed.nextGrowth += interval; }
                if (seed.stage == 3) { _seeds.Remove(seed); blooms.Add(seed.target); }
            }
            return blooms.ToArray();
        }
        public void Remove(int target) => _seeds.RemoveAll(s => s.target == target);
        public bool Restore(HealerSeed[] seeds, int maximum)
        {
            if (seeds == null || maximum < 1 || seeds.Length > maximum || seeds.Any(s => s == null || s.target == 0 || s.stage < 0 || s.stage >= 3 || !HealerTime.Positive(s.nextGrowth)) || seeds.Select(s => s.target).Distinct().Count() != seeds.Length) return false;
            _seeds.Clear(); _seeds.AddRange(seeds.Select(s => s.Copy())); return true;
        }
    }
    [Serializable] public sealed class HealerHarvestSnapshot
    {
        public float remaining;
        public int growth, maximum;
        public HealerHarvestSnapshot Copy() => (HealerHarvestSnapshot)MemberwiseClone();
    }
    public sealed class HealerHarvestState
    {
        private HealerHarvestSnapshot _state = new HealerHarvestSnapshot();
        public HealerHarvestSnapshot Snapshot => _state.Copy();
        public bool Active => _state.remaining > 0;
        public bool Begin(float duration, int maximum)
        {
            if (Active || !HealerTime.Positive(duration) || maximum < 1 || maximum > 4) return false;
            _state = new HealerHarvestSnapshot { remaining = duration, maximum = maximum }; return true;
        }
        public void DirectInput() { if (Active) _state.growth = Math.Min(_state.maximum, _state.growth + 1); }
        public bool Harvest(out int growth)
        {
            growth = 0; if (!Active) return false;
            growth = _state.growth; _state = new HealerHarvestSnapshot(); return true;
        }
        // True means exactly one automatic partial-heal event; no attack event.
        public bool Tick(float delta, out int growth)
        {
            growth = 0; if (!Active || !HealerTime.Positive(delta)) return false;
            if (delta < _state.remaining) { _state.remaining -= delta; return false; }
            return Harvest(out growth);
        }
        public bool Restore(HealerHarvestSnapshot value)
        {
            if (value == null || !HealerTime.Nonnegative(value.remaining) || value.growth < 0 || value.maximum < 0 || value.maximum > 4 || value.growth > value.maximum || (value.remaining > 0 ? value.maximum == 0 : value.maximum != 0 || value.growth != 0)) return false;
            _state = value.Copy(); return true;
        }
    }
    [Serializable] public sealed class HealerRequest
    {
        public int target;
        public bool replied;
        public float remaining;
        public HealerRequest Copy() => (HealerRequest)MemberwiseClone();
    }
    public sealed class HealerConductorState
    {
        private readonly List<HealerRequest> _requests = new List<HealerRequest>();
        public HealerRequest[] Snapshot => _requests.Select(r => r.Copy()).ToArray();
        public int Replies => _requests.Count(r => r.replied);
        public bool CanRequest(int target, int owner, int maximum) => target != 0 && target != owner && maximum > 0 && _requests.Count < maximum && !_requests.Any(r => !r.replied && r.target == target);
        public bool Request(int target, int owner, int maximum, float duration)
        {
            if (!CanRequest(target, owner, maximum) || !HealerTime.Positive(duration)) return false;
            _requests.Add(new HealerRequest { target = target, remaining = duration }); return true;
        }
        // Caller proves a subsequent direct attack/heal/personal-defense action.
        public bool Respond(int target, float replyLifetime)
        {
            if (!HealerTime.Positive(replyLifetime)) return false;
            var request = _requests.FirstOrDefault(r => r.target == target && !r.replied);
            if (request == null) return false;
            request.replied = true; request.remaining = replyLifetime; return true;
        }
        public void Tick(float delta, float replyLifetime)
        {
            if (!HealerTime.Positive(delta) || !HealerTime.Positive(replyLifetime)) return;
            foreach (var request in _requests.ToArray())
            {
                request.remaining -= delta;
                if (request.remaining > 0) continue;
                if (!request.replied) { request.replied = true; request.remaining += replyLifetime; }
                if (request.remaining <= 0) _requests.Remove(request);
            }
        }
        public int ConsumeReplies()
        {
            int count = Replies; _requests.RemoveAll(r => r.replied); return count;
        }
        public bool Restore(HealerRequest[] requests, int owner, int maximum)
        {
            if (requests == null || maximum < 1 || requests.Length > maximum || requests.Any(r => r == null || r.target == 0 || r.target == owner || !HealerTime.Positive(r.remaining))) return false;
            var pending = requests.Where(r => !r.replied).ToArray();
            if (pending.Select(r => r.target).Distinct().Count() != pending.Length) return false;
            _requests.Clear(); _requests.AddRange(requests.Select(r => r.Copy())); return true;
        }
    }
    internal static class HealerTime
    {
        public static bool Nonnegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
        public static bool Positive(float value) => Nonnegative(value) && value > 0;
    }
}
