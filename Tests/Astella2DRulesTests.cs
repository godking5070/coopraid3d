using System;
using System.Collections.Generic;
using Astella.TwoD;

// Pure checks for the 2D kits, encounters, blessings and damage rules (Astella2DKits.cs).
internal static class Astella2DRulesTests
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static void Kits()
    {
        Check(Astella2DKits.All.Count == 5, "one kit for each of the five roles");
        foreach (PixelRole role in Enum.GetValues(typeof(PixelRole)))
        {
            var kit = Astella2DKits.For(role);
            Check(kit.Role == role, role + " kit reports its role");
            Check(kit.MaxHp > 0 && kit.AllyDamage > 0 && kit.AllyInterval > 0 && kit.AllyDistance > 1, role + " kit has positive stats");
            Check(!string.IsNullOrEmpty(kit.RoleName) && !string.IsNullOrEmpty(kit.Summary), role + " kit has display text");
            for (int slot = 0; slot < 4; slot++)
            {
                var skill = kit.Skill(slot);
                Check(!string.IsNullOrEmpty(skill.Name) && !string.IsNullOrEmpty(skill.Description), role + " slot " + slot + " is named");
                bool effect = skill.Damage > 0 || skill.PartyHeal > 0 || skill.SelfHeal > 0 || skill.Revive || skill.Guard > 0 || skill.Empower > 0 || skill.Leap > 0;
                Check(effect, role + " slot " + slot + " does something");
                Check(!skill.NeedsRange || skill.Range > 0, role + " slot " + slot + " has a range when it deals damage");
                Check(skill.Guard < 1 && skill.Guard >= 0, role + " slot " + slot + " guard never makes the target immune");
                Check(skill.Guard == 0 || skill.GuardTime > 0, role + " slot " + slot + " guard has a duration");
                Check(skill.Empower == 0 || skill.EmpowerTime > 0, role + " slot " + slot + " empower has a duration");
            }
            Check(kit.Basic.Damage > 0 && kit.Basic.Cooldown > 0, role + " basic attack deals damage on a cooldown");
            Check(kit.Primary.Cooldown > 0 && kit.Utility.Cooldown > 0, role + " Q and E have cooldowns");
            Check(kit.Ultimate.Cooldown == 0 && kit.Ultimate.Damage > kit.Primary.Damage, role + " ultimate is gated by charge and hits hardest");
            Check(kit.Ranged == (kit.Basic.Range > 5), role + " ranged flag matches basic attack range");
        }
        Check(Astella2DKits.For(PixelRole.Tank).MaxHp > Astella2DKits.For(PixelRole.Caster).MaxHp, "tanks are sturdier than casters");
        Check(Astella2DKits.For(PixelRole.Healer).Utility.Revive, "healer E raises fallen allies");
        Check(Astella2DKits.For(PixelRole.Tank).Utility.GuardParty, "tank E guards the whole party");
        Check(Astella2DKits.For(PixelRole.Ranged).Utility.Leap > 0, "ranged E leaps");
        Check(Astella2DKits.For(PixelRole.Caster).Primary.Delay > 0, "caster Q is a delayed blast");
        bool threw = false;
        try { Astella2DKits.For(PixelRole.Tank).Skill(4); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "slot 4 is the shared dash, not a kit skill");
    }

    private static void Encounters()
    {
        float previousHp = 0;
        for (int i = 0; i < Astella2DEncounters.Count; i++)
        {
            var spec = Astella2DEncounters.Get(i);
            Check(spec.BossHp > previousHp, "boss health rises each encounter");
            previousHp = spec.BossHp;
            Check(spec.HazardDamage > 0 && spec.CastScale > .5f && spec.CastScale <= 1, "encounter " + i + " has sane hazard numbers");
            Check(spec.EnragedInterval < spec.Interval, "encounter " + i + " speeds up when enraged");
            Check(spec.Patterns.Length >= 5 && spec.Patterns[0] == BossPattern.TargetCircle, "encounter " + i + " opens with the targeted circle");
            Check(new HashSet<BossPattern>(spec.Patterns).Count == spec.Patterns.Length, "encounter " + i + " lists each pattern once");
            if (i > 0) Check(spec.Patterns.Length > Astella2DEncounters.Get(i - 1).Patterns.Length, "encounter " + i + " adds new patterns");
            var random = new Random(1234 + i);
            Check(Astella2DEncounters.Next(spec, 0, BossPattern.Cone, random) == BossPattern.TargetCircle, "first cast is always the targeted circle");
            var seen = new HashSet<BossPattern>();
            BossPattern last = BossPattern.TargetCircle;
            for (int cast = 1; cast < 400; cast++)
            {
                var next = Astella2DEncounters.Next(spec, cast, last, random);
                Check(next != last, "a pattern never repeats back to back");
                Check(Array.IndexOf(spec.Patterns, next) >= 0, "only the encounter's own patterns are cast");
                seen.Add(next); last = next;
            }
            Check(seen.Count == spec.Patterns.Length, "encounter " + i + " eventually casts every pattern");
        }
        bool threw = false;
        try { Astella2DEncounters.Get(3); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "only three encounters exist");
    }

    private static void Blessings()
    {
        Check(Astella2DBlessings.All.Count == 8, "eight blessings in the catalog");
        for (int i = 0; i < Astella2DBlessings.All.Count; i++)
        {
            Check(Astella2DBlessings.All[i].Id == i, "blessing ids are their catalog index");
            var mods = new RunModifiers();
            mods.Apply(i);
            bool changed = mods.Damage != 1 || mods.Health != 1 || mods.DashCooldown != 1 || mods.UltimateGain != 1 ||
                mods.DamageTaken != 1 || mods.AllyDamage != 1 || mods.Cooldown != 1 || mods.ExtraRevives != 0;
            Check(changed, "blessing " + i + " changes a run modifier");
        }
        var random = new Random(7);
        for (int draw = 0; draw < 300; draw++)
        {
            var offer = Astella2DBlessings.Draw(random, 3);
            Check(offer.Length == 3 && offer[0] != offer[1] && offer[1] != offer[2] && offer[0] != offer[2], "an offer has three distinct blessings");
        }
        var stack = new RunModifiers();
        stack.Apply(0); stack.Apply(0);
        Check(Math.Abs(stack.Damage - 1.5625f) < .0001f, "repeated damage blessings multiply");
        stack.Apply(5); stack.Apply(5);
        Check(stack.ExtraRevives == 2, "revive blessings add up");
        bool threw = false;
        try { stack.Apply(8); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "unknown blessing ids are rejected");
        Check(Astella2DBlessings.StartingRevives == 2, "two revives per encounter by default");
    }

    private static void Damage()
    {
        Check(Astella2DDamage.Incoming(100, 0, 1) == 100, "no guard takes full damage");
        Check(Math.Abs(Astella2DDamage.Incoming(100, .5f, 1) - 50) < .001f, "half guard halves damage");
        Check(Math.Abs(Astella2DDamage.Incoming(100, .5f, .8f) - 40) < .001f, "guard and armor blessing stack");
        Check(Astella2DDamage.Incoming(100, 5, 1) > 0, "guard is capped below immunity");
        Check(Astella2DDamage.Incoming(100, -1, 1) == 100, "negative guard is ignored");
        Check(Astella2DDamage.Incoming(float.NaN, 0, 1) == 0 && Astella2DDamage.Incoming(-5, 0, 1) == 0, "invalid damage is ignored");
        Check(Astella2DDamage.Incoming(100, float.NaN, float.NaN) == 100, "invalid modifiers fall back to full damage");
    }

    private static int Main()
    {
        try
        {
            Kits();
            Encounters();
            Blessings();
            Damage();
            Console.WriteLine("PASS: Astella 2D rules (" + checks + " checks). Kits, encounters, blessings and damage only.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL after " + checks + " checks: " + error.Message);
            return 1;
        }
    }
}
