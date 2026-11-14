using UnityEngine;

namespace RaidRogue
{
    // Shared decisions for regular bots and temporary controllers. No damage or mutable combat state here.
    public static class PartyAiTactics
    {
        public static Vector3 TankPosition(Transform actor, BossController boss, float range, float parking)
            => boss.isMeleeBoss && boss.ResolveLivingAggroTarget() == actor ? new Vector3(0, actor.position.y, parking)
                : CombatRange.AttackPosition(actor, boss.transform, Vector3.forward, range);
        public static bool PartyMechanic(Transform actor, int role, BossController boss, out Vector3 destination)
        {
            destination = actor.position;
            if (boss.GuardianNormal) return boss.GuardianDestination(actor,role,out destination);
            if (boss.IsStackMechanicActive && boss.StackTarget != null)
            {
                if (boss.StackTarget != actor)
                {
                    Vector3 offset = role == 1 ? Vector3.forward * .85f : role == 2 ? new Vector3(-.8f, 0, -.55f)
                        : role == 3 ? new Vector3(-.75f, 0, .65f) : new Vector3(.8f, 0, -.55f);
                    destination = boss.StackTarget.position + offset;
                }
                return true;
            }
            if (!boss.IsSpreadMechanicActive) return false;
            float radius = boss.ActiveTelegraph != null && boss.ActiveTelegraph.telegraphType == AoETelegraphType.Donut_Inner4_Outer12 ? 2.85f : 10f;
            Vector3 direction = role == 1 ? Vector3.forward : role == 2 ? Vector3.left : role == 3 ? Vector3.back : Vector3.right;
            destination = boss.transform.position + direction * radius; destination.y = actor.position.y; return true;
        }
        public static bool HazardDestination(Transform actor, BossController boss, float range, out Vector3 destination)
        {
            destination = actor.position; if (boss.GuardianNormal) return false; var aoe = boss.ActiveTelegraph;
            if (aoe == null) return false;
            Vector3 direction = actor.position - aoe.transform.position; direction.y = 0;
            if (direction.sqrMagnitude < .001f) direction = -boss.transform.forward;
            float radius = CombatRange.Radius(actor);
            if (aoe.telegraphType == AoETelegraphType.Circle_Radius6)
                destination = aoe.transform.position + direction.normalized * (aoe.outerRadius + radius + .4f);
            else if (aoe.telegraphType == AoETelegraphType.Donut_Inner4_Outer12)
                destination = aoe.transform.position + direction.normalized * Mathf.Min(Mathf.Max(0, aoe.innerRadius - radius - .2f), boss.CombatRadius + radius + .2f);
            else if (aoe.telegraphType == AoETelegraphType.FrontCone_120Deg)
                destination = CombatRange.AttackPosition(actor, boss.transform, -boss.transform.forward, range);
            else return false;
            destination.y = actor.position.y; return true;
        }
        public static Transform LowestHealth(out float lowest)
        {
            Transform target = null; lowest = 1;
            foreach (var member in PartyLife.OrderedMembers())
            {
                if (!CombatRange.LivingPartyMember(member)) continue;
                var pc = member.GetComponent<PlayerController>(); var bot = member.GetComponent<PartyBotAI>();
                float ratio = pc != null ? pc.HpRatio : bot.HpRatio;
                if (ratio < lowest) { lowest = ratio; target = member; }
            }
            return target;
        }
        public static bool ResurrectionApproach(Transform actor, Transform corpse, float range, float arena, AoETelegraph hazard, out Vector3 best)
        {
            best = actor.position; float closest = float.PositiveInfinity;
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI * 2 / 24;
                Vector3 candidate = CombatRange.ClampToArena(corpse.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Mathf.Max(0, range - 1), arena);
                candidate.y = actor.position.y;
                if (BattleResurrection.FlatDistance(candidate, corpse.position) > range - .1f) continue;
                bool safe = true;
                for (int sample = 1; sample <= 12 && hazard != null; sample++)
                    if (hazard.IsPointInsideXZ(Vector3.Lerp(actor.position, candidate, sample / 12f))) { safe = false; break; }
                float distance = (candidate - actor.position).sqrMagnitude;
                if (safe && distance < closest) { closest = distance; best = candidate; }
            }
            return !float.IsPositiveInfinity(closest);
        }
    }
}
