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
        public float ControlImmunity {get;private set;}
        public float MorningEnd {get;private set;}
        public float InvulnerabilityEnd {get;private set;}
        public float BrandEnd {get;private set;}
        public bool MorningActive=>MorningEnd>RaidCombatAuthority.ServerTime;
        public bool CombatInvulnerable=>InvulnerabilityEnd>RaidCombatAuthority.ServerTime;
        public bool BrandActive=>BrandEnd>RaidCombatAuthority.ServerTime;
        public string ExtensionLabel()
        {
            float now=RaidCombatAuthority.ServerTime;
            if(CombatInvulnerable)return $"엑스칼리버 무적 {InvulnerabilityEnd-now:0.0}s";
            if(MorningActive)return $"아침의 성역 {MorningEnd-now:0.0}s";
            if(BrandActive)return $"규탄의 낙인 {BrandEnd-now:0.0}s · 치명 +10%p";
            return "";
        }
        public void GrantMorning(float end){if(!RaidCombatAuthority.Replica&&CombatRange.LivingPartyMember(transform))MorningEnd=Mathf.Max(MorningEnd,end);}
        public void GrantCombatInvulnerability(float end){if(!RaidCombatAuthority.Replica&&CombatRange.LivingPartyMember(transform))InvulnerabilityEnd=Mathf.Max(InvulnerabilityEnd,end);}
        public void ApplyBrand(float end){if(!RaidCombatAuthority.Replica&&CombatRange.LivingEnemy(transform)){BrandEnd=Mathf.Max(BrandEnd,end);FloatingTextPopup.Spawn(transform.position+Vector3.up*2,"규탄의 낙인 · 치명 +10%p",Color.yellow,false);}}
        public void RestoreBrand(float end){if(RaidCombatAuthority.Replica)BrandEnd=end;}
        private int _knockbackSource;
        private int _controlSource;
        public bool Stunned=>_debuffs.Exists(d=>d.stun&&d.remaining>0);
        public bool Rooted=>_debuffs.Exists(d=>d.root&&d.remaining>0);
        public static CombatStatusEffects Ensure(GameObject go)=>go.GetComponent<CombatStatusEffects>()??go.AddComponent<CombatStatusEffects>();
        public void Apply(string id,string tag,float duration,bool dispellable,bool stun=false,bool root=false,bool ignoreImmunity=false)
        {
            if(RaidCombatAuthority.Replica||!CombatRange.LivingPartyMember(transform)||duration<=0||!CharacterDefinitionValidation.Finite(duration)||string.IsNullOrEmpty(id))return;
            if((stun||root)&&!CombatDamageRules.CanApplyControl(ControlImmunity,ignoreImmunity))return;
            if(stun||root)duration=AegisRouteData.ControlDuration(duration,GetComponent<AegisPaladin>()?.Tenacity??0);
            _debuffs.RemoveAll(d=>d.id==id);_debuffs.Add(new CombatDebuff{id=id,tag=tag,remaining=duration,dispellable=dispellable,stun=stun,root=root});
        }
        public bool CleanseOne(string[] tags)
        {if(RaidCombatAuthority.Replica)return false;int i=CombatDispel.FirstAllowed(_debuffs,tags);if(i<0)return false;_debuffs.RemoveAt(i);return true;}
        public void Clear(){_debuffs.Clear();KnockbackImmunity=ControlImmunity=MorningEnd=InvulnerabilityEnd=BrandEnd=0;_knockbackSource=_controlSource=0;}
        public void GrantControlImmunity(float duration,Transform source)
        {if(!RaidCombatAuthority.Replica&&CombatRange.LivingPartyMember(transform)&&duration>=ControlImmunity){ControlImmunity=duration;_controlSource=CombatMetricsManager.SourceId(source);}}
        public void GrantKnockbackImmunity(float duration,Transform source)
        {if(!RaidCombatAuthority.Replica&&CombatRange.LivingPartyMember(transform)&&duration>=KnockbackImmunity){KnockbackImmunity=duration;_knockbackSource=CombatMetricsManager.SourceId(source);}}
        public void RemoveProtectionFrom(int source)
        {if(RaidCombatAuthority.Replica||source==0)return;if(source==_knockbackSource){KnockbackImmunity=0;_knockbackSource=0;}if(source==_controlSource){ControlImmunity=0;_controlSource=0;}}
        public static float ShieldAmount(float amount,Transform source)=>amount*(source!=null?source.GetComponent<AegisPaladin>()?.OutgoingShieldScale??1:1);
        public string Capture()=>JsonUtility.ToJson(new CombatDebuffSnapshot{knockbackImmunity=KnockbackImmunity,knockbackSource=_knockbackSource,controlImmunity=ControlImmunity,controlSource=_controlSource,morningEnd=MorningEnd,invulnerabilityEnd=InvulnerabilityEnd,entries=_debuffs.ToArray()});
        public void Restore(string json)
        {if(!RaidCombatAuthority.Replica)return;Clear();if(string.IsNullOrEmpty(json))return;var s=JsonUtility.FromJson<CombatDebuffSnapshot>(json);if(s?.entries!=null){_debuffs.AddRange(s.entries);KnockbackImmunity=Mathf.Max(0,s.knockbackImmunity);_knockbackSource=s.knockbackSource;ControlImmunity=Mathf.Max(0,s.controlImmunity);_controlSource=s.controlSource;MorningEnd=s.morningEnd;InvulnerabilityEnd=s.invulnerabilityEnd;}}
        private void Update()
        {
            if(RaidCombatAuthority.Replica)return;
            GetComponent<PlayerController>()?.ExpireTimedShields();GetComponent<PartyBotAI>()?.ExpireTimedShields();
            if(StageManager.CombatSuspended)return;
            KnockbackImmunity=Mathf.Max(0,KnockbackImmunity-Time.deltaTime);
            ControlImmunity=Mathf.Max(0,ControlImmunity-Time.deltaTime);
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
        public void ApplyCharacterAppearance(Color body,Color accent)
        {
            normalBodyColor=body;
            if(dualVisual!=null)dualVisual.SetSecondaryColor(accent);
            if(!IsDead)ApplyBodyColor(IsDefensiveBuffActive?defensiveBodyColor:body);
        }
        private readonly CombatEventGate _resolvedCombatHits=new CombatEventGate();
        private Vector3 _skillRushDirection;
        private float _skillRushRemaining,_skillRushSpeed;
        public bool IsSkillRushing=>_skillRushRemaining>0;
        public void ClearResolvedCombatHits()=>_resolvedCombatHits.Clear();
        public void CancelSkillRush(){_skillRushRemaining=_skillRushSpeed=0;}
        public bool PlanSkillRush(Transform target,float distance,out Vector3 end)
        {
            end=transform.position;
            if(!CanMoveVoluntarily||IsDashing||IsSkillRushing||!CombatRange.LivingEnemy(target))return false;
            Vector3 delta=target.position-transform.position;delta.y=0;
            if(delta.sqrMagnitude>.00001f&&Vector3.Dot(transform.forward,delta.normalized)<0)return false;
            float travel=Mathf.Min(distance,Mathf.Max(0,delta.magnitude-CombatRange.Radius(transform)-CombatRange.Radius(target)-.1f));
            end=_movementCollision.Move(this,delta,travel,out bool blocked);
            return !blocked;
        }
        public void BeginSkillRush(Vector3 end,float seconds)
        {
            Vector3 delta=end-transform.position;delta.y=0;
            _skillRushDirection=delta.normalized;_skillRushRemaining=delta.magnitude;_skillRushSpeed=delta.magnitude/Mathf.Max(.01f,seconds);
            if(_dashPresentation!=null&&IsSkillRushing)_dashPresentation.BeginDash();
        }
        public void StepSkillRush(float seconds)
        {
            if(IsDead||IsStunned||IsMovementLocked){CancelSkillRush();GetComponent<CharacterActor>()?.InterruptCast();return;}
            Vector3 before=transform.position;float travel=Mathf.Min(_skillRushRemaining,_skillRushSpeed*seconds);
            transform.position=_movementCollision.Move(this,_skillRushDirection,travel,out bool blocked);
            _skillRushRemaining=Mathf.Max(0,_skillRushRemaining-travel);
            if(blocked){CancelSkillRush();GetComponent<CharacterActor>()?.InterruptCast();}
            if(_dashPresentation!=null)_dashPresentation.RecordMovement(before,transform.position);
        }
        public void RefillMovementCharges()
        {
            if(RaidCombatAuthority.Replica||IsDead)return;
            var actor=CharacterActor.Ensure(gameObject);var settings=actor.MovementSettings(dashSettings);
            if(RaidCombatAuthority.Active)NetworkMotor?.Dash.RefillCharges(settings);else _dash.RefillCharges(settings);
        }
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
            if(GetComponent<CombatStatusEffects>()?.CombatInvulnerable==true)return;
            currentHp=Mathf.Max(0,currentHp-amount);if(currentHp<=0)PartyLife.Ensure(gameObject).Die();
        }
    }
    public partial class PartyBotAI
    {
        private readonly CombatEventGate _resolvedCombatHits=new CombatEventGate();
        public void ClearResolvedCombatHits()=>_resolvedCombatHits.Clear();
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
