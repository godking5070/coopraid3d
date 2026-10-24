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
    public partial class RoguelikeCardManager : MonoBehaviour
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
            _cardPool.AddRange(CharacterEvolutionCards.Build());

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

            if (!RaidCombatAuthority.Replica) UpdateBerserkerShoutPassive();
        }

        /// <summary>
        /// StageManager에서 보스 처치(HP 0) 후 1.5초 뒤 호출되어 클리어 보상 카드 3택 UI를 띄웁니다.
        /// </summary>
        public void OpenStageClearCardReward(int clearedStageNumber, Action onCardChosenCallback)
        {
            if (_isSelectionOpen || StageManager.Instance?.Progress.Phase != RaidBattlePhase.Reward) return;
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
                if (EligibleRosterCard(card)&&(!_acquiredCardStacks.ContainsKey(card.id) || card.id == RoguelikeCardId.SwiftAwakening))
                {
                    candidatePool.Add(card);
                }
            }

            if (candidatePool.Count < 3)
            {
                candidatePool = _cardPool.FindAll(EligibleRosterCard);
            }

            for (int i = 0; i < 3 && candidatePool.Count > 0; i++)
            {
                int pickIdx = CampaignRuntime.Instance?.RewardRandom(candidatePool.Count) ?? UnityEngine.Random.Range(0, candidatePool.Count);
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
                    GetRoleAdaptedCardDisplay(data, out string roleTitle, out string roleCategory, out string roleDesc);

                    if (cardTitleTexts != null && i < cardTitleTexts.Length && cardTitleTexts[i] != null)
                    {
                        cardTitleTexts[i].text = alreadyOwned
                            ? $"{roleTitle} ({stackCount + 1}중첩)\n<size=15><color=#DDDDDD>{roleCategory}</color></size>"
                            : $"{roleTitle}\n<size=15><color=#DDDDDD>{roleCategory}</color></size>";
                        cardTitleTexts[i].color = data.accentColor;
                    }

                    if (cardDescTexts != null && i < cardDescTexts.Length && cardDescTexts[i] != null)
                    {
                        LocalShortcutText.Set(cardDescTexts[i], roleDesc + "\n\n<color=#FFE066>▶ 클릭 또는 [{key:Choice" + (i + 1) + "}]키로 선택 & 다음 스테이지 진입</color>");
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
            if (LocalGameplayInput.ModalCovered) return;
            if (RaidCombatAuthority.Active)
            {
                if (_isSelectionOpen && slotIndex >= 0 && slotIndex < _currentOfferedCards.Count) RaidNetworkSession.Instance.RequestAction(8, (int)_currentOfferedCards[slotIndex].id);
                return;
            }
            CommitSingleCard(slotIndex);
        }
        internal void AutoChooseSingleCard()
        {
            if(!RaidCombatAuthority.Active)CommitSingleCard(0);
        }
        private void CommitSingleCard(int slotIndex)
        {
            if (!_isSelectionOpen || slotIndex < 0 || slotIndex >= _currentOfferedCards.Count)
            {
                return;
            }

            if (StageManager.Instance == null || StageManager.Instance.Progress.Phase != RaidBattlePhase.Reward) return;
            RoguelikeCardData chosen = _currentOfferedCards[slotIndex];
            _isSelectionOpen = false; // Consume before applying or invoking another state transition.
            CampaignRuntime.Instance?.CardChosen(player, (int)chosen.id, _currentOfferedCards.ConvertAll(c => (int)c.id));
            ApplyCardEffect(chosen);
            if (cardSelectionModalRoot != null)
            {
                cardSelectionModalRoot.SetActive(false);
            }

            Time.timeScale = 1f;

            Action callback = _onCardSelectedCallback;
            _onCardSelectedCallback = null;
            callback?.Invoke();
        }

        public void CancelPendingSelection()
        {
            _onCardSelectedCallback = null; _isSelectionOpen = false; _currentOfferedCards.Clear(); _networkOfferEpoch = -1;
            if (cardSelectionModalRoot != null) cardSelectionModalRoot.SetActive(false);
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

            if(player!=null&&(int)card.id>=CharacterEvolutionCards.First)
                CharacterActor.Ensure(player.gameObject).RecordBuildEffect(((int)card.id-CharacterEvolutionCards.First)%2==0?"evolution.overcome":"evolution.maximize",1);
            if (player != null) CharacterActor.Ensure(player.gameObject).RecordBuildEffect(CampaignCardIds.Id((int)card.id), _acquiredCardStacks[card.id]);
            GetRoleAdaptedCardDisplay(card, out string roleTitle, out string roleCat, out _);
            _acquiredCardHistory.Add($"{roleTitle} ({roleCat})");

            if (player != null)
            {
                player.OnAcquireRoguelikeCard(card.id);
                FloatingTextPopup.Spawn(
                    player.transform.position + Vector3.up * 2.6f,
                    $"CARD GET! {roleTitle}",
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

        /// <summary>
        /// Step 8 요구사항 4: 내 현재 직업(탱커/힐러/근딜/캐스터)에 맞춰 카드 명칭과 효과 설명을 맞춤형으로 제공합니다.
        /// </summary>
        private void GetRoleAdaptedCardDisplay(
            RoguelikeCardData baseCard,
            out string title,
            out string categoryTag,
            out string description)
        {
            title = baseCard.title;
            categoryTag = baseCard.categoryTag;
            description = baseCard.description;

            PlayerRole role = player != null ? player.currentRole : PlayerRole.Blade_MeleeDPS;

            switch (baseCard.id)
            {
                case RoguelikeCardId.MasterOfRear:
                    if (role == PlayerRole.Archmage_Caster)
                    {
                        title = "[대마도사의 화염 각성]";
                        categoryTag = "캐스터 특화 / 화염 증폭";
                        description = "[1]번 <b>화염 폭풍 3단 캐스팅 콤보</b>\n(파이어·파이가·플레어)의\n<b>최종 피해량 +35% 영구 증폭!</b>";
                    }
                    else if (role == PlayerRole.Oracle_Healer)
                    {
                        title = "[천벌의 대사제]";
                        categoryTag = "힐러 특화 / 신성력 강화";
                        description = "[1]번 <b>심판의 빛 3단 딜량</b> 및\n[2]·[3]번 <b>치유/보호막 효율 +25% 영구 증가!</b>";
                    }
                    else if (role == PlayerRole.Guardian_Tank)
                    {
                        title = "[수호자의 분노]";
                        categoryTag = "탱커 특화 / 강타 강화";
                        description = "[1]번 <b>방패 강타</b> 및 [2]번 <b>심판의 투창</b>\n3단 진화 콤보 <b>피해량 +25% 영구 증가!</b>";
                    }
                    break;

                case RoguelikeCardId.ThornArmor:
                    if (role == PlayerRole.Guardian_Tank)
                    {
                        title = "[철벽 가시 갑옷]";
                        categoryTag = "탱커 특화 / 뎀감 반사";
                        description = "탱커 기본 방어 및 <b>[3]번 '철벽 방어' 활성 중\n보스 평타·탱크버스터 방어 시 강력한 반사 피해(600)</b>를 입힘!";
                    }
                    break;

                case RoguelikeCardId.TacticalRescue:
                    if (role == PlayerRole.Oracle_Healer)
                    {
                        title = "[신속 구출 & 축복]";
                        categoryTag = "힐러 특화 / 구출 강화";
                        description = "[4]번 <b>'구출(Rescue)' 쿨타임 대폭 감소(20초→12초)</b> 및\n구출 발동 시 <b>5초간 내 모든 딜·힐량 2배 증가!</b>";
                    }
                    break;

                case RoguelikeCardId.VampiricBlade:
                    if (role == PlayerRole.Oracle_Healer)
                    {
                        title = "[성스러운 역류]";
                        categoryTag = "힐러 특화 / 오버힐 딜 전환";
                        description = "[2]번 <b>'구원의 치유 & 보호막' 시전 시마다</b>\n넘치는 치유 에너지가 보스에게 <b>성스러운 폭발 피해(220)로 전환!</b>";
                    }
                    else if (role == PlayerRole.Guardian_Tank)
                    {
                        title = "[수호의 피]";
                        categoryTag = "탱커 특화 / 타격 자힐";
                        description = "[1]번 <b>방패 강타 콤보 적중 시마다\n내 체력 30 즉시 추가 회복!</b>";
                    }
                    else if (role == PlayerRole.Archmage_Caster)
                    {
                        title = "[마력 흡수]";
                        categoryTag = "캐스터 특화 / 주문 흡혈";
                        description = "[1]번 <b>화염 캐스팅 마법 적중 시마다\n내 체력 15 즉시 회복!</b>";
                    }
                    break;

                case RoguelikeCardId.SwiftAwakening:
                    if (role == PlayerRole.Archmage_Caster)
                    {
                        title = "[신속의 마도서]";
                        categoryTag = "캐스터 특화 / 캐스팅 속도 증가";
                        description = "내 캐릭터의 <b>[1]번 화염 캐스팅 시전 시간 단축</b> 및\n<b>글로벌 쿨타임(GCD) 0.4초 영구 감소!</b>";
                    }
                    break;
            }
        }

        private void UpdateBerserkerShoutPassive()
        {
            if (StageManager.CombatSuspended) return;
            if (_partyAttackSpeedBuffTimer > 0f)
            {
                _partyAttackSpeedBuffTimer -= Time.deltaTime;
                if (_partyAttackSpeedBuffTimer < 0f)
                {
                    _partyAttackSpeedBuffTimer = 0f;
                }
            }

            if (player == null || player.IsDead || !HasCard(RoguelikeCardId.BerserkerShout))
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

        public void ResetTemporaryCombatState()
        {
            _partyAttackSpeedBuffTimer = _berserkerShoutTimer = 0;
        }

        private void TriggerBerserkerShoutPulse()
        {
            _partyAttackSpeedBuffTimer = 5.0f;
            if (RaidCombatAuthority.Host) foreach (var member in PartyLife.OrderedMembers())
            { var manager = member.GetComponent<PlayerController>()?.cardManager; if (manager != null) manager._partyAttackSpeedBuffTimer = 5; }

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
            if (LocalGameplayInput.ModalCovered) return;
            for (int i = 0; i < 3; i++) if (LocalInputBindings.Pressed("Choice" + (i + 1))) { OnCardButtonClicked(i); break; }
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
