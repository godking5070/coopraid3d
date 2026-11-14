using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    [DefaultExecutionOrder(100)]
    public sealed partial class BattleResurrection : MonoBehaviour
    {
        private readonly ResurrectionState _state = new ResurrectionState();
        private PartyLife _life, _target;
        private PlayerController _player;
        private StageManager _stage;
        private Vector3 _lastPosition;
        private float _range, _healthFraction;
        private bool _fiveHeld;
        private static readonly ResurrectionSettings Defaults = new ResurrectionSettings();
        public bool IsCasting => Replicated != null ? Replicated.resurrecting : _state.Casting;
        public float Remaining => Replicated != null ? Replicated.resurrection : (float)_state.Remaining;
        public float Cooldown => Replicated != null ? Replicated.resurrectionCooldown : (float)_state.Cooldown;
        public float Progress => Replicated != null ? 1f - Replicated.resurrection / Mathf.Max(0.001f, Replicated.resurrectionTotal) : _state.Duration > 0 ? 1f - (float)(_state.Remaining / _state.Duration) : 0;
        public bool IsHealer => _life != null && _life.IsHealer;
        public ResurrectionSettings Settings => _stage != null ? _stage.resurrectionSettings : Defaults;
        private bool Moved => _player != null ? _player.IsMovingXZ || _player.IsDashing || _player.IsBeingRescued || _player.IsBeingKnockedBack
            : FlatDistance(transform.position, _lastPosition) > 0.001f;

        private void Awake()
        {
            _life = GetComponent<PartyLife>(); _player = GetComponent<PlayerController>();
            _stage = FindFirstObjectByType<StageManager>(); _lastPosition = transform.position;
        }
        private void Start() { if (_player != null && RaidCombatAuthority.Local(this) && GetComponent<ResurrectionSlotUI>() == null) gameObject.AddComponent<ResurrectionSlotUI>(); }

        private void Update()
        {
            if (_stage == null) _stage = FindFirstObjectByType<StageManager>();
            float dt = Time.timeScale > 0.001f ? Time.deltaTime : 0;
            if (!RaidCombatAuthority.Replica && !StageManager.CombatSuspended) _state.Tick(dt);
            bool pressed = false, held = false;
            if (_player != null && RaidCombatAuthority.Local(this))
            {
                pressed = LocalInputBindings.Pressed("Skill5");
                held = LocalInputBindings.Held("Skill5");
            }
            bool began = false;
            if (pressed && !_fiveHeld && !LocalGameplayInput.Blocked && IsHealer)
                began = TryBegin(TargetingManager.Instance != null ? TargetingManager.Instance.CurrentTarget : null);
            _fiveHeld = held;
            if (!RaidCombatAuthority.Replica && IsCasting && !began)
            {
                bool valid = _life != null && !_life.IsDead && IsHealer && !StageManager.CombatSuspended && !Moved &&
                    _target != null && _target.IsDead && FlatDistance(transform.position, _target.transform.position) <= _range &&
                    (_player == null || (!RaidCombatAuthority.SimulationInputBlocked(this) && !_player.IsStunned));
                if (!valid) { Cancel(); Feedback("부활 시전 취소"); }
                else if (_state.Advance(dt, true))
                {
                    bool revived = _target.TryRevive(_healthFraction);
                    _state.Complete(revived);
                    if (revived) GetComponent<CommonPotion>()?.EndRevivalProtection();
                    _target = null;
                }
            }
            _lastPosition = transform.position;
        }

        public bool TryBegin(Transform target)
        {
            if (RaidCombatAuthority.Route(this, 5, target)) return false;
            if (!IsHealer || _life.IsDead || StageManager.CombatSuspended || IsCasting || Cooldown > 0 ||
                (_player != null && (RaidCombatAuthority.InputBlocked(this) || _player.IsStunned || (_player.combatController != null && _player.combatController.IsCastingSpell)))) return false;
            var life = target != null ? target.GetComponent<PartyLife>() : null;
            if (life == null || !PartyLife.OrderedMembers().Contains(target)) { Feedback("부활할 아군을 선택하세요"); return false; }
            if (!life.IsDead) { Feedback("살아 있는 대상은 부활할 수 없습니다"); return false; }
            if (FlatDistance(transform.position, target.position) > Settings.range) { Feedback("부활 대상이 사거리 밖에 있습니다"); return false; }
            if (Moved) { Feedback("이동 중에는 부활을 시전할 수 없습니다"); return false; }
            if (!_state.Begin(Settings, true)) return false;
            GetComponent<CommonPotion>()?.EndRevivalProtection();
            _target = life; _range = Settings.range; _healthFraction = Settings.healthFraction;
            _lastPosition = transform.position;
            return true;
        }

        public Transform ChooseBotTarget()
        {
            Transform first = null;
            foreach (var member in PartyLife.OrderedMembers())
            {
                var life = member != null ? member.GetComponent<PartyLife>() : null;
                if (life == null || !life.IsDead) continue;
                if (life.IsTank) return member;
                if (first == null) first = member;
            }
            return first;
        }

        public void Cancel() { _state.Cancel(); _target = null; }
        public void ResetForRetry() { _state.Reset(); _target = null; _lastPosition = transform.position; }
        private void Feedback(string text)
        {
            if (_player != null) FloatingTextPopup.Spawn(transform.position + Vector3.up * 2.5f, text, new Color(0.6f, 0.9f, 1f), false);
        }
        public static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
