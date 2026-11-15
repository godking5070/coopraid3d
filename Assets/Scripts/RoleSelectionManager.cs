using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    /// <summary>
    /// Step 8 핵심: 4대 역할군(1.가디언 탱커 / 2.오라클 힐러 / 3.블레이드 근딜 / 4.아크메이지 캐스터) 선택 시스템 및 동적 파티 편성 매니저.
    /// - 게임 시작 시 화면 중앙에 [직업 선택 창] 팝업을 자동으로 띄우며, 전투 중에도 상단 [직업 변경 (P)] 버튼 또는 [P] 단축키로 언제든 직업을 즉시 변경할 수 있습니다.
    /// - 선택한 직업에 맞춰 내 캐릭터의 3D/2D 외형 색상, 스탯, 전용 스킬 핫바(1~4번)를 즉시 세팅하고,
    ///   나머지 3개의 빈 역할군을 AI 파티원 봇(PartyBotAI) 3명으로 자동 편성합니다.
    /// </summary>
    public class RoleSelectionManager : MonoBehaviour
    {
        public static RoleSelectionManager Instance { get; private set; }

        [Header("Core References")]
        public PlayerController player;
        public PlayerCombatController combatController;
        public BossController boss;
        public StageManager stageManager;
        public PartyListUI partyListUI;
        public SkillHotbarUI skillHotbarUI;
        public List<PartyBotAI> partyBots = new List<PartyBotAI>();

        [Header("Role Selection Modal & Top Button UI")]
        public Button topOpenRoleModalButton;
        public Text topOpenRoleModalButtonText;
        public GameObject roleModalRoot;
        public Text roleModalSubtitleText;
        public Button[] roleCardButtons = new Button[4];
        public Image[] roleCardBanners = new Image[4];
        public Text[] roleCardTitleTexts = new Text[4];
        public Text[] roleCardDescTexts = new Text[4];

        [Header("Default Spawn Coordinates (XZ Plane)")]
        public Vector3 tankSpawnPos = new Vector3(0f, 1.0f, 4.8f);
        public Vector3 meleeSpawnPos = new Vector3(0f, 1.0f, -4.0f);
        public Vector3 healerSpawnPos = new Vector3(-6.8f, 1.0f, -6.5f);
        public Vector3 casterSpawnPos = new Vector3(6.6f, 1.0f, -6.0f);

        private bool _isModalOpen;
        private bool _hasSelectedInitialRole;
        private RectTransform _selectionPanel;
        private Button _aegisSelect;
        private Text _aegisSelectText;

        public bool IsRoleModalOpen => _isModalOpen;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            RefreshSceneReferences();
            EnsureUI();
            BindButtons();
            // Also bind UI serialized by earlier SceneAutoBuilder versions, without rebuilding scenes.
            for (int i = 0; i < 4; i++)
            {
                if (roleCardDescTexts != null && i < roleCardDescTexts.Length && roleCardDescTexts[i] != null && roleCardDescTexts[i].GetComponent<LocalShortcutText>() == null)
                    LocalShortcutText.Set(roleCardDescTexts[i], roleCardDescTexts[i].text);
                if (roleCardButtons != null && i < roleCardButtons.Length && roleCardButtons[i] != null)
                    LocalShortcutText.Set(roleCardButtons[i].transform.Find("SelectActionBadge/BadgeText")?.GetComponent<Text>(), "▶ 클릭 또는 [{key:Choice" + (i + 1) + "}]키로 선택");
            }

            // SinglePlay explicitly opens selection; the menu preview must not start/reset a run.
            CloseRoleSelectionModal();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RefreshSceneReferences()
        {
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (combatController == null && player != null) combatController = player.GetComponent<PlayerCombatController>();
            if (combatController == null) combatController = FindFirstObjectByType<PlayerCombatController>();
            if (boss == null) boss = FindFirstObjectByType<BossController>();
            if (stageManager == null) stageManager = FindFirstObjectByType<StageManager>();
            if (partyListUI == null) partyListUI = FindFirstObjectByType<PartyListUI>();
            if (skillHotbarUI == null) skillHotbarUI = FindFirstObjectByType<SkillHotbarUI>();

            EnsureThreePartyBotsExist();
        }

        private void EnsureThreePartyBotsExist()
        {
            PartyBotAI[] existingBots = FindObjectsByType<PartyBotAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            partyBots.Clear();
            foreach (PartyBotAI b in existingBots)
            {
                if (b != null)
                {
                    partyBots.Add(b);
                }
            }

            partyBots.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.Ordinal));

            while (!RaidNetworkSession.Multiplayer && partyBots.Count < 3)
            {
                int idx = partyBots.Count + 1;
                GameObject botObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                botObj.name = $"PartyBot_Auto_{idx}";
                Collider col = botObj.GetComponent<Collider>();
                if (col != null) Destroy(col);

                DualVisualController dvc = botObj.AddComponent<DualVisualController>();
                dvc.unitKind = VisualUnitKind.BotRangedDPS;
                dvc.EnsureDualVisualHierarchy();
                dvc.Refresh2DSpritesAndColors();

                PartyBotAI newBot = botObj.AddComponent<PartyBotAI>();
                newBot.targetBoss = boss;
                newBot.humanPlayer = player;
                newBot.dualVisual = dvc;
                newBot.bodyRenderer = dvc.primary3DRenderer;
                partyBots.Add(newBot);
            }
        }

        private void Update()
        {
            UpdateTopButtonLabel();
            if (LocalGameplayInput.ModalCovered) return;
            if (CombatMetricsUI.BlocksUnderlyingInput) return;
            // 단축키 [P]: 언제든 직업 선택 창 열기/닫기
            if (WasShortcutPPressed())
            {
                if (_isModalOpen && _hasSelectedInitialRole)
                {
                    CloseRoleSelectionModal();
                }
                else if (!_isModalOpen)
                {
                    OpenRoleSelectionModal();
                }
                return;
            }

            if (_isModalOpen)
            {
                HandleModalUnscaledInputs();
            }
        }

        private bool CanChangePartyRoles()
        {
            if (RaidNetworkSession.Instance?.Ending == true || RaidNetworkSession.Multiplayer) return false;
            if (stageManager != null && stageManager.CanSelectInitialRole) return true;
            if (StageManager.CombatSuspended) return false;
            foreach (var member in PartyLife.OrderedMembers())
                if (!CombatRange.LivingPartyMember(member)) return false;
            return true;
        }

        public void OpenRoleSelectionModal()
        {
            if (RaidNetworkSession.Multiplayer || RaidNetworkSession.CombatPreview || RaidNetworkSession.Instance?.MenuOpen == true)
            { RaidNetworkSession.Instance.MenuOpen = true; return; }
            if (CombatMetricsUI.BlocksUnderlyingInput || !CanChangePartyRoles()) return;
            EnsureUI();

            if (roleModalRoot != null)
            {
                roleModalRoot.SetActive(true);
                roleModalRoot.transform.SetAsLastSibling();
            }

            PlayerRole curRole = player != null ? player.currentRole : PlayerRole.Blade_MeleeDPS;
            var character = player != null ? player.GetComponent<CharacterActor>() : null;
            string currentName = character?.Definition?.legacyRole == 0 ? character.Definition.name : GetRoleTitleKor(curRole);
            if (roleModalSubtitleText != null)
            {
                LocalShortcutText.Set(roleModalSubtitleText,
                    $"현재 직업: <color=#FFE055>{currentName}</color> · 아래 금색 버튼 → 12직업 → 기원 선택 → 출발\n" +
                    "기존 테스트 직업은 아래 카드 또는 <b>[{key:Choice1}] · [{key:Choice2}] · [{key:Choice3}] · [{key:Choice4}]</b>로 선택 · 남은 역할은 AI 봇이 담당합니다.");
            }

            _isModalOpen = true;
            Time.timeScale = 0f;
            UpdateTopButtonLabel();
        }

        public void CloseRoleSelectionModal()
        {
            if (_isModalOpen) LocalInputBindings.QuarantineHeld();
            _isModalOpen = false;
            if (roleModalRoot != null)
            {
                roleModalRoot.SetActive(false);
            }

            // 카드 선택창이나 승리 모달이 열려 있지 않다면 시간 재개
            RoguelikeCardManager cardMgr = FindFirstObjectByType<RoguelikeCardManager>();
            bool cardOpen = cardMgr != null && cardMgr.IsSelectionOpen;
            if (!cardOpen)
            {
                Time.timeScale = 1f;
            }
            UpdateTopButtonLabel();
        }

        public void SelectAegisPaladin()
        {
            if(!CanChangePartyRoles()||stageManager==null||!stageManager.CanSelectInitialRole)return;
            if(player==null)return;
            AegisRouteSelectionUI.Open(CharacterActor.Ensure(player.gameObject),false);
        }
        public bool SelectRosterJob(string id)
        {
            if(!CanChangePartyRoles()||!stageManager.CanSelectInitialRole||player==null)return false;
            var definition=CharacterCatalog.Find(id);if(definition==null||!definition.playable||definition.legacyRole!=0)return false;
            ApplySelectedRole((PlayerRole)CharacterRosterData.Role(definition),true,false);
            return CharacterActor.Ensure(player.gameObject).AssignDefinition(definition);
        }
        public void ConfirmAegisDeparture()
        {
            if(player==null||stageManager==null||!stageManager.CanSelectInitialRole||!CharacterActor.Ensure(player.gameObject).DepartureReady)return;
            _hasSelectedInitialRole=true;CloseRoleSelectionModal();
        }
        public void SelectRoleByIndex(int roleNumber1To4)
        {
            if (!CanChangePartyRoles()) return;
            PlayerRole chosen = roleNumber1To4 switch
            {
                1 => PlayerRole.Guardian_Tank,
                2 => PlayerRole.Oracle_Healer,
                3 => PlayerRole.Blade_MeleeDPS,
                4 => PlayerRole.Archmage_Caster,
                _ => PlayerRole.Blade_MeleeDPS
            };

            _hasSelectedInitialRole = true;
            ApplySelectedRole(chosen, repositionParty: true, showAlertBanner: true);
            CloseRoleSelectionModal();
        }

        /// <summary>
        /// Step 8 핵심:
        /// 1) 플레이어의 외형 색상(탱=파랑, 힐=초록, 근딜=노랑, 캐스터=주황), 체력, 방어율, 스킬 핫바(1~4번)를 즉시 세팅
        /// 2) 나머지 3개의 빈 역할군을 3명의 AI 파티원 봇(PartyBotAI)에 자동 배정
        /// 3) 내가 '탱커'일 때는 나에게 보스 어그로 1순위 고정, 아닐 때는 '탱커 봇'에게 어그로 1순위 고정!
        /// </summary>
        public void ApplySelectedRole(PlayerRole selectedRole, bool repositionParty = true, bool showAlertBanner = true)
        {
            RefreshSceneReferences();
            if (!CanChangePartyRoles()) return;

            // 1. 내 캐릭터 직업 스탯/외형/스킬셋 적용
            if (player != null)
            {
                player.ApplyRoleStatsAndVisuals(selectedRole, restoreFullHp: true);
                CharacterActor.Ensure(player.gameObject).UseLegacyRole();
                if (repositionParty)
                {
                    Vector3 mySpawn = selectedRole switch
                    {
                        PlayerRole.Guardian_Tank => tankSpawnPos,
                        PlayerRole.Oracle_Healer => healerSpawnPos,
                        PlayerRole.Archmage_Caster => casterSpawnPos,
                        _ => meleeSpawnPos
                    };
                    player.transform.position = new Vector3(mySpawn.x, 1.0f, mySpawn.z);
                    player.FaceBossImmediateXZ();
                }
            }

            // 2. 나머지 3개의 빈 역할군을 AI 봇 3명에게 동적 배정
            ConfigureRemainingThreeBots(selectedRole, repositionParty);

            // 3. 역할 교대 어그로 1순위 설정
            if (boss != null)
            {
                if (selectedRole == PlayerRole.Guardian_Tank && player != null)
                {
                    // 내가 '탱커'를 선택했을 때는 나에게 어그로 1순위 고정!
                    boss.SetAggroTarget(player.transform);
                }
                else
                {
                    // 내가 탱커가 아닐 때는 '탱커 AI 봇'이 어그로 1순위 및 북쪽 주차 담당!
                    PartyBotAI tankBot = FindBotWithRole(PartyBotRole.Tank);
                    if (tankBot != null)
                    {
                        boss.SetAggroTarget(tankBot.transform);
                    }
                }
            }

            // 4. 파티 리스트 UI 및 상단 버튼 갱신
            if (partyListUI != null)
            {
                partyListUI.RefreshPartyReferences();
            }

            UpdateTopButtonLabel();

            if (showAlertBanner && boss != null && player != null)
            {
                string roleAlert = selectedRole switch
                {
                    PlayerRole.Guardian_Tank =>
                        "★ [1. 가디언(탱커)] 선택 완료! 보스 어그로 1순위가 나에게 고정됩니다! (2페이즈 탱크버스터 줄은 '근딜 AI 봇'이 대신 가로채고 생존기로 나를 지켜줍니다!)",
                    PlayerRole.Oracle_Healer =>
                        "★ [2. 오라클(힐러)] 선택 완료! [2]구원의 치유&보호막(스마트 타겟) | [3]치유의 성역(초록 장판) | [4]구출(위험 아군 견인+3초 무적)로 파티를 구원하세요!",
                    PlayerRole.Blade_MeleeDPS =>
                        "★ [3. 블레이드(근딜)] 선택 완료! 보스 측면([1])·후면([2]) 3단 진화 웨폰스킬과 [4]질풍 찌르기(oGCD)로 극딜을 넣으세요!",
                    PlayerRole.Archmage_Caster =>
                        "★ [4. 아크메이지(캐스터)] 선택 완료! 제자리 [1]화염 3단 캐스팅(이동 시 취소!) & 이동 중 [2]냉기 무빙샷 & [4]삼연속 마법(6초 화염 즉발!)을 활용하세요!",
                    _ => ""
                };

                boss.ShowExternalAlert(roleAlert, new Color(0.4f, 0.95f, 1f, 1f), 5.0f);
                FloatingTextPopup.Spawn(
                    player.transform.position + Vector3.up * 2.7f,
                    $"직업 변경: {GetRoleTitleKor(selectedRole)}!",
                    player.normalBodyColor,
                    true
                );
            }
        }

        private void ConfigureRemainingThreeBots(PlayerRole playerRole, bool reposition)
        {
            EnsureThreePartyBotsExist();
            if (partyBots.Count < 3)
            {
                return;
            }

            // 플레이어가 선택한 직업을 제외한 나머지 3개 역할군 결정
            switch (playerRole)
            {
                case PlayerRole.Guardian_Tank:
                    // 플레이어=탱커 → 봇 3명: 근딜(블레이드) + 힐러(오라클) + 캐스터(아크메이지)
                    partyBots[0].ConfigureBotRoleDynamic(PartyBotRole.MeleeDPS, "BOT 1: 블레이드 (근딜·줄수호)", meleeSpawnPos, reposition);
                    partyBots[1].ConfigureBotRoleDynamic(PartyBotRole.Healer, "BOT 2: 오라클 (힐러)", healerSpawnPos, reposition);
                    partyBots[2].ConfigureBotRoleDynamic(PartyBotRole.RangedDPS, "BOT 3: 아크메이지 (캐스터)", casterSpawnPos, reposition);
                    break;

                case PlayerRole.Oracle_Healer:
                    // 플레이어=힐러 → 봇 3명: 탱커(가디언) + 근딜(블레이드) + 캐스터(아크메이지)
                    partyBots[0].ConfigureBotRoleDynamic(PartyBotRole.Tank, "BOT 1: 가디언 (탱커)", tankSpawnPos, reposition);
                    partyBots[1].ConfigureBotRoleDynamic(PartyBotRole.MeleeDPS, "BOT 2: 블레이드 (근딜)", meleeSpawnPos, reposition);
                    partyBots[2].ConfigureBotRoleDynamic(PartyBotRole.RangedDPS, "BOT 3: 아크메이지 (캐스터)", casterSpawnPos, reposition);
                    break;

                case PlayerRole.Archmage_Caster:
                    // 플레이어=캐스터 → 봇 3명: 탱커(가디언) + 힐러(오라클) + 근딜(블레이드)
                    partyBots[0].ConfigureBotRoleDynamic(PartyBotRole.Tank, "BOT 1: 가디언 (탱커)", tankSpawnPos, reposition);
                    partyBots[1].ConfigureBotRoleDynamic(PartyBotRole.Healer, "BOT 2: 오라클 (힐러)", healerSpawnPos, reposition);
                    partyBots[2].ConfigureBotRoleDynamic(PartyBotRole.MeleeDPS, "BOT 3: 블레이드 (근딜)", meleeSpawnPos, reposition);
                    break;

                case PlayerRole.Blade_MeleeDPS:
                default:
                    // 플레이어=근딜 → 봇 3명: 탱커(가디언) + 힐러(오라클) + 캐스터(아크메이지)
                    partyBots[0].ConfigureBotRoleDynamic(PartyBotRole.Tank, "BOT 1: 가디언 (탱커)", tankSpawnPos, reposition);
                    partyBots[1].ConfigureBotRoleDynamic(PartyBotRole.Healer, "BOT 2: 오라클 (힐러)", healerSpawnPos, reposition);
                    partyBots[2].ConfigureBotRoleDynamic(PartyBotRole.RangedDPS, "BOT 3: 아크메이지 (캐스터)", casterSpawnPos, reposition);
                    break;
            }
        }

        private PartyBotAI FindBotWithRole(PartyBotRole role)
        {
            foreach (PartyBotAI bot in partyBots)
            {
                if (bot != null && bot.role == role)
                {
                    return bot;
                }
            }
            return null;
        }

        private void BindButtons()
        {
            if (topOpenRoleModalButton != null)
            {
                topOpenRoleModalButton.onClick.RemoveAllListeners();
                topOpenRoleModalButton.onClick.AddListener(() =>
                {
                    if (_isModalOpen) CloseRoleSelectionModal();
                    else OpenRoleSelectionModal();
                });
            }

            if (roleCardButtons != null)
            {
                for (int i = 0; i < roleCardButtons.Length; i++)
                {
                    int roleNum = i + 1;
                    if (roleCardButtons[i] != null)
                    {
                        roleCardButtons[i].onClick.RemoveAllListeners();
                        roleCardButtons[i].onClick.AddListener(() => SelectRoleByIndex(roleNum));
                    }
                }
            }
        }

        private void HandleModalUnscaledInputs()
        {
            // 키보드 숫자 1, 2, 3, 4로 즉시 직업 선택 가능
            if (WasDigitPressed(1)) { SelectRoleByIndex(1); return; }
            if (WasDigitPressed(2)) { SelectRoleByIndex(2); return; }
            if (WasDigitPressed(3)) { SelectRoleByIndex(3); return; }
            if (WasDigitPressed(4)) { SelectRoleByIndex(4); return; }

            // Time.timeScale = 0 상태에서도 마우스 클릭이 100% 동작하도록 보장
            bool clicked = false;
            Vector2 mousePos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                clicked = true;
                mousePos = Mouse.current.position.ReadValue();
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!clicked && Input.GetMouseButtonDown(0))
            {
                clicked = true;
                mousePos = Input.mousePosition;
            }
