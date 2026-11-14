using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class CharacterKit
    {
        private void GrantSuperArmor(Transform target,float seconds)
        {
            var status=CombatStatusEffects.Ensure(target.gameObject);
            status.GrantControlImmunity(seconds,transform);status.GrantKnockbackImmunity(seconds,transform);
        }
        private void AddNote(int note)
        {
            float before = Resource;
            Gain(1);
            int count = Mathf.RoundToInt(Resource - before);
            for (int i = 0; i < count; i++)
                Data.notes = (Data.notes << 2 | note) & 63;
        }

        public float CooldownScale(CharacterSkillDefinition s)
        {
            string key = CharacterRosterData.Key(s.id);
            return Job.index == 7 && Route == 1 && (key == "a" || key == "r2" || key == "z1" || key == "v1") ? .7f : 1;
        }

        public float CriticalBonus(Transform target)
        {
            if (!Maximize)
                return 0;
            if (Job.index == 6 && Back(target))
                return .6f;
            if (Job.index == 8 && (_actor.Selected(CharacterSlot.Gcd2)?.id == _currentAttack))
                return .75f;
            if (Job.index == 9 && _player.HpRatio >= .8f && CombatRange.BoundaryDistance(transform, target) >= 12)
                return .5f;
            return 0;
        }

        private string _currentAttack;
        public void PlanMovement(DashSettings settings, ref float x, ref float z)
        {
            if (Job.index != 6)
                return;
            var target = RaidCombatAuthority.Active ? GetComponent<NetworkRaidAvatar>()?.CombatTarget : TargetingManager.Instance?.CurrentTarget;
            if (!CombatRange.LivingEnemy(target) || !CombatRange.InRange(transform, target, 6))
                return;
            var radial = transform.position - target.position;
            radial.y = 0;
            if (radial.sqrMagnitude < .01f)
                radial = -target.forward;
            var end = target.position - radial.normalized * (CombatRange.Radius(target) + CombatRange.Radius(transform) + .2f);
            var delta = end - transform.position;
            delta.y = 0;
            settings.distance = delta.magnitude;
            x = delta.normalized.x;
            z = delta.normalized.z;
        }

        private void UpdateDashTrail()
        {
            var trail = Effect("dashTrail");
            if (trail != null)
            {
                var start = new Vector3(trail.x, trail.y, trail.z);
                var delta = transform.position - start;
                foreach (var ally in Party())
                    if (Flat(ally.position, transform.position) <= 2 || delta.sqrMagnitude > .001f && InLine(start, delta, ally, delta.magnitude, 3))
                        Status("speed", "글리산도 스텝", ally, 4, .2f);
                trail.x = transform.position.x;
                trail.y = transform.position.y;
                trail.z = transform.position.z;
                if (!_player.IsDashing)
                    State.Remove("dashTrail");
            }

            if (Has("dashLanding") && !_player.IsDashing)
            {
                foreach (var enemy in Enemies())
                    if (CombatRange.InRange(transform, enemy, 3))
                        Attack(enemy, Job.Ability("m"));
                State.Remove("dashLanding");
            }
        }

        private void UpdateSatellites()
        {
            var effect = Effect("satellites");
            if (effect == null || Has("satelliteDelay"))
                return;
            var target = Enemies().FirstOrDefault(t => CombatRange.InRange(transform, t, 5));
            if (target == null)
                return;
            Attack(target, Job.Ability("f2"));
            effect.value--;
            Self("satelliteDelay", "위성 궤도", .5f);
            if (effect.value <= 0)
                State.Remove("satellites");
        }

        private static bool InLine(Vector3 origin, Vector3 direction, Transform target, float range, float width)
        {
            direction.y = 0;
            direction.Normalize();
            var offset = target.position - origin;
            offset.y = 0;
            float forward = Vector3.Dot(offset, direction);
            return forward >= -CombatRange.Radius(target) && forward <= range + CombatRange.Radius(target) && (offset - direction * forward).magnitude <= width * .5f + CombatRange.Radius(target);
        }

        public static bool WallBlocks(Transform target, Transform attacker)
        {
            if (target == null || attacker == null)
                return false;
            foreach (var kit in FindObjectsByType<CharacterKit>(FindObjectsSortMode.None))
            {
                var wall = kit.Active ? kit.Effect("wall") : null;
                if (wall == null)
                    continue;
                var point = new Vector3(wall.x, 0, wall.z);
                var forward = Quaternion.Euler(0, wall.yaw, 0) * Vector3.forward;
                var a = attacker.position - point;
                var b = target.position - point;
                a.y = b.y = 0;
                float da = Vector3.Dot(a, forward), db = Vector3.Dot(b, forward);
                if (da <= 0 || db > 0)
                    continue;
                var crossing = a + (b - a) * (da / (da - db));
                if (CharacterRosterRules.WallCrosses(da, db, crossing.magnitude, 3))
                    return true;
            }

            return false;
        }

        public static float AuraReduction(Transform target)
        {
            float result = 0;
            foreach (var kit in FindObjectsByType<CharacterKit>(FindObjectsSortMode.None))
                if (kit.Active && CombatRange.LivingPartyMember(kit.transform))
                    foreach (var e in kit.Data.effects)
                    {
                        if (e.end <= kit.Now)
                            continue;
                        float value = 0;
                        if (e.kind == "ground" && kit.Job.index == 3 && e.skill == "r2")
                            value = .15f;
                        if (e.kind == "ground" && kit.Job.index == 2 && e.skill == "z3")
                            value = .2f;
                        if (e.kind == "channel" && kit.Job.index == 4 && e.skill == "r3")
                            value = .5f;
                        Vector3 center = e.kind == "channel" ? kit.transform.position : new Vector3(e.x, e.y, e.z);
                        if (value > 0 && Flat(center, target.position) <= e.radius)
                            result = Mathf.Max(result, value);
                    }

            return result;
        }

        public static void AfterHeal(Transform source, Transform target, float actual, float attempted, float before, HealingKind kind)
        {
            if (RaidCombatAuthority.Replica || !CombatRange.LivingPartyMember(target))
                return;
            var receiver = target.GetComponent<CharacterKit>();
            if (kind == HealingKind.Potion && receiver?.Active == true && receiver.Job.index == 9 && receiver.Overcome)
                receiver.Self("speed", "신록의 대사 촉진", 4, .2f);
            var kit = source?.GetComponent<CharacterKit>();
            if (kit?.Active != true)
                return;
            if (kit.Job.index == 3)
            {
                if (kit.Route == 1 && before >= MaxHp(target) - .01f && attempted > 0)
                    kit.Status("maxHp", "세계수의 생명력", target, 8, .1f);
                if (kit.Overcome && actual > 0)
                    CombatStatusEffects.Shield(source, source, kit.Job.Id + ".bark", actual * .2f, 8);
            }
        }

        public static void OnShieldExpired(int source, Transform recipient, float amount)
        {
            if (amount <= 0 || !CombatRange.LivingPartyMember(recipient))
                return;
            var caster = PartyLife.OrderedMembers().FirstOrDefault(t => CombatMetricsManager.SourceId(t) == source);
            var kit = caster?.GetComponent<CharacterKit>();
            if (kit?.Active == true && kit.Job.index == 4 && kit.Overcome)
                HealStatic(caster, recipient, amount);
        }

        public static bool ShieldControlImmune(Transform recipient)
        {
            foreach (var member in PartyLife.OrderedMembers())
            {
                var kit = member.GetComponent<CharacterKit>();
                if (kit?.Active != true || kit.Job.index != 4 || kit.Route != 2)
                    continue;
                int source = CombatMetricsManager.SourceId(member);
                if ((recipient.GetComponent<PlayerController>()?.ShieldsFrom(source) ?? recipient.GetComponent<PartyBotAI>()?.ShieldsFrom(source) ?? 0) > 0)
                    return true;
            }

            return false;
        }
    }

    // The same party buffs apply to legacy test actors and new jobs. Never multiply yesterday's final stat.
    [DefaultExecutionOrder(480)]
    public sealed class CharacterPartyStats : MonoBehaviour
    {
        private float _lastHp = 1, _lastSpeed = 1;
        public static void Ensure(GameObject go)
        {
            if (go.GetComponent<CharacterPartyStats>() == null)
                go.AddComponent<CharacterPartyStats>();
        }

        private void LateUpdate()
        {
            if (RaidCombatAuthority.Replica)
                return;
            var kit = GetComponent<CharacterKit>();
            if (kit?.Active == true)
                return;
            var tank=GetComponent<NineKeyTankKit>();
            if(tank?.Active==true){tank.Recalculate();return;}
            var paladin = GetComponent<AegisPaladin>();
            if (paladin?.IsActive == true)
            {
                paladin.RecalculateStats();
                return;
            }

            float hpScale = 1 + CharacterKit.Bonus(transform, "maxHp"), speed = 1 + CharacterKit.Bonus(transform, "speed");
            var pc = GetComponent<PlayerController>();
            var bot = GetComponent<PartyBotAI>();
            if (pc != null)
            {
                float basis = pc.maxHp / _lastHp;
                float ratio = pc.HpRatio;
                pc.maxHp = basis * hpScale;
                pc.currentHp = pc.maxHp * ratio;
                pc.moveSpeed = pc.moveSpeed / _lastSpeed * speed;
            }
            else if (bot != null)
            {
                float ratio = bot.maxHp > 0 ? bot.currentHp / bot.maxHp : 0;
                bot.maxHp = bot.maxHp / _lastHp * hpScale;
                bot.currentHp = bot.maxHp * ratio;
                bot.moveSpeed = bot.moveSpeed / _lastSpeed * speed;
            }

            _lastHp = hpScale;
            _lastSpeed = speed;
        }

        public void ResetHealthFactor() => _lastHp = 1;
        public void ResetFactors()
        {
            _lastHp = _lastSpeed = 1;
        }
    }
}
