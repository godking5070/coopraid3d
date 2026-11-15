using System;
using System.Collections.Generic;

namespace RaidRogue
{
    public enum RaidBattlePhase { Lobby, Countdown, Combat, Reward, Wiped, Victory, Loading, BossClear, Closed, Approach, Arrival }
    public static class RaidLobbyAdmission
    {
        // Intermission and file continuation also use Lobby, but retain the original party.
        // Authenticated reconnects use the separate recovery-slot path.
        public static bool AllowsNewParticipant(RaidBattlePhase phase, int clearedStage, bool resumed)
            => phase == RaidBattlePhase.Lobby && clearedStage == 0 && !resumed;
    }
    // Ephemeral host presentation state, owned by one network session, never a save preference.
    public sealed class RaidSessionDifficulty
    {
        public string Value { get; private set; }
        public static bool Supported(string difficulty) => difficulty == "normal" || difficulty == "hard";
        public bool TryApply(RaidCombatFrame frame, out bool changed)
        {
            changed = false;
            if (frame == null || !Supported(frame.difficultyId)) return false;
            changed = Value != frame.difficultyId;
            Value = frame.difficultyId;
            return true;
        }
        public string Resolve(string personalDifficulty, bool replica) => replica && Value != null ? Value : personalDifficulty;
        public void Clear() => Value = null;
    }
    [Serializable] public sealed class RaidActionRequest
    {
        public uint sequence, lease;
        public int epoch, actor, skill, target;
        public string attemptId;
        public int deathGeneration;
        public string jobId, ruleId, choiceId, rewardKey; public int slot; // 1..5 skills, 6 potion, 7 target, 8 reward, 9 start/retry.
    }
    // Connection ownership and epoch are checked before invoking any existing gameplay method.
    public sealed class RaidActionGate
    {
        private readonly Dictionary<int, uint> _last = new Dictionary<int, uint>();
        public bool Accept(int connectionOwner, int epoch, RaidActionRequest request)
        {
            if (request == null || connectionOwner <= 0 || request.actor != connectionOwner || request.epoch != epoch ||
                request.sequence == 0 || request.skill < 1 || request.skill > 17) return false;
            if (_last.TryGetValue(connectionOwner, out uint last) && request.sequence <= last) return false;
            _last[connectionOwner] = request.sequence; return true;
        }
        public void Remove(int owner) => _last.Remove(owner);
    }
    public sealed class RaidRewardRound
    {
        private readonly Dictionary<int, int[]> _offers = new Dictionary<int, int[]>();
        private readonly HashSet<int> _chosen = new HashSet<int>();
        public int Epoch { get; private set; }
        public bool Complete => _offers.Count > 0 && _chosen.Count == _offers.Count;
        public void Begin(int epoch) { Epoch = epoch; _offers.Clear(); _chosen.Clear(); }
        public void Offer(int actor, int[] cards) { if (!_offers.ContainsKey(actor)) _offers.Add(actor, (int[])cards.Clone()); }
        public int[] Cards(int actor) => _offers.TryGetValue(actor, out var cards) ? (int[])cards.Clone() : Array.Empty<int>();
        public bool Chosen(int actor) => _chosen.Contains(actor);
        public void RestoreChosen(int actor) { if (_offers.ContainsKey(actor)) _chosen.Add(actor); }
        public bool Choose(int epoch, int actor, int card)
        {
            if (epoch != Epoch || _chosen.Contains(actor) || !_offers.TryGetValue(actor, out var cards) || Array.IndexOf(cards, card) < 0) return false;
            _chosen.Add(actor); return true;
        }
        public void Remove(int actor) { _offers.Remove(actor); _chosen.Remove(actor); }
    }
    // A lost fragment never applies a partial world. Subsequent full frames converge, with bounded memory.
    public sealed class RaidFrameAssembler
    {
        public const int ChunkBytes = 900, MaxChunks = 128;
        private uint _frame, _applied;
        private byte[][] _chunks;
        private int _received;
        public byte[] Add(uint frame, int index, int count, byte[] bytes)
        {
            if (frame <= _applied || frame < _frame || count < 1 || count > MaxChunks || index < 0 || index >= count || bytes == null || bytes.Length > ChunkBytes) return null;
            if (frame != _frame || _chunks == null) { _frame = frame; _chunks = new byte[count][]; _received = 0; }
            if (_chunks.Length != count || _chunks[index] != null) return null;
            _chunks[index] = bytes; if (++_received != count) return null;
            int length = 0; foreach (var chunk in _chunks) length += chunk.Length;
            var result = new byte[length]; int offset = 0;
            foreach (var chunk in _chunks) { Array.Copy(chunk, 0, result, offset, chunk.Length); offset += chunk.Length; }
            _applied = frame; _chunks = null; return result;
        }
    }
    [Serializable] public sealed class RaidCombatActor
    {
        public int id, role, target, combo1, combo2, deathGeneration;
        public bool bot, dead, stunned, rooted, forced, casting, resurrecting, rewardChosen, cardChosen;
        public string name, castName;
        public CharacterStateSnapshot character;
        public string jobState, debuffs; // Host-only payload, restored for presentation; never executes damage.
        public int controlState, autoRevision;
        public float returnDeadline;
        public int[] autoCards;
        public float hp, maxHp, shield, defense, tactical, haste, speedBoost, speed, protection, potion, potionTotal;
        public float gcd, gcdTotal, cd3, cd3Total, cd4, cd4Total, comboTime1, comboTime2, castRemaining, castTotal, triplecast, resurrection, resurrectionTotal, resurrectionCooldown;
        public int[] cards, offer;
    }
    [Serializable] public sealed class RaidCombatEnemy
    {
        public int id, type, stage, phase, state, target;
        public string name, castName, alert;
        public float x, y, z, yaw, hp, maxHp, radius, castRemaining, castTotal, groggy;
        public bool dead, casting;
        public float stagger, counterWindowEnd, brandEnd;
        public bool staggerLocked;
        public float staggerThreshold=1000;
        public int guardianPattern, guardianStep, guardianPhase, guardianClashPhase, guardianClashTank, guardianFollowup, guardianSideSign, guardianImpactSerial, guardianLockSerial, guardianLureTarget;
        public bool guardianTransformed, guardianClashSucceeded;
        public bool floraCounterOpen, floraTransformed, floraTransforming;
        public int campPattern, campStep, campPlateStep, campSuccessMask, campFailureMask, campMissingMask;
        public float campStagger;
        public bool campCounterOpen, campStoryUsed;
        public bool campHardTransformed, campHardFirstLockDone, campHardFinalDone;
        public int campHardTank, campHardEchoPlate;
        public int[] campAssignees;
        public float[] campPlateX, campPlateZ;
          public int marPattern, marStep, marTarget;
          public float marStagger;
          public bool marCounterOpen, marClimaxUsed, marStoryUsed, marFirewall, marRight;
          public bool marHardTransformed, marHardClimaxPreparing, marHardMemoryRight;
          public int marHardMemoryPattern, marHardStringMask;
          public float marHardMemoryYaw, marHardDollX, marHardDollZ, marHardRecoilX, marHardRecoilZ, marHardCrossingX;
          public int justPattern, justStep;
          public int[] justMassIds, justMassValues;
          public float[] justMarkX, justMarkZ;
          public float justStagger;
          public bool justVerdictUsed, justStoryUsed, justCounterOpen, justRight;
          public bool justHardTransformed, justHardIntroDone, justHardClimaxStarted,
              justHardWeightActive, justHardImpactShown, justHardWeightArrived;
          public int justHardBalanceKind, justHardRound, justHardWeightSide, justHardFollowupPattern, justHardNext;
          public float justHardNextPulse;
          public float[] justHardResidueX, justHardResidueZ;
          public int janusPattern,janusStep,janusAlphaCounter,janusOmegaCounter;
          public int[] janusPostActors;
          public float janusPostOverlap;
          public bool janusPostUsed,janusCounterUnlocked,janusStoryUsed,janusLineLocked,janusChainBroken;
            public bool janusHardTransformed;
            public int janusHardPostPhase,janusHardHoldingMask;
        public float guardianStarted, guardianEnds, guardianStagger;
    }
    [Serializable] public sealed class RaidCombatMechanic
    {
        public int id, kind, target; // 0..2 telegraphs, 3 spread, 4 stack, 5 tether, 6 sanctuary, 7 presentation-only projectile.
        public float x, y, z, yaw, inner, outer, angle, start, resolve, end, speed;
        public bool intercepted, guideOnly, guideLocked, tankGuide, targetMarker, thornStyle;
    }
    [Serializable] public sealed class RaidMetricMember
    {
        public int id, role, deaths; public string name, jobId;
        public double bossDamage, addDamage, healing, selfHealing, shield;
    }
    [Serializable] public sealed class RaidMetricResult
    {
        public int stage; public double seconds; public RaidMetricMember[] members; public string[] events;
    }
    [Serializable] public sealed class RaidCombatFrame
    {
        public bool campaignResume;
        public int campaignSavedStage;
        public string campaignSavedUtc;
        public bool campaignCompleted, campaignSaveFailed;
        public bool campaignEligible; // Missing in older frames => no first-play unlock from that host.
        public int epoch, stage, retries, irisCoins, totalRevives;
        public string attemptId;
        public string runId,chapterId;
        public string difficultyId; // Required host difficulty, including recovery full baselines.
        public uint serial, ownerLease, effectWatermark, poseTick;
        public bool controlGranted;
        public RaidPose[] recoveryPoses;
        public RaidBattlePhase phase;
        public bool bossVisible, portalActive;
        public float time, countdownEnd, runTime, phaseDeadline, arrivalRevealAt;
        public RaidCombatActor[] party;
        public RaidCombatEnemy boss;
        public RaidCombatEnemy[] adds;
        public RaidCombatMechanic[] mechanics;
        public RaidMetricResult current;
        public RaidMetricResult[] clears, failures;
    }
    [Serializable] public sealed class RaidCombatEffect
    {
        public uint id; public int epoch, kind; public float x, y, z, tx, ty, tz, r, g, b;
        public string text; public bool large;
        // Optional presentation hint; absent in older JSON means ordinary text.
        public bool important;
    }
}
