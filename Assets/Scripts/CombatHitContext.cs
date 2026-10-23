using UnityEngine;
namespace RaidRogue
{
    public readonly struct CombatHitContext
    {
        public readonly Transform Attacker;
        public readonly string SkillId;
        public readonly CombatDamageKind Kind;
        public readonly bool IgnoreGuard;
        public CombatHitContext(Transform attacker,string skillId,CombatDamageKind kind,bool ignoreGuard=false)
        {Attacker=attacker;SkillId=skillId;Kind=kind;IgnoreGuard=ignoreGuard;}
    }
    public static class CombatSecondaryDamage
    {
        // This amount is derived from already confirmed damage. Do not mitigate/buff it a second time.
        public static float Apply(Transform source,Transform target,double amount,string origin,CombatDamageKind kind)
        {
            if(RaidCombatAuthority.Replica||StageManager.CombatSuspended||!CombatRange.LivingPartyMember(source)||!CombatRange.LivingEnemy(target)||!CombatDamageRules.FinitePositive(amount))return 0;
            var boss=target.GetComponent<BossController>();
            return boss!=null?boss.ReceiveConfirmedSecondary((float)amount,source,origin,kind):target.GetComponent<BossAddMinion>().ReceiveConfirmedSecondary((float)amount,source,origin,kind);
        }
        public static void Absorption(int source,double amount,CombatHitContext hit)
        {
            CombatMetricsManager.Absorption(source,amount,hit.Kind!=CombatDamageKind.Reflection&&hit.Kind!=CombatDamageKind.Extra&&hit.Kind!=CombatDamageKind.Deferred&&hit.Kind!=CombatDamageKind.System);
            if(!CombatDamageRules.CanReact(hit.Kind)||!CombatRange.LivingEnemy(hit.Attacker))return;
            foreach(var member in PartyLife.OrderedMembers())
                if(CombatMetricsManager.SourceId(member)==source){member.GetComponent<AegisPaladin>()?.ReflectAbsorption(amount,hit);break;}
        }
    }
}
