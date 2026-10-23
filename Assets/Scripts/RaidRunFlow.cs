using System;
using System.Collections.Generic;

namespace RaidRogue
{
    [Serializable]
    public sealed class RaidFlowSettings
    {
        public float loadingSeconds = 30;
        public float lobbyApprovalSeconds = 30;
        public float rewardSeconds = 120;
        public float clearSeconds = 1.5f;
        public int countdownSeconds = 3;
    }

    // Guard for the existing StageManager, not another gameplay manager. A revision belongs to one
    // preparation/attempt; old coroutines and UI callbacks cannot advance a subsequent attempt.
    public sealed class RaidRunFlow
    {
        public RaidBattlePhase Phase { get; private set; } = RaidBattlePhase.Lobby;
        public int Stage { get; private set; }
        public int Revision { get; private set; }
        public bool IsCurrent(int revision, RaidBattlePhase phase) => Revision == revision && Phase == phase;
        public bool TryPrepare(int stage, int maximum, bool retry)
        {
            if (stage < 1 || stage > maximum) return false;
            bool valid = retry ? Phase == RaidBattlePhase.Wiped && stage == Stage
                : (Phase == RaidBattlePhase.Lobby && stage == 1) || (Phase == RaidBattlePhase.Reward && stage == Stage + 1);
            if (!valid) return false;
            Stage = stage; Revision++; Phase = RaidBattlePhase.Loading; return true;
        }
        public bool TryCountdown(int revision)
        { if (!IsCurrent(revision, RaidBattlePhase.Loading)) return false; Phase = RaidBattlePhase.Countdown; return true; }
        public bool TryStart(int revision)
        { if (!IsCurrent(revision, RaidBattlePhase.Countdown)) return false; Phase = RaidBattlePhase.Combat; return true; }
        public EncounterOutcome Resolve(int revision, bool bossDead, int living, int partySize)
        {
            if (!IsCurrent(revision, RaidBattlePhase.Combat)) return EncounterOutcome.None;
            var outcome = PartyBattleRules.Resolve(bossDead, living, partySize);
            if (outcome != EncounterOutcome.None) Phase = outcome == EncounterOutcome.Cleared ? RaidBattlePhase.BossClear : RaidBattlePhase.Wiped;
            return outcome;
        }
        public bool FinishClear(int revision, int maximum)
        { if (!IsCurrent(revision, RaidBattlePhase.BossClear)) return false; Phase = Stage >= maximum ? RaidBattlePhase.Victory : RaidBattlePhase.Reward; return true; }
        public bool End()
        { if (Phase == RaidBattlePhase.Closed) return false; Revision++; Phase = RaidBattlePhase.Closed; return true; }
    }

    // Steam lobby membership is provisional until a transport/roster admission completes.
    // A stale lobby-only member may not permanently prevent the admitted party from starting.
    public sealed class RaidAdmissionWait
    {
        private readonly Dictionary<ulong, float> _since = new Dictionary<ulong, float>();
        public bool Waiting(ulong id, bool admitted, float now, float limit)
        {
            if (admitted) { _since.Remove(id); return false; }
            if (!_since.TryGetValue(id, out float first)) _since[id] = first = now;
            return now - first < limit;
        }
        public void Retain(ICollection<ulong> members)
        { foreach (ulong id in new List<ulong>(_since.Keys)) if (!members.Contains(id)) _since.Remove(id); }
    }
}
