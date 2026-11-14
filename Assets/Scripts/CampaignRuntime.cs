using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    // Persistence adapter on the existing stage/session owner. Never simulates combat or rewards.
    public sealed class CampaignRuntime : MonoBehaviour
    {
        public static CampaignRuntime Instance { get; private set; }
        public static string CurrentOwner => CampaignIdentity.ForSteam(RaidSteamRuntime.Instance?.UserId ?? 0);
        public string Owner { get; private set; }
        public string Notice { get; private set; } = "";
        public CampaignCheckpoint Saved { get; private set; }
        public CampaignCheckpoint Resume { get; private set; }
        public CampaignSettlement Settlement { get; private set; }
        public bool ResumingSettlement => Resume != null && Settlement != null;
        public CampaignPersonalProgress Personal { get; private set; }
        public CampaignFileRead RunFile { get; private set; }
        public CampaignFileRead SettlementFile { get; private set; }
        public bool BuildLocked => Resume != null;
        public bool Restoring { get; private set; }
        public bool RunStarted => _working != null;
        private string _selectedDifficulty = CampaignCatalog.Normal;
        public string SelectedDifficulty => _selectedDifficulty;
        public bool CanSelectDifficulty => _working == null || _working.completed && !HasSaveFailure;
        public string DifficultyId => _working?.difficultyId ?? Resume?.difficultyId ?? _selectedDifficulty;
        public void SelectDifficulty(string difficulty)
        {
            if (CanSelectDifficulty && CampaignCatalog.SupportedDifficulty(difficulty)) _selectedDifficulty = difficulty;
        }
        public bool Eligible => !_tainted;
        public bool HasSaveFailure => _pending != null || _personalDirty || _settlementDirty;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Read only for the diagnostic snapshot. The archive never calls a campaign save method.
        internal CampaignCheckpoint ReportWorking => _working;
        internal CampaignPartySlot ReportSlot(Transform member) => member != null && _slots.TryGetValue(member, out var slot) ? slot : null;
#endif
        public string SaveFailureDetail => _lastBoundaryError ?? Notice;
        public bool TrainingUnlocked => Owner == CurrentOwner && Personal?.TrainingUnlocked == true;
        private string CampaignRoot
        {
            get
            {
#if UNITY_EDITOR
                string testProfile = UnityEditor.SessionState.GetString("GuardianNormal.TestProfile", "");
                if (testProfile.Length > 0) return CampaignStorage.RootFor(Application.persistentDataPath, new[] { CampaignStorage.TestProfileArgument + testProfile }, true);
#endif
                return CampaignStorage.RootFor(Application.persistentDataPath, Environment.GetCommandLineArgs(), Debug.isDebugBuild);
            }
        }
        public string RunPath => Path.Combine(CampaignStorage.DirectoryFor(CampaignRoot, Owner), "checkpoint.json");
        public string SettlementPath => Path.Combine(CampaignStorage.DirectoryFor(CampaignRoot, Owner), "settlement.json");
        private string PersonalPath => Path.Combine(CampaignStorage.DirectoryFor(CampaignRoot, Owner), "progress.json");
        private CampaignCheckpoint _working, _pending;
        private CampaignRandom _random;
        private bool _tainted, _personalDirty;
        private bool _settlementDirty;
        private string _personalError = "";
        private string _lastBoundaryError;
        private readonly Dictionary<Transform, CampaignPartySlot> _slots = new Dictionary<Transform, CampaignPartySlot>();
        private readonly Dictionary<int, string> _metricSlots = new Dictionary<int, string>();
        private float _retryAt;
        public static void Ensure(StageManager stage) { if (Instance == null) stage.gameObject.AddComponent<CampaignRuntime>(); }
        private void Awake() { Instance = this; ReloadAccount(); }
        private void OnDestroy() { if (Instance == this) Instance = null; }
        public void ReloadAccount()
        {
            if (Owner != null && Owner != CurrentOwner)
            {
                _working = _pending = Resume = null; Settlement = null; _settlementDirty = false; _slots.Clear(); _metricSlots.Clear();
                _personalDirty = _tainted = Restoring = false; _personalError = ""; _lastBoundaryError = null;
            }
            Owner = CurrentOwner;
            var personal = CampaignStorage.Read(PersonalPath, InspectPersonal);
            Personal = new CampaignPersonalProgress { owner = Owner };
            // Explicit initialization preserves optional fields in supported older personal v1
            // files; Unity's FromJson does not reliably run constructors for missing fields.
            if (personal.text != null) JsonUtility.FromJsonOverwrite(personal.text, Personal);
            RunFile = CampaignStorage.Read(RunPath, InspectRun);
            Saved = RunFile.text != null ? JsonUtility.FromJson<CampaignCheckpoint>(RunFile.text) : null;
            var settlementFile = CampaignStorage.Read(SettlementPath, InspectSettlement);
            SettlementFile = settlementFile;
            Settlement = settlementFile.text != null ? JsonUtility.FromJson<CampaignSettlement>(settlementFile.text) : null;
            if (Settlement != null && Saved != null && (Saved.runId != Settlement.runId || Saved.completed || Saved.nextStage > Settlement.stage)) Settlement = null;
            if (Settlement != null) DeliverConfirmedClear();
            Notice = string.Join(" ", new[] { personal.notice, RunFile.notice, settlementFile.notice }.Where(x => !string.IsNullOrEmpty(x)));
        }
        private CampaignFileState InspectSettlement(string text)
        {
            try
            {
                var s = JsonUtility.FromJson<CampaignSettlement>(text);
                if (s == null || s.version == 0) return CampaignFileState.Corrupt;
                if (s.version != 1 || s.run?.version != CampaignCatalog.SaveVersion || s.run.content != CampaignCatalog.ContentRevision) return CampaignFileState.Incompatible;
                return CampaignValidation.Settlement(s, Owner, CharacterCatalog.Find) == null ? CampaignFileState.Valid : CampaignFileState.Corrupt;
            }
            catch (Exception e) when (e is ArgumentException || e is NullReferenceException || e is InvalidOperationException) { return CampaignFileState.Corrupt; }
        }
        private CampaignFileState InspectPersonal(string text)
        {
            try
            {
                var p = new CampaignPersonalProgress { version = 0, clears = null, unlocks = null };
                JsonUtility.FromJsonOverwrite(text, p);
                if (p.version == 0) return CampaignFileState.Corrupt;
                if (p.version != 1) return CampaignFileState.Incompatible;
                return CampaignValidation.Personal(p, Owner) ? CampaignFileState.Valid : CampaignFileState.Corrupt;
            }
            catch (Exception e) when (e is ArgumentException || e is NullReferenceException) { return CampaignFileState.Corrupt; }
        }
        private CampaignFileState InspectRun(string text)
        {
            try
            {
                var c = new CampaignCheckpoint { version = 0, content = null, nextStage = 0, irisCoins = -1, totalRevives = -1, attemptsStarted = 0, eligible = false,
                    party = null, processedStages = null, rewards = null, receipts = null, clears = null, failures = null, runSeconds = float.NaN };
                JsonUtility.FromJsonOverwrite(text, c);
                if (c.version == 0 || c.content == null) return CampaignFileState.Corrupt;
                if (c.version != CampaignCatalog.SaveVersion || c.content != CampaignCatalog.ContentRevision) return CampaignFileState.Incompatible;
                return CampaignValidation.Checkpoint(c, Owner, CharacterCatalog.Find) == null ? CampaignFileState.Valid : CampaignFileState.Corrupt;
            }
            catch (Exception e) when (e is ArgumentException || e is NullReferenceException || e is InvalidOperationException) { return CampaignFileState.Corrupt; }
        }
        private void Update()
        {
            if (CurrentOwner != Owner)
            {
                if (RaidNetworkSession.Instance?.Mode == RaidSessionMode.Menu) ReloadAccount();
                else Notice = "계정이 변경되어 이 세션의 저장을 중단했습니다. 메뉴에서 새 계정으로 다시 시작하세요.";
                return;
            }
            RecordFirstBattle();
            if (HasSaveFailure && Time.unscaledTime >= _retryAt) { _retryAt = Time.unscaledTime + 10; RetrySave(); }
        }
        private void RecordFirstBattle()
        {
            if (TrainingUnlocked || _tainted || CharacterTrainingSession.Active) return;
            var session = RaidNetworkSession.Instance; var stage = StageManager.Instance;
            if (session == null || session.Ending || stage == null || !stage.EncounterActive) return;
            bool participating = session.Mode == RaidSessionMode.Single
                ? _working != null && _working.runId == stage.RunId
                : session.RegularCampaignParticipant;
            if (!Personal.RecordBattleParticipation(CurrentOwner, stage.ChapterId, stage.currentStage, Eligible, participating)) return;
            _personalDirty = true; SavePersonal();
            if (!_personalDirty) Notice = "훈련소 해금 · 메인 메뉴에서 모든 직업을 연습할 수 있습니다.";
        }
        public void MarkDevelopmentUse()
        {
            if (CharacterTrainingSession.Active) return;
            _tainted = true; if (_working != null) _working.eligible = false;
            _pending = null;
            Settlement = null; _settlementDirty = false;
            Notice = "개발/테스트 기능 사용 · 이 런의 개인 클리어와 체크포인트 저장 제외. 이전 저장은 유지합니다.";
        }
        public int RewardRandom(int count)
        {
            if (_random == null) _random = new CampaignRandom(unchecked((uint)Guid.NewGuid().GetHashCode()));
            int result = _random.Next(count); if (_working != null) _working.randomState = _random.State; return result;
        }
        private bool Authority => CurrentOwner == Owner && !CharacterTrainingSession.Active && !RaidCombatAuthority.Replica &&
            (RaidNetworkSession.Instance?.Mode == RaidSessionMode.Single || RaidNetworkSession.Instance?.IsHostAuthority == true && RaidNetworkSession.Instance.UsesSteam);
        public void Prepared(bool retry, bool newRun)
        {
            if (RaidNetworkSession.Instance?.IsHostAuthority == true && !RaidNetworkSession.Instance.UsesSteam)
                Notice = "LAN 테스트 세션은 인증된 사용자 식별자가 없어 영구 진행도/런 저장에서 제외됩니다. 기존 저장은 유지합니다.";
            if (!Authority || _tainted || retry) return;
            if (Restoring)
            {
                // PrepareEncounter has reset transient state. Reapply only the exact saved boundary
                // resource state (some resources deliberately survive a stage change).
                foreach (var pair in _slots)
                    if (!CharacterActor.Ensure(pair.Key.gameObject).RestoreCampaignState(pair.Value.character))
                    { RaidNetworkSession.Instance.FailSession("체크포인트 자원/스킬 경계 복구 검증 실패"); return; }
                Restoring = false;
                RestoreMetrics();
                return; // Loading a checkpoint never re-awards or rewrites that checkpoint.
            }
            var stage = StageManager.Instance;
            if (_working == null)
            {
                if (!newRun || stage.currentStage != 1 || stage.maxStage != CampaignCatalog.StageCount(stage.ChapterId)) { Notice = "등록되지 않은 스테이지 구성 · 저장 제외"; return; }
                _random = new CampaignRandom(unchecked((uint)Guid.NewGuid().GetHashCode()));
                _working = new CampaignCheckpoint { owner = Owner, runId = stage.RunId, chapterId = stage.ChapterId, difficultyId = _selectedDifficulty,
                    seed = unchecked((int)_random.State), randomState = _random.State,
                    transport = RaidNetworkSession.Instance.UsesSteam ? "steam" : "single" };
                BindInitialParty();
            }
            CaptureBoundary(false);
        }
        private void BindInitialParty()
        {
            _slots.Clear(); _metricSlots.Clear();
            foreach (var member in PartyLife.OrderedMembers())
            {
                var avatar = member.GetComponent<NetworkRaidAvatar>(); var pc = member.GetComponent<PlayerController>();
                bool bot = member.GetComponent<PartyBotAI>() != null;
                var slot = new CampaignPartySlot { slotId = Guid.NewGuid().ToString("N"), bot = bot, userId = bot ? "" : avatar != null ? CampaignIdentity.ForSteam(avatar.SteamId == 0 && avatar.IsLocal ? RaidSteamRuntime.Instance.UserId : avatar.SteamId) : Owner,
                    name = avatar != null ? avatar.DisplayName : bot ? "AI" : "Player", role = pc != null ? (int)pc.currentRole : (int)member.GetComponent<PartyBotAI>().role + 1 };
                Bind(member, slot);
            }
        }
        public void Bind(Transform member, CampaignPartySlot slot)
        {
            _slots[member] = slot;
            int metric = RaidCombatAuthority.Active ? RaidCombatAuthority.Id(member) : member.GetInstanceID();
            _metricSlots[metric] = slot.slotId;
        }
        public void Cleared(int stage)
        {
            if (!Authority || _working == null || _tainted) return;
            string key = CharacterRewardKey.Stage(_working.runId, _working.chapterId, stage);
            if (_working.processedStages.Contains(key)) return;
            _working.processedStages = _working.processedStages.Concat(new[] { key }).ToArray();
            foreach (var pair in _slots)
            {
                var slot = pair.Value; if (slot.bot) continue;
                var avatar = pair.Key.GetComponent<NetworkRaidAvatar>();
                if (avatar != null && (avatar.Automated || !RaidNetworkSession.Instance.CampaignUserConnected(avatar))) continue;
                var receipt = new CampaignClearReceipt { runId = _working.runId, chapterId = _working.chapterId, difficultyId = _working.difficultyId, userId = slot.userId,
                    stage = stage, final = stage == CampaignCatalog.StageCount(_working.chapterId), eligible = true };
                if (receipt.userId != Owner)
                    _working.receipts = _working.receipts.Concat(new[] { receipt }).ToArray();
            }
            Settlement = new CampaignSettlement { owner = Owner, runId = _working.runId, stage = stage };
            SaveSettlementSnapshot();
        }
        private void DeliverConfirmedClear()
        {
            if (Settlement?.run == null || Owner != CurrentOwner) return;
            var run = Settlement.run;
            var receipt = new CampaignClearReceipt { runId = run.runId, chapterId = run.chapterId, difficultyId = run.difficultyId,
                userId = Owner, stage = Settlement.stage, final = Settlement.stage == CampaignCatalog.StageCount(run.chapterId), eligible = run.eligible };
            AcceptReceipt(receipt);
            foreach (var remote in run.receipts.Where(r => r.stage == Settlement.stage))
            {
                if (Personal.Enqueue(remote)) _personalDirty = true;
                if (RaidNetworkSession.Instance?.IsHostAuthority == true) RaidNetworkSession.Instance.QueueCampaignReceipt(remote);
            }
            if (_personalDirty) SavePersonal();
        }
        internal void AcknowledgeReceipt(string userId, string key)
        {
            if (Owner != CurrentOwner) return;
            if (Personal.Acknowledge(userId, key)) { _personalDirty = true; SavePersonal(); }
            if (_working != null) _working.receipts = _working.receipts.Where(r => r.userId != userId || r.Key != key).ToArray();
            // Never rewrite a combat checkpoint just to acknowledge delivery. The next boundary
            // persists the smaller outbox; a crash can redeliver, and personal Record deduplicates.
        }
        public bool AcceptReceipt(CampaignClearReceipt receipt)
        {
            if (Owner != CurrentOwner || CharacterTrainingSession.Active || !CampaignValidation.Receipt(receipt, Owner) || !receipt.eligible) return false;
            if (Personal.Record(receipt)) _personalDirty = true;
            if (_personalDirty) SavePersonal();
            return !_personalDirty;
        }
        public void CardChosen(PlayerController player, int selected, IEnumerable<int> offered)
        {
            if (!Authority || _working == null || !_slots.TryGetValue(player.transform, out var slot)) return;
            string key = CharacterRewardKey.Stage(_working.runId, _working.chapterId, StageManager.Instance.currentStage) + "/card/" + slot.slotId;
            if (_working.rewards.Any(r => r.key == key)) return;
            _working.rewards = _working.rewards.Concat(new[] { new CampaignRewardRecord { key = key, slotId = slot.slotId, selectedId = CampaignCardIds.Id(selected), offeredIds = offered.Select(CampaignCardIds.Id).ToArray() } }).ToArray();
        }
        public bool MarkClearPresentationFinished()
        {
            if (Settlement == null) return !Authority || _tainted;
            if (!Settlement.presentationComplete) { Settlement.presentationComplete = true; SaveSettlementSnapshot(true); }
            return !_settlementDirty;
        }
        public void RecordSettlementOffer(Transform member, int[] offered)
        {
            if (Settlement == null || offered == null || offered.Length != 3 || !_slots.TryGetValue(member, out var slot) ||
                Settlement.offers.Any(o => o.slotId == slot.slotId)) return;
            Settlement.offers = Settlement.offers.Concat(new[] { new CampaignSettlementOffer { slotId = slot.slotId, cardIds = offered.Select(CampaignCardIds.Id).ToArray() } }).ToArray();
            SaveSettlementSnapshot(true);
        }
        public int[] SettlementOffer(Transform member)
        {
            if (Settlement == null || !_slots.TryGetValue(member, out var slot)) return null;
            var offer = Settlement.offers.FirstOrDefault(o => o.slotId == slot.slotId);
            return offer?.cardIds.Select(CampaignCardIds.Index).ToArray();
        }
        public bool SyncSettlement()
        {
            if (Settlement == null || !Authority || _tainted) return true;
            // Once the clear journal is durable, retrying an older stage-start save can
            // no longer improve recovery and must not block reward settlement forever.
            if (!_settlementDirty && CampaignValidation.SettlementSupersedesPending(Settlement, _pending))
            { _pending = null; _lastBoundaryError = null; }
            if (Saved?.runId == Settlement.runId && (Saved.completed || Saved.nextStage == Settlement.stage + 1))
            { ClearSettlement(); return Settlement == null; }
            if (_pending != null) return false; // A newer boundary is already queued for retry.
            SaveSettlementSnapshot();
            return !_settlementDirty;
        }
        private void SaveSettlementSnapshot(bool force = false)
        {
            if (Settlement == null || !Authority || _tainted) return;
            string previous = Settlement.run == null ? null : JsonUtility.ToJson(Settlement.run);
            foreach (var pair in _slots)
            {
                if (pair.Key == null) { _settlementDirty = true; Notice = "Cannot save pending rewards: a party member is missing."; return; }
                pair.Value.character = CharacterActor.Ensure(pair.Key.gameObject).Snapshot;
                pair.Value.cards = pair.Key.GetComponent<PlayerController>()?.cardManager?.CaptureCampaignCards() ?? Array.Empty<CampaignCardStack>();
                var pc = pair.Key.GetComponent<PlayerController>();
                var bot = pair.Key.GetComponent<PartyBotAI>();
                pair.Value.healthFraction = pc != null ? pc.HpRatio : bot != null && bot.maxHp > 0 ? Mathf.Clamp01(bot.currentHp / bot.maxHp) : 1;
            }
            _working.party = _slots.Values.OrderBy(p => p.role).ToArray();
            _working.nextStage = Settlement.stage;
            _working.runSeconds = StageManager.Instance.TotalPlayTime;
            _working.retries = CombatMetricsManager.Instance.Ledger.RetryCount;
            _working.totalRevives = StageManager.Instance.Iris.TotalUsed;
            _working.attemptsStarted = StageManager.Instance.Iris.AttemptsStarted;
            _working.clears = CaptureMetrics(CombatMetricsManager.Instance.Ledger.Stages);
            _working.failures = CaptureMetrics(CombatMetricsManager.Instance.Ledger.FailedAttempts);
            Settlement.run = JsonUtility.FromJson<CampaignCheckpoint>(JsonUtility.ToJson(_working));
            if (!force && !_settlementDirty && previous == JsonUtility.ToJson(Settlement.run)) return;
            Settlement.savedUtc = DateTimeOffset.UtcNow.ToString("O");
            _settlementDirty = true;
            RetrySettlementSave();
        }
        private void RetrySettlementSave()
        {
            if (!_settlementDirty || Settlement == null) return;
            if (CampaignStorage.Save(SettlementPath, JsonUtility.ToJson(Settlement, true), InspectSettlement, out string error))
            { _settlementDirty = false; Notice = "Boss clear and pending rewards saved."; DeliverConfirmedClear(); }
            else Notice = error;
        }
        public bool CommitNextStageBoundary(int nextStage)
        {
            if (Settlement == null) return !Authority || _tainted || Saved?.nextStage == nextStage && Saved.completed == false;
            if (!SyncSettlement()) return false;
            if (Saved?.runId == Settlement.runId && Saved.nextStage == nextStage && !Saved.completed && _pending == null)
            { ClearSettlement(); return Settlement == null; }
            CaptureBoundary(false, nextStage);
            if (_pending != null) return false;
            ClearSettlement();
            return Settlement == null;
        }
        private void ClearSettlement()
        {
            if (Settlement == null) return;
            if (!CampaignStorage.Delete(SettlementPath, out string error)) { Notice = error; return; }
            Settlement = null; _settlementDirty = false;
        }
        public void Completed()
        {
            if (!Authority || _working == null || _tainted || _working.completed) return;
            CaptureBoundary(true);
            if (_pending == null) ClearSettlement();
        }
        public bool PrepareFreshRun()
        {
            if (_working == null) return true;
            if (!_working.completed || _pending != null || Saved?.runId != _working.runId || !Saved.completed)
            { Notice = "이전 런의 완료 저장을 확인한 뒤 새로 출발할 수 있습니다."; return false; }
            _working = Resume = null; _random = null; _slots.Clear(); _metricSlots.Clear();
            Restoring = false; _tainted = false;
            return true;
        }
        private void CaptureBoundary(bool completed, int nextStage = 0)
        {
            var stage = StageManager.Instance;
            foreach (var pair in _slots)
            {
                if (pair.Key == null) { ReportBoundaryError("원래 파티 슬롯이 없어 저장을 중단했습니다"); return; }
                var actor = CharacterActor.Ensure(pair.Key.gameObject);
                var state = new CharacterRuntimeState(actor.Definition);
                if (!state.Restore(actor.Snapshot)) { ReportBoundaryError("직업 경계 상태 검증 실패"); return; }
                if (completed || nextStage > 0) state.Reset(CharacterReset.NextStage);
                pair.Value.character = state.Snapshot;
                pair.Value.cards = pair.Key.GetComponent<PlayerController>()?.cardManager?.CaptureCampaignCards() ?? Array.Empty<CampaignCardStack>();
                pair.Value.healthFraction = 1; pair.Value.potionCooldown = 0;
            }
            _working.party = _slots.Values.OrderBy(p => p.role).ToArray();
            _working.nextStage = nextStage > 0 ? nextStage : stage.currentStage; _working.completed = completed;
            _working.savedUtc = DateTimeOffset.UtcNow.ToString("O"); _working.runSeconds = stage.TotalPlayTime;
            _working.retries = CombatMetricsManager.Instance.Ledger.RetryCount;
            _working.totalRevives = stage.Iris.TotalUsed; _working.attemptsStarted = stage.Iris.AttemptsStarted + (nextStage > 0 ? 1 : 0);
            _working.irisCoins = IrisRescueState.DefaultCoins;
            _working.clears = CaptureMetrics(CombatMetricsManager.Instance.Ledger.Stages);
            _working.failures = CaptureMetrics(CombatMetricsManager.Instance.Ledger.FailedAttempts);
            _pending = JsonUtility.FromJson<CampaignCheckpoint>(JsonUtility.ToJson(_working));
            RetrySave();
        }
        public void RetrySave()
        {
            if (Owner != CurrentOwner) return;
            if (_personalDirty) SavePersonal();
            RetrySettlementSave();
            if (_pending == null) return;
            string validation = CampaignValidation.Checkpoint(_pending, Owner, CharacterCatalog.Find);
            if (validation != null) { ReportBoundaryError("저장하지 못했습니다: " + validation + " · 플레이는 계속되며 이전 저장 지점을 유지합니다."); return; }
            if (CampaignStorage.Save(RunPath, JsonUtility.ToJson(_pending, true), InspectRun, out string error))
            {
                Saved = _pending; _pending = null; RunFile = new CampaignFileRead { state = CampaignFileState.Valid };
                _lastBoundaryError = null;
                Notice = "런 체크포인트 저장 완료 · " + LastSaveLabel + (_personalDirty ? "\n개인 진행도 저장 실패: " + _personalError : "");
                if (Settlement != null && (Saved.completed || Saved.nextStage == Settlement.stage + 1)) ClearSettlement();
                return;
            }
            ReportBoundaryError(error + " 플레이는 계속됩니다. 현재 전투가 아니라 보관한 안전 경계 스냅샷만 재시도합니다.");
        }
        private void ReportBoundaryError(string message)
        {
            Notice = message;
            if (_lastBoundaryError == message) return;
            _lastBoundaryError = message;
            Debug.LogWarning("[Campaign checkpoint] " + message, this);
        }
        private void SavePersonal()
        {
            if (CampaignStorage.Save(PersonalPath, JsonUtility.ToJson(Personal, true), InspectPersonal, out string error)) { _personalDirty = false; _personalError = ""; }
            else { _personalError = error; Notice = error; }
        }
        public string LastSaveLabel => Settlement != null
            ? CampaignCatalog.Name(Settlement.run.chapterId) + " · Stage " + Settlement.stage + " 처치 후 정산 대기 · " + Settlement.savedUtc
            : Saved == null ? "저장된 체크포인트 없음" : CampaignCatalog.Name(Saved.chapterId) +
                (Saved.completed ? " · 완료" : " · Stage " + Saved.nextStage + " 시작") + " · " + Saved.savedUtc;
        public string SaveDescription
        {
            get
            {
                var run = Settlement?.run ?? Saved;
                if (run == null) return LastSaveLabel + "\n" + RunFile?.notice;
                return LastSaveLabel + "\n난이도: " + run.difficultyId + " · " + run.transport + " · 계정: " + Owner +
                    "\n누적 부활: " + run.totalRevives + " · 재도전: " + run.retries +
                    "\n" + string.Join("\n", run.party.Select(p => p.name + (p.bot ? " [AI]" : p.userId == Owner ? " [나]" : " [기존 참가자]") +
                    " · " + (CharacterCatalog.Find(p.character.jobId)?.name ?? p.character.jobId) + " / " +
                    (CharacterCatalog.Find(p.character.jobId)?.routes.FirstOrDefault(r => r.id == p.character.routeId)?.name ?? "기본")));
            }
        }
        public bool CanContinue => (Settlement != null || Saved != null && !Saved.completed) && Owner == CurrentOwner &&
            RunFile?.state != CampaignFileState.Incompatible &&
            (SettlementFile == null || SettlementFile.state == CampaignFileState.Missing || SettlementFile.state == CampaignFileState.Valid || SettlementFile.state == CampaignFileState.Backup) &&
            RaidNetworkSession.Instance?.Mode == RaidSessionMode.Menu;
        public void Continue(bool steam)
        {
            if (!CanContinue || RaidSteamRuntime.Instance?.Busy == true) return;
            ReloadAccount(); if (!CanContinue) return;
            var source = CampaignRecovery.ContinueSource(Settlement, Saved);
            if (source == null || steam != (source.transport == "steam") || steam && RaidSteamRuntime.Instance?.Available != true) { Notice = "Steam 호스트 저장은 같은 Steam 계정으로 새 로비를 생성하여 이어갑니다."; return; }
            Resume = JsonUtility.FromJson<CampaignCheckpoint>(JsonUtility.ToJson(source));
            _working = JsonUtility.FromJson<CampaignCheckpoint>(JsonUtility.ToJson(Resume));
            _random = new CampaignRandom(Resume.randomState); _slots.Clear(); _metricSlots.Clear();
            if (steam) RaidSteamRuntime.Instance.CreateRoom();
            else RaidNetworkSession.Instance.ContinueCampaignSingle();
        }
        public void BeginRestoreBoundary() { Restoring = true; }
        public void RestoreFinished() { Resume = null; }
        public void CancelResumeIntent()
        {
            if (RaidNetworkSession.Instance?.Mode != RaidSessionMode.Menu || Resume == null) return;
            Resume = _working = null; _slots.Clear(); _metricSlots.Clear(); Restoring = false;
        }
        public void DeleteSelected(bool personal)
        {
            if (RaidNetworkSession.Instance?.Mode != RaidSessionMode.Menu || Owner != CurrentOwner) return;
            if (RaidSteamRuntime.Instance?.Busy == true) { Notice = "로비 생성/접속을 마치거나 취소한 뒤 저장을 삭제하세요."; return; }
            if (!CampaignStorage.Delete(personal ? PersonalPath : RunPath, out string error)) { Notice = error; return; }
            if (!personal && !CampaignStorage.Delete(SettlementPath, out error)) { ReloadAccount(); Notice = error; return; }
            ReloadAccount();
            Notice = personal ? "이 계정의 개인 진행도와 챕터 해금만 삭제했습니다." : "이 계정의 런 체크포인트와 정산 대기 기록을 삭제했습니다. 개인 진행도는 유지됩니다.";
        }
        private CampaignMetricRecord[] CaptureMetrics(IReadOnlyList<CombatEncounterResult> records) => records.Select(r => new CampaignMetricRecord { stage = r.Stage, seconds = r.Seconds,
            members = r.Members.Where(m => _metricSlots.ContainsKey(m.Id)).Select(m => new CampaignMetricMember { slotId = _metricSlots[m.Id], name = m.Name, jobId = m.JobId, role = m.Role, deaths = m.Deaths,
                bossDamage = m.BossDamage, addDamage = m.AddDamage, healing = m.Healing, selfHealing = m.SelfHealing, shield = m.ShieldAbsorbed }).ToArray() }).ToArray();
        private CombatEncounterResult RestoreMetric(CampaignMetricRecord r) => new CombatEncounterResult(r.stage, r.seconds, r.members.Select(m => new CombatMemberRecord { Id = _metricSlots.First(p => p.Value == m.slotId).Key,
            Name = m.name, JobId = m.jobId, Role = m.role, Deaths = m.deaths, BossDamage = m.bossDamage, AddDamage = m.addDamage, Healing = m.healing, SelfHealing = m.selfHealing, ShieldAbsorbed = m.shield }));
        public void RestoreMetrics()
        {
            if (_working == null) return;
            CombatMetricsManager.Instance.Ledger.ReplaceNetworkSnapshot(new CombatEncounterResult(_working.nextStage, 0, Array.Empty<CombatMemberRecord>()), _working.clears.Select(RestoreMetric), _working.failures.Select(RestoreMetric), _working.retries);
        }
    }
}
