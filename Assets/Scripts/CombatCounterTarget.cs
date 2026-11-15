using UnityEngine;
namespace RaidRogue
{
    // Patterns opt in explicitly. A normal cast is NOT implicitly counterable.
    public sealed class CombatCounterTarget : MonoBehaviour
    {
        public float Stagger {get;private set;}
        private readonly CounterOpportunity _opportunity=new CounterOpportunity();
        public float WindowEnd=>_opportunity.End;
        public int OpportunityId=>_opportunity.Id;
        public bool WindowOpen=>WindowEnd>RaidCombatAuthority.ServerTime&&CombatRange.LivingEnemy(transform);
        public static CombatCounterTarget Ensure(Transform target)=>target.GetComponent<CombatCounterTarget>()??target.gameObject.AddComponent<CombatCounterTarget>();
        public void OpenWindow(float seconds){if(!RaidCombatAuthority.Replica)_opportunity.Open(RaidCombatAuthority.ServerTime,seconds);}
        public void CloseWindow(){if(!RaidCombatAuthority.Replica)_opportunity.Clear();}
        public void ResetState(){_opportunity.Clear();Stagger=0;}
        public void AddStagger(float points)
        {
            var boss=GetComponent<BossController>();
            if(RaidCombatAuthority.Replica||!CombatRange.LivingEnemy(transform)||boss!=null&&boss.IsGroggy)return;
            if(boss != null && boss.GuardianNormal) { boss.AddGuardianStagger(points); return; }
            Stagger+=Mathf.Max(0,points);
            CharacterTrainingSession.Instance?.RecordStagger(Mathf.Max(0,points));
            if(boss!=null&&Stagger>=AegisPaladinData.StaggerThreshold)
            {
                Stagger=0;_opportunity.Clear();boss.ApplyCounterGroggy(AegisPaladinData.CounterGroggy);
                FloatingTextPopup.Spawn(transform.position+Vector3.up*3,"무력화!",Color.cyan,true);
            }
        }
        public bool CanCounter(Transform attacker,float range,Vector3? attackFacing=null)
        {
            if(attacker==null)return false;
            Vector3 toAttacker=attacker.position-transform.position;toAttacker.y=0;toAttacker.Normalize();
            return CounterRules.Valid(WindowOpen,CombatRange.LivingEnemy(transform),CombatRange.InRange(attacker,transform,range),Vector3.Dot(transform.forward,toAttacker),Vector3.Dot(attackFacing??attacker.forward,-toAttacker),AegisPaladinData.CounterFrontAngle);
        }
        public bool TryCounter(Transform attacker,float range,float hitStagger=0,int opportunityId=-1,Vector3? attackFacing=null)
        {
            if(RaidCombatAuthority.Replica||attacker==null||!CombatRange.LivingPartyMember(attacker)||!_opportunity.Consume(opportunityId<0?OpportunityId:opportunityId,RaidCombatAuthority.ServerTime,CanCounter(attacker,range,attackFacing)))return false;
            CharacterTrainingSession.Instance?.RecordCounter();
            // Consume before reactions: one opportunity yields one party-wide charge restoration.
            foreach(var member in PartyLife.OrderedMembers())if(CombatRange.LivingPartyMember(member))CharacterActor.Ensure(member.gameObject).RefreshMovementCharges();
            var boss = GetComponent<BossController>();
            if(boss != null && boss.GuardianNormal) boss.GuardianCounterSucceeded();
            else { AddStagger(hitStagger+AegisPaladinData.CounterBonusStagger); boss?.ApplyCounterGroggy(AegisPaladinData.CounterGroggy); }
            FloatingTextPopup.Spawn(transform.position+Vector3.up*3,"COUNTER!",Color.cyan,true);return true;
        }
        public void Restore(float stagger,float window){if(RaidCombatAuthority.Replica){Stagger=stagger;_opportunity.RestoreEnd(window);}}
    }
    public partial class BossController
    {
        private bool _counterGroggy;
        public void ApplyCounterGroggy(float seconds)
        {
            if(GuardianNormal)return;
            if(RaidCombatAuthority.Replica||IsGroggy||IsBossDefeatedCollapse||StageManager.CombatSuspended)return;
            CleanupActiveTelegraphAndTether();_isChasing=false;_pendingPositionedSkill=null;
            _counterGroggy=true;fsmState=BossFSMState.Groggy;_groggyRemainingTime=seconds;
            // Keep currentPhase: counter interruption must never advance the HP phase.
        }
    }
}
