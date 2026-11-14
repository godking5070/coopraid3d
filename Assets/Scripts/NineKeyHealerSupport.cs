using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyHealerKit
    {
        private CharacterTimedEffect[] Flowers => _effects.Data.effects.Where(e => e.kind == "flower" && e.end > Now).OrderBy(e => e.end).ToArray();
        private bool PlaceFlower()
        {
            Vector3? point = _actor.ActionGroundPoint;
            if (!point.HasValue || !CanPlaceFlower(point.Value)) return false;
            var flowers = Flowers;
            if (_actor.ActionRecall)
            {
                if (!Core(2, 1) || flowers.Length == 0 || Now < _flowerMoveReady) return false;
                var flower = flowers[0]; flower.x = point.Value.x; flower.y = point.Value.y; flower.z = point.Value.z;
                _flowerMoveReady = Now + 2; return true;
            }
            if (!_runtime.Spend(Job.Resource, 1)) return false;
            int maximum = Core(2, 0) ? 1 : 2;
            string id;
            if (flowers.Length >= maximum) { id = flowers[0].id; _effects.Remove(id); }
            else id = flowers.Any(e => e.id == "flower.0") ? "flower.1" : "flower.0";
            _effects.Put(new CharacterTimedEffect { id = id, kind = "flower", skill = "flower", x = point.Value.x, y = point.Value.y, z = point.Value.z, end = Now + 20, next = Now + 20, interval = 1, radius = Core(2, 0) ? 10 : 7 });
            return true;
        }
        private bool NearFlower(Transform ally) => Flowers.Any(f => Vector3.Distance(ally.position, new Vector3(f.x, f.y, f.z)) <= f.radius);
        private void TickFlowers()
        {
            if (Job.index != 3 || Route != 2) return;
            var inside = new System.Collections.Generic.HashSet<int>();
            foreach (var ally in PartyLife.OrderedMembers().Where(CombatRange.LivingPartyMember))
            {
                if (!NearFlower(ally)) continue;
                int id = CharacterKit.Id(ally); inside.Add(id);
                if (_flowerInside.Contains(id)) continue;
                string gate = "flower.pass@" + CharacterKit.Id(ally);
                if (_effects.Find(gate, Now) != null) continue;
                Effect("flower.pass", "gate", ally, 0, 8);
                Effect("flower.speed", "speed", ally, .15f, 3);
            }
            _flowerInside.Clear(); _flowerInside.UnionWith(inside);
        }
        private void FinishRoute(int chain)
        {
            if (chain != 2) return;
            if (Job.index == 3 && Route == 2)
                foreach (var ally in PartyLife.OrderedMembers().Where(CombatRange.LivingPartyMember).Where(NearFlower))
                    Heal(ally, Power * (Core(2, 0) ? .05f : .035f)); // Unique party traversal, overlapping flowers cannot double heal.
            if (Job.index == 4 && Route == 1)
            {
                int replies = _conductor.ConsumeReplies();
                if (replies > 0)
                    foreach (var ally in Party()) Shield(ally, "chorus", Power * .025f * replies * (Core(1, 0) ? .6f : Core(1, 1) ? 1.3f : 1));
            }
        }
        private bool Prescribe()
        {
            if (!_resources.Prescribe(out int red)) return false;
            bool pure = red == 0 || red == 3;
            float scale = Core(2, 0) ? pure ? 1.25f : .8f : Core(2, 1) ? pure ? .8f : 1.25f : 1;
            foreach (var ally in Party())
            {
                Heal(ally, Power * .09f, true);
                if (red == 0) Effect("recipe.hot", "heal", ally, Power * .02f * scale, 6);
                if (red == 1) Shield(ally, "recipe", Power * .07f * scale);
                if (red == 2) Effect("recipe.guard", "reduction", ally, .12f * scale, 6);
                if (red == 3 && ally != transform)
                {
                    // One right per teammate, a fixed party-size budget, refresh
                    // replaces the right rather than adding another charge.
                    int size = PartyLife.OrderedMembers().Count;
                    float budget = size > 4 ? 160 : 120;
                    Effect("recipe.coop", "cooperation", ally, budget * scale / Mathf.Max(1, size - 1), 6);
                }
            }
            return true;
        }
        public static void OnDirectAttack(Transform source, Transform target, string origin, CombatDamageKind kind)
        {
            if (RaidCombatAuthority.Replica || StageManager.PlayerSkillsSuspended || kind != CombatDamageKind.Direct || !CombatRange.LivingPartyMember(source) || !CombatRange.LivingEnemy(target) || source.GetComponent<CharacterActor>()?.OwnsDirectSkillHit(origin) != true) return;
            foreach (var owner in PartyLife.OrderedMembers())
            {
                var kit = owner.GetComponent<NineKeyHealerKit>();
                if (kit?.Active != true || !CombatRange.LivingPartyMember(owner)) continue;
                kit.Bind(); kit._conductor.Respond(CharacterKit.Id(source), 10);
                var right = kit._effects.Find("recipe.coop@" + CharacterKit.Id(source), kit.Now);
                if (right == null) continue;
                kit._effects.Remove(right.id); // Consume before secondary damage; it cannot respond again.
                var boss = target.GetComponent<BossController>(); var add = target.GetComponent<BossAddMinion>();
                if (boss != null) boss.ReceiveRoleSkillAttack(kit.Job.name, "촉진약 협공", 0, Mathf.RoundToInt(right.value), Color.red, false, source: owner, kind: CombatDamageKind.Extra);
                else if (add != null) add.ReceivePlayerAttack(kit.Job.name, "촉진약 협공", Mathf.RoundToInt(right.value), Color.red, false, source: owner, kind: CombatDamageKind.Extra);
            }
        }
        public static void OnDirectHealing(Transform source, Transform target)
        {
            if (RaidCombatAuthority.Replica || StageManager.PlayerSkillsSuspended || source?.GetComponent<CharacterActor>()?.OwnsHealerInput != true || !CombatRange.LivingPartyMember(target)) return;
            foreach (var owner in PartyLife.OrderedMembers())
            {
                var kit = owner.GetComponent<NineKeyHealerKit>();
                if (kit?.Active != true || !CombatRange.LivingPartyMember(owner)) continue;
                kit.Bind(); kit._conductor.Respond(CharacterKit.Id(source), 10);
                var mark = kit._effects.Find("prognosis@" + CharacterKit.Id(target), kit.Now);
                if (mark == null) continue;
                kit._effects.Remove(mark.id); kit.Heal(target, mark.value); // No direct flag: no recursive prognosis/response.
            }
        }
        public static void OnPersonalDefense(Transform source)
        {
            if (RaidCombatAuthority.Replica || StageManager.PlayerSkillsSuspended || !CombatRange.LivingPartyMember(source)) return;
            foreach (var owner in PartyLife.OrderedMembers())
            {
                var kit = owner.GetComponent<NineKeyHealerKit>();
                if (kit?.Active != true || !CombatRange.LivingPartyMember(owner)) continue;
                kit.Bind(); kit._conductor.Respond(CharacterKit.Id(source), 10);
            }
        }
        public static float SupportBonus(Transform recipient, string kind)
        {
            float value = 0;
            foreach (var owner in PartyLife.OrderedMembers())
            {
                var kit = owner.GetComponent<NineKeyHealerKit>();
                if (kit?.Active != true || !CombatRange.LivingPartyMember(owner)) continue;
                foreach (var effect in kit._effects.Data.effects)
                    if (effect.kind == kind && effect.target == CharacterKit.Id(recipient) && effect.end > kit.Now) value = Mathf.Max(value, effect.value);
            }
            return value;
        }
        public static float Incoming(Transform recipient, float damage, CombatHitContext hit)
        {
            if (hit.Defense == CombatDefenseType.Bypass || hit.Kind == CombatDamageKind.System || hit.Kind == CombatDamageKind.Transfer || hit.Kind == CombatDamageKind.Deferred) return damage;
            var effects = new System.Collections.Generic.Dictionary<string, float>();
            foreach (var owner in PartyLife.OrderedMembers())
            {
                var kit = owner.GetComponent<NineKeyHealerKit>();
                if (kit?.Active != true || !CombatRange.LivingPartyMember(owner)) continue;
                foreach (var effect in kit._effects.Data.effects)
                    if (effect.kind == "reduction" && effect.target == CharacterKit.Id(recipient) && effect.end > kit.Now)
                    { effects.TryGetValue(effect.skill, out float old); effects[effect.skill] = Mathf.Max(old, effect.value); }
            }
            foreach (float reduction in effects.Values) damage *= 1 - Mathf.Clamp01(reduction);
            return damage;
        }
    }
}
