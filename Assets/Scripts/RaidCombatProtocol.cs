using System;
using System.Collections.Generic;

namespace RaidRogue
{
    public enum RaidBattlePhase { Lobby, Countdown, Combat, Reward, Wiped, Victory }
    [Serializable] public sealed class RaidActionRequest
    {
        public uint sequence;
        public int epoch, actor, skill, target; // 1..5 skills, 6 potion, 7 target, 8 reward, 9 start/retry.
    }
    // Connection ownership and epoch are checked before invoking any existing gameplay method.
    public sealed class RaidActionGate
    {
        private readonly Dictionary<int, uint> _last = new Dictionary<int, uint>();
        public bool Accept(int connectionOwner, int epoch, RaidActionRequest request)
        {
            if (request == null || connectionOwner <= 0 || request.actor != connectionOwner || request.epoch != epoch ||
                request.sequence == 0 || request.skill < 1 || request.skill > 9) return false;
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
        public int id, role, target, combo1, combo2;
        public bool bot, dead, stunned, rooted, forced, casting, resurrecting, rewardChosen;
        public string name, castName;
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
    }
    [Serializable] public sealed class RaidCombatMechanic
    {
        public int id, kind, target; // 0..2 telegraphs, 3 spread, 4 stack, 5 tether, 6 sanctuary.
        public float x, z, yaw, inner, outer, angle, start, resolve, end;
        public bool intercepted;
    }
    [Serializable] public sealed class RaidMetricMember
    {
        public int id, role, deaths; public string name;
        public double bossDamage, addDamage, healing, selfHealing, shield;
    }
    [Serializable] public sealed class RaidMetricResult
    {
        public int stage; public double seconds; public RaidMetricMember[] members;
    }
    [Serializable] public sealed class RaidCombatFrame
    {
        public int epoch, stage, retries;
        public RaidBattlePhase phase;
        public float time, countdownEnd, runTime;
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
    }
}
