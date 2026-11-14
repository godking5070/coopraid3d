using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyHealerKit
    {
        private HealerLayerState _layers = new HealerLayerState();
        private string LayerKey => Job.Id + ".nine.verse.layers";
        private float LayerRemaining(Transform target)
        {
            int source = CombatMetricsManager.SourceId(transform);
            return target.GetComponent<PlayerController>()?.ShieldFor(source, LayerKey) ?? target.GetComponent<PartyBotAI>()?.ShieldFor(source, LayerKey) ?? 0;
        }
        private void RemoveLayer(Transform target)
        {
            if (target == null) return;
            int source = CombatMetricsManager.SourceId(transform);
            target.GetComponent<PlayerController>()?.RemoveTimedShield(source, LayerKey);
            target.GetComponent<PartyBotAI>()?.RemoveTimedShield(source, LayerKey);
        }
        private void RegisterLayer(Transform target, float amount)
        {
            RemoveLayer(target);
            CombatStatusEffects.Shield(target, transform, LayerKey, amount, _layers.Snapshot.remaining, true);
            _layers.SetActiveBudget(CharacterKit.Id(target), LayerRemaining(target));
        }
        private bool LayerIdentity()
        {
            if (_layers.Active)
            {
                foreach (var entry in _layers.Snapshot.recipients)
                {
                    var patient = CharacterKit.Resolve(entry.target);
                    if (!CombatRange.LivingPartyMember(patient)) { _layers.Remove(entry.target); continue; }
                    _layers.SetActiveBudget(entry.target, LayerRemaining(patient));
                }
                foreach (var entry in _layers.Settle())
                {
                    var patient = CharacterKit.Resolve(entry.target); RemoveLayer(patient);
                    if (CombatRange.LivingPartyMember(patient)) Heal(patient, entry.Budget * .5f, true);
                }
                return true;
            }
            var recipients = Party().ToArray(); if (recipients.Length == 0 || !_runtime.Spend(Job.Resource, 2)) return false;
            int layers = Core(2, 0) ? 2 : Core(2, 1) ? 4 : 3;
            float amount = Power * .15f / layers;
            _layers.Begin(recipients.Select(CharacterKit.Id).ToArray(), layers, amount, 12);
            foreach (var patient in recipients) RegisterLayer(patient, amount);
            return true;
        }
        public static void OnLayerAbsorbed(Transform recipient, int source, string key, float amount)
        {
            if (RaidCombatAuthority.Replica || amount <= 0) return;
            var owner = PartyLife.OrderedMembers().FirstOrDefault(t => CombatMetricsManager.SourceId(t) == source);
            var kit = owner?.GetComponent<NineKeyHealerKit>();
            if (kit?.Active != true || key != kit.LayerKey || !kit._layers.Absorbed(CharacterKit.Id(recipient), amount, out float next)) return;
            kit.RemoveLayer(recipient);
            if (next > 0) kit.RegisterLayer(recipient, next);
        }
        private void TickLayers(float delta)
        {
            foreach (var entry in _layers.Snapshot.recipients)
            {
                var patient=CharacterKit.Resolve(entry.target);
                if (!CombatRange.LivingPartyMember(patient)) _layers.Remove(entry.target);
                else if(entry.activeRemaining<=0&&_layers.Snapshot.remaining>delta) RegisterLayer(patient,entry.perLayer);
            }
            foreach (int target in _layers.Tick(delta)) RemoveLayer(CharacterKit.Resolve(target));
        }
    }
}
