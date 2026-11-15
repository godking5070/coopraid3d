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
            if(LocalHUDLayoutController.IsEditing)
            {if(escape){Consume();LocalHUDLayoutController.Instance.CancelEdit();}return;}
            if (RaidConnectionUI.CampaignDialogOpen)
            { if (escape) { Consume(); FindFirstObjectByType<RaidConnectionUI>()?.CloseCampaignDialog(); } return; }
            if (RaidSteamRuntime.Instance?.ConfirmInvite == true)
            { if (escape) { Consume(); RaidSteamRuntime.Instance.AnswerInvite(false); } return; }
            if (_open)
            {
                bool pending = Settings.Confirmation.Pending;
                _confirmation.SetActive(pending); if (pending) RaidUIOrder.Raise(_confirmation.transform);
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
                else if (session.Mode == RaidSessionMode.Menu) FindFirstObjectByType<RaidConnectionUI>()?.TryBackMainMenu();
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
        private void BindAuthoredSettings()
        {
            RaidUIView.Bind(this,"Settings",Open);
            RaidUIView.Bind(this,"Apply",Apply);
            RaidUIView.Bind(this,"Cancel",Cancel);
            RaidUIView.Bind(this,"Defaults",Defaults);
            RaidUIView.Bind(this,"Keep",() => { if (Settings.KeepDisplay()) Close(true); else { _status.text = Settings.Notice; Refresh(); } });
            RaidUIView.Bind(this,"Revert",() => Settings.RevertDisplay());
            RaidUIView.Bind(this,"Swap",() =>
            {
                if (LocalBindingRules.TryBind(_draft.bindings, _captureRow, _captureSlot, _pendingKey, true)) { CancelCapture(); Refresh(); _status.text = "키 교체 완료 · 적용을 눌러 저장하세요."; }
                else _conflictText.text = "여러 액션과 충돌하거나 필수 키가 사라지는 교체입니다.\n재선택을 눌러 다른 키를 지정하세요.";
            });
            RaidUIView.Bind(this,"Reselect",() => { _pendingKey = null; _conflict.SetActive(false); LocalInputBindings.QuarantineHeld(); });
            string[] names={ "화면", "사운드", "조작", "카메라" };
            for(int i=0;i<4;i++){int tab=i;RaidUIView.Bind(this,names[i],()=>SelectTab(tab));}
            EnsureHUDLayoutButton();
        }
        private void Build()
        {
            _canvas = FindFirstObjectByType<Canvas>(); if (_canvas == null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if(RaidUIView.Attach(this,"settings",_canvas.transform)){ BindAuthoredSettings(); return; }
            var viewParent=RaidUIView.Begin(this,"settings",_canvas.transform);
            var open = ButtonAt(viewParent, "Settings", "설정", new Vector2(0, 0), new Vector2(230, 36), Open);
            _buttonRoot = open.gameObject; _openLabel = open.GetComponentInChildren<Text>();
            var rect = open.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1); rect.anchoredPosition = new Vector2(-24, -180);
            _modal = Box(viewParent, "LocalSettingsModal", Vector2.zero, Vector2.zero, new Color(0, 0, 0, .8f)).gameObject;
            Stretch(_modal.GetComponent<RectTransform>());
            _panel = Box(_modal.transform, "Panel", Vector2.zero, new Vector2(920, 720), new Color(.055f, .085f, .12f));
            Label(_panel, "설정 · 이 기기의 사용자 선호값", new Vector2(0, 322), new Vector2(860, 36), 25);
            string[] names = { "화면", "사운드", "조작", "카메라" };
            for (int i = 0; i < 4; i++) { int tab = i; ButtonAt(_panel, names[i], names[i], new Vector2(-330 + 220 * i, 268), new Vector2(208, 40), () => SelectTab(tab)); }
            _status = Label(_panel, "", new Vector2(0, -266), new Vector2(865, 68), 16);
            ButtonAt(_panel, "Apply", "적용", new Vector2(-285, -325), new Vector2(190, 44), Apply);
            ButtonAt(_panel, "Cancel", "취소 [Esc]", new Vector2(-65, -325), new Vector2(210, 44), Cancel);
            ButtonAt(_panel, "Defaults", "이 탭 기본값 복원", new Vector2(220, -325), new Vector2(300, 44), Defaults);
            BuildConfirmation();EnsureHUDLayoutButton(); _modal.SetActive(false);
        }
        private void EnsureHUDLayoutButton()
        {
            if(_panel==null)return;
            var view=RaidUIView.For(this);
            var existing=view?.actions.Find(a=>a.id=="HUDLayout");
            if(existing?.button!=null){RaidUIView.Bind(this,"HUDLayout",OpenHUDLayout);return;}
            // Extend the existing footer once. An authored HUDLayout button keeps its own layout.
            string[] ids={"Apply","Cancel","Defaults"};float[] positions={-330,-160,80};float[] widths={150,160,280};
            for(int i=0;i<ids.Length;i++)
            {
                var button=view?.actions.Find(a=>a.id==ids[i])?.button;if(button==null)continue;
                var rect=button.GetComponent<RectTransform>();rect.anchoredPosition=new Vector2(positions[i],rect.anchoredPosition.y);rect.sizeDelta=new Vector2(widths[i],rect.sizeDelta.y);
            }
            if(_font==null)_font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ButtonAt(_panel,"HUDLayout","HUD 배치 편집",new Vector2(325,-325),new Vector2(180,44),OpenHUDLayout);
        }
        private void OpenHUDLayout()
        {
            if(!_open||Settings==null||Settings.Confirmation.Pending||_captureRow>=0)return;
            var hud=FindFirstObjectByType<AstellaOrnateHUD>();
            var layout=LocalHUDLayoutController.Ensure(hud);
            if(layout==null||!layout.Begin(()=>{if(this!=null&&_open){_modal.SetActive(true);RaiseAboveMenus();}}))
            {_status.text="현재 화면에서 편집할 HUD를 찾을 수 없습니다.";return;}
            _modal.SetActive(false);LocalGameplayInput.SuppressPointerThisFrame();
        }
        public void Open()
        {
            // A menu click can arrive before this presenter's first Update after a UI swap.
            if (_modal == null) Build();
            if (_open || Settings == null || RaidNetworkSession.Instance?.Ending == true || RaidSteamRuntime.Instance?.OverlayOpen == true || RaidSteamRuntime.Instance?.ConfirmInvite == true) return;
            if (_modal == null || _status == null || _camera == null)
            {
                Debug.LogError("Settings UI is missing required presentation references; the window cannot open.", this);
                return;
            }
            _draft = Settings.Applied.Copy(); Settings.CaptureCurrentDisplay(_draft); Settings.ResolveDisplay(_draft); _cameraBefore = _camera.CaptureLocalState();
            _camera.ReleaseOrbit(); _open = IsOpen = true; LocalInputBindings.QuarantineHeld();
            _previousTimeScale = Time.timeScale; _pausedSingle = RaidNetworkSession.Instance?.Mode == RaidSessionMode.Single;
            if (_pausedSingle) Time.timeScale = 0;
            _status.text = Settings.Notice; if (_draft.bindingsNeedReview) _tab = 2; RebuildTabs(); SelectTab(_tab); _modal.SetActive(true);
            RaiseAboveMenus();
        }
        private bool _buildingForms;
        public void PrepareFormsForAuthoring()
        {
            if(_open || Settings==null)return;
            _draft=Settings.Applied.Copy();RebuildTabs();SelectTab(0);
        }
        private void RebuildTabs()
        {
            _buildingForms=true;
            foreach (var tab in _tabs) tab.SetActive(false); _refresh.Clear();
            BuildDisplay(Tab("Display", 430)); BuildAudio(Tab("Audio", 430)); BuildControls(Tab("Controls", 190 + LocalBindingRules.Actions.Length * 43)); BuildCamera(Tab("Camera", 515));
            _buildingForms=false;
        }
        private RectTransform Tab(string name, float height)
        {
            foreach(var existing in _tabs) { var saved=existing.GetComponent<RaidUIForm>();if(saved!=null&&saved.id==name)return saved.content; }
            var root = Box(_panel, name, new Vector2(0, 4), new Vector2(870, 460), new Color(.07f, .11f, .16f)); _tabs.Add(root.gameObject);
            var viewport = Rect(root, "Viewport", Vector2.zero, new Vector2(850, 450)); viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content", Vector2.zero, new Vector2(830, height)); content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1);
            var scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 28; scroll.movementType = ScrollRect.MovementType.Clamped;
            var form=root.gameObject.AddComponent<RaidUIForm>();form.id=name;form.content=content;
            content.gameObject.AddComponent<RaidUIForm>().id=name+".controls";
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
            LabelTop(parent, "데스크톱에서는 수직 동기화가 켜지면 화면 주사율이 우선하며 FPS 제한은 무시됩니다.\n버튼을 눌러 지원되는 값을 순환합니다. 화면 변경은 적용 후 15초 확인이 필요합니다.\n" + (Settings.CanChangeDisplay ? "해상도/모드는 이 디스플레이가 지원하는 항목만 표시합니다." : "이 실행 환경에서는 화면 해상도/모드 변경을 제공하지 않습니다. 테스트 빌드에서 확인하세요."), 310, 800, 104, 17,"hint");
        }
        private void BuildAudio(RectTransform parent)
        {
            Volume(parent, 30, "전체", () => _draft.master, v => _draft.master = v, LocalAudioBus.MasterPreview);
            Volume(parent, 120, "배경음", () => _draft.music, v => _draft.music = v, LocalAudioBus.Music);
            Volume(parent, 210, "효과음", () => _draft.effects, v => _draft.effects = v, LocalAudioBus.Effects);
            Volume(parent, 300, "UI", () => _draft.ui, v => _draft.ui = v, LocalAudioBus.UI);
            LabelTop(parent, "변경 즉시 미리듣기에 반영됩니다. 취소하면 이전 값으로 돌아갑니다.\n현재 음원 대신 짧은 테스트 톤을 사용합니다.", 388, 800, 46, 16,"hint");
        }
        private void Volume(RectTransform parent, float y, string name, Func<float> get, Action<float> set, LocalAudioBus bus)
        { SliderAt(parent, y, name, 0, 1, get, set); ButtonTop(parent, "미리듣기", 325, y + 24, 130, () => LocalAudioChannel.Preview(bus),"preview."+bus); }
        private void BuildCamera(RectTransform parent)
        {
            SliderAt(parent, 25, "마우스 회전 감도", .03f, .6f, () => _draft.rotationSensitivity, v => _draft.rotationSensitivity = v);
            Row(parent, 105, "상하 회전 반전", () => _draft.invertVertical ? "켜짐" : "꺼짐", () => { _draft.invertVertical = !_draft.invertVertical; Preview(); });
            SliderAt(parent, 180, "3D 휠 줌 감도", .25f, 5, () => _draft.zoom3D, v => _draft.zoom3D = v);
            SliderAt(parent, 265, "2D 휠 줌 감도", .25f, 5, () => _draft.zoom2D, v => _draft.zoom2D = v);
            ButtonTop(parent, "기본 카메라 상태 복원", 0, 365, 350, () => { _camera.ResetCameraDefaults(); Settings.Preview(_draft); });
            Row(parent, 398, "근접 공격 범위 표시", () => _draft.showAttackRange ? "켜짐" : "꺼짐", () => { _draft.showAttackRange = !_draft.showAttackRange; Preview(); });
            LabelTop(parent, "감도/반전은 저장합니다. 현재 회전 각도와 줌 위치는 게임 중 상태이며 파일에 저장하지 않습니다.", 465, 800, 42, 15,"hint");
        }
        private void BuildControls(RectTransform parent)
        {
            var form=parent.GetComponent<RaidUIForm>();
            ExtendControlsForm(parent, _draft.bindings);
            float height=190+LocalBindingRules.Actions.Length*43;
            if(parent.sizeDelta.y<height)parent.sizeDelta=new Vector2(parent.sizeDelta.x,height);
            Row(parent,5,"이동 방식",()=>_draft.movementMode==1?"우클릭 이동":"키보드 이동",()=>{_draft.movementMode=1-_draft.movementMode;Preview();});
            LabelTop(parent, "우클릭 이동: 누르고 있으면 연속 이동 · 휠 버튼 드래그로 카메라 회전 · 좌클릭 대상 선택\n키보드로 이동을 덮어쓸 수 있습니다. 벽 자동 우회는 하지 않습니다.\n아래 키 클릭으로 재지정 · Esc 취소 · 보조 키는 지울 수 있습니다.", 95, 800, 64, 16,"hint");
            for (int i = 0; i < _draft.bindings.Length; i++)
            {
                int row = i; var definition = LocalBindingRules.Find(_draft.bindings[i].action); float y = 175 + i * 43;
                bool existingLabel = form?.Find<Text>("label.binding." + _draft.bindings[row].action) != null;
                var name = LabelTop(parent, definition.Name, y - 16, 330, 34, 17,"binding."+_draft.bindings[row].action); if(!existingLabel||!RaidUIStyle.Authored(name))name.rectTransform.anchoredPosition = new Vector2(-230, -y + 16);
                for (int slot = 0; slot < 2; slot++)
                {
                    int captured = slot; var button = ButtonTop(parent, "", slot == 0 ? 35 : 225, y, 170, () => BeginCapture(row, captured),"binding."+_draft.bindings[row].action+"."+captured);
                    var label = button.GetComponent<RaidUIRepeatedItem>().label; _refresh.Add(() => { string key = LocalBindingRules.Get(_draft.bindings[row], captured); bool conflict = LocalBindingRules.Conflicts(_draft.bindings, row, captured, key).Count > 0; label.text = LocalInputBindings.KeyLabel(key) + (conflict ? " ⚠ 충돌" : ""); RaidUIStyle.Color(label, conflict ? new Color(1f,.6f,.3f) : Color.white); });
                }
                ButtonTop(parent, "×", 355, y, 60, () => { LocalBindingRules.TryBind(_draft.bindings, row, 1, "", false); Refresh(); },"clear."+_draft.bindings[row].action);
            }
        }
        private static void ExtendControlsForm(RectTransform parent, LocalKeyBinding[] bindings)
        {
            var form = parent.GetComponent<RaidUIForm>();
            if (form == null) return;
            // Use the same registry key as Row/ButtonTop. Existing authored controls move
            // only while the new movement row is absent, never on subsequent openings.
            if (form.Find<Button>("button.row.이동 방식") == null)
                foreach (Transform child in parent)
                    if (child is RectTransform rect) rect.anchoredPosition += new Vector2(0, -90);

            if (form.Find<Button>("button.binding.Interact.0") != null) return;
            bool afterInteraction = false;
            var moved = new HashSet<RectTransform>();
            foreach (var binding in bindings)
            {
                if (binding.action == "Interact") { afterInteraction = true; continue; }
                if (!afterInteraction) continue;
                // Retain each old row's authored offsets, making one row of room for G.
                foreach (string key in new[] { "label.binding." + binding.action,
                    "button.binding." + binding.action + ".0", "button.binding." + binding.action + ".1",
                    "button.clear." + binding.action })
                {
                    var rect = form.Find<Component>(key)?.transform as RectTransform;
                    if (rect != null && moved.Add(rect)) rect.anchoredPosition += new Vector2(0, -43);
                }
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
                    _conflictText.text = LocalInputBindings.KeyLabel(capturedKey) + " 충돌: " + string.Join(", ", names) + "\n기존 키와 교체하거나 다른 키를 선택하세요."; _conflict.SetActive(true); RaidUIOrder.Raise(_conflict.transform);
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
            else { _status.text = Settings.Notice; _wasConfirming = Settings.Confirmation.Pending; _confirmation.SetActive(_wasConfirming); if (_wasConfirming) RaidUIOrder.Raise(_confirmation.transform); }
        }
        private void Defaults()
        {
            var d = Settings.Defaults();
            if (_tab == 0) { CopyDisplay(d, _draft); _draft.vSync = d.vSync; _draft.frameLimit = d.frameLimit; Settings.ResolveDisplay(_draft); }
            if (_tab == 1) { _draft.master = d.master; _draft.music = d.music; _draft.effects = d.effects; _draft.ui = d.ui; }
            if (_tab == 2) { _draft.bindings = LocalBindingRules.Defaults();_draft.movementMode=d.movementMode; }
            if (_tab == 3) { _draft.rotationSensitivity = d.rotationSensitivity; _draft.invertVertical = d.invertVertical; _draft.zoom2D = d.zoom2D; _draft.zoom3D = d.zoom3D; _draft.showAttackRange = d.showAttackRange; }
            CancelCapture(); Preview(); _status.text = "이 탭의 기본값을 준비했습니다. 적용 또는 취소하세요.";
        }
        private void Preview() { Settings.Preview(_draft); Refresh(); }
        private void Refresh() { foreach (var refresh in _refresh) refresh(); }
        private void Cancel() => Close(false);
        private void Close(bool applied)
        {
            if (!_open) return;
            if(LocalHUDLayoutController.IsEditing)LocalHUDLayoutController.Instance.CancelEdit();
            CancelCapture();
            if (!applied && Settings != null) { Settings.CancelPreview(); if (_camera != null && _cameraBefore != null) _camera.RestoreLocalState(_cameraBefore); }
            _open = IsOpen = _wasConfirming = false;
            // The HUD may be destroyed before the camera presenter during scene teardown.
            if (_modal != null) _modal.SetActive(false);
            if (_confirmation != null) _confirmation.SetActive(false);
            if (_pausedSingle && !RaidNetworkSession.Multiplayer) Time.timeScale = _previousTimeScale;
            _pausedSingle = false;
            if (_camera != null) _camera.ReleaseOrbit();
            LocalInputBindings.QuarantineHeld();
        }
        public static void CloseForSessionEnd() { foreach (var ui in UnityEngine.Object.FindObjectsByType<LocalCameraSettingsUI>(FindObjectsSortMode.None)) ui.Cancel(); }
        private void LateUpdate()
        {
            if (_panel == null) return;
            var available = ((RectTransform)_canvas.transform).rect.size;
            if (!RaidUIStyle.Authored(_panel)) _panel.localScale = Vector3.one * Mathf.Max(.1f, Mathf.Min(1, (available.x - 16) / 920, (available.y - 16) / 720));
            _buttonRoot.SetActive(!_open && RaidSteamRuntime.Instance?.ConfirmInvite != true && RaidNetworkSession.Instance?.Mode != RaidSessionMode.Menu);
            if (!_open) RaidUIOrder.Raise(_buttonRoot.transform);
            // Connection UI sorts at order 2000. Its late sort calls RaiseAboveMenus after arranging its overlays.
        }
        public void RaiseAboveMenus()
        {
            if (_open && _modal != null && RaidSteamRuntime.Instance?.ConfirmInvite != true) RaidUIOrder.Raise(_modal.transform);
            else if (_buttonRoot != null && _buttonRoot.activeSelf && RaidSteamRuntime.Instance?.ConfirmInvite != true) RaidUIOrder.Raise(_buttonRoot.transform);
        }
        private RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); r.anchoredPosition = pos; r.sizeDelta = size; return r; }
        private RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        { var r = Rect(parent, name, pos, size); r.gameObject.AddComponent<Image>().color = color; return r; }
        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private Text Label(Transform parent, string value, Vector2 pos, Vector2 size, int fontSize)
        { var t = Rect(parent, "Label", pos, size).gameObject.AddComponent<Text>(); t.font = _font; t.text = value; t.fontSize = fontSize; RaidUIStyle.Color(t, Color.white); t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false; return t; }
        private Text LabelTop(Transform p, string text, float y, float width, float height, int size, string id=null)
        {
            var form=p.GetComponent<RaidUIForm>();string key="label."+(id??text);
            var saved=form?.Find<Text>(key);if(saved!=null){saved.text=text;return saved;}
            var t=Label(p,text,new Vector2(0,-y),new Vector2(width,height),size);
            t.rectTransform.anchorMin=t.rectTransform.anchorMax=t.rectTransform.pivot=new Vector2(.5f,1);
            form?.Remember(key,t);return t;
        }
        private Button ButtonAt(Transform p, string name, string text, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction click)
        {
            var r = Box(p, name, pos, size, new Color(.13f, .27f, .36f)); var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = r.GetComponent<Image>();
            b.navigation = new Navigation { mode = Navigation.Mode.None }; if (_buildingForms) b.onClick.AddListener(click); else RaidUIView.Bind(this,name,click,b); Label(r, text, Vector2.zero, size - Vector2.one * 6, 18); return b;
        }
        private Button ButtonTop(Transform p, string text, float x, float y, float width, UnityEngine.Events.UnityAction click,string id=null)
        {
            var form=p.GetComponent<RaidUIForm>();string key="button."+(id??text);
            var saved=form?.Find<Button>(key);
            if(saved!=null){saved.onClick.RemoveAllListeners();saved.onClick.AddListener(click);return saved;}
            var b=ButtonAt(p,text,text,new Vector2(x,-y),new Vector2(width,36),click);
            var r=b.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(.5f,1);
            var item=b.gameObject.AddComponent<RaidUIRepeatedItem>();item.button=b;item.label=b.GetComponentInChildren<Text>();
            form?.Remember(key,b);return b;
        }
        private void Row(Transform p, float y, string title, Func<string> value, UnityEngine.Events.UnityAction click, bool enabled = true)
        {
            bool existingLabel = p.GetComponent<RaidUIForm>()?.Find<Text>("label." + title) != null;
            var label = LabelTop(p, title, y, 350, 40, 20); if(!existingLabel||!RaidUIStyle.Authored(label))label.rectTransform.anchoredPosition = new Vector2(-205, -y);
            var b = ButtonTop(p, "", 205, y + 20, 365, click,"row."+title); b.interactable = enabled; var text = b.GetComponent<RaidUIRepeatedItem>().label; _refresh.Add(() => text.text = value());
        }
        private void SliderAt(Transform parent, float y, string name, float min, float max, Func<float> get, Action<float> set)
        {
            var label = LabelTop(parent, "", y, 680, 30, 19,"slider."+name); if(!RaidUIStyle.Authored(label))label.rectTransform.anchoredPosition = new Vector2(-60, -y);
            var form=parent.GetComponent<RaidUIForm>();var saved=form?.Find<Slider>("slider."+name);
            if(saved!=null)
            {
                saved.onValueChanged.RemoveAllListeners();saved.onValueChanged.AddListener(v=>{set(v);Preview();});
                _refresh.Add(()=>{saved.SetValueWithoutNotify(get());label.text=name+"   "+get().ToString(max==1?"P0":"F2");});return;
            }
            var rect = Rect(parent, name, new Vector2(-60, -y - 48), new Vector2(580, 24)); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            var track = Box(rect, "Track", Vector2.zero, new Vector2(580, 6), new Color(.25f, .37f, .45f));
            var handle = Box(rect, "Handle", Vector2.zero, new Vector2(20, 28), new Color(.3f, .9f, .8f));
            var slider = rect.gameObject.AddComponent<Slider>(); slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<Image>(); slider.minValue = min; slider.maxValue = max; slider.navigation = new Navigation { mode = Navigation.Mode.None };
            form?.Remember("slider."+name,slider);
            slider.onValueChanged.AddListener(v => { set(v); Preview(); });
            _refresh.Add(() => { slider.SetValueWithoutNotify(get()); label.text = name + "   " + get().ToString(max == 1 ? "P0" : "F2"); });
        }
        private void OnDisable() { Cancel(); }
        private void OnDestroy() { Cancel(); IsOpen = false; if (_buttonRoot != null) Destroy(_buttonRoot); if (_modal != null) Destroy(_modal); }
    }
}
