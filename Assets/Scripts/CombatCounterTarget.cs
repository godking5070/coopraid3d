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
            if(RaidCombatAuthority.Replica||StageManager.CombatSuspended||!CharacterDefinitionValidation.Finite(points)||points<=0||
                !CombatRange.LivingEnemy(transform))return;
            // Janus has two attackable bodies but one encounter/HP/stagger budget.
            var add=GetComponent<BossAddMinion>();
            if(add!=null&&add.addType==AddMinionType.JanusOmega&&add.ownerBoss!=null)
            {Ensure(add.ownerBoss.transform).AddStagger(points);return;}
            var boss=GetComponent<BossController>();
            if(boss!=null&&(boss.IsGroggy||boss.StaggerLocked))return;
            // Locked hits are discarded, never queued. Existing progress survives a mandatory pattern.
            float threshold=boss!=null?boss.StaggerThreshold:AegisPaladinData.StaggerThreshold;
            Stagger=Mathf.Min(threshold,Stagger+Mathf.Min(points,threshold));
            CharacterTrainingSession.Instance?.RecordStagger(points);
            if(boss!=null&&Stagger>=threshold&&boss.TryApplyStaggerGroggy())
            {
                Stagger=0;_opportunity.Clear();
                FloatingTextPopup.Spawn(transform.position+Vector3.up*3,"무력화!",Color.cyan,true, important: true);
            }
        }
        public bool CanCounter(Transform attacker,float range,Vector3? attackFacing=null)
        {
            if(attacker==null)return false;
            var guardian=GetComponent<BossController>();
            if(guardian!=null && guardian.GuardianClashCounterWindow && !guardian.GuardianClashCounterAllowed(attacker))return false;
            if(guardian!=null && guardian.CampanellaLockCounterWindow && !guardian.CampanellaLockCounterAllowed(attacker))return false;
            if(guardian!=null&&guardian.JanusEncounter&&!guardian.JanusCounterAllowed(attacker,false))return false;
            var janusAdd=GetComponent<BossAddMinion>();
            if(janusAdd!=null&&janusAdd.addType==AddMinionType.JanusOmega&&
                (janusAdd.ownerBoss==null||!janusAdd.ownerBoss.JanusCounterAllowed(attacker,true)))return false;
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
            bool interruptedCast=boss!=null&&boss.IsCasting;
            if(boss != null && boss.GuardianNormal) boss.GuardianCounterSucceeded(attacker);
            else if(boss != null && boss.FloraEncounter) boss.FloraCounterSucceeded(attacker);
            else if(boss != null && boss.CampanellaEncounter) boss.CampanellaCounterSucceeded(attacker);
            else if(boss != null && boss.MarionetteEncounter) boss.MarionetteCounterSucceeded(attacker);
            else if(boss != null && boss.JustitiaEncounter) boss.JustitiaCounterSucceeded(attacker);
            else if(boss != null && boss.JanusEncounter) boss.JanusCounterSucceeded(attacker,false);
            else if(GetComponent<BossAddMinion>() is BossAddMinion add&&add.addType==AddMinionType.JanusOmega)
                add.ownerBoss?.JanusCounterSucceeded(attacker,true);
            else { AddStagger(hitStagger+AegisPaladinData.CounterBonusStagger); boss?.ApplyCounterGroggy(AegisPaladinData.CounterGroggy); }
            if(interruptedCast)NineKeyDamageKit.NotifyBossAction(transform,DamageSealTrigger.CastEnd);
            bool tankHold=boss!=null&&(boss.GuardianClashHolding(attacker)||boss.CampanellaLockHolding(attacker));
            bool twin=boss?.JanusEncounter==true||GetComponent<BossAddMinion>()?.addType==AddMinionType.JanusOmega;
            FloatingTextPopup.Spawn(transform.position+Vector3.up*3,tankHold?"🛡 종치 봉쇄!":twin?"⚔ 쌍동 카운터!":"⚔ COUNTER!",
                tankHold||twin?new Color(.2f,.65f,1f):new Color(1f,.75f,.15f),true, important: true);return true;
        }
        public void Restore(float stagger,float window){if(RaidCombatAuthority.Replica){Stagger=stagger;_opportunity.RestoreEnd(window);}}
    }
    public partial class BossController
    {
        [Header("일반 무력화 · 프로토타입 튜닝")]
        public float normalStaggerThreshold=1000, normalStaggerGroggySeconds=8;
        private bool _replicaStaggerLocked;
        private float _replicaStaggerThreshold=1000;
        public float StaggerThreshold=>RaidCombatAuthority.Replica?_replicaStaggerThreshold:
            CharacterDefinitionValidation.Finite(normalStaggerThreshold)&&normalStaggerThreshold>0?normalStaggerThreshold:1000;
        public bool StaggerLocked=>RaidCombatAuthority.Replica?_replicaStaggerLocked:
            StageManager.CombatSuspended||currentHp<=0||IsBossDefeatedCollapse||IsGroggy||HpDamageBlocked||HardTransformationActive||
            !CharacterTrainingSession.Active&&(_raidwide.Active||_raidwide.DueWithin(Time.deltaTime,RaidwideReady,raidwide)||
            (GuardianNormal?GuardianStaggerLocked:FloraEncounter?FloraStaggerLocked:CampanellaEncounter?CampanellaStaggerLocked:
             MarionetteEncounter?MarionetteStaggerLocked:JustitiaEncounter?JustitiaStaggerLocked:JanusEncounter?JanusStaggerLocked:false));
        public bool TryApplyStaggerGroggy()
        {
            if(RaidCombatAuthority.Replica||StaggerLocked)return false;
            float seconds=CharacterDefinitionValidation.Finite(normalStaggerGroggySeconds)&&normalStaggerGroggySeconds>0?normalStaggerGroggySeconds:8;
            if(!CharacterTrainingSession.Active)
            {
                if(GuardianNormal)return TryGuardianStaggerGroggy(seconds);
                if(FloraEncounter)return TryFloraStaggerGroggy(seconds);
                if(CampanellaEncounter)return TryCampanellaStaggerGroggy(seconds);
                if(MarionetteEncounter)return TryMarionetteStaggerGroggy(seconds);
                if(JustitiaEncounter)return TryJustitiaStaggerGroggy(seconds);
                if(JanusEncounter)return TryJanusStaggerGroggy(seconds);
            }
            CleanupActiveTelegraphAndTether();_isChasing=false;_pendingPositionedSkill=null;
            _counterGroggy=true;fsmState=BossFSMState.Groggy;_groggyRemainingTime=seconds;
            return true;
        }
        private string StaggerHudText()
        {
            float value=GetComponent<CombatCounterTarget>()?.Stagger??0;
            int filled=Mathf.Clamp(Mathf.FloorToInt(value/StaggerThreshold*10),0,10);
            string bar=new string('■',filled)+new string('□',10-filled);
            return "<color="+(StaggerLocked?"#909090":"#C895FF")+">무력화 ["+bar+"] "+value.ToString("0")+"/"+StaggerThreshold.ToString("0")+
                (StaggerLocked?" · 누적 불가":"")+"</color>";
        }
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
