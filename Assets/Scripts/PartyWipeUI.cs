using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    public sealed class PartyWipeUI : MonoBehaviour
    {
        private StageManager _stage;
        private GameObject _root, _buttons;
        private Text _title, _details, _retryCaption;
        private Button _retry;
        private GameObject _connection;
        private void Awake() { _stage = GetComponent<StageManager>(); }
        private bool Build()
        {
            if (_root != null && RaidUIView.For(this) != null) return true;
            var canvas = _stage.stageHeaderText != null ? _stage.stageHeaderText.GetComponentInParent<Canvas>() : FindFirstObjectByType<Canvas>();
            if (canvas == null) return false;
            if (RaidUIView.Attach(this, "wipe", canvas.transform)) { BindActions(); return true; }
            var viewParent = RaidUIView.Begin(this, "wipe", canvas.transform);
            _root = new GameObject("PartyWipe_Overlay", typeof(RectTransform), typeof(Image));
            var overlay = _root.GetComponent<RectTransform>(); overlay.SetParent(viewParent, false);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.04f, 0.88f);
            _title = TextAt(overlay, "Title", new Vector2(0, 100), new Vector2(780, 80), 40);
            _details = TextAt(overlay, "Details", new Vector2(0, 15), new Vector2(850, 80), 23);
            _buttons = new GameObject("Actions", typeof(RectTransform));
            _buttons.transform.SetParent(overlay, false);
            ButtonAt(_buttons.transform, "이번 시도 전투 기록", new Vector2(-175, -100), () => GetComponent<CombatMetricsUI>()?.ShowFailedResult());
            _retry = ButtonAt(_buttons.transform, "현재 보스 재도전", new Vector2(175, -100), _stage.RetryCurrentBoss);
            _connection = ButtonAt(_buttons.transform, "메인 메뉴로", new Vector2(0, -180), () => RaidNetworkSession.Instance?.Disconnect()).gameObject;
            _retryCaption=_retry.GetComponentInChildren<Text>();
            return true;
        }
        private void BindActions()
        {
            RaidUIView.Bind(this, "이번 시도 전투 기록", () => GetComponent<CombatMetricsUI>()?.ShowFailedResult());
            RaidUIView.Bind(this, "현재 보스 재도전", _stage.RetryCurrentBoss);
            RaidUIView.Bind(this, "메인 메뉴로", () => RaidNetworkSession.Instance?.Disconnect());
        }
        public void ShowWipe()
        {
            if (!Build()) return;
            _root.SetActive(true); RaidUIOrder.Raise(_root.transform); _buttons.SetActive(true);
            _retry.interactable = !RaidCombatAuthority.Active || RaidCombatAuthority.Host;
            _retryCaption.text = _retry.interactable ? "현재 보스 재도전" : "호스트 재도전 대기";
            _connection.SetActive(true);
            _title.text = "PARTY WIPED";
            RaidUIStyle.Color(_title, new Color(1f, 0.4f, 0.4f));
            _details.text = $"STAGE {_stage.currentStage} / {_stage.maxStage} · 파티 전멸\n재도전: 현재 빌드 유지 · 공유 부활 4회. 이번 시도 전투 기록에서 피해를 확인하세요.";
        }
        public void ShowCountdown(int seconds)
        {
            if (!Build()) return;
            _root.SetActive(true); RaidUIOrder.Raise(_root.transform); _buttons.SetActive(false);
            RaidUIStyle.Color(_title, Color.white); _title.text = $"전투 준비 · {seconds}";
            _details.text = $"STAGE {_stage.currentStage} · 잠시 후 전투가 시작됩니다";
        }
        public void ShowNetworkLoading()
        {
            if (!Build()) return;
            _root.SetActive(true); RaidUIOrder.Raise(_root.transform); _buttons.SetActive(false);
            RaidUIStyle.Color(_title, Color.white); _title.text = "파티 전투 준비 중";
            _details.text = RaidNetworkSession.Instance?.FlowStatus ?? "모든 참가자의 전장 로딩을 기다립니다";
        }
        public void Hide() { if (_root != null) _root.SetActive(false); }
        private static Text TextAt(Transform parent, string name, Vector2 pos, Vector2 size, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.sizeDelta = size; rect.anchoredPosition = pos;
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; RaidUIStyle.Color(text, Color.white); text.raycastTarget = false;
            return text;
        }
        private Button ButtonAt(Transform parent, string caption, Vector2 pos, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.sizeDelta = new Vector2(320, 60); rect.anchoredPosition = pos;
            go.GetComponent<Image>().color = new Color(0.15f, 0.3f, 0.4f);
            go.GetComponent<Button>().targetGraphic = go.GetComponent<Image>();
            RaidUIView.Bind(this, caption, action, go.GetComponent<Button>());
            TextAt(rect, "Label", Vector2.zero, rect.sizeDelta, 21).text = caption;
            return go.GetComponent<Button>();
        }
        private void OnDestroy() { if (_root != null) Destroy(_root); }
    }
}
