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
    public class StageManager : MonoBehaviour
    {
        [Header("Stage Progression")]
        public int currentStage = 1;
        public int maxStage = 3;

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

        public bool IsStageTransitioning => _isStageTransitioning;
        public float TotalPlayTime => _totalPlayTime;

        private void Start()
        {
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

            if (!_isStageTransitioning && Time.timeScale > 0.01f)
            {
                _totalPlayTime += Time.deltaTime;
            }
        }

        /// <summary>
        /// 지정한 스테이지(1~3)의 보스 외형/크기/체력/패턴을 세팅하고 전투를 시작합니다.
        /// </summary>
        public void StartStage(int stageNumber, bool resetPlayTime = false)
        {
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
                boss.SetupBossForStage(currentStage);
            }
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

            StartCoroutine(BossDefeatedSequenceRoutine());
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
            // 내 캐릭터 체력 100% 회복 및 초기 위치 재배치
            if (player != null)
            {
                player.ResetToFullHealthAndPosition(playerSpawnPos);
            }

            // 3명의 AI 파티원 봇 전원 부활/체력 100% 회복 및 초기 위치 재배치
            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot == null) continue;

                Vector3 spawnPos = bot.role switch
                {
                    PartyBotRole.Tank => tankSpawnPos,
                    PartyBotRole.Healer => healerSpawnPos,
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
                    "<size=22><color=#FFCC66><b>[최종 보유 로그라이크 카드 빌드 요약]</b></color></size>\n" +
                    $"<size=20>{cardsList}</size>";
            }

            if (victoryModalRoot != null)
            {
                victoryModalRoot.SetActive(true);
            }
        }

        public void RestartFromStage1()
        {
            Time.timeScale = 1f;
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.buildIndex >= 0 ? activeScene.name : activeScene.path);
        }

        private void HandleVictoryModalClickFallback()
        {
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
