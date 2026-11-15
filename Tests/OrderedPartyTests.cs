using System;
using System.Collections.Generic;

namespace RaidRogue
{
    sealed class Transform
    {
        public readonly int Id;
        public Transform(int id) { Id = id; }
    }
    sealed class NetworkRaidAvatar
    {
        private readonly Transform _transform;
        public bool Destroyed;
        public NetworkRaidAvatar(int id) { _transform = new Transform(id); }
        public Transform transform
        {
            get
            {
                if (Destroyed) throw new InvalidOperationException("Destroyed avatar transform was accessed during teardown");
                return _transform;
            }
        }
        // Model Unity's destroyed Component comparison, while retaining its managed reference.
        public static bool operator ==(NetworkRaidAvatar left, NetworkRaidAvatar right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null) || right.Destroyed;
            if (ReferenceEquals(right, null)) return left.Destroyed;
            return ReferenceEquals(left, right);
        }
        public static bool operator !=(NetworkRaidAvatar left, NetworkRaidAvatar right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }
    sealed class RaidNetworkSession
    {
        private readonly Dictionary<int, NetworkRaidAvatar> _avatars = new Dictionary<int, NetworkRaidAvatar>();
        private int _localId;
        public void Add(int id, NetworkRaidAvatar avatar) { _avatars.Add(id, avatar); }
        public void Local(int id) { _localId = id; }
        public int StoredCount => _avatars.Count;
        /* ACTUAL_ORDERED_PARTY */
    }
    static class OrderedPartyTests
    {
        private static int _checks;
        private static void Check(bool ok, string message)
        { _checks++; if (!ok) { Console.Error.WriteLine("FAIL: " + message); Environment.Exit(1); } }
        private static void Expect(RaidNetworkSession session, params int[] ids)
        {
            List<Transform> party;
            try { party = session.OrderedParty(); }
            catch (Exception error) { Check(false, error.Message); return; }
            Check(party.Count == ids.Length, "Only surviving party transforms are returned");
            for (int i = 0; i < ids.Length; i++) Check(party[i] != null && party[i].Id == ids[i], "Local first, then ascending actor IDs");
        }
        private static void Main()
        {
            var session = new RaidNetworkSession();
            Expect(session);
            var local = new NetworkRaidAvatar(5);
            var first = new NetworkRaidAvatar(2);
            var last = new NetworkRaidAvatar(8);
            session.Add(8, last); session.Add(2, first); session.Add(5, local); session.Local(5);
            Expect(session, 5, 2, 8);
            session.Add(3, new NetworkRaidAvatar(3) { Destroyed = true });
            Expect(session, 5, 2, 8);
            session.Add(4, null);
            Expect(session, 5, 2, 8);
            Check(session.StoredCount == 5, "Read-only cleanup query does not mutate the roster");
            local.Destroyed = true;
            Expect(session, 2, 8);
            session.Local(99);
            Expect(session, 2, 8);
            first.Destroyed = true; last.Destroyed = true;
            Expect(session);
            Console.WriteLine("PASS: ordered party " + _checks + " checks using the actual method and destroyed-object null semantics; native Unity unload remains unverified.");
        }
    }
}
