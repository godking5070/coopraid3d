using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class CharacterKit
    {
        private bool Execute(CharacterSkillDefinition s, Transform target)
        {
            _attackSequence = true; _sequenceConsumed = false;
            bool criticalEligible = Job?.index != 10 || s.comboChain == 2 || s.castTime > 0;
            _sequenceCritical = criticalEligible && Has("nextCrit");
            _sequenceDamage = Effect("nextDamage")?.value ?? 0;
            _sequenceChance = Effect("nextChance")?.value ?? 0;
            try { return ExecuteSkill(s, target); }
            finally { _attackSequence = false; _sequenceCritical = false; _sequenceDamage = _sequenceChance = 0; ForceCritical = false; }
        }
        private bool ExecuteSkill(CharacterSkillDefinition s, Transform target)
        {
            if (!Active || RaidCombatAuthority.Replica || _player == null || _player.IsDead || Unavailable(s, target).Length > 0)
                return false;
            string key = CharacterRosterData.Key(s.id);
            var a = Job.Ability(key);
            if (a == null)
                return false;
            _recordedHits.Clear();
            if (CanAirAttack(s, target))
            {
                var members = Route == 1 ? Party() : Party().Where(t => t.GetComponent<PartyLife>()?.IsTank == true).Take(1);
                foreach (var ally in members)
                    Heal(ally, a.power * .3f * .5f * (Maximize ? 1.5f : 1));
                if (key == "a" || Combo(s) == 3)
                    Gain(7.5f);
                return true;
            }

            if (key == "g1" || key == "g2")
            {
                bool success = ComboAttack(s, a, target);
                if (success)
                    RememberAttack(key, target);
                return success;
            }

            if (key == "c")
            {
                if ((Job.index == 2 || Job.index == 6 || Job.index == 8) && !Approach(target, Job.index == 6))
                    return false;
                if (!CombatRange.LivingEnemy(target) || !CombatRange.InRange(transform, target, s.range))
                    return false;
                var counter = CombatCounterTarget.Ensure(target);
                int opportunity = counter.OpportunityId;
                bool validCounter = counter.CanCounter(transform, s.range);
                if (Attack(target, a, applyStagger: false) <= 0) return false;
                if (!validCounter || !counter.TryCounter(transform, s.range, StaggerPower(target, a), opportunity))
                    counter.AddStagger(StaggerPower(target, a));
                if (Job.index == 1)
                    GrantSuperArmor(transform,.5f);
                return true;
            }

            if (key == "q" || key == "e")
                return Support(s, a, target, key == "q");
            if (key == "a")
                return Weave(a, target);
            bool result = key.StartsWith("r") ? ActiveSkill(a, target) : key.StartsWith("f") ? WeaveChoice(a, target) : key.StartsWith("v") ? Ultimate(a, target) : key.StartsWith("z") && Identity(a, target);
            if (result && key.StartsWith("r") && a.power > 0)
                RememberAttack(key, target);
            if (result && key.StartsWith("z") && Job.index == 4)
                Data.notes = 0;
            return result;
        }

        private bool ComboAttack(CharacterSkillDefinition s, RosterSkill a, Transform target)
        {
            int step = Combo(s), chain = s.comboChain;
            float mult = step == 1 ? 1 : step == 2 ? 1.5f : CharacterRosterRules.ComboThirdMultiplier;
            bool third = step == 3, back = Back(target), flank = Flank(target);
            if (Job.index == 11 && chain == 2 && Has("meleeLeap") && Route != 3 && !Overcome && !CombatRange.InRange(transform, target, 3))
            {
                if (!Approach(target))
                    return false;
                State.Remove("meleeLeap");
            }

            if (Job.index == 6 && Has("ninjutsu"))
            {
                return CastNinjutsu(a.key, target);
            }

            if (Job.index == 6)
            {
                bool valid = chain == 1 ? flank : back;
                if (!valid)
                    mult *= .65f;
                if (third && chain == 2 && back)
                    mult *= Route == 1 ? 1.8f : 1.4f;
            }

            if (Job.index == 1 && Route == 2 && third)
                mult *= 1.8f;
            if (Job.index == 7 && chain == 1 && third && back)
                Self("penetration", "뇌전 관통", .05f, .3f);
            if (Job.index == 7 && Has(chain == 1 ? "next1" : "next2"))
                mult *= 1.35f;
            if (Job.index == 11)
            {
                if (Route != 2 && Route != 3 && Data.lastCombo == chain)
                    mult *= .7f;
                if (Has(chain == 1 ? "next1" : "next2"))
                    mult *= 1.3f;
            }

            bool area = chain == 2 && (Job.index != 8) || Job.index == 9 && Route == 3;
            if (Job.index == 2 && Route == 1)
                mult *= 1.2f;
            float actual = AttackArea(target, a, mult, area, Job.index == 8 && chain == 2 && third);
            if (actual <= 0)
                return false;
            State.Remove(chain == 1 ? "next1" : "next2");
            if (Job.index == 2 && chain == 2 && (step == 1 || third))
            {
                _actor.SpendResource(Job.Resource, 1);
                Recoil();
            }

            if (Job.index == 8 && chain == 2 && third)
                _actor.SpendResource(Job.Resource, 2);
            if (Job.index == 10)
            {
                Gain(third ? 20 : 8);
                if (chain == 2)
                {
                    State.Remove("nextInstant");
                    State.Remove("starHaste");
                }
            }

            Data.lastSkill = a.key;
            Data.lastPower = a.power * mult;
            Data.lastTarget = Id(target);
            if (!third)
                return true;
            if (Job.Role == 2)
                _actor.GainResource(Job.Id + ".awakening", 1);
            switch (Job.index)
            {
                case 1:
                    if (chain == 1)
                    {
                        float bleed = a.power * .2f + (Route == 3 ? (Effect("dot.g1@" + Id(target))?.value ?? 0) : 0);
                        Periodic("dot", target, a, 8, 1, Mathf.Min(a.power * 2, bleed));
                        Status("armorDown", a.name, target, 8, .05f);
                    }
                    else
                        Leech(actual * .3f);
                    break;
                case 2:
                    if (chain == 1 && !Has("overheat"))
                    {
                        Gain(1);
                        Self("speed", "브레스 장전", 4, .15f);
                    }

                    break;
                case 3:
                    if (chain == 1)
                        Gain(.5f);
                    else
                        foreach (var ally in Party())
                            if (Flat(ally.position, target.position) <= 15)
                                Heal(ally, MaxHp(ally) * .05f);
                    break;
                case 4:
                    AddNote(chain == 1 ? 1 : 2);
                    if (chain == 1)
                        BuffParty("attack", "소프라노 공명", 8, .03f);
                    else
                        ShieldParty(.05f, 6, a.key);
                    break;
                case 5:
                    Gain(15);
                    if (chain == 1)
                        _actor.ReduceCooldowns(new[] { Job.SkillId("q"), Job.SkillId("e") }, 2);
                    else
                    {
                        Ground(a, target.position, 5, 4, 40, .02f);
                        Status("vulnerability", a.name, target, 5, .05f);
                    }

                    break;
                case 6:
                    Gain((chain == 1 ? flank : back) ? 2 : 1);
                    if (chain == 1 && flank)
                        Self("crit", "측면 동맥", 8, .2f);
                    if (chain == 2 && back)
                        _actor.RestoreCharge(Job.SkillId("a"));
                    if (Has("shadowFinisher"))
                    {
                        float backScale = chain == 2 && !back ? (Route == 1 ? 1.8f : 1.4f) / .65f : chain == 1 && !flank ? 1 / .65f : 1;
                        foreach (var hit in _recordedHits.ToArray())
                            CombatSecondaryDamage.Apply(transform, Resolve(hit.Key), hit.Value * backScale, Job.SkillId(a.key) + ".shadow", CombatDamageKind.Extra);
                        State.Remove("shadowFinisher");
                    }

                    break;
                case 7:
                    Gain(CharacterEffectState.Alternating(Data.lastCombo, chain) ? 30 : 10);
                    if (chain == 1 && back)
                        Self("next2", "뇌전 일섬", 8);
                    if (chain == 2 && flank)
                    {
                        Self("next1", "폭풍 파쇄격", 8);
                        CombatSecondaryDamage.Apply(transform, target, actual * .3f, Job.SkillId(a.key), CombatDamageKind.Extra, true);
                    }

                    break;
                case 8:
                    if (chain == 1)
                    {
                        Gain(2);
                        Self("speed", "실버 불릿", 4, .15f);
                    }

                    break;
                case 9:
                    Gain(chain == 1 ? 15 : 25);
                    if (chain == 1)
                        Status("windMark", "바람의 표식", target, 8, .15f);
                    break;
                case 10:
                    if (chain == 1)
                        Self("starHaste", "별의 가속", 10);
                    break;
                case 11:
                    if (Route == 2 || Route == 3 || Data.lastCombo == 0 || CharacterEffectState.Alternating(Data.lastCombo, chain)) Gain(1);
                    if (chain == 1)
                        Self("meleeLeap", "근접 도약", 10);
                    Self(chain == 1 ? "next2" : "next1", "거울 교차", 10);
                    if (Maximize && CharacterEffectState.Alternating(Data.lastCombo, chain) && (chain == 1 || !Overcome && CombatRange.BoundaryDistance(transform, target) <= 3))
                    {
                        Self("dance", "경계의 무희", 8, Mathf.Min(.5f, (Effect("dance")?.value ?? 0) + .25f));
                    }

                    break;
            }

            Data.lastCombo = chain;
            return true;
        }

        private void Recoil()
        {
            if (Job.index != 2)
                return;
            if (Route != 2 && !Overcome)
                Self("root", "포격 반동", .6f);
            if (Resource <= 0)
                Self("overheat", "총열 과열", CharacterRosterRules.OverheatSeconds(Overcome, Maximize));
        }

        private bool Weave(RosterSkill a, Transform target)
        {
            if (Job.index == 6 && Has("ninjutsu"))
            {
                return CastNinjutsu(a.key, target);
            }

            if (Job.index == 11)
            {
                var previous = Job.Ability(Data.lastSkill);
                if (previous == null || !CombatRange.LivingEnemy(target))
                    return false;
                float hit = 0;
                foreach (var saved in Data.effects.Where(e => e.kind == "echo" && e.end > Now).ToArray())
                {
                    var victim = Resolve(saved.target);
                    if (CombatRange.LivingEnemy(victim))
                    {
                        hit += CombatSecondaryDamage.Apply(transform, victim, saved.value, Job.SkillId(previous.key) + ".echo", CombatDamageKind.Extra);
                        _player.PlaySkillLineVisual(victim.position, Tint);
                    }
                }

                if (hit <= 0)
                    return false;
                Self("clonePosition", "거울상", 20);
                var p = Effect("clonePosition");
                Vector3 point = target.position + (target.position - transform.position).normalized * 2;
                p.x = point.x;
                p.y = transform.position.y;
                p.z = point.z;
                if (Route == 1)
                    Data.clones = Mathf.Min(3, Data.clones + 1);
                return true;
            }

            int hits = Job.index == 6 ? 2 : Job.index == 9 ? 5 : Job.index == 10 ? 4 : 1;
            if (Job.index == 7)
            {
                Self("mirage", "미라지 다이브 준비", 6);
            }

            float total = 0;
            for (int i = 0; i < hits; i++)
                total += AttackArea(target, a, 1, Job.index == 1 || Job.index == 2);
            if (total <= 0)
                return false;
            if (Job.index == 3)
            {
                var ally = Party().OrderBy(t => Hp(t) / MaxHp(t)).FirstOrDefault();
                Periodic("hot", ally, a, 6, 1, .025f);
            }

            if (Job.index == 4)
                AddNote(3);
            if (Job.index == 5)
            {
                Gain(15);
                HealParty(.03f);
            }

            if (Job.index == 8)
                Gain(1);
            return true;
        }

        private bool Support(CharacterSkillDefinition s, RosterSkill a, Transform target, bool q, float echo = 1)
        {
            if (Job.Role == 2)
            {
                if (q && (!CombatRange.LivingPartyMember(target) || !CombatRange.InRange(transform, target, EffectiveRange(s))))
                    return false;
                bool awakened = echo == 1 && _actor.CommittingCostScale == 0 && _actor.Resource(Job.Id + ".awakening") >= 1;
                float scale = echo * (awakened ? 1.3f : 1);
                if (awakened)
                    _actor.SpendResource(Job.Id + ".awakening", 1);
                if (q)
                {
                    Heal(target, MaxHp(target) * (Job.index == 4 ? .08f : .3f) * scale);
                    if (Job.index == 4)
                        Shield(target, .25f * scale, 6, a.key);
                    Status("reduction", a.name, target, 6, Job.index == 5 ? .25f : .2f);
                    if (Job.index == 3)
                        Periodic("hot", target, a, 6, 1, .025f * scale);
                    if (Job.index == 5)
                        Status("attack", a.name, target, 6, .1f);
                }
                else
                {
                    HealParty((Job.index == 4 ? .1f : .25f) * scale);
                    if (Job.index == 3)
                        foreach (var ally in Party())
                            Periodic("hot", ally, a, 8, 1, .02f * scale);
                    if (Job.index == 4)
                        ShieldParty(.15f * scale, 8, a.key);
                    if (Job.index == 5)
                        BuffParty("debuffBlock", a.name, 5, 1);
                }

                Data.lastHeal = q ? "q" : "e";
                Data.lastTarget = Id(target);
                return true;
            }

            if (q)
            {
                float reduction = Job.index == 1 ? .3f : Job.index == 11 ? .4f : Job.index == 6 ? .3f : .35f;
                Self("reduction", a.name, a.duration, reduction);
                if (Job.index == 1)
                {
                    Self("maxHp", a.name, 10, .3f);
                    Self("healingReceived", a.name, 10, .25f);
                    Recalculate();
                }

                if (Job.index == 2)
                    BuffParty("reduction", a.name, 8, .15f, 8);
                if (Job.index == 6)
                    Self("invulnerable", a.name, 1.5f);
                if (Job.index == 7)
                    GrantSuperArmor(transform,8);
                if (Job.index == 8)
                    Self("speed", a.name, 6, .3f);
                if (Job.index == 9)
                    while (CombatStatusEffects.Ensure(gameObject).CleanseOne(new[] { "slow", "root" }))
                    {
                    }

                if (Job.index == 10)
                    Self("instant", a.name, 8);
                return true;
            }

            switch (Job.index)
            {
                case 1:
                    Taunt(target);
                    Status("attackDown", a.name, target, 8, .1f);
                    break;
                case 2:
                    Taunt(target);
                    Status("partyExtra", a.name, target, 8, .1f);
                    break;
                case 6:
                    Self("haste", a.name, 10, .15f);
                    BuffParty("critDamage", a.name, 10, .12f);
                    break;
                case 7:
                    var ally = Party().Where(t => t != transform && t.GetComponent<CharacterActor>()?.Definition?.role == CharacterRole.Damage).OrderBy(t => Flat(t.position, transform.position)).FirstOrDefault();
                    Self("damage", a.name, 10, .12f);
                    Self("crit", a.name, 10, .1f);
                    if (ally != null)
                    {
                        Status("damage", a.name, ally, 10, .12f);
                        Status("crit", a.name, ally, 10, .1f);
                    }

                    break;
                case 8:
                    BuffParty("crit", a.name, 10, .1f);
                    Self("attack", a.name, 10, .1f);
                    break;
                case 9:
                    BuffParty("speed", a.name, 10, .1f);
                    BuffParty("haste", a.name, 10, .1f);
                    Status("vulnerability", a.name, target, 10, .08f);
                    break;
                case 10:
                    Self("attack", a.name, 10, .15f);
                    BuffParty("damage", a.name, 10, .1f);
                    break;
                case 11:
                    BuffParty("crit", a.name, 10, .1f);
                    BuffParty("extra", a.name, 10, .05f);
                    break;
            }

            return true;
        }

        private bool ActiveSkill(RosterSkill a, Transform target)
        {
            switch (Job.index)
            {
                case 1:
                    if (a.key == "r2")
                    {
                        Channel(a, target, 2, 2f / 6, a.power, false);
                        return true;
                    }

                    if (a.key == "r3" && !Approach(target))
                        return false;
                    break;
                case 2:
                    if (a.key == "r1")
                    {
                        Channel(a, target, 2.5f, .5f, a.power, true);
                        return true;
                    }

                    if (a.key == "r2" && !Approach(target))
                        return false;
                    if (a.key == "r3")
                    {
                        ShieldParty(.15f, 5, a.key, 6);
                        Self("wall", a.name, 5);
                        var wall = Effect("wall");
                        wall.x = transform.position.x;
                        wall.y = transform.position.y;
                        wall.z = transform.position.z;
                        wall.yaw = transform.eulerAngles.y;
                        wall.radius = 6;
                        return true;
                    }

                    break;
                case 3:
                    if (a.key == "r1")
                    {
                        if (target == transform || !CombatRange.LivingPartyMember(target))
                            return false;
                        var pc = target.GetComponent<PlayerController>();
                        var bot = target.GetComponent<PartyBotAI>();
                        Vector3 point = transform.position + transform.right * 1.5f;
                        if (pc != null)
                            pc.StartPulledByHealerRescue(point);
                        else if (bot != null)
                            bot.StartPulledByPlayerRescue(point);
                        Shield(target, .15f, 6, a.key);
                        return true;
                    }

                    if (a.key == "r2")
                    {
                        Ground(a, target.position, 10, 6, 0, .03f);
                        return true;
                    }

                    Status("accuracyDown", a.name, target, 6, .1f);
                    break;
                case 4:
                    if (a.key == "r1")
                        Status("attackDown", a.name, target, 8, .1f);
                    if (a.key == "r2")
                    {
                        if (!Approach(target))
                            return false;
                        ShieldParty(.2f, 5, a.key, 6);
                        foreach (var ally in Party(6))
                            GrantSuperArmor(ally,5);
                    }

                    if (a.key == "r3")
                    {
                        Channel(a, transform, 3, .25f, 0, false);
                        return true;
                    }

                    break;
                case 5:
                    if (a.key == "r1")
                    {
                        Channel(a, target, 2.5f, .5f, a.power, false);
                        return true;
                    }

                    if (a.key == "r2")
                        Status("armorDown", a.name, target, 10, .12f);
                    if (a.key == "r3")
                    {
                        Status("deathPrevent", a.name, target, 5);
                        return true;
                    }

                    break;
                case 6:
                    if (a.key == "r2")
                    {
                        Periodic("dot", target, a, 3, .5f, a.power);
                        return true;
                    }

                    if (a.key == "r3")
                        Periodic("dot", target, a, 10, 1, 70);
                    break;
                case 7:
                    if (a.key == "r2")
                    {
                        if (!Approach(target))
                            return false;
                        Self("mirage", "미라지 다이브 준비", 6);
                    }

                    if (a.key == "r3")
                    {
                        Channel(a, target, 2, .2f, a.power, false);
                        Self("projectileGuard", a.name, 2);
                        return true;
                    }

                    break;
                case 8:
                    if (a.key == "r1")
                    {
                        Channel(a, target, 1.5f, .5f, a.power, Route == 1 || Overcome);
                        return true;
                    }

                    if (a.key == "r3")
                    {
                        Channel(a, target, 2, .2f, a.power, true);
                        return true;
                    }

                    break;
                case 9:
                    if (a.key == "r2" || a.key == "r3")
                    {
                        Ground(a, target.position, a.duration, a.radius, a.power);
                        return true;
                    }

                    break;
                case 10:
                    if (a.key == "r1")
                    {
                        Ground(a, target.position, 4, 5, a.power);
                        return true;
                    }

                    if (a.key == "r2")
                    {
                        Self("polaris", a.name, 10);
                        Effect("polaris").x = transform.position.x;
                        Effect("polaris").z = transform.position.z;
                        return true;
                    }

                    break;
                case 11:
                    if (a.key == "r1" && Route != 3 && !Approach(target))
                        return false;
                    if (a.key == "r2")
                    {
                        float hits = 0;
                        for (int i = 0; i < 6; i++)
                            hits += Attack(target, a);
                        return hits > 0;
                    }

                    break;
            }

            return AttackArea(target, a, Job.index == 9 && a.key == "r1" ? 1 + Mathf.Min(1, CombatRange.BoundaryDistance(transform, target) / 30) : 1, true, Job.index == 6 && a.key == "r1" && Back(target) || Job.index == 7 && a.key == "r1") > 0;
        }

        private bool WeaveChoice(RosterSkill a, Transform target)
        {
            switch (Job.index)
            {
                case 1:
                    if (a.key == "f1")
                    {
                        _player.currentHp *= .85f;
                        Gain(50);
                        Self("nextCrit", a.name, 15);
                        return true;
                    }

                    if (a.key == "f2")
                        Self("haste", a.name, 3, .2f);
                    if (a.key == "f3")
                    {
                        float total = Data.effects.Where(e => e.kind == "history" && e.end > Now).Sum(e => e.value);
                        if (total <= 0)
                            return false;
                        CombatStatusEffects.Shield(transform, transform, Job.SkillId(a.key), total, 6);
                        return true;
                    }

                    break;
                case 2:
                    if (a.key == "f1")
                    {
                        State.Remove("overheat");
                        Gain(_actor.ResourceMaximum(Job.Resource));
                        Self("cannonDamage", a.name, 5, .2f);
                        return true;
                    }

                    if (a.key == "f2")
                    {
                        if (!Backstep(4))
                            return false;
                        Self("invulnerable", a.name, .8f);
                    }

                    if (a.key == "f3")
                    {
                        Detonate(target, "dot");
                        BuffParty("speed", a.name, 5, .2f);
                    }

                    break;
                case 3:
                    if (a.key == "f1")
                    {
                        foreach (var owner in FindObjectsByType<CharacterKit>(FindObjectsSortMode.None).Where(k => k.Active && CombatRange.LivingPartyMember(k.transform)))
                            foreach (var e in owner.Data.effects.Where(e => e.kind == "hot" && e.end > Now).ToArray())
                            {
                                var recipient = Resolve(e.target);
                                owner.Heal(recipient, MaxHp(recipient) * e.value * Mathf.Max(0, Mathf.Floor((e.end - e.next) / e.interval) + 1));
                                owner.State.Remove(e.id);
                            }

                        return true;
                    }

                    if (a.key == "f2")
                    {
                        foreach (var ally in Party())
                            CombatStatusEffects.Ensure(ally.gameObject).CleanseOne(AegisPaladinData.DispelTags);
                        BuffParty("speed", a.name, 4, .25f);
                        return true;
                    }

                    Status("leechTaken", a.name, target, 6, .05f);
                    break;
                case 4:
                    if (a.key == "f1")
                    {
                        bool q = Data.lastHeal == "q";
                        Transform ally = q ? Resolve(Data.lastTarget) : transform;
                        if (q && !CombatRange.LivingPartyMember(ally))
                            return false;
                        return Support(_actor.Definition.Skill(Job.SkillId(Data.lastHeal)), Job.Ability(Data.lastHeal), ally, q, .5f);
                    }

                    if (a.key == "f2")
                    {
                        foreach (var ally in Party())
                        {
                            var actor = CharacterActor.Ensure(ally.gameObject);
                            actor.ReduceCooldowns(new[] { actor.Selected(CharacterSlot.Movement).id }, 5);
                        }
                        return true;
                    }

                    if (a.key == "f3")
                    {
                        int source = CombatMetricsManager.SourceId(transform);
                        float shield = Party().Sum(t => t.GetComponent<PlayerController>()?.ShieldsFrom(source) ?? t.GetComponent<PartyBotAI>()?.ShieldsFrom(source) ?? 0);
                        if (shield <= 0)
                            return false;
                        return CombatSecondaryDamage.Apply(transform, target, shield, Job.SkillId(a.key), CombatDamageKind.Extra) > 0;
                    }

                    break;
                case 5:
                    if (a.key == "f1")
                    {
                        foreach (var ally in Party())
                        {
                            var actor = ally.GetComponent<CharacterActor>();
                            if (actor != null)
                                actor.ReduceCooldowns(new[] { actor.Selected(CharacterSlot.OffGcd1)?.id, actor.Selected(CharacterSlot.OffGcd2)?.id }.Where(id => id != null), 3);
                        }

                        return true;
                    }

                    if (a.key == "f2")
                    {
                        BuffParty("reduction", a.name, 4, .25f);
                        return true;
                    }

                    _player.currentHp *= .9f;
                    Gain(50);
                    _actor.GainResource(Job.Id + ".awakening", 2);
                    return true;
                case 6:
                    if (a.key == "f1")
                    {
                        Self("shadowFinisher", a.name, 3);
                        return true;
                    }

                    if (a.key == "f2")
                    {
                        Gain(2);
                        Self("speed", a.name, 4, .2f);
                    }

                    if (a.key == "f3")
                    {
                        SetReturn(5);
                        return true;
                    }

                    break;
                case 7:
                    if (a.key == "f1")
                        State.Remove("mirage");
                    if (a.key == "f2")
                        Gain(30);
                    if (a.key == "f3")
                        return Backstep(15);
                    break;
                case 8:
                    if (a.key == "f1")
                    {
                        Gain(6);
                        Self("nextChance", a.name, 15, .3f);
                        return true;
                    }

                    if (a.key == "f2")
                        Periodic("dot", target, a, 5, 1, 50);
                    if (a.key == "f3" && !Backstep(10))
                        return false;
                    break;
                case 9:
                    if (a.key == "f2")
                    {
                        if (!Backstep(5))
                            return false;
                        for (int i = 0; i < 2; i++)
                            Attack(target, a);
                    }

                    if (a.key == "f3")
                    {
                        Self("evasion", a.name, 4, 1);
                        Self("nextDamage", a.name, 10, .3f);
                        _player.targetBoss?.ClearCharacterThreat(transform);
                        return true;
                    }

                    break;
                case 10:
                    if (a.key == "f1")
                    {
                        Self("nextInstant", a.name, 15);
                        Self("nextCrit", a.name, 15);
                        return true;
                    }

                    if (a.key == "f2")
                    {
                        Shield(transform, .2f, 8, a.key);
                        Self("satellites", a.name, 8, 3);
                        return true;
                    }

                    if (a.key == "f3")
                    {
                        var zone = Effect("polaris");
                        if (zone != null)
                            return MoveTo(new Vector3(zone.x, transform.position.y, zone.z));
                        var healer = Party().Where(t => t != transform && t.GetComponent<PartyLife>()?.IsHealer == true).OrderBy(t => Flat(t.position, transform.position)).FirstOrDefault();
                        return healer != null && Approach(healer);
                    }

                    break;
                case 11:
                    if (a.key == "f1")
                    {
                        var clone = Effect("clonePosition");
                        if (clone == null)
                            return false;
                        Vector3 previous = transform.position;
                        if (!MoveTo(new Vector3(clone.x, clone.y, clone.z)))
                            return false;
                        clone.x = previous.x;
                        clone.y = previous.y;
                        clone.z = previous.z;
                        foreach (var enemy in Enemies())
                            if (CombatRange.InRange(transform, enemy, 5))
                                Attack(enemy, a);
                        return true;
                    }

                    if (a.key == "f2")
                        Gain(1);
                    if (a.key == "f3")
                    {
                        Shield(transform, .15f, 5, a.key);
                        foreach (var ally in Party().Where(t => t != transform).OrderBy(t => Flat(t.position, transform.position)).Take(2))
                            Shield(ally, .15f, 5, a.key);
                        return true;
                    }

                    break;
            }

            return Attack(target, a, guaranteedCrit: Job.index == 9 && a.key == "f1") > 0;
        }

        private bool Ultimate(RosterSkill a, Transform target)
        {
            float actual = AttackArea(target, a, Job.index == 4 && a.key == "v1" ? 7 : 1, true);
            if (actual <= 0)
                return false;
            bool typeA = a.key == "v1";
            if (typeA)
            {
                if (Job.index == 1)
                    AttackArea(target, a, 1, true);
                if (Job.index == 2)
                {
                    _actor.RefreshSkillCooldowns(_actor.Definition.skills.Where(s => !s.id.EndsWith("v1") && !s.id.EndsWith("v2") && s.legacyAction == 0).Select(s => s.id));
                    Gain(_actor.ResourceMaximum(Job.Resource));
                    State.Remove("overheat");
                }

                if (Job.index == 3)
                    foreach (var ally in Party())
                    {
                        float overflow = Mathf.Max(0, MaxHp(ally) * HealingScale(transform, ally) - (MaxHp(ally) - Hp(ally)));
                        Heal(ally, MaxHp(ally));
                        CombatStatusEffects.Shield(ally, transform, Job.SkillId(a.key), overflow, 8);
                    }

                if (Job.index == 5)
                    foreach (var ally in Party())
                        Heal(ally, actual);
                if (Job.index == 7)
                    Self("invulnerable", a.name, 1);
                return true;
            }

            switch (Job.index)
            {
                case 1:
                    BuffParty("crit", a.name, 12, .15f);
                    BuffParty("leech", a.name, 12, .1f);
                    break;
                case 2:
                    Status("armorDown", a.name, target, 12, .15f);
                    Status("staggerTaken", a.name, target, 12, .2f);
                    break;
                case 3:
                    BuffParty("haste", a.name, 12, .15f);
                    break;
                case 4:
                    ShieldParty(.3f, 12, a.key, 1000);
                    BuffParty("damage", a.name, 12, .12f, 1000);
                    break;
                case 5:
                    BuffParty("critDamage", a.name, 12, .25f);
                    BuffParty("speed", a.name, 12, .2f);
                    break;
                case 6:
                    BuffParty("allBack", a.name, 12);
                    Status("armorDown", a.name, target, 12, .15f);
                    break;
                case 7:
                    Status("partyExtra", a.name, target, 12, .12f);
                    break;
                case 8:
                    Status("vulnerability", a.name, target, 12, .12f);
                    BuffParty("speed", a.name, 12, .15f);
                    break;
                case 9:
                    BuffParty("cooldownRate", a.name, 12, .2f);
                    BuffParty("attack", a.name, 12, .1f);
                    break;
                case 10:
                    BuffParty("penetration", a.name, 12, .2f);
                    BuffParty("regen", a.name, 12, 1);
                    break;
                case 11:
                    BuffParty("extra", a.name, 10, .12f);
                    break;
            }

            return true;
        }

        private bool Identity(RosterSkill a, Transform target)
        {
            int route = Route;
            if (route < 1)
                return false;
            switch (Job.index)
            {
                case 1:
                    if (route == 1)
                    {
                        Self("identity", a.name, 6);
                        Self("undying", a.name, 6);
                        Self("crit", a.name, 6, 1);
                        Self("leechScale", a.name, 6, 2);
                        return true;
                    }

                    if (route == 3)
                    {
                        float duration = Mathf.Max(.25f, Resource / 20);
                        Self("identity", a.name, duration);
                        Self("reduction", a.name, duration, .25f);
                        Channel(a, target, duration, .25f, a.power, true);
                        return true;
                    }

                    break;
                case 2:
                    if (route == 1)
                    {
                        float ammo = Resource;
                        float hit = AttackArea(target, a, ammo, true);
                        if (hit <= 0)
                            return false;
                        _actor.SpendResource(Job.Resource, ammo);
                        Recoil();
                        return true;
                    }

                    if (route == 2)
                    {
                        Channel(a, target, .45f, .15f, a.power, false);
                        return true;
                    }

                    Ground(a, transform.position, 8, 8, 0);
                    Self("identity", a.name, 8);
                    return true;
                case 3:
                    if (route == 1)
                    {
                        HealParty(.3f);
                        BuffParty("maxHp", a.name, 8, .15f);
                        Self("identity", a.name, 8);
                        return true;
                    }

                    if (route == 2)
                    {
                        Self("portal", a.name, 8);
                        var portal = Effect("portal");
                        portal.x = transform.position.x;
                        portal.y = transform.position.y;
                        portal.z = transform.position.z;
                        portal.radius = 2;
                        return true;
                    }

                    break;
                case 4:
                    if (route == 1)
                    {
                        BuffParty("damage", a.name, 10, .2f);
                        BuffParty("critDamage", a.name, 10, .15f);
                        Self("identity", a.name, 10);
                        return true;
                    }

                    if (route == 2)
                    {
                        HealParty(.4f);
                        BuffParty("debuffImmunity", a.name, 5);
                        BuffParty("reduction", a.name, 5, .3f);
                        foreach (var ally in Party(PartyRadius))
                            GrantSuperArmor(ally,5);
                        Self("identity", a.name, 5);
                        return true;
                    }

                    break;
                case 5:
                    if (route == 1)
                    {
                        BuffParty("layers", a.name, 8, 5);
                        Self("identity", a.name, 8);
                        return true;
                    }

                    if (route == 2)
                        BuffParty("attack", a.name, 8, .15f);
                    if (route == 3)
                    {
                        foreach (var ally in Party().OrderBy(t => Hp(t) / MaxHp(t)).Take(2))
                        {
                            Heal(ally, MaxHp(ally));
                            Status("invulnerable", a.name, ally, 2.5f);
                        }

                        return true;
                    }

                    break;
                case 6:
                    if (route == 2)
                    {
                        Self("identity", a.name, 6);
                        return true;
                    }

                    if (route == 3)
                    {
                        Self("ninjutsu", a.name, 8);
                        return true;
                    }

                    if (Back(target))
                        Self("refundZ", a.name, .1f, 6);
                    break;
                case 7:
                    if (route == 1)
                    {
                        Self("mirage", "미라지 다이브 준비", 6);
                        return AttackArea(target, a, 1, true, true) > 0;
                    }

                    if (route == 2)
                    {
                        if (!Approach(target))
                            return false;
                        for (int i = 0; i < 8; i++)
                            Attack(target, a);
                        return Attack(target, a, 2) > 0;
                    }

                    Ground(a, target.position, 8, 5, a.power);
                    BuffParty("haste", a.name, 8, .15f);
                    return true;
                case 8:
                    if (route == 1)
                    {
                        Self("identity", a.name, 5);
                        Channel(a, target, 5, .1f, a.power, true);
                        return true;
                    }

                    if (route == 2)
                    {
                        Ground(a, target.position, 8, 5, a.power);
                        return true;
                    }

                    break;
                case 9:
                    if (route == 2)
                    {
                        Self("identity", a.name, 5);
                        Channel(a, target, 5, .125f, a.power, true);
                        return true;
                    }

                    if (route == 3)
                    {
                        Ground(a, target.position, 4, 7, a.power);
                        return true;
                    }
                    return IdentityBurst(target, a, a.hits);
                case 10:
                    if (route == 2)
                    {
                        Channel(a, target, 4, .25f, a.power, true);
                        Self("identity", a.name, 4);
                        return true;
                    }

                    if (route == 3)
                        Status("magicVulnerability", a.name, target, 6, .15f);
                    break;
                case 11:
                    if (route == 1)
                        return AttackArea(target, a, 1 + Data.clones, true) > 0;
                    if (route == 2)
                    {
                        Self("identity", a.name, 6);
                        return true;
                    }

                    return IdentityBurst(target, a, a.hits);
            }

            return AttackArea(target, a, 1, true) > 0;
        }

        // Preserve the existing total potency/stagger while giving the piercing arrow and prism
        // their specified multiple hits. Derived damage remains excluded from proc recursion.
        private bool IdentityBurst(Transform target, RosterSkill a, int hits)
        {
            var pulse = new RosterSkill(a.key, a.name, a.cooldown, a.description, a.power / hits, a.range, a.duration, a.radius)
                { shape = a.shape, angle = a.angle, stagger = a.stagger / hits };
            float total = 0;
            for (int i = 0; i < hits; i++) total += AttackArea(target, pulse, 1, true);
            return total > 0;
        }

        private bool CastNinjutsu(string trigger, Transform target)
        {
            var spell = CharacterRosterData.Ninjutsu(trigger);
            float actual = AttackArea(target, spell, 1, spell.radius > 0, trigger == "g2");
            if (actual <= 0)
                return false;
            State.Remove("ninjutsu");
            return true;
        }

        private void RememberAttack(string key, Transform target)
        {
            if (Job.index != 11 || _recordedHits.Count == 0)
                return;
            foreach (var old in Data.effects.Where(e => e.kind == "echo").ToArray())
                State.Remove(old.id);
            foreach (var hit in _recordedHits)
                State.Put(new CharacterTimedEffect { id = "echo@" + hit.Key, kind = "echo", skill = key, target = hit.Key, value = hit.Value, end = Now + 30, next = float.MaxValue });
            Data.lastSkill = key;
            Data.lastPower = _recordedHits.Values.Sum();
            Data.lastTarget = Id(target);
        }

        private void SetReturn(float seconds)
        {
            Data.returnEnd = Now + seconds;
            Data.returnX = transform.position.x;
            Data.returnY = transform.position.y;
            Data.returnZ = transform.position.z;
        }

        public bool TryReturn(CharacterSlot slot)
        {
            if (!Active || RaidCombatAuthority.Replica || Data.returnEnd <= Now || _player.IsDead || _player.IsStunned)
                return false;
            if (!(Job.index == 3 && slot == CharacterSlot.Movement || Job.index == 6 && slot == CharacterSlot.OffGcd2 && _actor.Selected(slot).id == Job.SkillId("f3")))
                return false;
            if (!MoveTo(new Vector3(Data.returnX, Data.returnY, Data.returnZ)))
                return false;
            Data.returnEnd = 0;
            if (Job.index == 6)
                foreach (var enemy in Enemies())
                    if (CombatRange.InRange(transform, enemy, 4))
                        Attack(enemy, Job.Ability("f3"));
            return true;
        }

        private void Detonate(Transform target, string kind)
        {
            foreach (var e in Data.effects.Where(e => e.kind == kind && e.target == Id(target)).ToArray())
            {
                Attack(target, Job.Ability(e.skill), e.value / Mathf.Max(1, Job.Ability(e.skill).power) * Mathf.Max(0, Mathf.Floor((e.end - e.next) / e.interval) + 1), CombatDamageKind.Periodic);
                State.Remove(e.id);
            }
        }
    }
}
