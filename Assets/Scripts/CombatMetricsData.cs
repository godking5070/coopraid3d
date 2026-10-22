using System;
using System.Collections.Generic;

namespace RaidRogue
{
    // Pure data: no Unity clock, scene, UI, or combat simulation dependencies.
    public sealed class CombatMemberRecord
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public int Role { get; internal set; }
        public double BossDamage { get; internal set; }
        public double AddDamage { get; internal set; }
        public double Healing { get; internal set; }
        public double ShieldAbsorbed { get; internal set; }
        public int Deaths { get; internal set; }
        public double Damage => BossDamage + AddDamage;
        public double Dps(double seconds) => seconds > 0 ? Damage / seconds : 0;
        public double Hps(double seconds) => seconds > 0 ? Healing / seconds : 0;
        internal CombatMemberRecord Copy() => (CombatMemberRecord)MemberwiseClone();
    }

    public sealed class CombatEncounterResult
    {
        public int Stage { get; }
        public double Seconds { get; }
        public IReadOnlyList<CombatMemberRecord> Members { get; }

        internal CombatEncounterResult(int stage, double seconds, IEnumerable<CombatMemberRecord> members)
        {
            Stage = stage;
            Seconds = seconds;
            var copies = new List<CombatMemberRecord>();
            foreach (var member in members) copies.Add(member.Copy());
            Members = copies.AsReadOnly();
        }
    }

    public sealed class CombatMetricsLedger
    {
        private readonly Dictionary<int, CombatMemberRecord> _members = new Dictionary<int, CombatMemberRecord>();
        private readonly List<CombatEncounterResult> _stages = new List<CombatEncounterResult>();
        private readonly HashSet<int> _downMembers = new HashSet<int>();
        public bool Active { get; private set; }
        public int Stage { get; private set; }
        public int Generation { get; private set; }
        public double Seconds { get; private set; }
        public IReadOnlyList<CombatEncounterResult> Stages => _stages.AsReadOnly();

        public void Register(int id, string name, int role)
        {
            if (id == 0) return;
            if (!_members.TryGetValue(id, out var member))
                _members.Add(id, member = new CombatMemberRecord { Id = id });
            member.Name = name;
            member.Role = role;
        }

        public void ResetRun()
        {
            Active = false;
            Stage = 0;
            Seconds = 0;
            Generation++;
            _members.Clear();
            _downMembers.Clear();
            _stages.Clear();
        }

        public void BeginStage(int stage)
        {
            Generation++;
            Stage = stage;
            Seconds = 0;
            Active = true;
            _downMembers.Clear();
            var roster = new List<CombatMemberRecord>(_members.Values);
            _members.Clear();
            foreach (var member in roster) Register(member.Id, member.Name, member.Role);
        }

        // Wall-clock duration while gameplay is advancing; paused/reward time is excluded.
        public void Advance(double unscaledSeconds, bool gameplayAdvancing)
        {
            if (Active && gameplayAdvancing && ValidPositive(unscaledSeconds)) Seconds += unscaledSeconds;
        }

        public CombatEncounterResult Snapshot() => new CombatEncounterResult(Stage, Seconds, _members.Values);

        public void EndStage()
        {
            if (!Active) return;
            _stages.Add(Snapshot());
            Active = false;
        }

        public void RecordDamage(int source, double hpBefore, double hpAfter, bool isAdd)
        {
            if (!Active || !_members.TryGetValue(source, out var member)) return;
            double applied = HealthLost(hpBefore, hpAfter);
            if (isAdd) member.AddDamage += applied;
            else member.BossDamage += applied;
        }

        public void RecordHealing(int source, double hpBefore, double hpAfter)
        {
            if (!Active || !_members.TryGetValue(source, out var member)) return;
            double applied = hpAfter - hpBefore;
            if (hpBefore >= 0 && ValidPositive(applied)) member.Healing += applied;
        }

        public void RecordAbsorption(int source, double amount)
        {
            if (Active && ValidPositive(amount) && _members.TryGetValue(source, out var member))
                member.ShieldAbsorbed += amount;
        }

        public void SetDead(int memberId, bool dead)
        {
            if (!dead) { _downMembers.Remove(memberId); return; }
            if (Active && _members.TryGetValue(memberId, out var member) && _downMembers.Add(memberId))
                member.Deaths++;
        }

        public CombatEncounterResult RunTotal()
        {
            var totals = new Dictionary<int, CombatMemberRecord>();
            double seconds = 0;
            foreach (var stage in _stages)
            {
                seconds += stage.Seconds;
                foreach (var member in stage.Members)
                {
                    if (!totals.TryGetValue(member.Id, out var total))
                        totals.Add(member.Id, total = new CombatMemberRecord { Id = member.Id });
                    total.Name = member.Name;
                    total.Role = member.Role;
                    total.BossDamage += member.BossDamage;
                    total.AddDamage += member.AddDamage;
                    total.Healing += member.Healing;
                    total.ShieldAbsorbed += member.ShieldAbsorbed;
                    total.Deaths += member.Deaths;
                }
            }
            return new CombatEncounterResult(0, seconds, totals.Values);
        }

        private static double HealthLost(double before, double after)
        {
            if (!ValidPositive(before) || double.IsNaN(after) || double.IsInfinity(after)) return 0;
            return Math.Max(0, before - Math.Max(0, after));
        }

        private static bool ValidPositive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    // The game owns the shield value; this ledger only tracks ownership of its actual increments.
    // Multiple providers are consumed FIFO. Unattributed legacy shield is never assigned to a healer.
    public sealed class CombatShieldLedger
    {
        private sealed class Slice { public int Source; public double Remaining; }
        private readonly List<Slice> _slices = new List<Slice>();
        public double Remaining { get; private set; }

        public void Clear() { _slices.Clear(); Remaining = 0; }

        public void Reconcile(double actualShield)
        {
            actualShield = Math.Max(0, actualShield);
            if (actualShield <= 0) { Clear(); return; }
            if (actualShield < Remaining) Absorb(Remaining - actualShield, null);
            else if (actualShield > Remaining) Grant(0, actualShield - Remaining);
        }

        public void Grant(int source, double actualAdded)
        {
            if (actualAdded <= 0 || double.IsNaN(actualAdded) || double.IsInfinity(actualAdded)) return;
            if (_slices.Count > 0 && _slices[_slices.Count - 1].Source == source)
                _slices[_slices.Count - 1].Remaining += actualAdded;
            else _slices.Add(new Slice { Source = source, Remaining = actualAdded });
            Remaining += actualAdded;
        }

        public void Absorb(double actualAbsorbed, Action<int, double> record)
        {
            double pending = Math.Min(Math.Max(0, actualAbsorbed), Remaining);
            while (pending > 0 && _slices.Count > 0)
            {
                var slice = _slices[0];
                double used = Math.Min(pending, slice.Remaining);
                if (slice.Source != 0) record?.Invoke(slice.Source, used);
                pending -= used;
                Remaining -= used;
                slice.Remaining -= used;
                if (slice.Remaining <= 0) _slices.RemoveAt(0);
            }
            if (_slices.Count == 0) Remaining = 0;
        }
    }
}
