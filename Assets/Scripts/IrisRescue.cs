using UnityEngine;

namespace RaidRogue
{
    // NPC assistance uses the existing host action envelope and PartyLife revival. It is not a healer spell.
    public static class IrisRescue
    {
        public const float HealthFraction = .4f;
        public static string Unavailable(Transform requester, Transform target)
        {
            if (StageManager.Instance == null || StageManager.CombatSuspended) return "지금은 전투 중이 아닙니다";
            if (requester == null || target == null) return "구조할 사망 아군을 선택하세요";
            var party = PartyLife.OrderedMembers();
            if (!party.Contains(requester) || !party.Contains(target)) return "파티원만 구조할 수 있습니다";
            if (target.GetComponent<PartyLife>()?.IsDead != true) return "살아 있는 대상은 구조할 수 없습니다";
            if (StageManager.Instance.IrisCoins <= 0) return "이번 도전의 공유 부활을 모두 사용했습니다";
            return "";
        }
        public static bool TryUse(Transform requester, Transform target, string attempt, int deathGeneration)
            => TryUse(requester, target, attempt, deathGeneration, out _);

        public static bool TryUse(Transform requester, Transform target, string attempt, int deathGeneration, out string reason)
        {
            reason = Unavailable(requester, target);
            if (RaidCombatAuthority.Replica) { reason = "호스트의 구조 확인을 기다리세요"; return false; }
            if (reason.Length > 0) return false;
            var life = target.GetComponent<PartyLife>();
            string key = target.GetInstanceID() + "/" + deathGeneration;
            bool success = StageManager.Instance.Iris.TryRescue(attempt, key,
                () => life != null && life.IsDead && life.DeathGeneration == deathGeneration && !StageManager.CombatSuspended,
                () => life.TryRevive(HealthFraction));
            if (success)
                FloatingTextPopup.Spawn(target.position + Vector3.up * 2.5f, "이리스의 구원 · 공용 코인 " + StageManager.Instance.Iris.Coins, new Color(.9f, .85f, 1), true);
            reason = success ? "이리스 구조 완료 · 이번 도전 부활 " + StageManager.Instance.Iris.Coins + " / 4"
                : "이미 처리했거나 이전 도전의 구조 요청입니다";
            return success;
        }
    }

    public partial class StageManager
    {
        public readonly IrisRescueState Iris = new IrisRescueState();
        public int IrisCoins => RaidCombatAuthority.Replica ? _irisReplica : Iris.Coins;
        public string AttemptId => RaidCombatAuthority.Replica ? _attemptReplica : Iris.AttemptId;
        public int TotalRevives => RaidCombatAuthority.Replica ? _totalRevivesReplica : Iris.TotalUsed;

        private int _irisReplica = IrisRescueState.DefaultCoins;
        private string _attemptReplica = "";
        private int _totalRevivesReplica;
        public void RestoreIris(int coins, string attempt, int total)
        {
            if (RaidCombatAuthority.Replica)
            { _irisReplica = Mathf.Clamp(coins, 0, IrisRescueState.DefaultCoins); _attemptReplica = attempt; _totalRevivesReplica = total; }
        }
    }
}
