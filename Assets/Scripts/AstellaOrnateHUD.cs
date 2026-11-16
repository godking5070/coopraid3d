using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>
    /// Opt-in data presenter for the authored constellation HUD. Graphics, transforms,
    /// fonts and button actions belong to the prefab; only live values and state change here.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class AstellaOrnateHUD : MonoBehaviour
    {
        [Serializable]
        public sealed class JobVisual
        {
            public string jobId;
            public Material skillMaterial;
            public Sprite identityEmblem;
            public Color identityColor = Color.white;
        }

        [Serializable]
        public sealed class SlotBinding
        {
            public Button button;
            public CharacterSlot slot;
            public bool sharedResurrection;
            public string bindingOverride = "";
            [Tooltip("Only for newly authored potion/dash buttons that have no existing presenter callback.")]
            public bool bindAction;
            [Tooltip("Original presenter labels remain connected, but may be visually hidden by the authored prefab.")]
            public Text sourceLabel, sourceStatus, sourceKey;
            public Text keycap, cooldown;
            public Image cooldownShade, artwork;
            public Sprite fallbackArt;
            public CanvasGroup stateGroup;
            [Range(0, 1)] public float unavailableAlpha = .4f;
            [NonSerialized] internal bool materialResolved;
            [NonSerialized] internal Image materialImage;
            [NonSerialized] internal Material appliedMaterial;
        }

        public SkillHotbarUI hotbarSource;
        public GameObject playerRoot, resourceRoot, bossRoot, castRoot;
        public GameObject partyRevivalRoot;
        public Image playerHpFill, resourceFill, bossHpFill, castFill;
        public bool showOnlyCurrentHealth;
        public Text playerHpText, resourceText, bossTitleText, stageText, bossPercentText;
        public Text castNameText, castTimeText, warningText;
        [Tooltip("Optional existing mechanic alert. Its gameplay presenter retains ownership of the source.")]
        public Text mechanicSource;
        public Text tooltipText;
        public GameObject tooltipRoot;
        public bool hideBossUntilEncounter;
        [Tooltip("0: migrate the legacy one-row HUD once. 1: use the authored prefab layout without resetting its design.")]
        public int combatLayoutVersion;
        public bool showPlayerWarning = true;
        public Image identityEmblem;
        public Image[] identitySegments = Array.Empty<Image>();
        public GameObject secondaryResourceRoot;
        public Image secondaryResourceFill;
        public Text secondaryResourceText;
        public SlotBinding[] slots = Array.Empty<SlotBinding>();
        [Tooltip("Opt-in authored materials for coherent job skill colors. Potion, movement and shared resurrection keep their own art.")]
        public JobVisual[] jobVisuals = Array.Empty<JobVisual>();
        public Material defaultSkillMaterial;

        private PlayerController _player;
        private CharacterActor _actor;
        private NetworkRaidAvatar _localAvatar;
        private NineKeyDamageKit _damage;
        private BossController _boss;
        private StageManager _stage;
        private float _nextResolve;
        private CharacterDefinition _resourceDefinition;
        private string _resourceRoute;
        private JobResourceDefinition _resource;
        private int _hoveredSlot = -1;
        private string _paletteJob;
        private bool _splitLayoutChecked;
        private LocalHUDLayoutController _localHUDLayout;
        private Material _skillMaterial;

        private static readonly List<AstellaOrnateHUD> ActivePresenters = new List<AstellaOrnateHUD>();
        private void OnEnable() { if(!ActivePresenters.Contains(this))ActivePresenters.Add(this); _nextResolve = 0; _paletteJob = null; }
        private void OnDisable() { ActivePresenters.Remove(this); }
        public static bool HasBossCastPresentation(BossController boss,GameObject excludedRoot=null)
        {
            foreach(var hud in ActivePresenters)
                if(hud!=null&&hud.isActiveAndEnabled&&hud.castRoot!=null&&hud.castRoot!=excludedRoot&&hud.castFill!=null&&
                    (hud._boss==boss||hud._boss==null&&StageManager.Instance?.boss==boss))return true;
            return false;
        }
        private void OnValidate() { _paletteJob = null; }

        private void Resolve()
        {
            // Retry only for startup/deferred owners and local avatar changes, never every-frame scene traversal.
            if (Time.unscaledTime < _nextResolve) return;
            _nextResolve = Time.unscaledTime + .5f;
            if (_stage == null) _stage = StageManager.Instance != null ? StageManager.Instance : FindFirstObjectByType<StageManager>();
            if (_boss == null) _boss = _stage != null && _stage.boss != null ? _stage.boss : FindFirstObjectByType<BossController>();

            CharacterActor actor = null;
            NetworkRaidAvatar local = null;
            if (RaidNetworkSession.Multiplayer && RaidNetworkSession.Instance != null)
                foreach (var avatar in RaidNetworkSession.Instance.Avatars.Values)
                    if (avatar != null && avatar.IsLocal) { local = avatar; actor = avatar.GetComponent<CharacterActor>(); break; }
            if (actor == null && hotbarSource != null && hotbarSource.combatController != null)
                actor = hotbarSource.combatController.GetComponent<CharacterActor>();
            var ownedPlayer = local != null ? local.GetComponent<PlayerController>() :
                hotbarSource != null && hotbarSource.combatController != null ? hotbarSource.combatController.GetComponent<PlayerController>() : null;
            if (ownedPlayer != null) _player = ownedPlayer;
            if (_player == null) _player = FindFirstObjectByType<PlayerController>();
            if (actor == null && _player != null) actor = _player.GetComponent<CharacterActor>();
            if (_actor != actor)
            {
                _actor = actor;
                _resourceDefinition = null;
            }
            _damage = actor != null ? actor.GetComponent<NineKeyDamageKit>() : null;
            _localAvatar = local;
        }

        private void LateUpdate()
        {
            if(!_splitLayoutChecked)
            {
                _splitLayoutChecked=true;
                try
                {
                    if(AstellaSplitCombatLayout.ApplyIfMissing(this))
                    {
                        _paletteJob=null;
                        Debug.Log("SPLIT COMBAT HUD LIVE UPGRADE: 1234 / identity + V / QFR Space.",this);
                    }
                }
                catch(Exception error){Debug.LogError("Split HUD live upgrade failed: "+error,this);}
            }
            Resolve();
            if(_localHUDLayout==null)_localHUDLayout=LocalHUDLayoutController.Ensure(this);
            bool editing=LocalHUDLayoutController.IsEditing;
            bool visible = editing || RaidNetworkSession.Instance?.MenuOpen != true && !CombatMetricsUI.ResultsOpen;
            SetVisible(playerRoot, visible && (editing || _player != null || _localAvatar?.State != null));
            SetVisible(partyRevivalRoot, visible);
            SetVisible(bossRoot, editing || visible && _boss != null && (!hideBossUntilEncounter || RaidApproachField.Instance == null ||
                RaidApproachField.Instance.BossVisible));
            UpdateHealth();
            UpdateResource(visible);
            UpdateBoss(visible);
            if (warningText != null)
            {
                string warning = showPlayerWarning && visible && mechanicSource != null && mechanicSource.gameObject.activeSelf ? mechanicSource.text : "";
                SetText(warningText, warning);
                SetVisible(warningText.gameObject, !string.IsNullOrWhiteSpace(warning));
            }
            var snapshot = _actor?.Snapshot;
            ResolveJobMaterial();
            foreach (var slot in slots ?? Array.Empty<SlotBinding>()) UpdateSlot(slot, snapshot);
            UpdateTooltip(visible);
        }

        public void ShowTooltip(int slotIndex)
        { _hoveredSlot = slots != null && slotIndex >= 0 && slotIndex < slots.Length ? slotIndex : -1; }
        public void HideTooltip() { _hoveredSlot = -1; }
        public void UseSlot(int slotIndex)
        {
            if (slots == null || slotIndex < 0 || slotIndex >= slots.Length) return;
            var view = slots[slotIndex];
            if (view == null || !view.bindAction || view.button == null || !view.button.interactable ||
                !view.button.gameObject.activeInHierarchy || LocalGameplayInput.Blocked ||
                view.slot != CharacterSlot.Potion && view.slot != CharacterSlot.Movement) return;
            if (_actor != null) _actor.RequestSlot(view.slot);
            else if (!RaidNetworkSession.Multiplayer && _player != null)
            {
                if (view.slot == CharacterSlot.Potion) _player.Potion?.TryUse();
                else _player.TryStartDash();
            }
            LocalGameplayInput.SuppressPointerThisFrame();
        }
        private void UpdateTooltip(bool visible)
        {
            var view = _hoveredSlot >= 0 && slots != null && _hoveredSlot < slots.Length ? slots[_hoveredSlot] : null;
            bool show = visible && !LocalGameplayInput.Blocked && view?.button != null && view.button.gameObject.activeInHierarchy;
            SetVisible(tooltipRoot != null ? tooltipRoot : tooltipText != null ? tooltipText.gameObject : null, show);
            if (!show || tooltipText == null) return;
            var skill = view.sharedResurrection ? null : _actor?.Selected(view.slot);
            string binding = Binding(view);
            string key = binding.Length > 0 ? LocalInputBindings.Label(binding) : LegacyKey(view);
            bool legacy = _actor?.Definition?.legacyRole > 0;
            string title = legacy && view.sourceLabel != null ? view.sourceLabel.text : skill?.name ?? view.sourceLabel?.text ?? CharacterSlots.Name(view.slot);
            string description = legacy ? "" : skill?.description ?? "";
            string status = view.sourceStatus?.text ?? "";
            SetText(tooltipText, "[" + key + "] " + title +
                (description.Length > 0 ? "\n" + description : "") + (status.Length > 0 ? "\n" + status : ""));
        }

        private void UpdateHealth()
        {
            float hp = _localAvatar?.State != null ? _localAvatar.State.hp : _player != null ? _player.currentHp : 0;
            float max = _localAvatar?.State != null ? _localAvatar.State.maxHp : _player != null ? _player.maxHp : 0;
            SetFill(playerHpFill, max > 0 ? hp / max : 0);
            SetText(playerHpText, max > 0 ? showOnlyCurrentHealth ? $"{Mathf.CeilToInt(hp):N0}" : $"{Mathf.CeilToInt(hp):N0} / {Mathf.CeilToInt(max):N0}" : "—");
        }

        private void UpdateResource(bool visible)
        {
            var definition = _actor?.Definition;
            if (definition != _resourceDefinition || _resourceRoute != _actor?.RouteId)
            {
                _resourceDefinition = definition;
                _resourceRoute = _actor?.RouteId;
                _resource = null;
                var tank = definition != null && NineKeyTankKit.Supports(definition) ? CharacterRosterData.Find(definition.id) : null;
                foreach (var resource in definition?.resources ?? Array.Empty<JobResourceDefinition>())
                {
                    // Match the existing job HUD: Dragon Breaker's heat is only visible on its heat route.
                    if (tank?.index == 2 && resource.id == NineKeyTankData.Heat(tank) &&
                        (definition.routes.Length < 3 || _resourceRoute != definition.routes[2].id)) continue;
                    if (_actor.ResourceMaximum(resource.id) <= 0) continue;
                    _resource = resource;
                    break;
                }
            }
            SetVisible(resourceRoot, visible && (_resource != null || LocalHUDLayoutController.IsEditing));
            if (_resource == null) { SetText(resourceText, LocalHUDLayoutController.IsEditing?"아덴 게이지":""); SetFill(resourceFill, 0); UpdateSecondaryResource(visible); return; }
            float value = _actor.Resource(_resource.id), maximum = _actor.ResourceMaximum(_resource.id);
            if (_damage != null && _damage.Active)
            {
                value = _damage.DisplayResource(_resource.id, value);
                maximum = _damage.DisplayMaximum(_resource.id, maximum);
            }
            SetFill(resourceFill, maximum > 0 ? value / maximum : 0);
            string format = _resource.integer ? "0" : "0.#";
            SetText(resourceText, _resource.name + "  " + value.ToString(format) + " / " + maximum.ToString("0.#"));
            bool segmented = _resource.integer && maximum > 0 && maximum <= identitySegments.Length;
            for(int i=0;i<identitySegments.Length;i++)
            {
                var segment=identitySegments[i];if(segment==null)continue;
                SetVisible(segment.gameObject,segmented&&i<maximum);
                var color=segment.color;color.a=value>=i+1?1:.2f;segment.color=color;
            }
            UpdateSecondaryResource(visible);
        }

        private void UpdateSecondaryResource(bool visible)
        {
            if(secondaryResourceRoot==null)return;
            JobResourceDefinition resource=null;
            var ultimate=_actor?.Selected(CharacterSlot.Ultimate);
            foreach(var cost in ultimate?.costs??Array.Empty<ResourceCost>())
                if(cost.resourceId!=_resource?.id)
                {
                    resource=Array.Find(_actor.Definition.resources,r=>r.id==cost.resourceId);
                    if(resource!=null)break;
                }
            // V may not have been selected/unlocked yet; its declared gauge still exists.
            if(resource==null&&_actor?.Definition!=null)
                resource=Array.Find(_actor.Definition.resources,r=>r.id!=_resource?.id&&
                    r.id.EndsWith(".ultimate",StringComparison.Ordinal)&&_actor.ResourceMaximum(r.id)>0);
            SetVisible(secondaryResourceRoot,visible&&(resource!=null||LocalHUDLayoutController.IsEditing));
            if(resource==null){if(LocalHUDLayoutController.IsEditing)SetText(secondaryResourceText,"필살기 게이지");return;}
            float maximum=_actor.ResourceMaximum(resource.id),value=_actor.Resource(resource.id);
            SetFill(secondaryResourceFill,maximum>0?value/maximum:0);
            SetText(secondaryResourceText,resource.name+"  "+value.ToString("0.#")+" / "+maximum.ToString("0.#"));
        }

        private void UpdateBoss(bool visible)
        {
            if (_boss == null) { SetVisible(castRoot, LocalHUDLayoutController.IsEditing); return; }
            int stage = _stage != null ? _stage.currentStage : _boss.currentStageNumber;
            string difficulty = CampaignRuntime.Instance?.DifficultyId == CampaignCatalog.Hard ? "HARD" : "NORMAL";
            SetText(stageText, $"STAGE {stage:00} · {difficulty}");
            // StageManager's encounter title remains correct while the pending next-stage boss is still hidden.
            SetText(bossTitleText, StageTitle(stage, _boss.currentBossTitle));
            SetFill(bossHpFill, _boss.HpRatio);
            SetText(bossPercentText, $"{_boss.HpRatio * 100:0}%");
            bool casting = visible && (_boss.IsCasting||_boss.IsGroggy||LocalHUDLayoutController.IsEditing) && (bossRoot == null || bossRoot.activeSelf);
            SetVisible(castRoot, casting);
            if (casting)
            {
                bool preview=LocalHUDLayoutController.IsEditing&&!_boss.IsCasting&&!_boss.IsGroggy;
                bool special=_boss.IsGroggy||_boss.GuardianStaggerActive;
                SetFill(castFill, preview ? .5f : special&&_boss.bossCastFillImage!=null ? _boss.bossCastFillImage.fillAmount : _boss.CastProgress);
                SetText(castNameText, preview?"보스 시전바":_boss.IsGroggy?"그로기":_boss.CurrentCastSkillName);
                SetText(castTimeText, preview?"2.5s":$"{(_boss.IsGroggy?_boss.GroggyRemainingTime:_boss.CastRemainingTime):0.0}s");
            }
        }

        private void UpdateSlot(SlotBinding view, CharacterStateSnapshot snapshot)
        {
            if (view == null || view.button == null) return;
            if (view.bindAction)
            {
                bool ready = _player != null && RaidCombatAuthority.Local(_player) && !LocalGameplayInput.Blocked &&
                    (view.slot == CharacterSlot.Potion ? _player.Potion?.CanUse == true :
                    view.slot == CharacterSlot.Movement && _player.CanDash);
                if (view.button.interactable != ready) view.button.interactable = ready;
            }
            string key = Binding(view);
            SetText(view.keycap, key.Length > 0 ? LocalInputBindings.Label(key) : LegacyKey(view));
            var skill = view.sharedResurrection ? null : _actor?.Selected(view.slot);
            float remaining = 0, total = 0;
            var combat = hotbarSource != null ? hotbarSource.combatController : _player != null ? _player.combatController : null;
            bool legacy = _actor?.Definition?.legacyRole > 0;
            if (legacy && !view.sharedResurrection)
            {
                if (combat != null && (view.slot == CharacterSlot.Gcd1 || view.slot == CharacterSlot.Gcd2))
                { remaining = combat.GcdRemaining; total = combat.GcdTotal; }
                else if (combat != null && view.slot == CharacterSlot.OffGcd1)
                { remaining = combat.Skill3CooldownRemaining; total = combat.Skill3CooldownMax; }
                else if (combat != null && view.slot == CharacterSlot.Job1)
                { remaining = combat.Skill4CooldownRemaining; total = combat.Skill4CooldownMax; }
                else if (view.sourceStatus != null && view.sourceStatus.gameObject.activeSelf)
                {
                    float.TryParse(view.sourceStatus.text, NumberStyles.Float, CultureInfo.InvariantCulture, out remaining);
                    total = remaining;
                }
            }
            else if (skill != null && snapshot != null)
            {
                var state = Array.Find(snapshot.skills, s => s.id == skill.id);
                if (state != null && state.charges == 0) { remaining = state.recharge; total = state.rechargeTotal; }
                if (skill.usesGcd && snapshot.gcd > remaining) { remaining = snapshot.gcd; total = snapshot.gcdTotal; }
            }
            else if (!view.sharedResurrection && view.sourceStatus != null && view.sourceStatus.gameObject.activeSelf)
            {
                // Legacy source only has a bare numeric value while its cooldown is active.
                float.TryParse(view.sourceStatus.text, NumberStyles.Float, CultureInfo.InvariantCulture, out remaining);
                total = remaining;
            }
            if (view.slot == CharacterSlot.Potion && _player?.Potion != null)
            { remaining = _player.Potion.CooldownRemaining; total = _player.Potion.Settings.cooldown; }
            if (view.slot == CharacterSlot.Movement && _player != null && _actor?.Definition?.legacyRole > 0)
            { remaining = _player.DashCooldownRemaining; total = remaining > 0 && _player.DashCooldownFraction > 0 ? remaining / _player.DashCooldownFraction : 0; }
            bool cooling = remaining > .01f;
            SetText(view.cooldown, view.sharedResurrection ? ((_stage != null ? _stage.IrisCoins : 0) + "/" + IrisRescueState.DefaultCoins) :
                cooling ? remaining >= 10 ? Mathf.CeilToInt(remaining).ToString() : remaining.ToString("0.0") : "");
            if (view.cooldownShade != null)
            {
                SetVisible(view.cooldownShade.gameObject, cooling);
                SetFill(view.cooldownShade, total > 0 ? remaining / total : 0);
            }
            if (view.stateGroup != null)
            {
                float alpha = view.button.interactable ? 1 : view.unavailableAlpha;
                if (!Mathf.Approximately(view.stateGroup.alpha, alpha)) view.stateGroup.alpha = alpha;
            }
            if (view.artwork != null)
            {
                Sprite sprite = skill != null ? RaidUIPresentation.Settings?.Icon(skill.id) : null;
                if (sprite == null) sprite = view.fallbackArt;
                if (view.artwork.sprite != sprite) view.artwork.sprite = sprite;
                bool enabled = sprite != null;
                if (view.artwork.enabled != enabled) view.artwork.enabled = enabled;
                if (!view.sharedResurrection && view.slot != CharacterSlot.Potion && view.slot != CharacterSlot.Movement &&
                    (defaultSkillMaterial != null || jobVisuals != null && jobVisuals.Length > 0) &&
                    (!view.materialResolved || view.materialImage != view.artwork || view.appliedMaterial != _skillMaterial))
                {
                    view.artwork.material = _skillMaterial;
                    view.materialResolved = true;
                    view.materialImage = view.artwork;
                    view.appliedMaterial = _skillMaterial;
                }
            }
        }

        private void ResolveJobMaterial()
        {
            string job = _actor?.Definition?.id ?? "";
            if (_paletteJob == job) return;
            _paletteJob = job;
            var visual = Array.Find(jobVisuals ?? Array.Empty<JobVisual>(), v => v != null && v.jobId == job);
            _skillMaterial = visual?.skillMaterial != null ? visual.skillMaterial : defaultSkillMaterial;
            if(identityEmblem!=null)
            {
                var emblem=visual?.identityEmblem??RaidUIPresentation.Settings?.Icon(job);
                if(emblem!=null)identityEmblem.sprite=emblem;
            }
            if(visual!=null&&visual.identityEmblem!=null)
            {
                if(resourceFill!=null)resourceFill.color=visual.identityColor;
                foreach(var segment in identitySegments)if(segment!=null)segment.color=visual.identityColor;
            }
        }

        private static string LegacyKey(SlotBinding view)
        {
            string value = view.sourceKey != null ? view.sourceKey.text : view.sourceLabel != null ? view.sourceLabel.text : "";
            int open = value.IndexOf('['), close = value.IndexOf(']');
            return open >= 0 && close > open ? value.Substring(open + 1, close - open - 1) : value;
        }
        private string Binding(SlotBinding view) => !string.IsNullOrEmpty(view.bindingOverride) ? view.bindingOverride :
            view.sharedResurrection ? "Skill5" : _actor?.Definition != null ? CharacterSlots.Binding(_actor.Definition, view.slot) : "";
        private static string StageTitle(int stage, string fallback) => stage switch
        { 1 => "마을 수호 석상", 2 => "플로라 기어", 3 => "캄파넬라", 4 => "마리오네트", 5 => "유스티티아", 6 => "야누스", _ => fallback ?? "" };
        private static void SetText(Text text, string value) { if (text != null && text.text != value) text.text = value; }
        private static void SetFill(Image image, float value) { if (image != null) image.fillAmount = Mathf.Clamp01(value); }
        private static void SetVisible(GameObject target, bool visible) { if (target != null && target.activeSelf != visible) target.SetActive(visible); }
    }
}
