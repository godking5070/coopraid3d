using UnityEngine;
namespace RaidRogue
{
    public readonly struct CombatHitContext
    {
        public readonly Transform Attacker;
        public readonly string SkillId;
        public readonly CombatDamageKind Kind;
        public readonly bool IgnoreGuard;
        public readonly bool Projectile;
        public readonly CombatDefenseType Defense;
        public readonly ulong EventId;
        private static ulong _nextEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEvents()=>_nextEvent=0;
        public CombatHitContext(Transform attacker,string skillId,CombatDamageKind kind,bool ignoreGuard=false,CombatDefenseType defense=CombatDefenseType.Physical,ulong eventId=0,bool projectile=false)
        {Projectile=projectile;Attacker=attacker;SkillId=skillId;Kind=kind;IgnoreGuard=ignoreGuard;Defense=defense;EventId=eventId!=0?eventId:++_nextEvent;}
    }
    public static class CombatSecondaryDamage
    {
        // This amount is derived from already confirmed damage. Do not mitigate/buff it a second time.
        public static float Apply(Transform source,Transform target,double amount,string origin,CombatDamageKind kind,bool alreadyScaled=false)
        {
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!CombatRange.LivingPartyMember(source)||!CombatRange.LivingEnemy(target)||!CombatDamageRules.FinitePositive(amount))return 0;
            var boss=target.GetComponent<BossController>();
            return boss!=null?boss.ReceiveConfirmedSecondary((float)amount,source,origin,kind,alreadyScaled):target.GetComponent<BossAddMinion>().ReceiveConfirmedSecondary((float)amount,source,origin,kind,alreadyScaled);
        }
        public static void Absorption(int source,double amount,CombatHitContext hit)
        {
            CombatMetricsManager.Absorption(source,amount,hit.Kind!=CombatDamageKind.Reflection&&hit.Kind!=CombatDamageKind.Extra&&hit.Kind!=CombatDamageKind.Deferred&&hit.Kind!=CombatDamageKind.System&&hit.Kind!=CombatDamageKind.Transfer);
            if(!CombatDamageRules.CanReact(hit.Kind)||!CombatRange.LivingEnemy(hit.Attacker))return;
            foreach(var member in PartyLife.OrderedMembers())
                if(CombatMetricsManager.SourceId(member)==source){member.GetComponent<AegisPaladin>()?.ReflectAbsorption(amount,hit);break;}
        }
    }
}
