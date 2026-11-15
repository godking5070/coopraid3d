using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RaidRogue;

internal static class Part1SaveTests
{
    public static void Run(Action<bool, string> check)
    {
        check(CampaignCatalog.Available(CampaignCatalog.PartOne) && !CampaignCatalog.Available(CampaignCatalog.PartTwo), "only part1 can depart");
        check(CampaignCatalog.Stages(CampaignCatalog.PartOne).SequenceEqual(Enumerable.Range(1, 6)), "six stable chapter stages");
        check(CampaignCatalog.Stages(CampaignCatalog.PartOne).Select(CampaignCatalog.EncounterTemplate).SequenceEqual(new[] { 1, 2, 3, 1, 2, 3 }), "reuse authored encounters without new patterns");
        check(CampaignCatalog.EncounterTemplate(7) == 0 && CampaignCatalog.PartySize(CampaignCatalog.PartOne) == 4, "no stage7 or eight player part1");

        var budget = new IrisRescueState(); budget.BeginAttempt(0, 0);
        var life = new PartyLifeState(); life.Die(); string attempt = budget.AttemptId;
        Func<string> key = () => "slot-a/" + life.DeathGeneration;
        Func<bool> valid = () => life.Dead;
        check(!budget.TryRescue("old-attempt", key(), valid, life.Revive) && budget.Coins == 4 && life.Dead, "stale attempt cannot spend or revive");
        check(!budget.TryRescue(attempt, key(), valid, () => false) && budget.Coins == 4, "failed revive rolls reservation back");
        try { budget.TryRescue(attempt, key(), valid, () => throw new InvalidOperationException()); }
        catch (InvalidOperationException) { }
        check(budget.Coins == 4 && budget.Pending == 0, "exception releases reservation");
        string firstDeath = key();
        check(budget.TryRescue(attempt, firstDeath, valid, () => {
            check(budget.Pending == 1 && budget.Coins == 3, "reservation visible before revival callback");
            check(!budget.TryRescue(attempt, firstDeath, valid, life.Revive), "reentrant target request blocked");
            return life.Revive();
        }), "host commits one resurrection");
        check(!budget.TryRescue(attempt, firstDeath, () => true, () => true) && budget.TotalUsed == 1, "duplicate request commits once");
        life.Die(); check(key() != firstDeath, "later death has another identity");
        check(!budget.TryRescue(attempt, firstDeath, valid, life.Revive) && life.Dead, "old death request cannot affect new death");
        int remaining = budget.Coins;
        // Recovery leases are control changes on the same live actor, not encounter preparation.
        var slots = new RaidRecoverySlots(new RaidRecoverySettings()); var slot = slots.Register(17, 1);
        check(slots.Reserve(1, 0) && slots.BeginRestore(17, 1, out _) == null && slots.Complete(17, slot.Lease, 2), "control recovery handshake");
        check(budget.Coins == remaining && budget.AttemptId == attempt && life.Dead, "reconnect and AI transfer preserve live allowance/death");
        check(budget.TryRescue(attempt, key(), valid, life.Revive), "new death can consume next shared coin");
        check(budget.TryRescue(attempt, "bot/1", () => true, () => true), "bot spends same pool");
        int successes = 0, callbacks = 0;
        Parallel.For(0, 32, n => {
            if (budget.TryRescue(attempt, "last/" + n, () => true, () => { Interlocked.Increment(ref callbacks); return true; })) Interlocked.Increment(ref successes);
        });
        check(successes == 1 && callbacks == 1 && budget.Coins == 0 && budget.TotalUsed == 4, "32 simultaneous last-coin requests: one success");
        budget.BeginAttempt();
        check(budget.Coins == 4 && budget.TotalUsed == 4 && budget.AttemptId != attempt && budget.AttemptsStarted == 2, "wipe retry is new attempt and retains run totals");
        check(!budget.TryRescue(attempt, "late", () => true, () => true), "retry rejects late old-attempt packets");
        var fromDisk = new IrisRescueState(); fromDisk.BeginAttempt(budget.TotalUsed, budget.AttemptsStarted);
        check(fromDisk.Coins == 4 && fromDisk.TotalUsed == 4 && fromDisk.AttemptId != budget.AttemptId && fromDisk.AttemptsStarted == 3, "disk continue starts fresh attempt with saved cumulative totals");

        for (int start = 1; start <= 6; start++)
        {
            var flow = new RaidRunFlow();
            check(flow.TryResume(start, 6), "resume each stage boundary");
            for (int stage = start; stage <= 6; stage++)
            {
                int rev = flow.Revision;
                check(flow.TryApproach(rev) && flow.TryArrival(rev) && flow.TryCountdown(rev) && flow.TryStart(rev), "boundary goes through arrival and countdown");
                check(flow.Resolve(rev, false, 0, 4) == EncounterOutcome.Wiped, "existing wipe policy");
                check(flow.TryPrepare(stage, 6, true), "same-stage retry"); rev = flow.Revision;
                check(flow.TryApproach(rev) && flow.TryArrival(rev) && flow.TryCountdown(rev) && flow.TryStart(rev), "retry arrival and countdown");
                check(flow.Resolve(rev, true, 0, 4) == EncounterOutcome.Cleared, "simultaneous boss death and wipe clears");
                check(flow.Resolve(rev, true, 4, 4) == EncounterOutcome.None, "duplicate clear ignored");
                check(flow.FinishClear(rev, 6), "confirmed clear completes presentation");
                if (stage < 6) check(flow.Phase == RaidBattlePhase.Reward && flow.TryPrepare(stage + 1, 6, false), "stages1-5 reward then next boundary");
                else check(flow.Phase == RaidBattlePhase.Victory && !flow.TryPrepare(7, 6, false) && !flow.FinishClear(rev, 6), "stage6 terminal, no forced cards or stage7");
            }
        }
    }
}
