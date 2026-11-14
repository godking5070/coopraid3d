using System;
using System.Collections.Generic;

namespace RaidRogue
{
    [Serializable]
    public sealed class BossRaidwideSettings
    {
        public bool enabled = true;
        public float firstDelay = 15, interval = 25, castSeconds = 3;
        public float normalDamage = 350, hardDamage = 500;
        public string Name(int stage) => stage switch
        {
            1 => "광장 진동", 2 => "만개하는 독기", 3 => "침묵의 진혼종",
            4 => "비극의 합창", 5 => "만인의 심판", 6 => "쌍성 공명", _ => "전장 충격파"
        };
        public float Damage(bool hard) => Math.Max(0, hard ? hardDamage : normalDamage);
    }

    public enum BossRaidwideEvent { None, Begin, Hit }
    public sealed class BossRaidwideState
    {
        public bool Active { get; private set; }
        public float Remaining { get; private set; }
        private float _untilNext;
        public bool DueWithin(float seconds,bool ready,BossRaidwideSettings settings)
            => settings!=null&&settings.enabled&&ready&&!Active&&_untilNext<=Math.Max(0,seconds);
        public void Reset(BossRaidwideSettings settings)
        { Active = false; Remaining = 0; _untilNext = Math.Max(0, settings.firstDelay); }
        public BossRaidwideEvent Advance(float seconds, bool ready, BossRaidwideSettings settings)
        {
            if (!settings.enabled) { Active = false; Remaining = 0; return BossRaidwideEvent.None; }
            if (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return BossRaidwideEvent.None;
            if (Active)
            {
                Remaining = Math.Max(0, Remaining - seconds);
                if (Remaining > 0) return BossRaidwideEvent.None;
                Active = false; _untilNext = Math.Max(1, settings.interval);
                return BossRaidwideEvent.Hit;
            }
            _untilNext = Math.Max(0, _untilNext - seconds);
            if (_untilNext > 0 || !ready) return BossRaidwideEvent.None;
            Active = true; Remaining = Math.Max(.1f, settings.castSeconds);
            return BossRaidwideEvent.Begin;
        }
    }

    // Apply once from the configured preset on encounter reset, so retries do not compound it.
    public static class RaidBossHealthTuning
    {
        public const float HardMultiplier = 2f;
        public static float Resolve(float configuredHealth, bool hard)
            => Math.Max(1f, configuredHealth) * (hard ? HardMultiplier : 1f);
    }

    public sealed class BossTransformationHealthState
    {
        private bool _restored;
        public void Reset() => _restored = false;
        public bool TryRestore(bool authoritative, bool hard, float current, float maximum, out float health)
        {
            health = current;
            if (!authoritative || !hard || _restored || current <= 0 || maximum <= 0) return false;
            _restored = true;
            health = maximum;
            return true;
        }
    }

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
        public bool TryResume(int stage, int maximum)
        {
            if (Phase != RaidBattlePhase.Lobby || stage < 1 || stage > maximum) return false;
            Stage = stage; Revision++; Phase = RaidBattlePhase.Loading; return true;
        }
        public bool TryPrepare(int stage, int maximum, bool retry)
        {
            if (stage < 1 || stage > maximum) return false;
            bool valid = retry ? Phase == RaidBattlePhase.Wiped && stage == Stage
                : (Phase == RaidBattlePhase.Lobby && stage == (Stage == 0 ? 1 : Stage + 1)) || (Phase == RaidBattlePhase.Reward && stage == Stage + 1);
            if (!valid) return false;
            Stage = stage; Revision++; Phase = RaidBattlePhase.Loading; return true;
        }
        public bool TryDevelopmentPrepare(int stage,int maximum)
        {
            if(Phase!=RaidBattlePhase.Lobby||Stage!=0||stage<1||stage>maximum)return false;
            Stage=stage;Revision++;Phase=RaidBattlePhase.Loading;return true;
        }
        public bool TryApproach(int revision)
        { if (!IsCurrent(revision, RaidBattlePhase.Loading)) return false; Phase = RaidBattlePhase.Approach; return true; }
        public bool TryArrival(int revision)
        { if (!IsCurrent(revision, RaidBattlePhase.Approach)) return false; Phase = RaidBattlePhase.Arrival; return true; }
        public bool TryCountdown(int revision)
        { if (!IsCurrent(revision, RaidBattlePhase.Arrival)) return false; Phase = RaidBattlePhase.Countdown; return true; }
        public bool TryStart(int revision)
        { if (!IsCurrent(revision, RaidBattlePhase.Countdown)) return false; Phase = RaidBattlePhase.Combat; return true; }
        public EncounterOutcome Resolve(int revision, bool bossDead, int living, int partySize)
        {
            if (!IsCurrent(revision, RaidBattlePhase.Combat)) return EncounterOutcome.None;
            var outcome = PartyBattleRules.Resolve(bossDead, living, partySize);
            if (outcome != EncounterOutcome.None) Phase = outcome == EncounterOutcome.Cleared ? RaidBattlePhase.BossClear : RaidBattlePhase.Wiped;
            return outcome;
        }
        public bool FinishClear(int revision, int maximum,bool finalChoicePending=false)
        { if (!IsCurrent(revision, RaidBattlePhase.BossClear)) return false; Phase = Stage >= maximum&&!finalChoicePending ? RaidBattlePhase.Victory : RaidBattlePhase.Reward; return true; }
        public bool FinishFinalRewards(int revision,int maximum)
        {if(!IsCurrent(revision,RaidBattlePhase.Reward)||Stage<maximum)return false;Phase=RaidBattlePhase.Victory;return true;}
        public bool TryIntermission(int revision, int maximum)
        {
            if (!IsCurrent(revision, RaidBattlePhase.Reward) || Stage >= maximum) return false;
            Revision++; Phase = RaidBattlePhase.Lobby; return true;
        }
        public bool TryRestoreSettlement(int stage, int maximum)
        {
            if (Phase != RaidBattlePhase.Lobby || Stage != 0 || stage < 1 || stage > maximum) return false;
            Stage = stage; Revision++; Phase = RaidBattlePhase.Reward; return true;
        }
        public bool ReturnToLobby()
        {
            if (Phase != RaidBattlePhase.Victory) return false;
            Revision++; Stage = 0; Phase = RaidBattlePhase.Lobby; return true;
        }
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
