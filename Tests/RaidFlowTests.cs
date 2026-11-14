using System;
using System.Collections.Generic;
using RaidRogue;

internal static class RaidFlowTests
{
    private static int checks;
    private static void Check(bool pass, string reason) { checks++; if (!pass) throw new Exception(reason); }
    private static void Main()
    {
        BossHealth();
        CompleteRun(); FinalSkillRewards(); RetryAndStaleCallbacks(); CloseAtEveryPhase(); Admission(); RewardAndRecoveryBoundaries();
        Console.WriteLine("PASS: " + checks + " full-run, retry, duplicate outcome, stale callback, shutdown, admission timeout and reward/recovery boundary checks (no Unity runtime).");
    }
    private static void BossHealth()
    {
        float[] normalHealth={30000,36000,42000,46000,52000,60000};
        float[] hardHealth={40000,36000,42000,46000,52000,60000};
        float[] expectedHardHealth={80000,72000,84000,92000,104000,120000};
        for(int i=0;i<hardHealth.Length;i++)
        {
            Check(RaidBossHealthTuning.Resolve(normalHealth[i],false)==normalHealth[i],"normal health unchanged stage "+(i+1));
            Check(RaidBossHealthTuning.Resolve(hardHealth[i],true)==expectedHardHealth[i],"hard health doubled stage "+(i+1));
        }
    }
    private static void CompleteRun()
    {
        const int maximum = 6;
        var flow = new RaidRunFlow();
        Check(flow.Phase == RaidBattlePhase.Lobby && flow.Stage == 0, "Menu has no active encounter");
        Check(RaidLobbyAdmission.AllowsNewParticipant(flow.Phase, flow.Stage, false), "Fresh lobby admits new participants");
        Check(!RaidLobbyAdmission.AllowsNewParticipant(flow.Phase, flow.Stage, true), "File continuation reserves original party even before its stage header is restored");
        Check(!flow.TryStart(0) && !flow.TryPrepare(2, maximum, false) && !flow.TryPrepare(0, maximum, false), "Cannot skip preparation/first stage");
        for (int stage = 1; stage <= maximum; stage++)
        {
            Check(flow.TryPrepare(stage, maximum, false), "Prepare next legitimate stage");
            int revision = flow.Revision;
            Check(!flow.TryPrepare(stage, maximum, false) && !flow.TryStart(revision), "Duplicate preparation and early start denied");
            Check(flow.Resolve(revision, true, 0, 4) == EncounterOutcome.None, "No result while loading");
            StartPrepared(flow);
            Check(flow.Resolve(revision, false, 1, 4) == EncounterOutcome.None, "Survivor keeps attempt alive");
            Check(flow.Resolve(revision, true, 0, 4) == EncounterOutcome.Cleared, "Simultaneous kill and wipe awards clear");
            for (int n = 0; n < 20; n++) Check(flow.Resolve(revision, n % 2 == 0, 0, 4) == EncounterOutcome.None, "Duplicate outcome never creates another result");
            Check(!flow.TryPrepare(stage + 1, maximum, false), "Clear presentation cannot skip reward");
            Check(flow.FinishClear(revision, maximum) && !flow.FinishClear(revision, maximum), "Clear presentation completes once");
            Check(flow.Phase == (stage == maximum ? RaidBattlePhase.Victory : RaidBattlePhase.Reward), "Stages 1-5 receive cards; final without pending skill choices ends");
            if (stage < maximum)
            {
                Check(!flow.FinishFinalRewards(revision, maximum), "Intermediate rewards cannot complete the campaign");
                Check(flow.TryIntermission(revision, maximum) && !flow.TryIntermission(revision, maximum), "Reward portal returns to lobby exactly once");
                Check(flow.Stage == stage && flow.Phase == RaidBattlePhase.Lobby, "Intermission preserves cleared stage until next departure");
                Check(!RaidLobbyAdmission.AllowsNewParticipant(flow.Phase, flow.Stage, false), "Intermediate lobby cannot replace progressed AI with a new participant");
                Check(!flow.TryPrepare(stage, maximum, false) && !flow.TryPrepare(stage + 2, maximum, false), "Lobby cannot replay or skip a stage");
            }
        }
        Check(!flow.TryPrepare(7, maximum, false) && !flow.TryPrepare(6, maximum, true) && !flow.TryIntermission(flow.Revision, maximum), "No boss/reward portal after final clear");
        Check(flow.ReturnToLobby() && !flow.ReturnToLobby() && flow.Stage == 0, "Final result can return once to a fresh lobby");
        Check(RaidLobbyAdmission.AllowsNewParticipant(flow.Phase, flow.Stage, false), "Completed run reopens admission for a fresh party");
    }
    private static void StartPrepared(RaidRunFlow flow)
    {
        int revision = flow.Revision;
        Check(flow.Phase == RaidBattlePhase.Loading && !flow.TryArrival(revision) && !flow.TryCountdown(revision), "Loading cannot skip approach or arrival");
        Check(flow.TryApproach(revision) && !flow.TryApproach(revision), "Loading barrier opens approach exactly once");
        Check(!flow.TryCountdown(revision) && !flow.TryStart(revision), "Approach cannot skip arrival");
        Check(flow.TryArrival(revision) && !flow.TryArrival(revision), "Boss-room entry begins arrival exactly once");
        Check(!flow.TryStart(revision), "Arrival cannot start combat before completion");
        Check(flow.TryCountdown(revision) && !flow.TryCountdown(revision), "Completed arrival enters countdown exactly once");
        Check(flow.TryStart(revision) && !flow.TryStart(revision), "Countdown starts exactly one metrics attempt");
    }
    private static void FinalSkillRewards()
    {
        var flow = new RaidRunFlow();
        Check(flow.TryResume(6, 6), "Final-stage checkpoint can resume");
        StartPrepared(flow);
        int revision = flow.Revision;
        Check(flow.Resolve(revision, true, 4, 4) == EncounterOutcome.Cleared, "Final stage clears");
        Check(!flow.FinishFinalRewards(revision, 6), "Final skill choice cannot bypass defeat presentation");
        Check(flow.FinishClear(revision, 6, true) && flow.Phase == RaidBattlePhase.Reward, "Pending final RFV choice keeps reward phase");
        Check(!flow.TryIntermission(revision, 6) && !flow.TryPrepare(7, 6, false) && !flow.ReturnToLobby(), "Pending final choice cannot open a portal or skip to menu");
        Check(!flow.FinishFinalRewards(revision - 1, 6), "Stale final-choice callback is rejected");
        Check(flow.FinishFinalRewards(revision, 6) && !flow.FinishFinalRewards(revision, 6) && flow.Phase == RaidBattlePhase.Victory, "Settled final choices complete exactly once");
        var restored = new RaidRunFlow();
        Check(restored.TryRestoreSettlement(6, 6) && restored.Phase == RaidBattlePhase.Reward, "Final-clear journal resumes settlement rather than replaying boss");
        Check(!restored.TryStart(restored.Revision) && !restored.TryPrepare(7, 6, false), "Restored settlement cannot replay combat or create stage 7");
        Check(restored.FinishFinalRewards(restored.Revision, 6), "Restored final settlement can complete");
    }
    private static void RetryAndStaleCallbacks()
    {
        var flow = new RaidRunFlow(); Check(flow.TryPrepare(1, 6, false), "Prepare first attempt"); StartPrepared(flow);
        int old = flow.Revision;
        Check(flow.Resolve(old, false, 0, 4) == EncounterOutcome.Wiped, "Four dead wipes once");
        Check(!flow.TryPrepare(2, 6, true) && !flow.TryPrepare(2, 6, false), "Retry cannot skip stage");
        Check(flow.TryPrepare(1, 6, true), "Retry same stage");
        for (int i = 0; i < 100; i++)
            Check(!flow.TryApproach(old) && !flow.TryArrival(old) && !flow.TryCountdown(old) && !flow.TryStart(old) && !flow.FinishClear(old, 6) && flow.Resolve(old, true, 0, 4) == EncounterOutcome.None,
                "Old attempt countdown/clear/outcome cannot mutate new preparation");
        StartPrepared(flow);
        Check(flow.Resolve(flow.Revision, true, 3, 4) == EncounterOutcome.Cleared && flow.FinishClear(flow.Revision, 6), "Retry can clear normally");
    }
    private static void CloseAtEveryPhase()
    {
        var phases = new[] { RaidBattlePhase.Lobby, RaidBattlePhase.Loading, RaidBattlePhase.Approach,
            RaidBattlePhase.Arrival, RaidBattlePhase.Countdown, RaidBattlePhase.Combat, RaidBattlePhase.Wiped,
            RaidBattlePhase.BossClear, RaidBattlePhase.Reward, RaidBattlePhase.Victory };
        foreach (var phase in phases)
        {
            var flow = new RaidRunFlow();
            if (phase != RaidBattlePhase.Lobby) Check(flow.TryResume(phase == RaidBattlePhase.Victory ? 6 : 1, 6), "Prepare shutdown scenario");
            if (phase != RaidBattlePhase.Lobby && phase != RaidBattlePhase.Loading) Check(flow.TryApproach(flow.Revision), "Reach approach for shutdown");
            if (phase != RaidBattlePhase.Lobby && phase != RaidBattlePhase.Loading && phase != RaidBattlePhase.Approach) Check(flow.TryArrival(flow.Revision), "Reach arrival for shutdown");
            if (phase == RaidBattlePhase.Countdown || phase == RaidBattlePhase.Combat || phase == RaidBattlePhase.Wiped || phase == RaidBattlePhase.BossClear || phase == RaidBattlePhase.Reward || phase == RaidBattlePhase.Victory)
                Check(flow.TryCountdown(flow.Revision), "Reach countdown for shutdown");
            if (phase == RaidBattlePhase.Combat || phase == RaidBattlePhase.Wiped || phase == RaidBattlePhase.BossClear || phase == RaidBattlePhase.Reward || phase == RaidBattlePhase.Victory)
                Check(flow.TryStart(flow.Revision), "Reach combat for shutdown");
            if (phase == RaidBattlePhase.Wiped || phase == RaidBattlePhase.BossClear || phase == RaidBattlePhase.Reward || phase == RaidBattlePhase.Victory)
                Check(flow.Resolve(flow.Revision, phase != RaidBattlePhase.Wiped, 0, 4) == (phase == RaidBattlePhase.Wiped ? EncounterOutcome.Wiped : EncounterOutcome.Cleared), "Reach outcome for shutdown");
            if (phase == RaidBattlePhase.Reward || phase == RaidBattlePhase.Victory) Check(flow.FinishClear(flow.Revision, 6), "Reach settled phase for shutdown");
            Check(flow.Phase == phase, "Shutdown fixture reaches the phase it claims to test: " + phase);
            Check(RaidLobbyAdmission.AllowsNewParticipant(flow.Phase, flow.Stage, false) == (phase == RaidBattlePhase.Lobby), "New participants can never join an active encounter phase");
            int revision = flow.Revision;
            Check(flow.End() && !flow.End(), "Teardown is idempotent from every live phase");
            for (int n = 0; n < 50; n++)
                Check(!flow.TryPrepare(1, 6, false) && !flow.TryPrepare(1, 6, true) && !flow.TryApproach(revision) && !flow.TryArrival(revision) && !flow.TryCountdown(revision) &&
                    !flow.TryStart(revision) && !flow.FinishClear(revision, 6) && !flow.FinishFinalRewards(revision, 6) && flow.Resolve(revision, true, 0, 4) == EncounterOutcome.None &&
                    flow.Phase == RaidBattlePhase.Closed, "Delayed UI/network callbacks cannot reopen a closed session");
            var next = new RaidRunFlow(); Check(next.TryPrepare(1, 6, false) && next.Revision == 1, "New session has independent state");
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
            var flow = new RaidRunFlow(); Check(flow.TryPrepare(1, 6, false), "Prepare reward race"); StartPrepared(flow);
            Check(flow.Resolve(1, true, 4, 4) == EncounterOutcome.Cleared && flow.FinishClear(1, 6), "Reward race starts only after confirmed clear");
            var rewards = new RaidRewardRound(); rewards.Begin(1); rewards.Offer(1, new[] { 0, 1, 2 }); rewards.Offer(2, new[] { 2, 3, 4 });
            int grants = 0;
            if (rewards.Choose(1, 1, order == 0 ? 1 : 0)) grants++;
            if (rewards.Choose(1, 1, order == 0 ? 0 : 1)) grants++;
            Check(grants == 1 && !rewards.Complete, "Manual/timeout race consumes exactly once and still waits for peers");
            rewards.Remove(2);
            Check(rewards.Complete && flow.TryIntermission(1, 6) && flow.TryPrepare(2, 6, false), "Explicit departure cannot block rewards and next lobby departure");
            Check(!flow.TryPrepare(2, 6, false) && !rewards.Choose(2, 1, 2), "Duplicate departure/old offer cannot advance twice");
        }
        var barrier = new RaidRestoreBarrier(); barrier.SentFrame(1, 4, 1, (int)RaidBattlePhase.Reward);
        Check(!barrier.Accept(1, 4, 2, (int)RaidBattlePhase.Loading), "Return acknowledgement cannot grant from a previous stage");
        barrier.SentFrame(2, 4, 2, (int)RaidBattlePhase.Countdown);
        Check(!barrier.Accept(2, 4, 2, (int)RaidBattlePhase.Combat), "Countdown acknowledgement is stale after combat starts");
        barrier.SentFrame(3, 4, 2, (int)RaidBattlePhase.Combat);
        Check(barrier.Accept(3, 4, 2, (int)RaidBattlePhase.Combat), "Fresh current-state acknowledgement grants control");
    }
}
