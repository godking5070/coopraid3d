using System;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    [Serializable]
    public class HotbarSlotView
    {
        public Button slotButton;
        public Image borderHighlightImage;
        public Image iconBackgroundImage;
        public Text keybindBadgeText;
        public Text stageBadgeText;
        public Text skillNameText;
        public Image cooldownOverlayImage;
        public Text cooldownNumberText;
        public GameObject comboTimerBarRoot;
        public Image comboTimerFillImage;
        public Text comboTimerText;
    }

    /// <summary>
    /// Step 5 ~ Step 8 통합 스킬 핫바 UI:
    /// - 화면 하단 중앙에 [1], [2], [3], [4] 4개의 정사각형 스킬 슬롯 UI를 표시하고
    ///   현재 선택된 직업(1.가디언 / 2.오라클 / 3.블레이드 / 4.아크메이지)에 맞춰 스킬명, 아이콘 색상, 3단 진화 상태, GCD/oGCD 쿨타임을 실시간 표시합니다.
    /// - 스킬 핫바 바로 위에 아크메이지(캐스터) 전용 '플레이어 캐스팅 게이지 바(Player Cast Bar)' 및 이동 시 취소 경고("CAST INTERRUPTED!")를 표시합니다.
    /// </summary>
    public class SkillHotbarUI : MonoBehaviour
    {
        [Header("References")]
        public PlayerCombatController combatController;
        public HotbarSlotView[] slots = new HotbarSlotView[4];
        public Text gcdSummaryText;

        [Header("Player Cast Bar UI (Step 8 - 스킬 핫바 바로 위 플레이어 캐스팅 바)")]
        public GameObject playerCastBarRoot;
        public Image playerCastFillImage;
        public Text playerCastText;

        private Sprite _whiteFillSprite;

        private void Start()
        {
            if (combatController == null)
            {
                combatController = FindFirstObjectByType<PlayerCombatController>();
            }

            EnsurePlayerCastBarUI();
            EnsureFillSprites();
            BindSlotClickButtons();
            if (GetComponent<CharacterFrameworkUI>() == null) gameObject.AddComponent<CharacterFrameworkUI>();
        }

        private void EnsurePlayerCastBarUI()
        {
            if (playerCastBarRoot != null)
            {
                return;
            }

            Transform existing = transform.Find("PlayerCastBar_Root");
            if (existing != null)
            {
                playerCastBarRoot = existing.gameObject;
                Transform fillTr = existing.Find("PlayerCastBar_Fill");
                if (fillTr != null) playerCastFillImage = fillTr.GetComponent<Image>();
                Transform txtTr = existing.Find("PlayerCastBar_Text");
                if (txtTr != null) playerCastText = txtTr.GetComponent<Text>();
                return;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject rootObj = new GameObject("PlayerCastBar_Root");
            rootObj.transform.SetParent(transform, false);
            RectTransform rootRect = rootObj.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 1.0f);
            rootRect.anchorMax = new Vector2(0.5f, 1.0f);
            rootRect.pivot = new Vector2(0.5f, 0.0f);
            rootRect.anchoredPosition = new Vector2(0f, 10f);
            rootRect.sizeDelta = new Vector2(540f, 34f);

            Image bgImg = rootObj.AddComponent<Image>();
            bgImg.color = new Color(0.06f, 0.07f, 0.11f, 0.94f);
            Outline bgOutline = rootObj.AddComponent<Outline>();
            bgOutline.effectColor = new Color(1.0f, 0.55f, 0.18f, 0.9f);
            bgOutline.effectDistance = new Vector2(2f, -2f);

            GameObject fillObj = new GameObject("PlayerCastBar_Fill");
            fillObj.transform.SetParent(rootObj.transform, false);
            RectTransform fillRect = fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = new Vector2(4f, 4f);
            fillRect.offsetMax = new Vector2(-4f, -4f);
            playerCastFillImage = fillObj.AddComponent<Image>();
            playerCastFillImage.color = new Color(1.0f, 0.48f, 0.12f, 1f);

            GameObject txtObj = new GameObject("PlayerCastBar_Text");
            txtObj.transform.SetParent(rootObj.transform, false);
            RectTransform txtRect = txtObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(8f, 0f);
            txtRect.offsetMax = new Vector2(-8f, 0f);
            playerCastText = txtObj.AddComponent<Text>();
            playerCastText.font = font;
            playerCastText.fontSize = 15;
            playerCastText.fontStyle = FontStyle.Bold;
            playerCastText.alignment = TextAnchor.MiddleCenter;
            playerCastText.color = Color.white;
            playerCastText.raycastTarget = false;

            playerCastBarRoot = rootObj;
        }

        private void EnsureFillSprites()
        {
            _whiteFillSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f)
            );

            if (playerCastFillImage != null)
            {
                if (playerCastFillImage.sprite == null) playerCastFillImage.sprite = _whiteFillSprite;
                playerCastFillImage.type = Image.Type.Filled;
                playerCastFillImage.fillMethod = Image.FillMethod.Horizontal;
                playerCastFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            if (slots == null)
            {
                return;
            }

            foreach (HotbarSlotView slot in slots)
            {
                if (slot == null) continue;

                if (slot.cooldownOverlayImage != null)
                {
                    if (slot.cooldownOverlayImage.sprite == null)
                    {
                        slot.cooldownOverlayImage.sprite = _whiteFillSprite;
                    }
                    slot.cooldownOverlayImage.type = Image.Type.Filled;
                    slot.cooldownOverlayImage.fillMethod = Image.FillMethod.Vertical;
                    slot.cooldownOverlayImage.fillOrigin = (int)Image.OriginVertical.Bottom;
                }

                if (slot.comboTimerFillImage != null)
                {
                    if (slot.comboTimerFillImage.sprite == null)
                    {
                        slot.comboTimerFillImage.sprite = _whiteFillSprite;
                    }
                    slot.comboTimerFillImage.type = Image.Type.Filled;
                    slot.comboTimerFillImage.fillMethod = Image.FillMethod.Horizontal;
                    slot.comboTimerFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
                }
            }
        }

        private void BindSlotClickButtons()
        {
            if (slots == null || slots.Length < 4)
            {
                return;
            }

            if (slots[0]?.slotButton != null)
            {
                slots[0].slotButton.onClick.RemoveAllListeners();
                slots[0].slotButton.onClick.AddListener(() => combatController?.TryUseSkill1_FlankCombo());
            }
            if (slots[1]?.slotButton != null)
            {
                slots[1].slotButton.onClick.RemoveAllListeners();
                slots[1].slotButton.onClick.AddListener(() => combatController?.TryUseSkill2_RearCombo());
            }
            if (slots[2]?.slotButton != null)
            {
                slots[2].slotButton.onClick.RemoveAllListeners();
                slots[2].slotButton.onClick.AddListener(() => combatController?.TryUseSkill3_PersonalDefensive_oGCD());
            }
            if (slots[3]?.slotButton != null)
            {
                slots[3].slotButton.onClick.RemoveAllListeners();
                slots[3].slotButton.onClick.AddListener(() => combatController?.TryUseSkill4_GaleThrust_oGCD());
            }
        }

        private void Update()
        {
            if (combatController == null || slots == null || slots.Length < 4)
            {
                return;
            }

            if (UpdateCommonCharacter()) return;
            PlayerRole role = combatController.CurrentRole;
            UpdateSlot1(slots[0], role);
            UpdateSlot2(slots[1], role);
            UpdateSlot3(slots[2], role);
            UpdateSlot4(slots[3], role);
            UpdatePlayerCastBar(role);
            if (playerCastText != null) playerCastText.text = LocalInputBindings.SkillText(playerCastText.text);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i]?.keybindBadgeText != null) slots[i].keybindBadgeText.text = LocalInputBindings.Label("Skill" + (i + 1));
                if (slots[i]?.skillNameText != null) slots[i].skillNameText.text = LocalInputBindings.SkillText(slots[i].skillNameText.text);
            }

            if (gcdSummaryText != null)
            {
                string roleGuide = role switch
                {
                    PlayerRole.Guardian_Tank => "<color=#55BBFF>[가디언(탱커)]</color> 보스 어그로 1순위 고정! 2페이즈 줄(Tether)은 근딜 봇이 대신 가로채 지켜주며, [4]도발로 강탈도 가능!",
                    PlayerRole.Oracle_Healer => "<color=#44FF77>[오라클(힐러)]</color> [2]스마트 힐+노란 보호막 | [3]초록 힐 장판 | [4]구출(위험 장판 아군 끌어당기기 + 3초 무적)!",
                    PlayerRole.Archmage_Caster => "<color=#FF9933>[아크메이지(캐스터)]</color> [1]화염 말뚝 캐스팅(WASD 이동 시 취소!) | [2]무빙 냉기샷 | [4]삼연속 마법(6초 화염 즉발)!",
                    _ => "<color=#FFEE55>[블레이드(근딜)]</color> [1]측면·[2]후면 3단 진화 웨폰스킬(GCD) + [3]생존기·[4]질풍 찌르기(oGCD 끼워넣기)!"
                };

                gcdSummaryText.text =
                    $"현재 GCD: <b>{combatController.currentGcdDuration:F1}s</b>  |  {LocalInputBindings.SkillText(roleGuide)}";
            }
        }

        private bool UpdateCommonCharacter()
        {
            var actor = combatController.GetComponent<CharacterActor>();
            if (actor?.Definition == null || actor.Definition.legacyRole > 0) return false;
            var state = actor.Snapshot;
            for (int i = 0; i < 4; i++)
            {
                var slot = slots[i]; if (slot == null) continue;
                var commonSlot = CharacterSlots.LegacySlot(i + 1);
                var selection = System.Array.Find(state.selections, s => s.slot == commonSlot);
                var skill = selection == null ? null : actor.Definition.Skill(selection.skillId);
                bool available = skill != null && skill.implemented && System.Array.IndexOf(state.unlocked, skill.id) >= 0;
                var timing = skill == null ? null : System.Array.Find(state.skills, s => s.id == skill.id);
                float remaining = timing != null && timing.charges == 0 ? timing.recharge : 0;
                float total = timing?.rechargeTotal ?? 0;
                if (skill != null && skill.usesGcd && state.gcd > remaining) { remaining = state.gcd; total = state.gcdTotal; }
                if (slot.keybindBadgeText != null) slot.keybindBadgeText.text = LocalInputBindings.Label(CharacterSlots.Binding(commonSlot));
                if (slot.skillNameText != null) slot.skillNameText.text = actor.Definition.id==AegisPaladinData.JobId && (i==0||i==1)
                    ? (i==0?AegisPaladinData.SwordNames[state.combo1-1]:AegisPaladinData.ShieldNames[state.combo2-1]) : skill?.name ?? "빈 슬롯";
                var job=CharacterRosterData.Find(actor.Definition.id);
                if(job!=null&&i<2&&slot.skillNameText!=null)slot.skillNameText.text=i==0?job.combo1[state.combo1-1]:job.combo2[state.combo2-1];
                if(job?.index==6&&i<3&&actor.GetComponent<CharacterKit>()?.Has("ninjutsu")==true&&slot.skillNameText!=null)slot.skillNameText.text=CharacterRosterData.Ninjutsu(i==0?"g1":i==1?"g2":"a").name;
                if (slot.slotButton != null) slot.slotButton.interactable = available;
                if (slot.cooldownOverlayImage != null) slot.cooldownOverlayImage.fillAmount = total > 0 ? Mathf.Clamp01(remaining / total) : 0;
                if (slot.cooldownNumberText != null) slot.cooldownNumberText.text = !available ? "잠김 / 미구현" : remaining > 0 ? remaining.ToString("0.0") : skill.maximumCharges > 1 ? timing.charges + "/" + skill.maximumCharges : "";
                int chain = skill?.comboChain ?? 0; float combo = chain == 1 ? state.comboTime1 : chain == 2 ? state.comboTime2 : 0;
                if (slot.stageBadgeText != null) slot.stageBadgeText.text = chain == 0 ? "" : (chain == 1 ? state.combo1 : state.combo2) + "단";
                if (slot.comboTimerBarRoot != null) slot.comboTimerBarRoot.SetActive(combo > 0);
                if (slot.comboTimerFillImage != null) slot.comboTimerFillImage.fillAmount = skill != null && skill.comboWindow > 0 ? combo / skill.comboWindow : 0;
                if (slot.comboTimerText != null) slot.comboTimerText.text = combo.ToString("0.0");
            }
            if (gcdSummaryText != null) gcdSummaryText.text = actor.Definition.name + " · 공유 GCD " + state.gcd.ToString("0.0") + "초";
            bool casting = state.castSkillId.Length > 0;
            if (playerCastBarRoot != null) playerCastBarRoot.SetActive(casting);
            if (casting)
            {
                if (playerCastText != null) playerCastText.text = actor.Definition.Skill(state.castSkillId).name + " · " + state.castRemaining.ToString("0.0") + "초";
                if (playerCastFillImage != null) playerCastFillImage.fillAmount = state.castTotal > 0 ? 1 - state.castRemaining / state.castTotal : 0;
            }
            return true;
        }

        private void UpdatePlayerCastBar(PlayerRole role)
        {
            if (playerCastBarRoot == null)
            {
                return;
            }

            bool showCastBar = role == PlayerRole.Archmage_Caster ||
                               combatController.IsCastingSpell ||
                               combatController.HasInterruptAlert ||
                               combatController.IsTriplecastActive;

            if (playerCastBarRoot.activeSelf != showCastBar)
            {
                playerCastBarRoot.SetActive(showCastBar);
            }

            if (!showCastBar)
            {
                return;
            }

            if (combatController.IsCastingSpell)
            {
                if (playerCastFillImage != null)
                {
                    playerCastFillImage.fillAmount = combatController.CastProgress;
                    playerCastFillImage.color = new Color(1.0f, 0.45f, 0.10f, 1f);
                }
                if (playerCastText != null)
                {
                    playerCastText.text =
                        $"시전 중: {combatController.CastingSpellName}  ({combatController.CastProgress * 100f:F0}% | 남은 {combatController.CastRemaining:F1}s) — [이동 시 취소!]";
                    playerCastText.color = Color.white;
                }
            }
            else if (combatController.HasInterruptAlert)
            {
                if (playerCastFillImage != null)
                {
                    playerCastFillImage.fillAmount = 1f;
                    playerCastFillImage.color = new Color(0.85f, 0.14f, 0.16f, 0.95f);
                }
                if (playerCastText != null)
                {
                    playerCastText.text = combatController.InterruptAlertMessage;
                    playerCastText.color = new Color(1f, 0.92f, 0.35f, 1f);
                }
            }
            else if (combatController.IsTriplecastActive)
            {
                if (playerCastFillImage != null)
                {
                    playerCastFillImage.fillAmount = Mathf.Clamp01(combatController.TriplecastRemaining / 6.0f);
                    playerCastFillImage.color = new Color(0.95f, 0.72f, 0.12f, 0.95f);
                }
                if (playerCastText != null)
                {
                    playerCastText.text =
                        $"★ [삼연속 마법] 활성 중 ({combatController.TriplecastRemaining:F1}s) — [1]번 화염 폭풍 콤보 캐스팅 0초 즉발 무빙샷!! ★";
                    playerCastText.color = new Color(1f, 0.98f, 0.75f, 1f);
                }
            }
            else
            {
                int stg = combatController.Combo1Stage;
                string nextFire = PlayerCombatController.GetRoleSkill1StageName(PlayerRole.Archmage_Caster, stg);
                float nextCastSec = stg == 1 ? 1.2f : (stg == 2 ? 1.5f : 2.0f);
                if (playerCastFillImage != null)
                {
                    playerCastFillImage.fillAmount = 0f;
                }
                if (playerCastText != null)
                {
                    playerCastText.text =
                        $"[캐스터 캐스팅 게이지 대기]  [1] {stg}단 {nextFire} (시전 {nextCastSec:F1}s - 제자리 발사)  |  [2] 냉기 무빙샷 (즉발)";
                    playerCastText.color = new Color(1.0f, 0.82f, 0.55f, 0.95f);
                }
            }
        }

        // ---------------------------------------------------------------------
        // 슬롯 [1]: 직군별 주력 3단 진화 콤보 (GCD)
        // ---------------------------------------------------------------------
        private void UpdateSlot1(HotbarSlotView slot, PlayerRole role)
        {
            if (slot == null) return;

            int stage = combatController.Combo1Stage;
            string skillName = PlayerCombatController.GetRoleSkill1StageName(role, stage);

            switch (role)
            {
                case PlayerRole.Guardian_Tank:
                {
                    string desc = stage switch
                    {
                        1 => "데미지 100 (어그로)",
                        2 => "180 + 어그로 대폭 획득",
                        3 => "300 + 내 체력 15% 자힐",
                        _ => "데미지 100"
                    };
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = stage == 3 ? "★ 3단 자힐 강타 ★" : $"{stage}단계 (방패 강타)";
                        slot.stageBadgeText.color = stage == 3 ? new Color(0.4f, 0.95f, 1f, 1f) : new Color(0.75f, 0.9f, 1f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = $"<b>{skillName}</b>\n<size=13>{desc}</size>";
                    }
                    if (slot.iconBackgroundImage != null)
                    {
                        slot.iconBackgroundImage.color = stage switch
                        {
                            1 => new Color(0.12f, 0.26f, 0.48f, 0.96f),
                            2 => new Color(0.15f, 0.38f, 0.65f, 0.98f),
                            3 => new Color(0.20f, 0.55f, 0.78f, 1.00f),
                            _ => new Color(0.12f, 0.26f, 0.48f, 0.96f)
                        };
                    }
                    break;
                }

                case PlayerRole.Oracle_Healer:
                {
                    int dmg = stage == 1 ? 120 : (stage == 2 ? 200 : 350);
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = stage == 3 ? "★ 3단 천벌 폭딜 ★" : $"{stage}단계 (심판의 빛)";
                        slot.stageBadgeText.color = stage == 3 ? new Color(0.6f, 1f, 0.4f, 1f) : new Color(0.8f, 1f, 0.85f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = $"<b>{skillName}</b>\n<size=13>원거리 마법 딜 {dmg}</size>";
                    }
                    if (slot.iconBackgroundImage != null)
                    {
                        slot.iconBackgroundImage.color = stage switch
                        {
                            1 => new Color(0.12f, 0.36f, 0.22f, 0.96f),
                            2 => new Color(0.16f, 0.50f, 0.28f, 0.98f),
                            3 => new Color(0.28f, 0.62f, 0.18f, 1.00f),
                            _ => new Color(0.12f, 0.36f, 0.22f, 0.96f)
                        };
                    }
                    break;
                }

                case PlayerRole.Archmage_Caster:
                {
                    int dmg = stage == 1 ? 200 : (stage == 2 ? 380 : 750);
                    float castSec = stage == 1 ? 1.2f : (stage == 2 ? 1.5f : 2.0f);
                    bool instant = combatController.IsTriplecastActive;
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = instant
                            ? "★ 삼연속 즉발! ★"
                            : (stage == 3 ? "★ 3단 초고화력 ★" : $"{stage}단계 (화염 캐스팅)");
                        slot.stageBadgeText.color = (stage == 3 || instant)
                            ? new Color(1f, 0.9f, 0.25f, 1f)
                            : new Color(1f, 0.75f, 0.55f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        string castLabel = instant ? "즉발 무빙샷!" : $"캐스팅 {castSec:F1}s";
                        slot.skillNameText.text = $"<b>{skillName}</b>\n<size=13>{castLabel} / 딜 {dmg}</size>";
                    }
                    if (slot.iconBackgroundImage != null)
                    {
                        slot.iconBackgroundImage.color = stage switch
                        {
                            1 => new Color(0.45f, 0.18f, 0.10f, 0.96f),
                            2 => new Color(0.62f, 0.22f, 0.10f, 0.98f),
                            3 => new Color(0.78f, 0.35f, 0.08f, 1.00f),
                            _ => new Color(0.45f, 0.18f, 0.10f, 0.96f)
                        };
                    }
                    break;
                }

                case PlayerRole.Blade_MeleeDPS:
                default:
                {
                    PlayerCombatController.GetSkill1StageDamages(stage, out int normalDmg, out int critDmg);
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = stage == 3 ? "★ 3단 최종 오의 ★" : $"{stage}단계 (측면)";
                        slot.stageBadgeText.color = stage == 3
                            ? new Color(1f, 0.9f, 0.25f, 1f)
                            : (stage == 2 ? new Color(0.55f, 1f, 0.45f, 1f) : new Color(0.8f, 0.9f, 0.85f, 1f));
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = $"<b>{skillName}</b>\n<size=13>기본 {normalDmg} / 측면 {critDmg}</size>";
                    }
                    if (slot.iconBackgroundImage != null)
                    {
                        slot.iconBackgroundImage.color = stage switch
                        {
                            1 => new Color(0.14f, 0.34f, 0.22f, 0.96f),
                            2 => new Color(0.18f, 0.52f, 0.24f, 0.98f),
                            3 => new Color(0.58f, 0.45f, 0.10f, 1.00f),
                            _ => new Color(0.14f, 0.34f, 0.22f, 0.96f)
                        };
                    }
                    break;
                }
            }

            UpdateCooldownVisual(slot, combatController.GcdRemaining, combatController.GcdTotal);
            UpdateComboHighlightAndTimerBar(
                slot,
                stage,
                combatController.Combo1TimerRemaining,
                combatController.comboExpireDuration
            );
        }

        // ---------------------------------------------------------------------
        // 슬롯 [2]: 직군별 보조 3단 콤보 또는 힐러 치유/보호막 (GCD)
        // ---------------------------------------------------------------------
        private void UpdateSlot2(HotbarSlotView slot, PlayerRole role)
        {
            if (slot == null) return;

            int stage = combatController.Combo2Stage;
            string skillName = PlayerCombatController.GetRoleSkill2StageName(role, stage);

            switch (role)
            {
                case PlayerRole.Guardian_Tank:
                {
                    string desc = stage switch
                    {
                        1 => "원거리 투창 110",
                        2 => "관통 170 + 주차 유도",
                        3 => "260 + 성광 보호막 200",
                        _ => "원거리 투창 110"
                    };
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = stage == 3 ? "★ 3단 방패 투척 ★" : $"{stage}단계 (원거리 투창)";
                        slot.stageBadgeText.color = new Color(0.55f, 0.9f, 1f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = $"<b>{skillName}</b>\n<size=13>{desc}</size>";
                    }
                    if (slot.iconBackgroundImage != null)
                    {
                        slot.iconBackgroundImage.color = new Color(0.14f, 0.30f, 0.52f, 0.96f);
                    }
                    UpdateCooldownVisual(slot, combatController.GcdRemaining, combatController.GcdTotal);
                    UpdateComboHighlightAndTimerBar(slot, stage, combatController.Combo2TimerRemaining, combatController.comboExpireDuration);
                    break;
                }

                case PlayerRole.Oracle_Healer:
                {
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = "스마트 힐 & 보호막";
                        slot.stageBadgeText.color = new Color(0.45f, 1.0f, 0.65f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = "<b>구원의 치유&보호막</b>\n<size=13>최저HP +40% / 보호막 20%</size>";
                    }
                    if (slot.iconBackgroundImage != null)
                    {
                        slot.iconBackgroundImage.color = new Color(0.14f, 0.45f, 0.30f, 0.96f);
                    }
                    UpdateCooldownVisual(slot, combatController.GcdRemaining, combatController.GcdTotal);
                    UpdateComboHighlightAndTimerBar(slot, 1, 0f, 8f);
                    break;
                }

                case PlayerRole.Archmage_Caster:
                {
                    int dmg = stage == 1 ? 100 : (stage == 2 ? 150 : 220);
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = stage == 3 ? "★ 3단 절대영도 ★" : $"{stage}단계 (무빙 즉발)";
                        slot.stageBadgeText.color = new Color(0.5f, 0.92f, 1f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = $"<b>{skillName}</b>\n<size=13>무빙 즉발샷 / 딜 {dmg}</size>";
                    }
                    if (slot.iconBackgroundImage != null)
                    {
                        slot.iconBackgroundImage.color = new Color(0.12f, 0.32f, 0.50f, 0.96f);
                    }
                    UpdateCooldownVisual(slot, combatController.GcdRemaining, combatController.GcdTotal);
                    UpdateComboHighlightAndTimerBar(slot, stage, combatController.Combo2TimerRemaining, combatController.comboExpireDuration);
                    break;
                }

                case PlayerRole.Blade_MeleeDPS:
                default:
                {
                    PlayerCombatController.GetSkill2StageDamages(stage, out int normalDmg, out int critDmg);
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = stage == 3 ? "★ 3단 최종 오의 ★" : $"{stage}단계 (후면)";
                        slot.stageBadgeText.color = stage == 3
                            ? new Color(1f, 0.85f, 0.25f, 1f)
                            : (stage == 2 ? new Color(0.45f, 0.9f, 1f, 1f) : new Color(0.8f, 0.88f, 0.95f, 1f));
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = $"<b>{skillName}</b>\n<size=13>기본 {normalDmg} / 후면 {critDmg}</size>";
                    }
                    if (slot.iconBackgroundImage != null)
                    {
                        slot.iconBackgroundImage.color = stage switch
                        {
                            1 => new Color(0.14f, 0.25f, 0.42f, 0.96f),
                            2 => new Color(0.32f, 0.18f, 0.55f, 0.98f),
                            3 => new Color(0.62f, 0.14f, 0.32f, 1.00f),
                            _ => new Color(0.14f, 0.25f, 0.42f, 0.96f)
                        };
                    }
                    UpdateCooldownVisual(slot, combatController.GcdRemaining, combatController.GcdTotal);
                    UpdateComboHighlightAndTimerBar(slot, stage, combatController.Combo2TimerRemaining, combatController.comboExpireDuration);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------------
        // 슬롯 [3]: 직군별 oGCD 생존기 / 성역
        // ---------------------------------------------------------------------
        private void UpdateSlot3(HotbarSlotView slot, PlayerRole role)
        {
            if (slot == null) return;

            switch (role)
            {
                case PlayerRole.Guardian_Tank:
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = "oGCD 탱버 방어 (12s)";
                        slot.stageBadgeText.color = new Color(0.45f, 0.9f, 1f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = "<b>철벽 방어</b>\n<size=13>4초간 피해 60% 감소</size>";
                    }
                    break;

                case PlayerRole.Oracle_Healer:
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = "oGCD 광역 힐 (15s)";
                        slot.stageBadgeText.color = new Color(0.45f, 1.0f, 0.65f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = "<b>치유의 성역</b>\n<size=13>6초간 초록 광역 힐장판</size>";
                    }
                    break;

                case PlayerRole.Archmage_Caster:
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = "oGCD 배리어 (15s)";
                        slot.stageBadgeText.color = new Color(0.80f, 0.55f, 1.0f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = "<b>마나 배리어</b>\n<size=13>4초 70%감쇄+끊김면역</size>";
                    }
                    break;

                case PlayerRole.Blade_MeleeDPS:
                default:
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = "oGCD 생존기 (15s)";
                        slot.stageBadgeText.color = new Color(0.55f, 0.85f, 1f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = "<b>개인 생존기</b>\n<size=13>3초간 피해 80% 감소</size>";
                    }
                    break;
            }

            UpdateCooldownVisual(slot, combatController.Skill3CooldownRemaining, combatController.Skill3CooldownMax);

            if (slot.borderHighlightImage != null)
            {
                bool ready = combatController.Skill3CooldownRemaining <= 0.001f;
                slot.borderHighlightImage.color = ready
                    ? new Color(0.28f, 0.65f, 1f, 0.85f)
                    : new Color(0.22f, 0.24f, 0.30f, 0.8f);
            }

            if (slot.comboTimerBarRoot != null && slot.comboTimerBarRoot.activeSelf)
            {
                slot.comboTimerBarRoot.SetActive(false);
            }
        }

        // ---------------------------------------------------------------------
        // 슬롯 [4]: 직군별 oGCD 핵심 유틸/폭딜 어빌리티
        // ---------------------------------------------------------------------
        private void UpdateSlot4(HotbarSlotView slot, PlayerRole role)
        {
            if (slot == null) return;

            switch (role)
            {
                case PlayerRole.Guardian_Tank:
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = "oGCD 어그로/줄 (15s)";
                        slot.stageBadgeText.color = new Color(0.35f, 0.95f, 1f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = "<b>도발 및 줄 강탈</b>\n<size=13>어그로 고정+줄 강탈</size>";
                    }
                    break;

                case PlayerRole.Oracle_Healer:
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = $"oGCD 구출 ({combatController.Skill4CooldownMax:F0}s)";
                        slot.stageBadgeText.color = new Color(0.35f, 0.98f, 1f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = "<b>구출 (Rescue)</b>\n<size=13>위험아군 견인+3초무적</size>";
                    }
                    break;

                case PlayerRole.Archmage_Caster:
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = "oGCD 흑마 핵심 (25s)";
                        slot.stageBadgeText.color = new Color(1.0f, 0.78f, 0.25f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = "<b>삼연속 마법</b>\n<size=13>6초간 [1]화염 즉발샷!</size>";
                    }
                    break;

                case PlayerRole.Blade_MeleeDPS:
                default:
                    if (slot.stageBadgeText != null)
                    {
                        slot.stageBadgeText.text = "oGCD 끼워넣기 (8s)";
                        slot.stageBadgeText.color = new Color(0.4f, 1f, 0.9f, 1f);
                    }
                    if (slot.skillNameText != null)
                    {
                        slot.skillNameText.text = $"<b>질풍 찌르기</b>\n<size=13>즉발 추가 피해 {combatController.skill4GaleThrustDamage}</size>";
                    }
                    break;
            }

            UpdateCooldownVisual(slot, combatController.Skill4CooldownRemaining, combatController.Skill4CooldownMax);

            if (slot.borderHighlightImage != null)
            {
                bool ready = combatController.Skill4CooldownRemaining <= 0.001f;
                if (ready && combatController.IsOnGcd)
                {
                    float pulse = 0.65f + Mathf.PingPong(Time.time * 6f, 0.35f);
                    slot.borderHighlightImage.color = new Color(0.25f, 1f, 0.9f, pulse);
                }
                else
                {
                    slot.borderHighlightImage.color = ready
                        ? new Color(0.25f, 0.85f, 0.78f, 0.85f)
                        : new Color(0.22f, 0.24f, 0.30f, 0.8f);
                }
            }

            if (slot.comboTimerBarRoot != null && slot.comboTimerBarRoot.activeSelf)
            {
                slot.comboTimerBarRoot.SetActive(false);
            }
        }

        private static void UpdateCooldownVisual(HotbarSlotView slot, float remaining, float total)
        {
            bool onCooldown = remaining > 0.01f;

            if (slot.cooldownOverlayImage != null)
            {
                slot.cooldownOverlayImage.gameObject.SetActive(onCooldown);
                if (onCooldown)
                {
                    slot.cooldownOverlayImage.fillAmount = total > 0.01f ? Mathf.Clamp01(remaining / total) : 0f;
                }
            }

            if (slot.cooldownNumberText != null)
            {
                slot.cooldownNumberText.gameObject.SetActive(onCooldown);
                if (onCooldown)
                {
                    slot.cooldownNumberText.text = $"{remaining:F1}";
                }
            }
        }

        private static void UpdateComboHighlightAndTimerBar(
            HotbarSlotView slot,
            int stage,
            float comboRemaining,
            float comboTotal)
        {
            bool isComboActive = stage >= 2 && comboRemaining > 0.01f;

            if (slot.borderHighlightImage != null)
            {
                if (isComboActive)
                {
                    float pulse = Mathf.PingPong(Time.time * 7.5f, 1f);
                    Color baseCol = stage == 3
                        ? new Color(1.0f, 0.48f, 0.08f, 1f)
                        : new Color(1.0f, 0.90f, 0.15f, 1f);
                    Color brightCol = new Color(1.0f, 1.0f, 0.65f, 1f);
                    slot.borderHighlightImage.color = Color.Lerp(baseCol, brightCol, pulse);
                }
                else
                {
                    slot.borderHighlightImage.color = new Color(0.28f, 0.32f, 0.40f, 0.85f);
                }
            }

            if (slot.comboTimerBarRoot != null)
            {
                if (slot.comboTimerBarRoot.activeSelf != isComboActive)
                {
                    slot.comboTimerBarRoot.SetActive(isComboActive);
                }

                if (isComboActive)
                {
                    if (slot.comboTimerFillImage != null)
                    {
                        slot.comboTimerFillImage.fillAmount = comboTotal > 0.01f
                            ? Mathf.Clamp01(comboRemaining / comboTotal)
                            : 0f;
                        slot.comboTimerFillImage.color = stage == 3
                            ? new Color(1.0f, 0.55f, 0.12f, 1f)
                            : new Color(1.0f, 0.88f, 0.20f, 1f);
                    }

                    if (slot.comboTimerText != null)
                    {
                        slot.comboTimerText.text = $"콤보 {comboRemaining:F1}s";
                    }
                }
            }
        }
    }
}
