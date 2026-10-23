using System;
using System.Collections.Generic;
using RaidRogue;

internal static class RaidCombatNetworkTests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Equal(double expected, double actual, string message) => Check(Math.Abs(expected - actual) < .00001, message);
    private static RaidActionRequest Request(int actor, int epoch, uint sequence, int skill = 1, int target = -1) =>
        new RaidActionRequest { actor = actor, epoch = epoch, sequence = sequence, skill = skill, target = target };

    private static void Main()
    {
        OwnershipAndRetries(); RewardsAndDepartures(); FragmentLossAndReordering(); MetricsReplaceRatherThanReplay(); MovementBarrier();
        Console.WriteLine("PASS: " + checks + " authoritative combat protocol, reward, replication and motion-barrier checks.");
    }

    private static void OwnershipAndRetries()
    {
        var gate = new RaidActionGate();
        Check(!gate.Accept(2, 3, Request(1, 3, 1)), "client cannot act as host");
        Check(!gate.Accept(2, 3, Request(10001, 3, 1)), "client cannot act as AI");
        Check(!gate.Accept(0, 3, Request(0, 3, 1)), "unadmitted peer cannot act");
        Check(!gate.Accept(2, 3, Request(2, 2, 1)), "previous attempt request cannot apply");
        Check(!gate.Accept(2, 3, Request(2, 4, 1)), "future attempt request cannot apply");
        Check(!gate.Accept(2, 3, Request(2, 3, 1, 13)), "unknown skill rejected");
        Check(!gate.Accept(2, 3, Request(2, 3, 0)), "zero sequence rejected");
        Check(gate.Accept(2, 3, Request(2, 3, 1)), "invalid requests did not consume valid sequence");
        for (int i = 0; i < 10; i++) Check(!gate.Accept(2, 3, Request(2, 3, 1)), "retry cannot repeat damage or cooldown");
        Check(gate.Accept(1, 3, Request(1, 3, 1)), "host enters the same one-shot gate");
        Check(!gate.Accept(1, 3, Request(1, 3, 1)), "host is not processed a second time");
        Check(gate.Accept(2, 3, Request(2, 3, 3, 6)), "potion request has its own sequence");
        Check(!gate.Accept(2, 3, Request(2, 3, 2, 5)), "outdated action rejected");
        Check(gate.Accept(2, 4, Request(2, 4, 4)), "new attempt accepts a new action");
        Check(!gate.Accept(2, 4, Request(2, 3, 5)), "late previous attempt action rejected");
        gate.Remove(2);
        Check(gate.Accept(2, 4, Request(2, 4, 1)), "connection gate can be cleaned up");
    }

    private static void RewardsAndDepartures()
    {
        var rewards = new RaidRewardRound(); rewards.Begin(8);
        var offered = new[] { 1, 2, 3 };
        for (int player = 1; player <= 4; player++) rewards.Offer(player, offered);
        offered[0] = 6; var visible = rewards.Cards(1); visible[1] = 6;
        Check(!rewards.Choose(8, 1, 6), "modifying either offer array cannot change server choices");
        Check(!rewards.Choose(7, 1, 1), "old reward epoch rejected");
        Check(!rewards.Choose(8, 5, 1), "nonparticipant cannot choose");
        Check(!rewards.Complete, "wait for connected participants");
        Check(rewards.Choose(8, 1, 1), "host chooses a valid card");
        Check(!rewards.Choose(8, 1, 2), "different card in a duplicate request cannot apply twice");
        rewards.Offer(1, new[] { 4, 5, 6 });
        Check(rewards.Cards(1)[0] == 1 && rewards.Chosen(1), "offer cannot reset a completed choice");
        Check(rewards.Choose(8, 2, 3), "dead and living participants share the same reward gate");
        rewards.Remove(3); Check(!rewards.Complete, "one remaining connected participant still pending");
        rewards.Remove(4); Check(rewards.Complete, "departed users cannot block stage progression");
        Check(!rewards.Choose(8, 4, 1), "departed user's late choice rejected");
        rewards.Begin(9); Check(!rewards.Complete && !rewards.Chosen(1), "new round clears only reward state");
        rewards.Offer(1, new[] { 4, 5, 6 });
        Check(!rewards.Choose(8, 1, 4) && rewards.Choose(9, 1, 4) && rewards.Complete, "new round accepts only its offer and epoch");
    }

    private static void FragmentLossAndReordering()
    {
        var assembler = new RaidFrameAssembler();
        var first = new byte[] { 1, 2 }; var second = new byte[] { 3, 4 }; var third = new byte[] { 5 };
        Check(assembler.Add(1, 2, 3, third) == null, "incomplete state never applied");
        Check(assembler.Add(1, 2, 3, third) == null, "duplicate chunk does not advance completion");
        Check(assembler.Add(1, 0, 3, first) == null, "out-of-order state remains pending");
        var complete = assembler.Add(1, 1, 3, second);
        Check(complete != null && complete.Length == 5, "reordered complete state assembled");
        for (int i = 0; i < 5; i++) Equal(i + 1, complete[i], "ordered bytes preserved");
        Check(assembler.Add(1, 0, 3, first) == null, "applied frame cannot replay");
        Check(assembler.Add(2, 0, 2, first) == null, "lost second chunk remains incomplete");
        complete = assembler.Add(3, 0, 1, third);
        Check(complete != null && complete[0] == 5, "new full state recovers without old missing chunk");
        Check(assembler.Add(2, 1, 2, second) == null, "late lost chunk cannot roll back state");
        Check(assembler.Add(4, 0, 129, first) == null, "bounded chunk count");
        Check(assembler.Add(4, -1, 2, first) == null, "negative index rejected");
        Check(assembler.Add(4, 2, 2, first) == null, "out-of-range index rejected");
        Check(assembler.Add(4, 0, 1, new byte[901]) == null, "bounded fragment size");
        Check(assembler.Add(4, 0, 2, first) == null, "valid pending state");
        Check(assembler.Add(4, 1, 3, second) == null, "inconsistent count cannot complete state");
        Check(assembler.Add(4, 1, 2, second) != null, "valid retransmission recovers");
        var random = new Random(17);
        for (uint frame = 5; frame < 25; frame++)
        {
            var order = new List<int>(); for (int i = 0; i < 8; i++) order.Add(i);
            for (int i = 0; i < 8; i++) { int other = random.Next(8); int hold = order[i]; order[i] = order[other]; order[other] = hold; }
            int applied = 0;
            foreach (int index in order)
            {
                var packet = new RaidPacket { Kind = RaidPacketKind.CombatFrame, Tick = frame, Id = index, Role = 8, Bytes = new byte[] { (byte)index } };
                var wire = RaidWire.Encode(packet);
                Check(RaidWire.Decode(wire, out var decoded), "combat frame wire roundtrip");
                var result = assembler.Add(decoded.Tick, decoded.Id, decoded.Role, decoded.Bytes);
                if (result != null) { applied++; for (int j = 0; j < 8; j++) Equal(j, result[j], "shuffled fragment content"); }
            }
            Equal(1, applied, "world is applied exactly once per frame");
        }
    }

    private static void MetricsReplaceRatherThanReplay()
    {
        var host = new CombatMetricsLedger(); host.Register(2, "Healer", 2); host.BeginStage(1);
        host.Advance(10, true); host.RecordDamage(2, 500, 0, false); host.RecordHealing(2, 100, 250);
        host.RecordHealing(2, 250, 500, HealingKind.Potion); host.SetDead(2, true); host.SetDead(2, true); host.FailAttempt();
        host.RegisterRetry(); host.BeginStage(1); host.Advance(20, true); host.RecordDamage(2, 1000, 0, false);
        host.RecordHealing(2, 100, 300); host.RecordHealing(2, 300, 500, HealingKind.Potion); host.EndStage();
        host.Register(10001, "Replaced bot", 1); host.RetainCurrentRoster(new[] { 2 });
        Equal(1, host.Snapshot().Members.Count, "new attempt roster excludes replaced bots and departed peers");
        Equal(1, host.FailedAttempts[0].Members.Count, "pruning live roster does not mutate historical results");
        var client = new CombatMetricsLedger();
        for (int i = 0; i < 20; i++)
        {
            client.ReplaceNetworkSnapshot(host.Snapshot(), host.Stages, host.FailedAttempts, host.RetryCount);
            client.RecordDamage(2, 9999, 0, false); client.RecordHealing(2, 0, 9999); client.SetDead(2, true); client.Advance(1, true);
            var total = client.RunTotal(); var member = total.Members[0];
            Equal(1000, member.Damage, "replica effects and snapshots cannot add duplicate damage");
            Equal(50, member.Dps(total.Seconds), "failed attempts excluded from clear DPS");
            Equal(10, member.Hps(total.Seconds), "potions excluded from HPS");
            Equal(200, member.SelfHealing, "potion effective recovery remains separate");
            Equal(1, client.FailedAttempts[0].Members[0].Deaths, "death counted once and failure retained");
            Equal(1, client.RetryCount, "retry count replaces rather than increments");
        }
    }

    private static void MovementBarrier()
    {
        var queue = new RaidInputQueue();
        for (uint i = 1; i <= 8; i++) queue.Add(new RaidMoveInput { Sequence = i, MotionVersion = 2, X = 1, Dash = true });
        queue.DiscardPending(); Equal(8, queue.Acknowledged, "retry drains and acknowledges old prediction");
        Equal(0, queue.Next().Sequence, "old movement is not replayed after reset");
        Check(!queue.Add(new RaidMoveInput { Sequence = 8, MotionVersion = 2, X = 1 }), "late prior command cannot return");
        var packet = new RaidPacket { Kind = RaidPacketKind.Input };
        packet.Inputs.Add(new RaidMoveInput { Sequence = 9, MotionVersion = 3, Z = 1, Dash = true });
        Check(RaidWire.Decode(RaidWire.Encode(packet), out var copy), "forced-movement generation travels in owner input");
        Equal(3, copy.Inputs[0].MotionVersion, "input motion barrier retained");
        var posePacket = new RaidPacket { Kind = RaidPacketKind.Snapshot, Tick = 10 };
        posePacket.Poses.Add(new RaidPose { Id = 2, Role = 2, Name = "Healer", MotionVersion = 3, MovementLocked = true, MoveSpeed = 10, Bot = false });
        Check(RaidWire.Decode(RaidWire.Encode(posePacket), out copy), "authoritative movement lock and generation roundtrip");
        Check(copy.Poses[0].MovementLocked && copy.Poses[0].MotionVersion == 3 && copy.Poses[0].MoveSpeed == 10, "rescue/death override owner prediction");
    }
}
