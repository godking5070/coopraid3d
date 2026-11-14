using System;
using System.Linq;
using RaidRogue;

// Executes real pure production state/calculation code. Does not simulate Unity effect handlers.
class CharacterTrainingTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Near(double a, double b, string message) => Check(Math.Abs(a - b) < .0001, message + ": " + a + " / " + b);
    static CharacterUseContext Ready(float range = 0, float cast = 0) => new CharacterUseContext(true, true, true, range, castTime: cast);
    static CharacterRuntimeState Build(CharacterDefinition d, int route = 0, int r = 0, int f = 0, int v = 0, int card = 0)
    {
        Check(CharacterTrainingRules.TryBuild(d, d.routes[route].id, d.unlocks[0].choices[r], d.unlocks[1].choices[f], d.unlocks[2].choices[v], card, out var state), d.name + " practice loadout");
        return state;
    }
    static void Main()
    {
        var jobs = new[] { AegisPaladinData.Build() }.Concat(CharacterRosterData.Jobs.Select(CharacterRosterData.Definition)).ToArray();
        Check(jobs.Length == 12 && jobs.Sum(d => d.routes.Length) == 36, "12 jobs / 36 origins");
        Check(CharacterDefinitionValidation.Check(jobs).Length == 0, "unique IDs and references");
        foreach (var d in jobs)
        {
            // Cover each origin and every R/F/V candidate without pretending to play all permutations.
            for (int route = 0; route < 3; route++)
            {
                var state = Build(d, route, route, (route + 1) % 3, route % 2, route);
                var snap = state.Snapshot;
                Check(state.Selected(CharacterSlot.Identity).id == d.routes[route].identitySkillId, "Z follows origin");
                Check(new[] { CharacterSlot.Active, CharacterSlot.OffGcd2, CharacterSlot.Ultimate }.All(state.Unlocked), "practice choices unlocked");
                Check(snap.clearedStage == 0 && snap.clearedEncounters.Length == 0 && snap.chosenRules.Length == 0 && snap.chosenRewardKeys.Length == 0, "practice does not fabricate reward history");
                Check(snap.effects.Length == (route == 0 ? 0 : 1), "one evolution choice, no accumulated modifiers");
                var peer = Build(d, route);
                foreach (var resource in d.resources)
                {
                    state.Gain(resource.id, state.Maximum(resource.id));
                    Near(peer.Resource(resource.id), resource.initial, "resource ownership");
                }
                Check(state.TryUse(CharacterSlot.Gcd1, Ready(), 1, s => true, out _), "GCD 1 valid transaction");
                Check(!state.TryUse(CharacterSlot.Gcd2, Ready(), 1, s => true, out _), "shared GCD");
                Check(state.Snapshot.combo1 == 2 && state.Snapshot.combo2 == 1 && peer.Snapshot.gcd == 0, "independent chains and actors");
                var restored = new CharacterRuntimeState(d);
                int replay = 0; restored.ResourceChanged += (_, delta) => replay++;
                Check(restored.Restore(state.Snapshot), "loadout/cooldown snapshot restore");
                Check(replay == 0, "restore does not replay resource triggers");
                Near(restored.Snapshot.gcd, state.Snapshot.gcd, "restored GCD");
                restored.ClearOnDeath();
                Check(restored.Snapshot.combo1 == 1 && restored.Snapshot.castSkillId == "" && restored.RouteId == state.RouteId, "death combo cleanup and origin retention");
                restored.Reset(CharacterReset.Retry);
                Check(restored.RouteId == state.RouteId && restored.Selected(CharacterSlot.Active).id == state.Selected(CharacterSlot.Active).id && restored.Snapshot.gcd == 0, "retry retains loadout and resets timing");
                restored.Reset(CharacterReset.NewRun);
                Check(!restored.Unlocked(CharacterSlot.Active) && restored.RouteId == "", "normal new run removes practice unlocks");
            }
            var normal = new CharacterRuntimeState(d);
            Check(!normal.Unlocked(CharacterSlot.Active) && !normal.Unlocked(CharacterSlot.OffGcd2) && !normal.Unlocked(CharacterSlot.Ultimate), "normal run unchanged");
            Check(!CharacterTrainingRules.TryBuild(d, d.routes[0].id, d.unlocks[1].choices[0], d.unlocks[1].choices[0], d.unlocks[2].choices[0], 0, out _), "reject wrong slot");
            Check(!CharacterTrainingRules.TryBuild(d, "foreign-origin", d.unlocks[0].choices[0], d.unlocks[1].choices[0], d.unlocks[2].choices[0], 0, out _), "reject foreign origin");
            Check(!CharacterTrainingRules.TryBuild(d, d.routes[0].id, d.unlocks[0].choices[0], d.unlocks[1].choices[0], d.unlocks[2].choices[0], 3, out _), "reject invalid evolution");
            var failed = Build(d); int effects = 0;
            Check(!failed.TryUse(CharacterSlot.Gcd1, Ready(10000), 0, s => { effects++; return true; }, out _) && effects == 0, "range rejected before effects");
            Check(!failed.TryUse(CharacterSlot.Gcd1, Ready(), 0, s => false, out _) && failed.Snapshot.gcd == 0 && failed.Snapshot.combo1 == 1, "handler failure leaves timing and combo untouched");
        }
        ResourceEvents(jobs[0]); ChargeRecovery(); Clock(); Metrics(); Positions(); Effects();
        Console.WriteLine("PASS character training: " + checks + " assertions (pure code, not Unity play)");
    }
    static void ResourceEvents(CharacterDefinition d)
    {
        var state = Build(d); string id = d.resources[0].id; double gained = 0, spent = 0;
        state.ResourceChanged += (key, delta) => { if (key == id) { if (delta > 0) gained += delta; else spent -= delta; } };
        Check(!state.TryUse(CharacterSlot.Identity, Ready(), 0, s => true, out _), "insufficient Z does not spend");
        Near(spent, 0, "failed Z resource counter");
        state.Gain(id, 200); state.Gain(id, 5); Near(gained, 100, "count actual gain, not overcap");
        Check(!state.TryUse(CharacterSlot.Identity, Ready(), 0, s => false, out _), "failed effect no resource commit");
        Near(spent, 0, "effect failure resource counter");
        Check(state.TryUse(CharacterSlot.Identity, Ready(), 0, s => true, out _), "Z commit");
        Near(spent, 50, "count committed cost exactly once");
        Check(!state.TryUse(CharacterSlot.Identity, Ready(), 0, s => true, out _), "cooldown duplicate rejected");
        Near(spent, 50, "duplicate does not consume");
        state.ApplySkillTest(SkillTestOperation.FillResources, out _);
        Near(gained, 100, "panel fill excluded from combat generation");
    }
    static void ChargeRecovery()
    {
        var d = CharacterRosterData.Definition(CharacterRosterData.Jobs.First(j => j.slug == "dragon-breaker"));
        var s = Build(d); var a = s.Selected(CharacterSlot.OffGcd1);
        Check(s.TryUse(CharacterSlot.OffGcd1, Ready(), 0, x => true, out _), "use first of two charges");
        s.ReduceCooldowns(new[] { a.id }, 2);
        Near(s.Snapshot.skills.First(x => x.id == a.id).recharge, a.cooldown - 2, "cooldown reduction applies with spare charge");
        Check(s.TryUse(CharacterSlot.Gcd1, Ready(), 0, x => true, out _), "GCD starts");
        double gcd = s.Snapshot.gcd;
        s.ReduceCooldowns(new[] { a.id, a.id }, 999);
        Check(s.Snapshot.skills.First(x => x.id == a.id).charges == 2, "recovery respects maximum");
        Near(s.Snapshot.gcd, gcd, "reduction never changes shared GCD");
        s.TryUse(CharacterSlot.OffGcd1, Ready(), 0, x => true, out _);
        s.TryUse(CharacterSlot.OffGcd1, Ready(), 0, x => true, out _);
        s.RefreshSkillCooldowns(new[] { a.id, s.Selected(CharacterSlot.Gcd1).id });
        Check(s.Snapshot.skills.First(x => x.id == a.id).charges == 2 && s.Snapshot.gcd == 0, "explicit full refresh restores all charges and GCD");
        s.ReduceCooldowns(new[] { a.id }, 999);
        Check(s.Snapshot.skills.First(x => x.id == a.id).charges == 2, "no overcharge");
    }
    static void Clock()
    {
        var c = new TrainingAttackClock();
        Check(!c.Start(0, double.NaN, true) && !c.Start(0, 1, true), "invalid clock configuration");
        Check(c.Start(10, 2, true) && c.Telegraphing, "warning starts before first attack");
        Check(!c.Advance(11.49, out _) && c.Advance(11.5, out _), "attack waits 1.5 seconds");
        Check(!c.Advance(12, out bool warn) && warn, "repeat starts next warning");
        Check(c.Advance(1000, out _), "hitch executes one due attack");
        Check(!c.Advance(1000, out _), "same frame never bursts catches up");
        int version = c.Revision; c.Stop();
        Check(c.Revision != version && !c.Advance(10000, out _) && !c.Telegraphing, "stop invalidates pending attack");
        c.Start(0, 2, false); Check(c.Advance(1.5, out _) && !c.Running, "single attack ends");
    }
    static void Metrics()
    {
        var m = new CombatMetricsLedger(); m.Register(1, "practice", 1); m.BeginStage(0);
        m.RecordDamage(1, 1000, 600, false); m.RecordHealing(1, 10, 40); m.Advance(10, true);
        int generation = m.Generation; var before = m.Snapshot(); m.ResetCurrentCounters();
        Check(m.Generation == generation && m.Active && m.Stages.Count == 0, "display reset preserves live generation, no clear record");
        Near(m.Snapshot().Members[0].Damage, 0, "damage reset"); Near(before.Members[0].Damage, 400, "snapshot remains immutable");
        m.RecordDamage(1, 1000, 900, false); Near(m.Snapshot().Members[0].Damage, 100, "live effect can continue recording");
    }
    static void Positions()
    {
        foreach (double angle in new[] { 0, 44.99, 45.01, 90, 134.99, 135.01, 180 })
            Check(CharacterRosterRules.PositionalZoneFromDot((float)Math.Cos(angle * Math.PI / 180)) == (angle < 45 ? 0 : angle > 135 ? 2 : 1), "target ring angle " + angle);
    }
    static void Effects()
    {
        var s = new CharacterEffectState();
        var e = new CharacterTimedEffect { id = "practice-ground", kind = "ground", end = 8, next = 1, interval = 1 };
        s.Put(e); Check(CharacterEffectState.TakeTicks(e, 3) == 3, "ground first three ticks");
        var reconnect = new CharacterEffectState(); Check(reconnect.Restore(s.Data), "effect snapshot");
        Check(CharacterEffectState.TakeTicks(reconnect.Data.effects[0], 3) == 0, "reconnect does not replay ticks");
        Check(CharacterEffectState.TakeTicks(reconnect.Data.effects[0], 100) == 5, "remaining five ticks then end");
        s.Clear(); Check(s.Data.effects.Length == 0 && s.Data.clones == 0 && s.Data.returnEnd == 0, "build reset clears effects/clone/return state");
    }
}
