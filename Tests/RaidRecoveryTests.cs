using System;
using RaidRogue;

internal static class RaidRecoveryTests
{
    private static int checks;
    private static void Check(bool pass, string name) { checks++; if (!pass) throw new Exception(name); }
    private const ulong Account = 76561198012345678UL;
    private static void Main()
    {
        ReservationAndOwnership(); RewardRaces(); TransitionBarriers(); WireAndMotion();
        Console.WriteLine("PASS: " + checks + " recovery reservation, authenticated slot, reward race, epoch barrier and protocol checks (no Unity/Steam runtime).");
    }
    private static void ReservationAndOwnership()
    {
        var settings = new RaidRecoverySettings(); var seats = new RaidRecoverySlots(settings);
        Check(settings.reservationSeconds == 60 && settings.rewardSeconds == 15, "Requested policy defaults");
        var slot = seats.Register(Account, 2);
        Check(slot != null && !slot.Automated, "Connected human slot");
        Check(seats.Register(Account, 3) == null && seats.Register(Account + 1, 2) == null && seats.Register(0, 3) == null, "No duplicate identity/actor/anonymous ownership");
        Check(seats.BeginRestore(Account, 0, out _) != null, "Connected account cannot connect twice");
        Check(seats.Reserve(2, 100) && slot.Automated && slot.Deadline == 160, "Unexpected disconnect reserves existing actor");
        uint lostLease = slot.Lease;
        for (int i = 0; i < 50; i++) Check(!seats.Reserve(2, 101 + i) && slot.Lease == lostLease && slot.Deadline == 160, "Duplicate callbacks cannot renew reservation");
        Check(seats.BeginRestore(Account + 1, 110, out _) != null, "Another authenticated account cannot reclaim this slot");
        Check(seats.BeginRestore(Account, 110, out var restoring) == null && ReferenceEquals(slot, restoring) && restoring.Actor == 2, "Return preserves slot object and actor ID");
        Check(slot.Automated && slot.State == RaidControlState.Restoring, "AI owns restoration interval");
        Check(!seats.Complete(Account, lostLease, 111), "Old control lease cannot claim actor");
        Check(seats.BeginRestore(Account, 111, out _) != null, "Concurrent restore denied");
        Check(seats.Reserve(2, 120) && slot.Deadline == 160, "Failed restore keeps original deadline");
        Check(seats.BeginRestore(Account, 130, out _) == null, "Retry before expiry");
        Check(seats.Complete(Account, slot.Lease, 140) && !slot.Automated, "Only valid baseline acknowledgement transfers control");
        Check(!seats.Complete(Account, slot.Lease, 141), "Duplicate grant cannot transfer twice");
        Check(seats.Reserve(2, 200) && slot.Deadline == 260, "A later distinct outage gets its own reservation");
        Check(seats.BeginRestore(Account, 259, out _) == null, "Restore begun before deadline");
        Check(!seats.Complete(Account, slot.Lease, 260) && slot.State == RaidControlState.PermanentAI, "Deadline also applies during restoration");
        Check(!seats.Reserve(2, 300) && seats.BeginRestore(Account, 300, out _) != null && ReferenceEquals(seats.Actor(2), slot), "Late return denied; permanent AI retains actor");
        seats.Remove(2); Check(seats.Find(Account) == null, "Explicit leave removes reservation");
        var shortPolicy = new RaidRecoverySlots(new RaidRecoverySettings { reservationSeconds = 2, rewardSeconds = 1 });
        var shortSlot = shortPolicy.Register(Account, 2); shortPolicy.Reserve(2, 9);
        Check(!shortPolicy.Expire(shortSlot, 10.99f) && shortPolicy.Expire(shortSlot, 11), "Central duration setting controls exact boundary");
        for (int seed = 0; seed < 100; seed++)
        {
            var random = new Random(seed); var model = new RaidRecoverySlots(settings); var s = model.Register(Account, 2); model.Reserve(2, 0);
            for (int tick = 1; tick <= 80; tick++)
            {
                if (s.State == RaidControlState.Reserved && random.Next(2) == 0) model.BeginRestore(Account, tick, out _);
                else if (s.State == RaidControlState.Restoring) model.Reserve(2, tick);
                model.Expire(s, tick);
                Check(s.Automated && s.Actor == 2 && s.Deadline == 60, "Repeated failed restores preserve actor, AI authority and deadline");
                if (tick >= 60) Check(s.State == RaidControlState.PermanentAI, "Expiry cannot be bypassed by retry traffic");
            }
        }
    }
    private static void RewardRaces()
    {
        for (int humanFirst = 0; humanFirst < 2; humanFirst++)
        {
            var seats = new RaidRecoverySlots(new RaidRecoverySettings()); var slot = seats.Register(Account, 2);
            var rewards = new RaidRewardRound(); rewards.Begin(4); rewards.Offer(2, new[] { 3, 2, 1 });
            seats.Reserve(2, 10); seats.BeginReward(slot, 4, 20);
            Check(!seats.RewardDue(slot, 4, 34.99f) && seats.RewardDue(slot, 4, 35), "15 seconds counted from reward entry while absent");
            int grants = 0;
            if (humanFirst == 1 && rewards.Choose(4, 2, 2)) grants++;
            if (seats.RewardDue(slot, 4, 35) && rewards.Choose(4, 2, rewards.Cards(2)[0])) { grants++; seats.AutoSelected(slot, 3); }
            if (humanFirst == 0 && rewards.Choose(4, 2, 2)) grants++;
            Check(grants == 1 && rewards.Complete, "Human/auto selection race applies exactly one existing offer");
            Check(slot.AutoRevision == (humanFirst == 0 ? 1 : 0), "Only successful automatic selection is reported");
            Check(!rewards.Choose(4, 2, 3), "Repeated timer cannot duplicate card");
        }
        var ledger = new RaidRecoverySlots(new RaidRecoverySettings()); var member = ledger.Register(Account, 2);
        ledger.Reserve(2, 0, 1); ledger.BeginRestore(Account, 3, out _); ledger.Complete(Account, member.Lease, 4);
        Check(!ledger.RewardDue(member, 1, 40), "Connected participant has no automatic timer");
        ledger.Reserve(2, 8, 1);
        Check(!ledger.RewardDue(member, 1, 22.99f) && ledger.RewardDue(member, 1, 23), "Second outage in same reward round restarts absent interval");
        ledger.BeginReward(member, 2, 30);
        Check(!ledger.RewardDue(member, 1, 99) && !ledger.RewardDue(member, 2, 44.99f) && ledger.RewardDue(member, 2, 45), "Next stage owns new timer");
        ledger.Expire(member, 100); ledger.BeginReward(member, 3, 101);
        Check(ledger.RewardDue(member, 3, 116), "Permanent AI never blocks future rewards");
    }
    private static void TransitionBarriers()
    {
        var barrier = new RaidRestoreBarrier();
        for (int phase = 0; phase < 7; phase++)
        {
            uint serial = (uint)phase + 1; barrier.SentFrame(serial, 7, 3, phase);
            Check(barrier.Accept(serial, 7, 3, phase), "Full frame of current phase can be acknowledged");
            Check(!barrier.Accept(serial, 6, 3, phase), "Prior connection lease rejected");
            Check(!barrier.Accept(serial, 7, 4, phase), "Stage/retry transition invalidates old frame acknowledgement");
            Check(!barrier.Accept(serial, 7, 3, (phase + 1) % 7), "Reward/wipe/countdown transition invalidates previous baseline");
        }
        for (uint i = 100; i < 200; i++) barrier.SentFrame(i, 8, 4, 2);
        Check(!barrier.Accept(1, 7, 3, 0) && barrier.Accept(199, 8, 4, 2), "Bounded acknowledgement history");
        var assembler = new RaidFrameAssembler();
        Check(assembler.Add(10, 0, 2, new byte[] { 1 }) == null, "Partial baseline never applies");
        Check(assembler.Add(11, 0, 1, new byte[] { 2 })[0] == 2, "Newer complete state replaces partial previous state");
        Check(assembler.Add(10, 1, 2, new byte[] { 3 }) == null && assembler.Add(11, 0, 1, new byte[] { 2 }) == null, "Late/duplicate frames cannot overwrite or replay effects");
    }
    private static void WireAndMotion()
    {
        foreach (var kind in new[] { RaidPacketKind.RestoreReady, RaidPacketKind.RestoreComplete })
        {
            var bytes = RaidWire.Encode(new RaidPacket { Kind = kind, Id = 3, Tick = 444, Role = 8 });
            Check(RaidWire.Decode(bytes, out var packet) && packet.Kind == kind && packet.Id == 3 && packet.Tick == 444 && packet.Role == 8, "Recovery barrier packet round trip");
            Array.Resize(ref bytes, bytes.Length - 1); Check(!RaidWire.Decode(bytes, out _), "Truncated acknowledgement rejected");
        }
        foreach (var kind in new[] { RaidPacketKind.Leave, RaidPacketKind.ResumeHello, RaidPacketKind.PartyNotice })
            Check(RaidWire.Decode(RaidWire.Encode(new RaidPacket { Kind = kind, Text = "current lobby" }), out var packet) && packet.Kind == kind, "Recovery control packet round trip");
        var dash = new DashMotion(); var config = new DashSettings();
        Check(dash.TryStart(config, true, 1, 0, 0, 1), "Dash begun before disconnect");
        double cooldown = dash.CooldownRemaining; dash.Cancel();
        Check(!dash.Active && dash.CooldownRemaining == cooldown, "Disconnect cancels dash without refunding cooldown");
        var queue = new RaidInputQueue(); queue.Add(new RaidMoveInput { Sequence = 1, MotionVersion = 1, X = 1, Dash = true }); queue.DiscardPending();
        Check(queue.Next().Sequence == 0 && !queue.Add(new RaidMoveInput { Sequence = 1, X = 1 }), "Queued player input cannot replay during AI takeover");
    }
}
