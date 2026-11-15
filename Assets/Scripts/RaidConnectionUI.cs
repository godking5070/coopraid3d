using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    [DefaultExecutionOrder(2000)]
    public sealed partial class RaidConnectionUI : MonoBehaviour
    {
        private RaidNetworkSession _session;
        private GameObject _overlay, _start, _room, _bar;
        private Text _status, _barText, _roster, _flowText;
        private GameObject _flowBar;
        private InputField _name, _address;
        private readonly Button[] _roles = new Button[4];
        private readonly Text[] _roleCaptions = new Text[4];
        private Text _readyCaption;
        private Font _font;
        private Button _beginBattle, _steamCreate, _invite, _ready, _cancelJoin, _single, _lanHost, _lanJoin, _training;
        private Text _steamStatus, _inviteConfirmText;
        private GameObject _confirmation, _recoveryPanel;
        private Button _recover, _retry, _cancelRecovery, _aegis;
        private Text _recoveryStatus, _partyNotice;
        private float _nextRosterRefresh;
        private void Start()
        {
            _session = GetComponent<RaidNetworkSession>();
            var canvas = FindFirstObjectByType<SkillHotbarUI>()?.GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (RaidUIView.Attach(this, "connection", canvas.transform))
            {
                BindAuthoredMenu();
                Application.wantsToQuit += WantsToQuit;
                _name.text = PlayerPrefs.GetString("Raid.LAN.Name", "Player");
                _address.text = PlayerPrefs.GetString("Raid.LAN.Address", "127.0.0.1:7777");
                ShowMenuPage(MainMenuPage.Home);
                return;
            }
            var viewParent = RaidUIView.Begin(this, "connection", canvas.transform);
            var overlay = Box(viewParent, "Raid_MainMenu", Vector2.zero, Vector2.zero, new Color(.025f, .04f, .065f));
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _overlay = overlay.gameObject;
            BuildMainMenu(overlay);
            _room = Rect(_menuLayout, "RoomOptions", Vector2.zero, new Vector2(800, 380)).gameObject;
            _roster = Label(_room.transform, "", new Vector2(0, 80), new Vector2(850, 150), 18);
            _roster.resizeTextForBestFit=true;_roster.resizeTextMinSize=12;_roster.resizeTextMaxSize=18;
            for (int i = 0; i < 4; i++)
            {
                int role = i + 1;
                _roles[i] = ButtonAt(_room.transform, CombatMetricsManager.RoleName(role), new Vector2(-285 + i * 190, -40), new Vector2(180, 62), () => _session.SelectRole(role));
            }
            _aegis=ButtonAt(_room.transform,"12직업 · 직업 / 기원 선택",new Vector2(0,-105),new Vector2(540,46),AegisRouteSelectionUI.OpenNetwork);
            _ready = ButtonAt(_room.transform, "준비", new Vector2(-170, -160), new Vector2(325, 45), _session.ToggleReady);
            _invite = ButtonAt(_room.transform, "친구 초대", new Vector2(170, -160), new Vector2(325, 45), () => RaidSteamRuntime.Instance?.InviteFriends());
            _beginBattle = ButtonAt(_room.transform, "북쪽 문에서 출발 (호스트)", new Vector2(-170, -215), new Vector2(325, 48), () => _session.RequestAction(9));
            ButtonAt(_room.transform, "화면으로 돌아가기", new Vector2(170, -215), new Vector2(325, 48), () => _session.MenuOpen = false);
            ButtonAt(_room.transform, "방 나가기 · 시작 화면", new Vector2(0, -275), new Vector2(470, 48), _session.Disconnect);
            var confirm = Box(viewParent, "SteamInviteConfirmation", Vector2.zero, Vector2.zero, new Color(.02f, .03f, .05f, .98f));
            confirm.anchorMin = Vector2.zero; confirm.anchorMax = Vector2.one; confirm.offsetMin = confirm.offsetMax = Vector2.zero;
            _confirmation = confirm.gameObject;
            _inviteConfirmText = Label(confirm, "", new Vector2(0, 85), new Vector2(1100, 260), 24);
            ButtonAt(confirm, "현재 방 유지", new Vector2(-185, -70), new Vector2(340, 55), () => RaidSteamRuntime.Instance.AnswerInvite(false));
            ButtonAt(confirm, "나가고 초대 참가", new Vector2(185, -70), new Vector2(340, 55), () => RaidSteamRuntime.Instance.AnswerInvite(true));
            _confirmation.SetActive(false);
            var recovery = Box(viewParent, "RecoveryOverlay", Vector2.zero, Vector2.zero, new Color(.02f, .03f, .05f, .96f));
            recovery.anchorMin = Vector2.zero; recovery.anchorMax = Vector2.one; recovery.offsetMin = recovery.offsetMax = Vector2.zero;
            _recoveryPanel = recovery.gameObject;
            _recoveryStatus = Label(recovery, "", new Vector2(0, 80), new Vector2(920, 180), 25);
            _retry = ButtonAt(recovery, "다시 연결", new Vector2(-190, -60), new Vector2(350, 55), () => RaidSteamRuntime.Instance?.RequestRecovery());
            _cancelRecovery = ButtonAt(recovery, "재접속 취소", new Vector2(-190, -60), new Vector2(350, 55), _session.CancelRecovery);
            ButtonAt(recovery, "메인 메뉴로", new Vector2(190, -60), new Vector2(350, 55), () => ConfirmCampaignExit(false, _session.RecoveryMenu));
            _recoveryPanel.SetActive(false);
            _partyNotice = Label(viewParent, "", new Vector2(0, 250), new Vector2(900, 90), 23);
            var bar = Box(viewParent, "LAN_Status", Vector2.zero, new Vector2(390, 70), new Color(0.04f, 0.08f, 0.13f, 0.94f));
            bar.anchorMin = bar.anchorMax = new Vector2(0, 1); bar.pivot = new Vector2(0, 1); bar.anchoredPosition = new Vector2(15, -64);
            _bar = bar.gameObject;
            _barText = Label(bar, "", new Vector2(-65, 0), new Vector2(245, 60), 15);
            ButtonAt(bar, "접속 / 역할", new Vector2(125, 0), new Vector2(115, 48), () => _session.MenuOpen = true);
            var flow = Box(viewParent, "RunFlow_Status", Vector2.zero, new Vector2(1000, 52), new Color(.03f, .06f, .1f, .97f));
            flow.anchorMin = flow.anchorMax = new Vector2(.5f, 1); flow.pivot = new Vector2(.5f, 1); flow.anchoredPosition = new Vector2(0, -8);
            _flowBar = flow.gameObject;
            _flowText = Label(flow, "", new Vector2(-90, 0), new Vector2(790, 48), 17);
            ButtonAt(flow, "메뉴 / 나가기", new Vector2(405, 0), new Vector2(175, 42), () => _session.MenuOpen = true);
            _readyCaption=_ready.GetComponentInChildren<Text>();
            for(int i=0;i<4;i++)_roleCaptions[i]=_roles[i].GetComponentInChildren<Text>();
            BuildCampaignUI(viewParent, _menuLayout);
            ShowMenuPage(MainMenuPage.Home);
        }
        private void Connect(bool host)
        {
            PlayerPrefs.SetString("Raid.LAN.Name", _name.text); PlayerPrefs.SetString("Raid.LAN.Address", _address.text); PlayerPrefs.Save();
            _session.StartConnection(host, _name.text, _address.text);
        }
        private void Update()
        {
            if (_overlay == null) return;
            _overlay.SetActive(_session.MenuOpen); _bar.SetActive(!_session.MenuOpen);
            if (_session.MenuOpen) RaidUIOrder.Raise(_overlay.transform);
            var steam = RaidSteamRuntime.Instance;
            bool busy = steam != null && steam.Busy;
            bool starting = _session.Mode == RaidSessionMode.Menu;
            _confirmation.SetActive(steam != null && steam.ConfirmInvite);
            if (_confirmation.activeSelf)
                _inviteConfirmText.text = "다른 Steam 방의 초대를 받았습니다.\n현재 세션을 나가고 새 방에 참가하시겠습니까?\n\n" + _session.CampaignExitSaveLabel + "\n현재 전투와 미완료 보상 선택은 저장되지 않습니다.";
            if (_confirmation.activeSelf) RaidUIOrder.Raise(_confirmation.transform);
            if (starting) _steamStatus.text = steam != null && steam.Available ? steam.Status : "Steam 연결을 사용할 수 없습니다. 혼자 시작하거나 LAN으로 연결할 수 있습니다.";
            _steamCreate.interactable = steam != null && steam.Available && !busy;
            _single.interactable = _lanHost.interactable = _lanJoin.interactable = !busy;
            _cancelJoin.gameObject.SetActive(busy && !_session.IsRecovering);
            _recover.gameObject.SetActive(steam != null && steam.PreviousLobby != 0 && !busy && _session.Mode == RaidSessionMode.Menu && _menuPage == MainMenuPage.Join);
            _recover.interactable = steam != null && steam.CanRecover;
            bool recovering = _session.IsRecovering;
            _recoveryPanel.SetActive(recovering);
            _retry.gameObject.SetActive(recovering && !busy && _session.Mode == RaidSessionMode.Disconnected);
            _retry.interactable = steam != null && steam.CanRecover;
            _cancelRecovery.gameObject.SetActive(recovering && (busy || _session.Mode != RaidSessionMode.Disconnected));
            _recoveryStatus.text = _session.Status + "\n" + (steam != null ? steam.Status : "Steam 사용 불가") + "\n상태 복구 완료 후 조작이 재개됩니다.";
            _partyNotice.text = _session.VisiblePartyNotice; _partyNotice.gameObject.SetActive(_partyNotice.text.Length > 0);
            _invite.gameObject.SetActive(_session.UsesSteam && _session.CanJoinBattle);
            _aegis.gameObject.SetActive(_session.CanEditLobbyBuild); _aegis.interactable=_session.HasLocalAvatar&&_session.CanEditLobbyBuild;
            _ready.gameObject.SetActive(RaidNetworkSession.Multiplayer && _session.CanChooseRole);
            _ready.interactable = _session.HasLocalAvatar;
            _readyCaption.text = _session.LocalReady ? "준비 완료 · 취소" : "준비";
            _start.SetActive(starting && _menuPage != MainMenuPage.Saves); _room.SetActive(!starting);
            if (starting) UpdateMainMenu(true, busy);
            _status.text = _session.Status + "\n" + _session.FlowStatus;
            _flowText.text = _session.FlowStatus;
            bool singleModal = _session.Mode == RaidSessionMode.Single && StageManager.CombatSuspended;
            if (singleModal) _flowText.text = StageManager.Instance.CanSelectInitialRole ? "직업과 기원을 선택한 뒤 대기실 북쪽 문에서 출발하세요" : "전투 준비 / 결과 확인 · 메뉴에서 새 런을 시작할 수 있습니다";
            // This bar also owns the menu button, even when no progress message is needed.
            bool inRun = _session.Mode == RaidSessionMode.Single || _session.Mode == RaidSessionMode.Host ||
                _session.Mode == RaidSessionMode.Client || _session.Mode == RaidSessionMode.AiSpectator;
            _flowBar.SetActive(inRun && !CombatMetricsUI.ResultsOpen && !_session.MenuOpen && !recovering);
            _beginBattle.gameObject.SetActive(_session.IsHostAuthority && _session.CanChooseRole);
            _beginBattle.interactable = _session.CanStartLobby && RaidLobbyRoom.HostAtDoor;
            _barText.text = _session.Mode == RaidSessionMode.Single ? "싱글플레이 · AI 파티" : $"{(_session.UsesSteam ? "Steam" : "LAN")} · 접속 {_session.ConnectedPlayerCount}/4명 · {_session.BattlePhase}\n{LocalInputBindings.Label("Dash")} 대쉬 · {LocalInputBindings.Label("Potion")} 포션";
            if (_session.Mode == RaidSessionMode.Training)
            {
                _bar.SetActive(false); _flowBar.SetActive(false);
                _roster.text = "로컬 훈련장 · 진행도와 보상 저장 없음\n화면으로 돌아가거나 메인 메뉴로 종료할 수 있습니다.";
                foreach (var role in _roles) role.gameObject.SetActive(false);
                return;
            }
            // The hidden lobby used to rebuild strings and query native Steam members every frame.
            // Admission/control validation remains in the host simulation, independent of UI refresh.
            if (starting || !_session.MenuOpen || Time.unscaledTime < _nextRosterRefresh) return;
            _nextRosterRefresh = Time.unscaledTime + .2f;
            if (_session.Mode == RaidSessionMode.Single)
            {
                _roster.text = "싱글플레이 · AI 파티\n";
                var counters=new System.Collections.Generic.List<string>();
                foreach(var member in PartyLife.OrderedMembers())
                {
                    if(member==null)continue;
                    var actor=member.GetComponent<CharacterActor>();
                    string name=TargetingManager.Instance?.GetUnitDisplayName(member)??member.name;
                    string assignment=LobbyFourth(actor?.Definition,actor?.Snapshot,out bool counter);
                    _roster.text+=name+" · "+assignment+"\n";
                    if(counter)counters.Add(name);
                }
                _roster.text+=LobbyCounterSummary(counters.ToArray());
            }
            else
            {
                _roster.text = $"접속 {_session.ConnectedPlayerCount}/4 · 예약 포함 {_session.PlayerCount}자리 (호스트 포함)\n";
                var counters=new System.Collections.Generic.List<string>();
                foreach (var avatar in _session.Avatars.Values)
                {
                    if (avatar.IsBot && _session.CanChooseRole) continue;
                    var actor=avatar.GetComponent<CharacterActor>();
                    string assignment=LobbyFourth(actor?.Definition,actor?.Snapshot,out bool counter);
                    if(counter)counters.Add(avatar.DisplayName);
                    _roster.text += $"{avatar.DisplayName}{(avatar.Id == 1 ? " [호스트]" : "")}{(avatar.IsLocal ? " (나)" : "")} · {avatar.RoleName}{(avatar.GetComponent<AegisPaladin>()?.IsActive==true?" / "+avatar.GetComponent<AegisPaladin>().RouteName:"")} · {assignment} · {(avatar.IsBot ? "AI" : avatar.Automated ? _session.ConnectionLabel(avatar.State) : avatar.Ready ? "준비 완료" : "준비 전")} · {(avatar.IsBot || avatar.Automated ? "" : avatar.Loaded ? "연결 완료" : "로딩 중")}\n";
                }
                if (_session.UsesSteam && steam != null)
                    foreach (ulong id in steam.Members())
                    {
                        bool connected = false;
                        foreach (var avatar in _session.Avatars.Values) if (avatar.SteamId == id) connected = true;
                        if (!connected) _roster.text += steam.Persona(id) + " · 접속 승인 대기 (파티 미확정)\n";
                    }
                _roster.text+=LobbyCounterSummary(counters.ToArray());
                if (_session.CanChooseRole && _session.PlayerCount < 4) _roster.text += "\n시작 시 빈 역할은 AI가 담당합니다";
            }
            for (int i = 0; i < _roles.Length; i++)
            {
                NetworkRaidAvatar occupant = null;
                foreach (var avatar in _session.Avatars.Values) if (avatar.Role == i + 1) occupant = avatar;
                _roles[i].gameObject.SetActive(RaidNetworkSession.Multiplayer);
                _roles[i].interactable = !_session.AiSpectatorActive && _session.HasLocalAvatar && _session.CanEditLobbyBuild && (occupant == null || occupant.IsLocal || occupant.IsBot);
                _roleCaptions[i].text = CombatMetricsManager.RoleName(i + 1) + "\n" + (occupant != null ? occupant.DisplayName : "선택 가능");
            }
        }
        // Read confirmed actor selections only; this is not a readiness or departure gate.
        private static string LobbyFourth(CharacterDefinition definition,CharacterStateSnapshot state,out bool counter)
        {
            counter=false;
            if(definition==null||state==null||state.jobId!=definition.id||state.selections==null)return "4 정보 확인 중";
            var selected=System.Array.Find(state.selections,s=>s!=null&&s.slot==CharacterSlot.Counter);
            var skill=selected==null?null:definition.Skill(selected.skillId);
            if(skill==null)return "4 미선택";
            if(!skill.implemented)return "4 미구현";
            if(state.unlocked==null||System.Array.IndexOf(state.unlocked,skill.id)<0)return "4 미해금";
            var job=CharacterRosterData.Find(definition.id);
            if(skill.handlerId==NineKeyDamageData.Handler&&job!=null)
            {
                if(skill.id==NineKeyDamageData.Id(job,"c1"))return "4 무력화";
                if(skill.id==NineKeyDamageData.Id(job,"c2"))return "4 동료 보호";
                counter=skill.id==NineKeyDamageData.Id(job,"c0");
            }
            else counter=skill.handlerId==NineKeyTankData.Handler||skill.handlerId==NineKeyHealerData.Handler||
                skill.handlerId=="legacy.counter"||skill.id==AegisPaladinData.Id(CharacterSlot.Counter);
            return counter?"4 카운터":"4 역할 확인 필요";
        }
        private static string LobbyCounterSummary(string[] names)
        {
            return names!=null&&names.Length>0?"카운터 장착자: "+string.Join(", ",names)+" · 담당을 상의하세요":
                "카운터 장착자 없음 · 출발 전 장착과 담당을 확인하세요";
        }
        private RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchoredPosition = pos; rect.sizeDelta = size; return rect;
        }
        private RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        { var rect = Rect(parent, name, pos, size); rect.gameObject.AddComponent<Image>().color = color; return rect; }
        private Text Label(Transform parent, string value, Vector2 pos, Vector2 size, int fontSize)
        {
            var text = Rect(parent, "Text", pos, size).gameObject.AddComponent<Text>(); text.font = _font; text.fontSize = fontSize;
            text.text = value; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
        }
        private Button ButtonAt(Transform parent, string caption, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Box(parent, caption, pos, size, new Color(0.12f, 0.28f, 0.4f)); var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>(); RaidUIView.Bind(this, caption, action, button); Label(rect, caption, Vector2.zero, size - Vector2.one * 8, 18); return button;
        }
        private InputField Field(Transform parent, Vector2 pos, string value, int limit)
        {
            var rect = Box(parent, "Input", pos, new Vector2(450, 44), new Color(0.12f, 0.15f, 0.2f));
            var field = rect.gameObject.AddComponent<InputField>(); field.targetGraphic = rect.GetComponent<Image>();
            field.textComponent = Label(rect, "", Vector2.zero, new Vector2(425, 40), 20); field.textComponent.alignment = TextAnchor.MiddleLeft;
            field.textComponent.supportRichText = false; field.characterLimit = limit; field.text = value; return field;
        }
        private LocalCameraSettingsUI _settingsUI;
        private void LateUpdate()
        {
            // Results can open after Update. Do not raise the flow bar over the modal.
            if (CombatMetricsUI.ResultsOpen && _flowBar != null) _flowBar.SetActive(false);
            if (_flowBar != null && _flowBar.activeSelf) RaidUIOrder.Raise(_flowBar.transform);
            if (_overlay != null && _overlay.activeSelf) RaidUIOrder.Raise(_overlay.transform);
            if (_recoveryPanel != null && _recoveryPanel.activeSelf) RaidUIOrder.Raise(_recoveryPanel.transform);
            if (_partyNotice != null && _partyNotice.gameObject.activeSelf) RaidUIOrder.Raise(_partyNotice.transform);
            if (_confirmation != null && _confirmation.activeSelf) RaidUIOrder.Raise(_confirmation.transform);
            if (_settingsUI == null) _settingsUI = FindFirstObjectByType<LocalCameraSettingsUI>();
            _settingsUI?.RaiseAboveMenus();
            UpdateCampaignUI();
        }
        private void OnDestroy() { DestroyCampaignUI(); if (_flowBar != null) Destroy(_flowBar); if (_overlay != null) Destroy(_overlay); if (_bar != null) Destroy(_bar); if (_confirmation != null) Destroy(_confirmation); if (_recoveryPanel != null) Destroy(_recoveryPanel); if (_partyNotice != null) Destroy(_partyNotice.gameObject); }
    }
}
