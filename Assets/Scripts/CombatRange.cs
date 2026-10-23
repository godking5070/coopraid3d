using UnityEngine;

namespace RaidRogue
{
    public static class CombatRange
    {
        public static float Radius(Transform unit)
        {
            if (unit == null) return 0;
            var network = unit.GetComponent<NetworkRaidAvatar>();
            if (network != null) return network.CombatRadius;
            var boss = unit.GetComponent<BossController>();
            if (boss != null) return boss.CombatRadius;
            var add = unit.GetComponent<BossAddMinion>();
            if (add != null) return Mathf.Max(0, add.combatRadius);
            var player = unit.GetComponent<PlayerController>();
            if (player != null) return Mathf.Max(0, player.combatRadius);
            var bot = unit.GetComponent<PartyBotAI>();
            return bot != null ? Mathf.Max(0, bot.combatRadius) : 0;
        }

        public static bool LivingEnemy(Transform unit)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) return false;
            var boss = unit.GetComponent<BossController>();
            if (boss != null) return boss.currentHp > 0 && !boss.IsBossDefeatedCollapse;
            var add = unit.GetComponent<BossAddMinion>();
            return add != null && !add.isDead && add.currentHp > 0 &&
                (add.ownerBoss == null || !add.ownerBoss.IsBossDefeatedCollapse);
        }

        public static bool LivingPartyMember(Transform unit)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) return false;
            if (unit.GetComponent<NetworkRaidAvatar>() is NetworkRaidAvatar avatar && avatar.State != null && RaidCombatAuthority.Replica) return !avatar.State.dead;
            var player = unit.GetComponent<PlayerController>();
            if (player != null) return !player.IsDead;
            var bot = unit.GetComponent<PartyBotAI>();
            return bot != null && !bot.isDead && bot.currentHp > 0;
        }

        public static float BoundaryDistance(Transform attacker, Transform target)
        {
            if(attacker==null||target==null)return float.PositiveInfinity;
            return (float)CombatRangeMath.BoundaryDistance(CombatRangeMath.CenterDistance(attacker.position.x,attacker.position.z,target.position.x,target.position.z),Radius(attacker),Radius(target));
        }
        public static bool InRange(Transform attacker, Transform target, float range)
        {
            if (attacker == null || target == null) return false;
            return CombatRangeMath.InRange(CombatRangeMath.CenterDistance(attacker.position.x, attacker.position.z,
                target.position.x, target.position.z), Radius(attacker), Radius(target), range);
        }

        public static float RingRadius(Transform attacker, float range) => (float)CombatRangeMath.RingRadius(Radius(attacker), range);

        public static Vector3 AttackPosition(Transform attacker, Transform target, Vector3 direction, float range)
        {
            direction.y = 0;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.back;
            float distance = (float)CombatRangeMath.AttackCenterDistance(Radius(attacker), Radius(target), range);
            Vector3 pos = target.position + direction.normalized * distance;
            pos.y = attacker.position.y;
            return pos;
        }

        public static Vector3 ClampToArena(Vector3 position, float radius)
        {
            Vector2 flat = Vector2.ClampMagnitude(new Vector2(position.x, position.z), Mathf.Max(0, radius));
            return new Vector3(flat.x, position.y, flat.y);
        }
    }
}
