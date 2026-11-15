using System;
using System.Collections.Generic;
using RaidRogue;

public static class RaidSteamTests
{
    private static int _checks;
    private static void Check(bool condition, string name) { _checks++; if (!condition) throw new Exception(name); }
    public static void Main()
    {
        ulong lobby = 109775243785000123, host = 76561198012345678;
        BuildCompatibility(host);
        Check(RaidSteamPolicy.Game == "astella", "Astella lobby identity");
        var formerProject = new Dictionary<string, string> { ["game"] = "coopraid3d", ["build"] = "test", ["protocol"] = RaidWire.Version.ToString(), ["host"] = host.ToString() };
        Check(RaidSteamPolicy.Validate(formerProject, "test", RaidWire.Version, host) != null, "Retired project lobbies are not Astella sessions");
        Check(RaidSteamPolicy.LaunchLobby(new[] { "game.exe", "+connect_lobby", lobby.ToString() }) == lobby, "64-bit cold launch");
        Check(RaidSteamPolicy.LaunchLobby(new[] { "+connect_lobby", "\"" + ulong.MaxValue + "\"" }) == ulong.MaxValue, "No floating-point rounding");
        foreach (string invalid in new[] { "0", "-1", "+1", "1.0", "1e10", "18446744073709551616", "1;quit", "" })
            Check(!RaidSteamPolicy.TryId(invalid, out _), "Invalid ID " + invalid);
        var metadata = new Dictionary<string, string> { ["game"] = RaidSteamPolicy.Game, ["build"] = "test", ["protocol"] = RaidWire.Version.ToString(), ["host"] = host.ToString(), ["phase"] = "lobby", ["ready"] = "1" };
        Check(RaidSteamPolicy.Validate(metadata, "test", RaidWire.Version, host) == null, "Compatible lobby");
        Check(RaidSteamPolicy.Validate(metadata, "other", RaidWire.Version, host) != null, "Build mismatch");
        Check(RaidSteamPolicy.Validate(metadata, "test", RaidWire.Version + 1, host) != null, "Protocol mismatch");
        Check(RaidSteamPolicy.Validate(metadata, "test", RaidWire.Version, host + 1) != null, "No owner migration");
        foreach (string phase in new[] { "lobby", "loading", "approach", "arrival", "countdown", "combat", "bossclear", "reward", "wiped", "victory" })
        {
            metadata["phase"] = phase; metadata["ready"] = "0";
            Check(RaidSteamPolicy.ValidateRecovery(metadata, "test", RaidWire.Version, host) == null, "Authenticated reserved users may recover into " + phase + " without opening new admission");
            Check(RaidSteamPolicy.Validate(metadata, "test", RaidWire.Version, host) != null, "Recovery does not relax ordinary admission in " + phase);
        }
        Check(RaidSteamPolicy.ValidateRecovery(metadata, "test", RaidWire.Version, host + 1) != null, "Recovery rejects lobby ownership migration away from original metadata host");
        Check(RaidSteamPolicy.ValidateRecovery(metadata, "test", RaidWire.Version, 0) != null, "Recovery rejects missing original lobby owner");
        metadata["host"] = (host + 1).ToString();
        Check(RaidSteamPolicy.ValidateRecovery(metadata, "test", RaidWire.Version, host) != null, "Recovery rejects metadata host differing from authenticated lobby owner");
        metadata["host"] = host.ToString(); metadata["phase"] = "closed";
        Check(RaidSteamPolicy.ValidateRecovery(metadata, "test", RaidWire.Version, host) != null, "Closed original session cannot be recovered");
        metadata["phase"] = "combat";
        Check(RaidSteamPolicy.ValidateRecovery(metadata, "other", RaidWire.Version, host) != null && RaidSteamPolicy.ValidateRecovery(metadata, "test", RaidWire.Version + 1, host) != null, "Recovery preserves build and protocol restrictions");
        metadata["game"] = "other";
        Check(RaidSteamPolicy.ValidateRecovery(metadata, "test", RaidWire.Version, host) != null, "Recovery rejects another game's lobby");
        metadata["game"] = RaidSteamPolicy.Game;
        metadata["phase"] = "lobby"; metadata["ready"] = "1";
        foreach (var key in new[] { "game", "host", "phase", "ready" })
        { string old = metadata[key]; metadata[key] = "invalid"; Check(RaidSteamPolicy.Validate(metadata, "test", RaidWire.Version, host) != null, "Reject " + key); metadata[key] = old; }
        Check(RaidSteamPolicy.ShouldLeaveLateLobby(lobby, 0, 0), "Cancelled late join cleaned up");
        Check(!RaidSteamPolicy.ShouldLeaveLateLobby(lobby, lobby, 0), "Old callback cannot leave new same-lobby session");
        Check(!RaidSteamPolicy.ShouldLeaveLateLobby(lobby, 0, lobby), "Old callback cannot cancel same-lobby retry");
        Check(RaidSteamPolicy.ShouldLeaveLateLobby(lobby, lobby + 1, lobby + 1), "Old other-lobby membership removed");
        var gate = new RaidInviteGate();
        Check(gate.Request(lobby, 0, false), "Cold invite queued");
        Check(!gate.Request(lobby, 0, false), "Duplicate launch/callback rejected");
        Check(gate.Begin() == lobby, "Single join attempt");
        Check(!gate.Request(lobby, 0, false), "In-flight duplicate rejected");
        Check(gate.Request(lobby + 1, lobby, true) && gate.Begin() == 0 && gate.NeedsConfirmation, "Occupied session never leaves automatically");
        gate.Decline(); Check(gate.Attempt == lobby && gate.Pending == 0, "Decline preserves ongoing connection");
        gate.Request(lobby + 1, lobby, true); gate.Confirm(); gate.Finish();
        Check(gate.Begin() == lobby + 1, "Confirmed switch survives menu reload");
        gate.Finish(); Check(gate.Request(lobby + 1, 0, false), "Retry after failure");
        Check(!gate.Request(lobby, lobby, true), "Already in requested lobby");
        var roster = new RaidRoster();
        Check(!roster.AllReady, "Empty roster cannot start");
        var a = roster.Join("Host"); Check(!roster.SetReady(a.Id, true), "Unloaded member cannot ready");
        a.Loaded = true; Check(roster.SetReady(a.Id, true) && roster.AllReady, "One human can start with bots");
        var b = roster.Join("Client"); Check(!roster.AllReady, "Connecting member blocks start");
        b.Loaded = true; roster.SetReady(b.Id, true); Check(roster.AllReady, "All ready");
        Check(!roster.SelectRole(b.Id, a.Role) && roster.AllReady, "Invalid role request has no side effects");
        Check(roster.SelectRole(b.Id, 3) && !roster.AllReady && !b.Ready, "Role change cancels readiness");
        roster.Remove(b.Id); Check(roster.AllReady, "Departure removes readiness wait");
        for (int i = 0; i < 3; i++) { var m = roster.Join("Member"); m.Loaded = true; roster.SetReady(m.Id, true); }
        Check(roster.Join("Fifth") == null && roster.AllReady, "4-person server limit");
        foreach (var m in roster.Members) m.Loaded = false;
        Check(!roster.AllLoaded, "New encounter resets loading barrier");
        foreach (var m in roster.Members) m.Loaded = true;
        Check(roster.AllLoaded, "Every client acknowledges scene readiness");
        var packet = new RaidPacket { Kind = RaidPacketKind.Snapshot, Tick = 7 };
        packet.Poses.Add(new RaidPose { Id = 1, Role = 1, Name = "Steam host", SteamId = host, Ready = true, Loaded = true });
        Check(RaidWire.Decode(RaidWire.Encode(packet), out var decoded) && decoded.Poses[0].SteamId == host && decoded.Poses[0].Ready && decoded.Poses[0].Loaded, "Identity/readiness wire round trip");
        foreach (var kind in new[] { RaidPacketKind.Ready, RaidPacketKind.SceneReady })
        { packet = new RaidPacket { Kind = kind, Id = 1 }; Check(RaidWire.Decode(RaidWire.Encode(packet), out decoded) && decoded.Id == 1, "Readiness packet"); }
        packet = new RaidPacket { Kind = RaidPacketKind.Ready, Id = 2 };
        Check(!RaidWire.Decode(RaidWire.Encode(packet), out _), "Malformed readiness rejected");
        Console.WriteLine("PASS: " + _checks + " Steam policy/readiness/protocol checks (no Steam account or native connection exercised).");
    }
    private static void BuildCompatibility(ulong host)
    {
        var metadata = new Dictionary<string, string> {
            ["game"] = RaidSteamPolicy.Game, ["build"] = RaidSteamPolicy.CurrentBuild,
            ["protocol"] = RaidWire.Version.ToString(), ["host"] = host.ToString(),
            ["phase"] = "lobby", ["ready"] = "1"
        };
        Check(RaidSteamPolicy.Validate(metadata, RaidSteamPolicy.CurrentBuild, RaidWire.Version, host) == null &&
            RaidSteamPolicy.ValidateRecovery(metadata, RaidSteamPolicy.CurrentBuild, RaidWire.Version, host) == null,
            "Current release permits compatible admission and authenticated recovery");
        metadata["build"] = "steam-combat-1";
        Check(RaidSteamPolicy.Validate(metadata, RaidSteamPolicy.CurrentBuild, RaidWire.Version, host) != null &&
            RaidSteamPolicy.ValidateRecovery(metadata, RaidSteamPolicy.CurrentBuild, RaidWire.Version, host) != null,
            "Shipped legacy build tag is rejected independently of protocol");
        metadata["build"] = RaidSteamPolicy.CurrentBuild; metadata["protocol"] = "16";
        Check(RaidSteamPolicy.Validate(metadata, RaidSteamPolicy.CurrentBuild, RaidWire.Version, host) != null &&
            RaidSteamPolicy.ValidateRecovery(metadata, RaidSteamPolicy.CurrentBuild, RaidWire.Version, host) != null,
            "Shipped legacy protocol is rejected even when metadata advertises the current build tag");
        metadata["build"] = "steam-combat-1";
        Check(RaidSteamPolicy.Validate(metadata, RaidSteamPolicy.CurrentBuild, RaidWire.Version, host) != null &&
            RaidSteamPolicy.ValidateRecovery(metadata, RaidSteamPolicy.CurrentBuild, RaidWire.Version, host) != null,
            "Actual shipped legacy build and protocol cannot join or recover into this release");
        metadata["build"] = RaidSteamPolicy.CurrentBuild; metadata["protocol"] = RaidWire.Version.ToString();
        Check(RaidSteamPolicy.Validate(metadata, "steam-combat-1", 16, host) != null &&
            RaidSteamPolicy.ValidateRecovery(metadata, "steam-combat-1", 16, host) != null,
            "Compatibility equality also rejects current metadata for a legacy caller");
    }
}
