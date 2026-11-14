using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class StageManager
    {
        private RaidBattlePhase? _presentedNetworkPhase;
        private int _replicaDefeatEpoch = -1;
        private int _replicaArrivalRevealEpoch = -1;
        private int _replicaDefeatCleanedEpoch = -1;
        public float NetworkRunTime => _totalPlayTime;
        public bool PrepareNetworkEncounter(int stage, bool retry)
        { return RaidCombatAuthority.Host && PrepareEncounter(stage, retry, !_hasStartedEncounter); }
        internal bool FinishNetworkClear()
        {
            if (!RaidCombatAuthority.Host || !Progress.FinishClear(Progress.Revision, maxStage,HasPendingSkillChoices)) return false;
            TraceProgress("clear presentation completed"); return true;
        }
        public void ApplyNetworkStage(RaidCombatFrame frame)
        {
            _totalPlayTime = frame.runTime;
            RunId=frame.runId;
            currentStage = frame.stage; IsWiped = frame.phase == RaidBattlePhase.Wiped; IsPreparing = frame.phase == RaidBattlePhase.Countdown || frame.phase == RaidBattlePhase.Loading || frame.phase == RaidBattlePhase.Approach || frame.phase == RaidBattlePhase.Arrival;
            _isAllStagesCleared = frame.phase == RaidBattlePhase.Victory; _isStageTransitioning = frame.phase == RaidBattlePhase.Reward || frame.phase == RaidBattlePhase.BossClear;
            var approach = RaidApproachField.Instance;
            if (RaidCombatAuthority.Replica && approach != null)
            {
                if (frame.phase != RaidBattlePhase.Lobby && frame.phase != RaidBattlePhase.Closed)
                    GetComponent<RaidLobbyRoom>()?.HideForDeparture();
                if (frame.phase == RaidBattlePhase.Approach) approach.EnterApproach();
                else if (frame.phase == RaidBattlePhase.Loading) { approach.Show(true); approach.SetBossVisible(false); }
                else if (frame.phase == RaidBattlePhase.Arrival || frame.phase == RaidBattlePhase.Countdown || frame.phase == RaidBattlePhase.Combat)
                { approach.Show(true); approach.SealField(false); }
                else if (frame.phase == RaidBattlePhase.Lobby || frame.phase == RaidBattlePhase.Closed) approach.Show(false);
                if (frame.phase == RaidBattlePhase.Approach || frame.phase == RaidBattlePhase.Arrival || frame.phase == RaidBattlePhase.Countdown || frame.phase == RaidBattlePhase.Combat)
                    approach.SetBossVisible(frame.bossVisible);
                if (frame.phase == RaidBattlePhase.BossClear || frame.phase == RaidBattlePhase.Reward || frame.phase == RaidBattlePhase.Victory)
                { approach.Show(true); approach.SealField(false); approach.SetBossVisible(frame.bossVisible); }
                approach.SetPortalActive(frame.portalActive);
                approach.SetBossCombatReady(frame.phase == RaidBattlePhase.Combat);
            }
            if (frame.phase == RaidBattlePhase.Arrival)
            {
                var layout = approach?.Arrival(currentStage);
                if (RaidCombatAuthority.Replica && frame.bossVisible && _replicaArrivalRevealEpoch != frame.epoch)
                {
                    _replicaArrivalRevealEpoch = frame.epoch;
                    if (layout?.presentationPrefab?.HasBossAnimationTrack != true)
                        PlayConfiguredBossTrigger(layout?.arrivalAnimationTrigger);
                }
                bool playing = false;
                bool presentationFinished = false;
                if (RaidCombatAuthority.Replica && layout != null)
                {
                    double remaining = frame.phaseDeadline - RaidCombatAuthority.ServerTime;
                    if (remaining > layout.afterCutsceneSeconds + .05f)
                        playing = BeginStagePresentation(frame.phase, frame.epoch,
                            layout.Duration + layout.afterCutsceneSeconds - remaining);
                    else { presentationFinished = layout.presentationPrefab != null && layout.presentationPrefab.ConfigurationError == null; EndStagePresentation(); }
                }
                if (presentationFinished) QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
                else if (!playing && layout != null) QuarterViewCamera.LocalCamera?.SetArrivalCamera(layout.cameraPosition, layout.cameraLookAt);
            }
            else if (frame.phase == RaidBattlePhase.BossClear)
            {
                var layout = approach?.Defeat(currentStage);
                bool playing = RaidCombatAuthority.Replica && layout != null &&
                    BeginStagePresentation(frame.phase, frame.epoch,
                        layout.Duration - (frame.phaseDeadline - RaidCombatAuthority.ServerTime));
                if (RaidCombatAuthority.Replica && _replicaDefeatEpoch != frame.epoch)
                {
                    _replicaDefeatEpoch = frame.epoch;
                    bool animated = playing && _activeCutscene != null && _activeCutscene.HasBossAnimationTrack;
                    if (!animated) animated = PlayConfiguredBossTrigger(layout?.deathAnimationTrigger);
                    if (!animated) boss?.PlayBossCollapseAnimation();
                }
                if (!playing && layout != null) QuarterViewCamera.LocalCamera?.SetArrivalCamera(layout.cameraPosition, layout.cameraLookAt);
            }
            else { if (RaidCombatAuthority.Replica) EndStagePresentation(); QuarterViewCamera.LocalCamera?.ClearArrivalCamera(); }
            if (RaidCombatAuthority.Replica && !frame.bossVisible &&
                (frame.phase == RaidBattlePhase.Reward || frame.phase == RaidBattlePhase.Victory) &&
                _replicaDefeatCleanedEpoch != frame.epoch)
            { _replicaDefeatCleanedEpoch = frame.epoch; TargetingManager.Instance?.ClearTarget(); }
            UpdateStageHeaderUI();
        }
        public void PresentNetworkPhase(RaidBattlePhase phase, int seconds)
        {
            if (phase != RaidBattlePhase.Victory && victoryModalRoot != null) victoryModalRoot.SetActive(false);
            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(phase == RaidBattlePhase.BossClear);
            if (phase == RaidBattlePhase.BossClear && bossClearBannerText != null) bossClearBannerText.text = $"STAGE {currentStage} BOSS CLEAR!";
            if (phase == RaidBattlePhase.Loading) { _wipeUI.ShowNetworkLoading(); _presentedNetworkPhase = phase; return; }
            if (phase == RaidBattlePhase.Countdown) { _wipeUI.ShowCountdown(Mathf.Max(1, seconds)); _presentedNetworkPhase = phase; return; }
            if (phase == RaidBattlePhase.Approach) { _wipeUI.Hide(); _presentedNetworkPhase = phase; return; }
            if (phase == RaidBattlePhase.Arrival) { _wipeUI.Hide(); _presentedNetworkPhase = phase; return; }
            if (_presentedNetworkPhase == phase) return;
            _presentedNetworkPhase = phase; _wipeUI.Hide(); GetComponent<CombatMetricsUI>()?.CloseResults();
            if (phase == RaidBattlePhase.Wiped) _wipeUI.ShowWipe();
            if (phase == RaidBattlePhase.Victory) ShowAllStagesClearVictoryScreen();
        }
    }
    public partial class RoguelikeCardManager
    {
        private int _networkOfferEpoch = -1;
        public string NetworkCardName(int id) => _cardPool.Find(c => (int)c.id == id)?.title ?? ((RoguelikeCardId)id).ToString();
        public float NetworkHaste { get => _partyAttackSpeedBuffTimer; set => _partyAttackSpeedBuffTimer = value; }
        public int[] GenerateNetworkOffer()
        {
            var candidates = _cardPool.FindAll(c => EligibleRosterCard(c)&&(!_acquiredCardStacks.ContainsKey(c.id) || c.id == RoguelikeCardId.SwiftAwakening));
            if (candidates.Count < 3) candidates = _cardPool.FindAll(EligibleRosterCard);
            var result = new int[3];
            for (int i = 0; i < 3; i++) { int index = CampaignRuntime.Instance?.RewardRandom(candidates.Count) ?? UnityEngine.Random.Range(0, candidates.Count); result[i] = (int)candidates[index].id; candidates.RemoveAt(index); }
            return result;
        }
        public void ApplyNetworkCard(RoguelikeCardId id)
        {
            if (!RaidCombatAuthority.Host) return;
            var card = _cardPool.Find(c => c.id == id); if (card != null) ApplyCardEffect(card);
        }
        public void ShowNetworkOffer(int[] ids, int epoch)
        {
            if (ids == null || ids.Length != 3 || (_isSelectionOpen && _networkOfferEpoch == epoch)) return;
            _networkOfferEpoch = epoch; _currentOfferedCards.Clear();
            foreach (int id in ids) { var card = _cardPool.Find(c => (int)c.id == id); if (card == null) return; _currentOfferedCards.Add(card); }
            for (int i = 0; i < 3; i++)
            {
                var card = _currentOfferedCards[i]; GetRoleAdaptedCardDisplay(card, out string title, out string category, out string description);
                if (cardTitleTexts[i] != null) cardTitleTexts[i].text = title + "\n" + category;
                if (cardDescTexts[i] != null) LocalShortcutText.Set(cardDescTexts[i], description + "\n[{key:Choice" + (i + 1) + "}] 선택 · 모든 참가자 선택 후 진행");
                if (cardAccentBanners[i] != null) cardAccentBanners[i].color = card.accentColor;
            }
            if (modalHeaderText != null) modalHeaderText.text = "보스 토벌 보상 · 나의 카드 선택";
            _isSelectionOpen = true; if (cardSelectionModalRoot != null) cardSelectionModalRoot.SetActive(true);
        }
        public void ShowRestoredSingleOffer(int[] ids, Action chosen)
        {
            if (ids == null || ids.Length != 3) return;
            _onCardSelectedCallback = chosen;
            ShowNetworkOffer(ids, -2);
        }
        public void CloseNetworkOffer()
        {
            if (!_isSelectionOpen) return; _isSelectionOpen = false;
            if (cardSelectionModalRoot != null) cardSelectionModalRoot.SetActive(false);
        }
        public void ApplyNetworkCardStacks(int[] stacks)
        {
            if (stacks == null || stacks.Length != 7 && stacks.Length!=CharacterEvolutionCards.Count) return;
            _acquiredCardStacks.Clear(); _acquiredCardHistory.Clear();
            for (int i = 0; i < stacks.Length; i++) if (stacks[i] > 0)
            {
                _acquiredCardStacks[(RoguelikeCardId)i] = stacks[i];
                var card = _cardPool.Find(c => (int)c.id == i); GetRoleAdaptedCardDisplay(card, out string title, out _, out _);
                for (int j = 0; j < stacks[i]; j++) _acquiredCardHistory.Add(title);
            }
        }
    }
    public sealed partial class CombatMetricsManager
    {
        public static RaidMetricResult CaptureNetworkResult(CombatEncounterResult result)
        {
            var members = new List<RaidMetricMember>();
            foreach (var member in result.Members) members.Add(new RaidMetricMember { id = member.Id, name = member.Name, role = member.Role, jobId = member.JobId,
                bossDamage = member.BossDamage, addDamage = member.AddDamage, healing = member.Healing, selfHealing = member.SelfHealing, shield = member.ShieldAbsorbed, deaths = member.Deaths });
            return new RaidMetricResult { stage = result.Stage, seconds = result.Seconds, members = members.ToArray(), events = new List<string>(result.Events).ToArray() };
        }
        public static RaidMetricResult[] CaptureNetworkResults(IReadOnlyList<CombatEncounterResult> results)
        { var list = new List<RaidMetricResult>(); foreach (var result in results) list.Add(CaptureNetworkResult(result)); return list.ToArray(); }
        private static CombatEncounterResult RestoreResult(RaidMetricResult result)
        {
            var members = new List<CombatMemberRecord>();
            foreach (var member in result.members) members.Add(new CombatMemberRecord { Id = member.id, Name = member.name, Role = member.role, JobId = member.jobId,
                BossDamage = member.bossDamage, AddDamage = member.addDamage, Healing = member.healing, SelfHealing = member.selfHealing, ShieldAbsorbed = member.shield, Deaths = member.deaths });
            return new CombatEncounterResult(result.stage, result.seconds, members, result.events);
        }
        public void ApplyNetworkMetrics(RaidCombatFrame frame)
        {
            var clears = new List<CombatEncounterResult>(); var failures = new List<CombatEncounterResult>();
            foreach (var result in frame.clears) clears.Add(RestoreResult(result)); foreach (var result in frame.failures) failures.Add(RestoreResult(result));
            Ledger.ReplaceNetworkSnapshot(RestoreResult(frame.current), clears, failures, frame.retries);
        }
    }
}
