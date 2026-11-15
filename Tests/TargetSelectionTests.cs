using System;
using System.Collections.Generic;

namespace RaidRogue
{
    enum SkillTargetRule { None, Self, LivingEnemy, LivingAlly, DeadAlly }
    enum TargetFaction { None, Friendly, Enemy }
    class Transform
    {
        public TargetFaction Faction;
        public bool Alive = true;
        public BossController Boss;
        public PartyLife Life = new PartyLife();
        public T GetComponent<T>() where T : class => typeof(T) == typeof(BossController) ? Boss as T : Life as T;
    }
    class BossController { public Transform transform; }
    class PartyLife
    {
        public bool IsDead;
        public static readonly List<Transform> Members = new List<Transform>();
        public static List<Transform> OrderedMembers() => Members;
    }
    static class CombatRange
    {
        public static bool LivingEnemy(Transform t) => t != null && t.Alive && t.Faction == TargetFaction.Enemy;
        public static bool LivingPartyMember(Transform t) => t != null && t.Alive && t.Faction == TargetFaction.Friendly;
    }
    class TargetingManager
    {
        private Transform currentTarget;
        /* ACTUAL_TARGET_STATE */
        public Transform Current => currentTarget;
        public static bool BossVisible = true;
        private static bool BossCanBeTargeted() => BossVisible;
        private Transform ResolveUnitRoot(Transform target) => target;
        private TargetFaction GetTargetFaction(Transform target) => target?.Faction ?? TargetFaction.None;
        /* ACTUAL_TARGET_METHODS */
    }
    static class TargetSelectionTests
    {
        private static int checks;
        private static void Check(bool ok, string message)
        { checks++; if (!ok) { Console.Error.WriteLine("FAIL: " + message); Environment.Exit(1); } }
        private static void Main()
        {
            var self = new Transform { Faction = TargetFaction.Friendly };
            var ally = new Transform { Faction = TargetFaction.Friendly };
            var add = new Transform { Faction = TargetFaction.Enemy };
            var boss = new BossController { transform = new Transform { Faction = TargetFaction.Enemy } };
            boss.transform.Boss = boss;
            PartyLife.Members.Add(self); PartyLife.Members.Add(ally);
            var tm = new TargetingManager();
            Transform target; string reason;
            Check(tm.TryResolveSkillTarget(SkillTargetRule.LivingAlly, self, boss, out target, out reason) && target == self, "unselected support defaults to self");
            tm.SetTarget(add); tm.SetTarget(ally);
            Check(tm.AttackTarget == add && tm.SupportTarget == ally && tm.Current == ally, "friendly click preserves selected add");
            Check(tm.TryResolveSkillTarget(SkillTargetRule.LivingEnemy, self, boss, out target, out reason) && target == add, "attack routes to saved add, not ally target or boss");
            tm.SetTarget(boss.transform);
            Check(tm.SupportTarget == ally, "enemy click preserves support");
            Check(tm.TryResolveSkillTarget(SkillTargetRule.LivingAlly, self, boss, out target, out reason) && target == ally, "targeted support routes to remembered ally");
            ally.Alive = false; ally.Life.IsDead = true;
            Check(!tm.TryResolveSkillTarget(SkillTargetRule.LivingAlly, self, boss, out target, out reason) && target == ally && reason.Length > 0, "dead selected ally rejects rather than self-heals");
            Check(tm.TryResolveSkillTarget(SkillTargetRule.DeadAlly, self, boss, out target, out reason) && target == ally, "existing dead-ally skill remains supported");
            Check(tm.TryResolveSkillTarget(SkillTargetRule.Self, self, boss, out target, out reason) && target == self, "self-centered skill ignores invalid support");
            Check(tm.TryResolveSkillTarget(SkillTargetRule.None, self, boss, out target, out reason) && target == null, "untargeted skill ignores invalid support");
            tm.ForgetRemovedTarget(ally);
            Check(tm.HasSupportSelection && tm.SupportTarget == null, "removed ally retains explicit intent");
            Check(!tm.TryResolveSkillTarget(SkillTargetRule.LivingAlly, self, boss, out target, out reason) && target == null, "lost target does not become self");
            Check(tm.GetLivingOffensiveTarget(boss) == boss.transform, "removing support preserves attack");
            tm.SetTarget(self);
            Check(tm.TryResolveSkillTarget(SkillTargetRule.LivingAlly, self, boss, out target, out reason) && target == self, "explicit self selection recovers");
            tm.SetTarget(add); tm.ForgetRemovedTarget(add);
            Check(tm.SupportTarget == self, "enemy removal does not clear support");
            tm.ClearTarget();
            Check(!tm.HasSupportSelection && tm.AttackTarget == null && tm.SupportTarget == null, "intentional clear resets both channels");
            Check(tm.TryResolveSkillTarget(SkillTargetRule.LivingAlly, self, boss, out target, out reason) && target == self, "clear restores self default");
            Check(tm.GetLivingOffensiveTarget(boss) == boss.transform, "no enemy selection keeps existing boss fallback");
            tm.SetTarget(add); add.Alive = false;
            Check(!tm.TryResolveSkillTarget(SkillTargetRule.LivingEnemy, self, boss, out target, out reason), "invalid selected enemy not silently replaced by boss");
            tm.ClearTarget(); TargetingManager.BossVisible = false;
            tm.SetTarget(boss.transform);
            Check(tm.AttackTarget == null && tm.GetLivingOffensiveTarget(boss) == null, "hidden boss cannot be selected or used as fallback");
            TargetingManager.BossVisible = true; tm.SetTarget(boss.transform); TargetingManager.BossVisible = false;
            Check(tm.GetLivingOffensiveTarget(boss) == null, "remembered boss visibility rechecked");
            Console.WriteLine("PASS: target selection " + checks + " checks using actual selection/routing methods; Unity lifetime and input remain unverified.");
        }
    }
}
