using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    // Host effect adapter for the live nine-key healer definitions.
    [DefaultExecutionOrder(450)]
    public sealed partial class NineKeyHealerKit : MonoBehaviour
    {
        private CharacterActor _actor;
        private CharacterRuntimeState _runtime;
        private NineKeyHealerResources _resources;
        private readonly CharacterEffectState _effects = new CharacterEffectState();
        private float _safeReady;
        private string _encore = "";
        public RosterJob Job => CharacterRosterData.Find(_actor?.Definition?.id);
        public static bool Supports(CharacterDefinition definition) => CharacterSlots.IsNineKey(definition) && NineKeyHealerData.Supports(CharacterRosterData.Find(definition?.id));
        public bool Active => Supports(_actor?.Definition);
        public int Route => !Active ? 0 : Array.FindIndex(_actor.Definition.routes, r => r.id == _actor.RouteId) + 1;
        private float Now => RaidCombatAuthority.ServerTime;
        private float Power => GetComponent<PlayerController>()?.maxHp ?? GetComponent<PartyBotAI>()?.maxHp ?? 1;
        public static NineKeyHealerKit Ensure(GameObject go) => go.GetComponent<NineKeyHealerKit>() ?? go.AddComponent<NineKeyHealerKit>();
        private void Awake() { _actor = GetComponent<CharacterActor>(); _actor.RegisterHandler(NineKeyHealerData.Handler, Execute); }
        private void Bind()
        {
            if (!Active || _actor.RuntimeState == null || _runtime == _actor.RuntimeState) return;
            RemoveOwnedProtection();
            _runtime = _actor.RuntimeState; _resources = new NineKeyHealerResources(_runtime, Job); _effects.Clear(); _encore = ""; _safeReady = 0; ResetRoutes();
        }
        private void RemoveOwnedProtection()
        {
            if (_runtime == null || RaidCombatAuthority.Replica) return;
            int source = CombatMetricsManager.SourceId(transform);
            foreach (var member in PartyLife.OrderedMembers())
            {
                member.GetComponent<PlayerController>()?.RemoveShieldsFrom(source);
                member.GetComponent<PartyBotAI>()?.RemoveShieldsFrom(source);
            }
        }
        public void Clear(bool refund = false)
        {
            var composition = _resources?.Snapshot;
            if (composition != null) composition.verseReserved = false;
            RemoveOwnedProtection();
            if (refund && !RaidCombatAuthority.Replica) _song?.Cancel();
            _effects.Clear(); _encore = ""; _safeReady = 0;
            if (Active && _actor.RuntimeState != null)
            {
                _runtime = _actor.RuntimeState; _resources = new NineKeyHealerResources(_runtime, Job);
                if (composition != null) _resources.Restore(composition);
                ResetRoutes();
            }
            else { _runtime = null; _resources = null; }
        }
        public void BeforeInput(CharacterSlot slot)
        {
            if (!Active || RaidCombatAuthority.Replica || slot == CharacterSlot.Identity) return;
            if (slot == CharacterSlot.Gcd1 || slot == CharacterSlot.Gcd2 || slot == CharacterSlot.OffGcd1 || slot == CharacterSlot.Job1 || slot == CharacterSlot.OffGcd2 || slot == CharacterSlot.Counter || slot == CharacterSlot.Movement) Interrupt();
        }
        public float EffectiveRange(CharacterSkillDefinition skill) => (skill?.range ?? 0) + (skill != null && _actor.HasAuxiliary(NineKeyHealerData.Reach) && (skill.comboChain == 2 || skill.id == NineKeyHealerData.Id(Job, "q")) ? 5 : 0);
        public float CooldownScale(CharacterSkillDefinition skill) => skill?.id == NineKeyHealerData.Id(Job, "f") && _actor.HasAuxiliary(NineKeyHealerData.Wide) ? 1.15f : 1;
        private IEnumerable<Transform> Party(float radius = 30) => PartyLife.OrderedMembers().Where(t => CombatRange.LivingPartyMember(t) && CombatRange.InRange(transform, t, radius));
        private bool Valid(Transform target, float range) => CombatRange.LivingPartyMember(target) && CombatRange.InRange(transform, target, range);
        private void Effect(string key, string kind, Transform target, float value, float duration)
        {
            if (target == null) return;
            int id = CharacterKit.Id(target);
            _effects.Put(new CharacterTimedEffect { id = key + "@" + id, skill = key, kind = kind, target = id, value = value, end = Now + duration, next = Now + 1, interval = 1 });
        }
        private float Heal(Transform target, float amount, bool direct = false)
        {
            if (!CombatRange.LivingPartyMember(target)) return 0;
            var player = target.GetComponent<PlayerController>();
            float actual = player != null ? player.ReceiveHeal(amount, transform) : target.GetComponent<PartyBotAI>().ReceiveHeal(amount, transform);
            if (direct) { OnDirectHealing(transform, target); NineKeyDamageKit.OnDirectHealing(transform, target); }
            return actual;
        }
        private void Shield(Transform target, string key, float amount, float duration = 6) => CombatStatusEffects.Shield(target, transform, Job.Id + ".nine." + key, amount, duration, true);
        // Only the explicit Q/F base heal may call this. HoT and Encore cannot
        // recursively convert their own overheal or fire auxiliary traits.
        private void BasicHeal(Transform target, float amount)
        {
            float actual = Heal(target, amount, true);
            if (_actor.HasAuxiliary(NineKeyHealerData.Residual) && amount > actual)
                Effect("residual", "heal", target, Mathf.Min(Power * .03f, (amount - actual) * .25f) / 3, 3);
        }
        private bool Attack(CharacterSkillDefinition skill, Transform target, float amount, float stagger = 0, CombatDamageKind kind = CombatDamageKind.Direct)
        {
            if (!CombatRange.LivingEnemy(target) || !CombatRange.InRange(transform, target, EffectiveRange(skill))) return false;
            var boss = target.GetComponent<BossController>(); var add = target.GetComponent<BossAddMinion>();
            if (boss != null && boss.HpDamageBlocked || boss == null && add == null) return false;
            float before = boss != null ? boss.currentHp : add.currentHp;
            if (boss != null) boss.ReceiveRoleSkillAttack(Job.name, skill.name, 0, Mathf.RoundToInt(amount), Color.green, false, source: transform, origin: skill.id, kind: kind);
            else add.ReceivePlayerAttack(Job.name, skill.name, Mathf.RoundToInt(amount), Color.green, false, source: transform, origin: skill.id, kind: kind);
            bool hit = (boss != null ? boss.currentHp : add.currentHp) < before || AiSpectatorDamage.ZeroHpDamageMode(transform);
            if (hit && stagger > 0) CombatCounterTarget.Ensure(target).AddStagger(stagger);
            return hit;
        }
        private bool Execute(CharacterSkillDefinition skill, Transform target)
        {
            if (!Active || RaidCombatAuthority.Replica || StageManager.PlayerSkillsSuspended || !CombatRange.LivingPartyMember(transform)) return false;
            Bind(); string key = skill.id.Substring((Job.Id + ".nine.").Length);
            if (skill.comboChain > 0) return Combo(skill, target);
            if (key == "q")
            {
                if (!Valid(target, EffectiveRange(skill))) return false;
                BasicHeal(target, Power * .16f); CombatStatusEffects.Ensure(target.gameObject).CleanseOne(AegisPaladinData.DispelTags);
                if (Job.index == 3) Effect("q.hot", "heal", target, Power * .015f, 4);
                if (Job.index == 4) Shield(target, "q", Power * .04f);
                if (Job.index == 5) Effect("q.guard", "reduction", target, .12f, 4);
                if (_actor.HasAuxiliary(NineKeyHealerData.Safe) && Now >= _safeReady) { _safeReady = Now + 8; Shield(target, "safe", Power * .025f); }
                _encore = "q"; return true;
            }
            if (key == "f" || key == "v")
            {
                foreach (var ally in Party(key == "f" && _actor.HasAuxiliary(NineKeyHealerData.Wide) ? 35 : 30))
                {
                    float amount = Power * (key == "v" ? .25f : .1f);
                    if (key == "f") BasicHeal(ally, amount); else Heal(ally, amount, true);
                    if (Job.index == 4) Shield(ally, key, Power * (key == "v" ? .12f : .05f));
                    else Effect(key + ".hot", "heal", ally, Power * .015f, 5);
                }
                if (key == "f") _encore = "f";
                if (key == "v" && Job.index == 5) Attack(skill, target, 500);
                return true;
            }
            if (key == "c")
            {
                if (!Attack(skill, target, 100)) return false;
                var counter = CombatCounterTarget.Ensure(target); if (!counter.TryCounter(transform, skill.range, 40)) counter.AddStagger(40); return true;
            }
            if (key.StartsWith("a", StringComparison.Ordinal)) return Utility(key, skill, target);
            if (key.StartsWith("r", StringComparison.Ordinal)) return Identity(skill, target);
            return false;
        }
        private bool Combo(CharacterSkillDefinition skill, Transform target)
        {
            int chain = skill.comboChain, step = chain == 1 ? _actor.Snapshot.combo1 : _actor.Snapshot.combo2;
            if (chain == 1)
            {
                if (!Attack(skill, target, step == 3 ? 180 : 100)) return true;
            }
            else
            {
                if (!Valid(target, EffectiveRange(skill))) return false;
                int variant = skill.id.EndsWith("g21", StringComparison.Ordinal) ? 1 : skill.id.EndsWith("g22", StringComparison.Ordinal) ? 2 : 0;
                float amount = Power * (step == 3 ? .07f : .04f);
                if (variant == 1)
                {
                    var recipients = PartyLife.OrderedMembers().Where(t => Valid(t, EffectiveRange(skill)) && CombatRange.InRange(target, t, 6)).ToArray();
                    foreach (var ally in recipients) Heal(ally, amount * .7f / Mathf.Max(1, recipients.Length), true);
                }
                else Heal(target, Job.index == 3 && variant == 2 ? Power * (step == 1 ? .055f : step == 2 ? .04f : .025f) : amount * (variant == 2 ? .8f : 1), true);
                if (step == 3)
                {
                    if (variant == 0 && Job.index != 4) Effect("combo.hot", "heal", target, Power * .01f, 3);
                    if (variant == 0 && Job.index == 4 || variant == 2 && Job.index == 5) Shield(target, "combo", Power * .025f);
                    if (variant == 2 && Job.index == 4) Effect("combo.speed", "speed", target, .15f, 3);
                }
            }
            if (_actor.ClaimHealerDirect(skill)) DirectRoute(chain, target);
            if (step == 3 && _actor.ClaimHealerFinisher(skill)) { _resources.CompleteCombo(chain); FinishRoute(chain); }
            return true;
        }
        private bool Utility(string key, CharacterSkillDefinition skill, Transform target)
        {
            if (Job.index == 5 && key == "a0") return _resources.Supply();
            if (Job.index == 4 && key == "a1")
            {
                if (_encore == "q") { var ally = _actor.ActionSupportTarget; if (!Valid(ally, 25)) return false; Heal(ally, Power * .08f); return true; }
                if (_encore != "f") return false;
                foreach (var ally in Party()) Heal(ally, Power * .05f); return true;
            }
            if (Job.index == 4 && key == "a2") { foreach (var ally in Party()) Effect("tailwind", "speed", ally, .15f, 5); return true; }
            if (skill.target == SkillTargetRule.LivingAlly)
            {
                if (!Valid(target, EffectiveRange(skill))) return false;
                if (Job.index == 3 && key == "a1") Effect("bark", "reduction", target, .15f, 5);
                else
                {
                    if (Job.index == 3) CombatStatusEffects.Ensure(target.gameObject).CleanseOne(AegisPaladinData.DispelTags);
                    else Shield(target, "cushion", Power * .05f);
                    Effect("utility.hot", "heal", target, Power * .015f, 4);
                }
                return true;
            }
            Attack(skill, target, 150, 80); return true;
        }
        private void Update()
        {
            if (!Active || RaidCombatAuthority.Replica || StageManager.PlayerSkillsSuspended || !CombatRange.LivingPartyMember(transform)) return;
            Bind();
            TickRoutes(Time.deltaTime);
            foreach (var effect in _effects.Data.effects.ToArray())
            {
                if (effect.kind == "heal") { int ticks = CharacterEffectState.TakeTicks(effect, Now); for (int i = 0; i < ticks; i++) Heal(CharacterKit.Resolve(effect.target), effect.value); }
                if (effect.end <= Now) _effects.Remove(effect.id);
            }
        }
    }
}
