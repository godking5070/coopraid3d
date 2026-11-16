using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>
    /// 1부 Stage 1~6 보스 러시 루프 및
    /// 보스 처치 연출 -> 1.5초 뒤 로그라이크 카드 3택 보상 -> 파티 정비/재배치 -> 다음 보스 등장을 총괄하는 매니저 (Step 6).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public partial class StageManager : MonoBehaviour
    {
        [Header("Stage Progression")]
        public int currentStage = 1;
        public int maxStage = 6;
        public string RunId {get;private set;}=System.Guid.NewGuid().ToString("N");
        public string ChapterId=>CharacterRewardKey.CurrentChapter;
        private int _singleRewardRevision=-1;
        private bool _singleCardChosen;
        private float _singleRewardDeadline;
        public float SkillRewardSecondsRemaining=>Mathf.Max(0,(RaidCombatAuthority.Active?RaidNetworkSession.Instance.PhaseDeadline-RaidCombatAuthority.ServerTime:_singleRewardDeadline-Time.unscaledTime));
        public bool HasPendingSkillChoices=>PartyLife.OrderedMembers().Exists(m=>CharacterActor.Ensure(m.gameObject).PendingChoices.Length>0);

        [Header("Common Potion (shared by all four party members)")]
        public PotionSettings potionSettings = new PotionSettings();
        public ResurrectionSettings resurrectionSettings = new ResurrectionSettings();
        public static StageManager Instance { get; private set; }
        public static bool CombatSuspended => RaidNetworkSession.CombatPreview || Instance != null && (!Instance.EncounterActive || Instance.IsWiped || Instance.IsPreparing || Instance._isStageTransitioning || Instance._isAllStagesCleared);
        // Practice permits player skills only; boss AI, damage and progression stay suspended.
        public static bool PlayerSkillsSuspended => CombatSuspended && !RaidLobbyRoom.PracticeActive;
        public bool IsWiped { get; private set; }
        public bool IsPreparing { get; private set; }
        internal readonly RaidRunFlow Progress = new RaidRunFlow();
        public readonly RaidFlowSettings FlowSettings = new RaidFlowSettings();
        public bool EncounterActive => RaidCombatAuthority.Replica ? RaidNetworkSession.Instance.CombatRunning : Progress.Phase == RaidBattlePhase.Combat;
        public bool CanSelectInitialRole => CampaignRuntime.Instance?.BuildLocked != true && !_hasStartedEncounter && Progress.Stage == 0 && Progress.Phase == RaidBattlePhase.Lobby && RaidNetworkSession.Instance?.Mode == RaidSessionMode.Single;
        private bool _pendingBossClear;
        private PartyWipeUI _wipeUI;

        [Header("References")]
        public BossController boss;
        public PlayerController player;
        public RoguelikeCardManager cardManager;

        [Header("Stage & Clear UI References")]
        public Text stageHeaderText;
        public GameObject bossClearBannerRoot;
        public Text bossClearBannerText;
        public GameObject victoryModalRoot;
        public Text victorySummaryText;
        public Button restartButton;

        [Header("Initial Party Spawn Positions (XZ)")]
        public Vector3 playerSpawnPos = new Vector3(0f, 1.0f, -4.0f);
        public Vector3 tankSpawnPos = new Vector3(0f, 1.0f, 4.8f);
        public Vector3 healerSpawnPos = new Vector3(-6.8f, 1.0f, -6.5f);
        public Vector3 rangedSpawnPos = new Vector3(6.6f, 1.0f, -6.0f);

        private float _totalPlayTime;
        private bool _isStageTransitioning;
        private bool _isAllStagesCleared;
        private bool _hasStartedEncounter;
        internal bool LastPreparationNewRun { get; private set; }

        public bool IsStageTransitioning => _isStageTransitioning || IsWiped || IsPreparing;
        public float TotalPlayTime => _totalPlayTime;

        private void Awake() { Instance = this; maxStage = CampaignCatalog.StageCount(ChapterId); }
        private void OnDestroy() { EndStagePresentation(); EndSessionProgress(); if (Instance == this) Instance = null; }

        private void Start()
        {
            RaidNetworkSession.Ensure(this);
            CampaignRuntime.Ensure(this);
            _wipeUI = GetComponent<PartyWipeUI>() ?? gameObject.AddComponent<PartyWipeUI>();
            if(GetComponent<CharacterRewardUI>()==null)gameObject.AddComponent<CharacterRewardUI>();
            if (boss == null) boss = FindFirstObjectByType<BossController>();
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (cardManager == null) cardManager = FindFirstObjectByType<RoguelikeCardManager>();
            if (GetComponent<RaidLobbyRoom>() == null) gameObject.AddComponent<RaidLobbyRoom>();
            RaidApproachField.Ensure(this);

            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(false);
            if (victoryModalRoot != null) victoryModalRoot.SetActive(false);

            if (restartButton != null)
            {
                restartButton.onClick.RemoveAllListeners();
                restartButton.onClick.AddListener(RestartFromStage1);
                var label = restartButton.GetComponentInChildren<Text>(); if (label != null) label.text = "메인 메뉴 · 새 런";
            }

            // Menu preview is not an encounter. Role selection/host readiness starts preparation.
            currentStage = 1; CombatMetricsManager.Ensure(this).Ledger.ResetRun();
            boss?.StopEncounterEffects(); UpdateStageHeaderUI();
        }

        private void Update()
        {
            UpdateSingleRewards();
            if (!RaidCombatAuthority.Active && (RaidApproachField.Instance?.NearPortal(player?.transform) == true ||
                player != null && player.IsDead && RaidApproachField.Instance?.PortalActive == true) &&
                !LocalGameplayInput.ModalCovered && LocalInputBindings.Pressed("Interact"))
            {
                if (Progress.Phase == RaidBattlePhase.Reward) ReturnToLobbyIntermission();
                else if (Progress.Phase == RaidBattlePhase.Victory) ReturnToLobby();
            }
            if (_isAllStagesCleared)
            {
                return;
            }

            if (!CombatSuspended && Time.timeScale > 0.01f)
            {
                _totalPlayTime += Time.deltaTime;
            }
        }

        /// <summary>
        /// 이미 준비된 시도를 활성화합니다. 보스/파티 초기화는 PrepareEncounter에서 한 번만 수행합니다.
        /// </summary>
        public void StartStage(int stageNumber)
        {
            if (stageNumber != Progress.Stage || !Progress.TryStart(Progress.Revision)) return;
            RaidApproachField.Instance?.SetBossCombatReady(true);
            IsWiped = IsPreparing = _pendingBossClear = _isStageTransitioning = _isAllStagesCleared = false;
            _hasStartedEncounter = true;
            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(false);
            if (victoryModalRoot != null) victoryModalRoot.SetActive(false);
            _wipeUI.Hide(); UpdateStageHeaderUI();
            CombatMetricsManager.Ensure(this).BeginStage(currentStage, false);
            RaidNetworkSession.Instance?.AiSpectatorRecordAttemptStart();
            QuarterViewCamera.LocalCamera?.BeginCombat(currentStage == 1 && CombatMetricsManager.Instance.Ledger.RetryCount == 0);
            TraceProgress("countdown completed");
        }

        public void BeginSingleRun()
        {
            if (!CanSelectInitialRole || player==null || !CharacterActor.Ensure(player.gameObject).DepartureReady ||
                CampaignRuntime.Instance?.PrepareFreshRun() == false || !PrepareEncounter(1, false, true)) return;
            StartCoroutine(PreparedCountdown(Progress.Revision));
        }
        public void BeginSingleNextStage()
        {
            if (Progress.Phase != RaidBattlePhase.Lobby || Progress.Stage < 1 || Progress.Stage >= maxStage ||
                CampaignRuntime.Instance?.HasSaveFailure == true || !PrepareEncounter(Progress.Stage + 1, false)) return;
            StartCoroutine(PreparedCountdown(Progress.Revision));
        }
        internal bool RestoreSettlementFromLobby()
        {
            var campaign = CampaignRuntime.Instance;
            var settlement = campaign?.Settlement;
            // A stale callback must not replace live attempt history. Check the flow
            // before restoring counters, and reject bad counters before changing phase.
            if (settlement == null || campaign.Resume == null || Progress.Phase != RaidBattlePhase.Lobby ||
                Progress.Stage != 0 || settlement.stage < 1 || settlement.stage > maxStage ||
                !Iris.TryRestoreCompletedHistory(campaign.Resume.totalRevives, campaign.Resume.attemptsStarted) ||
                !Progress.TryRestoreSettlement(settlement.stage, maxStage)) return false;
            currentStage = settlement.stage;
            _hasStartedEncounter = true;
            _isStageTransitioning = true;
            IsPreparing = IsWiped = false;
            GetComponent<RaidLobbyRoom>()?.HideForDeparture();
            var field = RaidApproachField.Ensure(this);
            field.Begin(false);
            field.SummonParty();
            field.SetBossVisible(false);
            field.SetBossCombatReady(false);
            _singleRewardRevision = Progress.Revision;
            _singleRewardDeadline = Time.unscaledTime + FlowSettings.rewardSeconds;
            var playerSlot = campaign.Resume.party.FirstOrDefault(p => !p.bot && p.userId == campaign.Owner);
            string cardKey = CharacterRewardKey.Stage(campaign.Resume.runId, campaign.Resume.chapterId, currentStage) + "/card/" + playerSlot?.slotId;
            _singleCardChosen = currentStage >= maxStage || campaign.Resume.rewards.Any(r => r.key == cardKey);
            if (!RaidCombatAuthority.Active && !_singleCardChosen)
            {
                int[] offer = campaign.SettlementOffer(player.transform);
                if (offer == null) cardManager.OpenStageClearCardReward(currentStage, () => OnRewardCardChosenAdvanceToNextStage(_singleRewardRevision));
                else cardManager.ShowRestoredSingleOffer(offer, () => OnRewardCardChosenAdvanceToNextStage(_singleRewardRevision));
            }
            UpdateStageHeaderUI();
            return true;
        }
        public bool ReturnToLobbyIntermission()
        {
            if (RaidCombatAuthority.Replica || RaidApproachField.Instance?.PortalActive != true || !Progress.TryIntermission(Progress.Revision, maxStage)) return false;
            EndStagePresentation();
            StopAllCoroutines(); CancelInvoke(); CleanupAttempt();
            cardManager?.CancelPendingSelection(); _wipeUI?.Hide();
            GetComponent<CombatMetricsUI>()?.CloseResults();
            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(false);
            QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
            RaidApproachField.Instance?.Show(false);
            _isStageTransitioning = IsPreparing = false;
            currentStage = Progress.Stage + 1;
            // A dead player can use the clear portal but cannot walk to the next door.
            // Existing placement recovery heals the party without starting an attempt,
            // spending Iris coins, or resetting their chosen job, build and cards.
            ResetAndHealAllPartyMembers();
            GetComponent<RaidLobbyRoom>()?.ReturnFromRaid();
            UpdateStageHeaderUI(); TraceProgress("rewards settled; returned through portal");
            return true;
        }
        public bool ReturnToLobby()
        {
            if (!Progress.ReturnToLobby()) return false;
            EndStagePresentation();
            StopAllCoroutines(); CancelInvoke(); CleanupAttempt();
            cardManager?.CancelPendingSelection();
            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(false);
            if (victoryModalRoot != null) victoryModalRoot.SetActive(false);
            GetComponent<CombatMetricsUI>()?.CloseResults(); _wipeUI?.Hide();
            Time.timeScale = 1;
            RunId = System.Guid.NewGuid().ToString("N"); currentStage = 1;
            _hasStartedEncounter = _isAllStagesCleared = _isStageTransitioning = IsWiped = IsPreparing = _pendingBossClear = false;
            _singleRewardRevision = -1; _totalPlayTime = 0;
            var emptyCards = System.Array.Empty<CampaignCardStack>();
            foreach (var member in PartyLife.OrderedMembers())
            {
                var pc = member.GetComponent<PlayerController>();
                var actor = CharacterActor.Ensure(member.gameObject);
                if (pc != null) { pc.RestoreCampaignCards(emptyCards); pc.cardManager?.RestoreCampaignCards(emptyCards); }
                if (pc != null) pc.moveSpeed = actor.Definition.moveSpeed;
                actor.ResetEncounter(CharacterReset.NewRun);
                var life = PartyLife.Ensure(member.gameObject); life.ResetForEncounter();
                if (pc != null) { pc.currentHp = pc.maxHp; pc.currentShield = 0; pc.RefillMovementCharges(); }
            }
            cardManager?.RestoreCampaignCards(emptyCards);
            Iris.BeginAttempt(0, 0);
            RaidApproachField.Instance?.Show(false);
            boss?.StopEncounterEffects(); UpdateStageHeaderUI();
            GetComponent<RaidLobbyRoom>()?.ReturnFromRaid();
            TraceProgress("returned to lobby after completed run");
            return true;
        }
        private bool PrepareEncounter(int stage, bool retry, bool newRun = false, bool resume = false)
        {
            if (boss == null || player == null || cardManager == null || cardManager.cardSelectionModalRoot == null)
            { RaidNetworkSession.Instance?.FailSession("전투/보상 참조가 누락되었습니다. 원본 프로젝트의 SceneAutoBuilder 구성을 확인하세요."); return false; }
            bool spectatorJump=RaidNetworkSession.Instance?.AiSpectatorActive==true && !retry && !resume &&
                Progress.Stage==0 && stage>1;
            if (!(spectatorJump ? Progress.TryDevelopmentPrepare(stage,maxStage) :
                resume ? Progress.TryResume(stage, maxStage) : Progress.TryPrepare(stage, maxStage, retry))) return false;
            EndStagePresentation();
            GetComponent<RaidLobbyRoom>()?.HideForDeparture();
            var metrics = CombatMetricsManager.Ensure(this);
            if (newRun) { metrics.Ledger.ResetRun(); _totalPlayTime = 0; }
            if (retry) metrics.Ledger.RegisterRetry();
            var saved = resume ? CampaignRuntime.Instance?.Resume : null;
            Iris.BeginAttempt(newRun ? 0 : saved?.totalRevives, newRun ? 0 : saved?.attemptsStarted);
            currentStage = stage; IsPreparing = true; IsWiped = false;
            _isStageTransitioning = _isAllStagesCleared = _pendingBossClear = false;
            CleanupAttempt(); GetComponent<CombatMetricsUI>()?.CloseResults(); cardManager?.CancelPendingSelection();
            ResetAndHealAllPartyMembers();
            foreach (var member in PartyLife.OrderedMembers())
            {
                PartyLife.Ensure(member.gameObject).ResetForEncounter();
                CharacterActor.Ensure(member.gameObject).ResetEncounter(newRun ? CharacterReset.NewRun : retry ? CharacterReset.Retry : CharacterReset.NextStage);
                member.GetComponent<PlayerController>()?.RefillMovementCharges();
                member.GetComponent<PlayerController>()?.cardManager?.ResetTemporaryCombatState();
            }
            if(spectatorJump)
            {
                foreach(var member in PartyLife.OrderedMembers())
                {
                    var actor=CharacterActor.Ensure(member.gameObject);
                    for(int cleared=1;cleared<stage;cleared++)
                    {
                        actor.ClearedStage(cleared);
                        foreach(var choice in actor.PendingChoices)
                            if(actor.ChooseAiSpectatorPreset(choice.id,choice.choices[0]))
                                Debug.Log("[AI Spectator preset] "+member.name+" Stage "+cleared+" "+choice.id+"="+choice.choices[0]);
                        var cards=member.GetComponent<PlayerController>()?.cardManager;
                        if(cards!=null)
                        {
                            int[] offered=cards.GenerateNetworkOffer();
                            if(offered.Length>0){cards.ApplyNetworkCard((RoguelikeCardId)offered[0]);
                                Debug.Log("[AI Spectator preset] "+member.name+" Stage "+cleared+" card="+offered[0]);}
                        }
                    }
                }
            }
            boss.SetupBossForStage(stage);
            RaidApproachField.Ensure(this).Begin(retry);
            UpdateStageHeaderUI(); TraceProgress(retry ? "retry prepared; cards retained" : "stage prepared");
            LastPreparationNewRun = newRun;
            if (!RaidCombatAuthority.Host) CampaignRuntime.Instance?.Prepared(retry, newRun);
            return true;
        }
        internal bool BeginPreparedApproach()
        {
            if (!Progress.TryApproach(Progress.Revision)) return false;
            RaidApproachField.Ensure(this).EnterApproach();
            UpdateStageHeaderUI(); TraceProgress("approach opened"); return true;
        }
        internal bool BeginPreparedArrival()
        {
            if (RaidApproachField.Instance?.HumanEntered() != true || !Progress.TryArrival(Progress.Revision)) return false;
            RaidApproachField.Instance.SummonParty();
            if (currentStage == 2)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("제2외곽 수호자 : 플로라 기어\n추모의 꽃을 짓밟는 자... 거름으로 박제한다...");
            if (currentStage == 3 && boss?.CampanellaEncounter == true)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("제3외곽 수호자 : 캄파넬라\n침묵을 유지하라... 열두 번째 종소리가 울릴 때 영원한 밤이 찾아온다...");
            if (currentStage == 4 && boss?.MarionetteEncounter == true)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("제4외곽 수호자 : 마리오네트\n오늘 밤 상연할 비극은, 자신들이 이미 죽은 줄도 모르고 시계탑을 오르는 어리석은 영웅들의 이야기입니다!");
            if (currentStage == 5 && boss?.JustitiaEncounter == true)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("제5외곽 수호자 : 유스티티아\n피고인 네 명은 시계탑의 봉인을 훼손하고 등가교환의 금기를 어겼다...");
            if(currentStage==6&&boss?.JanusEncounter==true)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("외곽 성채 최종 수호자 : 야누스 알파 & 오메가\n단장님의 최후 명령을 이행한다... 설령 그대들이 과거의 주인일지라도 이 문 너머로는 들여보낼 수 없다!");
            var layout = RaidApproachField.Instance.Arrival(currentStage);
            if (layout.bossAnimatorController != null)
                RaidStageCutscenePresentation.EnsureBossAnimator(boss, layout.bossAnimatorController);
            if (!BeginStagePresentation(RaidBattlePhase.Arrival, Progress.Revision))
                QuarterViewCamera.LocalCamera?.SetArrivalCamera(layout.cameraPosition, layout.cameraLookAt);
            UpdateStageHeaderUI(); TraceProgress("human entered field; party summoned"); return true;
        }
        internal void RevealPreparedBoss()
        {
            var approach = RaidApproachField.Instance;
            if (approach == null || approach.BossVisible || boss == null) return;
            approach.SetBossVisible(true);
            var arrival = approach.Arrival(currentStage);
            if (_activeCutscene == null || !_activeCutscene.HasBossAnimationTrack)
                PlayConfiguredBossTrigger(arrival.arrivalAnimationTrigger);
            UpdateStageHeaderUI(); TraceProgress("boss revealed");
        }
        private bool PlayConfiguredBossTrigger(string trigger)
        {
            if (boss == null || string.IsNullOrWhiteSpace(trigger)) return false;
            var controller = RaidApproachField.Instance?.Arrival(currentStage).bossAnimatorController;
            var animator = RaidStageCutscenePresentation.EnsureBossAnimator(boss, controller);
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (var parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == trigger)
                { animator.SetTrigger(trigger); return true; }
            Debug.LogWarning($"Boss cutscene trigger '{trigger}' is missing from the Stage {currentStage} Animator Controller; using fallback where available.");
            return false;
        }
        internal bool BeginPreparedCountdown()
        {
            if (!Progress.TryCountdown(Progress.Revision)) return false;
            RevealPreparedBoss();
            EndStagePresentation();
            QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
            UpdateStageHeaderUI(); TraceProgress("arrival ended; countdown started"); return true;
        }
        private IEnumerator PreparedCountdown(int revision)
        {
            yield return null; // Runtime-created actor Start callbacks complete before the countdown.
            if (!Progress.IsCurrent(revision, RaidBattlePhase.Loading) || !BeginPreparedApproach()) yield break;
            while (Progress.IsCurrent(revision, RaidBattlePhase.Approach) && RaidApproachField.Instance?.HumanEntered() != true)
                yield return null;
            if (!Progress.IsCurrent(revision, RaidBattlePhase.Approach) || !BeginPreparedArrival()) yield break;
            var arrival = RaidApproachField.Instance.Arrival(currentStage);
            float began = Time.unscaledTime;
            float revealAt = began + arrival.RevealAt;
            float ending = began + arrival.Duration;
            while (Progress.IsCurrent(revision, RaidBattlePhase.Arrival) && Time.unscaledTime < ending)
            {
                if (Time.unscaledTime >= revealAt) RevealPreparedBoss();
                if (!LocalGameplayInput.ModalCovered && LocalInputBindings.Pressed("Interact")) { RevealPreparedBoss(); EndStagePresentation(); ending = Time.unscaledTime; }
                yield return null;
            }
            if (!Progress.IsCurrent(revision, RaidBattlePhase.Arrival)) { EndStagePresentation(); QuarterViewCamera.LocalCamera?.ClearArrivalCamera(); yield break; }
            RevealPreparedBoss();
            if (!Progress.IsCurrent(revision, RaidBattlePhase.Arrival) || !BeginPreparedCountdown()) yield break;
            // Keep the preparation state transition, but enter combat in this frame.
            if (Progress.IsCurrent(revision, RaidBattlePhase.Countdown)) StartStage(currentStage);
        }

        [ContextMenu("Retry Current Boss (Play Mode)")]
        public void RetryCurrentBoss()
        {
            if (RaidCombatAuthority.Active) { RaidNetworkSession.Instance.RequestAction(9); return; }
            if (!Application.isPlaying || boss == null || !IsWiped || !PrepareEncounter(currentStage, true)) return;
            Time.timeScale = 1; StartCoroutine(PreparedCountdown(Progress.Revision));
        }

        // Resolve after all combat Updates. A kill and a wipe in one frame always award the clear once.
        private void LateUpdate()
        {
            if (RaidCombatAuthority.Replica) return;
            if (!_hasStartedEncounter || CombatSuspended || boss == null) return;
            // Spend available spectator coins before resolving a same-frame party wipe.
            RaidNetworkSession.Instance?.AiSpectatorRescueDead();
            int living = 0;
            var members = PartyLife.OrderedMembers();
            foreach (var member in members) if (CombatRange.LivingPartyMember(member)) living++;
            if (living == 0 && Iris.Pending > 0 && !_pendingBossClear && boss.currentHp > 0) return;
            var outcome = Progress.Resolve(Progress.Revision, _pendingBossClear || boss.currentHp <= 0, living, RaidCombatAuthority.Active ? Mathf.Max(4, members.Count) : members.Count);
            if (outcome != EncounterOutcome.None) TraceProgress(outcome.ToString());
            if (outcome == EncounterOutcome.Cleared)
            {
                _pendingBossClear = false;
                foreach (var member in members) CharacterActor.Ensure(member.gameObject).ClearedStage(currentStage);
                CombatMetricsManager.Instance?.Ledger.EndStage();
                CleanupAttempt();
                CampaignRuntime.Instance?.Cleared(currentStage);
                BeginBossDefeatCutscene();
                if (RaidCombatAuthority.Host) { RaidNetworkSession.Instance.NetworkEncounterOutcome(true); }
                else StartCoroutine(BossDefeatedSequenceRoutine(Progress.Revision));
            }
            else if (outcome == EncounterOutcome.Wiped)
            {
                IsWiped = true;
                CombatMetricsManager.Instance?.Ledger.FailAttempt();
                CleanupAttempt();
                _wipeUI.ShowWipe();
                if (RaidCombatAuthority.Host) RaidNetworkSession.Instance.NetworkEncounterOutcome(false);
            }
        }

        private void CleanupAttempt()
        {
            foreach(var paladin in FindObjectsByType<AegisPaladin>(FindObjectsSortMode.None))paladin.ClearTransient();
            foreach(var kit in FindObjectsByType<CharacterKit>(FindObjectsSortMode.None))kit.Clear();
            foreach(var status in FindObjectsByType<CombatStatusEffects>(FindObjectsSortMode.None))status.Clear();
            CombatMetricsManager.Instance?.Ledger.InvalidateEffects();
            // Scene/application teardown may destroy these native objects before this manager.
            // C# null-conditional calls do not respect Unity's destroyed-object null check.
            if (boss != null)
            {
                boss.StopEncounterEffects();
                if (boss.addSpawner != null) boss.addSpawner.ResetWaveFlagsAndClearAdds();
            }
            foreach (var effect in FindObjectsByType<AoETelegraph>(FindObjectsSortMode.None)) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            foreach (var effect in FindObjectsByType<HealingSanctuaryZone>(FindObjectsSortMode.None)) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            foreach (var effect in FindObjectsByType<RangedBotProjectile>(FindObjectsSortMode.None)) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            foreach (var cast in FindObjectsByType<BattleResurrection>(FindObjectsSortMode.None)) cast.Cancel();
            foreach (var combat in FindObjectsByType<PlayerCombatController>(FindObjectsSortMode.None)) combat.InterruptForDeath();
        }

        /// <summary>
        /// 보스 HP가 0이 되었을 때 BossController에서 호출됩니다.
        /// 1) 보스가 쓰러지는 연출 + "BOSS CLEAR!" 표시
        /// 2) 연출 후 카드/스킬 선택 UI (Stage 6은 최종 승리 결과창)
        /// </summary>
        public void NotifyBossDefeated()
        {
            if (!EncounterActive || _isStageTransitioning || _isAllStagesCleared)
            {
                return;
            }

            _pendingBossClear = true;
        }

        internal void BeginBossDefeatCutscene()
        {
            _isStageTransitioning = true;
            RaidApproachField.Instance?.SetPortalActive(false);
            RaidApproachField.Instance?.SetBossCombatReady(false);
            var layout = RaidApproachField.Instance?.Defeat(currentStage);
            var arrival = RaidApproachField.Instance?.Arrival(currentStage);
            if (arrival?.bossAnimatorController != null)
                RaidStageCutscenePresentation.EnsureBossAnimator(boss, arrival.bossAnimatorController);
            bool presenting = BeginStagePresentation(RaidBattlePhase.BossClear, Progress.Revision);
            bool animated = presenting && _activeCutscene != null && _activeCutscene.HasBossAnimationTrack;
            if (!animated && layout != null) animated = PlayConfiguredBossTrigger(layout.deathAnimationTrigger);
            if (!animated) boss?.PlayBossCollapseAnimation();
            if(currentStage==3 && boss?.CampanellaEncounter==true)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("종치와 구동부가 멈춥니다. 대종은 더 이상 울리지 않습니다.");
            if(currentStage==4 && boss?.MarionetteEncounter==true)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("황금 실이 끊어지고 인형극의 막이 내립니다.");
            if(currentStage==5 && boss?.JustitiaEncounter==true)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("천칭의 움직임이 멎고 재판이 끝납니다. 성소 XII시 관 개방을 확인하세요.");
            if(currentStage==6&&boss?.JanusEncounter==true)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("두 문지기의 움직임이 멎고 붉은 봉인 사슬이 끊어집니다. 1부 최종 결과를 확인하세요.");
            if (layout != null)
            {
                if (!presenting)
                    QuarterViewCamera.LocalCamera?.SetArrivalCamera(layout.cameraPosition, layout.cameraLookAt);
            }
            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(true);
            if (bossClearBannerText != null) bossClearBannerText.text = currentStage == maxStage
                ? $"FINAL STAGE {maxStage} BOSS CLEAR!" : $"STAGE {currentStage} BOSS CLEAR!";
        }
        internal void EndBossDefeatCutscene()
        {
            EndStagePresentation();
            RaidApproachField.Instance?.SetBossVisible(false);
            TargetingManager.Instance?.ClearTarget();
            if (RaidCombatAuthority.Host)
                foreach (var avatar in RaidNetworkSession.Instance.Avatars.Values) avatar.CombatTarget = null;
            QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(false);
        }

        private IEnumerator BossDefeatedSequenceRoutine(int revision)
        {
            var layout = RaidApproachField.Instance?.Defeat(currentStage);
            float began = Time.unscaledTime;
            float end = began + (layout?.Duration ?? FlowSettings.clearSeconds);
            float vanish = began + (layout?.DepartureAt ?? 1);
            while (Progress.IsCurrent(revision, RaidBattlePhase.BossClear) && Time.unscaledTime < end)
            {
                if (Time.unscaledTime >= vanish) RaidApproachField.Instance?.SetBossVisible(false);
                if (!LocalGameplayInput.ModalCovered && LocalInputBindings.Pressed("Interact")) break;
                yield return null;
            }
            if (!Progress.IsCurrent(revision, RaidBattlePhase.BossClear)) { EndStagePresentation(); QuarterViewCamera.LocalCamera?.ClearArrivalCamera(); yield break; }
            EndBossDefeatCutscene();
            while (Progress.IsCurrent(revision, RaidBattlePhase.BossClear) && CampaignRuntime.Instance?.MarkClearPresentationFinished() == false)
                yield return null;
            if (!Progress.FinishClear(revision, maxStage,HasPendingSkillChoices)) yield break;
            TraceProgress("clear presentation completed");

            // 최종 스테이지는 다음 전투용 보상을 요구하지 않는다.
            if (Progress.Phase==RaidBattlePhase.Victory)
            {
                ShowAllStagesClearVictoryScreen();
                yield break;
            }

            // Stage 1~5는 기존 카드 보상을 유지한다.
            _singleRewardRevision=revision;_singleCardChosen=currentStage>=maxStage;
            _singleRewardDeadline=Time.unscaledTime+FlowSettings.rewardSeconds;
            if (!_singleCardChosen&&cardManager != null)
            {
                cardManager.OpenStageClearCardReward(currentStage, () => OnRewardCardChosenAdvanceToNextStage(revision));
            }
            else if(!_singleCardChosen)
            {
                RaidNetworkSession.Instance?.FailSession("카드 보상 UI를 찾을 수 없습니다. 원본 씬의 자동 구성을 확인하세요.");
            }
        }

        /// <summary>
        /// 3) 플레이어가 보상 카드 1장을 선택하면 호출됩니다:
        /// - 4인 파티원 전원 체력 100% 회복 및 초기 위치로 재배치
        /// - 필요한 선택 완료 후 다음 스테이지 준비/저장/카운트다운
        /// </summary>
        private void OnRewardCardChosenAdvanceToNextStage(int revision)
        {
            if(Progress.IsCurrent(revision,RaidBattlePhase.Reward)&&revision==_singleRewardRevision)_singleCardChosen=true;
        }
        private void UpdateSingleRewards()
        {
            if(RaidCombatAuthority.Active||!Progress.IsCurrent(_singleRewardRevision,RaidBattlePhase.Reward))return;
            bool expired=Time.unscaledTime>=_singleRewardDeadline;
            if(expired&&!_singleCardChosen)cardManager?.AutoChooseSingleCard();
            foreach(var member in PartyLife.OrderedMembers())
            {
                var actor=CharacterActor.Ensure(member.gameObject);
                if(member.GetComponent<PartyBotAI>()!=null||expired)foreach(var choice in actor.PendingChoices)actor.Choose(choice.id,choice.choices[0],out _,actor.ChoiceKey(choice.id),true);
            }
            if(!_singleCardChosen||HasPendingSkillChoices)return;
            if (CampaignRuntime.Instance?.SyncSettlement() == false) return;
            if (currentStage < maxStage && CampaignRuntime.Instance?.CommitNextStageBoundary(currentStage + 1) == false) return;
            _singleRewardRevision=-1;Time.timeScale=1;
            if(currentStage>=maxStage){if(Progress.FinishFinalRewards(Progress.Revision,maxStage))ShowAllStagesClearVictoryScreen();return;}
            RaidApproachField.Instance?.SetPortalActive(true);
            UpdateStageHeaderUI(); TraceProgress("rewards complete; return portal opened");
        }
        public void EndSessionProgress()
        {
            EndStagePresentation();
            if (this == null || !Progress.End()) return;
            StopAllCoroutines(); CancelInvoke(); CleanupAttempt();
            if (cardManager != null) cardManager.CancelPendingSelection();
            var metricsUI = GetComponent<CombatMetricsUI>();
            if (metricsUI != null) metricsUI.CloseResults();
            if (_wipeUI != null) _wipeUI.Hide();
            RaidApproachField.Instance?.Show(false);
            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(false);
            if (victoryModalRoot != null) victoryModalRoot.SetActive(false);
            TraceProgress("session ended");
        }
        private void TraceProgress(string reason)
            => Debug.Log($"[Raid {RaidNetworkSession.Instance?.SessionTag ?? "single"}] stage={currentStage} attempt={Progress.Revision} phase={Progress.Phase}: {reason}");

        public void ResetAndHealAllPartyMembers()
        {
            var players = RaidCombatAuthority.Active ? PartyLife.OrderedMembers().FindAll(t => t.GetComponent<PlayerController>() != null) : new System.Collections.Generic.List<Transform>();
            if (!RaidCombatAuthority.Active && player != null) players.Add(player.transform);
            foreach (var member in players)
            {
                var pc = member.GetComponent<PlayerController>();
                Vector3 spawn = pc.currentRole == PlayerRole.Guardian_Tank ? tankSpawnPos : pc.currentRole == PlayerRole.Oracle_Healer ? healerSpawnPos : pc.currentRole == PlayerRole.Archmage_Caster ? rangedSpawnPos : playerSpawnPos;
                pc.ResetToFullHealthAndPosition(spawn);
            }

            // 3명의 AI 파티원 봇 전원 부활/체력 100% 회복 및 각자 역할군 초기 위치 재배치
            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot == null) continue;

                Vector3 spawnPos = bot.role switch
                {
                    PartyBotRole.Tank => tankSpawnPos,
                    PartyBotRole.Healer => healerSpawnPos,
                    PartyBotRole.MeleeDPS => playerSpawnPos,
                    PartyBotRole.RangedDPS => rangedSpawnPos,
                    _ => bot.transform.position
                };

                bot.ResetBotForNewStage(spawnPos);
            }
        }

        private void ShowAllStagesClearVictoryScreen()
        {
            CampaignRuntime.Instance?.Completed();
            _isAllStagesCleared = true;
            _isStageTransitioning = false;
            if (!RaidCombatAuthority.Active) Time.timeScale = 1f;

            int minutes = Mathf.FloorToInt(_totalPlayTime / 60f);
            float seconds = _totalPlayTime % 60f;
            string timeFormatted = $"{minutes:00}m {seconds:00.0}s";

            string cardsList = cardManager != null
                ? cardManager.GetAcquiredCardsDetailedList()
                : "No cards acquired";

            if (victorySummaryText != null)
            {
                victorySummaryText.text =
                    "<size=42><color=#FFE044><b>PART 1 CLEAR!</b></color></size>\n\n" +
                    $"<size=24><b>Play time:</b> <color=#55EEFF>{timeFormatted}</color></size>\n\n" +
                    $"<size=22>Retries: {CombatMetricsManager.Instance?.Ledger.RetryCount ?? 0}</size>\n" +
                    $"<size=22>Shared Iris revives used: {TotalRevives}</size>\n" +
                    "<size=22><color=#FFCC66><b>Final card build</b></color></size>\n" +
                    $"<size=20>{cardsList}</size>";
            }

            if (victoryModalRoot != null)
            {
                victoryModalRoot.SetActive(true);
            }
            if (restartButton != null)
            {
                var caption = restartButton.GetComponentInChildren<Text>();
                if (caption != null) caption.text = "Close results - return at the central portal";
            }
            GetComponent<CombatMetricsUI>()?.ShowResults(true);
        }

        public void RestartFromStage1()
        {
            var session = RaidNetworkSession.Instance;
            if (session != null && (Progress.Phase == RaidBattlePhase.Victory ||
                RaidNetworkSession.Multiplayer && session.BattlePhase == RaidBattlePhase.Victory))
            {
                CloseVictoryForPortal();
                return;
            }
            session?.Disconnect();
        }

        public void CloseVictoryForPortal()
        {
            if (Progress.Phase != RaidBattlePhase.Victory && !(RaidCombatAuthority.Replica && RaidNetworkSession.Instance?.BattlePhase == RaidBattlePhase.Victory)) return;
            if (!RaidCombatAuthority.Replica && CampaignRuntime.Instance?.HasSaveFailure == true) return;
            if (victoryModalRoot != null) victoryModalRoot.SetActive(false);
            GetComponent<CombatMetricsUI>()?.CloseResults();
            Time.timeScale = 1;
            if (!RaidCombatAuthority.Replica) RaidApproachField.Instance?.SetPortalActive(true);
            UpdateStageHeaderUI();
        }

        public void UpdateStageHeaderUI()
        {
            if (stageHeaderText == null)
            {
                return;
            }

            int template = CampaignCatalog.EncounterTemplate(currentStage);
            string stageSubtitle = currentStage switch
            {
                1 => "마을 수호 석상",
                2 => "플로라 기어",
                3 => "캄파넬라",
                4 => "마리오네트",
                5 => "유스티티아",
                6 => "야누스",
                _ => ""
            };
            stageSubtitle += CampaignRuntime.Instance?.DifficultyId == CampaignCatalog.Hard ? " · 하드" : " · 노멀";
            Color stageColor = template switch
            {
                1 => new Color(0.85f, 0.65f, 1.0f, 1f),
                2 => new Color(0.30f, 0.95f, 1.0f, 1f),
                3 => new Color(1.0f, 0.82f, 0.25f, 1f),
                _ => Color.white
            };

            stageHeaderText.text = $"1부 STAGE {currentStage} / {maxStage}  —  {stageSubtitle}";
            if ((RaidNetworkSession.Multiplayer ? RaidNetworkSession.Instance.BattlePhase : Progress.Phase) == RaidBattlePhase.Approach)
                stageHeaderText.text += "  -  필드 입구로 이동";
            if ((RaidNetworkSession.Multiplayer ? RaidNetworkSession.Instance.BattlePhase : Progress.Phase) == RaidBattlePhase.Arrival)
                stageHeaderText.text += "  -  보스 등장 (상호작용: 건너뛰기 투표)";
            if ((RaidNetworkSession.Multiplayer ? RaidNetworkSession.Instance.BattlePhase : Progress.Phase) == RaidBattlePhase.BossClear)
                stageHeaderText.text += "  ·  처치 연출 중 (상호작용: 건너뛰기)";
            if (RaidApproachField.Instance?.PortalActive == true)
                stageHeaderText.text += "  ·  중앙 귀환 포탈 [상호작용]";
            RaidUIStyle.Color(stageHeaderText, stageColor);
        }
    }
}
