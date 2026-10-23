using System;
using RaidRogue;

internal static class PartyBattleTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
    private static void Equal(double expected, double actual, string message) =>
        Check(!double.IsNaN(actual) && Math.Abs(expected - actual) < 0.0001, message + $": {expected} != {actual}");
    private static CombatMemberRecord Member(CombatEncounterResult result, int id)
    {
        foreach (var member in result.Members) if (member.Id == id) return member;
        throw new Exception("Missing member " + id);
    }

    private static void LifeAndResurrection()
    {
        var life = new PartyLifeState();
        var ledger = new CombatMetricsLedger();
        ledger.Register(1, "Tank", 1); ledger.Register(2, "Healer", 2); ledger.BeginStage(2);
        var skill = new ResurrectionState();
        var settings = new ResurrectionSettings();
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            Check(life.Die(), "alive -> dead once"); ledger.SetDead(1, true);
            Check(!life.Die(), "repeated lethal event is idempotent"); ledger.SetDead(1, true);
            Equal(attempt, Member(ledger.Snapshot(), 1).Deaths, "only one death per life");
            Check(!skill.Begin(settings, false), "invalid target/range/caster rejected");
            Equal(0, skill.Cooldown, "invalid input has no cooldown");
            Check(skill.Begin(settings, true), "valid corpse starts cast");
            Check(!skill.Begin(settings, true), "second key press cannot restart cast");
            Check(!skill.Advance(1, true), "cast not complete before 3 seconds");
            Check(!skill.Advance(0, true), "pause does not advance cast");
            Equal(2, skill.Remaining, "remaining after pause");
            Check(!skill.Advance(1, false), "movement/death/range/target invalidation cancels");
            Check(!skill.Casting, "cancel clears casting state");
            Equal(0, skill.Cooldown, "cancellation costs no cooldown");
            Check(skill.Begin(settings, true), "cancelled cast can be retried");
            Check(skill.Advance(3, true), "cast ready at 3 seconds");
            bool revived = life.Revive(); skill.Complete(revived); ledger.SetDead(1, false);
            Check(revived && !life.Dead, "revive restores alive state");
            Check(!life.Revive(), "already revived target cannot revive twice");
            Equal(45, skill.Cooldown, "success starts 45 second cooldown");
            Equal(0, Member(ledger.Snapshot(), 2).Healing, "restoration never calls healing ledger");
            skill.Cancel(); Equal(45, skill.Cooldown, "death/cancel does not reset spent cooldown");
            skill.Tick(44.5); Check(!skill.Begin(settings, true), "cooldown still blocks cast");
            skill.Tick(0.5); Equal(0, skill.Cooldown, "cooldown expires");
        }
        Check(skill.Begin(settings, true), "second healer starts valid cast");
        Check(skill.Advance(3, true), "second healer finishes cast");
        skill.Complete(false); Equal(0, skill.Cooldown, "already revived before commit costs no cooldown");
        Check(!skill.Casting, "failed commit ends cast");
        skill.Begin(settings, true); skill.Reset();
        Check(!skill.Casting && skill.Cooldown == 0, "retry resets resurrection");
        life.Die(); life.Reset(); Check(!life.Dead, "retry restores life state");

        foreach (float invalid in new[] { -1f, 0f, float.NaN, float.PositiveInfinity })
        {
            Check(!new ResurrectionState().Begin(new ResurrectionSettings { castTime = invalid }, true), "invalid cast configuration");
            Check(!new ResurrectionState().Begin(new ResurrectionSettings { range = invalid }, true), "invalid range configuration");
            Check(!new ResurrectionState().Begin(new ResurrectionSettings { healthFraction = invalid }, true), "invalid HP configuration");
        }
        Check(!new ResurrectionState().Begin(new ResurrectionSettings { healthFraction = 1.1f }, true), "HP fraction over one rejected");
        var custom = new ResurrectionState();
        Check(custom.Begin(new ResurrectionSettings { castTime = 2, range = 30, cooldown = 10, healthFraction = 0.6f }, true), "custom skill configuration");
        custom.Advance(2, true); custom.Complete(true); Equal(10, custom.Cooldown, "custom cooldown used");
    }

    private static void OutcomesAndFailedAttempts()
    {
        for (int living = 0; living <= 4; living++)
        {
            Check(PartyBattleRules.Resolve(true, living, 4) == EncounterOutcome.Cleared, "boss kill wins even on simultaneous wipe");
            Check(PartyBattleRules.Resolve(false, living, 4) == (living == 0 ? EncounterOutcome.Wiped : EncounterOutcome.None), "four dead required");
        }
        Check(PartyBattleRules.Resolve(false, 0, 0) == EncounterOutcome.None, "uninitialized roster is not a wipe");
        var ledger = new CombatMetricsLedger();
        ledger.Register(1, "Tank", 1); ledger.Register(2, "Healer", 2);
        ledger.BeginStage(1); ledger.Advance(10, true); ledger.RecordDamage(1, 1000, 800, false);
        ledger.RecordHealing(2, 300, 400); ledger.EndStage(); ledger.EndStage();
        Equal(1, ledger.Stages.Count, "clear commits once");
        for (int failure = 1; failure <= 3; failure++)
        {
            ledger.BeginStage(2); int generation = ledger.Generation;
            ledger.Advance(20, true); ledger.Advance(100, false);
            ledger.RecordDamage(1, 1000, 400, false); ledger.RecordDamage(1, 100, 0, true);
            ledger.RecordHealing(2, 100, 500); ledger.RecordHealing(1, 200, 700, HealingKind.Potion);
            ledger.SetDead(1, true); ledger.SetDead(1, true);
            ledger.FailAttempt(); ledger.FailAttempt(); ledger.EndStage();
            Equal(failure, ledger.FailedAttempts.Count, "failure commits once");
            Check(ledger.Generation != generation, "failed encounter invalidates delayed effects");
            Equal(1, ledger.Stages.Count, "failure cannot become a clear");
            var failed = ledger.FailedAttempts[failure - 1];
            Equal(20, failed.Seconds, "failed combat time freezes");
            Equal(700, Member(failed, 1).Damage, "failed damage retained");
            Equal(500, Member(failed, 1).SelfHealing, "failed potion record retained");
            Equal(0, Member(failed, 1).Hps(failed.Seconds), "potion excluded from HPS");
            Equal(1, Member(failed, 1).Deaths, "failure deaths not duplicated");
            ledger.RecordDamage(1, 1000, 0, false); ledger.Advance(60, true);
            Equal(700, Member(failed, 1).Damage, "frozen failure is immutable");
            ledger.RegisterRetry(); Equal(failure, ledger.RetryCount, "retry count persists across attempts");
            Equal(200, Member(ledger.RunTotal(), 1).Damage, "failed damage excluded from run");
            Equal(10, ledger.RunTotal().Seconds, "failed time excluded from run");
        }
        ledger.BeginStage(2); Equal(0, Member(ledger.Snapshot(), 1).Deaths, "fresh attempt has no previous deaths");
        ledger.Advance(30, true); ledger.RecordDamage(1, 1000, 100, false); ledger.RecordHealing(2, 100, 400); ledger.EndStage();
        var run = ledger.RunTotal(); Equal(40, run.Seconds, "only clear durations summed");
        Equal(27.5, Member(run, 1).Dps(run.Seconds), "run DPS is total damage / total clear time");
        Equal(10, Member(run, 2).Hps(run.Seconds), "run HPS is total healing / total clear time");
        ledger.ResetRun(); Equal(0, ledger.RetryCount, "new run resets retries");
        Equal(0, ledger.FailedAttempts.Count, "new run clears failure history"); Equal(0, ledger.Stages.Count, "new run clears clear history");
    }

    private static void ConfigurablePotionAndRevival()
    {
        foreach (float fraction in new[] { 0.25f, 0.5f, 1f })
        {
            var potion = new PotionState(); float hp = 400;
            potion.GrantRevivalProtection(3);
            var settings = new PotionSettings { healFraction = fraction, cooldown = 30 };
            float amount = potion.TryUse(settings, hp, 1000, true, false, value => { float before = hp; hp = Math.Min(1000, hp + value); return hp - before; });
            Equal(Math.Min(600, 1000 * fraction), amount, "configured fraction capped at missing HP");
            Equal(0, potion.RevivalProtectionRemaining, "successful potion ends protection");
            potion.EndRevivalProtection(); potion.Tick(2); potion.GrantRevivalProtection(3);
            Equal(28, potion.CooldownRemaining, "death/revive does not reset item cooldown");
            potion.ResetForBossRetry(); Equal(0, potion.CooldownRemaining, "retry resets potion cooldown");
            Equal(0, potion.RevivalProtectionRemaining, "retry has no leftover protection");
        }
    }

    private static void Main()
    {
        LifeAndResurrection(); OutcomesAndFailedAttempts(); ConfigurablePotionAndRevival();
        Console.WriteLine($"PASS: {_checks} party death, resurrection, retry outcome and failed-attempt accounting checks.");
    }
}
