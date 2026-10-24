using System;
using System.Collections.Generic;

namespace RaidRogue
{
    // Host-owned party budget. Death keys include the target's death generation.
    public sealed class IrisRescueState
    {
        public const int DefaultCoins = 4;
        private readonly object _sync = new object();
        private readonly HashSet<string> _handled = new HashSet<string>();
        public string AttemptId { get; private set; } = "";
        public int Coins { get; private set; } = DefaultCoins;
        public int TotalUsed { get; private set; }
        public int AttemptsStarted { get; private set; }
        public int Pending { get; private set; }

        // Only encounter preparation calls this; death and control transfer never do.
        public void BeginAttempt(int? restoredTotal = null, int? restoredAttempts = null)
        {
            lock (_sync)
            {
                if (Pending != 0) throw new InvalidOperationException("Revival transaction in progress");
                if (restoredTotal.HasValue) TotalUsed = Math.Max(0, restoredTotal.Value);
                if (restoredAttempts.HasValue) AttemptsStarted = Math.Max(0, restoredAttempts.Value);
                AttemptId = Guid.NewGuid().ToString("N");
                Coins = DefaultCoins; AttemptsStarted++; _handled.Clear();
            }
        }

        public bool TryRescue(string attempt, string deathKey, Func<bool> valid, Func<bool> revive)
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(AttemptId) || attempt != AttemptId || string.IsNullOrEmpty(deathKey) ||
                    Coins <= 0 || _handled.Contains(deathKey) || valid == null || revive == null || !valid()) return false;
                // Reserve before callbacks: re-entry and last-coin races see the reservation.
                _handled.Add(deathKey); Coins--; Pending++;
                bool success = false;
                try { success = revive(); if (success) TotalUsed++; return success; }
                finally
                {
                    Pending--;
                    if (!success) { Coins++; _handled.Remove(deathKey); }
                }
            }
        }
    }
}
