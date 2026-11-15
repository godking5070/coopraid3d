using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    // Local runtime UI; no scene or Inspector wiring required.
    public sealed class LocalCameraSettingsUI : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        private QuarterViewCamera _camera;
        private GameObject _buttonRoot;
        private GameObject _modal;
        private Font _font;
        private Slider _rotation, _zoom3D, _zoom2D;
        private Text _rotationLabel, _zoom3DLabel, _zoom2DLabel, _invertLabel;
        private float _previousTimeScale;
        private bool _open;

        private void Awake() { _camera = GetComponent<QuarterViewCamera>(); }
        private void Update()
        {
            if (_buttonRoot == null) Build();
            if (!_open) return;
            bool escape = false;
#if ENABLE_INPUT_SYSTEM
            escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            escape = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (escape) Close();
        }

        private void Build()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Button open = MakeButton(canvas.transform, "Camera_Settings", "카메라 설정", new Vector2(310, 34), Vector2.zero);
            _buttonRoot = open.gameObject;
            var rect = open.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-24, -180);
            rect.SetAsFirstSibling();
            open.onClick.AddListener(Open);

            _modal = new GameObject("Camera_SettingsModal", typeof(RectTransform), typeof(Image));
            RectTransform overlay = _modal.GetComponent<RectTransform>();
            overlay.SetParent(canvas.transform, false);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _modal.GetComponent<Image>().color = new Color(0, 0, 0, 0.68f);
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(overlay, false);
            panelRect.sizeDelta = new Vector2(560, 430);
            panel.GetComponent<Image>().color = new Color(0.07f, 0.12f, 0.18f, 1);
            Label(panelRect, "카메라 설정 · 내 플레이어", -30, 23);
            _rotation = MakeSlider(panelRect, -90, 0.03f, 0.6f, out _rotationLabel);
            _rotation.onValueChanged.AddListener(value => { _camera.preferences.rotationSensitivity = value; Changed(); });
            var invert = MakeButton(panelRect, "InvertVertical", "", new Vector2(460, 36), new Vector2(0, 46));
            _invertLabel = invert.GetComponentInChildren<Text>();
            invert.onClick.AddListener(() => { _camera.preferences.invertVertical = !_camera.preferences.invertVertical; Changed(); });
            _zoom3D = MakeSlider(panelRect, -222, 0.25f, 5f, out _zoom3DLabel);
            _zoom3D.onValueChanged.AddListener(value => { _camera.preferences.zoomSensitivity3D = value; Changed(); });
            _zoom2D = MakeSlider(panelRect, -292, 0.25f, 5f, out _zoom2DLabel);
            _zoom2D.onValueChanged.AddListener(value => { _camera.preferences.zoomSensitivity2D = value; Changed(); });
            var reset = MakeButton(panelRect, "Reset", "기본 카메라로 복원", new Vector2(250, 42), new Vector2(-115, -160));
            reset.onClick.AddListener(() => { _camera.ResetCameraDefaults(); Refresh(); });
            var close = MakeButton(panelRect, "Close", "닫기 [Esc]", new Vector2(170, 42), new Vector2(155, -160));
            close.onClick.AddListener(Close);
            _modal.SetActive(false);
        }

        private void Open()
        {
            if (LocalGameplayInput.Blocked || _modal == null) return;
            _camera.ReleaseOrbit();
            _previousTimeScale = Time.timeScale;
            _open = IsOpen = true;
            LocalGameplayInput.SuppressThisFrame();
            Time.timeScale = 0;
            Refresh();
            _modal.SetActive(true);
            _modal.transform.SetAsLastSibling();
        }
        public static void CloseForSessionEnd()
        { foreach (var ui in Object.FindObjectsByType<LocalCameraSettingsUI>(FindObjectsSortMode.None)) ui.Close(); }

        private void Close()
        {
            if (!_open) return;
            _open = IsOpen = false;
            LocalGameplayInput.SuppressThisFrame();
            LocalGameplayInput.SuppressPointerThisFrame();
            if (_modal != null) _modal.SetActive(false);
            Time.timeScale = _previousTimeScale;
            _camera.SaveLocalPreferences();
            PlayerPrefs.Save();
        }
        private void Changed() { Refresh(); _camera.SaveLocalPreferences(); }
        private void Refresh()
        {
            var p = _camera.preferences;
            _rotation.SetValueWithoutNotify(p.rotationSensitivity);
            _zoom3D.SetValueWithoutNotify(p.zoomSensitivity3D);
            _zoom2D.SetValueWithoutNotify(p.zoomSensitivity2D);
            _rotationLabel.text = $"3D 마우스 회전 감도   {p.rotationSensitivity:F2}";
            _invertLabel.text = "3D 상하 회전 반전: " + (p.invertVertical ? "켜짐" : "꺼짐");
            _zoom3DLabel.text = $"3D 줌 감도   {p.zoomSensitivity3D:F2}";
            _zoom2DLabel.text = $"2D 줌 감도   {p.zoomSensitivity2D:F2}";
        }
        private Text Label(Transform parent, string value, float y, int size = 19)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, y); rect.sizeDelta = new Vector2(490, 32);
            var text = go.GetComponent<Text>(); text.text = value; text.font = _font; text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false;
            return text;
        }
        private Button MakeButton(Transform parent, string name, string text, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.sizeDelta = size; rect.anchoredPosition = position;
            go.GetComponent<Image>().color = new Color(0.14f, 0.28f, 0.36f);
            var button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var label = Label(rect, text, 0, 18);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }
        private Slider MakeSlider(Transform parent, float y, float min, float max, out Text label)
        {
            label = Label(parent, "", y);
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, y - 27); rect.sizeDelta = new Vector2(440, 20);
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            var trackRect = track.GetComponent<RectTransform>(); trackRect.SetParent(rect, false);
            trackRect.anchorMin = new Vector2(0, 0.35f); trackRect.anchorMax = new Vector2(1, 0.65f);
            trackRect.offsetMin = trackRect.offsetMax = Vector2.zero;
            track.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.4f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var handleRect = handle.GetComponent<RectTransform>(); handleRect.SetParent(rect, false);
            handleRect.sizeDelta = new Vector2(18, 24);
            handle.GetComponent<Image>().color = new Color(0.3f, 0.9f, 0.8f);
            var slider = go.GetComponent<Slider>(); slider.minValue = min; slider.maxValue = max;
            slider.handleRect = handleRect; slider.targetGraphic = handle.GetComponent<Image>();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }
        private void OnDisable() { Close(); }
        private void OnDestroy()
        {
            Close();
            if (_buttonRoot != null) Destroy(_buttonRoot);
            if (_modal != null) Destroy(_modal);
        }
    }
}
