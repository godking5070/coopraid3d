using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    // Runs after player movement and before ordinary combat Updates. Drinking changes only health,
    // item cooldown and revival protection; it never invokes a skill, changes facing or cancels a cast.
    [DefaultExecutionOrder(-75)]
    public sealed partial class CommonPotion : MonoBehaviour
    {
        private static readonly PotionSettings FallbackSettings = new PotionSettings();
        private readonly PotionState _state = new PotionState();
        private PlayerController _player;
        private PartyBotAI _bot;
        private StageManager _stage;
        private bool _qHeld;
        private LineRenderer _pulse;
        private Material _pulseMaterial;
        private float _pulseRemaining;

        public bool ItemsBlocked { get; set; }
        public float CooldownRemaining => Replicated != null ? Replicated.potion : (float)_state.CooldownRemaining;
        public float CooldownFraction => Replicated != null ? Mathf.Clamp01(Replicated.potion / Mathf.Max(0.001f, Replicated.potionTotal)) : _state.CooldownDuration > 0 ? Mathf.Clamp01((float)(_state.CooldownRemaining / _state.CooldownDuration)) : 0;
        public bool HasRevivalProtection => Replicated != null ? Replicated.protection > 0 : _state.RevivalProtectionRemaining > 0;
        public float CurrentHp => _player != null ? _player.currentHp : _bot != null ? _bot.currentHp : 0;
        public float MaximumHp => _player != null ? _player.maxHp : _bot != null ? _bot.maxHp : 0;
        public bool IsAlive => CurrentHp > 0 && (_player != null ? !_player.IsDead : _bot != null && !_bot.isDead);
        public PotionSettings Settings
        {
            get
            {
                if (_stage == null) _stage = FindFirstObjectByType<StageManager>();
                return _stage != null ? _stage.potionSettings : FallbackSettings;
            }
        }
        private bool UseBlocked => !isActiveAndEnabled || ItemsBlocked || StageManager.CombatSuspended || Time.timeScale <= 0.001f ||
            (_stage != null && (_stage.IsStageTransitioning || (_stage.boss != null && _stage.boss.IsBossDefeatedCollapse))) ||
            (_player != null && (!_player.isActiveAndEnabled || _player.IsStunned || RaidCombatAuthority.InputBlocked(this))) ||
            (_bot != null && !_bot.isActiveAndEnabled);
        public bool CanUse => CooldownRemaining <= 0 && _state.CanUse(Settings, CurrentHp, MaximumHp, IsAlive, UseBlocked);

        public static CommonPotion Ensure(GameObject member)
        {
            var potion = member.GetComponent<CommonPotion>();
            return potion != null ? potion : member.AddComponent<CommonPotion>();
        }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bot = GetComponent<PartyBotAI>();
            _stage = FindFirstObjectByType<StageManager>();
        }

        private void Start()
        {
            if (_player != null && RaidCombatAuthority.Local(this) && GetComponent<PotionSlotUI>() == null) gameObject.AddComponent<PotionSlotUI>();
        }

        private void Update()
        {
            bool pressed = false, held = false;
            if (_player != null && RaidCombatAuthority.Local(this))
            {
                pressed = LocalInputBindings.Pressed("Potion");
                held = LocalInputBindings.Held("Potion");
            }
            bool newPress = pressed && !_qHeld;
            _qHeld = held; // Consume blocked/full-health/cooldown presses too; never queue an item use.
            if (!RaidCombatAuthority.Replica) _state.Tick(!StageManager.CombatSuspended && Time.timeScale > 0.001f ? Time.deltaTime : 0);
            if (!IsAlive) _state.EndRevivalProtection();
            if (_player != null && newPress) TryUse();
            else if (_bot != null && PotionState.NeedsAutomaticPotion(CurrentHp, MaximumHp, IsAlive)) TryUse();
            UpdatePulse();
        }

        public bool TryUse()
        {
            if(StageManager.Instance?.boss?.MarionetteControlled(transform)==true)return false;
            if (RaidCombatAuthority.Route(this, 6)) return false;
            if (!CharacterActor.Ensure(gameObject).Available(CharacterSlot.Potion, out _)) return false;
            float actual = _state.TryUse(Settings, CurrentHp, MaximumHp, IsAlive, UseBlocked, ApplyHealing);
            if (actual <= 0) return false;
            BeginPulse();
            return true;
        }

        private float ApplyHealing(float amount) => _player != null
            ? _player.ReceiveHeal(amount, transform, HealingKind.Potion)
            : _bot != null ? _bot.ReceiveHeal(amount, transform, HealingKind.Potion) : 0;

        public void GrantRevivalProtection(float seconds = PotionSettings.RevivalProtectionSeconds) => _state.GrantRevivalProtection(seconds);
        public void EndRevivalProtection() => _state.EndRevivalProtection();
        public void ResetForBossRetry()
        {
            _state.ResetForBossRetry();
            _pulseRemaining = 0;
            if (_pulse != null) _pulse.enabled = false;
        }

        private void BeginPulse()
        {
            if (_pulse == null)
            {
                var effect = new GameObject("Potion_HealCross");
                effect.transform.SetParent(transform, false);
                _pulse = effect.AddComponent<LineRenderer>();
                _pulse.useWorldSpace = false;
                _pulse.positionCount = 5;
                _pulse.SetPositions(new[] { Vector3.left * 0.27f, Vector3.right * 0.27f, Vector3.zero,
                    Vector3.down * 0.27f, Vector3.up * 0.27f });
                _pulse.startWidth = _pulse.endWidth = 0.065f;
                _pulse.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _pulse.receiveShadows = false;
                _pulseMaterial = new Material(Shader.Find("Sprites/Default"));
                _pulse.sharedMaterial = _pulseMaterial;
            }
            _pulseRemaining = 0.45f;
            _pulse.enabled = true;
        }

        private void UpdatePulse()
        {
            if (_pulse == null || !_pulse.enabled) return;
            _pulseRemaining = Mathf.Max(0, _pulseRemaining - Time.deltaTime);
            _pulse.enabled = _pulseRemaining > 0;
            _pulse.transform.position = transform.position + Vector3.up * (0.8f + (0.45f - _pulseRemaining));
            if (Camera.main != null) _pulse.transform.rotation = Camera.main.transform.rotation;
            _pulse.startColor = _pulse.endColor = new Color(0.3f, 1f, 0.55f, _pulseRemaining / 0.45f);
        }

        private void OnDestroy() { if (_pulseMaterial != null) Destroy(_pulseMaterial); }
    }
}
