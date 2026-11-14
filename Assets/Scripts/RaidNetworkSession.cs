using System;
using System.Collections.Generic;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidRogue
{
    public enum RaidSessionMode { Menu, Single, Connecting, Host, Client, Closing, Disconnected, Restoring, Training, AiSpectator }

    [DefaultExecutionOrder(-50)]
    public sealed partial class RaidNetworkSession : MonoBehaviour
    {
        public static RaidNetworkSession Instance { get; private set; }
        private static string _returnMessage = "싱글플레이 또는 LAN 협동 레이드를 선택하세요.";
        public RaidSessionMode Mode { get; private set; } = RaidSessionMode.Menu;
        public string Status { get; private set; }
        public string SessionTag { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public bool Ending { get; private set; }
        private bool _reloading;
        private bool _menuOpen = true;
        private float _singleMenuTimeScale = 1;
        public bool MenuOpen
        {
            get => _menuOpen;
            set
            {
                if (_menuOpen == value) return;
                LocalInputBindings.QuarantineHeld();
                QuarterViewCamera.LocalCamera?.ReleaseOrbit();
                if (Mode == RaidSessionMode.Single || Mode == RaidSessionMode.Training)
                {
                    if (value) { _singleMenuTimeScale = Time.timeScale; Time.timeScale = 0; }
                    else Time.timeScale = _singleMenuTimeScale;
                }
                _menuOpen = value;
            }
        }
        public static bool CombatPreview => Instance != null && Instance.Mode != RaidSessionMode.Single && Instance.Mode != RaidSessionMode.Training && (Instance.IsRecovering || !Multiplayer || Instance.BattlePhase != RaidBattlePhase.Combat);
        public static bool Multiplayer => Instance != null && (Instance.Mode == RaidSessionMode.Host || Instance.Mode == RaidSessionMode.AiSpectator || Instance.Mode == RaidSessionMode.Client || Instance.Mode == RaidSessionMode.Restoring || Instance.Mode == RaidSessionMode.Disconnected || (Instance.Mode == RaidSessionMode.Connecting && Instance._resumeRequested));
        public static bool BlocksInput => Instance != null && (Instance.Ending || Instance.MenuOpen || (Instance.Mode != RaidSessionMode.Single && Instance.Mode != RaidSessionMode.Training && !Instance.ControlsReady) || (Instance.Mode != RaidSessionMode.Single && Instance.Mode != RaidSessionMode.Training && !Instance.HasLocalAvatar));
        public bool HasLocalAvatar => Multiplayer && _avatars.ContainsKey(_localId);
        public RaidNetworkMotor LocalMotor => _motors.TryGetValue(_localId, out var motor) ? motor : null;
        public int PlayerCount { get { int count = 0; foreach (var avatar in _avatars.Values) if (!avatar.IsBot) count++; return count; } }
        public int ConnectedPlayerCount { get { int count = 0; foreach (var avatar in _avatars.Values) if (!avatar.IsBot && !avatar.Automated) count++; return count; } }
        public IReadOnlyDictionary<int, NetworkRaidAvatar> Avatars => _avatars;
        private PlayerController _player;
        private IRaidTransport _transport;
        private int _server;
        public bool UsesSteam => _steamSession || (_transport != null && _transport.IsSteam);
        private readonly RaidRoster _roster = new RaidRoster();
        private readonly Dictionary<int, NetworkRaidAvatar> _avatars = new Dictionary<int, NetworkRaidAvatar>();
        private readonly Dictionary<int, RaidNetworkMotor> _motors = new Dictionary<int, RaidNetworkMotor>();
        private readonly List<RaidMoveInput> _prediction = new List<RaidMoveInput>();
        private sealed class Peer
        {
            public int Connection;
            public int Id;
            public uint Lease;
            public bool Voluntary, RecoveryFrame;
            public readonly RaidRestoreBarrier Barrier = new RaidRestoreBarrier();
            public float ConnectedAt, LastInput, CloseAt, LastRole, NextRecoveryFrame;
            public readonly RaidInputQueue Inputs = new RaidInputQueue();
        }
        private readonly List<Peer> _peers = new List<Peer>();
        private RaidPacket _config;
        private int _localId;
        private uint _sequence, _tick, _lastSnapshot;
        private float _accumulator, _connectTime, _lastServerPacket, _closeAt;
        private Vector3 _frameDirection;
        private Vector3 _queuedDashDirection;
        private bool _dashQueued, _returnRequested;
        private string _playerName;
        private bool IsHost => Mode == RaidSessionMode.Host || Mode == RaidSessionMode.AiSpectator;
        public static void Ensure(StageManager stage)
        {
            if (Instance == null) stage.gameObject.AddComponent<RaidNetworkSession>();
        }
        private void Awake() { Instance = this; Status = _returnMessage; _recovery = new RaidRecoverySlots(RecoverySettings); }
        private void Start()
        {
            _player = FindFirstObjectByType<PlayerController>();
            RoleSelectionManager.Instance?.CloseRoleSelectionModal();
            Time.timeScale = 1;
            Application.runInBackground = true;
            if (GetComponent<RaidConnectionUI>() == null) gameObject.AddComponent<RaidConnectionUI>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(Debug.isDebugBuild && GetComponent<AiSpectatorDevelopment>()==null)gameObject.AddComponent<AiSpectatorDevelopment>();
#endif
        }
        public void EnterTraining()
        {
            if (CampaignRuntime.Instance?.TrainingUnlocked != true)
            { Status = "훈련소는 정규 전투에 한 번 참여하면 해금됩니다."; return; }
            if (Ending || Mode != RaidSessionMode.Menu || _player == null || StageManager.Instance == null ||
                RaidSteamRuntime.Instance?.Busy == true || GetComponent<CharacterTrainingSession>() != null) return;
            // Only a fresh main menu can enter. No regular run is started, saved, or rewarded.
            Mode = RaidSessionMode.Training; MenuOpen = false;
            RoleSelectionManager.Instance?.CloseRoleSelectionModal();
            gameObject.AddComponent<CharacterTrainingSession>();
        }
        public void EnterTrainingFromLobby()
        {
            if (Mode != RaidSessionMode.Single || StageManager.Instance?.Progress.Phase != RaidBattlePhase.Lobby ||
                CampaignRuntime.Instance?.Resume != null || !RaidLobbyRoom.Active) return;
            Mode = RaidSessionMode.Menu;
            EnterTraining(); // Reuse the existing isolated practice session and unlock check.
        }
        public void SinglePlay()
        {
            if (Ending || Mode != RaidSessionMode.Menu || (RaidSteamRuntime.Instance != null && RaidSteamRuntime.Instance.Busy)) return;
            Mode = RaidSessionMode.Single; MenuOpen = false;
            RoleSelectionManager.Instance?.OpenRoleSelectionModal();
        }
        public void StartConnection(bool host, string name, string address)
        {
            if (Ending || Mode != RaidSessionMode.Menu || _player == null || (RaidSteamRuntime.Instance != null && RaidSteamRuntime.Instance.Busy)) return;
            if (!TryAddress(address, out var endpoint)) { Status = "주소 형식: 127.0.0.1:7777 또는 호스트의 LAN IPv4 주소"; return; }
            OpenTransport(() => new RaidLanTransport(host, endpoint), host, name, 0);
        }
        public bool StartSteamConnection(bool host, ulong hostId, string name)
        {
            if (Ending || Mode != RaidSessionMode.Menu || _player == null) return false;
            return OpenTransport(() => new RaidSteamTransport(host, hostId, id => RaidSteamRuntime.Instance.Admit(id)), host, name, RaidSteamRuntime.Instance.UserId);
        }
        private bool OpenTransport(Func<IRaidTransport> create, bool host, string name, ulong steamId)
        {
            _playerName = RaidRoster.CleanName(name);
            try
            {
                _transport = create(); _steamSession = _transport.IsSteam;
                if (host)
                {
                    Mode = RaidSessionMode.Host;
                    _config = new RaidPacket { Kind = RaidPacketKind.Welcome, Speed = _player.moveSpeed, Radius = _player.combatRadius,
                        Arena = _player.arenaBoundaryRadius, TurnSpeed = _player.rotationSmoothSpeed,
                        Dash = new DashSettings { distance = _player.dashSettings.distance, duration = _player.dashSettings.duration, cooldown = _player.dashSettings.cooldown, maximumCharges = _player.dashSettings.maximumCharges, preserveCast = _player.dashSettings.preserveCast } };
                    DisableBots(); _loadFrame = Time.frameCount; _loadingDeadline = Time.unscaledTime + StageManager.Instance.FlowSettings.loadingSeconds;
                    var member = _roster.Join(_playerName); _localId = member.Id; member.SteamId = steamId; member.Loaded = true;
                    AddAvatar(member.Id, member.Role, member.Name, SpawnPoint(member.Role)); ReconcileBots(); SyncLobbyAvatar(member);
                    Status = UsesSteam ? "Steam 호스트 · 역할 선택 후 준비하세요" : "LAN 호스트 · 같은 빌드/씬으로 참가하세요";
                    RestoreCampaignLobby();
                }
                else
                {
                    Mode = RaidSessionMode.Connecting; _server = _transport.Server; _connectTime = Time.unscaledTime;
                    Status = UsesSteam ? "Steam 호스트 연결 중…" : "LAN 호스트 연결 중…";
                }
                return true;
            }
            catch (Exception error)
            {
                DisposeDriver();
                if (Mode == RaidSessionMode.Host || Mode == RaidSessionMode.Client) RequestReturn("연결 실패: " + error.Message);
                Mode = _resumeRequested ? RaidSessionMode.Disconnected : RaidSessionMode.Menu; Status = "연결 실패: " + error.Message; return false;
            }
        }
        public static bool TryAddress(string address, out NetworkEndpoint endpoint)
        {
            endpoint = default;
            string[] parts = (address ?? "").Trim().Split(':');
            if (parts.Length > 2 || parts.Length == 0) return false;
            ushort port = 7777;
            if (parts.Length == 2 && (!ushort.TryParse(parts[1], out port) || port == 0)) return false;
            return NetworkEndpoint.TryParse(parts[0], port, out endpoint, NetworkFamily.Ipv4);
        }
        private void DisableBots()
        {
            RoleSelectionManager.Instance?.CloseRoleSelectionModal();
            foreach (var bot in FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None)) { _standbyBots.Add(bot); bot.gameObject.SetActive(false); }
            FindFirstObjectByType<BossController>()?.StopEncounterEffects();
            CombatMetricsManager.Instance?.Ledger.ResetRun();
            Time.timeScale = 1;
        }
        public void SampleInput(Vector3 direction, bool dash)
        {
            _frameDirection = Vector3.ClampMagnitude(direction, 1);
            if (dash && !BlocksInput && !_dashQueued)
            {
                _queuedDashDirection = _frameDirection;
                _dashQueued = true;
            }
            if (LocalGameplayInput.Blocked) { _frameDirection = Vector3.zero; _dashQueued = false; }
        }
        private void Update()
        {
            if (_returnRequested || (Mode == RaidSessionMode.Closing && Time.unscaledTime >= _closeAt)) { ReloadMenu(); return; }
            if (Mode == RaidSessionMode.AiSpectator)
            {
                _accumulator = Mathf.Min(_accumulator + Time.unscaledDeltaTime, RaidWire.Step * 4);
                while (_accumulator >= RaidWire.Step) { _accumulator -= RaidWire.Step; Tick(); }
                return;
            }
            if (_transport == null || !_transport.IsCreated) return;
            _transport.Update();
            if (IsHost) PollHost(); else if (Mode == RaidSessionMode.Client || Mode == RaidSessionMode.Connecting || Mode == RaidSessionMode.Restoring) PollClient();
            if (Mode == RaidSessionMode.Connecting && Time.unscaledTime - _connectTime > (UsesSteam ? 30 : 8)) { if (_resumeRequested) { UnexpectedClientLoss("호스트 재접속 시간 초과"); return; } RequestReturn(UsesSteam ? "Steam 호스트 접속 시간 초과: 호스트와 Steam 연결 상태를 확인하세요." : "접속 시간 초과: 주소, 포트, 호스트 실행 및 방화벽을 확인하세요."); }
            if ((Mode == RaidSessionMode.Client || Mode == RaidSessionMode.Restoring) && Time.unscaledTime - _lastServerPacket > RecoverySettings.silenceSeconds) { UnexpectedClientLoss("호스트 응답이 끊겼습니다"); return; }
            if (Mode == RaidSessionMode.Restoring && Time.unscaledTime - _restoreStarted > RecoverySettings.restoreSeconds) { UnexpectedClientLoss("현재 상태 복구 시간이 초과되었습니다"); return; }
            if (Multiplayer && !_returnRequested)
            {
                _accumulator = Mathf.Min(_accumulator + Time.unscaledDeltaTime, RaidWire.Step * 4);
                while (_accumulator >= RaidWire.Step) { _accumulator -= RaidWire.Step; Tick(); }
            }
            if (Mode == RaidSessionMode.Closing && Time.unscaledTime >= _closeAt) _returnRequested = true;
            if (_returnRequested) ReloadMenu();
        }
        private void PollHost()
        {
            int connection;
            while ((connection = _transport.Accept()) != 0)
            {
                if (_peers.Count >= 8) { _transport.Disconnect(connection); continue; }
                _peers.Add(new Peer { Connection = connection, ConnectedAt = Time.unscaledTime });
            }
            for (int i = _peers.Count - 1; i >= 0; i--)
            {
                Peer peer = _peers[i]; bool remove = false;
                RaidTransportEvent type;
                int budget = 64;
                while (budget-- > 0 && (type = _transport.Poll(peer.Connection, out var bytes)) != RaidTransportEvent.Empty)
                {
                    if (type == RaidTransportEvent.Disconnected) { peer.Voluntary |= _transport.TakeCloseCode(peer.Connection) == 1002; remove = true; break; }
                    if (type != RaidTransportEvent.Data || !RaidWire.Decode(bytes, out var packet) || peer.CloseAt > 0) continue;
                    peer.LastInput = Time.unscaledTime;
                    if ((packet.Kind == RaidPacketKind.Hello || packet.Kind == RaidPacketKind.ResumeHello) && peer.Id == 0)
                    {
                        string[] hello = packet.Text.Split('\n');
                        if (hello.Length != 2 || hello[0] != HelloTag) { Reject(peer, "호스트와 같은 씬/테스트 빌드를 사용하세요."); continue; }
                        if (UsesSteam && _campaignResumed)
                        {
                            ulong originalUser = _transport.RemoteSteamId(peer.Connection);
                            if (!AcceptsCampaignUser(originalUser)) { Reject(peer, "저장된 원래 참가자만 이 런에 복귀할 수 있습니다."); continue; }
                            TryResumePeer(peer); continue;
                        }
                        if (packet.Kind == RaidPacketKind.ResumeHello) { TryResumePeer(peer); continue; }
                        if (UsesSteam && _recovery.Find(_transport.RemoteSteamId(peer.Connection)) != null) { Reject(peer, "이미 배정된 자리입니다. 이전 게임으로 복귀를 사용하세요."); continue; }
                        if (!CanJoinBattle) { Reject(peer, "현재 런에는 참가할 수 없습니다. 최초 대기실에서만 참가할 수 있습니다."); continue; }
                        ulong steamId = UsesSteam ? _transport.RemoteSteamId(peer.Connection) : 0;
                        if (UsesSteam && (!RaidSteamRuntime.Instance.Admit(steamId) || _roster.Members.Exists(m => m.SteamId == steamId)))
                        { Reject(peer, "Steam 인증 사용자와 로비 참가 정보가 일치하지 않습니다."); continue; }
                        var member = _roster.Join(UsesSteam ? RaidSteamRuntime.Instance.Persona(steamId) : hello[1]);
                        if (member == null) { Reject(peer, "방이 가득 찼습니다. 최대 4명(호스트 포함)입니다."); continue; }
                        member.SteamId = steamId;
                        peer.Id = member.Id; peer.LastInput = Time.unscaledTime;
                        AddAvatar(member.Id, member.Role, member.Name, SpawnPoint(member.Role)); ReconcileBots(); SyncLobbyAvatar(member);
                        RegisterRecoveryMember(member); peer.Lease = _recovery.Actor(member.Id)?.Lease ?? 0;
                        SendWelcome(peer, false);
                        SendSnapshot(peer.Connection);
                    }
                    else if (peer.Id != 0 && packet.Kind == RaidPacketKind.Leave) { peer.Voluntary = true; _transport.Disconnect(peer.Connection); remove = true; break; }
                    else if (peer.Id != 0 && packet.Kind == RaidPacketKind.RestoreReady) AcceptRestore(peer, packet);
                    else if (peer.Id != 0 && packet.Kind == RaidPacketKind.CampaignAck) AcknowledgeCampaign(peer, packet.Text);
                    else if (peer.Id != 0 && packet.Kind == RaidPacketKind.RestoreComplete && (uint)packet.Role == peer.Lease && PeerControls(peer)) peer.RecoveryFrame = false;
                    else if (PeerControls(peer) && packet.Kind == RaidPacketKind.Ready) SetReady(peer.Id, packet.Id != 0);
                    else if (PeerControls(peer) && packet.Kind == RaidPacketKind.SceneReady) SetLoaded(peer.Id, packet.Id);
                    else if (PeerControls(peer) && packet.Kind == RaidPacketKind.CombatRequest)
                    {
                        try { ExecuteAction(peer.Id, JsonUtility.FromJson<RaidActionRequest>(packet.Text)); } catch (ArgumentException) { ActionFailure(peer.Id, "잘못된 행동 요청입니다"); }
                    }
                    else if (peer.Id != 0 && packet.Kind == RaidPacketKind.Ping)
                        Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.Pong, Speed = packet.Speed, Radius = ServerTime }, false);
                    else if (PeerControls(peer) && packet.Kind == RaidPacketKind.Input)
                    {
                        foreach (var input in packet.Inputs) if (peer.Inputs.Add(input)) peer.LastInput = Time.unscaledTime;
                    }
                    else if (PeerControls(peer) && packet.Kind == RaidPacketKind.Role && Time.unscaledTime - peer.LastRole >= 0.25f)
                    { peer.LastRole = Time.unscaledTime; ChooseRole(peer.Id, packet.Role, peer.Connection); }
                }
                if ((peer.CloseAt > 0 && Time.unscaledTime >= peer.CloseAt) || (peer.Id == 0 && Time.unscaledTime - peer.ConnectedAt > 6) || (peer.Id != 0 && Time.unscaledTime - peer.LastInput > RecoverySettings.silenceSeconds)) { _transport.Disconnect(peer.Connection, transient: peer.CloseAt == 0); remove = true; }
                if (remove) { DropPeer(peer); _peers.RemoveAt(i); }
            }
            FinishRewardsIfReady();
        }
        private void PollClient()
        {
            RaidTransportEvent type;
            while ((type = _transport.Poll(_server, out var bytes)) != RaidTransportEvent.Empty)
            {
                if (type == RaidTransportEvent.Connected)
                    Send(_server, new RaidPacket { Kind = _resumeRequested ? RaidPacketKind.ResumeHello : RaidPacketKind.Hello, Text = HelloTag + "\n" + _playerName }, true);
                else if (type == RaidTransportEvent.Disconnected)
                {
                    int code = _transport.TakeCloseCode(_server);
                    if (code == 1003 && _resumeRequested) UnexpectedClientLoss("같은 Steam 계정의 기존 연결을 정리 중입니다. 잠시 후 다시 시도하세요");
                    else if (code == 1000 || code == 1001 || code == 1003) { RaidSteamRuntime.Instance?.ForgetRecovery(); RequestReturn("호스트가 종료했거나 참가를 승인할 수 없는 상태입니다."); }
                    else if (UsesSteam && (Mode != RaidSessionMode.Connecting || _resumeRequested)) UnexpectedClientLoss("네트워크 연결이 끊겼습니다");
                    else RequestReturn("호스트 접속에 실패했습니다.");
                    return;
                }
                else if (type == RaidTransportEvent.Data && RaidWire.Decode(bytes, out var packet))
                {
                    _lastServerPacket = Time.unscaledTime;
                    if (packet.Kind == RaidPacketKind.Welcome && Mode == RaidSessionMode.Connecting)
                    {
                        _localId = packet.Id; _config = packet; _localLease = packet.Tick;
                        if (_avatars.Count == 0) DisableBots();
                        _controlReady = packet.Role == 0; _resumeRequested = packet.Role != 0;
                        Mode = packet.Role != 0 ? RaidSessionMode.Restoring : RaidSessionMode.Client; _restoreStarted = Time.unscaledTime;
                        Status = packet.Role != 0 ? "현재 전투 상태 복구 중…" : "연결됨 · 역할 선택 후 준비하세요";
                        if (UsesSteam) RaidSteamRuntime.Instance?.RememberRecovery();
                    }
                    else if (packet.Kind == RaidPacketKind.Snapshot && (Mode == RaidSessionMode.Client || Mode == RaidSessionMode.Restoring) && packet.Tick > _lastSnapshot)
                    { _lastSnapshot = packet.Tick; ApplySnapshot(packet); }
                    else if ((Mode == RaidSessionMode.Client || Mode == RaidSessionMode.Restoring) && (packet.Kind == RaidPacketKind.CombatFrame || packet.Kind == RaidPacketKind.CombatEffect || packet.Kind == RaidPacketKind.Pong)) ReceiveCombatPacket(packet);
                    else if (packet.Kind == RaidPacketKind.PartyNotice) ShowPartyNotice(packet.Text);
                    else if (packet.Kind == RaidPacketKind.CampaignClear) ReceiveCampaignReceipt(packet.Text);
                    else if (packet.Kind == RaidPacketKind.Notice)
                    {
                        if (packet.Text.StartsWith("재접속:")) { UnexpectedClientLoss(packet.Text.Substring(4)); return; }
                        if (packet.Text.StartsWith("종료:")) { RaidSteamRuntime.Instance?.ForgetRecovery(); RequestReturn(packet.Text.Substring(3)); return; }
                        Status = packet.Text; _player.combatController.ReportNetworkFailure(packet.Text);
                    }
                }
            }
        }
        private void Tick()
        {
            _tick++;
            if (Mode != RaidSessionMode.AiSpectator && HasLocalAvatar && ControlsReady && (IsHost || _prediction.Count < 90))
            {
                var moveDirection = _dashQueued ? _queuedDashDirection : _frameDirection;
                var input = new RaidMoveInput { Sequence = ++_sequence, MotionVersion = _avatars[_localId].MotionVersion, X = moveDirection.x, Z = moveDirection.z, Dash = _dashQueued, SupportId = RaidCombatAuthority.Id(TargetingManager.Instance!=null&&TargetingManager.Instance.HasSupportSelection?TargetingManager.Instance.SupportTarget:_player.transform) };
                _dashQueued = false;
                if (LocalGameplayInput.Blocked) { input.X = input.Z = 0; input.Dash = false; }
                if (IsHost) StepHostAvatar(_avatars[_localId], input);
                else { _config.Speed = _player.NetworkMoveSpeed; Vector3 before = LocalMotor.Position; LocalMotor.Step(input, _player.transform, _config, !_avatars[_localId].MovementLocked); _player.SetNetworkMoving((before - LocalMotor.Position).sqrMagnitude > .000001f); }
                if (!IsHost)
                {

                    _prediction.Add(input);
                    var packet = new RaidPacket { Kind = RaidPacketKind.Input };
                    for (int i = 0; i < Math.Min(8, _prediction.Count); i++) packet.Inputs.Add(_prediction[i]);
                    Send(_server, packet, false);
                }
                _avatars[_localId].Display(LocalMotor.Position, LocalMotor.Yaw, LocalMotor.Dash.Active);
            }
            if (!IsHost)
            {
                // Keep retransmitting unacknowledged input when prediction is capped during a latency spike.
                if (ControlsReady && _prediction.Count >= 90)
                {
                    var pending = new RaidPacket { Kind = RaidPacketKind.Input };
                    for (int i = 0; i < Math.Min(8, _prediction.Count); i++) pending.Inputs.Add(_prediction[i]);
                    Send(_server, pending, false);
                }
                return;
            }
            StepReplacementAI();
            foreach (var peer in _peers)
            {
                if (!PeerControls(peer) || !_motors.TryGetValue(peer.Id, out var motor)) continue;
                var input = peer.Inputs.Next();
                if (Time.unscaledTime - peer.LastInput > 0.3f) { input.X = input.Z = 0; input.Dash = false; }
                StepHostAvatar(_avatars[peer.Id], input);
            }
            foreach (var peer in _peers) if (peer.Id != 0 && peer.CloseAt == 0) SendSnapshot(peer.Connection);
        }
        private void SendSnapshot(int connection) => Send(connection, BuildSnapshot(), false);
        private RaidPacket BuildSnapshot()
        {
            var packet = new RaidPacket { Kind = RaidPacketKind.Snapshot, Tick = _tick + 1 };
            foreach (var avatar in _avatars.Values)
            {
                var member = _roster.Members.Find(m => m.Id == avatar.Id) ?? new RaidRoster.Member { Id = avatar.Id, Role = avatar.Role, Name = avatar.DisplayName };
                if (avatar.IsBot) { _motors[member.Id].Position = avatar.transform.position; _motors[member.Id].Yaw = avatar.transform.eulerAngles.y; }
                var peer = _peers.Find(p => p.Id == member.Id);
                var pose = _motors[member.Id].Snapshot(member, peer != null ? peer.Inputs.Acknowledged : _sequence);
                pose.SteamId = member.SteamId; pose.Ready = member.Ready; pose.Loaded = member.Loaded;
                pose.MovementLocked = avatar.MovementLocked; pose.MotionVersion = avatar.MotionVersion; pose.Bot = avatar.IsBot;
                pose.MoveSpeed = avatar.GetComponent<PlayerController>()?.NetworkMoveSpeed ?? 7.8f;
                packet.Poses.Add(pose);
            }
            return packet;
        }
        private void ApplySnapshot(RaidPacket packet)
        {
            var live = new HashSet<int>();
            foreach (var pose in packet.Poses)
            {
                live.Add(pose.Id);
                if (!_avatars.ContainsKey(pose.Id)) AddAvatar(pose.Id, pose.Role, pose.Name, new Vector3(pose.X, 1, pose.Z));
                var avatar = _avatars[pose.Id]; avatar.SteamId = pose.SteamId; avatar.Ready = pose.Ready; avatar.Loaded = pose.Loaded; avatar.SetIdentity(pose.Role, pose.Name); avatar.MovementLocked = pose.MovementLocked; avatar.IsBot = pose.Bot;
                if (pose.Id == _localId && avatar.MotionVersion != pose.MotionVersion) _prediction.Clear();
                avatar.MotionVersion = pose.MotionVersion;
                var motor = _motors[pose.Id]; motor.Restore(pose);
                if (pose.Id == _localId)
                {
                    _config.Speed = pose.MoveSpeed; _player.moveSpeed = pose.MoveSpeed;
                    _prediction.RemoveAll(input => input.Sequence <= pose.Ack || input.MotionVersion != pose.MotionVersion);
                    foreach (var input in _prediction) motor.Step(input, avatar.transform, _config, !avatar.MovementLocked);
                }
                avatar.Display(motor.Position, motor.Yaw, motor.Dash.Active);
            }
            foreach (int id in new List<int>(_avatars.Keys)) if (!live.Contains(id)) RemoveAvatar(id);
        }
        private void AddAvatar(int id, int role, string name, Vector3 position)
        {
            if (_avatars.ContainsKey(id)) return;
            bool local = id == _localId;
            GameObject obj = local ? _player.gameObject : RaidPrimitive.Create(PrimitiveType.Capsule);
            if (!local) obj.name = "NetworkPlayer_" + id;
            var avatar = obj.GetComponent<NetworkRaidAvatar>() ?? obj.AddComponent<NetworkRaidAvatar>();
            avatar.Configure(id, local, _config.Radius); avatar.SetIdentity(role, name);
            _avatars.Add(id, avatar); _motors.Add(id, new RaidNetworkMotor { Position = position });
            EnsureCombatActor(avatar); avatar.Display(position, 0, false);
        }
        private void RemoveAvatar(int id)
        {
            if (!_avatars.TryGetValue(id, out var avatar)) return;
            TargetingManager.Instance?.ForgetRemovedTarget(avatar.transform);
            if (!avatar.IsLocal) { avatar.gameObject.SetActive(false); if (avatar.GetComponent<PartyBotAI>() == null) Destroy(avatar.gameObject); }
            _avatars.Remove(id); _motors.Remove(id);
        }
        public List<Transform> OrderedParty()
        {
            var ids = new List<int>(_avatars.Keys); ids.Sort();
            if (ids.Remove(_localId)) ids.Insert(0, _localId);
            var result = new List<Transform>(); foreach (int id in ids) result.Add(_avatars[id].transform); return result;
        }
        public void SelectRole(int role)
        {
            if (AiSpectatorActive || !HasLocalAvatar || !ControlsReady) return;
            if (IsHost) ChooseRole(_localId, role, default);
            else Send(_server, new RaidPacket { Kind = RaidPacketKind.Role, Role = role }, true);
        }
        private void ChooseRole(int id, int role, int connection)
        {
            if (!CanEditLobbyBuild) { ActionFailure(id, "전투 시작 후 또는 이어하기 대기실에서는 역할을 변경할 수 없습니다"); return; }
            bool success = _roster.SelectRole(id, role);
            string message = success ? "역할이 변경되었습니다." : "이미 다른 플레이어가 선택한 역할입니다.";
            if (success) { var member = _roster.Members.Find(m => m.Id == id); member.Ready=false; _avatars[id].SetIdentity(role, member.Name); CharacterActor.Ensure(_avatars[id].gameObject).UseLegacyRole(); _avatars[id].RefreshJobLabel(); SyncLobbyAvatar(member); ReconcileBots(); }
            if (id == _localId) Status = message; else Send(connection, new RaidPacket { Kind = RaidPacketKind.Notice, Text = message }, true);
        }
        private static Vector3 SpawnPoint(int role) => new Vector3((role - 2.5f) * 2.5f, 1, -7);
        private void Reject(Peer peer, string text)
        {
            if (peer.CloseAt > 0) return;
            Debug.LogWarning($"[Raid {SessionTag}] epoch={BattleEpoch} actor={peer.Id} rejected: {text}");
            Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.Notice, Text = "종료:" + text }, true); peer.CloseAt = Time.unscaledTime + 0.5f;
        }
        private void Send(int connection, RaidPacket packet, bool reliable)
        {
            if (_transport == null || !_transport.IsCreated || connection == 0) return;
            _transport.Send(connection, RaidWire.Encode(packet), reliable);
        }
        public void Disconnect()
        {
            if (!Ending && Mode != RaidSessionMode.Menu && Mode != RaidSessionMode.Training && GetComponent<RaidConnectionUI>() is RaidConnectionUI ui)
            { ui.ConfirmCampaignExit(false); return; }
            DisconnectConfirmed();
        }
        internal void DisconnectConfirmed()
        {
            if (Ending) return;
            if (IsHost)
                foreach (var peer in _peers) Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.Notice, Text = "종료:호스트가 방을 닫았습니다." }, true);
            else if (_transport != null && _transport.IsCreated && _server != 0)
            { Send(_server, new RaidPacket { Kind = RaidPacketKind.Leave }, true); _transport.Disconnect(_server, true); }
            if (UsesSteam) RaidSteamRuntime.Instance?.ForgetRecovery();
            _returnMessage = "연결을 종료했습니다.";
            if (UsesSteam) RaidSteamRuntime.Instance?.NoteSessionEnd(_returnMessage);
            EndProgress(_returnMessage);
            if (_transport != null && _transport.IsCreated) { Mode = RaidSessionMode.Closing; _closeAt = Time.unscaledTime + 0.35f; }
            else ReloadMenu();
        }
        public void FailSession(string reason)
        {
            if (Ending) return;
            if (IsHost) foreach (var peer in _peers) Send(peer.Connection, new RaidPacket { Kind = RaidPacketKind.Notice, Text = "종료:" + reason }, true);
            _returnMessage = reason; EndProgress(reason);
            Mode = RaidSessionMode.Closing; _closeAt = Time.unscaledTime + .35f;
            if (_transport == null || !_transport.IsCreated) RequestReturn(reason);
        }
        private void EndProgress(string reason)
        {
            if (Ending) return;
            AiSpectatorRecordSessionStop();
            Ending = true; Status = reason; _controlReady = false; _frameDirection = Vector3.zero; _dashQueued = false;
            _prediction.Clear(); foreach (var peer in _peers) peer.Inputs.DiscardPending();
            StageManager.Instance?.EndSessionProgress(); ClearReplicaMechanics();
            QuarterViewCamera.LocalCamera?.ReleaseOrbit();
            LocalCameraSettingsUI.CloseForSessionEnd(); LocalGameplayInput.ResetSession();
            MenuOpen = true; Time.timeScale = 1;
            SetBattlePhase(RaidBattlePhase.Closed, reason);
            RaidSteamRuntime.Instance?.NoteSessionEnd(reason); RaidSteamRuntime.Instance?.LeaveLobby();
        }
        private void RequestReturn(string message) { _returnMessage = message; EndProgress(message); _returnRequested = true; }
        private void ReloadMenu()
        {
            if (_reloading) return;
            _reloading = true; EndProgress(_returnMessage);
            DisposeDriver(); Time.timeScale = 1;
            var scene = SceneManager.GetActiveScene(); SceneManager.LoadScene(scene.path);
        }
        private void DisposeDriver() { _transport?.Dispose(); _transport = null; }
        public void CloseTransport() => DisposeDriver();
        private void OnDestroy() { StageManager.Instance?.EndSessionProgress(); LocalGameplayInput.ResetSession(); DisposeDriver(); if (Instance == this) { RaidSteamRuntime.Instance?.LeaveLobby(); Instance = null; } }
        private void OnApplicationQuit() => DisposeDriver();
    }
}
