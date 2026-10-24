using UnityEngine;

namespace RaidRogue
{
    // NPC assistance uses the existing host action envelope and PartyLife revival. It is not a healer spell.
    public static class IrisRescue
    {
        public const float HealthFraction = .4f;
        public static bool TryUse(Transform requester, Transform target)
        {
            if (RaidCombatAuthority.Replica || StageManager.CombatSuspended || StageManager.Instance == null || requester == null)
                return false;
            var party = PartyLife.OrderedMembers();
            if (!party.Contains(requester) || !party.Contains(target))
                return false;
            var life = target.GetComponent<PartyLife>();
            bool success = StageManager.Instance.Iris.TryRescue(life != null && life.IsDead, () => life.TryRevive(HealthFraction));
            if (success)
                FloatingTextPopup.Spawn(target.position + Vector3.up * 2.5f, "이리스의 구원 · 공용 코인 " + StageManager.Instance.Iris.Coins, new Color(.9f, .85f, 1), true);
            return success;
        }
    }

    public partial class StageManager
    {
        public readonly IrisRescueState Iris = new IrisRescueState();
        public int IrisCoins => RaidCombatAuthority.Replica ? _irisReplica : Iris.Coins;

        private int _irisReplica = IrisRescueState.DefaultCoins;
        public void RestoreIris(int coins)
        {
            if (RaidCombatAuthority.Replica)
                _irisReplica = Mathf.Clamp(coins, 0, 99);
        }
    }
}
