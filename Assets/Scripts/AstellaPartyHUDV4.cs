using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>Authored party HUD adapter. The existing PartyListUI remains the roster/target-selection owner.</summary>
    [DefaultExecutionOrder(11000)]
    public sealed class AstellaPartyHUDV4 : MonoBehaviour
    {
        [Serializable] public sealed class JobIcon { public string jobId; public Sprite icon; }
        [Serializable] public sealed class StatusView
        {
            [Tooltip("Actual state key, e.g. shield, revival-protection, morning, stun, bleed, poison, burn or dead.")]
            public string key;
            public GameObject root;
            public Text remainingText;
        }
        [Serializable] public sealed class Row
        {
            [Range(0, 3)] public int sourceIndex;
            public GameObject root;
            public Button button;
            public Text nameText, hpText, keyText;
            public Image jobIcon;
            public Sprite fallbackJobIcon;
            public AstellaPointedGauge gauge, shieldGauge;
            public GameObject dead, selected, aggro;
            public CanvasGroup stateGroup;
            public StatusView[] statuses = Array.Empty<StatusView>();
            [NonSerialized] internal Transform target;
            [NonSerialized] internal PlayerController player;
            [NonSerialized] internal PartyBotAI bot;
            [NonSerialized] internal NetworkRaidAvatar avatar;
            [NonSerialized] internal CharacterActor actor;
            [NonSerialized] internal CombatStatusEffects effects;
            [NonSerialized] internal CommonPotion potion;
            [NonSerialized] internal Button boundButton;
            [NonSerialized] internal UnityAction listener;
            [NonSerialized] internal float nextStatus;
        }

        public PartyListUI source;
        [Tooltip("A child panel, not the GameObject containing this presenter, so menu visibility can recover.")]
        public GameObject panelRoot;
        public Text headerText;
        public string headerFormat = "파티 {0}";
        public Row[] rows = Array.Empty<Row>();
        public JobIcon[] jobIcons = Array.Empty<JobIcon>();
        public Sprite normalHpSprite, criticalHpSprite;
        [Range(1, 16)] public int maximumVisibleStatuses = 8;

        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private readonly HashSet<Transform> _members = new HashSet<Transform>();
        private float _nextResolve;
        private Text _legacyRevivalBudget;

        private void OnEnable() { _nextResolve = 0; }
        private void Start() { ResolveSource(); BindButtons(); }
        private void ResolveSource()
        {
            if (source != null || Time.unscaledTime < _nextResolve) return;
            _nextResolve = Time.unscaledTime + .5f;
            source = GetComponentInChildren<PartyListUI>(true) ?? FindFirstObjectByType<PartyListUI>();
        }

        private void LateUpdate()
        {
            ResolveSource();
            BindButtons();
            if (_legacyRevivalBudget == null && source?.panelRoot != null)
                _legacyRevivalBudget = source.panelRoot.Find("AttemptRevivalBudget")?.GetComponent<Text>();
            if (_legacyRevivalBudget != null && _legacyRevivalBudget.enabled) _legacyRevivalBudget.enabled = false;
            bool editing = LocalHUDLayoutController.IsEditing;
            bool visible = editing || RaidNetworkSession.Instance?.MenuOpen != true && !CombatMetricsUI.ResultsOpen;
            SetVisible(panelRoot, visible);
            if (!visible) return;
            _members.Clear();
            foreach (var row in rows ?? Array.Empty<Row>())
            {
                if (row == null) continue;
                var original = SourceRow(row.sourceIndex);
                Transform target = original?.boundTarget;
                CacheTarget(row, target);
                if (target != null) _members.Add(target);
                UpdateRow(row, original, editing);
            }
            if (headerText != null)
                SetText(headerText, (headerFormat ?? "파티 {0}").Replace("{0}", _members.Count.ToString()));
        }

        private PartyMemberRowUI SourceRow(int index) => source?.memberRows != null && index >= 0 && index < source.memberRows.Length ? source.memberRows[index] : null;
        private void CacheTarget(Row row, Transform target)
        {
            if (row.target == target) return;
            row.target = target;
            row.player = target != null ? target.GetComponent<PlayerController>() : null;
            row.bot = target != null ? target.GetComponent<PartyBotAI>() : null;
            row.avatar = target != null ? target.GetComponent<NetworkRaidAvatar>() : null;
            row.actor = target != null ? target.GetComponent<CharacterActor>() : null;
            row.effects = target != null ? target.GetComponent<CombatStatusEffects>() : null;
            row.potion = target != null ? target.GetComponent<CommonPotion>() : null;
            row.nextStatus = 0;
        }

        private void UpdateRow(Row row, PartyMemberRowUI original, bool editing)
        {
            bool present = row.target != null;
            float hp = 0, maximum = 0, shield = 0;
            bool dead = false;
            if (row.avatar?.State != null)
            {
                hp = row.avatar.State.hp; maximum = row.avatar.State.maxHp; shield = row.avatar.State.shield;
                dead = row.avatar.State.dead || hp <= 0;
            }
            else if (row.player != null)
            {
                hp = row.player.currentHp; maximum = row.player.maxHp; shield = row.player.currentShield;
                dead = row.player.IsDead;
            }
            else if (row.bot != null)
            {
                hp = row.bot.currentHp; maximum = row.bot.maxHp; shield = row.bot.currentShield;
                dead = row.bot.isDead || hp <= 0;
            }
            float ratio = maximum > 0 ? Mathf.Clamp01(hp / maximum) : original?.hpFillImage != null && present ? original.hpFillImage.fillAmount : 0;
            if (row.gauge != null) row.gauge.SetValue(ratio);
            if (row.gauge?.artwork != null)
            {
                Sprite fill = present && (dead || ratio <= .35f) ? criticalHpSprite : normalHpSprite;
                if (fill != null && row.gauge.artwork.sprite != fill) row.gauge.artwork.sprite = fill;
            }
            if (row.shieldGauge != null) row.shieldGauge.SetValue(maximum > 0 ? shield / maximum : 0);
            SetText(row.hpText, present ? Mathf.CeilToInt(ratio * 100) + "%" : "—");
            SetText(row.keyText, LocalInputBindings.Label("Party" + (row.sourceIndex + 1)));

            string name = row.avatar != null && !string.IsNullOrEmpty(row.avatar.DisplayName) ? row.avatar.DisplayName : row.actor?.Definition?.name;
            if (string.IsNullOrEmpty(name) && present && original?.memberNameText != null)
            {
                name = Tags.Replace(original.memberNameText.text ?? "", "").Trim();
                if (name.StartsWith("[", StringComparison.Ordinal) && name.IndexOf(']') >= 0) name = name.Substring(name.IndexOf(']') + 1).Trim();
                name = name.Replace("[어그로1]", "").Replace("(전투불능)", "").Replace("[부활 보호]", "").Trim();
            }
            SetText(row.nameText, present ? name ?? row.target.name : "접속 대기");
            string job = row.actor?.Definition?.id ?? "";
            var icon = Array.Find(jobIcons ?? Array.Empty<JobIcon>(), entry => entry != null && entry.jobId == job);
            if (icon == null && row.actor?.Definition?.legacyRole > 0)
            {
                string mapped = row.actor.Definition.legacyRole == 1 ? "job.aegis-paladin" : row.actor.Definition.legacyRole == 2 ? "job.sylvan-oracle" :
                    row.actor.Definition.legacyRole == 3 ? "job.nightblade" : "job.astromancer";
                icon = Array.Find(jobIcons ?? Array.Empty<JobIcon>(), entry => entry != null && entry.jobId == mapped);
            }
            if (row.jobIcon != null)
            {
                Sprite sprite = icon?.icon != null ? icon.icon : row.fallbackJobIcon;
                if (row.jobIcon.sprite != sprite) row.jobIcon.sprite = sprite;
                if (row.jobIcon.enabled != (sprite != null)) row.jobIcon.enabled = sprite != null;
            }
            SetVisible(row.dead, present && dead);
            SetVisible(row.selected, present && TargetingManager.Instance?.SupportTarget == row.target);
            SetVisible(row.aggro, present && !dead && source?.boss?.aggroTarget == row.target);
            if (row.stateGroup != null)
            {
                float alpha = !present ? .4f : dead ? .55f : 1;
                if (!Mathf.Approximately(row.stateGroup.alpha, alpha)) row.stateGroup.alpha = alpha;
            }
            if (row.button != null && row.button != original?.selectButton)
                row.button.interactable = present && !editing && !LocalGameplayInput.Blocked;
            if (Time.unscaledTime >= row.nextStatus)
            {
                row.nextStatus = Time.unscaledTime + .2f;
                UpdateStatuses(row, shield, dead);
            }
        }

        private void UpdateStatuses(Row row, float shield, bool dead)
        {
            if (row.target != null)
            {
                row.effects = row.target.GetComponent<CombatStatusEffects>();
                row.potion = row.target.GetComponent<CommonPotion>();
            }
            CombatDebuffSnapshot snapshot = null;
            bool needsDebuffs = false;
            foreach (var view in row.statuses ?? Array.Empty<StatusView>())
                if (view != null && IsDebuffKey(view.key)) { needsDebuffs = true; break; }
            // Read the status registry's own public snapshot, never private fields or invented sample effects.
            if (needsDebuffs && row.effects != null) snapshot = JsonUtility.FromJson<CombatDebuffSnapshot>(row.effects.Capture());
            float now = RaidCombatAuthority.ServerTime;
            int activeCount = 0;
            foreach (var view in row.statuses ?? Array.Empty<StatusView>())
            {
                if (view == null) continue;
                bool active = false; float remaining = 0;
                var effects = row.effects;
                switch (view.key)
                {
                    case "dead": active = dead; break;
                    case "shield": active = shield > .01f; break;
                    case "revival-protection": active = row.potion?.HasRevivalProtection == true; break;
                    case "invulnerable": active = effects?.CombatInvulnerable == true; remaining = active ? effects.InvulnerabilityEnd - now : 0; break;
                    case "morning": active = effects?.MorningActive == true; remaining = active ? effects.MorningEnd - now : 0; break;
                    case "knockback-immunity": remaining = effects?.KnockbackImmunity ?? 0; active = remaining > 0; break;
                    case "control-immunity": remaining = effects?.ControlImmunity ?? 0; active = remaining > 0; break;
                    case "flora-attack": active = effects != null && effects.ActiveFloraAttack > 0; remaining = active ? effects.FloraAttackEnd - now : 0; break;
                    case "personal-defense": active = row.player?.IsDefensiveBuffActive == true; remaining = row.player?.DefensiveRemainingTime ?? 0; break;
                    default:
                        foreach (var debuff in snapshot?.entries ?? Array.Empty<CombatDebuff>())
                            if (debuff != null && debuff.remaining > 0 && (debuff.tag == view.key || debuff.id == view.key ||
                                view.key == "stun" && debuff.stun || view.key == "root" && debuff.root))
                            { active = true; remaining = Mathf.Max(remaining, debuff.remaining); }
                        break;
                }
                active &= row.target != null && (!dead || view.key == "dead");
                active &= activeCount < maximumVisibleStatuses;
                if (active) activeCount++;
                SetVisible(view.root, active);
                SetText(view.remainingText, active && remaining > 0 ? Mathf.CeilToInt(remaining).ToString() : "");
            }
        }

        private static bool IsDebuffKey(string key) => key != "dead" && key != "shield" && key != "revival-protection" &&
            key != "invulnerable" && key != "morning" && key != "knockback-immunity" && key != "control-immunity" &&
            key != "flora-attack" && key != "personal-defense";
        private void BindButtons()
        {
            foreach (var row in rows ?? Array.Empty<Row>())
            {
                if (row == null || row.boundButton == row.button) continue;
                if (row.boundButton != null && row.listener != null) row.boundButton.onClick.RemoveListener(row.listener);
                row.boundButton = row.button;
                if (row.button == null || row.button == SourceRow(row.sourceIndex)?.selectButton) continue;
                var captured = row;
                row.listener = () => Select(captured);
                row.button.onClick.AddListener(row.listener);
            }
        }
        private void Select(Row row)
        {
            if (LocalHUDLayoutController.IsEditing || LocalGameplayInput.Blocked || row?.button == null || !row.button.interactable) return;
            var original = SourceRow(row.sourceIndex);
            if (original?.selectButton != null && original.selectButton != row.button) original.selectButton.onClick.Invoke();
            else if (original?.boundTarget != null) TargetingManager.Instance?.SetTarget(original.boundTarget);
            LocalGameplayInput.SuppressPointerThisFrame();
        }
        private void OnDestroy()
        {
            foreach (var row in rows ?? Array.Empty<Row>())
                if (row?.boundButton != null && row.listener != null) row.boundButton.onClick.RemoveListener(row.listener);
        }
        private static void SetVisible(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
        private static void SetText(Text target, string value) { if (target != null && target.text != value) target.text = value; }
    }
}
