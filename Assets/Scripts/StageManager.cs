using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>
    /// 다단계 스테이지(Stage 1 -> Stage 2 -> Stage 3 최종 보스) 보스 러시 루프 및
    /// 보스 처치 연출 -> 1.5초 뒤 로그라이크 카드 3택 보상 -> 파티 정비/재배치 -> 다음 보스 등장을 총괄하는 매니저 (Step 6).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public partial class StageManager : MonoBehaviour
    {
        [Header("Stage Progression")]
        public int currentStage = 1;
        public int maxStage = 3;

        [Header("Common Potion (shared by all four party members)")]
        public PotionSettings potionSettings = new PotionSettings();
        public ResurrectionSettings resurrectionSettings = new ResurrectionSettings();
        public static StageManager Instance { get; private set; }
        public static bool CombatSuspended => RaidNetworkSession.CombatPreview || Instance != null && (!Instance.EncounterActive || Instance.IsWiped || Instance.IsPreparing || Instance._isStageTransitioning || Instance._isAllStagesCleared);
        public bool IsWiped { get; private set; }
        public bool IsPreparing { get; private set; }
        internal readonly RaidRunFlow Progress = new RaidRunFlow();
        public readonly RaidFlowSettings FlowSettings = new RaidFlowSettings();
        public bool EncounterActive => RaidCombatAuthority.Replica ? RaidNetworkSession.Instance.CombatRunning : Progress.Phase == RaidBattlePhase.Combat;
        public bool CanSelectInitialRole => !_hasStartedEncounter && Progress.Phase == RaidBattlePhase.Lobby && RaidNetworkSession.Instance?.Mode == RaidSessionMode.Single;
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

        public bool IsStageTransitioning => _isStageTransitioning || IsWiped || IsPreparing;
        public float TotalPlayTime => _totalPlayTime;

        private void Awake() { Instance = this; }
        private void OnDestroy() { EndSessionProgress(); if (Instance == this) Instance = null; }

        private void Start()
        {
            RaidNetworkSession.Ensure(this);
            _wipeUI = GetComponent<PartyWipeUI>() ?? gameObject.AddComponent<PartyWipeUI>();
            if (boss == null) boss = FindFirstObjectByType<BossController>();
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (cardManager == null) cardManager = FindFirstObjectByType<RoguelikeCardManager>();

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
            IsWiped = IsPreparing = _pendingBossClear = _isStageTransitioning = _isAllStagesCleared = false;
            _hasStartedEncounter = true;
            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(false);
            if (victoryModalRoot != null) victoryModalRoot.SetActive(false);
            _wipeUI.Hide(); UpdateStageHeaderUI();
            CombatMetricsManager.Ensure(this).BeginStage(currentStage, false);
            QuarterViewCamera.LocalCamera?.BeginCombat(currentStage == 1 && CombatMetricsManager.Instance.Ledger.RetryCount == 0);
            TraceProgress("countdown completed");
        }

        public void BeginSingleRun()
        {
            if (!CanSelectInitialRole || !PrepareEncounter(1, false, true)) return;
            StartCoroutine(PreparedCountdown(Progress.Revision));
        }
        private bool PrepareEncounter(int stage, bool retry, bool newRun = false)
        {
            if (boss == null || player == null || cardManager == null || cardManager.cardSelectionModalRoot == null)
            { RaidNetworkSession.Instance?.FailSession("전투/보상 참조가 누락되었습니다. 원본 프로젝트의 SceneAutoBuilder 구성을 확인하세요."); return false; }
            if (!Progress.TryPrepare(stage, maxStage, retry)) return false;
            var metrics = CombatMetricsManager.Ensure(this);
            if (newRun) { metrics.Ledger.ResetRun(); _totalPlayTime = 0; }
            if (retry) metrics.Ledger.RegisterRetry();
            currentStage = stage; IsPreparing = true; IsWiped = false;
            _isStageTransitioning = _isAllStagesCleared = _pendingBossClear = false;
            CleanupAttempt(); GetComponent<CombatMetricsUI>()?.CloseResults(); cardManager?.CancelPendingSelection();
            ResetAndHealAllPartyMembers();
            foreach (var member in PartyLife.OrderedMembers())
            {
                PartyLife.Ensure(member.gameObject).ResetForEncounter();
                member.GetComponent<PlayerController>()?.cardManager?.ResetTemporaryCombatState();
            }
            boss.SetupBossForStage(stage); UpdateStageHeaderUI(); TraceProgress(retry ? "retry prepared; cards retained" : "stage prepared");
            return true;
        }
        internal bool BeginPreparedCountdown()
        {
            if (!Progress.TryCountdown(Progress.Revision)) return false;
            TraceProgress("all participants loaded"); return true;
        }
        private IEnumerator PreparedCountdown(int revision)
        {
            yield return null; // Runtime-created actor Start callbacks complete before the countdown.
            if (!Progress.IsCurrent(revision, RaidBattlePhase.Loading) || !BeginPreparedCountdown()) yield break;
            for (int seconds = FlowSettings.countdownSeconds; seconds > 0; seconds--)
            {
                if (!Progress.IsCurrent(revision, RaidBattlePhase.Countdown)) yield break;
                _wipeUI.ShowCountdown(seconds);
                float remaining = 1;
                while (remaining > 0)
                {
                    if (!Progress.IsCurrent(revision, RaidBattlePhase.Countdown)) yield break;
                    if (RaidNetworkSession.Instance?.MenuOpen != true) remaining -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }
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
            int living = 0;
            var members = PartyLife.OrderedMembers();
            foreach (var member in members) if (CombatRange.LivingPartyMember(member)) living++;
            var outcome = Progress.Resolve(Progress.Revision, _pendingBossClear || boss.currentHp <= 0, living, RaidCombatAuthority.Active ? Mathf.Max(4, members.Count) : members.Count);
            if (outcome != EncounterOutcome.None) TraceProgress(outcome.ToString());
            if (outcome == EncounterOutcome.Cleared)
            {
                _pendingBossClear = false;
                CombatMetricsManager.Instance?.Ledger.EndStage();
                CleanupAttempt();
                if (RaidCombatAuthority.Host) { boss.PlayBossCollapseAnimation(); RaidNetworkSession.Instance.NetworkEncounterOutcome(true); }
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
            CombatMetricsManager.Instance?.Ledger.InvalidateEffects();
            boss?.StopEncounterEffects();
            boss?.addSpawner?.ResetWaveFlagsAndClearAdds();
            foreach (var effect in FindObjectsByType<AoETelegraph>(FindObjectsSortMode.None)) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            foreach (var effect in FindObjectsByType<HealingSanctuaryZone>(FindObjectsSortMode.None)) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            foreach (var effect in FindObjectsByType<RangedBotProjectile>(FindObjectsSortMode.None)) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            foreach (var cast in FindObjectsByType<BattleResurrection>(FindObjectsSortMode.None)) cast.Cancel();
            foreach (var combat in FindObjectsByType<PlayerCombatController>(FindObjectsSortMode.None)) combat.InterruptForDeath();
        }

        /// <summary>
        /// 보스 HP가 0이 되었을 때 BossController에서 호출됩니다.
        /// 1) 보스가 쓰러지는 연출 + "BOSS CLEAR!" 표시
        /// 2) 1.5초 뒤 로그라이크 카드 3택 UI 팝업 (Stage 3 클리어 시 최종 승리 결과창 팝업)
        /// </summary>
        public void NotifyBossDefeated()
        {
            if (!EncounterActive || _isStageTransitioning || _isAllStagesCleared)
            {
                return;
            }

            _pendingBossClear = true;
        }

        private IEnumerator BossDefeatedSequenceRoutine(int revision)
        {
            _isStageTransitioning = true;

            // 1) 보스가 쓰러지는 연출 및 화면 중앙 "BOSS CLEAR!" 텍스트 표시
            if (boss != null)
            {
                boss.PlayBossCollapseAnimation();
            }

            if (bossClearBannerRoot != null)
            {
                bossClearBannerRoot.SetActive(true);
            }
            if (bossClearBannerText != null)
            {
                bossClearBannerText.text = currentStage < maxStage
                    ? $"★ STAGE {currentStage} BOSS CLEAR! ★\n<size=24>잠시 후 클리어 보상 로그라이크 카드를 선택합니다...</size>"
                    : $"★ FINAL STAGE {maxStage} BOSS CLEAR!! ★\n<size=24>모든 스테이지 보스를 토벌했습니다!</size>";
            }

            // 2) 1.5초 대기
            yield return new WaitForSeconds(FlowSettings.clearSeconds);
            if (!Progress.FinishClear(revision, maxStage)) yield break;
            TraceProgress("clear presentation completed");

            if (bossClearBannerRoot != null)
            {
                bossClearBannerRoot.SetActive(false);
            }

            // Stage 3 최종 보스까지 처치한 경우 -> 최종 승리 결과창 표시!
            if (currentStage >= maxStage)
            {
                ShowAllStagesClearVictoryScreen();
                yield break;
            }

            // Stage 1 또는 Stage 2 클리어 시 -> 로그라이크 카드 3택 보상 UI 팝업!
            if (cardManager != null)
            {
                cardManager.OpenStageClearCardReward(currentStage, () => OnRewardCardChosenAdvanceToNextStage(revision));
            }
            else
            {
                RaidNetworkSession.Instance?.FailSession("카드 보상 UI를 찾을 수 없습니다. 원본 씬의 자동 구성을 확인하세요.");
            }
        }

        /// <summary>
        /// 3) 플레이어가 보상 카드 1장을 선택하면 호출됩니다:
        /// - 4인 파티원 전원 체력 100% 회복 및 초기 위치로 재배치
        /// - 즉시 다음 스테이지(Stage 2 / Stage 3) 보스 등장 및 전투 시작!
        /// </summary>
        private void OnRewardCardChosenAdvanceToNextStage(int revision)
        {
            if (!Progress.IsCurrent(revision, RaidBattlePhase.Reward) || !PrepareEncounter(currentStage + 1, false)) return;
            StartCoroutine(PreparedCountdown(Progress.Revision));
        }
        public void EndSessionProgress()
        {
            if (!Progress.End()) return;
            StopAllCoroutines(); CancelInvoke(); CleanupAttempt();
            cardManager?.CancelPendingSelection(); GetComponent<CombatMetricsUI>()?.CloseResults(); _wipeUI?.Hide();
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
            _isAllStagesCleared = true;
            _isStageTransitioning = false;
            if (!RaidCombatAuthority.Active) Time.timeScale = 0f;

            int minutes = Mathf.FloorToInt(_totalPlayTime / 60f);
            float seconds = _totalPlayTime % 60f;
            string timeFormatted = $"{minutes:00}분 {seconds:00.0}초";

            string cardsList = cardManager != null
                ? cardManager.GetAcquiredCardsDetailedList()
                : "• 획득한 카드 없음";

            if (victorySummaryText != null)
            {
                victorySummaryText.text =
                    "<size=42><color=#FFE044><b>★ ALL STAGES CLEAR! VICTORY! ★</b></color></size>\n\n" +
                    $"<size=24><b>총 플레이 타임 :</b> <color=#55EEFF>{timeFormatted}</color></size>\n\n" +
                    $"<size=22>전체 런 재도전: {CombatMetricsManager.Instance?.Ledger.RetryCount ?? 0}회</size>\n" +
                    "<size=22><color=#FFCC66><b>[최종 보유 로그라이크 카드 빌드 요약]</b></color></size>\n" +
                    $"<size=20>{cardsList}</size>";
            }

            if (victoryModalRoot != null)
            {
                victoryModalRoot.SetActive(true);
            }
            GetComponent<CombatMetricsUI>()?.ShowResults(true);
        }

        public void RestartFromStage1()
        {
            if (LocalGameplayInput.ModalCovered) return;
            RaidNetworkSession.Instance?.Disconnect(); // A new run is a fresh scene/session with no retained cards or statics.
        }

        public void UpdateStageHeaderUI()
        {
            if (stageHeaderText == null)
            {
                return;
            }

            string stageSubtitle = currentStage switch
            {
                1 => "공허의 파수꾼 (입문 - 보라색 / HP 3,000)",
                2 => "심연의 집행자 (중급 - 청록색 1.2배 / HP 6,000 / 4인 산개 & 4인 쉐어)",
                3 => "종말의 황제 (최종 보스 - 적금색 1.5배 / HP 10,000 / 시전 +25% 가속 & 복합 기믹)",
                _ => ""
            };

            Color stageColor = currentStage switch
            {
                1 => new Color(0.85f, 0.65f, 1.0f, 1f),
                2 => new Color(0.30f, 0.95f, 1.0f, 1f),
                3 => new Color(1.0f, 0.82f, 0.25f, 1f),
                _ => Color.white
            };

            stageHeaderText.text = $"STAGE {currentStage} / {maxStage}  —  {stageSubtitle}";
            stageHeaderText.color = stageColor;
        }
    }
}
