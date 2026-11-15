using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyDamageKit
    {
        public bool GroundAssaultActive=>Active&&Has("assault");
        public void Interrupt()
        {
            if(!Active||RaidCombatAuthority.Replica)return;Bind();_snipe.Interrupt();_meteor.Interrupt();
            if(GroundAssaultActive){_effects.Remove("assault@"+CharacterKit.Id(transform));GetComponent<PlayerController>()?.CancelSkillRush();}
        }
        public void BeforeInput(CharacterSlot slot)
        {
            if(!Active||RaidCombatAuthority.Replica||slot==CharacterSlot.Identity)return;
            Bind();_snipe.Interrupt();_meteor.Interrupt();
            if(slot==CharacterSlot.Job1||slot==CharacterSlot.Counter||slot==CharacterSlot.Movement)Interrupt();
        }
        private void Update()
        {
            if(!Active||RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!CombatRange.LivingPartyMember(transform))return;
            Bind();TickRoutes(Time.deltaTime);
        }
        private void TickRoutes(float delta)
        {
            if(Job.index==6&&Route==1)_weakness.Prune(id=>CombatRange.LivingEnemy(Resolve(id)));
            // Consume queued completions outside the boss FSM transition, before callbacks.
            var completedSeals=_pendingSeals.ToArray();_pendingSeals.Clear();
            foreach(var completed in completedSeals)ResolveSeal(completed);
            var assault=_effects.Find("assault@"+CharacterKit.Id(transform),Now);
            if(assault!=null&&Now>=assault.next&&GetComponent<PlayerController>()?.IsSkillRushing!=true)
            {
                _effects.Remove(assault.id); // pending manual attack is consumed before hits
                if(Vector3.Distance(transform.position,new Vector3(assault.x,assault.y,assault.z))<.5f)
                {
                    bool marked=false;
                    float power=SplitResidualCloud(Skill("r1"),new DamagePoint(assault.x,assault.z),assault.value);
                    foreach(var target in CharacterKit.Enemies().Where(t=>CombatRange.InRange(transform,t,4)))
                        if(Attack(Skill("r1"),target,power,0,CombatDamageKind.Direct,true)&&!marked)
                        {marked=true;if(!Has("assault.mark.gate")){Mark(target);Effect("assault.mark.gate","gate",transform,1,12);}}
                }
            }
            if(_seal.Tick(delta,out var seal))ResolveSeal(seal);
            if(_grenade.Tick(delta,out int grenadeTarget))DetonateGrenade(grenadeTarget);
            int loaded=_hands.Tick(delta);
            if(loaded>0)Effect("hand."+loaded,"hand.ready",transform,1,12);
            _snipe.Tick(delta);
            var meteorTarget=Resolve(_meteorTarget);
            if(_meteor.Tick(delta,Enemy(meteorTarget),out bool uninterruptedMeteor))
            {if(Attack(Skill("r1"),meteorTarget,(Core(1,0)?600:750)*(Core(1,1)&&uninterruptedMeteor?1.2f:1),0,CombatDamageKind.Direct,true))Mark(meteorTarget);_meteorTarget=0;}
            _burst.Tick(delta);_season.Tick(delta);_instant.Tick(delta);_memory.Tick(delta);_projection.Tick(delta);_prism.Tick(delta);_return.Tick(delta);
            _tempestPreparation.Tick(delta);
            var seedPoint=_seed.Snapshot.point;
            int seedTicks=_seed.Tick(delta,out bool seedExploded);
            for(int n=0;n<seedTicks;n++)SeedDamage(seedPoint,40);
            if(seedExploded)SeedDamage(seedPoint,(180+_seed.Snapshot.stored)*_tempestPreparation.TakeRelease());
            TickArrows(delta);
            foreach(var tick in _spears.Tick(delta))
                foreach(var enemy in CharacterKit.Enemies().Where(t=>Vector3.Distance(t.position,World(tick.Point))<=4))Attack(Skill("r3"),enemy,tick.Power,0,CombatDamageKind.Extra,field:tick.Point,fieldRadius:4);
            TickGravity(delta);
            TickScheduledDamage();
            TickResidualClouds();
            TickMemoryReplay();
            TickUltimate();
            foreach(var effect in _effects.Data.effects.ToArray())if(effect.end<=Now)_effects.Remove(effect.id);
        }
        private void TickGravity(float delta)
        {
            var field=_gravity.Snapshot;int ticks=_gravity.Tick(delta);
            float radius=field.expanded?(Core(3,0)?9:7):(Core(3,1)?2:3);
            float power=field.expanded?40:Core(3,1)?110:80;
            float stagger=field.expanded?0:Core(3,1)?20:15;
            for(int n=0;n<ticks;n++)foreach(var enemy in CharacterKit.Enemies())
                Attack(Skill("r3"),enemy,power,stagger,CombatDamageKind.Extra,field:field.point,fieldRadius:radius);
        }
        private void TickArrows(float delta)
        {
            var arrivals=_arrows.Tick(delta,(pair,straight)=>
            {
                var skill=Skill(straight?pair.straightSkill:pair.curveSkill);int step=straight?pair.straightStep:pair.curveStep;
                int chain=straight?1:2,choice=straight?0:Key(skill).Last()-'0';
                var target=Resolve(pair.target);bool hit=ArrowAttack(skill,target,step,chain,choice,pair.origin,pair.range,out var landed);
                // Each queued half is removed before this callback. It is a saved
                // manual input, not an automatic secondary resource producer.
                if(landed!=null)CompleteDirect(skill,landed,chain,step,choice);return hit;
            });
            foreach(var arrival in arrivals)if(arrival.Converged)Attack(Skill("r1"),Resolve(arrival.Pair.target),80,0,CombatDamageKind.Extra,launched:true);
        }
        private bool ArrowAttack(CharacterSkillDefinition skill,Transform target,int step,int chain,int choice,DamagePoint origin,float range,out Transform landed)
        {
            landed=null;if(!CombatRange.LivingEnemy(target))return false;
            float power=step==3?180:100,stagger=NineKeyDamageData.ComboStagger(Job.index,chain,choice);
            bool spread=chain==2&&choice==1,piercing=chain==2&&choice==0;if(spread)power*=.8f;
            var center=Point(target.position);
            var candidates=spread||piercing?new[]{target}.Concat(CharacterKit.Enemies()).Where(t=>t!=null).Distinct().ToArray():new[]{target};
            bool pairedTargetHit=false;
            foreach(var enemy in candidates)
            {
                if(enemy!=target&&!NineKeyDamageData.ComboAreaHit(piercing?1:3,piercing?origin:center,center,Point(enemy.position),piercing?range:3,CombatRange.Radius(enemy)))continue;
                if(!Attack(skill,enemy,power,stagger,CombatDamageKind.Direct,true,launched:true))continue;
                if(landed==null)landed=enemy;
                if(enemy==target)pairedTargetHit=true;
            }
            // Collateral can complete this one manual input, but convergence needs
            // both arrows to land on their originally paired enemy.
            return pairedTargetHit;
        }
    }
}
