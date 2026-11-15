using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    [Serializable] public sealed class HealerLayerRecipient
    {
        public int target, layers;
        public float perLayer, activeRemaining;
        public HealerLayerRecipient Copy() => (HealerLayerRecipient)MemberwiseClone();
        public float Budget => activeRemaining + Math.Max(0, layers - 1) * perLayer;
    }
    [Serializable] public sealed class HealerLayerSnapshot
    {
        public float remaining;
        public HealerLayerRecipient[] recipients = Array.Empty<HealerLayerRecipient>();
        public HealerLayerSnapshot Copy() => new HealerLayerSnapshot { remaining = remaining, recipients = recipients.Select(r => r.Copy()).ToArray() };
    }
    // Each instance belongs to one bard. Only its current layer is registered in
    // the shared shield ledger; waiting layers must never enter currentShield.
    public sealed class HealerLayerState
    {
        private HealerLayerSnapshot _state = new HealerLayerSnapshot();
        public HealerLayerSnapshot Snapshot => _state.Copy();
        public bool Active => _state.remaining > 0 && _state.recipients.Length > 0;
        public bool Begin(int[] targets, int layers, float perLayer, float duration)
        {
            if (Active || targets == null || targets.Length == 0 || targets.Length > 8 || targets.Any(t => t == 0) || targets.Distinct().Count() != targets.Length || layers < 1 || layers > 4 || !HealerTime.Positive(perLayer) || !HealerTime.Positive(duration)) return false;
            _state = new HealerLayerSnapshot { remaining = duration, recipients = targets.Select(t => new HealerLayerRecipient { target = t, layers = layers, perLayer = perLayer, activeRemaining = perLayer }).ToArray() }; return true;
        }
        // Run once AFTER one damage verdict, using the amount the shared ledger
        // actually attributed to this source AND effect key. Do not call for an
        // unrelated shield or a zero-damage hit. The adapter discards old residue
        // and registers the returned next layer only after this hit is finished.
        public bool Absorbed(int target, float actual, out float nextLayer)
        {
            nextLayer = 0;
            if (!Active || !HealerTime.Positive(actual)) return false;
            var entry = _state.recipients.FirstOrDefault(r => r.target == target);
            if (entry == null) return false;
            entry.layers--;
            if (entry.layers == 0) Remove(target);
            else { entry.activeRemaining = entry.perLayer; nextLayer = entry.activeRemaining; }
            return true;
        }
        // The common shield cap can admit less than the requested active layer.
        // Store that actual amount so settlement never invents missing protection.
        public bool SetActiveBudget(int target, float actual)
        {
            if (!HealerTime.Nonnegative(actual)) return false;
            var entry = _state.recipients.FirstOrDefault(r => r.target == target);
            if (entry == null) return false;
            entry.activeRemaining = actual; return true;
        }
        public void Remove(int target)
        {
            _state.recipients = _state.recipients.Where(r => r.target != target).ToArray();
            if (_state.recipients.Length == 0) _state = new HealerLayerSnapshot();
        }
        public HealerLayerRecipient[] Settle()
        {
            var result = Snapshot.recipients; _state = new HealerLayerSnapshot(); return result;
        }
        // Return initial recipients for removal of only this caster's ledger key.
        // No automatic healing is returned on natural expiry.
        public int[] Tick(float delta)
        {
            if (!Active || !HealerTime.Positive(delta)) return Array.Empty<int>();
            _state.remaining = Math.Max(0, _state.remaining - delta);
            if (_state.remaining > 0) return Array.Empty<int>();
            var targets = _state.recipients.Select(r => r.target).ToArray(); _state = new HealerLayerSnapshot(); return targets;
        }
        public bool Restore(HealerLayerSnapshot value)
        {
            if (value == null || !HealerTime.Nonnegative(value.remaining) || value.recipients == null || value.recipients.Length > 8 || (value.remaining > 0) != (value.recipients.Length > 0)) return false;
            if (value.recipients.Any(r => r == null || r.target == 0 || r.layers < 1 || r.layers > 4 || !HealerTime.Positive(r.perLayer) || !HealerTime.Nonnegative(r.activeRemaining)) || value.recipients.Select(r => r.target).Distinct().Count() != value.recipients.Length) return false;
            _state = value.Copy(); return true;
        }
    }
}
