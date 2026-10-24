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
        private Button _rescue, _previous, _next;
        private RectTransform _deathPanel;
        private Text _deathText;
        private float _nextRefresh;
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
                _rescue = _root.AddComponent<Button>(); _rescue.targetGraphic = _root.GetComponent<Image>(); _rescue.onClick.AddListener(() => _skill.TryBegin(TargetingManager.Instance?.CurrentTarget));
                BuildDeathPanel(hotbar.GetComponentInParent<Canvas>().transform);
            }
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .15f;
            bool dead = GetComponent<PlayerController>()?.IsDead == true;
            var target = dead ? transform : TargetingManager.Instance?.CurrentTarget;
            string reason = IrisRescue.Unavailable(transform, target);
            bool blocked = LocalGameplayInput.Blocked;
            _root.SetActive(true);
            _rescue.interactable = !blocked && reason.Length == 0;
            _label.text = $"[{LocalInputBindings.Label("Skill5")}] 이리스의 구원\n이번 도전 부활 {StageManager.Instance?.IrisCoins??0} / {IrisRescueState.DefaultCoins}";
            var fill = _fill.rectTransform; fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(_skill.IsCasting ? _skill.Progress : 0, 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
            bool show = dead && !StageManager.CombatSuspended && !LocalGameplayInput.ModalCovered;
            _deathPanel.gameObject.SetActive(show);
            var camera = QuarterViewCamera.LocalCamera;
            if (!dead || StageManager.CombatSuspended) { camera?.Spectate(null); return; }
            if (!show) return;
            var members = PartyLife.OrderedMembers();
            int living = members.FindAll(CombatRange.LivingPartyMember).Count;
            if (camera != null && !CombatRange.LivingPartyMember(camera.SpectatorTarget)) CycleSpectator(1);
            _previous.interactable = _next.interactable = living > 1;
            string watching = camera?.SpectatorTarget != null
                ? camera.SpectatorTarget.GetComponent<CharacterActor>()?.Definition.name ?? camera.SpectatorTarget.name : "살아 있는 파티원 없음";
            var combat = GetComponent<PlayerCombatController>();
            string result = combat != null && combat.HasRangeFeedback ? combat.RangeFeedback : "";
            _deathText.text = $"전투 불능 · 이번 도전 부활 {StageManager.Instance?.IrisCoins ?? 0} / 4\n" +
                (reason.Length == 0 ? $"[{LocalInputBindings.Label("Skill5")}] 이리스에게 내 구조 요청" : reason) +
                "\n관전: " + watching + " · 보기만 변경\n" + result;
        }
        private void BuildDeathPanel(Transform canvas)
        {
            var go = new GameObject("PartyDeath_Status", typeof(RectTransform), typeof(Image));
            _deathPanel = go.GetComponent<RectTransform>(); _deathPanel.SetParent(canvas, false);
            _deathPanel.anchorMin = _deathPanel.anchorMax = new Vector2(.5f, .65f);
            _deathPanel.sizeDelta = new Vector2(650, 165);
            go.GetComponent<Image>().color = new Color(.025f, .035f, .06f, .94f);
            var text = new GameObject("Status", typeof(RectTransform), typeof(Text));
            text.transform.SetParent(_deathPanel, false); _deathText = text.GetComponent<Text>();
            _deathText.rectTransform.sizeDelta = new Vector2(630, 105); _deathText.rectTransform.anchoredPosition = new Vector2(0, 24);
            _deathText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); _deathText.fontSize = 18;
            _deathText.alignment = TextAnchor.MiddleCenter; _deathText.color = Color.white; _deathText.raycastTarget = false;
            _previous = SpectatorButton("이전 파티원", -160, -1);
            _next = SpectatorButton("다음 파티원", 160, 1);
            go.SetActive(false);
        }
        private Button SpectatorButton(string caption, float x, int step)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(_deathPanel, false);
            rect.sizeDelta = new Vector2(280, 38); rect.anchoredPosition = new Vector2(x, -57);
            go.GetComponent<Image>().color = new Color(.15f, .22f, .3f, 1);
            var button = go.GetComponent<Button>(); button.onClick.AddListener(() => CycleSpectator(step));
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)); label.transform.SetParent(rect, false);
            var t = label.GetComponent<Text>(); t.rectTransform.sizeDelta = rect.sizeDelta;
            t.font = _deathText.font; t.fontSize = 16; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.text = caption; t.raycastTarget = false;
            return button;
        }
        private void CycleSpectator(int step)
        {
            if (GetComponent<PlayerController>()?.IsDead != true || StageManager.CombatSuspended || LocalGameplayInput.ModalCovered) return;
            var camera = QuarterViewCamera.LocalCamera; if (camera == null) return;
            var living = PartyLife.OrderedMembers().FindAll(CombatRange.LivingPartyMember);
            int current = living.IndexOf(camera.SpectatorTarget);
            int next = CameraControlMath.SpectatorIndex(current, living.Count, step);
            camera.Spectate(next < 0 ? null : living[next]);
            LocalGameplayInput.SuppressPointerThisFrame();
        }
        private void OnDestroy()
        {
            QuarterViewCamera.LocalCamera?.Spectate(null);
            if (_root != null) Destroy(_root);
            if (_deathPanel != null) Destroy(_deathPanel.gameObject);
        }
    }
}
