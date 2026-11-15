using System;
using System.Collections.Generic;

namespace RaidRogue
{
    [Serializable]
    public sealed class RaidRecoverySettings
    {
        // Host-owned seconds. Adjust recovery and disconnected reward policy here.
        public float reservationSeconds = 60;
        public float rewardSeconds = 15;
        public float silenceSeconds = 15;
        public float restoreSeconds = 20;
    }
    public enum RaidControlState { Connected, Reserved, Restoring, PermanentAI }
    public sealed class RaidRecoverySlots
    {
        public sealed class Slot
        {
            public ulong SteamId;
            public int Actor;
            public uint Lease = 1;
            public RaidControlState State;
            public float Deadline, RewardAt;
            public int RewardEpoch = -1, AutoRevision;
            public readonly List<int> AutoCards = new List<int>();
            public bool Automated => State != RaidControlState.Connected;
        }
        private readonly Dictionary<ulong, Slot> _slots = new Dictionary<ulong, Slot>();
        private readonly RaidRecoverySettings _settings;
        public RaidRecoverySlots(RaidRecoverySettings settings) => _settings = settings;
        public IEnumerable<Slot> All => _slots.Values;
        public Slot Find(ulong id) => _slots.TryGetValue(id, out var slot) ? slot : null;
        public Slot Actor(int id) { foreach (var slot in _slots.Values) if (slot.Actor == id) return slot; return null; }
        public Slot Register(ulong id, int actor)
        {
            if (id == 0 || actor <= 0 || _slots.ContainsKey(id) || Actor(actor) != null) return null;
            var slot = new Slot { SteamId = id, Actor = actor }; _slots.Add(id, slot); return slot;
        }
        public bool Reserve(int actor, float now, int rewardEpoch = -1)
        {
            var slot = Actor(actor);
            if (slot == null || slot.State == RaidControlState.Reserved || slot.State == RaidControlState.PermanentAI) return false;
            // A failed reconnect never extends the original reservation.
            if (slot.State == RaidControlState.Connected) slot.Deadline = now + _settings.reservationSeconds;
            slot.State = RaidControlState.Reserved; slot.Lease++;
            if (rewardEpoch >= 0) BeginReward(slot, rewardEpoch, now);
            return true;
        }
        public bool Expire(Slot slot, float now)
        {
            if ((slot.State != RaidControlState.Reserved && slot.State != RaidControlState.Restoring) || now < slot.Deadline) return false;
            slot.State = RaidControlState.PermanentAI; slot.Lease++; return true;
        }
        public string BeginRestore(ulong authenticatedId, float now, out Slot slot)
        {
            slot = Find(authenticatedId);
            if (slot == null) return "예약된 자리가 없습니다. 신규 참가는 최초 대기실에서만 가능합니다.";
            Expire(slot, now);
            if (slot.State == RaidControlState.PermanentAI) return "복귀 가능 시간이 지났습니다. 이 자리는 현재 런의 AI가 맡습니다.";
            if (slot.State != RaidControlState.Reserved) return "같은 Steam 계정이 이미 접속했거나 복구 중입니다.";
            slot.State = RaidControlState.Restoring; slot.Lease++; return null;
        }
        public bool Complete(ulong authenticatedId, uint lease, float now)
        {
            var slot = Find(authenticatedId);
            if (slot == null) return false;
            Expire(slot, now);
            if (slot.State != RaidControlState.Restoring || slot.Lease != lease) return false;
            slot.State = RaidControlState.Connected; slot.Deadline = slot.RewardAt = 0; return true;
        }
        public void Remove(int actor)
        { var slot = Actor(actor); if (slot != null) _slots.Remove(slot.SteamId); }
        public void BeginReward(Slot slot, int epoch, float now)
        {
            if (!slot.Automated || (slot.RewardEpoch == epoch && slot.RewardAt > 0)) return;
            slot.RewardEpoch = epoch; slot.RewardAt = now + _settings.rewardSeconds;
        }
        public bool RewardDue(Slot slot, int epoch, float now) => slot.Automated && slot.RewardEpoch == epoch && now >= slot.RewardAt;
        public void AutoSelected(Slot slot, int card) { slot.AutoCards.Add(card); slot.AutoRevision++; }
    }
    // Acknowledgements refer to a full host frame, not to client-supplied health or position.
    public sealed class RaidRestoreBarrier
    {
        private struct Sent { public uint Frame, Lease; public int Epoch, Phase; }
        private readonly Queue<Sent> _sent = new Queue<Sent>();
        public void SentFrame(uint frame, uint lease, int epoch, int phase)
        {
            _sent.Enqueue(new Sent { Frame = frame, Lease = lease, Epoch = epoch, Phase = phase });
            while (_sent.Count > 64) _sent.Dequeue();
        }
        public bool Accept(uint frame, uint lease, int epoch, int phase)
        {
            foreach (var sent in _sent) if (sent.Frame == frame && sent.Lease == lease && sent.Epoch == epoch && sent.Phase == phase) return true;
            return false;
        }
    }
}
