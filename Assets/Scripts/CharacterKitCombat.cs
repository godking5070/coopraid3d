using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class CharacterKit
    {
        private bool _attackSequence, _sequenceCritical, _sequenceConsumed;
        private float _sequenceDamage, _sequenceChance;
        private float _primaryDamage;
        private readonly Dictionary<int, float> _recordedHits = new Dictionary<int, float>();
        public bool ForceCritical { get; private set; }

        public static float Incoming(Transform target, float damage, CombatHitContext hit)
        {
            if (hit.Kind == CombatDamageKind.System || hit.Kind == CombatDamageKind.Transfer || hit.Kind == CombatDamageKind.Deferred)
                return damage;
            if (Protected(target))
                return 0;
            damage *= 1 - AuraReduction(target);
            damage = CharacterEffectState.ReducedDamage(damage, Buffs(target, "reduction").GroupBy(b => b.owner.Job.Id).Select(g => g.Max(b => b.effect.value)));
            var recipient = target?.GetComponent<CharacterKit>();
            if (recipient?.Active == true && recipient.Job.index == 7 && recipient.Overcome && recipient.Has("air") && hit.Kind == CombatDamageKind.Area)
                return 0;
            if (recipient?.Active == true && recipient.Job.index == 10 && recipient.Maximize && recipient._actor.Snapshot.castSkillId.Length > 0)
                damage *= .6f;
            if (CharacterEvolutionCards.StationaryBulwark(target))
                damage *= .75f;
            if (hit.Projectile && (Bonus(target, "projectileGuard") > 0 || WallBlocks(target, hit.Attacker)) && !hit.IgnoreGuard)
                return 0;
            damage *= 1 - Mathf.Clamp01(Bonus(hit.Attacker, "attackDown"));
            if (Bonus(hit.Attacker, "accuracyDown") > 0 && UnityEngine.Random.value < Bonus(hit.Attacker, "accuracyDown"))
                return 0;
            if (Bonus(target, "evasion") > 0 && UnityEngine.Random.value < Bonus(target, "evasion"))
                return 0;
            // Layered shields consume only on real positive attack damage, once per deduplicated hit.
            var layers = Buffs(target, "layers").FirstOrDefault();
            if (damage > 0 && layers.owner != null && layers.effect.value > 0)
            {
                float absorb = Mathf.Min(damage, MaxHp(target) * .04f);
                damage -= absorb;
                layers.effect.value--;
                CombatMetricsManager.Absorption(CombatMetricsManager.SourceId(layers.owner.transform), absorb, false);
            }

            return damage;
        }

        public static float PreserveLife(Transform target, float hp, CombatDamageKind kind)
        {
            if (kind == CombatDamageKind.System)
                return hp;
            if (Bonus(target, "undying") > 0)
                return Mathf.Max(1, hp);
            var prevent = Buffs(target, "deathPrevent").FirstOrDefault();
            if (hp <= 0 && prevent.owner != null)
            {
                prevent.owner.State.Remove(prevent.effect.id);
                return MaxHp(target) * .3f;
            }

            return hp;
        }

        public static float HealingScale(Transform source, Transform target)
        {
            float value = 1 + Bonus(target, "healingReceived");
            var kit = source?.GetComponent<CharacterKit>();
            var recipient = target?.GetComponent<CharacterKit>();
            if (recipient?.Active == true && recipient.Job.index == 9)
                value *= CharacterRosterRules.RangerReceivedMultiplier(recipient.Overcome, recipient.Maximize);
            if (kit?.Active == true)
            {
                if (kit.Job.index == 3 && kit.Route == 1)
                    value *= 1.3f;
                if (kit.Job.index == 3 && kit.Maximize)
                    value *= 1.4f;
                if (kit.Job.index == 5 && kit.Route == 3 && Hp(target) < MaxHp(target) * .5f)
                    value *= 2;
            }

            return value;
        }

        public static float ShieldScale(Transform source, Transform target = null)
        {
            float value = 1;
            var kit = source?.GetComponent<CharacterKit>();
            var recipient = target?.GetComponent<CharacterKit>();
            if (kit?.Active == true && kit.Job.index == 4)
            {
                if (kit.Route == 2)
                    value *= 1.4f;
                if (kit.Maximize)
                    value *= 1.5f;
            }

            if (recipient?.Active == true && recipient.Job.index == 9)
                value *= CharacterRosterRules.RangerReceivedMultiplier(recipient.Overcome, recipient.Maximize);
            return value;
        }

        public static float AttackScale(Transform source, Transform target, CombatDamageKind kind, bool scaleAttack)
        {
            if (kind != CombatDamageKind.Direct && kind != CombatDamageKind.Periodic && kind != CombatDamageKind.Unclassified)
                return 1;
            float value = 1;
            if (scaleAttack)
                value *= 1 + Bonus(source, "attack");
            value *= 1 + Bonus(source, "damage");
            value *= 1 + Bonus(target, "vulnerability");
            float penetration = Bonus(source, "penetration");
            var attackerKit = source?.GetComponent<CharacterKit>();
            if (attackerKit?.Active == true && (attackerKit.Job.index == 8 && attackerKit._currentAttack == attackerKit.Job.SkillId("z3") || attackerKit.Job.index == 5 && attackerKit._currentAttack == attackerKit.Job.SkillId("v1")))
                penetration = 1;
            value *= CharacterRosterRules.ArmorFactor(Bonus(target, "armorDown"), penetration);
            if (source?.GetComponent<CharacterActor>()?.Definition?.classification == AttackClassification.Caster)
                value *= 1 + Bonus(target, "magicVulnerability");
            float crit = (kind == CombatDamageKind.Direct && attackerKit?._attackSequence == true ? attackerKit._sequenceChance : 0) + Bonus(source, "crit") + Bonus(target, "critTaken") + (target?.GetComponent<CombatStatusEffects>()?.BrandActive == true ? .1f : 0);
            if (Buffs(target, "windMark").Any(b => b.owner.transform == source))
                crit += .15f;
            var kit = source?.GetComponent<CharacterKit>();
            if (kit?.ForceCritical == true || UnityEngine.Random.value < Mathf.Clamp01(crit))
                value *= 1.5f + Bonus(source, "critDamage") + (kit?.Active == true ? kit.CriticalBonus(target) : 0);
            return value;
        }

        public static void AfterAttack(Transform source, Transform target, float actual, string origin, CombatDamageKind kind)
        {
            if (actual <= 0 || !CombatRange.LivingPartyMember(source) || kind == CombatDamageKind.Extra || kind == CombatDamageKind.Reflection || kind == CombatDamageKind.Deferred || kind == CombatDamageKind.Transfer)
                return;
            float leech = Bonus(source, "leech") + Bonus(target, "leechTaken");
            var kit = source.GetComponent<CharacterKit>();
            if (kit?.Active == true)
            {
                kit._primaryDamage = actual;
                kit.OnDamage(actual, target, kind);
                if (kit.Job.index == 1)
                    leech += (.05f + kit.Resource / 100 * .1f) * (kit.Route == 1 ? 1 + Mathf.Pow(1 - kit._player.HpRatio, 2) * 2 : 1) * (kit.Maximize ? 1 + 2 * (1 - kit._player.HpRatio) : 1);
            }

            if (leech > 0)
            {
                float healing = actual * leech * (1 + Bonus(source, "leechScale"));
                if (kit != null)
                    kit.Leech(healing);
                else
                    HealStatic(source, source, healing);
            }

            if (kind == CombatDamageKind.Periodic)
                return;
            float extra = Mathf.Max(Bonus(source, "extra"), Bonus(target, "partyExtra"));
            if (extra > 0)
                CombatSecondaryDamage.Apply(source, target, actual * extra, origin + ".synergy", CombatDamageKind.Extra);
        }

        private void OnDamage(float actual, Transform target, CombatDamageKind kind)
        {
            if (Job.index == 1 && kind == CombatDamageKind.Direct)
                Gain(3);
            if (Job.index == 5)
            {
                var allies = Route == 1 ? Party() : Party().Where(t => t.GetComponent<PartyLife>()?.IsTank == true).Take(1);
                foreach (var ally in allies)
                    Heal(ally, actual * .3f * (Maximize ? 1.5f : 1));
            }

            if (Job.index == 3 && Route == 3 && kind == CombatDamageKind.Periodic)
            {
                var ally = Party().OrderBy(t => Hp(t) / MaxHp(t)).FirstOrDefault();
                Heal(ally, actual * .5f);
            }
        }

        public void OnDamaged(float confirmed, CombatDamageKind kind)
        {
            if (Active && Job.index == 1 && confirmed > 0 && CombatDamageRules.CanReact(kind))
                Gain(5);
        }

        private void Leech(float amount)
        {
            float attempted = amount * HealingScale(transform, transform);
            float actual = Heal(transform, amount);
            if (Overcome && Job.index == 1 && attempted > actual && Hp(transform) >= MaxHp(transform) - .01f)
            {
                string key = Job.Id + ".overheal";
                float present = _player.ShieldFor(CombatMetricsManager.SourceId(transform), key);
                CombatStatusEffects.Shield(transform, transform, key, Mathf.Min(MaxHp(transform) * .3f, present + attempted - actual), 3600);
            }

            if (actual > 0)
                State.Put(new CharacterTimedEffect { id = "leech." + Guid.NewGuid().ToString("N"), kind = "history", end = Now + 5, next = float.MaxValue, value = actual });
        }

        private float Heal(Transform target, float amount) => HealStatic(transform, target, amount);
        private static float HealStatic(Transform source, Transform target, float amount)
        {
            if (!CombatRange.LivingPartyMember(target) || RaidCombatAuthority.Replica)
                return 0;
            var pc = target.GetComponent<PlayerController>();
            return pc != null ? pc.ReceiveHeal(amount, source) : target.GetComponent<PartyBotAI>().ReceiveHeal(amount, source);
        }

        private void HealParty(float fraction, float radius = -1)
        {
            foreach (var ally in Party(radius < 0 ? PartyRadius : radius))
                Heal(ally, MaxHp(ally) * fraction);
        }

        private void Shield(Transform target, float ratio, float seconds, string key) => CombatStatusEffects.Shield(target, transform, Job.Id + "." + key, MaxHp(target) * ratio, seconds);
        private void ShieldParty(float ratio, float seconds, string key, float radius = -1)
        {
            foreach (var ally in Party(radius < 0 ? PartyRadius : radius))
                Shield(ally, ratio, seconds, key);
        }

        private float FacingDot(Transform target)
        {
            var v = transform.position - target.position;
            v.y = 0;
            var f = target.forward;
            f.y = 0;
            return Vector3.Dot(f.normalized, v.normalized);
        }

        private PositionalZone Position(Transform target)
        {
            if (target == null) return PositionalZone.Front;
            var boss = target.GetComponent<BossController>();
            if (boss != null) return boss.GetPositionalZone(transform.position, out _);
            return (PositionalZone)CharacterRosterRules.PositionalZoneFromDot(FacingDot(target));
        }
        private float StaggerPower(Transform target, RosterSkill a)
        {
            float stagger = (Job.index == 8 && a.key == "g1" ? 0 : a.stagger) * (1 + Bonus(target, "staggerTaken"));
            if (Job.index == 8 && Route == 2 || Job.index == 4 && Route == 3)
                stagger *= 1.5f;
            if (Job.index == 2 && Route == 1 || Job.index == 5 && Route == 2 || Job.index == 10 && Route == 3)
                stagger *= 1.5f;
            if (Job.index == 7 && Route == 3)
                stagger *= 1.4f;
            if (Job.index == 9 && Route == 3)
                stagger *= 1.5f;
            return stagger;
        }
        private bool Back(Transform target) => target != null && (Bonus(transform, "allBack") > 0 || Job.index == 6 && Route == 2 || Position(target) == PositionalZone.Rear);
        private bool Flank(Transform target) => target != null && (Bonus(transform, "allBack") > 0 || Job.index == 6 && Route == 2 || Position(target) == PositionalZone.Flank);
        private float Attack(Transform target, RosterSkill a, float multiplier = 1, CombatDamageKind kind = CombatDamageKind.Direct, bool guaranteedCrit = false, bool applyStagger = true)
        {
            if (!CombatRange.LivingEnemy(target) || RaidCombatAuthority.Replica)
                return 0;
            float power = a.power * multiplier;
            if (Job.index == 2 && CharacterRosterRules.IsCannon(a.key))
                power *= 1 + (Effect("cannonDamage")?.value ?? 0);
            ForceCritical = guaranteedCrit || kind == CombatDamageKind.Direct && _attackSequence && _sequenceCritical;
            if (kind == CombatDamageKind.Direct && _attackSequence) power *= 1 + _sequenceDamage;

            if (Job.index == 9)
            {
                float distance = CombatRange.BoundaryDistance(transform, target);
                power *= CharacterRosterRules.RangerNearMultiplier(distance, Overcome, Maximize);
                if (Route == 1)
                    power *= 1 + Mathf.Clamp01(distance / 18) * .45f;
            }

            if (Job.index == 10 && Route == 1 && (a.key == "g2" || a.cast > 0))
                power *= 1.6f;
            if (Job.index == 4 && Route == 3)
                power *= 1.3f;
            if (Job.index == 1 && Maximize)
                power *= 1 + 2 * (1 - _player.HpRatio);
            if (Job.index == 2 && Maximize && Has("overheat") && a.key == "g1")
                power *= 1.8f;
            if (Job.index == 3 && Maximize || Job.index == 10 && Maximize)
                power *= Job.index == 3 ? 1.4f : 1.6f;
            if (Job.index == 6 && Maximize && Position(target) == PositionalZone.Front)
                power *= .5f;
            if (Job.index == 7 && (a.key == "a" || a.key == "r2" || a.key == "z1" || a.key == "v1"))
            {
                if (Route == 1)
                    power *= 1.5f;
                if (Maximize)
                    power *= 1.7f;
            }

            if (Job.index == 8 && Route == 3 && (a.key == "g2" || a.key == "z3"))
                power *= 1.5f;
            if (Job.index == 11)
                power *= 1 + (Effect("dance")?.value ?? 0);
            int damage = Mathf.RoundToInt(power * (_player.combatController?.GetPlayerDamageBuffMultiplier() ?? 1));
            _currentAttack = Job.SkillId(a.key);
            _primaryDamage = 0;
            float before, after;
            var boss = target.GetComponent<BossController>();
            if (boss != null)
            {
                before = boss.currentHp;
                boss.ReceiveRoleSkillAttack(Job.name, a.name, 0, damage, Tint, false, source: transform, origin: Job.SkillId(a.key), kind: kind);
                after = boss.currentHp;
            }
            else
            {
                var add = target.GetComponent<BossAddMinion>();
                before = add.currentHp;
                add.ReceivePlayerAttack(Job.name, a.name, damage, Tint, false, source: transform, origin: Job.SkillId(a.key), kind: kind);
                after = add.currentHp;
            }

            ForceCritical = false;
            float actual = kind == CombatDamageKind.Extra ? Mathf.Max(0, before - after) : _primaryDamage;
            if (actual <= 0)
                return 0;
            if (kind == CombatDamageKind.Direct && _attackSequence && !_sequenceConsumed)
            {
                if (_sequenceCritical) { State.Remove("nextCrit"); State.Remove("nextInstant"); }
                if (_sequenceDamage > 0) State.Remove("nextDamage");
                if (_sequenceChance > 0) State.Remove("nextChance");
                _sequenceConsumed = true;
            }
            if (applyStagger) CombatCounterTarget.Ensure(target).AddStagger(StaggerPower(target, a));
            if (kind == CombatDamageKind.Direct)
            {
                _recordedHits.TryGetValue(Id(target), out float recorded);
                _recordedHits[Id(target)] = recorded + actual;
                _player.PlaySkillLineVisual(target.position, Tint);
                if (!Channeling)
                    _player.FaceAttackTarget(target);
            }

            if (kind == CombatDamageKind.Direct)
            {
                if (Job.index == 11 && Data.clones > 0 && Route == 1 && a.key != "z1")
                    CombatSecondaryDamage.Apply(transform, target, actual * .3f * Data.clones, Job.SkillId(a.key) + ".clone", CombatDamageKind.Extra);
                if (Job.index == 6 && Has("identity"))
                    CombatSecondaryDamage.Apply(transform, target, actual * .3f, Job.SkillId(a.key) + ".shadow", CombatDamageKind.Extra);
                if (Job.index == 2 && Route == 3)
                {
                    var burn = Effect("dot.g1@" + Id(target));
                    Periodic("dot", target, Job.Ability("g1"), 3600, 1, Mathf.Min(350, (burn?.value ?? 0) + 35));
                }

                if (Job.index == 10 && Route == 3)
                    Ground(a, target.position, 3, 3, 40);
            }

            return actual;
        }

        private float AttackArea(Transform target, RosterSkill skill, float multiplier = 1, bool area = false, bool crit = false)
        {
            if (!CombatRange.LivingEnemy(target))
                return 0;
            Vector3 center = skill.shape == "self" ? transform.position : target.position;
            float total = Attack(target, skill, multiplier, guaranteedCrit: crit);
            area = area || skill.shape != "target";
            float radius = skill.radius * (Job.index == 2 && Route == 1 || Job.index == 5 && Route == 2 || Job.index == 11 && Route == 2 ? 1.5f : 1);
            if (area)
                foreach (var enemy in Enemies())
                    if (enemy != target && (skill.shape == "line" ? InLine(transform.position, target.position - transform.position, enemy, skill.range, 2) : skill.shape == "cone" ? AegisExtensionData.InCone(enemy.position.x - transform.position.x, enemy.position.z - transform.position.z, skill.range, transform.eulerAngles.y, skill.angle) : CombatRange.CircleTouches(center, enemy, radius)))
                        total += Attack(enemy, skill, multiplier, guaranteedCrit: crit);
            return total;
        }

        private void Periodic(string kind, Transform target, RosterSkill a, float seconds, float interval, float value)
        {
            State.Put(new CharacterTimedEffect { id = kind + "." + a.key + "@" + Id(target), kind = kind, skill = a.key, target = Id(target), end = Now + seconds, next = Now + interval, interval = interval, value = value });
        }

        private void Ground(RosterSkill a, Vector3 center, float seconds, float radius, float power, float healing = 0)
        {
            State.Put(new CharacterTimedEffect { id = "ground." + a.key, kind = "ground", skill = a.key, end = Now + seconds, next = Now + 1, interval = 1, value = power, aux = healing, x = center.x, y = center.y, z = center.z, radius = radius });
        }

        private void Channel(RosterSkill a, Transform target, float seconds, float interval, float power, bool moving)
        {
            if (CombatRange.LivingEnemy(target))
                _player.FaceAttackTarget(target);
            Self("channel", a.name, seconds, 1, moving ? 1 : 0);
            State.Put(new CharacterTimedEffect { id = "channel." + a.key, kind = "channel", skill = a.key, target = Id(target), end = Now + seconds, next = Now + interval, interval = interval, value = power, x = transform.position.x, y = transform.position.y, z = transform.position.z, yaw = transform.eulerAngles.y, radius = Job.index == 4 ? PartyRadius : a.radius });
        }

        private void TickEffect(CharacterTimedEffect e)
        {
            var a = Job.Ability(e.skill);
            if (a == null)
                return;
            Transform target = Resolve(e.target);
            if (e.kind == "hot")
            {
                Heal(target, MaxHp(target) * e.value);
                return;
            }

            if (e.kind == "dot")
            {
                if (CombatRange.LivingEnemy(target))
                    Attack(target, a, e.value / Mathf.Max(1, a.power), CombatDamageKind.Periodic);
                return;
            }

            Vector3 center = e.kind == "ground" ? new Vector3(e.x, e.y, e.z) : transform.position;
            if (e.kind == "channel" && Job.index == 1 && e.skill == "z3")
            {
                if (!_actor.SpendResource(Job.Resource, 20 * e.interval))
                {
                    CancelChannel();
                    return;
                }
            }

            System.Collections.Generic.IEnumerable<Transform> targets;
            if (e.kind == "channel" && a.shape == "cone")
                targets = Enemies().Where(t => AegisExtensionData.InCone(t.position.x - center.x, t.position.z - center.z, a.range, e.yaw, a.angle));
            else if (e.kind == "channel" && a.shape == "self")
                targets = Enemies().Where(t => CombatRange.CircleTouches(center, t, e.radius));
            else
                targets = e.kind == "channel" ? (CombatRange.LivingEnemy(target) && CombatRange.InRange(transform, target, a.range) ? new[]
                {
                    target
                }

                : Array.Empty<Transform>()) : Enemies().Where(t => CombatRange.CircleTouches(center, t, e.radius));
            bool hitAny = false;
            foreach (var enemy in targets)
                if (e.value > 0)
                {
                    hitAny |= Attack(enemy, a, e.value / Mathf.Max(1, a.power), CombatDamageKind.Periodic) > 0;
                    if (Job.index == 1 && e.skill == "r2")
                        Heal(transform, (_player.maxHp - _player.currentHp) * .05f);
                }

            foreach (var ally in Party())
                if (Flat(center, ally.position) <= e.radius)
                {
                    if (e.aux > 0)
                        Heal(ally, MaxHp(ally) * e.aux);
                    // Ground protection is queried spatially by Incoming; no lingering aura after exit.

                }

            if (Job.index == 4 && e.skill == "r3")
            {
                HealParty(.08f);
            }

            if (Job.index == 5 && e.skill == "r1")
                HealParty(.04f);
            if (Job.index == 2 && e.skill == "z3")
            {
                foreach (var ally in Party())
                    if (Flat(center, ally.position) <= 8)
                        Shield(ally, .03f, 1.1f, e.skill);
            }

            if (Job.index == 2 && e.skill == "z2")
                _actor.RestoreCharge(Job.SkillId("a"));
            if (Job.index == 9 && e.skill == "r3" && hitAny)
                Gain(5);
        }

        private void Taunt(Transform target)
        {
            var boss = target?.GetComponent<BossController>();
            if (boss != null)
            {
                boss.AddCombatThreat(transform, 0, true);
                boss.SetAggroTarget(transform);
            }
            else
                target?.GetComponent<BossAddMinion>()?.TauntBy(transform, Job.name);
        }

        private bool MoveTo(Vector3 destination)
        {
            if (_player == null || _player.IsDead || _player.IsBeingKnockedBack || _player.IsBeingRescued)
                return false;
            Vector3 delta = destination - transform.position;
            delta.y = 0;
            Vector3 end = _collision.Move(_player, delta, delta.magnitude, out bool blocked);
            if (blocked || (end - destination).sqrMagnitude > .05f)
                return false;
            Vector3 before = transform.position;
            transform.position = end;
            if (RaidCombatAuthority.Active)
                RaidNetworkSession.Instance.CommitSkillRelocation(transform);
            _player.targetBoss?.TryInterceptPlayerMovement(_player, before, end);
            return true;
        }

        private bool Approach(Transform target, bool front = false)
        {
            if (target == null)
                return false;
            Vector3 radial = front ? target.forward : (transform.position - target.position).normalized;
            radial.y = 0;
            if (radial.sqrMagnitude < .001f)
                radial = -target.forward;
            return MoveTo(target.position + radial.normalized * (CombatRange.Radius(target) + CombatRange.Radius(transform) + .2f));
        }

        private bool Backstep(float distance) => MoveTo(transform.position - transform.forward * distance);
        public void MovementStarted(Vector3 before)
        {
            if (!Active || RaidCombatAuthority.Replica)
                return;
            CancelChannel();
            if (Job.index == 8)
                Gain(2);
            if (Job.index == 6 && Overcome)
                Self("allBack", "그림자 유영", 4);
            if (Job.index == 5 || Job.index == 11)
                Self("invulnerable", "이동 무적", .25f);
            if (Job.index == 1)
            {
                GrantSuperArmor(transform,.5f);
                Self("dashLanding", "맹수의 도약", 1);
            }

            if (Job.index == 3)
            {
                Data.returnEnd = Now + 3;
                Data.returnX = before.x;
                Data.returnY = before.y;
                Data.returnZ = before.z;
            }

            if (Job.index == 4)
            {
                Self("dashTrail", "글리산도 스텝", 1);
                var trail = Effect("dashTrail");
                trail.x = before.x;
                trail.y = before.y;
                trail.z = before.z;
            }
        }
    }
}
