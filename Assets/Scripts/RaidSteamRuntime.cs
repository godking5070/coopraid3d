using System;
using System.Collections.Generic;
using System.Globalization;
using Steamworks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidRogue
{
    // The only owner of Steam initialization, callbacks, lobby membership and shutdown.
    [DefaultExecutionOrder(-1000)]
    public sealed partial class RaidSteamRuntime : MonoBehaviour
    {
        public static RaidSteamRuntime Instance { get; private set; }
        public bool Available { get; private set; }
        public bool OverlayOpen { get; private set; }
        public bool ConfirmInvite => _invites.NeedsConfirmation;
        public bool Busy => _creating || _invites.Attempt != 0 || _recovering;
        public ulong LobbyId { get; private set; }
        public ulong HostId { get; private set; }
        public ulong UserId => Available ? SteamUser.GetSteamID().m_SteamID : 0;
        public string Status { get; private set; } = "Steam 초기화 중";
        public string Build => _settings.build;
        public string Persona(ulong id) => RaidRoster.CleanName(SteamFriends.GetFriendPersonaName(new CSteamID(id)));
        private RaidSteamSettings _settings;
        private readonly RaidInviteGate _invites = new RaidInviteGate();
        private readonly List<IDisposable> _calls = new List<IDisposable>();
        private Callback<GameLobbyJoinRequested_t> _invite;
        private Callback<GameOverlayActivated_t> _overlay;
        private Callback<LobbyChatUpdate_t> _members;
        private Callback<LobbyDataUpdate_t> _data;
        private bool _creating, _waitingHost;
        private readonly List<EventSystem> _blockedUI = new List<EventSystem>();
        private int _generation;
        private float _deadline, _nextCheck;
        private string _publishedPhase;
        private RaidNetworkSession Session => RaidNetworkSession.Instance;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        { if (Instance == null) new GameObject("RaidSteamRuntime").AddComponent<RaidSteamRuntime>(); }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            try
            {
                _settings = RaidSteamSettings.Load();
                if (_settings == null || string.IsNullOrWhiteSpace(_settings.build) || _settings.build.Length > 40) throw new InvalidOperationException("Steam 설정 파일의 빌드 식별자를 확인하세요");
                if (_settings.appId == 0) { Status = "Steam App ID 미설정 · 싱글플레이와 LAN 테스트 사용 가능"; return; }
                if (!SteamAPI.IsSteamRunning()) throw new InvalidOperationException("Steam을 먼저 실행하고 로그인하세요");
                if (!Application.isEditor && !Debug.isDebugBuild && _settings.restartThroughSteam && SteamAPI.RestartAppIfNecessary(new AppId_t(_settings.appId)))
                { Status = "Steam을 통해 다시 실행합니다"; Application.Quit(); return; }
                if (!Packsize.Test() || !DllCheck.Test()) throw new InvalidOperationException("Steam 네이티브 라이브러리/플랫폼 불일치");
                if (SteamAPI.InitEx(out string error) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK) throw new InvalidOperationException(error);
                Available = true;
                if (!SteamUser.BLoggedOn()) throw new InvalidOperationException("Steam 온라인 로그인이 필요합니다");
                if (SteamUtils.GetAppID().m_AppId != _settings.appId) throw new InvalidOperationException("설정 App ID와 Steam 실행 App ID가 다릅니다. steam_appid.txt와 실행 설정을 확인하세요.");
                SteamNetworkingUtils.InitRelayNetworkAccess();
                _invite = Callback<GameLobbyJoinRequested_t>.Create(e => RequestInvite(e.m_steamIDLobby.m_SteamID));
                _overlay = Callback<GameOverlayActivated_t>.Create(e =>
                {
                    SetOverlay(e.m_bActive != 0);
                    QuarterViewCamera.LocalCamera?.ReleaseOrbit();
                    LocalGameplayInput.SuppressThisFrame(); LocalGameplayInput.SuppressPointerThisFrame();
                    // Never change timeScale: the host and the battle keep running.
                });
                _members = Callback<LobbyChatUpdate_t>.Create(e => { if (e.m_ulSteamIDLobby == LobbyId) _nextCheck = 0; });
                _data = Callback<LobbyDataUpdate_t>.Create(e => { if (e.m_ulSteamIDLobby == LobbyId) { _nextCheck = 0; if (_recovering && !_recoveryConnecting) RecoveryMetadata(e.m_bSuccess != 0); } });
                Status = "Steam 연결됨 · " + Persona(UserId);
                ulong launch = RaidSteamPolicy.LaunchLobby(Environment.GetCommandLineArgs());
                if (launch == 0 && SteamApps.GetLaunchCommandLine(out string command, 4096) > 0)
                    launch = RaidSteamPolicy.LaunchLobby(command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                if (launch != 0) RequestInvite(launch);
            }
            catch (Exception error)
            {
                if (Available) SteamAPI.Shutdown(); Available = false;
                Status = "Steam 사용 불가: " + error.Message + " · 싱글/LAN 사용 가능";
            }
        }
        private void Update()
        {
            if (!Available) return;
            SteamAPI.RunCallbacks();
            if (Session != null && Session.Ending) return;
            if (_recovering) { if (Time.unscaledTime > _deadline) RecoveryError("재접속 시간이 초과되었습니다. 다시 시도해 주세요.", false); return; }
            if (_readOnlyRecovery) return;
            if (_invites.Pending != 0 && !_invites.NeedsConfirmation && !Busy && Session != null && Session.Mode == RaidSessionMode.Menu)
                JoinPending();
            if (Busy && Time.unscaledTime > _deadline) { Fail("Steam 참가 시간이 초과되었습니다. 호스트 상태를 확인한 뒤 다시 초대받으세요."); return; }
            if (LobbyId == 0 || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + .5f;
            ulong owner = SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyId)).m_SteamID;
            if (HostId != 0 && IsMember(UserId) && owner != 0 && (owner != HostId || !IsMember(HostId)))
            { Fail("호스트가 방을 나갔습니다. 호스트 이전은 지원하지 않습니다."); return; }
            if (_waitingHost) { CheckHost(); return; }
            if (Session == null || !Session.UsesSteam) return;
            if (Session.IsHostAuthority)
            {
                PublishPhase();
            }
            else if (Session.Mode == RaidSessionMode.Client && _invites.Attempt != 0)
            { _invites.Finish(); Status = "Steam Relay 연결 완료"; }
        }
        public void CreateRoom()
        {
            if (!Available || Busy || LobbyId != 0 || Session == null || Session.Ending || Session.Mode != RaidSessionMode.Menu) return;
            _creating = true; _deadline = Time.unscaledTime + 30; int generation = ++_generation; var expectedSession = Session;
            Status = "친구 전용 Steam 방 생성 중…";
            CallResult<LobbyCreated_t> call = null;
            call = CallResult<LobbyCreated_t>.Create((result, failed) =>
            {
                _calls.Remove(call); call.Dispose();
                if (generation != _generation || expectedSession == null || Session != expectedSession || expectedSession.Ending)
                { if (!failed && result.m_eResult == EResult.k_EResultOK) CleanupLateLobby(result.m_ulSteamIDLobby); return; }
                _creating = false;
                if (failed || result.m_eResult != EResult.k_EResultOK) { Fail("Steam 방 생성 실패: " + result.m_eResult); return; }
                LobbyId = result.m_ulSteamIDLobby; HostId = UserId;
                var lobby = new CSteamID(LobbyId); SteamMatchmaking.SetLobbyJoinable(lobby, false);
                if (!SteamMatchmaking.SetLobbyMemberLimit(lobby, 4)) { Fail("Steam 방 정원을 설정할 수 없습니다."); return; }
                if (!Session.StartSteamConnection(true, HostId, Persona(UserId))) { Fail("Steam 서버를 준비하지 못했습니다."); return; }
                if (!SetData("game", RaidSteamPolicy.Game) || !SetData("build", Build) || !SetData("protocol", RaidWire.Version.ToString(CultureInfo.InvariantCulture)) || !SetData("host", HostId.ToString(CultureInfo.InvariantCulture)))
                { Fail("Steam 로비 정보를 게시할 수 없습니다."); return; }
                _publishedPhase = null; PublishPhase();
                if (LobbyId != 0) Status = "Steam 방 생성 완료 · 역할을 선택하고 준비하세요";
            });
            _calls.Add(call); call.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4));
        }
        public void RequestInvite(ulong lobby)
        {
            if (!Available || Session?.Ending == true || !new CSteamID(lobby).IsLobby()) return;
            if (lobby == PreviousLobby && Session != null && (Session.Mode == RaidSessionMode.Menu || Session.Mode == RaidSessionMode.Disconnected)) { RequestRecovery(); return; }
            bool occupied = LobbyId != 0 || Busy || (Session != null && Session.Mode != RaidSessionMode.Menu);
            if (!_invites.Request(lobby, LobbyId, occupied)) return;
            if (occupied) { QuarterViewCamera.LocalCamera?.ReleaseOrbit(); Status = "다른 Steam 방 초대 · 현재 세션을 나갈지 선택하세요"; }
        }
        public void AnswerInvite(bool accept)
        {
            if (!_invites.NeedsConfirmation) return;
            if (!accept) { _invites.Decline(); return; }
            _invites.Confirm();
            LeaveLobby(); // Keeps the explicitly confirmed pending invite across the menu reload.
            if (Session != null && Session.Mode != RaidSessionMode.Menu) Session.Disconnect();
        }
        private void JoinPending()
        {
            ulong lobbyId = _invites.Begin(); if (lobbyId == 0) return;
            if (lobbyId == PreviousLobby) { _invites.Finish(); RequestRecovery(); return; }
            int generation = ++_generation; var expectedSession = Session; _deadline = Time.unscaledTime + 30; Status = "Steam 로비 참가 중…";
            CallResult<LobbyEnter_t> call = null;
            call = CallResult<LobbyEnter_t>.Create((result, failed) =>
            {
                _calls.Remove(call); call.Dispose();
                if (generation != _generation || expectedSession == null || Session != expectedSession || expectedSession.Ending)
                { if (!failed && result.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) CleanupLateLobby(result.m_ulSteamIDLobby); return; }
                if (failed || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                { Fail("로비 참가 실패: 방이 닫혔거나 정원이 찼거나 참가가 차단되었습니다 (" + result.m_EChatRoomEnterResponse + ")"); return; }
                LobbyId = result.m_ulSteamIDLobby; _waitingHost = true; _nextCheck = 0;
                SteamMatchmaking.RequestLobbyData(new CSteamID(LobbyId));
            });
            _calls.Add(call); call.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)));
        }
        private Dictionary<string, string> Metadata()
        {
            var data = new Dictionary<string, string>();
            foreach (string key in new[] { "game", "build", "protocol", "host", "phase", "ready" }) data[key] = SteamMatchmaking.GetLobbyData(new CSteamID(LobbyId), key);
            return data;
        }
        private void CheckHost()
        {
            if (Session == null || Session.Ending) return;
            var data = Metadata();
            if (string.IsNullOrEmpty(data["game"])) { SteamMatchmaking.RequestLobbyData(new CSteamID(LobbyId)); return; }
            ulong owner = SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyId)).m_SteamID;
            string error = RaidSteamPolicy.Validate(data, Build, RaidWire.Version, owner);
            if (error != null)
            {
                if (data["ready"] != "1" && data["phase"] == "lobby" && data["game"] == RaidSteamPolicy.Game && data["build"] == Build && data["protocol"] == RaidWire.Version.ToString(CultureInfo.InvariantCulture) && data["host"] == owner.ToString(CultureInfo.InvariantCulture)) { Status = "호스트의 서버 준비를 기다리는 중…"; return; }
                Fail(error); return;
            }
            if (owner == 0 || owner == UserId || LobbyCount > 4 || !IsMember(UserId) || !IsMember(owner)) { Fail("유효하지 않은 Steam 로비/호스트입니다."); return; }
            HostId = owner; _waitingHost = false;
            if (!Session.StartSteamConnection(false, HostId, Persona(UserId))) { Fail("Steam 호스트에 연결할 수 없습니다."); return; }
            Status = "로비 참가 완료 · Steam Relay 접속 승인 대기 중…";
        }
        public bool Admit(ulong remote)
        {
            return Available && LobbyId != 0 && HostId == UserId && Session != null && !Session.Ending && (Session.KnowsSteamMember(remote) || (Session.CanJoinBattle && IsMember(remote) && LobbyCount <= 4)) &&
                SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyId)).m_SteamID == HostId && remote != UserId;
        }
        public int LobbyCount => Available && LobbyId != 0 ? SteamMatchmaking.GetNumLobbyMembers(new CSteamID(LobbyId)) : 0;
        public List<ulong> Members()
        {
            var list = new List<ulong>();
            for (int i = 0; i < LobbyCount; i++) list.Add(SteamMatchmaking.GetLobbyMemberByIndex(new CSteamID(LobbyId), i).m_SteamID);
            return list;
        }
        private bool IsMember(ulong id) => Members().Contains(id);
        private bool SetData(string key, string value) => SteamMatchmaking.SetLobbyData(new CSteamID(LobbyId), key, value);
        public void PublishPhase()
        {
            if (!Available || LobbyId == 0 || HostId != UserId || Session == null) return;
            string phase = Session.IsHostAuthority ? Session.BattlePhase.ToString().ToLowerInvariant() : "closed";
            if (_publishedPhase == phase) return;
            bool joinable = phase == "lobby";
            SteamMatchmaking.SetLobbyJoinable(new CSteamID(LobbyId), false);
            if (!SetData("phase", phase) || !SetData("ready", joinable ? "1" : "0") || !SteamMatchmaking.SetLobbyJoinable(new CSteamID(LobbyId), joinable))
            { Fail("Steam 로비 상태 갱신에 실패하여 방을 닫았습니다."); return; }
            _publishedPhase = phase;
        }
        public void InviteFriends()
        {
            if (!Available || LobbyId == 0 || Session == null || !Session.CanJoinBattle) return;
            if (!SteamUtils.IsOverlayEnabled()) { Status = "Steam 오버레이를 사용할 수 없습니다. Steam 실행 및 게임별 오버레이 설정을 확인하세요."; return; }
            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(LobbyId));
        }
        public void NoteSessionEnd(string reason) => Status = reason;
        public void CancelJoin() { _invites.Decline(); Fail("Steam 접속을 취소했습니다."); }
        private void Fail(string reason)
        {
            Status = reason; LeaveLobby();
            if (Session != null) Session.EndSteamSession(reason);
        }
        public void LeaveLobby()
        {
            ulong attempt = _invites.Attempt;
            ++_generation; _creating = _waitingHost = _recovering = _recoveryConnecting = _readOnlyRecovery = false; _invites.Finish(); _publishedPhase = null;
            if (Available && attempt != 0 && attempt != LobbyId) SteamMatchmaking.LeaveLobby(new CSteamID(attempt));
            if (Available && LobbyId != 0)
            {
                var lobby = new CSteamID(LobbyId);
                if (HostId == UserId) { SteamMatchmaking.SetLobbyJoinable(lobby, false); SteamMatchmaking.SetLobbyData(lobby, "ready", "0"); SteamMatchmaking.SetLobbyData(lobby, "phase", "closed"); }
                SteamMatchmaking.LeaveLobby(lobby);
            }
            LobbyId = HostId = 0;
        }
        private void CleanupLateLobby(ulong lobby)
        {
            // A cancelled operation may complete after a new attempt at the SAME lobby.
            if (RaidSteamPolicy.ShouldLeaveLateLobby(lobby, LobbyId, _invites.Attempt)) SteamMatchmaking.LeaveLobby(new CSteamID(lobby));
        }
        private void SetOverlay(bool open)
        {
            if (OverlayOpen != open) LocalInputBindings.QuarantineHeld();
            OverlayOpen = open;
            if (open)
            {
                foreach (var ui in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                    if (ui.enabled) { _blockedUI.Add(ui); ui.enabled = false; }
            }
            else
            {
                foreach (var ui in _blockedUI) if (ui != null) ui.enabled = true;
                _blockedUI.Clear();
            }
        }
        private void LateUpdate()
        {
            // Scene reloads can create a new EventSystem while the overlay is still open.
            if (OverlayOpen) SetOverlay(true);
        }
        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() { if (Instance == this) { Shutdown(); Instance = null; } }
        private void Shutdown()
        {
            if (!Available) return;
            Session?.CloseTransport(); LeaveLobby();
            foreach (var call in _calls) call.Dispose(); _calls.Clear();
            _invite?.Dispose(); _overlay?.Dispose(); _members?.Dispose(); _data?.Dispose();
            SteamAPI.Shutdown(); Available = false; SetOverlay(false);
        }
    }
}
