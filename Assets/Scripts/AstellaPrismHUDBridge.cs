using System;
using System.Globalization;
using Astella.PrismV4;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>
    /// Mirrors existing presentation data into a separate Prism view. Source presenters
    /// retain all resource/readiness/roster policies and every existing action listener.
    /// </summary>
    [DefaultExecutionOrder(13000)]
    public sealed class AstellaPrismHUDBridge : MonoBehaviour
    {
        [Serializable]
        public sealed class SlotMirror
        {
            public int sourceIndex;
            public PrismV4Bindings view;
        }

        [Serializable]
        public sealed class PartyMirror
        {
            public int sourceIndex;
            public PrismV4Bindings view;
            public PrismV4Gauge shieldFill;
            public GameObject aggroOverlay;
            [NonSerialized] internal PrismV4Gauge cachedHealthFill;
            [NonSerialized] internal Sprite normalHealthSprite;
        }

        public AstellaOrnateHUD hud;
        public AstellaClassJobHUD jobSource;
        public AstellaClassUltimateHUD ultimateSource;
        public AstellaPlayerHUDV3 readinessSource;
        public AstellaPartyHUDV4 partySource;
        // Separate from this bridge so all authored controls can be hidden on menus.
        public GameObject presentationRoot;
        public PrismV4Bindings playerView, bossView, centerView;
        public SlotMirror[] slots = Array.Empty<SlotMirror>();
        public PartyMirror[] partyRows = Array.Empty<PartyMirror>();

        private void LateUpdate()
        {
            SetVisible(presentationRoot, PresentationAllowed);
            MirrorPlayer();
            MirrorBoss();
            MirrorCenter();
            foreach (var slot in slots ?? Array.Empty<SlotMirror>()) MirrorSlot(slot);
            foreach (var row in partyRows ?? Array.Empty<PartyMirror>()) MirrorParty(row);
        }

        private void OnDisable()
        {
            SetVisible(presentationRoot, false);
            SetVisible(centerView != null ? centerView.readyGlow : null, false);
            foreach (var row in partyRows ?? Array.Empty<PartyMirror>())
                if (row?.cachedHealthFill != null && row.normalHealthSprite != null)
                    row.cachedHealthFill.sprite = row.normalHealthSprite;
        }

        private void MirrorPlayer()
        {
            if (playerView == null) return;
            MirrorVisibility(playerView, hud != null ? hud.playerRoot : null);
            SetGauge(playerView.healthFill, hud?.playerHpFill != null ? hud.playerHpFill.fillAmount : 0);
            CopyText(playerView.valueText, hud != null ? hud.playerHpText : null);
            bool available = jobSource != null && jobSource.isActiveAndEnabled && jobSource.HasResource &&
                Finite(jobSource.CurrentValue) && Finite(jobSource.Maximum) && jobSource.Maximum > 0 &&
                !string.IsNullOrEmpty(jobSource.ResourceId) && !jobSource.ResourceId.EndsWith(".ultimate", StringComparison.Ordinal);
            SetGauge(playerView.jobResourceFill, available ? jobSource.CurrentValue / jobSource.Maximum : 0);
            SetText(playerView.resourceText, available ? FormatPrimary(jobSource.CurrentValue, jobSource.Maximum, jobSource.ResourceIsInteger) : "—");
            SetText(playerView.nameText, available ? jobSource.ResourceLabel : "");
        }

        private void MirrorBoss()
        {
            if (bossView == null) return;
            MirrorVisibility(bossView, hud != null ? hud.bossRoot : null);
            SetGauge(bossView.bossFill, hud?.bossHpFill != null ? hud.bossHpFill.fillAmount : 0);
            CopyText(bossView.nameText, hud != null ? hud.bossTitleText : null);
            CopyText(bossView.valueText, hud != null ? hud.bossPercentText : null);
            CopyText(bossView.resourceText, hud != null ? hud.stageText : null);
            bool casting = hud?.castRoot != null && hud.castRoot.activeInHierarchy;
            SetGauge(bossView.castFill, hud?.castFill != null ? hud.castFill.fillAmount : 0);
            if (bossView.castFill != null)
            {
                var parent = bossView.castFill.transform.parent;
                var cast = parent != null && parent != bossView.transform ? parent.gameObject : bossView.castFill.gameObject;
                SetVisible(cast, casting);
            }
            SetVisible(bossView.castNameText != null ? bossView.castNameText.gameObject : null, casting);
            SetVisible(bossView.castTimeText != null ? bossView.castTimeText.gameObject : null, casting);
            CopyText(bossView.castNameText, hud != null ? hud.castNameText : null);
            CopyText(bossView.castTimeText, hud != null ? hud.castTimeText : null);
        }

        private void MirrorCenter()
        {
            if (centerView == null) return;
            bool visible = MirrorVisibility(centerView, hud != null ? hud.playerRoot : null);
            bool hasSource = ultimateSource != null && ultimateSource.isActiveAndEnabled;
            float ratio = hasSource ? ultimateSource.ChargeRatio : 0;
            SetGauge(centerView.ultimateChargeFill, ratio);
            bool ready = hasSource && ultimateSource.IsReady && readinessSource != null &&
                readinessSource.isActiveAndEnabled && readinessSource.IsReady;
            bool unavailable = !hasSource || ultimateSource.CurrentState == AstellaClassUltimateHUD.UltimateState.Unavailable ||
                Ratio(ratio) >= 1 && !ready;
            SetVisible(centerView.readyGlow, visible && ready);
            SetVisible(centerView.unavailableOverlay, visible && unavailable);
            Text percentage = null;
            foreach (var candidate in hasSource && ultimateSource.views != null ? ultimateSource.views : Array.Empty<AstellaClassUltimateHUD.ClassView>())
                if (candidate?.root != null && candidate.root.activeSelf && candidate.percentText != null)
                { percentage = candidate.percentText; break; }
            if (percentage != null) CopyText(centerView.valueText, percentage);
            else SetText(centerView.valueText, hasSource ? Mathf.FloorToInt(Ratio(ratio) * 100) + "%" : "—");
            AstellaOrnateHUD.SlotBinding binding = null;
            foreach (var candidate in hud != null && hud.slots != null ? hud.slots : Array.Empty<AstellaOrnateHUD.SlotBinding>())
                if (candidate != null && !candidate.sharedResurrection && candidate.slot == CharacterSlot.Ultimate)
                { binding = candidate; break; }
            CopyText(centerView.keyText, binding != null ? binding.keycap : null);
            CopyText(centerView.cooldownText, binding != null ? binding.cooldown : null);
        }

        private void MirrorSlot(SlotMirror mirror)
        {
            if (mirror?.view == null) return;
            var source = hud?.slots != null && mirror.sourceIndex >= 0 && mirror.sourceIndex < hud.slots.Length
                ? hud.slots[mirror.sourceIndex] : null;
            var view = mirror.view;
            CopyText(view.keyText, source != null ? source.keycap : null);
            CopyText(view.cooldownText, source != null ? source.cooldown : null);
            Sprite icon = source?.artwork != null ? source.artwork.sprite : null;
            if (icon == null) icon = source?.fallbackArt;
            CopyIcon(view.artwork, source != null ? source.artwork : null, icon);
            CopyText(view.nameText, source != null ? source.sourceLabel : null);
            SetVisible(view.nameText != null ? view.nameText.gameObject : null, icon == null);
            bool cooling = source?.cooldownShade != null && source.cooldownShade.gameObject.activeSelf;
            if (view.cooldownShade != null)
            {
                float ratio = cooling ? Ratio(source.cooldownShade.fillAmount) : 0;
                if (!Mathf.Approximately(view.cooldownShade.fillAmount, ratio)) view.cooldownShade.fillAmount = ratio;
                view.cooldownShade.enabled = cooling && ratio > 0;
            }
            bool disabled = source?.button == null || !source.button.IsInteractable();
            if (view.disabledOverlay != null) view.disabledOverlay.enabled = disabled;
            // The original button's authored skin (or its SpriteSwap fallback) owns
            // view.frame. No listeners, targeting, tooltip or input state change here.
        }

        private void MirrorParty(PartyMirror mirror)
        {
            if (mirror?.view == null) return;
            var original = Array.Find(partySource != null && partySource.rows != null ? partySource.rows : Array.Empty<AstellaPartyHUDV4.Row>(),
                row => row != null && row.sourceIndex == mirror.sourceIndex);
            var view = mirror.view;
            MirrorVisibility(view, original?.root != null ? original.root : partySource != null ? partySource.panelRoot : null);
            CopyText(view.nameText, original != null ? original.nameText : null);
            CopyText(view.valueText, original != null ? original.hpText : null);
            CopyText(view.keyText, original != null ? original.keyText : null);
            float hp = original?.gauge != null ? original.gauge.Value : 0;
            SetGauge(view.healthFill, hp);
            SetGauge(mirror.shieldFill, original?.shieldGauge != null ? original.shieldGauge.Value : 0);
            bool present = original?.target != null;
            bool dead = original?.dead != null && original.dead.activeSelf;
            if (view.healthFill != null)
            {
                if (mirror.cachedHealthFill != view.healthFill)
                { mirror.cachedHealthFill = view.healthFill; mirror.normalHealthSprite = view.healthFill.sprite; }
                Sprite fill = present && (dead || Ratio(hp) <= .35f) && view.criticalHealthSprite != null
                    ? view.criticalHealthSprite : mirror.normalHealthSprite;
                if (view.healthFill.sprite != fill) view.healthFill.sprite = fill;
            }
            CopyIcon(view.artwork, original != null ? original.jobIcon : null, original?.jobIcon != null ? original.jobIcon.sprite : null);
            if (view.selectedOverlay != null) view.selectedOverlay.enabled = original?.selected != null && original.selected.activeSelf;
            if (view.deadOverlay != null) view.deadOverlay.enabled = dead;
            SetVisible(mirror.aggroOverlay, original?.aggro != null && original.aggro.activeSelf);
        }

        private static bool MirrorVisibility(PrismV4Bindings view, GameObject sourceRoot)
        {
            bool visible = PresentationAllowed && sourceRoot != null && sourceRoot.activeInHierarchy;
            if (view != null && view.gameObject != sourceRoot) SetVisible(view.gameObject, visible);
            return visible;
        }

        private static bool PresentationAllowed
        {
            get
            {
                var session = RaidNetworkSession.Instance;
                return !CombatMetricsUI.ResultsOpen &&
                    (session == null || !session.MenuOpen &&
                        (session.Mode != RaidSessionMode.Menu || LocalHUDLayoutController.IsEditing));
            }
        }

        private static string FormatPrimary(float value, float maximum, bool integer)
            => value.ToString(integer ? "0" : "0.#", CultureInfo.InvariantCulture) + " / " + maximum.ToString("0.#", CultureInfo.InvariantCulture);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Ratio(float value) => Finite(value) ? Mathf.Clamp01(value) : 0;
        private static void SetGauge(PrismV4Gauge gauge, float value)
        {
            if (gauge == null) return;
            if (gauge.sourceFill != null) gauge.Bind(null);
            float ratio = Ratio(value);
            if (!Mathf.Approximately(gauge.previewValue, ratio)) gauge.SetValue(ratio);
        }
        private static void CopyIcon(Image target, Image source, Sprite fallback)
        {
            if (target == null) return;
            Sprite sprite = source != null && source.sprite != null ? source.sprite : fallback;
            if (target.sprite != sprite) target.sprite = sprite;
            if (target.enabled != (sprite != null)) target.enabled = sprite != null;
            if (source != null && target.material != source.material) target.material = source.material;
        }
        private static void CopyText(Text target, Text source) => SetText(target, source != null ? source.text : "");
        private static void SetText(Text target, string value) { if (target != null && target.text != value) target.text = value; }
        private static void SetVisible(GameObject target, bool visible) { if (target != null && target.activeSelf != visible) target.SetActive(visible); }
    }
}
