using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    [DisallowMultipleComponent]
    public sealed class PlayerAttackRangeDisplay : MonoBehaviour
    {
        public bool showRange = true;
        private PlayerCombatController _combat;
        private LineRenderer _ring;
        private Material _material;
        private Button _settingsButton;
        private Text _settingsText;
        private Text _feedback;
        private const int Segments = 96;

        private void Awake()
        {
            _combat = GetComponent<PlayerCombatController>();
            var root = new GameObject("Player_MeleeReach_ThinRing");
            root.transform.SetParent(transform, false);
            _ring = root.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = true;
            _ring.positionCount = Segments;
            _ring.startWidth = _ring.endWidth = 0.035f;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            _ring.sortingOrder = 1;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            _material = new Material(shader);
            _ring.sharedMaterial = _material;
            _ring.enabled = false;
        }

        private void LateUpdate()
        {
            if (_combat == null) return;
            if (_settingsButton == null) BuildSettingsUI();
            bool melee = _combat.IsMeleeRole && CombatRange.LivingPartyMember(transform);
            _ring.enabled = melee && showRange;
            if (_ring.enabled)
            {
                float range = _combat.PrimaryMeleeRange;
                float radius = CombatRange.RingRadius(transform, range);
                Transform target = _combat.GetOffensiveTarget();
                bool inRange = CombatRange.LivingEnemy(target) && CombatRange.InRange(transform, target, range);
                Color color = inRange ? new Color(0.2f, 0.9f, 0.8f, 0.5f) : new Color(0.6f, 0.65f, 0.7f, 0.4f);
                _ring.startColor = _ring.endColor = color;
                for (int i = 0; i < Segments; i++)
                {
                    float radians = i * (Mathf.PI * 2f / Segments);
                    _ring.SetPosition(i, new Vector3(transform.position.x + Mathf.Sin(radians) * radius, 0.08f,
                        transform.position.z + Mathf.Cos(radians) * radius));
                }
            }
            if (_feedback != null) _feedback.text = _combat.HasRangeFeedback ? _combat.RangeFeedback : "";
        }

        private void BuildSettingsUI()
        {
            Canvas canvas = _combat.hotbarUI != null ? _combat.hotbarUI.GetComponentInParent<Canvas>() : null;
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("AttackRange_SettingsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-24, -138);
            rect.sizeDelta = new Vector2(310, 34);
            root.GetComponent<Image>().color = new Color(0.08f, 0.18f, 0.22f, 0.92f);
            _settingsButton = root.GetComponent<Button>();
            _settingsButton.targetGraphic = root.GetComponent<Image>();
            _settingsText = CreateLabel(root.transform, font, 17);
            _settingsText.rectTransform.anchorMin = Vector2.zero;
            _settingsText.rectTransform.anchorMax = Vector2.one;
            _settingsText.rectTransform.offsetMin = _settingsText.rectTransform.offsetMax = Vector2.zero;
            _settingsButton.onClick.AddListener(() =>
            {
                if (CombatMetricsUI.BlocksUnderlyingInput) return;
                showRange = !showRange;
                RefreshButton();
            });
            RefreshButton();
            _feedback = CreateLabel(canvas.transform, font, 22);
            _feedback.name = "MeleeRange_Feedback";
            _feedback.color = new Color(1, 0.75f, 0.45f);
            var feedbackRect = _feedback.rectTransform;
            feedbackRect.anchorMin = feedbackRect.anchorMax = new Vector2(0.5f, 1);
            feedbackRect.anchoredPosition = new Vector2(0, -310);
            feedbackRect.sizeDelta = new Vector2(650, 32);
            // Existing role/reward/result overlays stay above this small settings control.
            rect.SetAsFirstSibling();
            feedbackRect.SetAsFirstSibling();
        }

        private void RefreshButton() => _settingsText.text = "공격 범위 표시: " + (showRange ? "켜짐" : "꺼짐");

        private static Text CreateLabel(Transform parent, Font font, int size)
        {
            var obj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        private void OnDisable() { if (_ring != null) _ring.enabled = false; }
        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_settingsButton != null) Destroy(_settingsButton.gameObject);
            if (_feedback != null) Destroy(_feedback.gameObject);
        }
    }
}
