using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>Authored class-specific ultimate charge states, independent of the job's primary resource.</summary>
    [DefaultExecutionOrder(11700)]
    public sealed class AstellaClassUltimateHUD : MonoBehaviour
    {
        public enum UltimateState { Unavailable, Charging, Ready }
        [Serializable] public sealed class StateView
        {
            public GameObject root;
            public RectTransform fillMask;
        }
        [Serializable] public sealed class Motion
        {
            public string[] sheetResourcePaths = Array.Empty<string>();
            public int frameCount = 16, framesPerSheet = 16, columns = 4;
            public float framesPerSecond = 24;
            public Vector2 frameSize = new Vector2(360, 280);
        }
        [Serializable] public sealed class ClassView
        {
            public string jobId;
            public GameObject root;
            public StateView charging = new StateView(), ready = new StateView(), unavailable = new StateView();
            [Tooltip("Top-left pixel coordinates from the provided 360x280 ultimate manifest.")]
            public Rect chargeBounds = new Rect(30, 53, 300, 156);
            public Vector2 canvasSize = new Vector2(360, 280);
            public Text percentText, keyText;
            public RawImage motion;
            public Motion chargeBurst = new Motion();
            public Motion readyLoop = new Motion { frameCount = 32, framesPerSecond = 20 };
        }

        public AstellaOrnateHUD hud;
        public AstellaPlayerHUDV3 readinessSource;
        public ClassView[] views = Array.Empty<ClassView>();
        public bool reduceMotion;
        [Tooltip("Only used by HUD layout editing when no actual class is selected. It never supplies a charge value.")]
        public int layoutPreviewClass;
        public UltimateState CurrentState { get; private set; } = UltimateState.Unavailable;
        public float ChargeRatio { get; private set; }
        public string ResourceId { get; private set; } = "";
        public string RestrictionReason { get; private set; } = "준비 중";
        public bool IsReady => CurrentState == UltimateState.Ready;

        private CharacterActor _actor;
        private PlayerController _player;
        private NineKeyDamageKit _damage;
        private CharacterKit _kit;
        private float _nextResolve;
        private ClassView _active;
        private bool _sampled, _previousFull, _previousReduced;
        private UltimateState _previousState;
        private float _burstStart = -1, _loopStart;
        private RawImage _motionImage;
        private Texture2D _texture;
        private string _sheetPath;
        private float _nextTextureLoad;
        private const int MaximumCachedSheets = 3;
        private readonly Dictionary<string, Texture2D> _sheetCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Queue<string> _sheetOrder = new Queue<string>();

        private void Awake()
        {
            if (hud == null) hud = GetComponentInParent<AstellaOrnateHUD>();
            if (readinessSource == null) readinessSource = GetComponentInParent<AstellaPlayerHUDV3>();
        }
        private void OnEnable() { _nextResolve = 0; _sampled = false; }
        private void OnDisable() { ResetMotion(); ReleaseCache(); _active = null; _sampled = false; }
        private void OnDestroy() { ReleaseCache(); }

        private void LateUpdate()
        {
            ResolveActor();
            bool editing = LocalHUDLayoutController.IsEditing;
            bool visible = editing || RaidNetworkSession.Instance?.MenuOpen != true && !CombatMetricsUI.ResultsOpen;
            string job = _actor?.Definition?.id;
            var view = Array.Find(views ?? Array.Empty<ClassView>(), entry => entry != null && entry.jobId == job);
            if (view == null && editing && views != null && views.Length > 0)
                view = views[Mathf.Clamp(layoutPreviewClass, 0, views.Length - 1)];
            if (_active != view)
            {
                ResetMotion(); ReleaseCache(); _active = view; _sampled = false;
            }
            foreach (var candidate in views ?? Array.Empty<ClassView>())
                if (candidate != null) SetVisible(candidate.root, visible && candidate == view);

            var resource = Array.Find(_actor?.Definition?.resources ?? Array.Empty<JobResourceDefinition>(),
                entry => entry != null && entry.id != null && entry.id.EndsWith(".ultimate", StringComparison.Ordinal));
            float maximum = resource != null ? _actor.ResourceMaximum(resource.id) : 0;
            float value = resource != null ? _actor.Resource(resource.id) : 0;
            bool validCharge = resource != null && maximum > 0 && CharacterDefinitionValidation.Finite(maximum) && CharacterDefinitionValidation.Finite(value);
            ResourceId = validCharge ? resource.id : "";
            ChargeRatio = validCharge ? Mathf.Clamp01(value / maximum) : 0;
            bool full = validCharge && value + .0001f >= maximum;
            var binding = UltimateBinding();
            var skill = _actor?.Selected(CharacterSlot.Ultimate);
            var snapshot = _actor?.Snapshot;
            bool blocked = Restricted(binding, skill, snapshot, ResourceId, validCharge, out string reason);
            bool sourceReady = readinessSource != null && readinessSource.isActiveAndEnabled && readinessSource.IsReady;
            CurrentState = blocked || full && !sourceReady ? UltimateState.Unavailable : full ? UltimateState.Ready : UltimateState.Charging;
            RestrictionReason = blocked ? reason : full && !sourceReady ? readinessSource?.ReadinessReason ?? "필살기 사용 조건 미충족" : "";

            if (view != null)
            {
                SetVisible(view.charging?.root, CurrentState == UltimateState.Charging);
                SetVisible(view.ready?.root, CurrentState == UltimateState.Ready);
                SetVisible(view.unavailable?.root, CurrentState == UltimateState.Unavailable);
                Fill(view.charging, view, ChargeRatio); Fill(view.ready, view, ChargeRatio); Fill(view.unavailable, view, ChargeRatio);
                SetText(view.percentText, validCharge ? (full ? "100%" : Mathf.Clamp(Mathf.FloorToInt(ChargeRatio * 100), 0, 99) + "%") : "—");
                string key = binding != null && !string.IsNullOrEmpty(binding.bindingOverride) ? binding.bindingOverride :
                    _actor?.Definition != null ? CharacterSlots.Binding(_actor.Definition, CharacterSlot.Ultimate) : "Ultimate";
                SetText(view.keyText, LocalInputBindings.Label(key));
            }

            float now = Time.unscaledTime;
            bool animate = visible && view != null && CurrentState == UltimateState.Ready && !reduceMotion;
            if (!animate) ResetMotion();
            else
            {
                if (!_sampled || _previousState != UltimateState.Ready || _previousReduced)
                {
                    _loopStart = now;
                    _burstStart = _sampled && _previousState == UltimateState.Charging && !_previousFull && full ? now : -1;
                }
                Animate(view, now);
            }
            _sampled = true; _previousFull = full; _previousState = CurrentState; _previousReduced = reduceMotion;
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
            else if (hud?.hotbarSource?.combatController != null)
                actor = hud.hotbarSource.combatController.GetComponent<CharacterActor>();
            _actor = actor;
            _player = actor != null ? actor.GetComponent<PlayerController>() : null;
            _damage = actor != null ? actor.GetComponent<NineKeyDamageKit>() : null;
            _kit = actor != null ? actor.GetComponent<CharacterKit>() : null;
        }

        private AstellaOrnateHUD.SlotBinding UltimateBinding()
        {
            foreach (var binding in hud != null && hud.slots != null ? hud.slots : Array.Empty<AstellaOrnateHUD.SlotBinding>())
                if (binding != null && !binding.sharedResurrection && binding.slot == CharacterSlot.Ultimate) return binding;
            return null;
        }

        private bool Restricted(AstellaOrnateHUD.SlotBinding binding, CharacterSkillDefinition skill, CharacterStateSnapshot snapshot,
            string ultimateResource, bool validCharge, out string reason)
        {
            reason = "필살기 정보 없음";
            if (_actor?.Definition == null || !validCharge || binding?.button == null) return true;
            if (!_actor.Available(CharacterSlot.Ultimate, out reason)) return true;
            if (LocalHUDLayoutController.IsEditing || LocalGameplayInput.Blocked || RaidNetworkSession.Instance?.MenuOpen == true || CombatMetricsUI.ResultsOpen || StageManager.PlayerSkillsSuspended)
            { reason = "조작 제한"; return true; }
            if (!RaidCombatAuthority.Local(_actor) || !CombatRange.LivingPartyMember(_actor.transform) ||
                _player != null && (_player.IsStunned || _player.IsBeingRescued || _player.IsBeingKnockedBack))
            { reason = "행동 제한"; return true; }
            var boss = StageManager.Instance?.boss;
            if (boss != null && (boss.GuardianLured(_actor.transform) || boss.MarionetteControlled(_actor.transform)))
            { reason = "행동 제한"; return true; }
            if (skill == null || snapshot == null) { reason = "필살기 정보 없음"; return true; }
            if (!string.IsNullOrEmpty(snapshot.castSkillId) && !CharacterSlots.InterruptsCast(_actor.Definition, CharacterSlot.Ultimate) &&
                (!skill.usableWhileCasting || skill.castTime > 0))
            { reason = "시전 중"; return true; }
            var timing = Array.Find(snapshot.skills ?? Array.Empty<JobSkillState>(), state => state != null && state.id == skill.id);
            if (timing == null || timing.charges <= 0 || skill.usesGcd && snapshot.gcd > .01f)
            { reason = "재사용 대기 중"; return true; }
            float scale = _damage?.Active == true ? _damage.CostScale(skill) : _kit?.Active == true ? _kit.CostScale(skill) : 1;
            foreach (var cost in skill.costs ?? Array.Empty<ResourceCost>())
                if (cost != null && cost.resourceId != ultimateResource && _actor.Resource(cost.resourceId) + .001f < cost.amount * scale)
                { reason = "별도 자원 부족"; return true; }
            reason = "";
            return false;
        }

        private static void Fill(StateView state, ClassView view, float ratio)
        {
            if (state?.fillMask == null) return;
            float height = Mathf.Max(1, view.canvasSize.y);
            float bottom = height - view.chargeBounds.y - view.chargeBounds.height;
            Vector2 min = new Vector2(0, bottom / height);
            Vector2 max = new Vector2(1, (bottom + view.chargeBounds.height * ratio) / height);
            if (state.fillMask.anchorMin != min) state.fillMask.anchorMin = min;
            if (state.fillMask.anchorMax != max) state.fillMask.anchorMax = max;
        }

        private void Animate(ClassView view, float now)
        {
            if (view.motion == null) { DetachTexture(); return; }
            if (_motionImage != view.motion)
            { DetachTexture(); _motionImage = view.motion; }
            Motion clip = view.readyLoop;
            int frame;
            float burstDuration = Duration(view.chargeBurst);
            if (_burstStart >= 0 && burstDuration > 0 && now - _burstStart < burstDuration)
            {
                clip = view.chargeBurst;
                frame = Mathf.Min(clip.frameCount - 1, Mathf.FloorToInt((now - _burstStart) * clip.framesPerSecond));
            }
            else
            {
                if (_burstStart >= 0) { _loopStart = _burstStart + burstDuration; _burstStart = -1; }
                if (clip == null || clip.frameCount <= 0 || clip.framesPerSecond <= 0) { ResetMotion(); return; }
                frame = Mathf.FloorToInt(Mathf.Max(0, now - _loopStart) * clip.framesPerSecond) % clip.frameCount;
            }
            if (clip == null || clip.framesPerSheet <= 0 || clip.columns <= 0 || clip.frameSize.x <= 0 || clip.frameSize.y <= 0)
            { ResetMotion(); return; }
            int sheet = frame / clip.framesPerSheet;
            if (clip.sheetResourcePaths == null || sheet >= clip.sheetResourcePaths.Length) { ResetMotion(); return; }
            string path = (clip.sheetResourcePaths[sheet] ?? "").Replace('\\', '/');
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4);
            if (_sheetPath != path)
            {
                DetachTexture(); _motionImage = view.motion; _sheetPath = path; _nextTextureLoad = 0;
            }
            if (_texture == null && !string.IsNullOrEmpty(path) && now >= _nextTextureLoad)
            {
                _nextTextureLoad = now + .5f;
                if (!_sheetCache.TryGetValue(path, out _texture) || _texture == null)
                {
                    // The provided class has three sheets. Bound malformed/custom clips too.
                    while (_sheetCache.Count >= MaximumCachedSheets && _sheetOrder.Count > 0)
                    {
                        string oldest = _sheetOrder.Dequeue();
                        if (!_sheetCache.TryGetValue(oldest, out var obsolete)) continue;
                        _sheetCache.Remove(oldest);
                        if (obsolete != null) Resources.UnloadAsset(obsolete);
                    }
                    _texture = Resources.Load<Texture2D>(path);
                    if (_texture != null)
                    {
                        _sheetCache[path] = _texture;
                        _sheetOrder.Enqueue(path);
                    }
                }
            }
            if (_texture == null) { _motionImage.enabled = false; return; }
            if (_motionImage.texture != _texture) _motionImage.texture = _texture;
            int local = frame % clip.framesPerSheet, column = local % clip.columns, row = local / clip.columns;
            Rect uv = new Rect(column * clip.frameSize.x / _texture.width, 1 - (row + 1) * clip.frameSize.y / _texture.height,
                clip.frameSize.x / _texture.width, clip.frameSize.y / _texture.height);
            if (_motionImage.uvRect != uv) _motionImage.uvRect = uv;
            if (!_motionImage.enabled) _motionImage.enabled = true;
        }

        private static float Duration(Motion clip) => clip != null && clip.frameCount > 0 && clip.framesPerSecond > 0 ? clip.frameCount / clip.framesPerSecond : 0;
        private void ResetMotion() { _burstStart = -1; DetachTexture(); }
        private void DetachTexture()
        {
            if (_motionImage != null) { _motionImage.texture = null; _motionImage.enabled = false; }
            _texture = null; _sheetPath = null; _motionImage = null;
        }
        private void ReleaseCache()
        {
            DetachTexture();
            foreach (var texture in _sheetCache.Values)
                if (texture != null) Resources.UnloadAsset(texture);
            _sheetCache.Clear();
            _sheetOrder.Clear();
        }
        private static void SetVisible(GameObject target, bool visible) { if (target != null && target.activeSelf != visible) target.SetActive(visible); }
        private static void SetText(Text target, string value) { if (target != null && target.text != value) target.text = value; }
    }
}
