using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    public sealed class ResurrectionSlotUI : MonoBehaviour
    {
        private BattleResurrection _skill;
        private GameObject _root;
        private Text _label;
        private Image _fill;
        private void Awake() { _skill = GetComponent<BattleResurrection>(); }
        private void Update()
        {
            if (_root == null)
            {
                var hotbar = FindFirstObjectByType<SkillHotbarUI>(); if (hotbar == null) return;
                _root = new GameObject("Healer_ResurrectionSlot", typeof(RectTransform), typeof(Image));
                var rect = _root.GetComponent<RectTransform>(); rect.SetParent(hotbar.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(0, 0);
                rect.anchoredPosition = new Vector2(12, 8); rect.sizeDelta = new Vector2(180, 70);
                _root.GetComponent<Image>().color = new Color(0.08f, 0.24f, 0.22f, 0.95f);
                var bar = new GameObject("CastProgress", typeof(RectTransform), typeof(Image)); bar.transform.SetParent(rect, false);
                _fill = bar.GetComponent<Image>(); _fill.color = new Color(0.3f, 0.8f, 0.6f, 0.45f); _fill.raycastTarget = false;
                var text = new GameObject("Text", typeof(RectTransform), typeof(Text)); text.transform.SetParent(rect, false);
                var tr = text.GetComponent<RectTransform>(); tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
                _label = text.GetComponent<Text>(); _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _label.fontSize = 14; _label.alignment = TextAnchor.MiddleCenter; _label.color = Color.white; _label.raycastTarget = false;
                var button = _root.AddComponent<Button>(); button.targetGraphic = _root.GetComponent<Image>(); button.onClick.AddListener(() => _skill.TryBegin(TargetingManager.Instance?.CurrentTarget));
            }
            _root.SetActive(true);
            _label.text = $"[{LocalInputBindings.Label("Skill5")}] 이리스의 구원\n이번 도전 부활 {StageManager.Instance?.IrisCoins??0} / {IrisRescueState.DefaultCoins}";
            var fill = _fill.rectTransform; fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(_skill.IsCasting ? _skill.Progress : 0, 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
        }
        private void OnDestroy() { if (_root != null) Destroy(_root); }
    }
}
