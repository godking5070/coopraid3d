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
    /// 화면 하단 중앙에 [1], [2], [3], [4] 4개의 정사각형 스킬 슬롯 UI를 표시하고
    /// GCD/oGCD 쿨타임 오버레이, 1단->2단->3단 진화 스킬명/색상, 콤보 반짝임 테두리,
    /// 슬롯 하단 8초 콤보 유지 시간 바를 실시간으로 갱신하는 컴포넌트 (Step 5).
    /// </summary>
    public class SkillHotbarUI : MonoBehaviour
    {
        [Header("References")]
        public PlayerCombatController combatController;
        public HotbarSlotView[] slots = new HotbarSlotView[4];
        public Text gcdSummaryText;

        private Sprite _whiteFillSprite;

        private void Start()
        {
            if (combatController == null)
            {
                combatController = FindFirstObjectByType<PlayerCombatController>();
            }

            EnsureFillSprites();
            BindSlotClickButtons();
        }

        private void EnsureFillSprites()
        {
            _whiteFillSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f)
            );

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

            UpdateSlot1_FlankCombo(slots[0]);
            UpdateSlot2_RearCombo(slots[1]);
            UpdateSlot3_DefensiveOGcd(slots[2]);
            UpdateSlot4_GaleThrustOGcd(slots[3]);

            if (gcdSummaryText != null)
            {
                gcdSummaryText.text =
                    $"현재 글로벌 쿨타임(GCD): <b>{combatController.currentGcdDuration:F1}초</b>  |  " +
                    "[1]·[2] 웨폰스킬(GCD 공유) 사용 후 글쿨이 도는 동안 [3]·[4] 어빌리티(oGCD)를 즉시 끼워넣기(Weaving)할 수 있습니다!";
            }
        }

        // ---------------------------------------------------------------------
        // 슬롯 [1]: 측면 3단 진화 웨폰스킬 (GCD)
        // ---------------------------------------------------------------------
        private void UpdateSlot1_FlankCombo(HotbarSlotView slot)
        {
            if (slot == null) return;

            int stage = combatController.Combo1Stage;
            string skillName = PlayerCombatController.GetSkill1StageName(stage);
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

            // GCD 쿨타임 오버레이 및 소수점 1자리 숫자
            UpdateCooldownVisual(slot, combatController.GcdRemaining, combatController.GcdTotal);

            // 2단/3단 업그레이드 시 파판14 스타일 반짝이는 노란색/주황색 테두리 하이라이트 & 하단 8초 콤보 유지 바
            UpdateComboHighlightAndTimerBar(
                slot,
                stage,
                combatController.Combo1TimerRemaining,
                combatController.comboExpireDuration
            );
        }

        // ---------------------------------------------------------------------
        // 슬롯 [2]: 후면 3단 진화 웨폰스킬 (GCD)
        // ---------------------------------------------------------------------
        private void UpdateSlot2_RearCombo(HotbarSlotView slot)
        {
            if (slot == null) return;

            int stage = combatController.Combo2Stage;
            string skillName = PlayerCombatController.GetSkill2StageName(stage);
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

            UpdateComboHighlightAndTimerBar(
                slot,
                stage,
                combatController.Combo2TimerRemaining,
                combatController.comboExpireDuration
            );
        }

        // ---------------------------------------------------------------------
        // 슬롯 [3]: 개인 생존기 (oGCD 어빌리티 - 쿨타임 15초)
        // ---------------------------------------------------------------------
        private void UpdateSlot3_DefensiveOGcd(HotbarSlotView slot)
        {
            if (slot == null) return;

            if (slot.stageBadgeText != null)
            {
                slot.stageBadgeText.text = "oGCD 생존기";
                slot.stageBadgeText.color = new Color(0.55f, 0.85f, 1f, 1f);
            }

            if (slot.skillNameText != null)
            {
                slot.skillNameText.text = "<b>개인 생존기</b>\n<size=13>3초간 피해 80% 감소</size>";
            }

            UpdateCooldownVisual(slot, combatController.Skill3CooldownRemaining, combatController.skill3DefensiveCooldown);

            // 글쿨(GCD) 도는 중에도 사용 가능하면 테두리를 은은한 하늘색으로 표시
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
        // 슬롯 [4]: 질풍 찌르기 (oGCD 끼워넣기 어빌리티 - 쿨타임 8초)
        // ---------------------------------------------------------------------
        private void UpdateSlot4_GaleThrustOGcd(HotbarSlotView slot)
        {
            if (slot == null) return;

            if (slot.stageBadgeText != null)
            {
                slot.stageBadgeText.text = "oGCD 끼워넣기";
                slot.stageBadgeText.color = new Color(0.4f, 1f, 0.9f, 1f);
            }

            if (slot.skillNameText != null)
            {
                slot.skillNameText.text = $"<b>질풍 찌르기</b>\n<size=13>즉발 추가 피해 {combatController.skill4GaleThrustDamage}</size>";
            }

            UpdateCooldownVisual(slot, combatController.Skill4CooldownRemaining, combatController.skill4GaleThrustCooldown);

            if (slot.borderHighlightImage != null)
            {
                bool ready = combatController.Skill4CooldownRemaining <= 0.001f;
                // 글쿨이 도는 중에 끼워넣기(Weaving) 가능할 때 밝게 강조!
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

            // 2단계, 3단계 진화 상태일 때 슬롯 테두리가 밝은 노란색/주황색으로 반짝거리게(Highlight) 강조
            if (slot.borderHighlightImage != null)
            {
                if (isComboActive)
                {
                    float pulse = Mathf.PingPong(Time.time * 7.5f, 1f);
                    Color baseCol = stage == 3
                        ? new Color(1.0f, 0.48f, 0.08f, 1f) // 3단계: 강렬한 주황색
                        : new Color(1.0f, 0.90f, 0.15f, 1f); // 2단계: 밝은 노란색
                    Color brightCol = new Color(1.0f, 1.0f, 0.65f, 1f);
                    slot.borderHighlightImage.color = Color.Lerp(baseCol, brightCol, pulse);
                }
                else
                {
                    slot.borderHighlightImage.color = new Color(0.28f, 0.32f, 0.40f, 0.85f);
                }
            }

            // 슬롯 아래 8초 콤보 유지 시간 바 표시
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
