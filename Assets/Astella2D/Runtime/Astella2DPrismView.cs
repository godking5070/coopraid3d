using System;
using System.Globalization;
using Astella.PrismV4;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Astella.TwoD
{
    public sealed class Astella2DPrismView : MonoBehaviour
    {
        public PrismV4Bindings player, boss, center;
        public PrismV4Bindings[] slots = Array.Empty<PrismV4Bindings>();
        public PrismV4Bindings[] party = Array.Empty<PrismV4Bindings>();
        public PrismV4Gauge[] partyShields = Array.Empty<PrismV4Gauge>();
        public GameObject[] partyAggro = Array.Empty<GameObject>();
        // One optional original skill sprite per authored slot, in display order.
        public Sprite[] slotIcons = Array.Empty<Sprite>();
        public GameObject tooltipRoot;
        public Text tooltipText;

        private static readonly int[] SkillBySlot = { 0, 1, -1, -1, 2, -1, -1, 4 };
        private static readonly string[] SkillNames = { "기본 타격", "공명 기술", "은빛 가호", "별의 공명", "회피 대시" };
        private static readonly string[] SkillKeys = { "J", "Q", "E", "R", "Space" };
        private Sprite[] _normalPartyHealth;
        private GameObject _castRoot;
        private bool _initialized;
        private float _dashDuration;

        public void Initialize(Action<int> useSkill, Action<int> selectParty)
        {
            if (_initialized) return;
            _initialized = true;
            Visible(tooltipRoot, false);
            if (tooltipRoot != null)
                foreach (var graphic in tooltipRoot.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            _normalPartyHealth = new Sprite[party.Length];
            for (int i = 0; i < party.Length; i++)
            {
                int member = i;
                var row = party[i];
                _normalPartyHealth[i] = row != null && row.healthFill != null ? row.healthFill.sprite : null;
                if (row != null && row.button != null)
                    row.button.onClick.AddListener(() => selectParty(member));
                if (i < partyShields.Length) Gauge(partyShields[i], 0);
                if (i < partyAggro.Length) Visible(partyAggro[i], false);
            }
            for (int i = 0; i < slots.Length; i++)
            {
                var view = slots[i];
                if (view == null) continue;
                int skill = i < SkillBySlot.Length ? SkillBySlot[i] : -1;
                if (skill >= 0 && view.button != null)
                {
                    view.button.onClick.AddListener(() => useSkill(skill));
                    BindTooltip(view.button, skill);
                }
                Sprite icon = skill >= 0 && i < slotIcons.Length ? slotIcons[i] : null;
                if (view.artwork != null)
                {
                    view.artwork.sprite = icon;
                    view.artwork.enabled = icon != null;
                }
                Text(view.nameText, skill >= 0 && icon == null ? SkillNames[skill] : "");
                Text(view.keyText, skill >= 0 ? SkillKeys[skill] : "");
            }
            if (center != null)
            {
                if (center.button != null)
                {
                    center.button.onClick.AddListener(() => useSkill(3));
                    BindTooltip(center.button, 3);
                }
                Text(center.keyText, SkillKeys[3]);
            }
            if (boss != null && boss.castFill != null && boss.castFill.transform.parent != boss.transform)
                _castRoot = boss.castFill.transform.parent.gameObject;
        }

        public void Show(bool value)
        {
            if (!value) Visible(tooltipRoot, false);
            Visible(gameObject, value);
        }

        private void OnDisable() => Visible(tooltipRoot, false);

        private void BindTooltip(Button button, int skill)
        {
            if (tooltipRoot == null || tooltipText == null) return;
            var trigger = button.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                Text(tooltipText, $"{SkillNames[skill]}  [{(skill == 0 ? "LMB / J" : SkillKeys[skill])}]");
                Visible(tooltipRoot, true);
            });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => Visible(tooltipRoot, false));
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        public void UpdatePlayer(float hp, float maxHp, float ultimate, float dashCooldown, float dashDuration)
        {
            _dashDuration = dashDuration;
            if (player != null)
            {
                Gauge(player.healthFill, Fraction(hp, maxHp));
                Text(player.valueText, Whole(hp) + " / " + Whole(maxHp));
                Gauge(player.jobResourceFill, 1 - Fraction(dashCooldown, dashDuration));
                Text(player.nameText, SkillNames[4]);
                Text(player.resourceText, Time(Mathf.Max(0, dashCooldown)));
            }
            if (center != null)
            {
                Gauge(center.ultimateChargeFill, Fraction(ultimate, 100));
                Text(center.valueText, Mathf.FloorToInt(Mathf.Clamp(ultimate, 0, 100)) + "%");
            }
        }

        public void UpdateBoss(float hp, float maxHp, int encounter, string name, string castName,
            float castRemaining, float castDuration)
        {
            if (boss == null) return;
            float hpRatio = Fraction(hp, maxHp);
            Gauge(boss.bossFill, hpRatio);
            Text(boss.nameText, name);
            Text(boss.valueText, (hpRatio * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            Text(boss.resourceText, $"원정 {encounter} / 3");
            bool casting = castRemaining > 0 && !string.IsNullOrEmpty(castName);
            Gauge(boss.castFill, casting ? Fraction(castRemaining, castDuration) : 0);
            Visible(_castRoot, casting);
            Text(boss.castNameText, casting ? castName : "");
            Text(boss.castTimeText, casting ? Time(castRemaining) : "");
        }

        public void UpdateParty(int index, string name, float hp, float maxHp, Sprite portrait, bool selected)
        {
            if (index < 0 || index >= party.Length || party[index] == null) return;
            var row = party[index];
            bool present = maxHp > 0;
            float hpRatio = Fraction(hp, maxHp);
            Gauge(row.healthFill, hpRatio);
            if (row.healthFill != null && _normalPartyHealth != null)
            {
                Sprite health = present && hpRatio <= .35f && row.criticalHealthSprite != null
                    ? row.criticalHealthSprite : _normalPartyHealth[index];
                if (row.healthFill.sprite != health) row.healthFill.sprite = health;
            }
            Text(row.nameText, present ? name : "");
            Text(row.valueText, present ? Whole(hp) : "");
            Text(row.keyText, "");
            if (row.artwork != null)
            {
                if (row.artwork.sprite != portrait) row.artwork.sprite = portrait;
                row.artwork.enabled = present && portrait != null;
            }
            if (row.selectedOverlay != null) row.selectedOverlay.enabled = present && selected;
            if (row.deadOverlay != null) row.deadOverlay.enabled = present && hp <= 0;
            if (row.button != null) row.button.interactable = present;
        }

        public void UpdateSkills(float[] cooldowns, float[] durations, float ultimate, bool canUse)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var view = slots[i];
                if (view == null) continue;
                int skill = i < SkillBySlot.Length ? SkillBySlot[i] : -1;
                float cooldown = skill >= 0 ? cooldowns[skill] : 0;
                bool available = skill >= 0 && canUse;
                if (view.button != null) view.button.interactable = available && cooldown <= 0;
                if (view.disabledOverlay != null) view.disabledOverlay.enabled = !available;
                if (view.cooldownShade != null)
                {
                    float duration = skill == 4 ? _dashDuration : skill >= 0 ? durations[skill] : 0;
                    view.cooldownShade.fillAmount = Fraction(cooldown, duration);
                    view.cooldownShade.enabled = cooldown > 0;
                }
                Text(view.cooldownText, cooldown > .05f ? Time(cooldown) : "");
            }
            if (center == null) return;
            bool ready = canUse && ultimate >= 100 && cooldowns[3] <= 0;
            if (center.button != null) center.button.interactable = ready;
            Visible(center.readyGlow, ready);
            Visible(center.unavailableOverlay, !canUse);
            Text(center.cooldownText, cooldowns[3] > .05f ? Time(cooldowns[3]) : "");
        }

        private static float Fraction(float value, float max) => max > 0 ? Mathf.Clamp01(value / max) : 0;
        private static string Whole(float value) => Mathf.CeilToInt(value).ToString(CultureInfo.InvariantCulture);
        private static string Time(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        private static void Gauge(PrismV4Gauge gauge, float value)
        {
            if (gauge == null) return;
            if (gauge.sourceFill != null) gauge.Bind(null);
            if (!Mathf.Approximately(gauge.previewValue, value)) gauge.SetValue(value);
        }

        private static void Text(UnityEngine.UI.Text label, string value)
        {
            if (label != null && label.text != value) label.text = value;
        }

        private static void Visible(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value) target.SetActive(value);
        }
    }
}
