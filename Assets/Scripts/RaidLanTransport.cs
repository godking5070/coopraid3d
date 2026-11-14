using System;
using System.Collections.Generic;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

namespace RaidRogue
{
    public sealed class RaidLanTransport : IRaidTransport
    {
        private NetworkDriver _driver;
        private NetworkPipeline _reliable;
        private readonly Dictionary<int, NetworkConnection> _connections = new Dictionary<int, NetworkConnection>();
        private int _next;
        public bool IsCreated => _driver.IsCreated;
        public bool IsSteam => false;
        public int Server { get; }
        public RaidLanTransport(bool host, NetworkEndpoint endpoint)
        {
            var settings = new NetworkSettings();
            settings.WithNetworkConfigParameters(connectTimeoutMS: 500, maxConnectAttempts: 10, disconnectTimeoutMS: 5000);
            try
            {
                _driver = NetworkDriver.Create(settings);
                _reliable = _driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                if (_driver.Bind(host ? NetworkEndpoint.AnyIpv4.WithPort(endpoint.Port) : NetworkEndpoint.AnyIpv4) != 0 || (host && _driver.Listen() != 0))
                    throw new InvalidOperationException("LAN 포트를 열 수 없습니다.");
                if (!host) { Server = ++_next; _connections.Add(Server, _driver.Connect(endpoint)); }
            }
            catch { Dispose(); throw; }
            finally { settings.Dispose(); }
        }
        public void Update() => _driver.ScheduleUpdate().Complete();
        public int Accept()
        {
            var connection = _driver.Accept(); if (connection == default) return 0;
            int id = ++_next; _connections.Add(id, connection); return id;
        }
        public RaidTransportEvent Poll(int connection, out byte[] bytes)
        {
            bytes = null;
            if (!_connections.TryGetValue(connection, out var handle)) return RaidTransportEvent.Empty;
            var type = _driver.PopEventForConnection(handle, out var reader);
            if (type == NetworkEvent.Type.Connect) return RaidTransportEvent.Connected;
            if (type == NetworkEvent.Type.Disconnect) { _connections.Remove(connection); return RaidTransportEvent.Disconnected; }
            if (type != NetworkEvent.Type.Data) return RaidTransportEvent.Empty;
            if (reader.Length > RaidWire.MaxBytes) return RaidTransportEvent.Data;
            bytes = new byte[reader.Length]; for (int i = 0; i < bytes.Length; i++) bytes[i] = reader.ReadByte();
            return RaidTransportEvent.Data;
        }
        public bool Send(int connection, byte[] bytes, bool reliable)
        {
            if (!_connections.TryGetValue(connection, out var handle)) return false;
            if (_driver.BeginSend(reliable ? _reliable : NetworkPipeline.Null, handle, out var writer, bytes.Length) != 0) return false;
            foreach (byte value in bytes) writer.WriteByte(value);
            return _driver.EndSend(writer) >= 0;
        }
        public void Disconnect(int connection) { if (_connections.TryGetValue(connection, out var handle)) _driver.Disconnect(handle); _connections.Remove(connection); }
        public ulong RemoteSteamId(int connection) => 0;
        public void Dispose() { if (_driver.IsCreated) _driver.Dispose(); _driver = default; _connections.Clear(); }
    }
}
