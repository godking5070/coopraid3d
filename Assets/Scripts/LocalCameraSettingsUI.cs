using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidRogue
{
    // The former camera-only window is the single settings UI. It is reachable above the main menu.
    [DefaultExecutionOrder(-300)]
    public sealed class LocalCameraSettingsUI : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        private QuarterViewCamera _camera;
        private Canvas _canvas;
        private GameObject _buttonRoot, _modal, _confirmation, _conflict;
        private RectTransform _panel;
        private Font _font;
        private Text _status, _confirmText, _conflictText, _openLabel;
        private readonly List<Action> _refresh = new List<Action>();
        private readonly List<GameObject> _tabs = new List<GameObject>();
        private LocalSettingsData _draft;
        private LocalCameraPreferences _cameraBefore;
        private bool _open, _pausedSingle, _wasConfirming, _overlayWasOpen;
        private float _previousTimeScale;
        private int _tab, _captureRow = -1, _captureSlot;
        private string _pendingKey;
        private LocalSettingsRuntime Settings => LocalSettingsRuntime.Instance;
        private void Awake() { _camera = GetComponent<QuarterViewCamera>(); }
        private void Update()
        {
            if (_buttonRoot == null) Build();
            if (_buttonRoot == null || Settings == null) return;
            _openLabel.text = "설정 [" + LocalInputBindings.Label("Settings") + "]" + (Settings.Applied.bindingsNeedReview ? " · 키 충돌 확인" : "");
            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !LocalGameplayInput.EscapeConsumed;
            if (RaidSteamRuntime.Instance?.OverlayOpen == true)
            {
                if (!_overlayWasOpen) { CancelCapture(); _camera.ReleaseOrbit(); }
                _overlayWasOpen = true; return;
            }
            if (_overlayWasOpen) { _overlayWasOpen = false; LocalInputBindings.QuarantineHeld(); return; }
            if (!Application.isFocused) return;
            if (RaidSteamRuntime.Instance?.ConfirmInvite == true)
            { if (escape) { Consume(); RaidSteamRuntime.Instance.AnswerInvite(false); } return; }
            if (_open)
            {
                bool pending = Settings.Confirmation.Pending;
                _confirmation.SetActive(pending); if (pending) _confirmation.transform.SetAsLastSibling();
                if (pending) _confirmText.text = "이 화면 설정을 유지할까요?\n" + Math.Max(0, Math.Ceiling(Settings.Confirmation.Deadline - Time.realtimeSinceStartupAsDouble)) + "초 후 자동 복원";
                if (_wasConfirming && !pending)
                { CopyDisplay(Settings.LastRevertedDisplay ?? Settings.Applied, _draft); _status.text = Settings.Notice; Refresh(); }
                _wasConfirming = pending;
                if (escape)
                {
                    Consume();
                    if (pending) Settings.RevertDisplay();
                    else if (_captureRow >= 0 || _pendingKey != null) CancelCapture();
                    else Cancel();
                    return;
                }
                if (_captureRow >= 0 && _pendingKey == null && Application.isFocused) CaptureKey();
                if (!LocalInputBindings.Capturing && LocalInputBindings.Pressed("Settings")) Cancel();
                return;
            }
            if (escape) { Consume(); CloseTopWindow(); return; }
            if (LocalGameplayInput.IsTyping || !Application.isFocused || RaidNetworkSession.Instance?.Ending == true) return;
            if (LocalInputBindings.Pressed("Settings")) Open();
            else if(AegisRouteSelectionUI.IsOpen)
            {if(LocalInputBindings.Pressed("Menu")){Consume();AegisRouteSelectionUI.Close();}return;}
            else if (LocalInputBindings.Pressed("Menu")) { Consume(); ToggleMenu(); }
            else if (LocalInputBindings.Pressed("Results") && RaidNetworkSession.Instance?.MenuOpen != true)
            {
                var metrics = FindFirstObjectByType<CombatMetricsUI>();
                if (CombatMetricsUI.ResultsOpen) metrics?.CloseResults();
                else if (StageManager.Instance?.IsWiped == true) metrics?.ShowFailedResult();
                else metrics?.ShowResults(StageManager.Instance?.Progress.Phase == RaidBattlePhase.Victory || RaidNetworkSession.Instance?.BattlePhase == RaidBattlePhase.Victory);
                Consume();
            }
        }
        private static void Consume() { LocalGameplayInput.ConsumeEscape(); LocalInputBindings.QuarantineHeld(); }
        private void CloseTopWindow()
        {
            if(AegisRouteSelectionUI.IsOpen){AegisRouteSelectionUI.Close();return;}
            var session = RaidNetworkSession.Instance;
            if (session?.MenuOpen == true)
            {
                if (session.IsRecovering) return; // Recovery buttons retain their explicit leave/cancel semantics.
                if (RaidSteamRuntime.Instance?.Busy == true) RaidSteamRuntime.Instance.CancelJoin();
                else if (session.Mode == RaidSessionMode.Connecting) session.Disconnect();
                else if (session.Mode == RaidSessionMode.Single || session.ControlsReady && session.HasLocalAvatar) session.MenuOpen = false;
                return;
            }
            if (CombatMetricsUI.ResultsOpen) { FindFirstObjectByType<CombatMetricsUI>()?.CloseResults(); return; }
            var roles = RoleSelectionManager.Instance;
            if (roles != null && roles.IsRoleModalOpen)
            {
                if (StageManager.Instance?.CanSelectInitialRole == true) { if (session != null) session.MenuOpen = true; }
                else roles.CloseRoleSelectionModal();
                return;
            }
            if (_camera.IsOrbitDragging) { _camera.ReleaseOrbit(); return; }
            if (session != null) session.MenuOpen = true;
        }
        private static void ToggleMenu()
        {
            var session = RaidNetworkSession.Instance; if (session == null || session.Ending || session.IsRecovering) return;
            if (!session.MenuOpen || session.Mode == RaidSessionMode.Single || session.HasLocalAvatar) session.MenuOpen = !session.MenuOpen;
        }
        private void Build()
        {
            _canvas = FindFirstObjectByType<Canvas>(); if (_canvas == null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var open = ButtonAt(_canvas.transform, "Settings", "설정", new Vector2(0, 0), new Vector2(230, 36), Open);
            _buttonRoot = open.gameObject; _openLabel = open.GetComponentInChildren<Text>();
            var rect = open.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1); rect.anchoredPosition = new Vector2(-24, -180);
            _modal = Box(_canvas.transform, "LocalSettingsModal", Vector2.zero, Vector2.zero, new Color(0, 0, 0, .8f)).gameObject;
            Stretch(_modal.GetComponent<RectTransform>());
            _panel = Box(_modal.transform, "Panel", Vector2.zero, new Vector2(920, 720), new Color(.055f, .085f, .12f));
            Label(_panel, "설정 · 이 기기의 사용자 선호값", new Vector2(0, 322), new Vector2(860, 36), 25);
            string[] names = { "화면", "사운드", "조작", "카메라" };
            for (int i = 0; i < 4; i++) { int tab = i; ButtonAt(_panel, names[i], names[i], new Vector2(-330 + 220 * i, 268), new Vector2(208, 40), () => SelectTab(tab)); }
            _status = Label(_panel, "", new Vector2(0, -266), new Vector2(865, 68), 16);
            ButtonAt(_panel, "Apply", "적용", new Vector2(-285, -325), new Vector2(190, 44), Apply);
            ButtonAt(_panel, "Cancel", "취소 [Esc]", new Vector2(-65, -325), new Vector2(210, 44), Cancel);
            ButtonAt(_panel, "Defaults", "이 탭 기본값 복원", new Vector2(220, -325), new Vector2(300, 44), Defaults);
            BuildConfirmation(); _modal.SetActive(false);
        }
        public void Open()
        {
            if (_open || Settings == null || _modal == null || RaidNetworkSession.Instance?.Ending == true || RaidSteamRuntime.Instance?.OverlayOpen == true || RaidSteamRuntime.Instance?.ConfirmInvite == true) return;
            _draft = Settings.Applied.Copy(); Settings.CaptureCurrentDisplay(_draft); Settings.ResolveDisplay(_draft); _cameraBefore = _camera.CaptureLocalState();
            _camera.ReleaseOrbit(); _open = IsOpen = true; LocalInputBindings.QuarantineHeld();
            _previousTimeScale = Time.timeScale; _pausedSingle = RaidNetworkSession.Instance?.Mode == RaidSessionMode.Single;
            if (_pausedSingle) Time.timeScale = 0;
            _status.text = Settings.Notice; if (_draft.bindingsNeedReview) _tab = 2; RebuildTabs(); SelectTab(_tab); _modal.SetActive(true);
        }
        private void RebuildTabs()
        {
            foreach (var tab in _tabs) { tab.SetActive(false); Destroy(tab); } _tabs.Clear(); _refresh.Clear();
            BuildDisplay(Tab("Display", 430)); BuildAudio(Tab("Audio", 430)); BuildControls(Tab("Controls", 100 + LocalBindingRules.Actions.Length * 43)); BuildCamera(Tab("Camera", 515));
        }
        private RectTransform Tab(string name, float height)
        {
            var root = Box(_panel, name, new Vector2(0, 4), new Vector2(870, 460), new Color(.07f, .11f, .16f)); _tabs.Add(root.gameObject);
            var viewport = Rect(root, "Viewport", Vector2.zero, new Vector2(850, 450)); viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content", Vector2.zero, new Vector2(830, height)); content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1);
            var scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 28; scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }
        private void SelectTab(int tab)
        {
            CancelCapture(); _tab = tab;
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].SetActive(i == tab);
            Refresh();
        }
        private void BuildDisplay(RectTransform parent)
        {
            var resolutions = Settings.Resolutions();
            Row(parent, 30, "해상도", () => _draft.width + " × " + _draft.height, () =>
            { int index = resolutions.IndexOf(new Vector2Int(_draft.width, _draft.height)); var next = resolutions[(index + 1) % resolutions.Count]; _draft.width = next.x; _draft.height = next.y; Refresh(); }, Settings.CanChangeDisplay);
            Row(parent, 95, "화면 모드", () => _draft.windowMode == 0 ? "전체 화면" : _draft.windowMode == 1 ? "테두리 없는 창" : "창 모드", () =>
            { _draft.windowMode = (_draft.windowMode + 1) % 3; if (_draft.windowMode == 0 && !Settings.CanExclusive) _draft.windowMode = 1; Refresh(); }, Settings.CanChangeDisplay);
            Row(parent, 160, "수직 동기화", () => _draft.vSync ? "켜짐" : "꺼짐", () => { _draft.vSync = !_draft.vSync; Refresh(); }, Settings.CanVSync);
            int[] caps = { 0, 30, 60, 90, 120, 144, 165, 240, 360 };
            Row(parent, 225, "최대 프레임", () => _draft.frameLimit == 0 ? "제한 없음" : _draft.frameLimit + " FPS", () => { _draft.frameLimit = caps[(Array.IndexOf(caps, _draft.frameLimit) + 1) % caps.Length]; Refresh(); });
            LabelTop(parent, "데스크톱에서는 수직 동기화가 켜지면 화면 주사율이 우선하며 FPS 제한은 무시됩니다.\n버튼을 눌러 지원되는 값을 순환합니다. 화면 변경은 적용 후 15초 확인이 필요합니다.\n" + (Settings.CanChangeDisplay ? "해상도/모드는 이 디스플레이가 지원하는 항목만 표시합니다." : "이 실행 환경에서는 화면 해상도/모드 변경을 제공하지 않습니다. 테스트 빌드에서 확인하세요."), 310, 800, 104, 17);
        }
        private void BuildAudio(RectTransform parent)
        {
            Volume(parent, 30, "전체", () => _draft.master, v => _draft.master = v, LocalAudioBus.MasterPreview);
            Volume(parent, 120, "배경음", () => _draft.music, v => _draft.music = v, LocalAudioBus.Music);
            Volume(parent, 210, "효과음", () => _draft.effects, v => _draft.effects = v, LocalAudioBus.Effects);
            Volume(parent, 300, "UI", () => _draft.ui, v => _draft.ui = v, LocalAudioBus.UI);
            LabelTop(parent, "변경 즉시 미리듣기에 반영됩니다. 취소하면 이전 값으로 돌아갑니다.\n현재 음원 대신 짧은 테스트 톤을 사용합니다.", 388, 800, 46, 16);
        }
        private void Volume(RectTransform parent, float y, string name, Func<float> get, Action<float> set, LocalAudioBus bus)
        { SliderAt(parent, y, name, 0, 1, get, set); ButtonTop(parent, "미리듣기", 325, y + 24, 130, () => LocalAudioChannel.Preview(bus)); }
        private void BuildCamera(RectTransform parent)
        {
            SliderAt(parent, 25, "마우스 회전 감도", .03f, .6f, () => _draft.rotationSensitivity, v => _draft.rotationSensitivity = v);
            Row(parent, 105, "상하 회전 반전", () => _draft.invertVertical ? "켜짐" : "꺼짐", () => { _draft.invertVertical = !_draft.invertVertical; Preview(); });
            SliderAt(parent, 180, "3D 휠 줌 감도", .25f, 5, () => _draft.zoom3D, v => _draft.zoom3D = v);
            SliderAt(parent, 265, "2D 휠 줌 감도", .25f, 5, () => _draft.zoom2D, v => _draft.zoom2D = v);
            ButtonTop(parent, "기본 카메라 상태 복원", 0, 365, 350, () => { _camera.ResetCameraDefaults(); Settings.Preview(_draft); });
            Row(parent, 398, "근접 공격 범위 표시", () => _draft.showAttackRange ? "켜짐" : "꺼짐", () => { _draft.showAttackRange = !_draft.showAttackRange; Preview(); });
            LabelTop(parent, "감도/반전은 저장합니다. 현재 회전 각도와 줌 위치는 게임 중 상태이며 파일에 저장하지 않습니다.", 465, 800, 42, 15);
        }
        private void BuildControls(RectTransform parent)
        {
            LabelTop(parent, "스크롤하여 키 목록 확인 · 기본 / 보조 키 클릭 · Esc 취소 · 보조 키는 지울 수 있습니다.\n전투와 선택창은 키 공유 가능, 공통 창은 중복 불가. Esc/Enter/조합키와 마우스는 고정입니다.", 5, 800, 54, 16);
            for (int i = 0; i < _draft.bindings.Length; i++)
            {
                int row = i; var definition = LocalBindingRules.Find(_draft.bindings[i].action); float y = 85 + i * 43;
                var name = LabelTop(parent, definition.Name, y - 16, 330, 34, 17); name.rectTransform.anchoredPosition = new Vector2(-230, -y + 16);
                for (int slot = 0; slot < 2; slot++)
                {
                    int captured = slot; var button = ButtonTop(parent, "", slot == 0 ? 35 : 225, y, 170, () => BeginCapture(row, captured));
                    var label = button.GetComponentInChildren<Text>(); _refresh.Add(() => { string key = LocalBindingRules.Get(_draft.bindings[row], captured); bool conflict = LocalBindingRules.Conflicts(_draft.bindings, row, captured, key).Count > 0; label.text = LocalInputBindings.KeyLabel(key) + (conflict ? " ⚠ 충돌" : ""); label.color = conflict ? new Color(1f,.6f,.3f) : Color.white; });
                }
                ButtonTop(parent, "×", 355, y, 60, () => { LocalBindingRules.TryBind(_draft.bindings, row, 1, "", false); Refresh(); });
            }
        }
        private void BeginCapture(int row, int slot)
        {
            _captureRow = row; _captureSlot = slot; _pendingKey = null; LocalInputBindings.Capturing = true;
            LocalInputBindings.QuarantineHeld(); _status.text = LocalBindingRules.Find(_draft.bindings[row].action).Name + "에 사용할 키를 누르세요. [Esc] 취소";
        }
        private void CaptureKey()
        {
            if (Keyboard.current == null) return;
            foreach (var key in Keyboard.current.allKeys)
            {
                if (!key.wasPressedThisFrame || key.keyCode == Key.Escape) continue;
                string capturedKey = LocalBindingRules.CanonicalKey(key.keyCode.ToString());
                if (!LocalBindingRules.ValidKey(capturedKey) || LocalInputBindings.Modified)
                { _status.text = "이 키는 UI 복구/시스템 조작용입니다. 다른 단일 키를 선택하세요."; return; }
                var conflicts = LocalBindingRules.Conflicts(_draft.bindings, _captureRow, _captureSlot, capturedKey);
                if (conflicts.Count == 0) { LocalBindingRules.TryBind(_draft.bindings, _captureRow, _captureSlot, capturedKey, false); CancelCapture(); Refresh(); _status.text = "키 변경 준비 완료 · 적용을 눌러 저장하세요."; }
                else
                {
                    _pendingKey = capturedKey; var names = new List<string>(); foreach (var c in conflicts) names.Add(LocalBindingRules.Find(_draft.bindings[c.row].action).Name);
                    _conflictText.text = LocalInputBindings.KeyLabel(capturedKey) + " 충돌: " + string.Join(", ", names) + "\n기존 키와 교체하거나 다른 키를 선택하세요."; _conflict.SetActive(true); _conflict.transform.SetAsLastSibling();
                }
                LocalInputBindings.QuarantineHeld(); return;
            }
        }
        private void CancelCapture() { _captureRow = -1; _pendingKey = null; LocalInputBindings.Capturing = false; if (_conflict != null) _conflict.SetActive(false); LocalInputBindings.QuarantineHeld(); }
        private void BuildConfirmation()
        {
            _confirmation = Box(_panel, "DisplayConfirm", Vector2.zero, new Vector2(920, 720), new Color(.02f, .04f, .07f, .99f)).gameObject;
            _confirmText = Label(_confirmation.transform, "", new Vector2(0, 80), new Vector2(800, 110), 27);
            ButtonAt(_confirmation.transform, "Keep", "유지", new Vector2(-180, -80), new Vector2(300, 55), () => { if (Settings.KeepDisplay()) Close(true); else { _status.text = Settings.Notice; Refresh(); } });
            ButtonAt(_confirmation.transform, "Revert", "되돌리기", new Vector2(180, -80), new Vector2(300, 55), () => Settings.RevertDisplay());
            _confirmation.SetActive(false);
            _conflict = Box(_panel, "BindingConflict", Vector2.zero, new Vector2(920, 720), new Color(.02f, .04f, .07f, .99f)).gameObject;
            _conflictText = Label(_conflict.transform, "", new Vector2(0, 80), new Vector2(800, 145), 23);
            ButtonAt(_conflict.transform, "Swap", "기존 키와 교체", new Vector2(-180, -80), new Vector2(300, 55), () =>
            {
                if (LocalBindingRules.TryBind(_draft.bindings, _captureRow, _captureSlot, _pendingKey, true)) { CancelCapture(); Refresh(); _status.text = "키 교체 완료 · 적용을 눌러 저장하세요."; }
                else _conflictText.text = "여러 액션과 충돌하거나 필수 키가 사라지는 교체입니다.\n재선택을 눌러 다른 키를 지정하세요.";
            });
            ButtonAt(_conflict.transform, "Reselect", "재선택", new Vector2(180, -80), new Vector2(300, 55), () => { _pendingKey = null; _conflict.SetActive(false); LocalInputBindings.QuarantineHeld(); });
            _conflict.SetActive(false);
        }
        private static void CopyDisplay(LocalSettingsData from, LocalSettingsData to) { to.width = from.width; to.height = from.height; to.windowMode = from.windowMode; }
        private void Apply()
        {
            CancelCapture(); if (Settings.Apply(_draft)) Close(true);
            else { _status.text = Settings.Notice; _wasConfirming = Settings.Confirmation.Pending; _confirmation.SetActive(_wasConfirming); if (_wasConfirming) _confirmation.transform.SetAsLastSibling(); }
        }
        private void Defaults()
        {
            var d = Settings.Defaults();
            if (_tab == 0) { CopyDisplay(d, _draft); _draft.vSync = d.vSync; _draft.frameLimit = d.frameLimit; Settings.ResolveDisplay(_draft); }
            if (_tab == 1) { _draft.master = d.master; _draft.music = d.music; _draft.effects = d.effects; _draft.ui = d.ui; }
            if (_tab == 2) _draft.bindings = LocalBindingRules.Defaults();
            if (_tab == 3) { _draft.rotationSensitivity = d.rotationSensitivity; _draft.invertVertical = d.invertVertical; _draft.zoom2D = d.zoom2D; _draft.zoom3D = d.zoom3D; _draft.showAttackRange = d.showAttackRange; }
            CancelCapture(); Preview(); _status.text = "이 탭의 기본값을 준비했습니다. 적용 또는 취소하세요.";
        }
        private void Preview() { Settings.Preview(_draft); Refresh(); }
        private void Refresh() { foreach (var refresh in _refresh) refresh(); }
        private void Cancel() => Close(false);
        private void Close(bool applied)
        {
            if (!_open) return;
            CancelCapture();
            if (!applied && Settings != null) { Settings.CancelPreview(); if (_camera != null && _cameraBefore != null) _camera.RestoreLocalState(_cameraBefore); }
            _open = IsOpen = _wasConfirming = false; _modal?.SetActive(false); _confirmation?.SetActive(false);
            if (_pausedSingle && !RaidNetworkSession.Multiplayer) Time.timeScale = _previousTimeScale;
            _pausedSingle = false; _camera?.ReleaseOrbit(); LocalInputBindings.QuarantineHeld();
        }
        public static void CloseForSessionEnd() { foreach (var ui in UnityEngine.Object.FindObjectsByType<LocalCameraSettingsUI>(FindObjectsSortMode.None)) ui.Cancel(); }
        private void LateUpdate()
        {
            if (_panel == null) return;
            var available = ((RectTransform)_canvas.transform).rect.size;
            _panel.localScale = Vector3.one * Mathf.Max(.1f, Mathf.Min(1, (available.x - 16) / 920, (available.y - 16) / 720));
            _buttonRoot.SetActive(!_open && RaidSteamRuntime.Instance?.ConfirmInvite != true);
            if (!_open) _buttonRoot.transform.SetAsLastSibling();
            // Connection UI sorts at order 2000. Its late sort calls RaiseAboveMenus after arranging its overlays.
        }
        public void RaiseAboveMenus()
        {
            if (_open && _modal != null && RaidSteamRuntime.Instance?.ConfirmInvite != true) _modal.transform.SetAsLastSibling();
            else if (_buttonRoot != null && _buttonRoot.activeSelf && RaidSteamRuntime.Instance?.ConfirmInvite != true) _buttonRoot.transform.SetAsLastSibling();
        }
        private RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); r.anchoredPosition = pos; r.sizeDelta = size; return r; }
        private RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        { var r = Rect(parent, name, pos, size); r.gameObject.AddComponent<Image>().color = color; return r; }
        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private Text Label(Transform parent, string value, Vector2 pos, Vector2 size, int fontSize)
        { var t = Rect(parent, "Label", pos, size).gameObject.AddComponent<Text>(); t.font = _font; t.text = value; t.fontSize = fontSize; t.color = Color.white; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false; return t; }
        private Text LabelTop(Transform p, string text, float y, float width, float height, int size)
        { var t = Label(p, text, new Vector2(0, -y), new Vector2(width, height), size); t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(.5f, 1); return t; }
        private Button ButtonAt(Transform p, string name, string text, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction click)
        {
            var r = Box(p, name, pos, size, new Color(.13f, .27f, .36f)); var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = r.GetComponent<Image>();
            b.navigation = new Navigation { mode = Navigation.Mode.None }; b.onClick.AddListener(click); Label(r, text, Vector2.zero, size - Vector2.one * 6, 18); return b;
        }
        private Button ButtonTop(Transform p, string text, float x, float y, float width, UnityEngine.Events.UnityAction click)
        { var b = ButtonAt(p, text, text, new Vector2(x, -y), new Vector2(width, 36), click); var r = b.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(.5f, 1); return b; }
        private void Row(Transform p, float y, string title, Func<string> value, UnityEngine.Events.UnityAction click, bool enabled = true)
        {
            var label = LabelTop(p, title, y, 350, 40, 20); label.rectTransform.anchoredPosition = new Vector2(-205, -y);
            var b = ButtonTop(p, "", 205, y + 20, 365, click); b.interactable = enabled; var text = b.GetComponentInChildren<Text>(); _refresh.Add(() => text.text = value());
        }
        private void SliderAt(Transform parent, float y, string name, float min, float max, Func<float> get, Action<float> set)
        {
            var label = LabelTop(parent, "", y, 680, 30, 19); label.rectTransform.anchoredPosition = new Vector2(-60, -y);
            var rect = Rect(parent, name, new Vector2(-60, -y - 48), new Vector2(580, 24)); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            var track = Box(rect, "Track", Vector2.zero, new Vector2(580, 6), new Color(.25f, .37f, .45f));
            var handle = Box(rect, "Handle", Vector2.zero, new Vector2(20, 28), new Color(.3f, .9f, .8f));
            var slider = rect.gameObject.AddComponent<Slider>(); slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<Image>(); slider.minValue = min; slider.maxValue = max; slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.onValueChanged.AddListener(v => { set(v); Preview(); });
            _refresh.Add(() => { slider.SetValueWithoutNotify(get()); label.text = name + "   " + get().ToString(max == 1 ? "P0" : "F2"); });
        }
        private void OnDisable() { Cancel(); }
        private void OnDestroy() { Cancel(); IsOpen = false; if (_buttonRoot != null) Destroy(_buttonRoot); if (_modal != null) Destroy(_modal); }
    }
}
