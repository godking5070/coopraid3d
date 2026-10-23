using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    [Serializable]
    public class PartyMemberRowUI
    {
        public GameObject rowRoot;
        public Button selectButton;
        public Transform boundTarget;
        public Image aggroRedBorderImage;
        public Outline targetSelectionOutline;
        public Image roleBadgeBgImage;
        public Text roleBadgeText;
        public Text memberNameText;
        public Image hpFillImage;
        public Image shieldFillImage;
        public Text hpValueText;
    }

    /// <summary>
    /// Step 8 요구사항 3: 파판14(FF14) 스타일 '4인 파티 리스트 UI' (PartyListUI.cs)
    /// - 화면 좌측 중앙에 4인 파티원(나 + AI 봇 3명)의
    ///   역할군 아이콘 색상, 이름, 현재 체력바(HP), 노란색 보호막 게이지(Shield), 현재 어그로 1순위 표시(붉은 테두리)를 실시간으로 렌더링합니다.
    /// </summary>
    public class PartyListUI : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;
        public BossController boss;
        public List<PartyBotAI> partyBots = new List<PartyBotAI>();

        [Header("UI Elements")]
        public RectTransform panelRoot;
        public PartyMemberRowUI[] memberRows = new PartyMemberRowUI[4];

        private Sprite _whiteSprite;

        private void Start()
        {
            RefreshPartyReferences();
            EnsureUI();
            if (panelRoot != null) LocalShortcutText.Set(panelRoot.Find("HeaderTitle")?.GetComponent<Text>(), "◆ PARTY LIST · 클릭 / {key:Party1} {key:Party2} {key:Party3} {key:Party4}");
            EnsureFillSprites();
        }

        public void RefreshPartyReferences()
        {
            if (player == null)
            {
                player = FindFirstObjectByType<PlayerController>();
            }
            if (boss == null)
            {
                boss = FindFirstObjectByType<BossController>();
            }

            PartyBotAI[] foundBots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            partyBots.Clear();
            foreach (PartyBotAI bot in foundBots)
            {
                if (bot != null)
                {
                    partyBots.Add(bot);
                }
            }
            partyBots.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name, StringComparison.Ordinal));
        }

        public void EnsureUI()
        {
            if (panelRoot != null && memberRows != null && memberRows.Length == 4 && memberRows[0] != null && memberRows[0].rowRoot != null)
            {
                BindRowClickListeners();
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = FindFirstObjectByType<Canvas>();
            }
            if (canvas == null)
            {
                return;
            }

            Transform existing = canvas.transform.Find("PartyList_LeftHUD");
            if (existing != null && panelRoot == null)
            {
                panelRoot = existing.GetComponent<RectTransform>();
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (panelRoot == null)
            {
                GameObject panelObj = new GameObject("PartyList_LeftHUD");
                panelObj.transform.SetParent(canvas.transform, false);
                panelRoot = panelObj.AddComponent<RectTransform>();
                panelRoot.anchorMin = new Vector2(0f, 0.5f);
                panelRoot.anchorMax = new Vector2(0f, 0.5f);
                panelRoot.pivot = new Vector2(0f, 0.5f);
                panelRoot.anchoredPosition = new Vector2(18f, 65f);
                panelRoot.sizeDelta = new Vector2(335f, 316f);

                Image panelBg = panelObj.AddComponent<Image>();
                panelBg.color = new Color(0.05f, 0.07f, 0.12f, 0.88f);
                Outline panelOutline = panelObj.AddComponent<Outline>();
                panelOutline.effectColor = new Color(0.28f, 0.42f, 0.65f, 0.9f);
                panelOutline.effectDistance = new Vector2(2f, -2f);

                // 상단 헤더 타이틀
                GameObject headerObj = new GameObject("HeaderTitle");
                headerObj.transform.SetParent(panelObj.transform, false);
                RectTransform headerRect = headerObj.AddComponent<RectTransform>();
                headerRect.anchorMin = new Vector2(0f, 1f);
                headerRect.anchorMax = new Vector2(1f, 1f);
                headerRect.pivot = new Vector2(0.5f, 1f);
                headerRect.anchoredPosition = new Vector2(0f, -6f);
                headerRect.sizeDelta = new Vector2(-16f, 26f);

                Text headerTxt = headerObj.AddComponent<Text>();
                headerTxt.font = font;
                headerTxt.fontSize = 13;
                headerTxt.fontStyle = FontStyle.Bold;
                headerTxt.alignment = TextAnchor.MiddleLeft;
                headerTxt.color = new Color(0.85f, 0.92f, 1.0f, 1f);
                LocalShortcutText.Set(headerTxt, "◆ PARTY LIST · 클릭 / {key:Party1} {key:Party2} {key:Party3} {key:Party4}");
                headerTxt.raycastTarget = false;
            }
            else
            {
                panelRoot.anchoredPosition = new Vector2(18f, 65f);
            }

            memberRows = new PartyMemberRowUI[4];
            for (int i = 0; i < 4; i++)
            {
                Transform existingRow = panelRoot.Find($"PartyMemberRow_{i}");
                if (existingRow != null)
                {
                    DestroyImmediate(existingRow.gameObject);
                }

                memberRows[i] = CreatePartyMemberRow(panelRoot, i, font);
            }
            BindRowClickListeners();
        }

        private void BindRowClickListeners()
        {
            if (memberRows == null) return;

            for (int i = 0; i < memberRows.Length; i++)
            {
                PartyMemberRowUI row = memberRows[i];
                if (row == null || row.rowRoot == null) continue;

                if (row.targetSelectionOutline == null)
                {
                    row.targetSelectionOutline = row.rowRoot.GetComponent<Outline>();
                    if (row.targetSelectionOutline == null)
                    {
                        row.targetSelectionOutline = row.rowRoot.AddComponent<Outline>();
                    }
                    row.targetSelectionOutline.effectDistance = new Vector2(2.5f, -2.5f);
                }

                if (row.selectButton == null)
                {
                    row.selectButton = row.rowRoot.GetComponent<Button>();
                    if (row.selectButton == null)
                    {
                        row.selectButton = row.rowRoot.AddComponent<Button>();
                    }
                }
                row.selectButton.targetGraphic = row.aggroRedBorderImage;

                int capturedIndex = i;
                PartyMemberRowUI capturedRow = row;
                row.selectButton.onClick.RemoveAllListeners();
                row.selectButton.onClick.AddListener(() =>
                {
                    if (TargetingManager.Instance != null)
                    {
                        if (capturedRow.boundTarget != null)
                        {
                            TargetingManager.Instance.SetTarget(capturedRow.boundTarget);
                        }
                        else
                        {
                            TargetingManager.Instance.SelectPartyMemberByIndex(capturedIndex);
                        }
                    }
                });
            }
        }

        public bool TrySelectPartyMemberAtScreenPoint(Vector3 screenPos)
        {
            if (memberRows == null || TargetingManager.Instance == null) return false;

            Vector2 pt = new Vector2(screenPos.x, screenPos.y);
            for (int i = 0; i < memberRows.Length; i++)
            {
                PartyMemberRowUI row = memberRows[i];
                if (row == null || row.rowRoot == null || !row.rowRoot.activeInHierarchy) continue;

                RectTransform rt = row.rowRoot.GetComponent<RectTransform>();
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, pt, null))
                {
                    if (row.boundTarget != null)
                    {
                        TargetingManager.Instance.SetTarget(row.boundTarget);
                    }
                    else
                    {
                        TargetingManager.Instance.SelectPartyMemberByIndex(i);
                    }
                    return true;
                }
            }
            return false;
        }

        private static PartyMemberRowUI CreatePartyMemberRow(RectTransform parent, int index, Font font)
        {
            PartyMemberRowUI row = new PartyMemberRowUI();

            GameObject rowObj = new GameObject($"PartyMemberRow_{index}");
            rowObj.transform.SetParent(parent, false);
            RectTransform rowRect = rowObj.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -36f - index * 68f);
            rowRect.sizeDelta = new Vector2(-16f, 62f);

            // 어그로 1순위 붉은 테두리 배경
            row.aggroRedBorderImage = rowObj.AddComponent<Image>();
            row.aggroRedBorderImage.color = new Color(0.20f, 0.24f, 0.32f, 0.92f);

            // 타겟 선택 시 밝은 하늘색 아웃라인
            row.targetSelectionOutline = rowObj.AddComponent<Outline>();
            row.targetSelectionOutline.effectColor = new Color(0.20f, 0.90f, 1.0f, 0f);
            row.targetSelectionOutline.effectDistance = new Vector2(2.5f, -2.5f);

            // 마우스 클릭으로 즉시 해당 파티원 타겟 지정
            row.selectButton = rowObj.AddComponent<Button>();
            row.selectButton.targetGraphic = row.aggroRedBorderImage;
            int capturedIndex = index;
            PartyMemberRowUI capturedRow = row;
            row.selectButton.onClick.AddListener(() =>
            {
                if (TargetingManager.Instance != null)
                {
                    if (capturedRow.boundTarget != null)
                    {
                        TargetingManager.Instance.SetTarget(capturedRow.boundTarget);
                    }
                    else
                    {
                        TargetingManager.Instance.SelectPartyMemberByIndex(capturedIndex);
                    }
                }
            });

            // 내부 다크 슬롯 배경
            GameObject innerBgObj = new GameObject("InnerBg");
            innerBgObj.transform.SetParent(rowObj.transform, false);
            RectTransform innerRect = innerBgObj.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(3f, 3f);
            innerRect.offsetMax = new Vector2(-3f, -3f);
            Image innerImg = innerBgObj.AddComponent<Image>();
            innerImg.color = new Color(0.09f, 0.11f, 0.16f, 0.96f);

            // 좌측 역할군 색상 아이콘 박스
            GameObject roleBoxObj = new GameObject("RoleIconBox");
            roleBoxObj.transform.SetParent(innerBgObj.transform, false);
            RectTransform roleBoxRect = roleBoxObj.AddComponent<RectTransform>();
            roleBoxRect.anchorMin = new Vector2(0f, 0.5f);
            roleBoxRect.anchorMax = new Vector2(0f, 0.5f);
            roleBoxRect.pivot = new Vector2(0f, 0.5f);
            roleBoxRect.anchoredPosition = new Vector2(6f, 0f);
            roleBoxRect.sizeDelta = new Vector2(44f, 44f);
            row.roleBadgeBgImage = roleBoxObj.AddComponent<Image>();
            row.roleBadgeBgImage.color = new Color(0.2f, 0.6f, 1f, 1f);

            GameObject roleTxtObj = new GameObject("RoleBadgeText");
            roleTxtObj.transform.SetParent(roleBoxObj.transform, false);
            RectTransform roleTxtRect = roleTxtObj.AddComponent<RectTransform>();
            roleTxtRect.anchorMin = Vector2.zero;
            roleTxtRect.anchorMax = Vector2.one;
            roleTxtRect.offsetMin = Vector2.zero;
            roleTxtRect.offsetMax = Vector2.zero;
            row.roleBadgeText = roleTxtObj.AddComponent<Text>();
            row.roleBadgeText.font = font;
            row.roleBadgeText.fontSize = 12;
            row.roleBadgeText.fontStyle = FontStyle.Bold;
            row.roleBadgeText.alignment = TextAnchor.MiddleCenter;
            row.roleBadgeText.color = new Color(0.05f, 0.05f, 0.08f, 1f);
            row.roleBadgeText.text = "ROLE";
            row.roleBadgeText.raycastTarget = false;

            // 이름 및 어그로 표시 텍스트
            GameObject nameObj = new GameObject("MemberNameText");
            nameObj.transform.SetParent(innerBgObj.transform, false);
            RectTransform nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(58f, -4f);
            nameRect.sizeDelta = new Vector2(-64f, 20f);
            row.memberNameText = nameObj.AddComponent<Text>();
            row.memberNameText.font = font;
            row.memberNameText.fontSize = 13;
            row.memberNameText.fontStyle = FontStyle.Bold;
            row.memberNameText.alignment = TextAnchor.MiddleLeft;
            row.memberNameText.color = Color.white;
            row.memberNameText.raycastTarget = false;

            // 체력(HP) 게이지 배경
            GameObject hpBgObj = new GameObject("HP_BarBg");
            hpBgObj.transform.SetParent(innerBgObj.transform, false);
            RectTransform hpBgRect = hpBgObj.AddComponent<RectTransform>();
            hpBgRect.anchorMin = new Vector2(0f, 0f);
            hpBgRect.anchorMax = new Vector2(1f, 0f);
            hpBgRect.pivot = new Vector2(0f, 0f);
            hpBgRect.anchoredPosition = new Vector2(58f, 14f);
            hpBgRect.sizeDelta = new Vector2(-66f, 16f);
            Image hpBgImg = hpBgObj.AddComponent<Image>();
            hpBgImg.color = new Color(0.04f, 0.05f, 0.08f, 0.95f);

            // 체력(HP) Fill
            GameObject hpFillObj = new GameObject("HP_Fill");
            hpFillObj.transform.SetParent(hpBgObj.transform, false);
            RectTransform hpFillRect = hpFillObj.AddComponent<RectTransform>();
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = Vector2.one;
            hpFillRect.offsetMin = Vector2.zero;
            hpFillRect.offsetMax = Vector2.zero;
            row.hpFillImage = hpFillObj.AddComponent<Image>();
            row.hpFillImage.color = new Color(0.25f, 0.9f, 0.45f, 1f);

            // 보호막(Shield) 게이지 배경 (HP 바 바로 아래 노란색 게이지)
            GameObject shBgObj = new GameObject("Shield_BarBg");
            shBgObj.transform.SetParent(innerBgObj.transform, false);
            RectTransform shBgRect = shBgObj.AddComponent<RectTransform>();
            shBgRect.anchorMin = new Vector2(0f, 0f);
            shBgRect.anchorMax = new Vector2(1f, 0f);
            shBgRect.pivot = new Vector2(0f, 0f);
            shBgRect.anchoredPosition = new Vector2(58f, 5f);
            shBgRect.sizeDelta = new Vector2(-66f, 7f);
            Image shBgImg = shBgObj.AddComponent<Image>();
            shBgImg.color = new Color(0.08f, 0.08f, 0.10f, 0.9f);

            GameObject shFillObj = new GameObject("Shield_Fill");
            shFillObj.transform.SetParent(shBgObj.transform, false);
            RectTransform shFillRect = shFillObj.AddComponent<RectTransform>();
            shFillRect.anchorMin = Vector2.zero;
            shFillRect.anchorMax = Vector2.one;
            shFillRect.offsetMin = Vector2.zero;
            shFillRect.offsetMax = Vector2.zero;
            row.shieldFillImage = shFillObj.AddComponent<Image>();
            row.shieldFillImage.color = new Color(1.0f, 0.90f, 0.18f, 1f);

            // HP + Shield 수치 텍스트 (HP 바 위에 표시)
            GameObject hpTxtObj = new GameObject("HP_ValueText");
            hpTxtObj.transform.SetParent(hpBgObj.transform, false);
            RectTransform hpTxtRect = hpTxtObj.AddComponent<RectTransform>();
            hpTxtRect.anchorMin = Vector2.zero;
            hpTxtRect.anchorMax = Vector2.one;
            hpTxtRect.offsetMin = new Vector2(4f, -2f);
            hpTxtRect.offsetMax = new Vector2(-4f, 2f);
            row.hpValueText = hpTxtObj.AddComponent<Text>();
            row.hpValueText.font = font;
            row.hpValueText.fontSize = 11;
            row.hpValueText.fontStyle = FontStyle.Bold;
            row.hpValueText.alignment = TextAnchor.MiddleRight;
            row.hpValueText.color = Color.white;
            row.hpValueText.raycastTarget = false;

            row.rowRoot = rowObj;
            return row;
        }

        private void EnsureFillSprites()
        {
            if (_whiteSprite == null)
            {
                _whiteSprite = Sprite.Create(
                    Texture2D.whiteTexture,
                    new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                    new Vector2(0.5f, 0.5f)
                );
            }

            if (memberRows == null) return;

            foreach (PartyMemberRowUI row in memberRows)
            {
                if (row == null) continue;
                if (row.hpFillImage != null)
                {
                    if (row.hpFillImage.sprite == null) row.hpFillImage.sprite = _whiteSprite;
                    row.hpFillImage.type = Image.Type.Filled;
                    row.hpFillImage.fillMethod = Image.FillMethod.Horizontal;
                    row.hpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                }
                if (row.shieldFillImage != null)
                {
                    if (row.shieldFillImage.sprite == null) row.shieldFillImage.sprite = _whiteSprite;
                    row.shieldFillImage.type = Image.Type.Filled;
                    row.shieldFillImage.fillMethod = Image.FillMethod.Horizontal;
                    row.shieldFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                }
            }
        }

        private void Update()
        {
            if (RaidNetworkSession.Multiplayer) { UpdateNetworkParty(); return; }
            if (player == null || partyBots.Count == 0)
            {
                RefreshPartyReferences();
            }

            if (memberRows == null || memberRows.Length < 4)
            {
                return;
            }

            Transform currentAggro = boss != null ? boss.aggroTarget : null;
            Transform currentSelectedTarget = TargetingManager.Instance != null ? TargetingManager.Instance.CurrentTarget : null;

            // 1번 슬롯: 플레이어 (나 - F1)
            if (player != null && memberRows[0] != null)
            {
                memberRows[0].boundTarget = player.transform;
                bool isPlayerAggro = currentAggro != null && currentAggro == player.transform;
                bool isPlayerTargeted = currentSelectedTarget != null && currentSelectedTarget == player.transform;
                GetPlayerRoleBadge(player.currentRole, out string badgeCode, out Color roleColor);

                UpdateRowVisual(
                    memberRows[0],
                    $"[{LocalInputBindings.Label("Party1")}]\n{badgeCode}",
                    roleColor,
                    $"[{LocalInputBindings.Label("Party1")}] {CharacterActor.Ensure(player.gameObject).Definition.name}",
                    player.currentHp,
                    player.maxHp,
                    player.currentShield,
                    isPlayerAggro,
                    isPlayerTargeted,
                    isDead: player.IsDead
                );
            }

            // 2~4번 슬롯: AI 파티원 봇 3명 (F2 ~ F4)
            for (int i = 0; i < 3; i++)
            {
                PartyMemberRowUI row = memberRows[i + 1];
                if (row == null) continue;

                if (i < partyBots.Count && partyBots[i] != null)
                {
                    PartyBotAI bot = partyBots[i];
                    row.boundTarget = bot.transform;
                    bool isBotAggro = currentAggro != null && currentAggro == bot.transform;
                    bool isBotTargeted = currentSelectedTarget != null && currentSelectedTarget == bot.transform;
                    GetBotRoleBadge(bot.role, out string badgeCode, out Color roleColor);

                    string fKeyTag = $"[{LocalInputBindings.Label("Party" + (i + 2))}]";
                    UpdateRowVisual(
                        row,
                        $"{fKeyTag}\n{badgeCode}",
                        roleColor,
                        $"{fKeyTag} AI · {CharacterActor.Ensure(bot.gameObject).Definition.name}",
                        bot.currentHp,
                        bot.maxHp,
                        bot.currentShield,
                        isBotAggro,
                        isBotTargeted,
                        bot.isDead
                    );
                }
            }
        }

        private void UpdateNetworkParty()
        {
            if (memberRows == null || memberRows.Length != 4) return;
            var members = RaidNetworkSession.Instance.OrderedParty();
            for (int i = 0; i < 4; i++)
            {
                var row = memberRows[i]; if (row == null) continue;
                row.boundTarget = i < members.Count ? members[i] : null;
                var avatar = row.boundTarget != null ? row.boundTarget.GetComponent<NetworkRaidAvatar>() : null;
                bool selected = avatar != null && TargetingManager.Instance != null && TargetingManager.Instance.CurrentTarget == avatar.transform;
                var state = avatar?.State;
                UpdateRowVisual(row, LocalInputBindings.Label("Party" + (i + 1)), avatar != null ? NetworkRaidAvatar.RoleColor(avatar.Role) : Color.gray,
                    avatar != null ? $"[{LocalInputBindings.Label("Party" + (i + 1))}] {avatar.DisplayName} · {avatar.RoleName}" : "접속 대기", state?.hp ?? 1, state?.maxHp ?? 1, state?.shield ?? 0,
                    avatar != null && boss != null && boss.aggroTarget == avatar.transform, selected, state?.dead == true);
                if (row.hpValueText != null && state != null)
                {
                    string suffix = state.dead ? " · 사망" : state.protection > 0 ? " · 부활 보호" : state.resurrecting ? " · 부활 시전" : state.casting ? " · " + state.castName : "";
                    if (!state.dead && state.defense > 0) suffix += " · 방어";
                    if (!state.dead && state.haste > 0) suffix += " · 가속";
                    if(!state.dead)suffix+=RouteEffectLabel(row.boundTarget);
                    if (RaidNetworkSession.Instance.BattlePhase == RaidBattlePhase.Reward) suffix = avatar.IsBot ? " · AI" : state.rewardChosen ? " · 선택 완료" : " · 카드 선택 중";
                    string connection = RaidNetworkSession.Instance.ConnectionLabel(state);
                    row.hpValueText.text = $"{state.hp:0}/{state.maxHp:0} (+{state.shield:0}){suffix}";
                    if (row.memberNameText != null)
                    {
                        if (connection.Length > 0) row.memberNameText.text += "\n" + connection;
                        row.memberNameText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 22);
                        row.memberNameText.lineSpacing = .85f; row.memberNameText.resizeTextForBestFit = true;
                        row.memberNameText.resizeTextMinSize = 9; row.memberNameText.resizeTextMaxSize = 13;
                    }
                }
            }
        }

        private static void UpdateRowVisual(
            PartyMemberRowUI row,
            string badgeText,
            Color roleColor,
            string displayName,
            float curHp,
            float maxHp,
            float curShield,
            bool isAggroTop,
            bool isTargetedByPlayer,
            bool isDead)
        {
            if (row.roleBadgeBgImage != null)
            {
                row.roleBadgeBgImage.color = isDead ? new Color(0.3f, 0.3f, 0.35f, 1f) : roleColor;
            }
            if (row.roleBadgeText != null)
            {
                row.roleBadgeText.fontSize = 10;
                row.roleBadgeText.text = badgeText;
            }

            if (row.targetSelectionOutline != null)
            {
                row.targetSelectionOutline.effectColor = isTargetedByPlayer
                    ? new Color(0.20f, 0.92f, 1.0f, 1.0f)
                    : new Color(0f, 0f, 0f, 0f);
            }

            // 어그로 1순위 표시 (붉은 테두리 하이라이트) + 아군 타겟 선택 시 하늘색 강조
            if (row.aggroRedBorderImage != null)
            {
                if (isTargetedByPlayer)
                {
                    row.aggroRedBorderImage.color = new Color(0.18f, 0.85f, 1.0f, 0.96f);
                }
                else if (isAggroTop && !isDead)
                {
                    float pulse = 0.78f + Mathf.PingPong(Time.unscaledTime * 3.5f, 0.22f);
                    row.aggroRedBorderImage.color = new Color(1.0f, 0.16f, 0.16f, pulse);
                }
                else
                {
                    row.aggroRedBorderImage.color = new Color(0.20f, 0.25f, 0.34f, 0.88f);
                }
            }

            if (row.memberNameText != null)
            {
                string targetPrefix = isTargetedByPlayer ? "<color=#33E6FF>▶ </color>" : "";
                string aggroTag = isAggroTop && !isDead ? " <color=#FF4444>[어그로1]</color>" : "";
                string deadTag = isDead ? " <color=#FF3333>(전투불능)</color>" : "";
                if (!isDead && row.boundTarget != null && row.boundTarget.GetComponent<CommonPotion>()?.HasRevivalProtection == true)
                    deadTag = " <color=#88FFFF>[부활 보호]</color>";
                row.memberNameText.text = $"{targetPrefix}{displayName}{aggroTag}{deadTag}";
                row.memberNameText.color = isDead
                    ? new Color(0.6f, 0.6f, 0.65f, 1f)
                    : Color.Lerp(roleColor, Color.white, 0.55f);
            }

            float hpRatio = maxHp > 0.1f ? Mathf.Clamp01(curHp / maxHp) : 0f;
            float shieldRatio = maxHp > 0.1f ? Mathf.Clamp01(curShield / maxHp) : 0f;

            if (row.hpFillImage != null)
            {
                row.hpFillImage.fillAmount = hpRatio;
                row.hpFillImage.color = isDead
                    ? new Color(0.35f, 0.15f, 0.15f, 1f)
                    : (hpRatio < 0.35f ? new Color(1.0f, 0.32f, 0.25f, 1f) : roleColor);
            }

            if (row.shieldFillImage != null)
            {
                row.shieldFillImage.fillAmount = shieldRatio;
                row.shieldFillImage.color = new Color(1.0f, 0.90f, 0.18f, 1f);
            }

            if (row.hpValueText != null)
            {
                string shieldTxt = curShield > 0.5f ? $" <color=#FFEE33>(+{Mathf.RoundToInt(curShield)})</color>" : "";
                row.hpValueText.text = $"HP {Mathf.CeilToInt(curHp)}/{Mathf.CeilToInt(maxHp)}{shieldTxt}"+(!isDead?RouteEffectLabel(row.boundTarget):"");
            }
        }

        private static string RouteEffectLabel(Transform member)
        {
            if(member==null)return "";
            float protection=member.GetComponent<CombatStatusEffects>()?.KnockbackImmunity??0;
            var paladin=member.GetComponent<AegisPaladin>();
            if(protection>0)return " · 성채 "+protection.ToString("0.0")+"s";
            if(paladin?.IsActive!=true)return "";
            if(paladin.DamageState.Route.Guard>0)return " · 가드 "+paladin.DamageState.Route.Guard.ToString("0.0")+"s";
            if(paladin.Transformed)return " · 쌍성검 "+paladin.DamageState.Route.Transformation.ToString("0.0")+"s";
            return "";
        }
        private static void GetPlayerRoleBadge(PlayerRole role, out string badgeCode, out Color color)
        {
            switch (role)
            {
                case PlayerRole.Guardian_Tank:
                    badgeCode = "TANK\n가디언";
                    color = new Color(0.20f, 0.58f, 1.0f, 1f);
                    break;
                case PlayerRole.Oracle_Healer:
                    badgeCode = "HEAL\n오라클";
                    color = new Color(0.20f, 0.90f, 0.42f, 1f);
                    break;
                case PlayerRole.Archmage_Caster:
                    badgeCode = "CAST\n흑마";
                    color = new Color(1.0f, 0.50f, 0.15f, 1f);
                    break;
                case PlayerRole.Blade_MeleeDPS:
                default:
                    badgeCode = "DPS\n근딜";
                    color = new Color(1.0f, 0.85f, 0.15f, 1f);
                    break;
            }
        }

        private static void GetBotRoleBadge(PartyBotRole role, out string badgeCode, out Color color)
        {
            switch (role)
            {
                case PartyBotRole.Tank:
                    badgeCode = "TANK\n봇";
                    color = new Color(0.20f, 0.58f, 1.0f, 1f);
                    break;
                case PartyBotRole.Healer:
                    badgeCode = "HEAL\n봇";
                    color = new Color(0.20f, 0.90f, 0.42f, 1f);
                    break;
                case PartyBotRole.MeleeDPS:
                    badgeCode = "MELEE\n봇";
                    color = new Color(1.0f, 0.85f, 0.15f, 1f);
                    break;
                case PartyBotRole.RangedDPS:
                default:
                    badgeCode = "CAST\n봇";
                    color = new Color(1.0f, 0.50f, 0.15f, 1f);
                    break;
            }
        }
    }
}
