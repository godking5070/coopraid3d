using System;
using System.Collections.Generic;
using UnityEngine;
namespace RaidRogue
{
    // Explicit dispellable status registry. Unknown encounter flags are never cleared by cleansing.
    public sealed class CombatStatusEffects : MonoBehaviour
    {
        private readonly List<CombatDebuff> _debuffs=new List<CombatDebuff>();
        public float KnockbackImmunity {get;private set;}
        private int _knockbackSource;
        public bool Stunned=>_debuffs.Exists(d=>d.stun&&d.remaining>0);
        public bool Rooted=>_debuffs.Exists(d=>d.root&&d.remaining>0);
        public static CombatStatusEffects Ensure(GameObject go)=>go.GetComponent<CombatStatusEffects>()??go.AddComponent<CombatStatusEffects>();
        public void Apply(string id,string tag,float duration,bool dispellable,bool stun=false,bool root=false)
        {
            if(RaidCombatAuthority.Replica||!CombatRange.LivingPartyMember(transform)||duration<=0||!CharacterDefinitionValidation.Finite(duration)||string.IsNullOrEmpty(id))return;
            _debuffs.RemoveAll(d=>d.id==id);_debuffs.Add(new CombatDebuff{id=id,tag=tag,remaining=duration,dispellable=dispellable,stun=stun,root=root});
        }
        public bool CleanseOne(string[] tags)
        {if(RaidCombatAuthority.Replica)return false;int i=CombatDispel.FirstAllowed(_debuffs,tags);if(i<0)return false;_debuffs.RemoveAt(i);return true;}
        public void Clear(){_debuffs.Clear();KnockbackImmunity=0;_knockbackSource=0;}
        public void GrantKnockbackImmunity(float duration,Transform source)
        {if(!RaidCombatAuthority.Replica&&CombatRange.LivingPartyMember(transform)&&duration>=KnockbackImmunity){KnockbackImmunity=duration;_knockbackSource=CombatMetricsManager.SourceId(source);}}
        public void RemoveProtectionFrom(int source){if(!RaidCombatAuthority.Replica&&source!=0&&source==_knockbackSource){KnockbackImmunity=0;_knockbackSource=0;}}
        public static float ShieldAmount(float amount,Transform source)=>amount*(source!=null?source.GetComponent<AegisPaladin>()?.OutgoingShieldScale??1:1);
        public string Capture()=>JsonUtility.ToJson(new CombatDebuffSnapshot{knockbackImmunity=KnockbackImmunity,knockbackSource=_knockbackSource,entries=_debuffs.ToArray()});
        public void Restore(string json)
        {if(!RaidCombatAuthority.Replica)return;Clear();if(string.IsNullOrEmpty(json))return;var s=JsonUtility.FromJson<CombatDebuffSnapshot>(json);if(s?.entries!=null){_debuffs.AddRange(s.entries);KnockbackImmunity=Mathf.Max(0,s.knockbackImmunity);_knockbackSource=s.knockbackSource;}}
        private void Update()
        {
            if(RaidCombatAuthority.Replica)return;
            GetComponent<PlayerController>()?.ExpireTimedShields();GetComponent<PartyBotAI>()?.ExpireTimedShields();
            if(StageManager.CombatSuspended)return;
            KnockbackImmunity=Mathf.Max(0,KnockbackImmunity-Time.deltaTime);
            foreach(var d in _debuffs)d.remaining-=Time.deltaTime;_debuffs.RemoveAll(d=>d.remaining<=0);
        }
        public static void Shield(Transform recipient,Transform source,string key,float amount,float duration)
        {
            if(!CombatRange.LivingPartyMember(recipient)||RaidCombatAuthority.Replica||StageManager.CombatSuspended)return;
            Ensure(recipient.gameObject);var pc=recipient.GetComponent<PlayerController>();
            if(pc!=null)pc.AddTimedShield(amount,source,key,duration);else recipient.GetComponent<PartyBotAI>()?.AddTimedShield(amount,source,key,duration);
        }
    }
    public partial class PlayerController
    {
        public void RemoveShieldsFrom(int source)
        {if(!RaidCombatAuthority.Replica){_metricsShields.Reconcile(currentShield);currentShield=Mathf.Max(0,currentShield-(float)_metricsShields.RemoveSource(source));}}
        public void ExpireTimedShields()
        {if(!RaidCombatAuthority.Replica){_metricsShields.Reconcile(currentShield);currentShield=Mathf.Max(0,currentShield-(float)_metricsShields.Expire(Time.time));}}
        public void AddTimedShield(float amount,Transform source,string key,float duration)
        {
            if(RaidCombatAuthority.Replica||IsDead||StageManager.CombatSuspended||duration<=0)return;
            amount=CombatStatusEffects.ShieldAmount(amount,source);
            ExpireTimedShields();_metricsShields.GrantTimed(CombatMetricsManager.SourceId(source),key,amount,maxHp,Time.time+duration);currentShield=(float)_metricsShields.Remaining;
            FloatingTextPopup.Spawn(transform.position+Vector3.up*2.3f,"보호막 "+Mathf.RoundToInt(amount),Color.yellow,false);
        }
        // Repayment of an already confirmed hit: no mitigation, shield, reflection or resource callbacks.
        public void ApplyDeferredDamage(float amount)
        {
            if(RaidCombatAuthority.Replica||IsDead||StageManager.CombatSuspended||amount<=0)return;
            currentHp=Mathf.Max(0,currentHp-amount);if(currentHp<=0)PartyLife.Ensure(gameObject).Die();
        }
    }
    public partial class PartyBotAI
    {
        public void RemoveShieldsFrom(int source)
        {if(!RaidCombatAuthority.Replica){_metricsShields.Reconcile(currentShield);currentShield=Mathf.Max(0,currentShield-(float)_metricsShields.RemoveSource(source));}}
        public void ExpireTimedShields()
        {if(!RaidCombatAuthority.Replica){_metricsShields.Reconcile(currentShield);currentShield=Mathf.Max(0,currentShield-(float)_metricsShields.Expire(Time.time));}}
        public void AddTimedShield(float amount,Transform source,string key,float duration)
        {
            if(RaidCombatAuthority.Replica||isDead||StageManager.CombatSuspended||duration<=0)return;
            amount=CombatStatusEffects.ShieldAmount(amount,source);
            ExpireTimedShields();_metricsShields.GrantTimed(CombatMetricsManager.SourceId(source),key,amount,maxHp,Time.time+duration);currentShield=(float)_metricsShields.Remaining;
            FloatingTextPopup.Spawn(transform.position+Vector3.up*2.3f,"보호막 "+Mathf.RoundToInt(amount),Color.yellow,false);
        }
    }
}