#endif
            if (clicked && roleCardButtons != null)
            {
                for (int i = 0; i < roleCardButtons.Length; i++)
                {
                    if (roleCardButtons[i] == null) continue;
                    RectTransform rt = roleCardButtons[i].GetComponent<RectTransform>();
                    if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
                    {
                        SelectRoleByIndex(i + 1);
                        return;
                    }
                }
            }
        }

        private void UpdateTopButtonLabel()
        {
            if (topOpenRoleModalButtonText == null)
            {
                return;
            }

            PlayerRole cur = player != null ? player.currentRole : PlayerRole.Blade_MeleeDPS;
            var character = player != null ? player.GetComponent<CharacterActor>() : null;
            string currentName = character?.Definition?.legacyRole == 0 ? character.Definition.name : GetRoleShortKor(cur);
            topOpenRoleModalButtonText.text = $"[직업 변경: {currentName} ({LocalInputBindings.Label("Roles")})]";
        }

        public static string GetRoleTitleKor(PlayerRole role)
        {
            return role switch
            {
                PlayerRole.Guardian_Tank => "1. 가디언 (탱커 - Tank)",
                PlayerRole.Oracle_Healer => "2. 오라클 (힐러 - Healer)",
                PlayerRole.Blade_MeleeDPS => "3. 블레이드 (근딜 - Melee DPS)",
                PlayerRole.Archmage_Caster => "4. 아크메이지 (캐스터 - Caster DPS)",
                _ => "3. 블레이드 (근딜)"
            };
        }

        public static string GetRoleShortKor(PlayerRole role)
        {
            return role switch
            {
                PlayerRole.Guardian_Tank => "1.가디언(탱커)",
                PlayerRole.Oracle_Healer => "2.오라클(힐러)",
                PlayerRole.Blade_MeleeDPS => "3.블레이드(근딜)",
                PlayerRole.Archmage_Caster => "4.아크메이지(캐스터)",
                _ => "3.블레이드(근딜)"
            };
        }

        public void EnsureUI()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // 1. 상단 우측 [직업 변경 (P)] 버튼 생성 (뷰 전환 버튼 바로 아래 또는 옆)
            if (topOpenRoleModalButton == null)
            {
                Transform existingBtn = canvas.transform.Find("RoleSwitchButton_TopRight");
                if (existingBtn != null)
                {
                    topOpenRoleModalButton = existingBtn.GetComponent<Button>();
                    topOpenRoleModalButtonText = existingBtn.GetComponentInChildren<Text>();
                }
                else
                {
                    GameObject btnObj = new GameObject("RoleSwitchButton_TopRight");
                    btnObj.transform.SetParent(canvas.transform, false);
                    RectTransform rt = btnObj.AddComponent<RectTransform>();
                    rt.anchorMin = new Vector2(1f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-24f, -80f);
                    rt.sizeDelta = new Vector2(310f, 48f);

                    Image bg = btnObj.AddComponent<Image>();
                    bg.color = new Color(0.16f, 0.24f, 0.44f, 0.96f);

                    Outline outline = btnObj.AddComponent<Outline>();
                    outline.effectColor = new Color(1.0f, 0.82f, 0.28f, 0.95f);
                    outline.effectDistance = new Vector2(2f, -2f);

                    topOpenRoleModalButton = btnObj.AddComponent<Button>();
                    topOpenRoleModalButton.targetGraphic = bg;

                    GameObject txtObj = new GameObject("ButtonLabel");
                    txtObj.transform.SetParent(btnObj.transform, false);
                    RectTransform txtRect = txtObj.AddComponent<RectTransform>();
                    txtRect.anchorMin = Vector2.zero;
                    txtRect.anchorMax = Vector2.one;
                    txtRect.offsetMin = new Vector2(8f, 4f);
                    txtRect.offsetMax = new Vector2(-8f, -4f);

                    topOpenRoleModalButtonText = txtObj.AddComponent<Text>();
                    topOpenRoleModalButtonText.font = font;
                    topOpenRoleModalButtonText.fontSize = 16;
                    topOpenRoleModalButtonText.fontStyle = FontStyle.Bold;
                    topOpenRoleModalButtonText.alignment = TextAnchor.MiddleCenter;
                    topOpenRoleModalButtonText.color = new Color(1f, 0.95f, 0.7f, 1f);
                    topOpenRoleModalButtonText.text = $"[직업 변경: 3.블레이드(근딜) ({LocalInputBindings.Label("Roles")})]";
                }
            }

            // 2. 화면 중앙 [직업 선택 창: 1.가디언 / 2.오라클 / 3.블레이드 / 4.아크메이지] 모달 생성
            if (roleModalRoot == null)
            {
                Transform existingModal = canvas.transform.Find("RoleSelection_ModalOverlay");
                if (existingModal != null)
                {
                    roleModalRoot = existingModal.gameObject;
                }
                else
                {
                    BuildRoleSelectionModalUI(canvas.transform, font);
                }
            }
            // Runtime upgrade also covers previously generated scenes and does not depend on the hotbar being visible.
            if (Application.isPlaying) EnsureCharacterChoices(font);
        }

        private void EnsureCharacterChoices(Font font)
        {
            if (RaidUIStyle.Authored(roleModalRoot != null ? roleModalRoot.transform : null) && _aegisSelect != null)
            {
                _aegisSelect.onClick.RemoveAllListeners(); _aegisSelect.onClick.AddListener(SelectAegisPaladin); return;
            }
            if (roleModalRoot == null) return;
            _selectionPanel = roleModalRoot.transform.Find("RoleSelection_CenterWindow") as RectTransform;
            if (_selectionPanel == null) return;
            _selectionPanel.sizeDelta = new Vector2(1480, 900);
            var title = _selectionPanel.Find("ModalTitle")?.GetComponent<Text>();
            if (title != null) title.text = "직업 선택 · 이지스 팔라딘 / 기존 테스트 직업";
            if (roleModalSubtitleText != null) roleModalSubtitleText.rectTransform.sizeDelta = new Vector2(1400, 52);
            for (int i = 0; i < roleCardButtons.Length; i++)
                if (roleCardButtons[i] != null)
                {
                    var card = roleCardButtons[i].GetComponent<RectTransform>();
                    card.anchoredPosition = new Vector2(card.anchoredPosition.x, -62);
                }

            var choice = _selectionPanel.Find("AegisBasicSelect") as RectTransform;
            if (choice == null)
            {
                // Reuse an existing runtime choice when scripts reload during Play.
                choice = roleModalRoot.transform.Find("AegisBasicSelect") as RectTransform;
                if (choice == null) choice = new GameObject("AegisBasicSelect", typeof(RectTransform)).GetComponent<RectTransform>();
                choice.SetParent(_selectionPanel, false);
            }
            choice.anchorMin = choice.anchorMax = choice.pivot = new Vector2(.5f, .5f);
            choice.anchoredPosition = new Vector2(0, 254); choice.sizeDelta = new Vector2(1400, 76);
            var image = choice.GetComponent<Image>() ?? choice.gameObject.AddComponent<Image>();
            image.color = new Color(.32f, .24f, .09f, 1);
            _aegisSelect = choice.GetComponent<Button>() ?? choice.gameObject.AddComponent<Button>();
            _aegisSelect.targetGraphic = image;
            _aegisSelect.onClick.RemoveAllListeners();
            _aegisSelect.onClick.AddListener(SelectAegisPaladin);
            _aegisSelectText = ChoiceLabel(choice, font, 24);
            _aegisSelectText.color = new Color(1, .9f, .5f);

            var catalog = _selectionPanel.Find("RegisteredClasses") as RectTransform;
            if (catalog == null)
            {
                catalog = roleModalRoot.transform.Find("RegisteredClasses") as RectTransform;
                if (catalog == null) catalog = new GameObject("RegisteredClasses", typeof(RectTransform)).GetComponent<RectTransform>();
                catalog.SetParent(_selectionPanel, false);
            }
            catalog.anchorMin = catalog.anchorMax = catalog.pivot = new Vector2(.5f, .5f);
            catalog.anchoredPosition = new Vector2(0, -385); catalog.sizeDelta = new Vector2(1400, 80);
            var names = new List<string>();
            foreach (var job in CharacterCatalog.All) if (!job.playable) names.Add(job.name);
            ChoiceLabel(catalog, font, 16).text = "미구현 직업 목록\n" + string.Join(" · ", names);
            RefreshCharacterChoiceLayout();
        }

        private static Text ChoiceLabel(RectTransform parent, Font font, int size)
        {
            var text = parent.GetComponentInChildren<Text>(true);
            if (text == null)
            {
                var label = new GameObject("Label", typeof(RectTransform));
                label.transform.SetParent(parent, false);
                text = label.AddComponent<Text>();
            }
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(14, 4); rect.offsetMax = new Vector2(-14, -4);
            text.font = font; text.fontSize = size; text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white; text.raycastTarget = false;
            return text;
        }

        private void LateUpdate()
        {
            if (_isModalOpen) RefreshCharacterChoiceLayout();
        }

        private void RefreshCharacterChoiceLayout()
        {
            if (_selectionPanel == null || roleModalRoot == null) return;
            var available = ((RectTransform)roleModalRoot.transform).rect.size;
            if (!RaidUIStyle.Authored(_selectionPanel) && available.x > 0 && available.y > 0)
                _selectionPanel.localScale = Vector3.one * Mathf.Clamp(Mathf.Min((available.x - 24) / 1480, (available.y - 24) / 900), .01f, 1);
            if (_aegisSelect == null) return;
            bool canSelect = stageManager != null && stageManager.CanSelectInitialRole && CharacterCatalog.Find(AegisPaladinData.JobId)?.playable == true;
            _aegisSelect.interactable = canSelect && !LocalGameplayInput.ModalCovered;
            _aegisSelectText.text = canSelect
                ? "▶ 12직업 선택 · 탱커 / 힐러 / 근딜 / 원딜 / 캐스터\n기본기 · 기원 3루트 / Z · 스테이지별 R/F/V 선택"
                : "12직업 · 새 런 출발 전에 선택할 수 있습니다";
        }

        private void BuildRoleSelectionModalUI(Transform canvasTransform, Font font)
        {
            GameObject modalObj = new GameObject("RoleSelection_ModalOverlay");
            modalObj.transform.SetParent(canvasTransform, false);
            RectTransform modalRect = modalObj.AddComponent<RectTransform>();
            modalRect.anchorMin = Vector2.zero;
            modalRect.anchorMax = Vector2.one;
            modalRect.offsetMin = Vector2.zero;
            modalRect.offsetMax = Vector2.zero;

            Image dimImg = modalObj.AddComponent<Image>();
            dimImg.color = new Color(0.02f, 0.03f, 0.07f, 0.88f);

            // 중앙 컨테이너 패널
            GameObject panelObj = new GameObject("RoleSelection_CenterWindow");
            panelObj.transform.SetParent(modalObj.transform, false);
            RectTransform panelRect = panelObj.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(1480f, 680f);

            Image panelBg = panelObj.AddComponent<Image>();
            panelBg.color = new Color(0.07f, 0.09f, 0.15f, 0.98f);
            Outline panelOutline = panelObj.AddComponent<Outline>();
            panelOutline.effectColor = new Color(0.35f, 0.72f, 1.0f, 0.95f);
            panelOutline.effectDistance = new Vector2(3f, -3f);

            // 상단 메인 타이틀
            GameObject titleObj = new GameObject("ModalTitle");
            titleObj.transform.SetParent(panelObj.transform, false);
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -22f);
            titleRect.sizeDelta = new Vector2(1400f, 52f);

            Text titleTxt = titleObj.AddComponent<Text>();
            titleTxt.font = font;
            titleTxt.fontSize = 30;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color = new Color(1.0f, 0.90f, 0.30f, 1f);
            titleTxt.text = "[직업 선택 창 : 1.가디언(탱커) / 2.오라클(힐러) / 3.블레이드(근딜) / 4.아크메이지(캐스터)]";
            titleTxt.raycastTarget = false;

            // 서브타이틀 안내
            GameObject subObj = new GameObject("ModalSubtitle");
            subObj.transform.SetParent(panelObj.transform, false);
            RectTransform subRect = subObj.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.5f, 1f);
            subRect.anchorMax = new Vector2(0.5f, 1f);
            subRect.pivot = new Vector2(0.5f, 1f);
            subRect.anchoredPosition = new Vector2(0f, -76f);
            subRect.sizeDelta = new Vector2(1400f, 36f);

            roleModalSubtitleText = subObj.AddComponent<Text>();
            roleModalSubtitleText.font = font;
            roleModalSubtitleText.fontSize = 18;
            roleModalSubtitleText.fontStyle = FontStyle.Bold;
            roleModalSubtitleText.alignment = TextAnchor.MiddleCenter;
            roleModalSubtitleText.color = new Color(0.82f, 0.92f, 1.0f, 1f);
            roleModalSubtitleText.raycastTarget = false;

            // 4개 직업 카드 정보
            string[] roleTitles =
            {
                "1. 가디언 (탱커 - Tank)\n<size=15><color=#AADDFF>외형: 파랑 | 최대 HP 3,000 (3배) & 기본 방어</color></size>",
                "2. 오라클 (힐러 - Healer)\n<size=15><color=#AAFFCC>외형: 초록 | 스마트 힐 & 노란 보호막 & 구출</color></size>",
                "3. 블레이드 (근딜 - Melee)\n<size=15><color=#FFEEAA>외형: 노랑 | 측·후면(Positional) 3단 오의 극딜</color></size>",
                "4. 아크메이지 (캐스터 - Caster)\n<size=15><color=#FFCCAA>외형: 주황 | 말뚝 화염 캐스팅 & 삼연속 마법</color></size>"
            };

            string[] roleDescs =
            {
                "<b>[역할 특징]</b> 보스 어그로 1순위 고정! 2페이즈 줄(Tether)은 <b>근딜 AI 봇이 대신 가로채 생존기로 나를 지켜줍니다!</b>\n\n" +
                "• <b>[1] 방패 강타 3단 (GCD):</b>\n  방패 가격(100) → 회전 베기(180+어그로) → 대지 분쇄(300+<b>내 체력 15% 자힐</b>)\n" +
                "• <b>[2] 심판의 투창 3단 (GCD):</b>\n  중거리 창/방패 투척 보스 주차 유도 & 성광 보호막\n" +
                "• <b>[3] 철벽 방어 (oGCD, 쿨 12초):</b>\n  4초간 받는 모든 피해 <b>60% 감소</b> (탱크버스터 방어)\n" +
                "• <b>[4] 도발 및 줄 강탈 (oGCD, 쿨 15초):</b>\n  어그로 최우선 고정 + 아군의 줄(Tether) 즉시 강탈!",

                "<b>[역할 특징]</b> 체력 비율이 가장 낮은 아군을 자동으로 우선 타겟하는 스마트 힐러!\n\n" +
                "• <b>[1] 심판의 빛 3단 (GCD):</b>\n  빛의 탄환(120) → 단죄의 빛(200) → 천벌의 기둥(350 원거리 마법 딜)\n" +
                "• <b>[2] 구원의 치유 & 보호막 (GCD):</b>\n  최저 체력 아군 <b>HP 40% 즉시 회복 + 최대 체력 20% 노란 보호막(Shield)</b> 부여\n" +
                "• <b>[3] 치유의 성역 (oGCD, 쿨 15초):</b>\n  내 발밑에 6초간 유지되는 <b>초록색 광역 힐 장판</b> 설치\n" +
                "• <b>[4] 구출 - Rescue (oGCD, 쿨 20초):</b>\n  위험 장판 안의 아군 봇을 내 위치로 끌어당기며 <b>3초간 무적/이속 버프</b> 부여!",

                "<b>[역할 특징]</b> 보스의 측면(45~135°)과 후면(135~180°)을 노려 크리티컬 오의를 꽂아 넣는 기동형 근접 딜러!\n\n" +
                "• <b>[1] 측면 3단 진화 콤보 (GCD):</b>\n  섬광 베기(100) → 질풍(250) → <b>최종 오의: 측면 일섬(450!)</b>\n" +
                "• <b>[2] 후면 3단 진화 콤보 (GCD):</b>\n  그림자 베기(100) → 암습(250) → <b>최종 오의: 심장 적출(450!)</b>\n" +
                "• <b>[3] 개인 생존기 (oGCD, 쿨 15초):</b>\n  3초간 받는 피해 <b>80% 감소</b> (줄 가로채기 방어 핵심)\n" +
                "• <b>[4] 질풍 찌르기 (oGCD, 쿨 8초):</b>\n  글쿨(GCD) 도중 즉시 끼워넣는 고속 관통 추가 타격(180)!",

                "<b>[역할 특징]</b> 스킬 핫바 위 <b>전용 캐스팅 바</b> 보유! 캐스팅 도중 WASD 이동 시 시전이 취소(<b>CAST INTERRUPTED!</b>)됩니다.\n\n" +
                "• <b>[1] 화염 폭풍 3단 캐스팅 (GCD - 말뚝 딜):</b>\n  1단 파이어(1.2s, 200) → 2단 파이가(1.5s, 380) → <b>3단 플레어(2.0s, 750 초고화력 폭발!)</b>\n" +
                "• <b>[2] 냉기 파동 3단 (GCD - 무빙샷):</b>\n  캐스팅 없이 <b>이동하면서 즉시 발사</b>(100→150→220, 장판 회피용)\n" +
                "• <b>[3] 마나 배리어 (oGCD, 쿨 15초):</b>\n  4초간 피해 70% 감소 + 피격 시 시전 끊김 면역 + 보호막\n" +
                "• <b>[4] 삼연속 마법 (oGCD, 쿨 25초):</b>\n  <b>6초 동안 [1]번 화염 캐스팅 콤보가 캐스팅 0초(즉발 무빙샷!)</b>로 발동!"
            };

            Color[] roleColors =
            {
                new Color(0.18f, 0.58f, 1.0f, 1f), // 탱커: 파랑
                new Color(0.18f, 0.90f, 0.42f, 1f), // 힐러: 초록
                new Color(1.0f, 0.85f, 0.15f, 1f),  // 근딜: 노랑
                new Color(1.0f, 0.48f, 0.12f, 1f)   // 캐스터: 주황
            };

            roleCardButtons = new Button[4];
            roleCardBanners = new Image[4];
            roleCardTitleTexts = new Text[4];
            roleCardDescTexts = new Text[4];

            const float cardWidth = 335f;
            const float cardHeight = 520f;
            const float spacing = 22f;
            float startX = -((cardWidth * 4f + spacing * 3f) * 0.5f) + cardWidth * 0.5f;

            for (int i = 0; i < 4; i++)
            {
                GameObject cardObj = new GameObject($"RoleCard_{i + 1}");
                cardObj.transform.SetParent(panelObj.transform, false);
                RectTransform cardRect = cardObj.AddComponent<RectTransform>();
                cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = new Vector2(startX + i * (cardWidth + spacing), -38f);
                cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);

                Image cardBg = cardObj.AddComponent<Image>();
                cardBg.color = new Color(0.11f, 0.14f, 0.22f, 0.98f);

                Outline cardOutline = cardObj.AddComponent<Outline>();
                cardOutline.effectColor = roleColors[i];
                cardOutline.effectDistance = new Vector2(2f, -2f);

                Button btn = cardObj.AddComponent<Button>();
                btn.targetGraphic = cardBg;
                roleCardButtons[i] = btn;

                // 상단 컬러 배너
                GameObject bannerObj = new GameObject("TopAccentBanner");
                bannerObj.transform.SetParent(cardObj.transform, false);
                RectTransform bannerRect = bannerObj.AddComponent<RectTransform>();
                bannerRect.anchorMin = new Vector2(0f, 1f);
                bannerRect.anchorMax = new Vector2(1f, 1f);
                bannerRect.pivot = new Vector2(0.5f, 1f);
                bannerRect.anchoredPosition = Vector2.zero;
                bannerRect.sizeDelta = new Vector2(0f, 12f);
                Image bannerImg = bannerObj.AddComponent<Image>();
                bannerImg.color = roleColors[i];
                bannerImg.raycastTarget = false;
                roleCardBanners[i] = bannerImg;

                // 직업 타이틀
                GameObject rTitleObj = new GameObject("RoleTitleText");
                rTitleObj.transform.SetParent(cardObj.transform, false);
                RectTransform rTitleRect = rTitleObj.AddComponent<RectTransform>();
                rTitleRect.anchorMin = new Vector2(0f, 1f);
                rTitleRect.anchorMax = new Vector2(1f, 1f);
                rTitleRect.pivot = new Vector2(0.5f, 1f);
                rTitleRect.anchoredPosition = new Vector2(0f, -18f);
                rTitleRect.sizeDelta = new Vector2(-20f, 64f);
                Text rTitleTxt = rTitleObj.AddComponent<Text>();
                rTitleTxt.font = font;
                rTitleTxt.fontSize = 21;
                rTitleTxt.fontStyle = FontStyle.Bold;
                rTitleTxt.alignment = TextAnchor.MiddleCenter;
                rTitleTxt.color = roleColors[i];
                rTitleTxt.text = roleTitles[i];
                rTitleTxt.raycastTarget = false;
                roleCardTitleTexts[i] = rTitleTxt;

                // 스킬셋 설명 본문
                GameObject rDescObj = new GameObject("RoleDescText");
                rDescObj.transform.SetParent(cardObj.transform, false);
                RectTransform rDescRect = rDescObj.AddComponent<RectTransform>();
                rDescRect.anchorMin = new Vector2(0f, 0f);
                rDescRect.anchorMax = new Vector2(1f, 1f);
                rDescRect.offsetMin = new Vector2(14f, 52f);
                rDescRect.offsetMax = new Vector2(-14f, -88f);
                Text rDescTxt = rDescObj.AddComponent<Text>();
                rDescTxt.font = font;
                rDescTxt.fontSize = 14;
                rDescTxt.alignment = TextAnchor.UpperLeft;
                rDescTxt.lineSpacing = 1.15f;
                rDescTxt.color = new Color(0.92f, 0.95f, 1.0f, 1f);
                LocalShortcutText.Set(rDescTxt, roleDescs[i]);
                rDescTxt.raycastTarget = false;
                roleCardDescTexts[i] = rDescTxt;

                // 하단 선택 버튼 안내 배지
                GameObject selectBadgeObj = new GameObject("SelectActionBadge");
                selectBadgeObj.transform.SetParent(cardObj.transform, false);
                RectTransform selectBadgeRect = selectBadgeObj.AddComponent<RectTransform>();
                selectBadgeRect.anchorMin = new Vector2(0f, 0f);
                selectBadgeRect.anchorMax = new Vector2(1f, 0f);
                selectBadgeRect.pivot = new Vector2(0.5f, 0f);
                selectBadgeRect.anchoredPosition = new Vector2(0f, 10f);
                selectBadgeRect.sizeDelta = new Vector2(-24f, 36f);
                Image badgeBg = selectBadgeObj.AddComponent<Image>();
                badgeBg.color = new Color(roleColors[i].r * 0.35f, roleColors[i].g * 0.35f, roleColors[i].b * 0.35f, 0.95f);
                badgeBg.raycastTarget = false;

                GameObject badgeTxtObj = new GameObject("BadgeText");
                badgeTxtObj.transform.SetParent(selectBadgeObj.transform, false);
                RectTransform badgeTxtRect = badgeTxtObj.AddComponent<RectTransform>();
                badgeTxtRect.anchorMin = Vector2.zero;
                badgeTxtRect.anchorMax = Vector2.one;
                badgeTxtRect.offsetMin = Vector2.zero;
                badgeTxtRect.offsetMax = Vector2.zero;
                Text badgeTxt = badgeTxtObj.AddComponent<Text>();
                badgeTxt.font = font;
                badgeTxt.fontSize = 15;
                badgeTxt.fontStyle = FontStyle.Bold;
                badgeTxt.alignment = TextAnchor.MiddleCenter;
                badgeTxt.color = Color.white;
                LocalShortcutText.Set(badgeTxt, "▶ 클릭 또는 [{key:Choice" + (i + 1) + "}]키로 선택");
                badgeTxt.raycastTarget = false;
            }

            roleModalRoot = modalObj;
        }

        private static bool WasShortcutPPressed()
        { return LocalInputBindings.Pressed("Roles"); }

        private static bool WasDigitPressed(int num)
        { return LocalInputBindings.Pressed("Choice" + num); }
    }
}
