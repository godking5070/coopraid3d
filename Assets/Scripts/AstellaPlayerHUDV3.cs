using System;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>Actual job ultimate artwork and readiness for the authored player HUD v3. Does not submit actions.</summary>
    [DefaultExecutionOrder(11600)]
    public sealed class AstellaPlayerHUDV3 : MonoBehaviour
    {
        [Serializable]
        public sealed class JobUltimateIcon
        {
            public string jobId;
            public Sprite icon;
        }

        public AstellaOrnateHUD hud;
        [Tooltip("Owned by this presenter only. Leave the Ultimate SlotBinding.artwork unset to avoid palette processing.")]
        public Image ultimateArtwork;
        public GameObject readyGlow, disabledOverlay;
        public JobUltimateIcon[] jobUltimateIcons = Array.Empty<JobUltimateIcon>();
        public bool compactUltimateValue = true;
        public bool IsReady { get; private set; }
        public string ReadinessReason { get; private set; } = "준비 중";

        private CharacterActor _actor;
        private NineKeyDamageKit _damage;
        private CharacterKit _kit;
        private float _nextResolve;

        private void Awake() { if (hud == null) hud = GetComponentInParent<AstellaOrnateHUD>(); }
        private void OnEnable() { _nextResolve = 0; }
        private void OnDisable()
        {
            IsReady = false;
            SetVisible(readyGlow, false);
        }

        private void LateUpdate()
        {
            ResolveActor();
            var definition = _actor?.Definition;
            var icon = definition != null ? Array.Find(jobUltimateIcons ?? Array.Empty<JobUltimateIcon>(), entry => entry != null && entry.jobId == definition.id) : null;
            if (ultimateArtwork != null)
            {
                Sprite sprite = icon?.icon;
                if (ultimateArtwork.sprite != sprite) ultimateArtwork.sprite = sprite;
                if (ultimateArtwork.enabled != (sprite != null)) ultimateArtwork.enabled = sprite != null;
            }
            IsReady = Ready(UltimateBinding(), out string reason);
            ReadinessReason = reason;
            SetVisible(readyGlow, IsReady);
            SetVisible(disabledOverlay, !IsReady);
            CompactUltimateValue();
        }

        private void CompactUltimateValue()
        {
            if (!compactUltimateValue || hud == null || hud.secondaryResourceText == null || _actor?.Definition?.resources == null) return;
            var resource = Array.Find(_actor.Definition.resources, entry => entry != null && entry.id != null && entry.id.EndsWith(".ultimate", StringComparison.Ordinal));
            if (resource == null) return;
            float maximum = _actor.ResourceMaximum(resource.id), current = _actor.Resource(resource.id);
            if (maximum <= 0 || !CharacterDefinitionValidation.Finite(maximum) || !CharacterDefinitionValidation.Finite(current)) return;
            string value = current.ToString("0.#") + " / " + maximum.ToString("0.#");
            if (hud.secondaryResourceText.text != value) hud.secondaryResourceText.text = value;
        }

        private void ResolveActor()
        {
            if (Time.unscaledTime < _nextResolve) return;
            _nextResolve = Time.unscaledTime + .25f;
            CharacterActor actor = null;
            if (RaidNetworkSession.Multiplayer)
            {
                if (RaidNetworkSession.Instance != null)
                    foreach (var avatar in RaidNetworkSession.Instance.Avatars.Values)
                        if (avatar != null && avatar.IsLocal) { actor = avatar.GetComponent<CharacterActor>(); break; }
            }
            else if (hud != null && hud.hotbarSource != null && hud.hotbarSource.combatController != null)
                actor = hud.hotbarSource.combatController.GetComponent<CharacterActor>();
            _actor = actor;
            _damage = actor != null ? actor.GetComponent<NineKeyDamageKit>() : null;
            _kit = actor != null ? actor.GetComponent<CharacterKit>() : null;
        }

        private AstellaOrnateHUD.SlotBinding UltimateBinding()
        {
            foreach (var binding in hud != null && hud.slots != null ? hud.slots : Array.Empty<AstellaOrnateHUD.SlotBinding>())
                if (binding != null && !binding.sharedResurrection && binding.slot == CharacterSlot.Ultimate) return binding;
            return null;
        }

        private bool Ready(AstellaOrnateHUD.SlotBinding binding, out string reason)
        {
            reason = "준비 중";
            if (binding?.button == null || !binding.button.IsActive() || !binding.button.gameObject.activeInHierarchy) return false;
            if (_actor == null || _actor.Definition == null) return false;
            if (LocalHUDLayoutController.IsEditing || LocalGameplayInput.Blocked || RaidNetworkSession.Instance?.MenuOpen == true || CombatMetricsUI.ResultsOpen)
            { reason = "조작 제한"; return false; }
            if (!RaidCombatAuthority.Local(_actor) || !CombatRange.LivingPartyMember(_actor.transform))
            { reason = "사용 불가"; return false; }
            if (!_actor.Available(CharacterSlot.Ultimate, out reason)) return false;
            if (!binding.button.IsInteractable())
            { reason = "사용 조건 미충족"; return false; }
            var skill = _actor.Selected(CharacterSlot.Ultimate);
            var snapshot = _actor.Snapshot;
            if (skill == null || !skill.implemented || snapshot == null)
            { reason = "잠김"; return false; }
            var timing = Array.Find(snapshot.skills ?? Array.Empty<JobSkillState>(), state => state != null && state.id == skill.id);
            if (timing == null || timing.charges <= 0 || skill.usesGcd && snapshot.gcd > .01f)
            { reason = "재사용 대기 중"; return false; }
            if (binding.cooldownShade != null && binding.cooldownShade.gameObject.activeSelf && binding.cooldownShade.fillAmount > .001f)
            { reason = "재사용 대기 중"; return false; }

            float scale = _damage?.Active == true ? _damage.CostScale(skill) : _kit?.Active == true ? _kit.CostScale(skill) : 1;
            foreach (var cost in skill.costs ?? Array.Empty<ResourceCost>())
            {
                if (cost == null) { reason = "자원 정보 없음"; return false; }
                float required = cost.amount * scale;
                if (required <= 0) continue;
                float maximum = _actor.ResourceMaximum(cost.resourceId), current = _actor.Resource(cost.resourceId);
                if (!CharacterDefinitionValidation.Finite(required) || !CharacterDefinitionValidation.Finite(maximum) ||
                    !CharacterDefinitionValidation.Finite(current) || maximum + .001f < required || current + .001f < required)
                { reason = "필살기 자원 부족"; return false; }
            }
            reason = "사용 가능";
            return true;
        }

        private static void SetVisible(GameObject target, bool visible)
        { if (target != null && target.activeSelf != visible) target.SetActive(visible); }
    }
}
