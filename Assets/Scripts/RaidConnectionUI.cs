using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    [DefaultExecutionOrder(2000)]
    public sealed class RaidConnectionUI : MonoBehaviour
    {
        private RaidNetworkSession _session;
        private GameObject _overlay, _start, _room, _bar;
        private Text _status, _barText, _roster, _flowText;
        private GameObject _flowBar;
        private InputField _name, _address;
        private readonly Button[] _roles = new Button[4];
        private Font _font;
        private Button _beginBattle, _steamCreate, _invite, _ready, _cancelJoin, _single, _lanHost, _lanJoin;
        private Text _steamStatus;
        private GameObject _confirmation, _recoveryPanel;
        private Button _recover, _retry, _cancelRecovery;
        private Text _recoveryStatus, _partyNotice;
        private void Start()
        {
            _session = GetComponent<RaidNetworkSession>();
            var canvas = FindFirstObjectByType<SkillHotbarUI>()?.GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var overlay = Box(canvas.transform, "LAN_ConnectionOverlay", Vector2.zero, Vector2.zero, new Color(0.025f, 0.04f, 0.07f, 0.97f));
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _overlay = overlay.gameObject;
            Label(overlay, "4인 협동 레이드", new Vector2(0, 300), new Vector2(800, 50), 34);
            _status = Label(overlay, "", new Vector2(0, 245), new Vector2(850, 65), 20);
            _steamStatus = Label(overlay, "", new Vector2(0, 185), new Vector2(900, 55), 17);
            _start = Rect(overlay, "StartOptions", Vector2.zero, new Vector2(800, 380)).gameObject;
            Label(_start.transform, "플레이어 이름", new Vector2(-260, 115), new Vector2(220, 40), 20);
            _name = Field(_start.transform, new Vector2(130, 115), PlayerPrefs.GetString("Raid.LAN.Name", "Player"), 16);
            Label(_start.transform, "호스트 주소 : 포트", new Vector2(-260, 60), new Vector2(220, 40), 20);
            _address = Field(_start.transform, new Vector2(130, 60), PlayerPrefs.GetString("Raid.LAN.Address", "127.0.0.1:7777"), 40);
            _single = ButtonAt(_start.transform, "싱글플레이 · AI 파티", new Vector2(0, 0), new Vector2(560, 48), _session.SinglePlay);
            _lanHost = ButtonAt(_start.transform, "LAN 방 만들기", new Vector2(-150, -60), new Vector2(280, 48), () => Connect(true));
            _lanJoin = ButtonAt(_start.transform, "LAN 주소로 참가", new Vector2(150, -60), new Vector2(280, 48), () => Connect(false));
            _steamCreate = ButtonAt(_start.transform, "Steam 방 만들기 · 친구 전용", new Vector2(0, -120), new Vector2(560, 48), () => RaidSteamRuntime.Instance?.CreateRoom());
            _cancelJoin = ButtonAt(_start.transform, "Steam 접속 취소", new Vector2(0, -180), new Vector2(300, 42), () => RaidSteamRuntime.Instance?.CancelJoin());
            _recover = ButtonAt(_start.transform, "이전 게임으로 복귀", new Vector2(0, -180), new Vector2(400, 42), () => RaidSteamRuntime.Instance?.RequestRecovery());
            Label(_start.transform, "Steam 친구 초대는 오버레이에서 수락 · LAN 테스트는 주소로 참가\n최대 4명 · 빈 역할은 호스트 AI가 담당", new Vector2(0, -245), new Vector2(820, 70), 18);
            _room = Rect(overlay, "RoomOptions", Vector2.zero, new Vector2(800, 380)).gameObject;
            _roster = Label(_room.transform, "", new Vector2(0, 85), new Vector2(850, 160), 20);
            for (int i = 0; i < 4; i++)
            {
                int role = i + 1;
                _roles[i] = ButtonAt(_room.transform, CombatMetricsManager.RoleName(role), new Vector2(-285 + i * 190, -40), new Vector2(180, 62), () => _session.SelectRole(role));
            }
            _ready = ButtonAt(_room.transform, "준비", new Vector2(-170, -105), new Vector2(325, 45), _session.ToggleReady);
            _invite = ButtonAt(_room.transform, "친구 초대", new Vector2(170, -105), new Vector2(325, 45), () => RaidSteamRuntime.Instance?.InviteFriends());
            _beginBattle = ButtonAt(_room.transform, "전투 시작 (호스트)", new Vector2(-170, -165), new Vector2(325, 48), () => _session.RequestAction(9));
            ButtonAt(_room.transform, "화면으로 돌아가기", new Vector2(170, -165), new Vector2(325, 48), () => _session.MenuOpen = false);
            ButtonAt(_room.transform, "방 나가기 · 시작 화면", new Vector2(0, -225), new Vector2(470, 48), _session.Disconnect);
            var confirm = Box(canvas.transform, "SteamInviteConfirmation", Vector2.zero, Vector2.zero, new Color(.02f, .03f, .05f, .98f));
            confirm.anchorMin = Vector2.zero; confirm.anchorMax = Vector2.one; confirm.offsetMin = confirm.offsetMax = Vector2.zero;
            _confirmation = confirm.gameObject;
            Label(confirm, "다른 Steam 방의 초대를 받았습니다.\n현재 세션을 나가고 새 방에 참가하시겠습니까?", new Vector2(0, 70), new Vector2(850, 130), 27);
            ButtonAt(confirm, "현재 방 유지", new Vector2(-185, -70), new Vector2(340, 55), () => RaidSteamRuntime.Instance.AnswerInvite(false));
            ButtonAt(confirm, "나가고 초대 참가", new Vector2(185, -70), new Vector2(340, 55), () => RaidSteamRuntime.Instance.AnswerInvite(true));
            _confirmation.SetActive(false);
            var recovery = Box(canvas.transform, "RecoveryOverlay", Vector2.zero, Vector2.zero, new Color(.02f, .03f, .05f, .96f));
            recovery.anchorMin = Vector2.zero; recovery.anchorMax = Vector2.one; recovery.offsetMin = recovery.offsetMax = Vector2.zero;
            _recoveryPanel = recovery.gameObject;
            _recoveryStatus = Label(recovery, "", new Vector2(0, 80), new Vector2(920, 180), 25);
            _retry = ButtonAt(recovery, "다시 연결", new Vector2(-190, -60), new Vector2(350, 55), () => RaidSteamRuntime.Instance?.RequestRecovery());
            _cancelRecovery = ButtonAt(recovery, "재접속 취소", new Vector2(-190, -60), new Vector2(350, 55), _session.CancelRecovery);
            ButtonAt(recovery, "메인 메뉴로", new Vector2(190, -60), new Vector2(350, 55), _session.RecoveryMenu);
            _recoveryPanel.SetActive(false);
            _partyNotice = Label(canvas.transform, "", new Vector2(0, 250), new Vector2(900, 90), 23);
            var bar = Box(canvas.transform, "LAN_Status", Vector2.zero, new Vector2(390, 70), new Color(0.04f, 0.08f, 0.13f, 0.94f));
            bar.anchorMin = bar.anchorMax = new Vector2(0, 1); bar.pivot = new Vector2(0, 1); bar.anchoredPosition = new Vector2(15, -64);
            _bar = bar.gameObject;
            _barText = Label(bar, "", new Vector2(-65, 0), new Vector2(245, 60), 15);
            ButtonAt(bar, "접속 / 역할", new Vector2(125, 0), new Vector2(115, 48), () => _session.MenuOpen = true);
            var flow = Box(canvas.transform, "RunFlow_Status", Vector2.zero, new Vector2(1000, 52), new Color(.03f, .06f, .1f, .97f));
            flow.anchorMin = flow.anchorMax = new Vector2(.5f, 1); flow.pivot = new Vector2(.5f, 1); flow.anchoredPosition = new Vector2(0, -8);
            _flowBar = flow.gameObject;
            _flowText = Label(flow, "", new Vector2(-90, 0), new Vector2(790, 48), 17);
            ButtonAt(flow, "메뉴 / 나가기", new Vector2(405, 0), new Vector2(175, 42), () => _session.MenuOpen = true);
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
            if (_session.MenuOpen) _overlay.transform.SetAsLastSibling();
            var steam = RaidSteamRuntime.Instance;
            bool busy = steam != null && steam.Busy;
            _confirmation.SetActive(steam != null && steam.ConfirmInvite);
            if (_confirmation.activeSelf) _confirmation.transform.SetAsLastSibling();
            _steamStatus.text = steam != null ? steam.Status : "Steam 사용 불가 · 싱글/LAN 사용 가능";
            _steamCreate.interactable = steam != null && steam.Available && !busy;
            _single.interactable = _lanHost.interactable = _lanJoin.interactable = !busy;
            _cancelJoin.gameObject.SetActive(busy && !_session.IsRecovering);
            _recover.gameObject.SetActive(steam != null && steam.PreviousLobby != 0 && !busy && _session.Mode == RaidSessionMode.Menu);
            _recover.interactable = steam != null && steam.CanRecover;
            bool recovering = _session.IsRecovering;
            _recoveryPanel.SetActive(recovering);
            _retry.gameObject.SetActive(recovering && !busy && _session.Mode == RaidSessionMode.Disconnected);
            _retry.interactable = steam != null && steam.CanRecover;
            _cancelRecovery.gameObject.SetActive(recovering && (busy || _session.Mode != RaidSessionMode.Disconnected));
            _recoveryStatus.text = _session.Status + "\n" + (steam != null ? steam.Status : "Steam 사용 불가") + "\n상태 복구 완료 후 조작이 재개됩니다.";
            _partyNotice.text = _session.VisiblePartyNotice; _partyNotice.gameObject.SetActive(_partyNotice.text.Length > 0);
            _invite.gameObject.SetActive(_session.UsesSteam && _session.CanJoinBattle);
            _ready.gameObject.SetActive(RaidNetworkSession.Multiplayer && _session.CanChooseRole);
            _ready.interactable = _session.HasLocalAvatar;
            _ready.GetComponentInChildren<Text>().text = _session.LocalReady ? "준비 완료 · 취소" : "준비";
            bool starting = _session.Mode == RaidSessionMode.Menu;
            _start.SetActive(starting); _room.SetActive(!starting);
            _status.text = _session.Status + "\n" + _session.FlowStatus;
            _flowText.text = _session.FlowStatus;
            bool singleModal = _session.Mode == RaidSessionMode.Single && StageManager.CombatSuspended;
            if (singleModal) _flowText.text = StageManager.Instance.CanSelectInitialRole ? "직업 선택 후 준비 카운트다운을 시작합니다" : "전투 준비 / 결과 확인 · 메뉴에서 새 런을 시작할 수 있습니다";
            _flowBar.SetActive(!_session.MenuOpen && !recovering && (_flowText.text.Length > 0 || singleModal));
            _beginBattle.gameObject.SetActive(_session.IsHostAuthority && _session.CanJoinBattle);
            _beginBattle.interactable = _session.CanStartLobby;
            _barText.text = _session.Mode == RaidSessionMode.Single ? "싱글플레이 · AI 파티" : $"{(_session.UsesSteam ? "Steam" : "LAN")} · 접속 {_session.ConnectedPlayerCount}/4명 · {_session.BattlePhase}\nSPACE 대쉬 · Q 포션";
            if (starting) return;
            if (_session.Mode == RaidSessionMode.Single)
                _roster.text = "싱글플레이 진행 중\n연결 종료를 누르면 시작 화면으로 돌아갑니다.";
            else
            {
                _roster.text = $"접속 {_session.ConnectedPlayerCount}/4 · 예약 포함 {_session.PlayerCount}자리 (호스트 포함)\n";
                foreach (var avatar in _session.Avatars.Values)
                {
                    if (avatar.IsBot && _session.CanChooseRole) continue;
                    _roster.text += $"{avatar.DisplayName}{(avatar.Id == 1 ? " [호스트]" : "")}{(avatar.IsLocal ? " (나)" : "")} · {avatar.RoleName} · {(avatar.IsBot ? "AI" : avatar.Automated ? _session.ConnectionLabel(avatar.State) : avatar.Ready ? "준비 완료" : "준비 전")} · {(avatar.IsBot || avatar.Automated ? "" : avatar.Loaded ? "연결 완료" : "로딩 중")}\n";
                }
                if (_session.UsesSteam && steam != null)
                    foreach (ulong id in steam.Members())
                    {
                        bool connected = false;
                        foreach (var avatar in _session.Avatars.Values) if (avatar.SteamId == id) connected = true;
                        if (!connected) _roster.text += steam.Persona(id) + " · 접속 승인 대기 (파티 미확정)\n";
                    }
                if (_session.CanChooseRole && _session.PlayerCount < 4) _roster.text += "시작 시 빈 역할은 AI가 담당합니다";
            }
            for (int i = 0; i < _roles.Length; i++)
            {
                NetworkRaidAvatar occupant = null;
                foreach (var avatar in _session.Avatars.Values) if (avatar.Role == i + 1) occupant = avatar;
                _roles[i].gameObject.SetActive(RaidNetworkSession.Multiplayer);
                _roles[i].interactable = _session.HasLocalAvatar && _session.CanChooseRole && (occupant == null || occupant.IsLocal || occupant.IsBot);
                _roles[i].GetComponentInChildren<Text>().text = CombatMetricsManager.RoleName(i + 1) + "\n" + (occupant != null ? occupant.DisplayName : "선택 가능");
            }
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
            button.targetGraphic = rect.GetComponent<Image>(); button.onClick.AddListener(action); Label(rect, caption, Vector2.zero, size - Vector2.one * 8, 18); return button;
        }
        private InputField Field(Transform parent, Vector2 pos, string value, int limit)
        {
            var rect = Box(parent, "Input", pos, new Vector2(450, 44), new Color(0.12f, 0.15f, 0.2f));
            var field = rect.gameObject.AddComponent<InputField>(); field.targetGraphic = rect.GetComponent<Image>();
            field.textComponent = Label(rect, "", Vector2.zero, new Vector2(425, 40), 20); field.textComponent.alignment = TextAnchor.MiddleLeft;
            field.textComponent.supportRichText = false; field.characterLimit = limit; field.text = value; return field;
        }
        private void LateUpdate()
        {
            if (_flowBar != null && _flowBar.activeSelf) _flowBar.transform.SetAsLastSibling();
            if (_overlay != null && _overlay.activeSelf) _overlay.transform.SetAsLastSibling();
            if (_recoveryPanel != null && _recoveryPanel.activeSelf) _recoveryPanel.transform.SetAsLastSibling();
            if (_partyNotice != null && _partyNotice.gameObject.activeSelf) _partyNotice.transform.SetAsLastSibling();
            if (_confirmation != null && _confirmation.activeSelf) _confirmation.transform.SetAsLastSibling();
        }
        private void OnDestroy() { if (_flowBar != null) Destroy(_flowBar); if (_overlay != null) Destroy(_overlay); if (_bar != null) Destroy(_bar); if (_confirmation != null) Destroy(_confirmation); if (_recoveryPanel != null) Destroy(_recoveryPanel); if (_partyNotice != null) Destroy(_partyNotice.gameObject); }
    }
}
