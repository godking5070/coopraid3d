using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    /// <summary>
    /// 다단계 스테이지(Stage 1 -> Stage 2 -> Stage 3 최종 보스) 보스 러시 루프 및
    /// 보스 처치 연출 -> 1.5초 뒤 로그라이크 카드 3택 보상 -> 파티 정비/재배치 -> 다음 보스 등장을 총괄하는 매니저 (Step 6).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class StageManager : MonoBehaviour
    {
        [Header("Stage Progression")]
        public int currentStage = 1;
        public int maxStage = 3;

        [Header("Common Potion (shared by all four party members)")]
        public PotionSettings potionSettings = new PotionSettings();
        public ResurrectionSettings resurrectionSettings = new ResurrectionSettings();
        public static StageManager Instance { get; private set; }
        public static bool CombatSuspended => RaidNetworkSession.CombatPreview || Instance != null && (Instance.IsWiped || Instance.IsPreparing || Instance._isStageTransitioning || Instance._isAllStagesCleared);
        public bool IsWiped { get; private set; }
        public bool IsPreparing { get; private set; }
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
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            RaidNetworkSession.Ensure(this);
            _wipeUI = gameObject.AddComponent<PartyWipeUI>();
            if (boss == null) boss = FindFirstObjectByType<BossController>();
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (cardManager == null) cardManager = FindFirstObjectByType<RoguelikeCardManager>();

            if (bossClearBannerRoot != null) bossClearBannerRoot.SetActive(false);
            if (victoryModalRoot != null) victoryModalRoot.SetActive(false);

            if (restartButton != null)
            {
                restartButton.onClick.RemoveAllListeners();
                restartButton.onClick.AddListener(RestartFromStage1);
            }

            StartStage(1, resetPlayTime: true);
        }

        private void Update()
        {
            if (_isAllStagesCleared)
            {
                HandleVictoryModalClickFallback();
                return;
            }

            if (!CombatSuspended && Time.timeScale > 0.01f)
            {
                _totalPlayTime += Time.deltaTime;
            }
        }

        /// <summary>
        /// 지정한 스테이지(1~3)의 보스 외형/크기/체력/패턴을 세팅하고 전투를 시작합니다.
        /// </summary>
        public void StartStage(int stageNumber, bool resetPlayTime = false)
        {
            IsWiped = IsPreparing = _pendingBossClear = false;
            int nextStage = Mathf.Clamp(stageNumber, 1, maxStage);
            bool retryingCurrentBoss = _hasStartedEncounter && currentStage == nextStage;
            if (resetPlayTime || retryingCurrentBoss)
            {
                // Initial run/retry resets all members, including dead bots. Normal revival and
                // advancing to a different stage preserve their remaining item cooldowns.
                if (player != null) CommonPotion.Ensure(player.gameObject).ResetForBossRetry();
                foreach (var bot in FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None))
                    CommonPotion.Ensure(bot.gameObject).ResetForBossRetry();
            }
            _hasStartedEncounter = true;
            currentStage = Mathf.Clamp(stageNumber, 1, maxStage);
            _isStageTransitioning = false;
            _isAllStagesCleared = false;

            if (resetPlayTime)
            {
                _totalPlayTime = 0f;
            }

            if (bossClearBannerRoot != null)
            {
                bossClearBannerRoot.SetActive(false);
            }
            if (victoryModalRoot != null)
            {
                victoryModalRoot.SetActive(false);
            }

            UpdateStageHeaderUI();

            if (boss != null)
            {
                if (retryingCurrentBoss && boss.addSpawner != null)
                    boss.addSpawner.ResetWaveFlagsAndClearAdds();
                boss.SetupBossForStage(currentStage);
            }
            CombatMetricsManager.Ensure(this).BeginStage(currentStage, resetPlayTime);
            QuarterViewCamera.LocalCamera?.BeginCombat(resetPlayTime);
        }

        [ContextMenu("Retry Current Boss (Play Mode)")]
        public void RetryCurrentBoss()
        {
            if (!Application.isPlaying || boss == null || IsPreparing || _isStageTransitioning || _isAllStagesCleared || _pendingBossClear || boss.currentHp <= 0) return;
            var metrics = CombatMetricsManager.Ensure(this);
            if (!IsWiped) metrics.Ledger.FailAttempt();
            metrics.Ledger.RegisterRetry();
            IsPreparing = true; IsWiped = false;
            StartCoroutine(RetryRoutine());
        }

        private IEnumerator RetryRoutine()
        {
            GetComponent<CombatMetricsUI>()?.CloseResults();
            Time.timeScale = 1;
            CleanupAttempt();
            ResetAndHealAllPartyMembers();
            foreach (var member in PartyLife.OrderedMembers()) PartyLife.Ensure(member.gameObject).ResetForEncounter();
            cardManager?.ResetTemporaryCombatState();
            boss.SetupBossForStage(currentStage);
            for (int seconds = 3; seconds > 0; seconds--)
            {
                _wipeUI.ShowCountdown(seconds);
                yield return new WaitForSecondsRealtime(1);
            }
            _wipeUI.Hide();
            StartStage(currentStage);
        }

        // Resolve after all combat Updates. A kill and a wipe in one frame always award the clear once.
        private void LateUpdate()
        {
            if (!_hasStartedEncounter || CombatSuspended || boss == null) return;
            int living = 0;
            var members = PartyLife.OrderedMembers();
            foreach (var member in members) if (CombatRange.LivingPartyMember(member)) living++;
            var outcome = PartyBattleRules.Resolve(_pendingBossClear || boss.currentHp <= 0, living, members.Count);
            if (outcome == EncounterOutcome.Cleared)
            {
                _pendingBossClear = false;
                CombatMetricsManager.Instance?.Ledger.EndStage();
                CleanupAttempt();
                StartCoroutine(BossDefeatedSequenceRoutine());
            }
            else if (outcome == EncounterOutcome.Wiped)
            {
                IsWiped = true;
                CombatMetricsManager.Instance?.Ledger.FailAttempt();
                CleanupAttempt();
                _wipeUI.ShowWipe();
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
        }

        /// <summary>
        /// 보스 HP가 0이 되었을 때 BossController에서 호출됩니다.
        /// 1) 보스가 쓰러지는 연출 + "BOSS CLEAR!" 표시
        /// 2) 1.5초 뒤 로그라이크 카드 3택 UI 팝업 (Stage 3 클리어 시 최종 승리 결과창 팝업)
        /// </summary>
        public void NotifyBossDefeated()
        {
            if (_isStageTransitioning || _isAllStagesCleared)
            {
                return;
            }

            _pendingBossClear = true;
        }

        private IEnumerator BossDefeatedSequenceRoutine()
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
                    : "★ FINAL STAGE 3 BOSS CLEAR!! ★\n<size=24>모든 스테이지 보스를 토벌했습니다!</size>";
            }

            // 2) 1.5초 대기
            yield return new WaitForSeconds(1.5f);

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
                cardManager.OpenStageClearCardReward(currentStage, OnRewardCardChosenAdvanceToNextStage);
            }
            else
            {
                OnRewardCardChosenAdvanceToNextStage();
            }
        }

        /// <summary>
        /// 3) 플레이어가 보상 카드 1장을 선택하면 호출됩니다:
        /// - 4인 파티원 전원 체력 100% 회복 및 초기 위치로 재배치
        /// - 즉시 다음 스테이지(Stage 2 / Stage 3) 보스 등장 및 전투 시작!
        /// </summary>
        private void OnRewardCardChosenAdvanceToNextStage()
        {
            ResetAndHealAllPartyMembers();

            int nextStage = Mathf.Min(maxStage, currentStage + 1);
            StartStage(nextStage, resetPlayTime: false);
        }

        public void ResetAndHealAllPartyMembers()
        {
            // 내 캐릭터 체력 100% 회복 및 현재 직업에 맞는 초기 위치 재배치
            if (player != null)
            {
                Vector3 pSpawn = player.currentRole switch
                {
                    PlayerRole.Guardian_Tank => tankSpawnPos,
                    PlayerRole.Oracle_Healer => healerSpawnPos,
                    PlayerRole.Archmage_Caster => rangedSpawnPos,
                    _ => playerSpawnPos
                };
                player.ResetToFullHealthAndPosition(pSpawn);
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
            Time.timeScale = 0f;

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
            if (CombatMetricsUI.BlocksUnderlyingInput) return;
            CombatMetricsManager.Instance?.Ledger.ResetRun();
            Time.timeScale = 1f;
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.buildIndex >= 0 ? activeScene.name : activeScene.path);
        }

        private void HandleVictoryModalClickFallback()
        {
            if (CombatMetricsUI.BlocksUnderlyingInput) return;
            if (victoryModalRoot == null || !victoryModalRoot.activeSelf || restartButton == null)
            {
                return;
            }

            bool mouseClicked = false;
            Vector2 mousePos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                mouseClicked = true;
                mousePos = Mouse.current.position.ReadValue();
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!mouseClicked && Input.GetMouseButtonDown(0))
            {
                mouseClicked = true;
                mousePos = Input.mousePosition;
            }
#endif

            if (mouseClicked)
            {
                RectTransform rt = restartButton.GetComponent<RectTransform>();
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
                {
                    RestartFromStage1();
                }
            }
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
