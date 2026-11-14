using System;

namespace RaidRogue
{
    public enum RaidTransportEvent { Empty, Connected, Disconnected, Data }
    // Both transports carry the same RaidWire packets and host-authoritative combat requests.
    public interface IRaidTransport : IDisposable
    {
        bool IsCreated { get; }
        bool IsSteam { get; }
        int Server { get; }
        void Update();
        int Accept();
        RaidTransportEvent Poll(int connection, out byte[] bytes);
        bool Send(int connection, byte[] bytes, bool reliable);
        void Disconnect(int connection, bool voluntary = false, bool transient = false);
        int TakeCloseCode(int connection);
        ulong RemoteSteamId(int connection);
    }

}
