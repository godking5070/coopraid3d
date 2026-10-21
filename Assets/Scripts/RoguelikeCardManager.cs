using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public enum RoguelikeCardId
    {
        MasterOfRear,    // 1) [후면의 지배자]: 후면 기습 성공 시 피해량 50% 추가 증가 및 쿨타임 초기화
        GhostStep,       // 2) [유령 걸음]: 이동속도 30% 증가 및 측·후면 판정 각도가 15도 넓어짐
        ThornArmor,      // 3) [가시 갑옷]: 줄 가로채기 후 생존기로 방어 성공 시 보스에게 큰 반사 피해
        TacticalRescue,  // 4) [전술적 구출]: 힐러에게 구출로 끌려갈 경우 5초간 공격력 2배 증가
        BerserkerShout,  // 5) [광전사의 함성]: 10초마다 파티 전체 공격속도 증가 버프 자동 발동
        VampiricBlade,   // 6) [흡혈 검]: 방향 특화(Positional) 공격 성공 시 내 체력 10 회복
        SwiftAwakening   // 7) [신속의 각성]: 내 캐릭터의 글로벌 쿨타임(GCD) 0.4초 감소 (2.0초 -> 1.6초 -> 1.2초)
    }

    [Serializable]
    public class RoguelikeCardData
    {
        public RoguelikeCardId id;
        public string title;
        public string categoryTag;
        public string description;
        public Color accentColor;
    }

    /// <summary>
    /// 보스를 완전히 처치(HP 0)했을 때만 클리어 보상으로 3장의 랜덤 로그라이크 카드 UI를 띄우고,
    /// 카드 선택 시 효과를 영구 중첩 적용한 뒤 다음 스테이지로 진행시키는 매니저 (Step 6).
    /// </summary>
    public class RoguelikeCardManager : MonoBehaviour
    {
        [Header("References")]
        public BossController boss;
        public PlayerController player;

        [Header("UI References (SceneAutoBuilder 자동 연결)")]
        public GameObject cardSelectionModalRoot;
        public Text modalHeaderText;
        public Button[] cardButtons = new Button[3];
        public Image[] cardAccentBanners = new Image[3];
        public Text[] cardTitleTexts = new Text[3];
        public Text[] cardDescTexts = new Text[3];

        private bool _isSelectionOpen;
        private Action _onCardSelectedCallback;

        private readonly List<RoguelikeCardData> _cardPool = new List<RoguelikeCardData>();
        private readonly List<RoguelikeCardData> _currentOfferedCards = new List<RoguelikeCardData>();
        private readonly Dictionary<RoguelikeCardId, int> _acquiredCardStacks = new Dictionary<RoguelikeCardId, int>();
        private readonly List<string> _acquiredCardHistory = new List<string>();

        // [5] 광전사의 함성 타이머 (10초마다 파티 전체 공속 증가 버프)
        private float _berserkerShoutTimer;
        private float _partyAttackSpeedBuffTimer;

        public bool IsSelectionOpen => _isSelectionOpen;
        public bool HasPartyAttackSpeedBuff => _partyAttackSpeedBuffTimer > 0f;

        private void Awake()
        {
            InitializeCardPool();
        }

        private void Start()
        {
            if (boss == null)
            {
                boss = FindFirstObjectByType<BossController>();
            }
            if (player == null)
            {
                player = FindFirstObjectByType<PlayerController>();
            }

            BindCardButtons();

            if (cardSelectionModalRoot != null)
            {
                cardSelectionModalRoot.SetActive(false);
            }
        }

        private void InitializeCardPool()
        {
            _cardPool.Clear();

            _cardPool.Add(new RoguelikeCardData
            {
                id = RoguelikeCardId.MasterOfRear,
                title = "[후면의 지배자]",
                categoryTag = "근딜 특화 / 극딜",
                description = "후면 기습([2]번) 방향 판정 성공 시\n<b>피해량 50% 추가 증가</b> 및\n<b>글로벌 쿨타임(GCD) 즉시 초기화</b>",
                accentColor = new Color(1.0f, 0.42f, 0.15f, 1f)
            });

            _cardPool.Add(new RoguelikeCardData
            {
                id = RoguelikeCardId.GhostStep,
                title = "[유령 걸음]",
                categoryTag = "기동성 / 판정 완화",
                description = "내 캐릭터 <b>이동속도 30% 증가</b> 및\n보스 <b>측·후면 판정 각도가 15도 넓어짐</b>\n(측면 30~150° / 후면 120~180°)",
                accentColor = new Color(0.25f, 0.88f, 0.95f, 1f)
            });

            _cardPool.Add(new RoguelikeCardData
            {
                id = RoguelikeCardId.ThornArmor,
                title = "[가시 갑옷]",
                categoryTag = "기믹 반격 / 탱크버스터",
                description = "2페이즈 <b>줄 가로채기 후 [3]번 생존기</b>로\n방어 성공 시 보스에게\n<b>강력한 반사 피해(600)</b>를 입힘",
                accentColor = new Color(0.35f, 0.95f, 0.45f, 1f)
            });

            _cardPool.Add(new RoguelikeCardData
            {
                id = RoguelikeCardId.TacticalRescue,
                title = "[전술적 구출]",
                categoryTag = "힐러 연계 / 버프",
                description = "위험 장판에서 <b>힐러에게 '구출'로 끌려갈 경우</b>\n<b>5초간 내 공격력 2배 증가</b> 버프 획득",
                accentColor = new Color(0.95f, 0.80f, 0.20f, 1f)
            });

            _cardPool.Add(new RoguelikeCardData
            {
                id = RoguelikeCardId.BerserkerShout,
                title = "[광전사의 함성]",
                categoryTag = "파티 시너지 / 공속",
                description = "<b>10초마다 파티 전체 공격속도 증가</b> 버프가\n자동으로 발동되어 봇과 내 공격 주기 단축",
                accentColor = new Color(0.95f, 0.25f, 0.38f, 1f)
            });

            _cardPool.Add(new RoguelikeCardData
            {
                id = RoguelikeCardId.VampiricBlade,
                title = "[흡혈 검]",
                categoryTag = "생존 / 지속력",
                description = "측면([1]) 또는 후면([2]) <b>방향 특화(Positional)\n공격 성공 시마다 내 체력 10 즉시 회복</b>",
                accentColor = new Color(0.75f, 0.28f, 0.95f, 1f)
            });

            _cardPool.Add(new RoguelikeCardData
            {
                id = RoguelikeCardId.SwiftAwakening,
                title = "[신속의 각성]",
                categoryTag = "GCD 가속 / 템포 강화",
                description = "내 캐릭터의 <b>글로벌 쿨타임(GCD)이 0.4초 영구 감소</b>\n(2.0초 → 1.6초 → 1.2초, 중첩 가능!)",
                accentColor = new Color(0.20f, 1.0f, 0.82f, 1f)
            });
        }

        private void BindCardButtons()
        {
            if (cardButtons == null)
            {
                return;
            }

            for (int i = 0; i < cardButtons.Length; i++)
            {
                int capturedIndex = i;
                if (cardButtons[i] != null)
                {
                    cardButtons[i].onClick.RemoveAllListeners();
                    cardButtons[i].onClick.AddListener(() => OnCardButtonClicked(capturedIndex));
                }
            }
        }

        private void Update()
        {
            if (_isSelectionOpen)
            {
                HandleUnscaledCardModalInput();
                return;
            }

            UpdateBerserkerShoutPassive();
        }

        /// <summary>
        /// StageManager에서 보스 처치(HP 0) 후 1.5초 뒤 호출되어 클리어 보상 카드 3택 UI를 띄웁니다.
        /// </summary>
        public void OpenStageClearCardReward(int clearedStageNumber, Action onCardChosenCallback)
        {
            _onCardSelectedCallback = onCardChosenCallback;
            OpenCardSelection(
                $"★ STAGE {clearedStageNumber} 보스 토벌 완료! ★\n다음 스테이지(STAGE {clearedStageNumber + 1}) 진입 전 보상 카드를 1장 선택하세요"
            );
        }

        public void OpenCardSelection(string headerReason)
        {
            if (_isSelectionOpen)
            {
                return;
            }

            _currentOfferedCards.Clear();

            List<RoguelikeCardData> candidatePool = new List<RoguelikeCardData>();
            foreach (RoguelikeCardData card in _cardPool)
            {
                if (!_acquiredCardStacks.ContainsKey(card.id) || card.id == RoguelikeCardId.SwiftAwakening)
                {
                    candidatePool.Add(card);
                }
            }

            if (candidatePool.Count < 3)
            {
                candidatePool = new List<RoguelikeCardData>(_cardPool);
            }

            for (int i = 0; i < 3 && candidatePool.Count > 0; i++)
            {
                int pickIdx = UnityEngine.Random.Range(0, candidatePool.Count);
                _currentOfferedCards.Add(candidatePool[pickIdx]);
                candidatePool.RemoveAt(pickIdx);
            }

            if (modalHeaderText != null)
            {
                modalHeaderText.text = headerReason;
            }

            for (int i = 0; i < 3; i++)
            {
                if (i < _currentOfferedCards.Count)
                {
                    RoguelikeCardData data = _currentOfferedCards[i];
                    bool alreadyOwned = _acquiredCardStacks.TryGetValue(data.id, out int stackCount) && stackCount > 0;

                    if (cardTitleTexts != null && i < cardTitleTexts.Length && cardTitleTexts[i] != null)
                    {
                        cardTitleTexts[i].text = alreadyOwned
                            ? $"{data.title} ({stackCount + 1}중첩)\n<size=15><color=#DDDDDD>{data.categoryTag}</color></size>"
                            : $"{data.title}\n<size=15><color=#DDDDDD>{data.categoryTag}</color></size>";
                        cardTitleTexts[i].color = data.accentColor;
                    }

                    if (cardDescTexts != null && i < cardDescTexts.Length && cardDescTexts[i] != null)
                    {
                        cardDescTexts[i].text = data.description + "\n\n<color=#FFE066>▶ 마우스 클릭하여 선택 & 다음 스테이지 진입</color>";
                    }

                    if (cardAccentBanners != null && i < cardAccentBanners.Length && cardAccentBanners[i] != null)
                    {
                        cardAccentBanners[i].color = data.accentColor;
                    }
                }
            }

            if (cardSelectionModalRoot != null)
            {
                cardSelectionModalRoot.SetActive(true);
            }

            _isSelectionOpen = true;
            Time.timeScale = 0f;
        }

        public void OnCardButtonClicked(int slotIndex)
        {
            if (!_isSelectionOpen || slotIndex < 0 || slotIndex >= _currentOfferedCards.Count)
            {
                return;
            }

            RoguelikeCardData chosen = _currentOfferedCards[slotIndex];
            ApplyCardEffect(chosen);

            _isSelectionOpen = false;
            if (cardSelectionModalRoot != null)
            {
                cardSelectionModalRoot.SetActive(false);
            }

            Time.timeScale = 1f;

            Action callback = _onCardSelectedCallback;
            _onCardSelectedCallback = null;
            callback?.Invoke();
        }

        private void ApplyCardEffect(RoguelikeCardData card)
        {
            if (_acquiredCardStacks.TryGetValue(card.id, out int count))
            {
                _acquiredCardStacks[card.id] = count + 1;
            }
            else
            {
                _acquiredCardStacks[card.id] = 1;
            }

            _acquiredCardHistory.Add($"{card.title} ({card.categoryTag})");

            if (player != null)
            {
                player.OnAcquireRoguelikeCard(card.id);
                FloatingTextPopup.Spawn(
                    player.transform.position + Vector3.up * 2.6f,
                    $"CARD GET! {card.title}",
                    card.accentColor,
                    true
                );
            }

            if (card.id == RoguelikeCardId.BerserkerShout)
            {
                TriggerBerserkerShoutPulse();
                _berserkerShoutTimer = 0f;
            }
        }

        private void UpdateBerserkerShoutPassive()
        {
            if (_partyAttackSpeedBuffTimer > 0f)
            {
                _partyAttackSpeedBuffTimer -= Time.deltaTime;
                if (_partyAttackSpeedBuffTimer < 0f)
                {
                    _partyAttackSpeedBuffTimer = 0f;
                }
            }

            if (!HasCard(RoguelikeCardId.BerserkerShout))
            {
                return;
            }

            _berserkerShoutTimer += Time.deltaTime;
            if (_berserkerShoutTimer >= 10f)
            {
                _berserkerShoutTimer = 0f;
                TriggerBerserkerShoutPulse();
            }
        }

        private void TriggerBerserkerShoutPulse()
        {
            _partyAttackSpeedBuffTimer = 5.0f;

            if (player != null)
            {
                FloatingTextPopup.Spawn(
                    player.transform.position + Vector3.up * 2.5f,
                    "[광전사의 함성] 파티 전체 공격속도 +50% 증가!",
                    new Color(1f, 0.35f, 0.45f, 1f),
                    true
                );
            }

            PartyBotAI[] bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            foreach (PartyBotAI bot in bots)
            {
                if (bot != null && !bot.isDead)
                {
                    bot.ApplyBerserkerAttackSpeedBoost(5.0f);
                }
            }
        }

        private void HandleUnscaledCardModalInput()
        {
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

            if (mouseClicked && cardButtons != null)
            {
                for (int i = 0; i < cardButtons.Length; i++)
                {
                    if (cardButtons[i] == null) continue;
                    RectTransform rt = cardButtons[i].GetComponent<RectTransform>();
                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
                    {
                        OnCardButtonClicked(i);
                        return;
                    }
                }
            }
        }

        public bool HasCard(RoguelikeCardId id)
        {
            return _acquiredCardStacks.TryGetValue(id, out int c) && c > 0;
        }

        public string GetAcquiredCardsSummary()
        {
            if (_acquiredCardHistory.Count == 0)
            {
                return "없음 (보스 처치 시 카드 3택 보상 지급!)";
            }

            List<string> titles = new List<string>();
            foreach (var kv in _acquiredCardStacks)
            {
                RoguelikeCardData d = _cardPool.Find(c => c.id == kv.Key);
                if (d != null)
                {
                    titles.Add(kv.Value > 1 ? $"{d.title}x{kv.Value}" : d.title);
                }
            }
            return string.Join(", ", titles);
        }

        public string GetAcquiredCardsDetailedList()
        {
            if (_acquiredCardHistory.Count == 0)
            {
                return "• 획득한 로그라이크 카드가 없습니다.";
            }

            List<string> lines = new List<string>();
            for (int i = 0; i < _acquiredCardHistory.Count; i++)
            {
                lines.Add($"• Stage {i + 1} 클리어 보상 : <b>{_acquiredCardHistory[i]}</b>");
            }
            return string.Join("\n", lines);
        }
    }
}
