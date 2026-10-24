using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using RaidRogue;

static class CampaignSaveTests
{
    static int checks;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = CampaignStorage.MaximumBytes };
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static T Copy<T>(T x) => Json.Deserialize<T>(Json.Serialize(x));
    static readonly string Owner = CampaignIdentity.ForSteam(76561198012345678);
    static CampaignCheckpoint Build(CharacterDefinition job, int route, int finished, int variant = 0)
    {
        string run = Guid.NewGuid().ToString("N");
        var data = new CampaignCheckpoint { owner = Owner, runId = run, savedUtc = DateTimeOffset.UtcNow.ToString("O"), randomState = 123, seed = 123, nextStage = Math.Min(6, finished + 1), attemptsStarted = Math.Min(6, finished + 1), completed = finished == 6 };
        int jobRole = job.legacyRole > 0 ? job.legacyRole : job.id == AegisPaladinData.JobId ? 1 : CharacterRosterData.Find(job.id).Role;
        data.party = Enumerable.Range(1, 4).Select(role => {
            bool local = role == jobRole; var definition = local ? job : CharacterCatalog.Legacy(role);
            var state = new CharacterRuntimeState(definition); state.BeginRun(run, CampaignCatalog.PartOne);
            if (definition.routes.Length > 0) Check(state.ChooseRoute(definition.routes[route].id, true), "choose origin");
            for (int n = 1; n <= finished; n++)
            {
                Check(state.ClearedStage(run, CampaignCatalog.PartOne, n), "first clear");
                foreach (var rule in state.PendingChoices()) Check(state.ChooseSkill(rule.id, rule.choices[variant % rule.choices.Length], state.RewardKey(rule)), "choose extension");
            }
            state.Reset(CharacterReset.NextStage);
            return new CampaignPartySlot { slotId = Guid.NewGuid().ToString("N"), userId = local ? Owner : "", name = local ? "Player" : "AI", role = role, bot = !local, character = state.Snapshot,
                cards = local && finished > 0 ? new[] { new CampaignCardStack { id = CampaignCardIds.Id(6), stacks = Math.Min(finished, 5) } } : Array.Empty<CampaignCardStack>() };
        }).ToArray();
        data.processedStages = Enumerable.Range(1, finished).Select(n => CharacterRewardKey.Stage(run, CampaignCatalog.PartOne, n)).ToArray();
        var human = data.party.Single(p => !p.bot);
        foreach (var p in data.party) p.character.effects = CampaignCardIds.Effects(p.cards);
        data.rewards = Enumerable.Range(1, Math.Min(5, finished)).Select(n => new CampaignRewardRecord { key = CharacterRewardKey.Stage(run, CampaignCatalog.PartOne, n) + "/card/" + human.slotId, slotId = human.slotId,
            offeredIds = new[] { CampaignCardIds.Id(6), CampaignCardIds.Id(0), CampaignCardIds.Id(1) }, selectedId = CampaignCardIds.Id(6) }).ToArray();
        data.clears = Enumerable.Range(1, finished).Select(n => new CampaignMetricRecord { stage = n, seconds = 35, members = data.party.Select(p => new CampaignMetricMember { slotId = p.slotId, name = p.name, jobId = p.character.jobId, role = p.role, bossDamage = 120 }).ToArray() }).ToArray();
        return data;
    }
    static bool Valid(CampaignCheckpoint c) => CampaignValidation.Checkpoint(c, Owner, CharacterCatalog.Find) == null;
    static CampaignFileState Inspect(string s)
    {
        try { var c = Json.Deserialize<CampaignCheckpoint>(s); return c == null ? CampaignFileState.Corrupt : c.version != CampaignCatalog.SaveVersion || c.content != CampaignCatalog.ContentRevision ? CampaignFileState.Incompatible : Valid(c) ? CampaignFileState.Valid : CampaignFileState.Corrupt; }
        catch { return CampaignFileState.Corrupt; }
    }
    static CampaignClearReceipt Receipt(string chapter, int stage, bool eligible = true) => new CampaignClearReceipt { runId = Guid.NewGuid().ToString("N"), chapterId = chapter, difficultyId = "normal", stage = stage, final = stage == CampaignCatalog.StageCount(chapter), eligible = eligible, userId = Owner };
    static void Main()
    {
        Part1SaveTests.Run(Check);
        CharacterCatalog.Register(AegisPaladinData.Build(), out _, true);
        foreach (var job in CharacterRosterData.Jobs) CharacterCatalog.Register(CharacterRosterData.Definition(job), out _, true);
        var definitions = CharacterCatalog.All.Where(d => d.playable).ToArray();
        foreach (var job in definitions)
        foreach (var rule in job.unlocks)
        foreach (string choice in rule.choices)
        {
            var request = new RaidActionRequest { actor = 1, epoch = 6, sequence = uint.MaxValue, lease = uint.MaxValue,
                skill = 11, jobId = job.id, ruleId = rule.id, choiceId = choice, slot = (int)rule.slot,
                rewardKey = CharacterRewardKey.Choice(Guid.NewGuid().ToString("N"), CampaignCatalog.PartOne, rule.afterStage, rule.id) };
            string payload = Json.Serialize(request);
            Check(RaidWire.Decode(RaidWire.Encode(new RaidPacket { Kind = RaidPacketKind.CombatRequest, Text = payload }), out var decoded) && decoded.Text == payload,
                "complete stable selection request survives bounded wire: " + choice);
        }
        string rescuePayload = Json.Serialize(new RaidActionRequest { actor = 1, epoch = 6, sequence = uint.MaxValue, lease = uint.MaxValue,
            skill = 5, target = 4, attemptId = Guid.NewGuid().ToString("N"), deathGeneration = int.MaxValue });
        Check(RaidWire.Decode(RaidWire.Encode(new RaidPacket { Kind = RaidPacketKind.CombatRequest, Text = rescuePayload }), out var rescuePacket) && rescuePacket.Text == rescuePayload, "rescue identity survives wire");
        bool rejectedLarge = false;
        try { RaidWire.Encode(new RaidPacket { Kind = RaidPacketKind.CombatRequest, Text = new string('a', 1001) }); }
        catch (InvalidDataException) { rejectedLarge = true; }
        Check(rejectedLarge, "oversized action rejected");
        foreach (var job in definitions)
        for (int route = 0; route < Math.Max(1, job.routes.Length); route++)
        for (int finished = 0; finished <= 6; finished++)
        for (int option = 0; option < 3; option++)
        {
            var c = Build(job, route, finished, option);
            Check(Valid(c), job.id + " boundary validation " + finished + ": " + CampaignValidation.Checkpoint(c, Owner, CharacterCatalog.Find));
            var restored = Copy(c); Check(Valid(restored), "serialization roundtrip");
            Check(restored.party.Single(p => !p.bot).character.routeId == c.party.Single(p => !p.bot).character.routeId, "route survives");
            foreach (var slot in restored.party)
            {
                var state = new CharacterRuntimeState(CharacterCatalog.Find(slot.character.jobId));
                Check(state.Restore(slot.character), "restore independently of object IDs");
                foreach (var rule in state.Definition.unlocks.Where(r => r.afterStage <= finished)) Check(!state.ChooseSkill(rule.id, rule.choices[0], state.RewardKey(rule)), "no reward replay");
                if (finished > 0) Check(!state.ClearedStage(c.runId, CampaignCatalog.PartOne, 1), "no clear replay");
                state.Reset(CharacterReset.Retry); Check(state.Snapshot.chosenRewardKeys.SequenceEqual(slot.character.chosenRewardKeys), "retry keeps choices");
            }
        }
        var sample = Build(AegisPaladinData.Build(), 1, 2);
        void Reject(Action<CampaignCheckpoint> change, string reason) { var c = Copy(sample); change(c); Check(!Valid(c), reason); }
        Reject(c => c.owner = "offline-local", "cross account");
        Reject(c => c.version = 99, "future version");
        Reject(c => c.content = "different-balance", "unknown content");
        Reject(c => c.chapterId = CampaignCatalog.PartTwo, "missing campaign content");
        Reject(c => c.nextStage = 7, "out of range stage");
        Reject(c => c.irisCoins = 3, "boundary must start with four shared revivals");
        Reject(c => c.totalRevives = -1, "missing/negative cumulative usage");
        Reject(c => c.totalRevives = 100, "impossible cumulative usage");
        Reject(c => c.attemptsStarted = 0, "missing attempt count");
        Reject(c => c.retries = 1, "retry ledger must match failed attempts");
        Reject(c => c.processedStages = new[] { c.processedStages[1] }, "skipped stage");
        Reject(c => c.party[0].slotId = c.party[1].slotId, "duplicate stable slot");
        Reject(c => c.party[0].healthFraction = .5f, "mid-combat health");
        Reject(c => c.party[0].potionCooldown = 3, "mid-combat potion");
        Reject(c => c.party[0].character.castTarget = 987, "ephemeral cast target");
        Reject(c => c.party[0].character.skills[0].recharge = 8, "mid-combat cooldown");
        Reject(c => c.party[0].character.routeId = "bogus", "unknown route");
        Reject(c => c.party[0].role = 4, "job role mismatch");
        Reject(c => c.party[0].character.effects = new[] { new JobBuildEffect { id = "evolution.maximize", stacks = 99 } }, "unearned card effect");
        Reject(c => c.eligible = false, "development checkpoint rejected");
        Reject(c => c.party[0].character.selections = Array.Empty<JobSlotSelection>(), "missing selections");
        Reject(c => c.party[0].character.resources[0].value = float.NaN, "bad resource");
        Reject(c => c.party.Single(p => !p.bot).cards[0].stacks++, "duplicate card grant");
        Reject(c => c.party.Single(p => !p.bot).cards[0].id = "card.aegis-paladin.maximize", "card ledger mismatch");
        Reject(c => c.rewards[0].selectedId = "card.unknown", "unknown card");
        Reject(c => c.rewards = c.rewards.Concat(new[] { c.rewards[0] }).ToArray(), "duplicate reward ledger");
        Reject(c => c.rewards[0].offeredIds = new[] { "card.swift" }, "lost draw");
        Reject(c => c.clears[0].members[0].slotId = "old-network-id", "metric stable IDs only");
        Reject(c => c.clears[0].members = new[] { c.clears[0].members[0], c.clears[0].members[0] }, "duplicate member metrics");
        Reject(c => c.clears[0].members[0].jobId = "wrong-job", "metric job ownership");
        Reject(c => {
            var rule = CharacterCatalog.Find(c.party[0].character.jobId).unlocks.Single(r => r.afterStage == 3);
            c.party[0].character.selections.Single(s => s.slot == rule.slot).skillId = rule.choices[1];
        }, "locked choice cannot silently change");
        Reject(c => { var r = Receipt(CampaignCatalog.PartOne, 1); r.runId = c.runId; c.receipts = new[] { r, r }; }, "duplicate confirmed receipts");
        Reject(c => { var r = Receipt(CampaignCatalog.PartOne, 1, false); r.runId = c.runId; c.receipts = new[] { r }; }, "ineligible checkpoint receipt");
        // Original human slots survive a new lobby with different ephemeral actor IDs.
        var multiplayer = Copy(sample); multiplayer.transport = "steam";
        var guest = multiplayer.party.Single(p => p.role == 2); guest.bot = false;
        guest.userId = CampaignIdentity.ForSteam(76561198012345679); guest.name = "Original guest";
        guest.cards = Copy(multiplayer.party[0].cards); guest.character.effects = CampaignCardIds.Effects(guest.cards);
        multiplayer.rewards = multiplayer.rewards.Concat(multiplayer.rewards.Select(r => new CampaignRewardRecord {
            slotId = guest.slotId, key = r.key.Replace(multiplayer.party[0].slotId, guest.slotId), selectedId = r.selectedId, offeredIds = r.offeredIds }).ToArray()).ToArray();
        Check(Valid(multiplayer), "distinct authenticated saved human builds");
        var pendingReceipt = Receipt(CampaignCatalog.PartOne, 2); pendingReceipt.runId = multiplayer.runId; pendingReceipt.userId = guest.userId;
        multiplayer.receipts = new[] { pendingReceipt };
        Check(Valid(multiplayer), "durable pending delivery belongs to original remote owner");
        var invalidOutbox = Copy(multiplayer); invalidOutbox.receipts = new[] { pendingReceipt, pendingReceipt };
        Check(!Valid(invalidOutbox), "duplicate remote delivery rejected");
        invalidOutbox = Copy(multiplayer); invalidOutbox.receipts[0].eligible = false;
        Check(!Valid(invalidOutbox), "ineligible remote delivery rejected");
        var guestProgress = new CampaignPersonalProgress { owner = guest.userId };
        Check(guestProgress.Record(pendingReceipt) && !guestProgress.Record(Copy(pendingReceipt)), "redelivered pending event grants personal progress once");
        Check(!new CampaignPersonalProgress { owner = Owner }.Record(pendingReceipt), "outbox never grants host another player's clear");
        var senderProgress = new CampaignPersonalProgress { owner = Owner };
        Check(senderProgress.Enqueue(pendingReceipt) && !senderProgress.Enqueue(Copy(pendingReceipt)), "durable delivery queued once independently of checkpoint");
        Check(senderProgress.clears.Length == 0 && CampaignValidation.Personal(senderProgress, Owner), "remote delivery cannot grant host progress");
        var terminalReceipt = Copy(pendingReceipt); terminalReceipt.stage = 6; terminalReceipt.final = true;
        Check(senderProgress.Enqueue(terminalReceipt), "terminal event survives without a resumable run");
        Check(!senderProgress.Acknowledge("steam-1", terminalReceipt.Key) && senderProgress.deliveries.Length == 2, "another account cannot acknowledge a delivery");
        var restartedHost = Copy(senderProgress);
        var recipientProgress = new CampaignPersonalProgress { owner = guest.userId };
        Check(recipientProgress.Record(restartedHost.deliveries[1]) && !recipientProgress.Record(restartedHost.deliveries[1]), "later authenticated session consumes final receipt exactly once");
        Check(restartedHost.Acknowledge(guest.userId, terminalReceipt.Key) && !restartedHost.Acknowledge(guest.userId, terminalReceipt.Key), "disk ACK idempotently retires only that delivery");
        var loadedParty = Copy(multiplayer).party;
        var lobby = new RaidRoster(); var oldPreview = lobby.Join("preview"); lobby.Remove(oldPreview.Id);
        var recovery = new RaidRecoverySlots(new RaidRecoverySettings());
        foreach (var p in loadedParty.Where(p => !p.bot))
        {
            var member = lobby.Join(p.name); member.Role = p.role; member.SteamId = CampaignIdentity.Steam(p.userId);
            Check(member.Id != oldPreview.Id, "new session never uses persisted network ID");
            if (p.userId == Owner) continue;
            var reservation = recovery.Register(member.SteamId, member.Id);
            Check(recovery.Reserve(member.Id, 0), "original participant reserved"); reservation.Deadline = float.MaxValue;
        }
        Check(recovery.BeginRestore(76561198012345680, 100, out _) != null, "newcomer cannot inherit saved build");
        Check(recovery.BeginRestore(CampaignIdentity.Steam(guest.userId), 100, out var returning) == null, "original account reclaims reservation");
        Check(recovery.BeginRestore(CampaignIdentity.Steam(guest.userId), 100, out _) != null, "duplicate authenticated claim rejected");
        Check(returning.Automated && !recovery.Complete(returning.SteamId, returning.Lease - 1, 100), "restore barrier retains AI until correct lease");
        Check(recovery.Complete(returning.SteamId, returning.Lease, 100), "confirmed original owner resumes");
        var recoveredBuild = loadedParty.Single(p => CampaignIdentity.Steam(p.userId) == returning.SteamId);
        Check(recoveredBuild.slotId == guest.slotId && recoveredBuild.cards[0].stacks == guest.cards[0].stacks && recoveredBuild.character.jobId == guest.character.jobId, "authenticated stable slot owns exact saved build");
        var absentProgress = new CampaignPersonalProgress { owner = guest.userId };
        Check(!absentProgress.Record(Receipt(CampaignCatalog.PartOne, 2)), "host personal receipt cannot credit absent AI owner");
        var random = new CampaignRandom(19234);
        for (int i = 0; i < 300; i++)
        {
            random.Next(31); var restart = new CampaignRandom(random.State);
            for (int j = 0; j < 17; j++) Check(random.Next(31) == restart.Next(31), "deterministic reward continuation");
        }
        var progress = new CampaignPersonalProgress { owner = Owner };
        for (int n = 1; n <= 3; n++) Check(progress.Record(Receipt(CampaignCatalog.Prototype, n)), "prototype records");
        Check(!progress.PartTwoUnlocked, "prototype cannot unlock part2");
        for (int n = 1; n <= 6; n++) Check(!progress.Record(Receipt(CampaignCatalog.PartOne, n, false)), "training/dev/mocks excluded");
        var other = Receipt(CampaignCatalog.PartOne, 6); other.userId = "offline-local";
        Check(!progress.Record(other), "another user cannot grant unlock");
        Check(progress.Record(Receipt(CampaignCatalog.PartOne, 6)), "confirmed final");
        Check(!progress.PartTwoUnlocked, "final alone insufficient");
        for (int n = 1; n <= 5; n++)
        { var r = Receipt(CampaignCatalog.PartOne, n); Check(progress.Record(r), "required clear"); Check(!progress.Record(r), "retransmitted receipt idempotent"); }
        Check(progress.PartTwoUnlocked && Copy(progress).PartTwoUnlocked, "permanent independent progress survives IO representation");
        Check(!CampaignCatalog.Available(CampaignCatalog.PartTwo) && CampaignCatalog.PartySize(CampaignCatalog.PartTwo) == 8, "no fabricated part2 content");
        Check(CampaignStorage.DirectoryFor("root", Owner) != CampaignStorage.DirectoryFor("root", "offline-local"), "offline partition");
        Check(CampaignStorage.DirectoryFor("root", Owner) != CampaignStorage.DirectoryFor("root", CampaignIdentity.ForSteam(76561198012345679)), "account switch partition");
        Check(!CampaignIdentity.Valid("../../other") && !CampaignIdentity.Valid("steam-0") && !CampaignIdentity.Valid("steam-0001"), "identity path traversal");
        foreach (int index in Enumerable.Range(0, 31)) Check(CampaignCardIds.Index(CampaignCardIds.Id(index)) == index, "stable card id mapping");
        var flow = new RaidRunFlow(); Check(flow.TryResume(3, 3), "resume boundary enters loading");
        Check(!flow.TryResume(2, 3) && !flow.TryPrepare(2, 3, false), "no double resume or premature stage advance");
        Check(flow.TryCountdown(flow.Revision) && flow.TryStart(flow.Revision), "resume countdown uses existing flow");
        Check(!flow.TryResume(1, 3), "no combat load");
        foreach (var kind in new[] { RaidPacketKind.CampaignClear, RaidPacketKind.CampaignAck })
        { string text = "verified-owner/run/main/1"; Check(RaidWire.Decode(RaidWire.Encode(new RaidPacket { Kind = kind, Text = text }), out var decoded) && decoded.Kind == kind && decoded.Text == text, "receipt wire round trip"); }
        foreach (string chapter in new[] { CampaignCatalog.Prototype, CampaignCatalog.PartOne, CampaignCatalog.PartTwo })
        {
            var r = Receipt(chapter, CampaignCatalog.StageCount(chapter));
            // Fields only, as Unity JsonUtility serializes. Exercise the actual bounded packet.
            string text = Json.Serialize(new { r.runId, r.chapterId, r.difficultyId, r.userId, r.stage, r.final, r.eligible });
            Check(RaidWire.Decode(RaidWire.Encode(new RaidPacket { Kind = RaidPacketKind.CampaignClear, Text = text }), out var packet) && packet.Text == text, "full clear receipt fits protocol budget");
        }
        string directory = Path.Combine(Path.GetTempPath(), "RaidRogue-Campaign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "checkpoint.json");
        string deliveryPath = Path.Combine(directory, "progress.json");
        CampaignFileState InspectProgress(string json)
        {
            try { return CampaignValidation.Personal(Json.Deserialize<CampaignPersonalProgress>(json), Owner) ? CampaignFileState.Valid : CampaignFileState.Corrupt; }
            catch { return CampaignFileState.Corrupt; }
        }
        Check(CampaignStorage.Save(deliveryPath, Json.Serialize(senderProgress), InspectProgress, out _), "host delivery journal written atomically");
        var deliveryReload = Json.Deserialize<CampaignPersonalProgress>(CampaignStorage.Read(deliveryPath, InspectProgress).text);
        Check(deliveryReload.deliveries.Length == 2 && deliveryReload.clears.Length == 0, "disk delivery reload never replays host clears");
        string boundaries = Path.Combine(directory, "six-boundaries.json");
        for (int next = 1; next <= 6; next++)
        {
            var boundary = Build(AegisPaladinData.Build(), 0, next - 1);
            boundary.attemptsStarted += 2; boundary.totalRevives = 5;
            string bytes = Json.Serialize(boundary);
            Check(CampaignStorage.Save(boundaries, bytes, Inspect, out _), "write stage-start boundary " + next);
            var read = Json.Deserialize<CampaignCheckpoint>(CampaignStorage.Read(boundaries, Inspect).text);
            Check(read.nextStage == next && !read.completed && read.totalRevives == 5, "disk stage/usage restoration " + next);
            var live = new CharacterRuntimeState(CharacterCatalog.Find(read.party[0].character.jobId));
            Check(live.Restore(read.party[0].character), "disk build restore " + next);
            live.Gain(AegisPaladinData.Holy, 100); live.Reset(CharacterReset.Retry);
            var resumedAttempt = new IrisRescueState(); resumedAttempt.BeginAttempt(read.totalRevives, read.attemptsStarted);
            resumedAttempt.TryRescue(resumedAttempt.AttemptId, "runtime-death", () => true, () => true);
            Check(File.ReadAllText(boundaries) == bytes && read.irisCoins == 4 && resumedAttempt.Coins == 3, "combat/AI/retry activity never overwrites disk boundary " + next);
        }
        var completed = Build(AegisPaladinData.Build(), 0, 6);
        Check(CampaignStorage.Save(boundaries, Json.Serialize(completed), Inspect, out _) &&
            Json.Deserialize<CampaignCheckpoint>(CampaignStorage.Read(boundaries, Inspect).text).completed, "terminal completion survives disk IO");
        Check(CampaignStorage.Save(boundaries, Json.Serialize(Build(AegisPaladinData.Build(), 0, 0)), Inspect, out _) &&
            Json.Deserialize<CampaignPersonalProgress>(CampaignStorage.Read(deliveryPath, InspectProgress).text).deliveries.Length == 2, "new run cannot erase undelivered terminal confirmations");
        string first = Json.Serialize(Build(AegisPaladinData.Build(), 0, 0)), second = Json.Serialize(sample);
        Check(CampaignStorage.Read(path, Inspect).state == CampaignFileState.Missing, "missing save normal");
        Check(CampaignStorage.Save(path, first, Inspect, out _), "first atomic write");
        Check(CampaignStorage.Save(path, second, Inspect, out _), "replacement write");
        Check(File.ReadAllText(path + ".bak") == first && File.ReadAllText(path) == second, "normal backup and exact bytes");
        var checkpointReload = Json.Deserialize<CampaignCheckpoint>(CampaignStorage.Read(path, Inspect).text);
        var reloadedActor = new CharacterRuntimeState(CharacterCatalog.Find(checkpointReload.party[0].character.jobId));
        Check(reloadedActor.Restore(checkpointReload.party[0].character), "on-disk boundary restores actor");
        Check(!reloadedActor.ClearedStage(checkpointReload.runId, checkpointReload.chapterId, 1), "disk resume does not grant repeated clear");
        var selectedRule = reloadedActor.Definition.unlocks.First(r => r.afterStage == 1);
        Check(!reloadedActor.ChooseSkill(selectedRule.id, selectedRule.choices[0], reloadedActor.RewardKey(selectedRule)), "disk resume cannot reroll chosen skill");
        Check(reloadedActor.PendingChoices().Length == 0 && checkpointReload.nextStage == 3 && checkpointReload.party[0].cards[0].stacks == 2, "boundary has all rewards and exact next stage");
        var future = Copy(sample); future.version = 99; File.WriteAllText(path, Json.Serialize(future));
        Check(CampaignStorage.Read(path, Inspect).state == CampaignFileState.Incompatible, "future primary never downgraded to backup");
        Check(!CampaignStorage.Save(path, second, Inspect, out _) && Inspect(File.ReadAllText(path)) == CampaignFileState.Incompatible, "future file never overwritten");
        var prototype = Copy(sample); prototype.version = 1; prototype.content = "prototype-3-roster12-v1"; prototype.chapterId = CampaignCatalog.Prototype;
        string prototypeBytes = Json.Serialize(prototype); File.WriteAllText(path, prototypeBytes);
        Check(CampaignStorage.Read(path, Inspect).state == CampaignFileState.Incompatible && !CampaignStorage.Save(path, second, Inspect, out _) && File.ReadAllText(path) == prototypeBytes, "prototype v1 never becomes part1 or gets overwritten");
        File.WriteAllText(path, second); File.WriteAllText(path + ".bak", Json.Serialize(future));
        Check(!CampaignStorage.Save(path, first, Inspect, out _) && File.ReadAllText(path) == second && Inspect(File.ReadAllText(path + ".bak")) == CampaignFileState.Incompatible, "future backup never overwritten");
        File.WriteAllText(path + ".bak", first);
        File.WriteAllText(path, "partial{");
        Check(CampaignStorage.Read(path, Inspect).state == CampaignFileState.Backup && CampaignStorage.Read(path, Inspect).text == first, "torn file fallback");
        Check(CampaignStorage.Save(path, second, Inspect, out _) && File.ReadAllText(path + ".bak") == first, "repair preserves known good backup");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(!CampaignStorage.Save(path, first, Inspect, out _), "write failure reported");
        Check(File.ReadAllText(path) == second, "failed write preserves previous bytes");
        Check(Directory.GetFiles(directory, "*.tmp-*").Length == 0, "temporary files cleaned");
        File.WriteAllText(path, "broken"); File.WriteAllText(path + ".bak", "broken");
        Check(!CampaignStorage.Read(path, Inspect).Writable && !CampaignStorage.Save(path, first, Inspect, out _), "corruption not silently defaulted over");
        Check(CampaignStorage.Delete(path, out _) && !File.Exists(path) && !File.Exists(path + ".bak"), "delete selected scope primary and backup");
        Console.WriteLine("PASS campaign save: " + checks + " assertions (real atomic file IO + pure state; no Unity/Steam execution). Files: " + directory);
    }
}
