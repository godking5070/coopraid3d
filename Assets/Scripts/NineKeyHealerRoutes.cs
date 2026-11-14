using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyHealerKit
    {
        private HealerGardenState _garden;
        private HealerHarvestState _harvest;
        private HealerLinkState _link;
        private HealerSurgeryState _surgery;
        private HealerSongState _song;
        private HealerConductorState _conductor;
        private float _flowerMoveReady;
        private readonly System.Collections.Generic.HashSet<int> _flowerInside = new System.Collections.Generic.HashSet<int>();
        private float _gardenTick;
        private int _songTarget;
        private bool Core(int route, int variant) => _actor.RuntimeState.CoreTrait == NineKeyHealerData.Core(Job, route, variant);
        private void ResetRoutes()
        {
            _layers = new HealerLayerState(); _songTarget = 0;
            _garden = new HealerGardenState(); _harvest = new HealerHarvestState(); _link = new HealerLinkState(); _surgery = new HealerSurgeryState(); _song = new HealerSongState(_resources); _conductor = new HealerConductorState(); _gardenTick = _flowerMoveReady = 0; _flowerInside.Clear();
        }
        private bool Identity(CharacterSkillDefinition skill, Transform enemy)
        {
            var patient = _actor.ActionSupportTarget;
            if (Job.index == 4 && Route == 2) return LayerIdentity();
            if (Job.index == 3 && Route == 2) return PlaceFlower();
            if (Job.index == 4 && Route == 1)
            {
                int maximum = Core(1, 0) ? 1 : 3;
                if (!Valid(patient, 25) || !_conductor.CanRequest(CharacterKit.Id(patient), CharacterKit.Id(transform), maximum) || !_runtime.Spend(Job.Resource, 1)) return false;
                _conductor.Request(CharacterKit.Id(patient), CharacterKit.Id(transform), maximum, 8);
                Shield(patient, "request", Power * (Core(1, 0) ? .09f : Core(1, 1) ? .025f : .04f)); return true;
            }
            if (Job.index == 5 && Route == 2) return Prescribe();
            if (Job.index == 3 && Route == 1)
            {
                int maximum = Core(1, 0) ? 2 : 3; float interval = Core(1, 1) ? 3 : 4;
                if (!Valid(patient, 25) || !_garden.CanPlant(CharacterKit.Id(patient), maximum) || !_runtime.Spend(Job.Resource, 1)) return false;
                return _garden.Plant(CharacterKit.Id(patient), maximum, interval);
            }
            if (Job.index == 3 && Route == 3)
            {
                if (_harvest.Harvest(out int growth)) { Harvest(growth, 1); Attack(skill, enemy, 120 + 60 * growth); return true; }
                if (!_runtime.Spend(Job.Resource, 1)) return false;
                return _harvest.Begin(Core(3, 1) ? 16 : 12, Core(3, 0) ? 2 : Core(3, 1) ? 4 : 3);
            }
            if (Job.index == 4 && Route == 3)
            {
                if (_song.Active) { _song.Cancel(); return true; }
                if (!_song.Begin(Core(3, 0) ? 1.5f : 2, Core(3, 0) ? 2 : 3)) return false;
                _songTarget = CharacterKit.Id(enemy); return true;
            }
            if (Job.index == 5 && Route == 1)
            {
                if (_actor.ActionRecall && !_link.Active) return false;
                if (!Valid(patient, 25)) return false;
                int target = CharacterKit.Id(patient);
                if (_link.Active) return _link.Move(target, _actor.ActionRecall, .25f);
                if (!_runtime.Spend(Job.Resource, 40)) return false;
                _link.Begin(target, Core(1, 1), 12); Heal(patient, Power * .04f, true); return true;
            }
            if (Job.index == 5 && Route == 3)
            {
                if (!Valid(patient, 25) || !_surgery.Ready) return false;
                if (!_surgery.Active && !_runtime.Spend(Job.Resource, 40)) return false;
                if (!_surgery.Treat(CharacterKit.Id(patient), 12, .25f, out var treatment)) return false;
                if (treatment.Step == 1) { Heal(patient, Power * .12f, true); Effect("stabilize", "reduction", patient, .12f, 4); }
                if (treatment.Step == 2) { Heal(patient, Power * .1f, true); Shield(patient, "suture", Power * .04f); }
                if (treatment.Step == 3)
                {
                    float scale = Core(3, 0) && treatment.SamePatient ? 1.3f : 1;
                    Effect("prognosis.hot", "heal", patient, Power * .015f * scale, 5);
                    Effect("prognosis", "directHealMark", patient, Power * .06f * scale, 8);
                }
                if (Core(3, 1) && treatment.ChangedPatient)
                    foreach (var ally in Party().Where(t => CombatRange.InRange(patient, t, 6))) Heal(ally, Power * .025f);
                return true;
            }
            return false;
        }
        private void Harvest(int growth, float fraction)
        {
            float scale = Core(3, 0) ? .8f : Core(3, 1) && growth == 4 ? 1.2f : 1;
            foreach (var ally in Party()) Heal(ally, Power * (.04f + .025f * growth) * fraction * scale, fraction == 1);
        }
        private void Bloom(int target)
        {
            Heal(CharacterKit.Resolve(target), Power * (Core(1, 0) ? .12f : Core(1, 1) ? .065f : .09f));
        }
        private void DirectRoute(int chain, Transform primary)
        {
            if (Job.index == 3 && Route == 1 && chain == 2 && _garden.Accelerate(CharacterKit.Id(primary))) Bloom(CharacterKit.Id(primary));
            if (Job.index == 3 && Route == 3) _harvest.DirectInput();
            if (Job.index == 5 && Route == 1 && _link.Active)
            {
                var link = _link.Snapshot;
                float share = link.branched ? .5f : Core(1, 0) ? 1.2f : 1;
                foreach (int id in new[] { link.first, link.second })
                {
                    var patient = CharacterKit.Resolve(id);
                    if (Valid(patient, 25)) Heal(patient, Power * .035f * share);
                }
            }
        }
        public void Interrupt()
        {
            if (!Active || RaidCombatAuthority.Replica) return;
            Bind(); _song.Cancel();
        }
        private void TickRoutes(float delta)
        {
            if (Job.index == 3 && Route == 1)
            {
                // Heals use living planted recipients only. Early blooming removes
                // the plant before this pass, so it cannot keep its remaining HoT.
                // Advance chronologically: a stalled frame must not pay every
                // elapsed second at the old stage, nor heal after natural bloom.
                float pending = delta;
                while (pending > 0 && _garden.Snapshot.Length > 0)
                {
                    float slice = Mathf.Min(pending, 1 - _gardenTick);
                    _gardenTick += slice; pending -= slice;
                    if (_gardenTick >= 1)
                    {
                        _gardenTick = 0;
                        foreach (var seed in _garden.Snapshot)
                        {
                            var patient = CharacterKit.Resolve(seed.target);
                            if (!CombatRange.LivingPartyMember(patient)) { _garden.Remove(seed.target); continue; }
                            Heal(patient, Power * (.008f + seed.stage * .004f) * (Core(1, 0) ? 1.3f : Core(1, 1) ? .8f : 1));
                        }
                    }
                    foreach (int target in _garden.Tick(slice, Core(1, 1) ? 3 : 4)) Bloom(target);
                }
                if (_garden.Snapshot.Length == 0) _gardenTick = 0;
            }
            if (Job.index == 3 && Route == 3 && _harvest.Tick(delta, out int growth)) Harvest(growth, .5f);
            if (Job.index == 4 && Route == 3)
            {
                foreach (int verse in _song.Tick(delta))
                {
                    foreach (var ally in Party()) Heal(ally, Power * .07f * (Core(3, 0) ? .8f : Core(3, 1) ? verse == 3 ? 1.6f : .7f : 1));
                    Attack(_actor.Selected(CharacterSlot.Identity), CharacterKit.Resolve(_songTarget), 120, 0, CombatDamageKind.Extra);
                }
            }
            _link.Tick(delta); _surgery.Tick(delta);
            _conductor.Tick(delta, 10);
            TickFlowers();
            TickLayers(delta);
        }
    }
}
