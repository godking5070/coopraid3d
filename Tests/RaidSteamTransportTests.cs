// Deterministic transport lifecycle tests. This fake replaces only the native Steam API boundary;
// production RaidSteamTransport is also compiled separately against the real pinned SDK.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RaidRogue;
using Steamworks;

public static class RaidSteamTransportTests
{
    private static int _checks;
    private static void Check(bool condition, string name) { _checks++; if (!condition) throw new Exception(name); }
    public static void Main()
    {
        var members = new HashSet<ulong> { 100, 200, 300, 400 };
        using (var host = new RaidSteamTransport(true, 1, id => members.Contains(id)))
        {
            SteamNetworkingSockets.Change(11, 100, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting);
            Check(host.Accept() == 0 && SteamNetworkingSockets.Accepted.Contains(11), "Native handshake precedes game acceptance");
            SteamNetworkingSockets.Change(11, 100, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected);
            Check(host.Accept() == 11 && host.RemoteSteamId(11) == 100, "Authenticated remote identity");
            SteamNetworkingSockets.Change(11, 100, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected);
            Check(host.Accept() == 0, "Duplicate connected callback cannot create second peer");
            SteamNetworkingSockets.Change(12, 100, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting);
            Check(SteamNetworkingSockets.Closed.Contains(12), "Duplicate account rejected");
            SteamNetworkingSockets.Change(13, 999, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting);
            Check(SteamNetworkingSockets.Closed.Contains(13), "Nonmember transport rejected");
            SteamNetworkingSockets.Change(14, 200, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting);
            SteamNetworkingSockets.Change(15, 300, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting);
            SteamNetworkingSockets.Change(16, 400, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting);
            Check(SteamNetworkingSockets.Closed.Contains(16), "Three remote connections maximum, including pending handshakes");
            byte[] payload = RaidWire.Encode(new RaidPacket { Kind = RaidPacketKind.Ready, Id = 1 });
            Check(host.Send(11, payload, true) && SteamNetworkingSockets.LastFlags == Constants.k_nSteamNetworkingSend_Reliable, "Control traffic reliable");
            Check(host.Send(11, payload, false) && SteamNetworkingSockets.LastFlags == Constants.k_nSteamNetworkingSend_UnreliableNoDelay, "Snapshots use immediate unreliable path");
            Check(!host.Send(11, new byte[RaidWire.MaxBytes + 1], true), "Oversized outbound packet rejected");
            SteamNetworkingSockets.Enqueue(11, 100, payload);
            Check(host.Poll(11, out var bytes) == RaidTransportEvent.Data && RaidWire.Decode(bytes, out _), "Real protocol receives native message bytes");
            Check(SteamNetworkingMessage_t.Released == 1, "Native message always released");
            SteamNetworkingSockets.Enqueue(11, 999, payload);
            Check(host.Poll(11, out bytes) == RaidTransportEvent.Data && bytes == null, "Message identity must match authenticated peer");
            SteamNetworkingSockets.Enqueue(11, 100, new byte[RaidWire.MaxBytes + 1]);
            Check(host.Poll(11, out bytes) == RaidTransportEvent.Data && bytes == null && SteamNetworkingMessage_t.Released == 3, "Invalid inbound message released");
            members.Remove(200);
            SteamNetworkingSockets.Change(14, 200, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected);
            Check(host.Accept() == 0 && SteamNetworkingSockets.Closed.Contains(14), "Lobby departure during handshake rejected");
            SteamNetworkingSockets.Change(15, 444, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected);
            Check(host.Accept() == 0 && SteamNetworkingSockets.Closed.Contains(15), "Changed native identity rejected at completion");
            SteamNetworkingSockets.Change(11, 100, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer);
            Check(host.Poll(11, out _) == RaidTransportEvent.Disconnected && host.RemoteSteamId(11) == 0, "Disconnect removes authenticated identity");
            Check(host.Poll(11, out _) == RaidTransportEvent.Empty && !host.Send(11, payload, true), "Disconnected peer cannot send or reappear");
        }
        Check(Callback<SteamNetConnectionStatusChangedCallback_t>.Count == 0 && SteamNetworkingSockets.ListenerClosed, "Dispose closes listener and callback");
        using (var client = new RaidSteamTransport(false, 76561198012345678, _ => false))
        {
            Check(SteamNetworkingSockets.ExpectedHost == 76561198012345678, "P2P connects by full 64-bit host ID");
            SteamNetworkingSockets.Change((uint)client.Server, 76561198012345679, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected, false);
            Check(client.Poll(client.Server, out _) == RaidTransportEvent.Disconnected, "Unexpected Steam host rejected");
        }
        using (var client = new RaidSteamTransport(false, 76561198012345678, _ => false))
        {
            SteamNetworkingSockets.Change((uint)client.Server, 76561198012345678, ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected, false);
            Check(client.Poll(client.Server, out _) == RaidTransportEvent.Connected, "Verified host connects");
            client.Disconnect(client.Server);
            Check(client.Poll(client.Server, out _) == RaidTransportEvent.Disconnected, "Cancel clears client transport");
        }
        Check(Callback<SteamNetConnectionStatusChangedCallback_t>.Count == 0, "Repeated connect/dispose leaves no callback");
        Console.WriteLine("PASS: " + _checks + " Steam transport adapter checks using a fake native boundary; actual Steam connectivity is not tested.");
    }
}
namespace Steamworks
{
    public enum EResult { k_EResultOK }
    public enum ESteamNetworkingConnectionState { k_ESteamNetworkingConnectionState_Connecting, k_ESteamNetworkingConnectionState_Connected, k_ESteamNetworkingConnectionState_ClosedByPeer, k_ESteamNetworkingConnectionState_ProblemDetectedLocally }
    public static class Constants { public const int k_nSteamNetworkingSend_Reliable = 8, k_nSteamNetworkingSend_UnreliableNoDelay = 4; }
    public struct HSteamNetConnection
    {
        public uint m_HSteamNetConnection; public static HSteamNetConnection Invalid => default;
        public static bool operator ==(HSteamNetConnection a, HSteamNetConnection b) => a.m_HSteamNetConnection == b.m_HSteamNetConnection;
        public static bool operator !=(HSteamNetConnection a, HSteamNetConnection b) => !(a == b);
        public override bool Equals(object value) => value is HSteamNetConnection c && c == this;
        public override int GetHashCode() => (int)m_HSteamNetConnection;
    }
    public struct HSteamListenSocket
    {
        public uint Value; public static HSteamListenSocket Invalid => default;
        public static bool operator ==(HSteamListenSocket a, HSteamListenSocket b) => a.Value == b.Value;
        public static bool operator !=(HSteamListenSocket a, HSteamListenSocket b) => !(a == b);
        public override bool Equals(object value) => value is HSteamListenSocket c && c == this;
        public override int GetHashCode() => (int)Value;
    }
    public struct SteamNetworkingIdentity { public ulong Value; public void SetSteamID64(ulong value) => Value = value; public ulong GetSteamID64() => Value; }
    public struct SteamNetConnectionInfo_t { public SteamNetworkingIdentity m_identityRemote; public HSteamListenSocket m_hListenSocket; public ESteamNetworkingConnectionState m_eState; }
    public struct SteamNetConnectionStatusChangedCallback_t { public HSteamNetConnection m_hConn; public SteamNetConnectionInfo_t m_info; }
    public sealed class Callback<T> : IDisposable
    {
        private static readonly List<Callback<T>> Callbacks = new List<Callback<T>>();
        private Action<T> _action; public static int Count => Callbacks.Count;
        public static Callback<T> Create(Action<T> action) { var cb = new Callback<T> { _action = action }; Callbacks.Add(cb); return cb; }
        public static void Raise(T value) { foreach (var cb in Callbacks.ToArray()) cb._action(value); }
        public void Dispose() => Callbacks.Remove(this);
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct SteamNetworkingMessage_t
    {
        public IntPtr m_pData; public int m_cbSize; public SteamNetworkingIdentity m_identityPeer;
        public static int Released;
        public static void Release(IntPtr pointer) { var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointer); Marshal.FreeHGlobal(message.m_pData); Marshal.FreeHGlobal(pointer); Released++; }
    }
    public static class SteamNetworkingSockets
    {
        public static readonly HashSet<uint> Closed = new HashSet<uint>(), Accepted = new HashSet<uint>();
        private static readonly Dictionary<uint, SteamNetConnectionInfo_t> Info = new Dictionary<uint, SteamNetConnectionInfo_t>();
        private static readonly Dictionary<int, Queue<IntPtr>> Messages = new Dictionary<int, Queue<IntPtr>>();
        public static bool ListenerClosed; public static int LastFlags; public static ulong ExpectedHost;
        private static uint _nextClient = 1000;
        public static HSteamListenSocket CreateListenSocketP2P(int port, int count, object options) => new HSteamListenSocket { Value = 42 };
        public static HSteamNetConnection ConnectP2P(ref SteamNetworkingIdentity host, int port, int count, object options) { ExpectedHost = host.Value; return new HSteamNetConnection { m_HSteamNetConnection = ++_nextClient }; }
        public static EResult AcceptConnection(HSteamNetConnection c) { Accepted.Add(c.m_HSteamNetConnection); return EResult.k_EResultOK; }
        public static bool CloseConnection(HSteamNetConnection c, int reason, string description, bool linger) { Closed.Add(c.m_HSteamNetConnection); return true; }
        public static bool CloseListenSocket(HSteamListenSocket socket) { ListenerClosed = true; return true; }
        public static bool GetConnectionInfo(HSteamNetConnection c, out SteamNetConnectionInfo_t info) => Info.TryGetValue(c.m_HSteamNetConnection, out info);
        public static EResult SendMessageToConnection(HSteamNetConnection c, IntPtr data, uint size, int flags, out long number) { number = 1; LastFlags = flags; return EResult.k_EResultOK; }
        public static int ReceiveMessagesOnConnection(HSteamNetConnection c, IntPtr[] messages, int max)
        { if (!Messages.TryGetValue((int)c.m_HSteamNetConnection, out var queue) || queue.Count == 0) return 0; messages[0] = queue.Dequeue(); return 1; }
        public static void Change(uint handle, ulong user, ESteamNetworkingConnectionState state, bool incoming = true)
        {
            var info = new SteamNetConnectionInfo_t { m_identityRemote = new SteamNetworkingIdentity { Value = user }, m_hListenSocket = new HSteamListenSocket { Value = incoming ? 42u : 0u }, m_eState = state };
            Info[handle] = info; Callback<SteamNetConnectionStatusChangedCallback_t>.Raise(new SteamNetConnectionStatusChangedCallback_t { m_hConn = new HSteamNetConnection { m_HSteamNetConnection = handle }, m_info = info });
        }
        public static void Enqueue(int handle, ulong user, byte[] bytes)
        {
            var data = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, data, bytes.Length);
            var msg = new SteamNetworkingMessage_t { m_pData = data, m_cbSize = bytes.Length, m_identityPeer = new SteamNetworkingIdentity { Value = user } };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<SteamNetworkingMessage_t>()); Marshal.StructureToPtr(msg, pointer, false);
            if (!Messages.TryGetValue(handle, out var queue)) Messages[handle] = queue = new Queue<IntPtr>(); queue.Enqueue(pointer);
        }
    }
}
