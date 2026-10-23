using UnityEngine;
namespace RaidRogue
{
    // Patterns opt in explicitly. A normal cast is NOT implicitly counterable.
    public sealed class CombatCounterTarget : MonoBehaviour
    {
        public float Stagger {get;private set;}
        public float WindowEnd {get;private set;}
        public bool WindowOpen=>WindowEnd>RaidCombatAuthority.ServerTime&&CombatRange.LivingEnemy(transform);
        public static CombatCounterTarget Ensure(Transform target)=>target.GetComponent<CombatCounterTarget>()??target.gameObject.AddComponent<CombatCounterTarget>();
        public void OpenWindow(float seconds){if(!RaidCombatAuthority.Replica&&seconds>0)WindowEnd=RaidCombatAuthority.ServerTime+seconds;}
        public void ResetState(){WindowEnd=Stagger=0;}
        public void AddStagger(float points)
        {
            var boss=GetComponent<BossController>();
            if(RaidCombatAuthority.Replica||!CombatRange.LivingEnemy(transform)||boss!=null&&boss.IsGroggy)return;
            Stagger+=Mathf.Max(0,points);
            if(boss!=null&&Stagger>=AegisPaladinData.StaggerThreshold)
            {
                Stagger=0;WindowEnd=0;boss.ApplyCounterGroggy(AegisPaladinData.CounterGroggy);
                FloatingTextPopup.Spawn(transform.position+Vector3.up*3,"무력화!",Color.cyan,true);
            }
        }
        public bool CanCounter(Transform attacker,float range)
        {
            Vector3 toAttacker=attacker.position-transform.position;toAttacker.y=0;toAttacker.Normalize();
            return CounterRules.Valid(WindowOpen,CombatRange.LivingEnemy(transform),CombatRange.InRange(attacker,transform,range),Vector3.Dot(transform.forward,toAttacker),Vector3.Dot(attacker.forward,-toAttacker),AegisPaladinData.CounterFrontAngle);
        }
        public bool TryCounter(Transform attacker,float range,float hitStagger=0)
        {
            if(RaidCombatAuthority.Replica||!CanCounter(attacker,range))return false;
            WindowEnd=0;AddStagger(hitStagger+AegisPaladinData.CounterBonusStagger);
            GetComponent<BossController>()?.ApplyCounterGroggy(AegisPaladinData.CounterGroggy);
            FloatingTextPopup.Spawn(transform.position+Vector3.up*3,"COUNTER!",Color.cyan,true);return true;
        }
        public void Restore(float stagger,float window){if(RaidCombatAuthority.Replica){Stagger=stagger;WindowEnd=window;}}
    }
    public partial class BossController
    {
        private bool _counterGroggy;
        public void ApplyCounterGroggy(float seconds)
        {
            if(RaidCombatAuthority.Replica||IsGroggy||IsBossDefeatedCollapse||StageManager.CombatSuspended)return;
            CleanupActiveTelegraphAndTether();_isChasing=false;_pendingPositionedSkill=null;
            _counterGroggy=true;fsmState=BossFSMState.Groggy;_groggyRemainingTime=seconds;
            // Keep currentPhase: counter interruption must never advance the HP phase.
        }
    }
}
