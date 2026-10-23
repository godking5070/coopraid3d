using UnityEngine;

namespace RaidRogue
{
    // Replica controllers present shared combat state; only the owner reads input and only the host simulates combat.
    public sealed class NetworkRaidAvatar : MonoBehaviour
    {
        public int Id { get; private set; }
        public int Role { get; private set; }
        public string DisplayName { get; private set; }
        public bool IsLocal { get; private set; }
        public float CombatRadius { get; private set; }
        // Host-owned movement gate, including combat state, stun, death and forced movement.
        public bool MovementLocked { get; set; }
        public bool IsBot { get; set; }
        public int MotionVersion { get; set; }
        public bool ForcedLast { get; set; }
        public Transform CombatTarget { get; set; }
        public RaidCombatActor State { get; set; }
        private Vector3 _targetPosition;
        private float _targetYaw;
        private TextMesh _name;
        private TrailRenderer _trail;
        private Material _trailMaterial;
        private bool _first = true;
        public static Color RoleColor(int role) => role == 1 ? new Color(0.18f, 0.55f, 1f) : role == 2
            ? new Color(0.18f, 0.90f, 0.38f) : role == 3 ? new Color(1f, 0.85f, 0.12f) : new Color(1f, 0.48f, 0.12f);
        public string RoleName => CombatMetricsManager.RoleName(Role);
        public void Configure(int id, bool local, float radius) { Id = id; IsLocal = local; CombatRadius = radius; }
        public void SetIdentity(int role, string displayName)
        {
            if (Role == role && DisplayName == displayName) return;
            Role = role; DisplayName = RaidRoster.CleanName(displayName);
            if (GetComponent<PlayerController>() != null)
            {
                var player = GetComponent<PlayerController>();
                player.ApplyRoleStatsAndVisuals((PlayerRole)role);
                player.SetNetworkDisplayName(DisplayName + " · " + RoleName, RoleColor(role));
            }
            else
            {
                var visual = GetComponent<DualVisualController>();
                if (visual == null) visual = gameObject.AddComponent<DualVisualController>();
                visual.unitKind = role == 1 ? VisualUnitKind.BotTank : role == 2 ? VisualUnitKind.BotHealer : role == 3 ? VisualUnitKind.PlayerMelee : VisualUnitKind.BotRangedDPS;
                visual.SetPrimaryColor(RoleColor(role)); visual.SetSecondaryColor(Color.white); visual.Refresh2DSpritesAndColors();
                visual.ApplyViewMode(ViewModeManager.Instance != null && ViewModeManager.Instance.Is2DMode);
            }
            if (IsLocal) return; // Reuse the existing overhead label instead of drawing two names.
            if (_name == null)
            {
                var label = new GameObject("NetworkPlayerName"); label.transform.SetParent(transform, false);
                _name = label.AddComponent<TextMesh>(); _name.fontSize = 48; _name.characterSize = 0.055f; _name.anchor = TextAnchor.MiddleCenter;
            }
            _name.text = DisplayName + " · " + RoleName; _name.color = RoleColor(role);
        }
        public void Display(Vector3 position, float yaw, bool dashing)
        {
            _targetPosition = position; _targetYaw = yaw;
            if (_first || IsLocal || RaidCombatAuthority.Host) { transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0)); _first = false; }
            if (_trail == null)
            {
                var obj = new GameObject("NetworkDashTrail"); obj.transform.SetParent(transform, false); obj.transform.localPosition = Vector3.down * 0.65f;
                _trail = obj.AddComponent<TrailRenderer>(); _trail.time = 0.15f; _trail.startWidth = 0.16f; _trail.endWidth = 0;
                _trail.minVertexDistance = 0.08f; _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _trailMaterial = new Material(Shader.Find("Sprites/Default")); _trail.sharedMaterial = _trailMaterial;
                _trail.startColor = new Color(0.6f, 0.9f, 1f, 0.45f); _trail.endColor = new Color(0.6f, 0.9f, 1f, 0);
            }
            _trail.emitting = dashing;
        }
        private void LateUpdate()
        {
            if (!IsLocal && !_first && !RaidCombatAuthority.Host)
            {
                float blend = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
                transform.position = Vector3.Lerp(transform.position, _targetPosition, blend);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, _targetYaw, 0), blend);
            }
            if (_name != null)
            {
                _name.gameObject.SetActive(GetComponent<PlayerController>() == null && GetComponent<PartyBotAI>() == null);
                _name.transform.position = transform.position + new Vector3(0, 1.8f, ViewModeManager.Is2DViewActive ? 1.15f : 0);
                if (Camera.main != null) _name.transform.rotation = Camera.main.transform.rotation;
            }
        }
        private void OnDestroy() { if (_trailMaterial != null) Destroy(_trailMaterial); }
    }
}
