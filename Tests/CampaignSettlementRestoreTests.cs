using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using RaidRogue;

// Runs the production restore, card-journal and settlement-capture methods.
// Unity scene objects are deterministic stubs; rules, counters and validation are real.
static class JsonUtility
{
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = CampaignStorage.MaximumBytes };
    public static string ToJson(object value, bool pretty = false) => Json.Serialize(value);
    public static T FromJson<T>(string text) => Json.Deserialize<T>(text);
    public static T Copy<T>(T value) => FromJson<T>(ToJson(value));
}
sealed class GameObject
{
    public CharacterActor actor; public PlayerController player; public PartyBotAI bot;
    public T GetComponent<T>() where T : class => typeof(T) == typeof(PlayerController) ? player as T : typeof(T) == typeof(PartyBotAI) ? bot as T : actor as T;
}
sealed class Transform
{
    public readonly GameObject gameObject = new GameObject();
    public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
}
sealed class CharacterActor
{
    public CharacterRuntimeState state;
    public CharacterStateSnapshot Snapshot => state.Snapshot;
    public static CharacterActor Ensure(GameObject value) => value.actor;
}
sealed class PlayerController
{
    public Transform transform; public float HpRatio;
    public RoguelikeCardManager cardManager;
}
sealed class PartyBotAI { public float currentHp, maxHp = 1000; }
sealed class RoguelikeCardManager
{
    public CampaignCardStack[] cards = Array.Empty<CampaignCardStack>();
    public int restoredOffers, freshOffers; public Action chosen;
    public CampaignCardStack[] CaptureCampaignCards() => cards;
    public void OpenStageClearCardReward(int stage, Action callback) { freshOffers++; chosen = callback; }
    public void ShowRestoredSingleOffer(int[] offer, Action callback) { restoredOffers++; chosen = callback; }
}
sealed class RaidLobbyRoom { public int hidden; public void HideForDeparture() { hidden++; } }
sealed class RaidApproachField
{
    public static readonly RaidApproachField Instance = new RaidApproachField();
    public static RaidApproachField Ensure(StageManager stage) => Instance;
    public void Begin(bool retry) { } public void SummonParty() { }
    public void SetBossVisible(bool visible) { } public void SetBossCombatReady(bool ready) { }
}
static class RaidCombatAuthority { public static bool Active; }
static class Time { public static float unscaledTime = 12; }
static class Mathf { public static float Clamp01(float value) => Math.Max(0, Math.Min(1, value)); }
sealed class CombatMetricsManager
{
    public static CombatMetricsManager Instance = new CombatMetricsManager();
    public readonly CombatMetricsLedger Ledger = new CombatMetricsLedger();
}
sealed class StageManager
{
    public static StageManager Instance;
    public readonly RaidRunFlow Progress = new RaidRunFlow();
    public readonly IrisRescueState Iris = new IrisRescueState();
    public readonly RaidFlowSettings FlowSettings = new RaidFlowSettings();
    public readonly RaidLobbyRoom room = new RaidLobbyRoom();
    public int currentStage = 1, maxStage = 6, _singleRewardRevision = -1;
    public bool _singleCardChosen, _hasStartedEncounter, _isStageTransitioning, IsPreparing, IsWiped;
    public float _singleRewardDeadline, TotalPlayTime = 254.275f;
    public PlayerController player; public RoguelikeCardManager cardManager;
    T GetComponent<T>() where T : class => room as T;
    void UpdateStageHeaderUI() { }
    /* ACTUAL_SETTLEMENT_RESTORE */
    /* ACTUAL_CARD_CALLBACK */
}
sealed class CampaignRuntime
{
    public static CampaignRuntime Instance;
    public static string CurrentOwner = "offline-local";
    public CampaignSettlement Settlement; public CampaignCheckpoint Resume, Saved, _pending;
    public string Owner; public bool Authority = true, _tainted, _settlementDirty;
    public string Notice, validation;
    public string RunPath, SettlementPath, _personalError = "", _lastBoundaryError;
    public bool _personalDirty; public CampaignFileRead RunFile;
    public CampaignCheckpoint _working;
    public readonly Dictionary<Transform, CampaignPartySlot> _slots = new Dictionary<Transform, CampaignPartySlot>();
    public readonly Dictionary<int, string> _metricSlots = new Dictionary<int, string>();
    public int[] SettlementOffer(Transform member) => Settlement.offers.Single(o => o.slotId == _slots[member].slotId).cardIds.Select(CampaignCardIds.Index).ToArray();
    public void SaveNow() => SaveSettlementSnapshot(true);
    void RetrySettlementSave() { if (Settlement == null) return; validation = CampaignValidation.Settlement(Settlement, Owner, CharacterCatalog.Find); _settlementDirty = validation != null; }
    void SavePersonal() { }
    void ReportBoundaryError(string message) { Notice = _lastBoundaryError = message; }
    CampaignFileState InspectRun(string text) => CampaignValidation.Checkpoint(JsonUtility.FromJson<CampaignCheckpoint>(text), Owner, CharacterCatalog.Find) == null ? CampaignFileState.Valid : CampaignFileState.Corrupt;
    /* ACTUAL_BOUNDARY_RETRY */
    /* ACTUAL_SETTLEMENT_CLEAR */
    /* ACTUAL_SAVE_LABEL */
    /* ACTUAL_CARD_JOURNAL */
    /* ACTUAL_SETTLEMENT_CAPTURE */
    /* ACTUAL_METRIC_CAPTURE */
}
static class CampaignSettlementRestoreTests
{
    static int checks;
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static CampaignSettlement Sample()
    {
        string runId = Guid.NewGuid().ToString("N");
        var run = new CampaignCheckpoint { owner = "offline-local", runId = runId, transport = "single", savedUtc = DateTimeOffset.UtcNow.ToString("O"),
            nextStage = 1, randomState = 17, seed = 17, totalRevives = 1, attemptsStarted = 1, runSeconds = 254.275f };
        run.party = Enumerable.Range(1, 4).Select(role => {
            var definition = role == 1 ? CharacterCatalog.Find(AegisPaladinData.JobId) : CharacterCatalog.Legacy(role);
            var state = new CharacterRuntimeState(definition); state.BeginRun(runId, CampaignCatalog.PartOne);
            if (definition.routes.Length > 0) state.ChooseRoute(definition.routes[0].id, true);
            state.ClearedStage(runId, CampaignCatalog.PartOne, 1);
            foreach (var rule in state.PendingChoices()) state.ChooseSkill(rule.id, rule.choices[0], state.RewardKey(rule));
            return new CampaignPartySlot { slotId = Guid.NewGuid().ToString("N"), role = role, bot = role != 1, userId = role == 1 ? run.owner : "",
                name = role == 1 ? "Player" : "AI", character = state.Snapshot, healthFraction = .62f };
        }).ToArray();
        run.processedStages = new[] { CharacterRewardKey.Stage(runId, CampaignCatalog.PartOne, 1) };
        run.clears = new[] { new CampaignMetricRecord { stage = 1, seconds = 254.275, members = run.party.Select(p => new CampaignMetricMember {
            slotId = p.slotId, role = p.role, jobId = p.character.jobId, name = p.name, deaths = p.bot ? 0 : 1 }).ToArray() } };
        return new CampaignSettlement { owner = run.owner, runId = runId, stage = 1, run = run, presentationComplete = true, savedUtc = run.savedUtc,
            offers = new[] { new CampaignSettlementOffer { slotId = run.party[0].slotId, cardIds = new[] { "card.vampiric", "card.thorn", "card.swift" } } } };
    }
    static StageManager Setup(CampaignSettlement saved)
    {
        var stage = new StageManager(); StageManager.Instance = stage;
        var campaign = new CampaignRuntime { Settlement = JsonUtility.Copy(saved), Resume = JsonUtility.Copy(saved.run), _working = JsonUtility.Copy(saved.run), Owner = saved.owner };
        CampaignRuntime.Instance = campaign; CombatMetricsManager.Instance = new CombatMetricsManager();
        int metric = 10;
        foreach (var slot in campaign.Resume.party)
        {
            var member = new Transform(); var state = new CharacterRuntimeState(CharacterCatalog.Find(slot.character.jobId));
            Check(state.Restore(slot.character), "fixture restores saved character"); member.gameObject.actor = new CharacterActor { state = state };
            if (slot.bot) member.gameObject.bot = new PartyBotAI { currentHp = slot.healthFraction * 1000 };
            else { var cards = new RoguelikeCardManager { cards = slot.cards }; member.gameObject.player = new PlayerController { transform = member, HpRatio = slot.healthFraction, cardManager = cards }; stage.player = member.gameObject.player; stage.cardManager = cards; }
            campaign._slots[member] = slot; campaign._metricSlots[++metric] = slot.slotId;
        }
        var clears = saved.run.clears.Select(r => new CombatEncounterResult(r.stage, r.seconds, r.members.Select(m => new CombatMemberRecord {
            Id = campaign._metricSlots.Single(p => p.Value == m.slotId).Key, Name = m.name, JobId = m.jobId, Role = m.role, Deaths = m.deaths })));
        CombatMetricsManager.Instance.Ledger.ReplaceNetworkSnapshot(new CombatEncounterResult(1, 0, Array.Empty<CombatMemberRecord>()), clears, Array.Empty<CombatEncounterResult>(), 0);
        return stage;
    }
    static void BoundaryNoticeChecks(CampaignCheckpoint next, CampaignSettlement previous)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RaidRogue-BoundaryNotice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var campaign = new CampaignRuntime { Owner = next.owner, _pending = JsonUtility.Copy(next), Settlement = JsonUtility.Copy(previous),
            RunPath = Path.Combine(folder, "checkpoint.json"), SettlementPath = Path.Combine(folder, "settlement.json") };
        try
        {
            File.WriteAllText(campaign.SettlementPath, "pending journal");
            campaign.RetrySave();
            Check(campaign.Saved?.nextStage == 2 && campaign._pending == null && campaign.Settlement == null, "actual retry commits next boundary and retires pending rewards");
            Check(campaign.Notice.Contains("Stage 2 시작") && !campaign.Notice.Contains("정산 대기"), "success notice describes committed boundary after settlement cleanup");
            Check(!File.Exists(campaign.SettlementPath) && JsonUtility.FromJson<CampaignCheckpoint>(File.ReadAllText(campaign.RunPath)).nextStage == 2, "notice agrees with actual checkpoint and retired journal on disk");

            campaign.Settlement = JsonUtility.Copy(previous); campaign._pending = JsonUtility.Copy(next);
            File.WriteAllText(campaign.SettlementPath, "pending journal");
            using (var locked = new FileStream(campaign.SettlementPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                campaign.RetrySave();
                Check(campaign.Saved.nextStage == 2 && campaign.Settlement != null && campaign.Notice.Contains("삭제하지 못했습니다"), "cleanup IO failure is retained instead of overwritten by a success notice");
            }
            campaign._pending = JsonUtility.Copy(next); campaign.RetrySave();
            Check(campaign.Settlement == null && campaign.Notice.Contains("Stage 2 시작"), "retry clears old cleanup failure and displays the actual committed boundary");

            var completed = JsonUtility.Copy(next); completed.nextStage = completed.attemptsStarted = 6; completed.completed = true;
            foreach (var slot in completed.party)
            {
                var state = new CharacterRuntimeState(CharacterCatalog.Find(slot.character.jobId));
                Check(state.Restore(slot.character), "final-boundary fixture restores the previous job state");
                for (int stage = 2; stage <= 6; stage++)
                {
                    state.ClearedStage(completed.runId, completed.chapterId, stage);
                    foreach (var rule in state.PendingChoices()) state.ChooseSkill(rule.id, rule.choices[0], state.RewardKey(rule));
                }
                state.Reset(CharacterReset.NextStage); slot.character = state.Snapshot;
                slot.cards = slot.bot ? Array.Empty<CampaignCardStack>() : new[] { new CampaignCardStack { id = "card.swift", stacks = 5 } };
                slot.character.effects = CampaignCardIds.Effects(slot.cards);
            }
            completed.processedStages = Enumerable.Range(1, 6).Select(n => CharacterRewardKey.Stage(completed.runId, completed.chapterId, n)).ToArray();
            var human = completed.party.Single(p => !p.bot);
            completed.rewards = Enumerable.Range(1, 5).Select(n => new CampaignRewardRecord { key = CharacterRewardKey.Stage(completed.runId, completed.chapterId, n) + "/card/" + human.slotId,
                slotId = human.slotId, selectedId = "card.swift", offeredIds = new[] { "card.swift", "card.thorn", "card.vampiric" } }).ToArray();
            completed.clears = Enumerable.Range(1, 6).Select(n => { var record = JsonUtility.Copy(next.clears[0]); record.stage = n; if (n > 1) foreach (var member in record.members) member.deaths = 0; return record; }).ToArray();
            completed.runSeconds = (float)completed.clears.Sum(r => r.seconds);
            Check(CampaignValidation.Checkpoint(completed, completed.owner, CharacterCatalog.Find) == null, "final-boundary fixture satisfies real completed-run validation");
            var finalJournal = JsonUtility.Copy(previous); finalJournal.stage = 6; finalJournal.offers = Array.Empty<CampaignSettlementOffer>();
            finalJournal.run = JsonUtility.Copy(completed); finalJournal.run.completed = false;
            Check(CampaignValidation.Settlement(finalJournal, completed.owner, CharacterCatalog.Find) == null, "final-boundary fixture retains a valid pre-completion settlement");
            campaign.Settlement = finalJournal; campaign._pending = completed; File.WriteAllText(campaign.SettlementPath, "final clear journal"); campaign.RetrySave();
            Check(campaign.Saved.completed && campaign.Settlement == null && campaign.Notice.Contains(" · 완료") && !campaign.Notice.Contains("정산 대기"), "final clear commits completion and displays completion after retiring the journal");
            Check(JsonUtility.FromJson<CampaignCheckpoint>(File.ReadAllText(campaign.RunPath)).completed && !File.Exists(campaign.SettlementPath), "completed-run notice agrees with durable completion and journal cleanup");
        }
        finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }
    static void Main()
    {
        CharacterCatalog.Register(AegisPaladinData.BuildNineKey(), out _, true);
        var saved = Sample(); Check(CampaignValidation.Settlement(saved, saved.owner, CharacterCatalog.Find) == null, "saved completed-attempt fixture valid");
        var stage = Setup(saved); var campaign = CampaignRuntime.Instance;
        Check(stage.RestoreSettlementFromLobby(), "actual restore method resumes pending rewards");
        campaign.SaveNow(); Check(campaign.validation == null, "actual capture retains valid pending settlement before selection: " + campaign.validation);
        Check(stage.Progress.Phase == RaidBattlePhase.Reward && stage.Iris.AttemptsStarted == 1 && stage.Iris.TotalUsed == 1 && stage.Iris.AttemptId == "", "reward restore preserves completed history without starting combat");
        Check(stage.cardManager.restoredOffers == 1 && stage.cardManager.freshOffers == 0, "saved card offer reused");
        var human = campaign._slots[stage.player.transform]; stage.cardManager.cards = new[] { new CampaignCardStack { id = "card.swift", stacks = 1 } };
        var actor = stage.player.transform.gameObject.actor; var selectedState = actor.Snapshot; selectedState.effects = CampaignCardIds.Effects(stage.cardManager.cards);
        Check(actor.state.Restore(selectedState), "selected card effect accepted");
        campaign.CardChosen(stage.player, CampaignCardIds.Index("card.swift"), saved.offers[0].cardIds.Select(CampaignCardIds.Index)); stage.cardManager.chosen();
        campaign.SaveNow(); Check(stage._singleCardChosen && campaign.validation == null && !campaign._settlementDirty, "actual card journal and capture save valid after Swift selection");
        Check(campaign.Settlement.run.totalRevives == 1 && campaign.Settlement.run.attemptsStarted == 1 && campaign.Settlement.run.rewards.Length == 1, "card selection preserves exact cumulative history and one reward");
        Check(!stage.RestoreSettlementFromLobby() && stage.cardManager.restoredOffers == 1 && stage.Iris.AttemptsStarted == 1, "duplicate callback cannot replay restore");
        Check(stage.Progress.TryIntermission(stage.Progress.Revision, 6) && stage.Progress.TryPrepare(2, 6, false), "next stage preparation admitted"); stage.Iris.BeginAttempt();
        Check(stage.Iris.AttemptsStarted == 2 && stage.Iris.TotalUsed == 1 && stage.Iris.AttemptId.Length > 0, "only actual next attempt increments cumulative count");
        var next = JsonUtility.Copy(campaign.Settlement.run); next.nextStage = 2; next.attemptsStarted = stage.Iris.AttemptsStarted; next.totalRevives = stage.Iris.TotalUsed;
        foreach (var slot in next.party)
        {
            var boundary = new CharacterRuntimeState(CharacterCatalog.Find(slot.character.jobId)); Check(boundary.Restore(slot.character), "next stage build restores");
            boundary.Reset(CharacterReset.NextStage); slot.character = boundary.Snapshot; slot.healthFraction = 1; slot.potionCooldown = 0;
        }
        Check(CampaignValidation.Checkpoint(next, next.owner, CharacterCatalog.Find) == null, "selected reward and restored history admit valid Stage 2 checkpoint");
        BoundaryNoticeChecks(next, campaign.Settlement);
        var capped = JsonUtility.Copy(next); capped.attemptsStarted = IrisRescueState.MaximumRecordedAttempts;
        Check(CampaignValidation.Checkpoint(capped, capped.owner, CharacterCatalog.Find) == null, "checkpoint accepts recorded attempt limit");
        capped.attemptsStarted++; Check(CampaignValidation.Checkpoint(capped, capped.owner, CharacterCatalog.Find) != null, "checkpoint rejects history beyond recorded limit");
        var cappedSettlement = JsonUtility.Copy(saved); cappedSettlement.run.attemptsStarted = IrisRescueState.MaximumRecordedAttempts;
        Check(CampaignValidation.Settlement(cappedSettlement, cappedSettlement.owner, CharacterCatalog.Find) == null, "settlement accepts same recorded attempt limit");
        cappedSettlement.run.attemptsStarted++; Check(CampaignValidation.Settlement(cappedSettlement, cappedSettlement.owner, CharacterCatalog.Find) != null, "settlement rejects history beyond same limit");
        Check(!stage.RestoreSettlementFromLobby() && stage.Iris.AttemptsStarted == 2 && stage.Iris.TotalUsed == 1, "stale reward callback cannot rewind active history");

        stage = Setup(saved); CampaignRuntime.Instance.Resume.attemptsStarted = 0;
        Check(!stage.RestoreSettlementFromLobby() && stage.Progress.Phase == RaidBattlePhase.Lobby && stage.Iris.AttemptsStarted == 0, "invalid history changes neither flow nor counters");
        stage = Setup(saved); stage.Iris.BeginAttempt(0, 0); string active = stage.Iris.AttemptId;
        Check(stage.Iris.TryRescue(active, "death", () => true, () => {
            Check(!stage.RestoreSettlementFromLobby() && stage.Progress.Phase == RaidBattlePhase.Lobby && stage.Iris.Pending == 1, "pending revival blocks restore before phase changes"); return true;
        }), "pending callback fixture commits one revival");
        Check(stage.Iris.TotalUsed == 1 && stage.Iris.AttemptsStarted == 1 && stage.Iris.AttemptId == active, "rejected pending restore preserves original transaction");
        var history = new IrisRescueState(); history.BeginAttempt(0, 0); active = history.AttemptId;
        foreach (var invalid in new[] { (-1, 1), (0, 0), (0, -1), (5, 1) })
            Check(!history.TryRestoreCompletedHistory(invalid.Item1, invalid.Item2) && history.AttemptsStarted == 1 && history.TotalUsed == 0 && history.AttemptId == active, "invalid history rejected atomically");
        Check(history.TryRestoreCompletedHistory(1, 1) && history.TryRestoreCompletedHistory(1, 1) && history.AttemptsStarted == 1 && history.Coins == 4, "history replay idempotent");
        Check(!history.TryRescue(active, "late", () => true, () => true) && !history.TryRescue("", "late", () => true, () => true), "completed history does not authorize stale or empty attempt rescue");
        Check(!history.TryRestoreCompletedHistory(int.MaxValue, int.MaxValue) && history.AttemptsStarted == 1 && history.TotalUsed == 1 && history.AttemptId == "", "overflow-scale history rejected atomically");
        Check(!history.TryRestoreCompletedHistory(0, IrisRescueState.MaximumRecordedAttempts + 1) && history.AttemptsStarted == 1, "history rejects attempts beyond recorded limit");
        Check(history.TryRestoreCompletedHistory(4 * IrisRescueState.MaximumRecordedAttempts, IrisRescueState.MaximumRecordedAttempts), "history accepts exact recorded limit and budget");
        history.BeginAttempt(); Check(history.AttemptsStarted == IrisRescueState.MaximumRecordedAttempts + 1 && history.TotalUsed == 4 * IrisRescueState.MaximumRecordedAttempts, "subsequent attempt increment cannot overflow");
        Console.WriteLine("PASS: " + checks + " actual settlement restore/capture, boundary save and Iris history checks; no Unity, input, or user profile writes.");
    }
}
