using System;
using System.Collections.Generic;
using RaidRogue;

internal static class RaidFlowTests
{
    private static int checks;
    private static void Check(bool pass, string reason) { checks++; if (!pass) throw new Exception(reason); }
    private static void Main()
    {
        CompleteRun(); RetryAndStaleCallbacks(); CloseAtEveryPhase(); Admission(); RewardAndRecoveryBoundaries();
        Console.WriteLine("PASS: " + checks + " full-run, retry, duplicate outcome, stale callback, shutdown, admission timeout and reward/recovery boundary checks (no Unity runtime).");
    }
    private static void CompleteRun()
    {
        var flow = new RaidRunFlow();
        Check(flow.Phase == RaidBattlePhase.Lobby && flow.Stage == 0, "Menu has no active encounter");
        Check(!flow.TryStart(0) && !flow.TryPrepare(2, 3, false) && !flow.TryPrepare(0, 3, false), "Cannot skip preparation/first stage");
        for (int stage = 1; stage <= 3; stage++)
        {
            Check(flow.TryPrepare(stage, 3, false), "Prepare next legitimate stage");
            int revision = flow.Revision;
            Check(!flow.TryPrepare(stage, 3, false) && !flow.TryStart(revision), "Duplicate preparation and early start denied");
            Check(flow.Resolve(revision, true, 0, 4) == EncounterOutcome.None, "No result while loading");
            Check(!flow.TryCountdown(revision), "Loading cannot skip the walkable approach");
            Check(flow.TryApproach(revision) && !flow.TryStart(revision) && !flow.TryCountdown(revision), "Approach cannot skip the human entry and arrival");
            Check(!flow.TryApproach(revision) && flow.TryArrival(revision) && !flow.TryArrival(revision), "Arrival begins once per attempt");
            Check(flow.TryCountdown(revision) && !flow.TryCountdown(revision), "Arrival countdown releases exactly once");
            Check(flow.TryStart(revision) && !flow.TryStart(revision), "Countdown starts exactly one metrics attempt");
            Check(flow.Resolve(revision, false, 1, 4) == EncounterOutcome.None, "Survivor keeps attempt alive");
            Check(flow.Resolve(revision, true, 0, 4) == EncounterOutcome.Cleared, "Simultaneous kill and wipe awards clear");
            for (int n = 0; n < 20; n++) Check(flow.Resolve(revision, n % 2 == 0, 0, 4) == EncounterOutcome.None, "Duplicate outcome never creates another result");
            Check(!flow.TryPrepare(stage + 1, 3, false), "Clear presentation cannot skip reward");
            Check(flow.FinishClear(revision, 3) && !flow.FinishClear(revision, 3), "Clear presentation completes once");
            Check(flow.Phase == (stage == 3 ? RaidBattlePhase.Victory : RaidBattlePhase.Reward), "Only intermediate stages receive cards");
        }
        Check(!flow.TryPrepare(4, 3, false) && !flow.TryPrepare(3, 3, true), "No boss/reward after final clear");
        int completedRevision = flow.Revision;
        Check(flow.ReturnToLobby() && !flow.ReturnToLobby() && flow.Stage == 0 && flow.Phase == RaidBattlePhase.Lobby,
            "A completed party may return to the waiting room exactly once");
        Check(!flow.TryStart(completedRevision) && flow.TryPrepare(1, 3, false),
            "Old victory callbacks cannot enter the next run; a fresh Stage 1 can prepare");
    }
    private static void RetryAndStaleCallbacks()
    {
        var flow = new RaidRunFlow(); flow.TryPrepare(1, 3, false); flow.TryApproach(flow.Revision); flow.TryArrival(flow.Revision); flow.TryCountdown(flow.Revision); flow.TryStart(flow.Revision);
        int old = flow.Revision;
        Check(flow.Resolve(old, false, 0, 4) == EncounterOutcome.Wiped, "Four dead wipes once");
        Check(!flow.TryPrepare(2, 3, true) && !flow.TryPrepare(2, 3, false), "Retry cannot skip stage");
        Check(flow.TryPrepare(1, 3, true), "Retry same stage");
        for (int i = 0; i < 100; i++)
            Check(!flow.TryArrival(old) && !flow.TryCountdown(old) && !flow.TryStart(old) && !flow.FinishClear(old, 3) && flow.Resolve(old, true, 0, 4) == EncounterOutcome.None,
                "Old attempt countdown/clear/outcome cannot mutate new preparation");
        Check(flow.TryApproach(flow.Revision) && flow.TryArrival(flow.Revision) && flow.TryCountdown(flow.Revision) && flow.TryStart(flow.Revision), "Current attempt still starts");
        Check(flow.Resolve(flow.Revision, true, 3, 4) == EncounterOutcome.Cleared && flow.FinishClear(flow.Revision, 3), "Retry can clear normally");
    }
    private static void CloseAtEveryPhase()
    {
        for (int phase = 0; phase < 9; phase++)
        {
            var flow = new RaidRunFlow();
            if (phase > 0) flow.TryPrepare(1, phase == 8 ? 1 : 3, false);
            if (phase > 1) flow.TryApproach(flow.Revision);
            if (phase > 2) flow.TryArrival(flow.Revision);
            if (phase > 3) flow.TryCountdown(flow.Revision);
            if (phase > 4) flow.TryStart(flow.Revision);
            if (phase > 5) flow.Resolve(flow.Revision, phase != 6, 0, 4);
            if (phase > 7) flow.FinishClear(flow.Revision, phase == 8 ? 1 : 3);
            int revision = flow.Revision;
            Check(flow.End() && !flow.End(), "Teardown is idempotent from every live phase");
            for (int n = 0; n < 50; n++)
                Check(!flow.TryPrepare(1, 3, false) && !flow.TryPrepare(1, 3, true) && !flow.TryArrival(revision) && !flow.TryCountdown(revision) &&
                    !flow.TryStart(revision) && !flow.FinishClear(revision, 3) && flow.Resolve(revision, true, 0, 4) == EncounterOutcome.None &&
                    flow.Phase == RaidBattlePhase.Closed, "Delayed UI/network callbacks cannot reopen a closed session");
            var next = new RaidRunFlow(); Check(next.TryPrepare(1, 3, false) && next.Revision == 1, "New session has independent state");
        }
    }
    private static void Admission()
    {
        var wait = new RaidAdmissionWait(); const ulong id = 76561198012345678;
        Check(wait.Waiting(id, false, 10, 30), "Lobby-only participant gets connection grace period");
        Check(wait.Waiting(id, false, 39.99f, 30) && !wait.Waiting(id, false, 40, 30), "Unconnected member cannot block forever");
        Check(!wait.Waiting(id, false, 400, 30), "Repeated checks do not renew grace");
        Check(!wait.Waiting(id, true, 401, 30), "Admitted member does not block admission wait");
        wait.Retain(new List<ulong>());
        Check(wait.Waiting(id, false, 500, 30), "A genuinely new lobby membership gets its own grace");
    }
    private static void RewardAndRecoveryBoundaries()
    {
        // Existing reward gate is shared by direct input, 15-second disconnected selection and
        // the new full-room deadline. Either ordering must grant exactly one card.
        for (int order = 0; order < 2; order++)
        {
            var flow = new RaidRunFlow(); flow.TryPrepare(1, 3, false); flow.TryApproach(1); flow.TryArrival(1); flow.TryCountdown(1); flow.TryStart(1);
            flow.Resolve(1, true, 4, 4); flow.FinishClear(1, 3);
            var rewards = new RaidRewardRound(); rewards.Begin(1); rewards.Offer(1, new[] { 0, 1, 2 }); rewards.Offer(2, new[] { 2, 3, 4 });
            int grants = 0;
            if (rewards.Choose(1, 1, order == 0 ? 1 : 0)) grants++;
            if (rewards.Choose(1, 1, order == 0 ? 0 : 1)) grants++;
            Check(grants == 1 && !rewards.Complete, "Manual/timeout race consumes exactly once and still waits for peers");
            rewards.Remove(2);
            Check(rewards.Complete && flow.TryPrepare(2, 3, false), "Explicit departure cannot block rewards");
            Check(!flow.TryPrepare(2, 3, false) && !rewards.Choose(2, 1, 2), "Duplicate departure/old offer cannot advance twice");
        }
        var barrier = new RaidRestoreBarrier(); barrier.SentFrame(1, 4, 1, (int)RaidBattlePhase.Reward);
        Check(!barrier.Accept(1, 4, 2, (int)RaidBattlePhase.Loading), "Return acknowledgement cannot grant from a previous stage");
        barrier.SentFrame(2, 4, 2, (int)RaidBattlePhase.Countdown);
        Check(!barrier.Accept(2, 4, 2, (int)RaidBattlePhase.Combat), "Countdown acknowledgement is stale after combat starts");
        barrier.SentFrame(3, 4, 2, (int)RaidBattlePhase.Combat);
        Check(barrier.Accept(3, 4, 2, (int)RaidBattlePhase.Combat), "Fresh current-state acknowledgement grants control");
    }
}
