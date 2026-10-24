using System;

namespace RaidRogue
{
    // Provisional encounter allowance; a retry resets it, reconnect does not.
    public sealed class IrisRescueState
    {
        public const int DefaultCoins = 3;
        public int Coins { get; private set; } = DefaultCoins;

        public void Reset(int allowance = DefaultCoins) => Coins = Math.Max(0, Math.Min(99, allowance));
        public bool TryRescue(bool valid, Func<bool> revive)
        {
            if (!valid || Coins <= 0 || revive == null || !revive())
                return false;
            Coins--;
            return true;
        }
    }
}
