using System;
using RaidRogue;

internal static class RaidNetworkTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Equal(double a, double b, string message) => Check(Math.Abs(a - b) < 0.0001, message);
    private static RaidPacket RoundTrip(RaidPacket packet)
    {
        var data = RaidWire.Encode(packet); Check(data.Length <= RaidWire.MaxBytes, "packet fits MTU");
        Check(RaidWire.Decode(data, out var decoded), "valid packet roundtrip"); return decoded;
    }
    private static void Main()
    {
        var roster = new RaidRoster();
        for (int i = 0; i < 4; i++) Check(roster.Join("Player " + i) != null, "host plus three clients");
        Check(roster.Join("Fifth") == null, "capacity includes host");
        Check(!roster.SelectRole(1, 2), "occupied role rejected by host");
        Check(!roster.SelectRole(999, 1), "unknown owner rejected");
        Check(!roster.SelectRole(1, 5), "invalid role rejected");
        roster.Remove(2);
        Check(roster.SelectRole(1, 2), "first request wins free role");
        Check(!roster.SelectRole(3, 2), "second simultaneous request loses");
        var replacement = roster.Join("New"); Check(replacement.Id > 4 && replacement.Role == 1, "rejoin gets fresh identity and free role");
        Check(RaidRoster.CleanName("<b>\n홍길동</b>").IndexOf('<') < 0, "name markup stripped");

        var queue = new RaidInputQueue();
        var input = new RaidMoveInput { Sequence = 1, X = 1, Dash = true };
        Check(queue.Add(input), "owner input accepted"); Check(!queue.Add(input), "duplicate not queued twice");
        Check(!queue.Add(new RaidMoveInput { Sequence = 2, X = float.NaN }), "NaN rejected");
        Check(!queue.Add(new RaidMoveInput { Sequence = 2, Z = float.PositiveInfinity }), "infinity rejected");
        Check(!queue.Add(new RaidMoveInput { Sequence = 2, X = 20 }), "speed vector rejected");
        Check(!queue.Add(new RaidMoveInput { Sequence = 100000 }), "sequence jump rejected");
        for (uint seq = 2; seq <= 90; seq++) Check(queue.Add(new RaidMoveInput { Sequence = seq, X = 1, Dash = true }), "bounded flood queued");
        var dash = new DashMotion(); var settings = new DashSettings(); int starts = 0; double distance = 0;
        // Processing is driven by host ticks, never by the number or timing of received packets.
        for (int tick = 0; tick < 30; tick++)
        {
            var command = queue.Next(); dash.TickCooldown(RaidWire.Step);
            if (command.Dash && dash.TryStart(settings, true, command.X, command.Z, 0, 1)) starts++;
            distance += dash.Active ? dash.Step(RaidWire.Step) : 7.8 * RaidWire.Step;
        }
        Equal(30, queue.Acknowledged, "flood cannot process more than one command per tick");
        Equal(1, starts, "repeated dash requests obey host cooldown");
        Check(distance <= 4 + 7.8, "one second movement budget bounded");
        Check(!queue.Add(input), "replayed old input rejected");

        foreach (double split in new[] { 0.0, 0.033, 0.1, 0.19, 0.2, 1.0 })
        {
            var authority = new DashMotion(); authority.TryStart(settings, true, 1, 1, 0, 1);
            authority.Step(split); authority.TickCooldown(split);
            var prediction = new DashMotion(); prediction.Restore(authority.Capture());
            for (int tick = 0; tick < 180; tick++)
            {
                Equal(authority.Step(RaidWire.Step), prediction.Step(RaidWire.Step), "restored dash replays same distance");
                authority.TickCooldown(RaidWire.Step); prediction.TickCooldown(RaidWire.Step);
                Equal(authority.CooldownRemaining, prediction.CooldownRemaining, "restored cooldown agrees");
            }
        }
        var snapshot = new RaidPacket { Kind = RaidPacketKind.Snapshot, Tick = 900 };
        for (int i = 1; i <= 4; i++) snapshot.Poses.Add(new RaidPose { Id = i, Role = i, Name = "한국어 플레이어" + i, Ack = 42, X = i * 2, Z = -5, Yaw = 359, MovementLocked = i == 2, Dash = dash.Capture() });
        var copy = RoundTrip(snapshot);
        Equal(4, copy.Poses.Count, "full roster for late join"); Equal(900, copy.Tick, "snapshot ordering tick");
        Equal(42, copy.Poses[3].Ack, "per-owner input acknowledgement");
        Check(copy.Poses[0].Name == snapshot.Poses[0].Name, "UTF8 names roundtrip");
        Check(!copy.Poses[0].MovementLocked && copy.Poses[1].MovementLocked, "host movement lock reaches owner prediction");
        var welcome = RoundTrip(new RaidPacket { Kind = RaidPacketKind.Welcome, Id = 4, Speed = 7.8f, Radius = 0.5f, Arena = 18, TurnSpeed = 720 });
        Equal(5, welcome.Dash.cooldown, "host tuning delivered");
        var packet = new RaidPacket { Kind = RaidPacketKind.Input }; packet.Inputs.Add(input);
        Check(RoundTrip(packet).Inputs[0].Dash, "dash edge delivered");
        Check(RoundTrip(new RaidPacket { Kind = RaidPacketKind.Hello, Text = "Step9_TestScene\nPlayer" }).Text.Contains("Player"), "handshake payload");
        Check(RoundTrip(new RaidPacket { Kind = RaidPacketKind.Notice, Text = "종료:정원 초과" }).Text.Contains("정원"), "rejection explanation");
        Equal(3, RoundTrip(new RaidPacket { Kind = RaidPacketKind.Role, Role = 3 }).Role, "role request");
        byte[] valid = RaidWire.Encode(snapshot);
        for (int length = 0; length < valid.Length; length++)
        {
            var truncated = new byte[length]; Array.Copy(valid, truncated, length);
            Check(!RaidWire.Decode(truncated, out _), "truncated packet rejected without throwing");
        }
        valid[0] = 255; Check(!RaidWire.Decode(valid, out _), "protocol mismatch rejected");
        var random = new Random(24);
        for (int i = 0; i < 1000; i++) { var bytes = new byte[random.Next(1200)]; random.NextBytes(bytes); RaidWire.Decode(bytes, out _); checks++; }
        Console.WriteLine($"PASS: {checks} network capacity, role arbitration, input authority, replay and packet validation checks.");
    }
}
