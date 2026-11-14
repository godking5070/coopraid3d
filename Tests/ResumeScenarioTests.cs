using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using RaidRogue;

// Verifies the chosen test party with production definitions/state/flow and real
// temporary file IO. Does not simulate boss combat, Unity input or live transport.
static class ResumeScenarioTests
{
    public sealed class Scenario { public int version; public string chapterId; public string[] difficulties; public int[] stages; public Slot[] party; }
    public sealed class Slot { public int role, routeIndex, loadoutChoiceIndex; public string jobId; }
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static int checks;
    static void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
    static CharacterDefinition Definition(string id)
    {
        if (id == AegisPaladinData.JobId) return AegisPaladinData.BuildNineKey();
        var job = CharacterRosterData.Find(id);
        if (NineKeyHealerData.Supports(job)) return NineKeyHealerData.Build(job);
        if (NineKeyDamageData.Supports(job)) return NineKeyDamageData.Build(job);
        throw new Exception("Unexpected scenario job: " + id);
    }
    static string BuildKey(CharacterStateSnapshot snapshot) => snapshot.routeId + "/" + snapshot.coreTrait + "/" +
        string.Join(",", snapshot.auxiliaryTraits) + "/" + string.Join(",", snapshot.selections.Where(s => s.slot != CharacterSlot.Ultimate).Select(s => s.slot + "=" + s.skillId));
    static void Main(string[] args)
    {
        var scenario = Json.Deserialize<Scenario>(File.ReadAllText(args[0]));
        string output = args[1]; Directory.CreateDirectory(output);
        Check(scenario.version == 1 && scenario.chapterId == CampaignCatalog.PartOne, "scenario version/chapter");
        Check(scenario.difficulties.SequenceEqual(new[] { CampaignCatalog.Normal, CampaignCatalog.Hard }), "both difficulties");
        Check(scenario.stages.SequenceEqual(CampaignCatalog.Stages(scenario.chapterId)), "all six stages");
        Check(scenario.party.Select(s => s.role).SequenceEqual(new[] { 1, 2, 3, 4 }), "four separate role slots");
        var definitions = scenario.party.Select(s => Definition(s.jobId)).ToArray();
        Check(CharacterDefinitionValidation.Check(definitions).Length == 0, "production definitions valid");
        foreach (string difficulty in scenario.difficulties)
        {
            var states = scenario.party.Select((slot, i) => {
                var definition = definitions[i];
                Check(definition.playable && CharacterSlots.IsNineKey(definition), "live nine-key job");
                Check(CharacterRosterData.Role(definition) == slot.role, "role belongs to job");
                var state = new CharacterRuntimeState(definition);
                Check(state.ChooseRoute(definition.routes[slot.routeIndex].id, true), "choose fixed origin");
                foreach (var rule in definition.loadouts.Where(r => string.IsNullOrEmpty(r.routeId) || r.routeId == state.RouteId))
                    Check(state.ChangeLoadout(rule.id, rule.choices[slot.loadoutChoiceIndex], true), "choose fixed loadout " + rule.id);
                Check(state.Snapshot.auxiliaryTraits.Length == 2 && state.Snapshot.auxiliaryTraits.Distinct().Count() == 2, "two distinct auxiliary traits");
                string key = BuildKey(state.Snapshot);
                state.Reset(CharacterReset.NewRun, true);
                Check(BuildKey(state.Snapshot) == key, "lobby build survives fresh departure");
                state.BeginRun(Guid.NewGuid().ToString("N"), scenario.chapterId);
                Check(!state.Unlocked(CharacterSlot.Ultimate), "old ultimate does not leak into new run");
                return state;
            }).ToArray();
            File.WriteAllText(Path.Combine(output, difficulty + "-departure.json"), Json.Serialize(states.Select(s => s.Snapshot).ToArray()));
            var flow = new RaidRunFlow();
            foreach (int stage in scenario.stages)
            {
                Check(flow.TryPrepare(stage, 6, false), "prepare next stage");
                int revision = flow.Revision;
                Check(flow.TryApproach(revision) && flow.TryArrival(revision) && flow.TryCountdown(revision) && flow.TryStart(revision), "arrival/countdown into combat");
                Check(flow.Resolve(revision, true, 4, 4) == EncounterOutcome.Cleared, "confirmed clear boundary");
                Check(flow.Resolve(revision, true, 4, 4) == EncounterOutcome.None, "duplicate clear ignored");
                foreach (var state in states)
                {
                    string key = BuildKey(state.Snapshot);
                    string run = state.Snapshot.runId;
                    Check(state.ClearedStage(run, scenario.chapterId, stage), "grant clear once");
                    Check(!state.ClearedStage(run, scenario.chapterId, stage), "reject replayed clear");
                    foreach (var rule in state.PendingChoices()) Check(state.ChooseSkill(rule.id, rule.choices[0], state.RewardKey(rule)), "confirm stage reward");
                    state.Reset(CharacterReset.NextStage);
                    Check(BuildKey(state.Snapshot) == key, "next-stage build preserved");
                    Check(state.Unlocked(CharacterSlot.Ultimate) == (stage >= 3), "V unlock boundary");
                    var saved = state.Snapshot;
                    Check(saved.gcd == 0 && saved.castSkillId == "" && saved.castTarget == 0, "safe transient reset");
                    string path = Path.Combine(output, difficulty + "-" + saved.jobId + ".json");
                    Func<string, CampaignFileState> inspect = text => {
                        try { return new CharacterRuntimeState(state.Definition).Restore(Json.Deserialize<CharacterStateSnapshot>(text)) ? CampaignFileState.Valid : CampaignFileState.Corrupt; }
                        catch { return CampaignFileState.Corrupt; }
                    };
                    Check(CampaignStorage.Save(path, Json.Serialize(saved), inspect, out string error), "atomic build state write: " + error);
                    var read = CampaignStorage.Read(path, inspect);
                    Check(read.state == CampaignFileState.Valid, "read persisted state");
                    var restored = new CharacterRuntimeState(state.Definition);
                    Check(restored.Restore(Json.Deserialize<CharacterStateSnapshot>(read.text)), "fresh actor state restore");
                    Check(BuildKey(restored.Snapshot) == key && restored.Snapshot.clearedStage == stage, "restored build and stage match");
                }
                Check(flow.FinishClear(revision, 6), "finish clear presentation");
                if (stage < 6) Check(flow.TryIntermission(revision, 6), "return through intermission");
                else Check(flow.Phase == RaidBattlePhase.Victory && !flow.TryPrepare(7, 6, false), "final victory, no seventh stage");
            }
        }
        Console.WriteLine("PASS: " + checks + " fixed-party departure, six-stage state, duplicate reward and real file restore checks. No boss simulation, Unity input, live LAN/Steam or balance validation. Output: " + output);
    }
}
