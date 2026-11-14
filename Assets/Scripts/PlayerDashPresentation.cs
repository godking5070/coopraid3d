using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Added by PlayerController at runtime, shared by the 2D and 3D views.
    public sealed class PlayerDashPresentation : MonoBehaviour
    {
        private PlayerController _player;
        private TrailRenderer _trail;
        private Material _trailMaterial;
        private GameObject _slot;
        private Image _background;
        private RectTransform _overlay;
        private Text _state, _details, _key;
        private float _nextHotbarSearch;

        public void Initialize(PlayerController player)
        {
            _player = player;
            if (_trail != null) return;
            GameObject trailObject = new GameObject("CommonDash_Trail");
            trailObject.transform.SetParent(transform, false);
            _trail = trailObject.AddComponent<TrailRenderer>();
            _trail.time = 0.16f;
            _trail.emitting = false; // Explicit positions also render a whole dash on a slow frame.
            _trail.minVertexDistance = 0.05f;
            _trail.startWidth = 0.11f;
            _trail.endWidth = 0.025f;
            _trail.numCapVertices = 2;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.startColor = new Color(0.7f, 0.92f, 1f, 0.5f);
            _trail.endColor = new Color(0.7f, 0.92f, 1f, 0f);
            _trailMaterial = new Material(Shader.Find("Sprites/Default"));
            _trail.sharedMaterial = _trailMaterial;
        }

        public void BeginDash()
        {
            if (_trail == null) return;
            _trail.Clear();
            _trail.AddPosition(TrailPoint(transform.position));
        }

        public void RecordMovement(Vector3 from, Vector3 to)
        {
            if (_trail != null && (to - from).sqrMagnitude > 0.000001f) _trail.AddPosition(TrailPoint(to));
        }

        public void ClearTrail() { if (_trail != null) _trail.Clear(); }
        private static Vector3 TrailPoint(Vector3 body) => body + Vector3.down * 0.82f;

        private void Update()
        {
            if (_player == null || !RaidCombatAuthority.Local(this)) return;
            if (_slot == null)
            {
                if (Time.unscaledTime < _nextHotbarSearch) return;
                _nextHotbarSearch = Time.unscaledTime + 0.5f;
                SkillHotbarUI hotbar = FindFirstObjectByType<SkillHotbarUI>();
                if (hotbar == null) return;
                CreateSlot(hotbar.transform);
            }

            _key.text = LocalInputBindings.Label("Dash") + " 대쉬";
            bool ready = _player.CanDash;
            _background.color = ready ? new Color(0.13f, 0.32f, 0.38f, 0.96f) : new Color(0.12f, 0.14f, 0.18f, 0.96f);
            _overlay.anchorMax = new Vector2(1, _player.DashCooldownFraction);
            _state.text = _player.IsDashing ? "대쉬 중" : _player.DashCooldownRemaining > 0
                ? $"{_player.DashCooldownRemaining:0.0}s" : ready ? "사용 가능" : "사용 불가";
            _state.color = ready ? new Color(0.65f, 1f, 0.92f) : Color.white;
            DashSettings settings = _player.dashSettings;
            _details.text = settings != null ? $"{settings.distance:0.#}m / {settings.duration:0.##}초\nGCD 별도" : "설정 없음";
        }

        private void CreateSlot(Transform hotbar)
        {
            RectTransform rect = Rect("CommonDash_Slot", hotbar);
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(12, 36);
            rect.sizeDelta = new Vector2(126, 124);
            _slot = rect.gameObject;
            _background = _slot.AddComponent<Image>();
            // It is a read-only keyboard slot, but still blocks clicks from reaching the battlefield.
            _background.raycastTarget = true;
            Outline outline = _slot.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.7f, 0.8f, 0.75f);
            outline.effectDistance = new Vector2(1, -1);
            _overlay = Rect("CooldownOverlay", rect);
            _overlay.pivot = Vector2.zero;
            Image shade = _overlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.6f);
            shade.raycastTarget = false;
            _key = Label("Key", rect, "", 18, new Vector2(0, 0.72f), Vector2.one);
            _state = Label("State", rect, "사용 가능", 21, new Vector2(0, 0.36f), new Vector2(1, 0.72f));
            _details = Label("Details", rect, "", 12, Vector2.zero, new Vector2(1, 0.36f));
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text Label(string name, Transform parent, string value, int size, Vector2 min, Vector2 max)
        {
            RectTransform rect = Rect(name, parent);
            rect.anchorMin = min; rect.anchorMax = max;
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = value;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private void OnDestroy()
        {
            if (_slot != null) Destroy(_slot);
            if (_trailMaterial != null) Destroy(_trailMaterial);
        }
    }
}
