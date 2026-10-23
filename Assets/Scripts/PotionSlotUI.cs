using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Added only to the human player's CommonPotion; follows the existing hotbar in either view.
    public sealed class PotionSlotUI : MonoBehaviour
    {
        private CommonPotion _potion;
        private GameObject _slot;
        private Image _background;
        private RectTransform _overlay;
        private Text _status;
        private Text _description;
        private float _nextSearch;

        private void Awake() { _potion = GetComponent<CommonPotion>(); }
        private void Update()
        {
            if (_potion == null) return;
            if (_slot == null)
            {
                if (Time.unscaledTime < _nextSearch) return;
                _nextSearch = Time.unscaledTime + 0.5f;
                var hotbar = FindFirstObjectByType<SkillHotbarUI>();
                if (hotbar == null) return;
                Build(hotbar.transform);
            }
            bool ready = _potion.CanUse;
            _description.text = $"최대 HP {(_potion.Settings != null ? _potion.Settings.healFraction * 100 : 0):0.#}%\n즉시 회복 · GCD 별도";
            _background.color = ready ? new Color(0.12f, 0.32f, 0.2f, 0.96f) : new Color(0.12f, 0.14f, 0.18f, 0.96f);
            _overlay.anchorMax = new Vector2(1, _potion.CooldownFraction);
            _status.text = _potion.CooldownRemaining > 0 ? $"{_potion.CooldownRemaining:0.0}s" :
                !_potion.IsAlive ? "전투 불능" : _potion.CurrentHp >= _potion.MaximumHp ? "HP 가득 참" : ready ? "사용 가능" : "사용 불가";
            _status.color = ready ? new Color(0.65f, 1f, 0.75f) : Color.white;
        }

        private void Build(Transform hotbar)
        {
            var root = Rect("CommonPotion_Slot", hotbar);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(1, 0);
            root.anchoredPosition = new Vector2(-12, 36); // Q left, SPACE right: fits narrower views without overlapping skills.
            root.sizeDelta = new Vector2(126, 124);
            _slot = root.gameObject;
            _background = _slot.AddComponent<Image>();
            _background.raycastTarget = true;
            var outline = _slot.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.8f, 0.5f, 0.75f);
            outline.effectDistance = new Vector2(1, -1);
            _overlay = Rect("CooldownOverlay", root);
            _overlay.pivot = Vector2.zero;
            var shade = _overlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.6f);
            shade.raycastTarget = false;
            Label(root, "Key", "Q 포션", 18, new Vector2(0, 0.72f), Vector2.one);
            _status = Label(root, "Status", "", 18, new Vector2(0, 0.36f), new Vector2(1, 0.72f));
            _description = Label(root, "Description", "", 12, Vector2.zero, new Vector2(1, 0.36f));
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text Label(Transform parent, string name, string value, int size, Vector2 min, Vector2 max)
        {
            var rect = Rect(name, parent);
            rect.anchorMin = min; rect.anchorMax = max;
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = value;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private void OnDestroy() { if (_slot != null) Destroy(_slot); }
    }
}
