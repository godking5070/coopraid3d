using UnityEngine;

namespace RaidRogue
{
    // Authority is a transport boundary around the existing controllers, not another combat simulator.
    public static class RaidCombatAuthority
    {
        internal static int ExecutingActor;
        public static bool Active => RaidNetworkSession.Multiplayer;
        public static bool Host => Active && RaidNetworkSession.Instance.IsHostAuthority;
        public static bool Replica => Active && !Host;
        public static bool Local(Component actor) => actor == null || actor.GetComponent<NetworkRaidAvatar>() == null || actor.GetComponent<NetworkRaidAvatar>().IsLocal;
        public static bool InputBlocked(Component actor) => Active && ExecutingActor == Id(actor != null ? actor.transform : null) && ExecutingActor != 0
            ? false : !Local(actor) || LocalGameplayInput.Blocked;
        public static bool SimulationInputBlocked(Component actor) => Local(actor) && LocalGameplayInput.Blocked;
        public static int Id(Transform target) => RaidNetworkSession.Instance != null ? RaidNetworkSession.Instance.CombatId(target) : 0;
        public static Transform Resolve(int id) => RaidNetworkSession.Instance != null ? RaidNetworkSession.Instance.CombatTarget(id) : null;
        public static bool Route(Component actor, int skill, Transform target = null)
        {
            if (!Active || (Host && actor.GetComponent<PartyBotAI>() != null) ||
                (ExecutingActor != 0 && ExecutingActor == Id(actor.transform))) return false;
            if (Local(actor) && !LocalGameplayInput.Blocked)
                RaidNetworkSession.Instance.RequestAction(skill, target != null ? Id(target) : Id(TargetingManager.Instance?.CurrentTarget));
            return true;
        }
        public static float ServerTime => Active ? RaidNetworkSession.Instance.ServerTime : Time.time;
        public static void Threat(Transform actor, float amount)
        {
            if (!Replica && amount > 0 && actor != null) StageManager.Instance?.boss?.AddCombatThreat(actor, amount);
        }
    }
}
