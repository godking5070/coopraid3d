using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;

namespace RaidRogue
{
    // SteamNetworkingSockets authenticates the remote identity and uses Steam Datagram Relay.
    // No UDP/IP connection, client-supplied Steam ID, or secondary SDK wrapper is involved.
    public sealed class RaidSteamTransport : IRaidTransport
    {
        private readonly Dictionary<int, HSteamNetConnection> _connections = new Dictionary<int, HSteamNetConnection>();
        private readonly Dictionary<int, ulong> _identities = new Dictionary<int, ulong>();
        private readonly Dictionary<int, Queue<RaidTransportEvent>> _events = new Dictionary<int, Queue<RaidTransportEvent>>();
        private readonly Dictionary<int, int> _closeCodes = new Dictionary<int, int>();
        private readonly HashSet<int> _exposed = new HashSet<int>();
        private readonly Queue<int> _accepted = new Queue<int>();
        private readonly IntPtr[] _message = new IntPtr[1];
        private readonly Func<ulong, bool> _admit;
        private readonly bool _host;
        private readonly ulong _expectedHost;
        private HSteamListenSocket _listen;
        private Callback<SteamNetConnectionStatusChangedCallback_t> _status;
        public bool IsCreated { get; private set; }
        public bool IsSteam => true;
        public int Server { get; private set; }
        public RaidSteamTransport(bool host, ulong hostId, Func<ulong, bool> admit)
        {
            _host = host; _expectedHost = hostId; _admit = admit;
            _status = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
            try
            {
                if (host)
                {
                    _listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
                    if (_listen == HSteamListenSocket.Invalid) throw new InvalidOperationException("Steam P2P 서버를 열 수 없습니다.");
                }
                else
                {
                    var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(hostId);
                    var connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
                    if (connection == HSteamNetConnection.Invalid) throw new InvalidOperationException("Steam 호스트 연결을 시작할 수 없습니다.");
                    Server = unchecked((int)connection.m_HSteamNetConnection); Track(Server, connection, hostId); _exposed.Add(Server);
                }
                IsCreated = true;
            }
            catch { Dispose(); throw; }
        }
        private void Track(int id, HSteamNetConnection handle, ulong identity)
        { _connections[id] = handle; _identities[id] = identity; if (!_events.ContainsKey(id)) _events[id] = new Queue<RaidTransportEvent>(); }
        private void OnStatus(SteamNetConnectionStatusChangedCallback_t change)
        {
            if (!IsCreated) return;
            int id = unchecked((int)change.m_hConn.m_HSteamNetConnection);
            var state = change.m_info.m_eState;
            if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting && _host)
            {
                if (change.m_info.m_hListenSocket != _listen || _connections.ContainsKey(id)) return;
                ulong remote = change.m_info.m_identityRemote.GetSteamID64();
                if (remote == 0 || !_admit(remote) || _connections.Count >= 3 || _identities.ContainsValue(remote))
                { SteamNetworkingSockets.CloseConnection(change.m_hConn, _identities.ContainsValue(remote) ? 1003 : 1001, "Lobby admission denied", false); return; }
                Track(id, change.m_hConn, remote);
                if (SteamNetworkingSockets.AcceptConnection(change.m_hConn) != EResult.k_EResultOK) { Disconnect(id); return; }
            }
            else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected && _connections.ContainsKey(id))
            {
                // Obtain the authenticated identity again at the completed transport handshake.
                if (!SteamNetworkingSockets.GetConnectionInfo(change.m_hConn, out var info)) { Disconnect(id); return; }
                ulong remote = info.m_identityRemote.GetSteamID64();
                if (remote != _identities[id] || (_host ? !_admit(remote) : remote != _expectedHost)) { Disconnect(id); return; }
                if (_host) { if (_exposed.Add(id)) _accepted.Enqueue(id); } else _events[id].Enqueue(RaidTransportEvent.Connected);
            }
            else if (_connections.ContainsKey(id) && (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
                state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally))
            { _closeCodes[id] = change.m_info.m_eEndReason; Disconnect(id); }
        }
        public void Update() { } // RaidSteamRuntime owns SteamAPI.RunCallbacks exactly once per frame.
        public int Accept() => _accepted.Count > 0 ? _accepted.Dequeue() : 0;
        public RaidTransportEvent Poll(int connection, out byte[] bytes)
        {
            bytes = null;
            if (_events.TryGetValue(connection, out var events) && events.Count > 0)
            {
                var result = events.Dequeue(); if (result == RaidTransportEvent.Disconnected) { _events.Remove(connection); _exposed.Remove(connection); } return result;
            }
            if (!_connections.TryGetValue(connection, out var handle)) return RaidTransportEvent.Empty;
            int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(handle, _message, 1);
            if (count <= 0) return RaidTransportEvent.Empty;
            try
            {
                var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_message[0]);
                if (message.m_cbSize > 0 && message.m_cbSize <= RaidWire.MaxBytes && message.m_identityPeer.GetSteamID64() == RemoteSteamId(connection))
                { bytes = new byte[message.m_cbSize]; Marshal.Copy(message.m_pData, bytes, 0, bytes.Length); }
                return RaidTransportEvent.Data;
            }
            finally { SteamNetworkingMessage_t.Release(_message[0]); _message[0] = IntPtr.Zero; }
        }
        public bool Send(int connection, byte[] bytes, bool reliable)
        {
            if (!_connections.TryGetValue(connection, out var handle) || bytes.Length > RaidWire.MaxBytes) return false;
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { return SteamNetworkingSockets.SendMessageToConnection(handle, pinned.AddrOfPinnedObject(), (uint)bytes.Length,
                reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoDelay, out _) == EResult.k_EResultOK; }
            finally { pinned.Free(); }
        }
        public ulong RemoteSteamId(int connection) => _identities.TryGetValue(connection, out ulong id) ? id : 0;
        public int TakeCloseCode(int connection) { _closeCodes.TryGetValue(connection, out int code); _closeCodes.Remove(connection); return code; }
        public void Disconnect(int connection, bool voluntary = false, bool transient = false)
        {
            if (_connections.TryGetValue(connection, out var handle)) SteamNetworkingSockets.CloseConnection(handle, voluntary ? 1002 : transient ? 1004 : 1000, voluntary ? "Voluntary leave" : "Session closed", false);
            _connections.Remove(connection); _identities.Remove(connection);
            if (_exposed.Contains(connection) && _events.TryGetValue(connection, out var events)) { events.Clear(); events.Enqueue(RaidTransportEvent.Disconnected); }
            else { _events.Remove(connection); _closeCodes.Remove(connection); }
        }
        public void Dispose()
        {
            IsCreated = false; _status?.Dispose(); _status = null;
            foreach (int id in new List<int>(_connections.Keys)) Disconnect(id);
            if (_listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(_listen);
            _listen = HSteamListenSocket.Invalid; _events.Clear(); _accepted.Clear(); _exposed.Clear(); _closeCodes.Clear();
        }
    }
}
