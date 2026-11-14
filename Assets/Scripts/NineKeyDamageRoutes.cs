using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyDamageKit
    {
        private bool Spend(float cost)=>_runtime.Spend(Job.Resource,cost);
        private bool Enemy(Transform target)=>CombatRange.LivingEnemy(target)&&CombatRange.InRange(transform,target,25);
        private bool Identity(CharacterSkillDefinition skill,Transform enemy)
        {
            var patient=_actor.ActionSupportTarget;
            if(Job.index==6)
            {
                if(!Enemy(enemy))return false;
                if(Route==1)
                {
                    if(!Spend(2))return false;bool informed=_weakness.Consume(CharacterKit.Id(enemy));
                    if(Attack(skill,enemy,informed?(Core(1,0)?500:Core(1,1)?350:400):250)&&informed)Mark(enemy);return true;
                }
                if(Route==2)
                {
                    if(!_resources.FinishDance(out int beats))return false;
                    float finisher=beats==3?450:beats==2?250:120;
                    if(Core(2,0))finisher*=1.25f;
                    else if(Core(2,1))finisher*=beats==3?1.3f:.8f;
                    Attack(skill,enemy,finisher,beats==2?100:0);
                    if(beats==1)Effect("dance.reach","reach",transform,8,6);
                    if(!Has("dance.speed.gate")){Speed();Effect("dance.speed.gate","gate",transform,1,8);}return true;
                }
                if(_seal.Active||!Spend(2))return false;
                _seal.Attach(CharacterKit.Id(enemy),_actor.ActionRecall?DamageSealTrigger.PatternMoveEnd:DamageSealTrigger.CastEnd,Core(3,1)?14:8);
                if(Core(3,0))Attack(skill,enemy,80);return true;
            }
            if(Job.index==7)
            {
                if(Route==1)
                {
                    var player=GetComponent<PlayerController>();
                    if(player==null||!_actor.ActionGroundPoint.HasValue||GroundAssaultActive||!player.PlanSkillRushToPoint(_actor.ActionGroundPoint.Value,10,out var end)||!Spend(Core(1,0)?30:50))return false;
                    Effect("assault","assault",transform,(Core(1,0)?240:400)*_tempestPreparation.TakeRelease(),1);
                    var pending=_effects.Find("assault@"+CharacterKit.Id(transform),Now);
                    pending.x=end.x;pending.y=end.y;pending.z=end.z;pending.next=Now+.2f;
                    player.BeginSkillRush(end,.2f);return true;
                }
                if(Route==2){if(Has("acceleration")||!Spend(80))return false;Effect("acceleration","acceleration",transform,1,12);return true;}
                if(_actor.ActionRecall)
                {
                    var recalled=_spears.Recall();if(recalled.Length==0)return false;
                    float releaseScale=_tempestPreparation.TakeRelease();
                    foreach(var spear in recalled)
                    {
                        _runtime.Gain(Job.Resource,spear.charge*5);
                        foreach(var target in CharacterKit.Enemies().Where(t=>Vector3.Distance(t.position,World(spear.point))<=5))Attack(skill,target,(150+spear.charge*50+spear.stored)*releaseScale,0,CombatDamageKind.Extra,field:spear.point,fieldRadius:5);
                    }
                    if(!Has("recall.speed.gate")){Speed();Effect("recall.speed.gate","gate",transform,1,12);}return true;
                }
                if(!_actor.ActionGroundPoint.HasValue||!_spears.CanPlace||!Spend(40))return false;
                return _spears.Place(Point(_actor.ActionGroundPoint.Value),InstallationDuration(Core(3,0)?14:Core(3,1)?26:20),40,Core(3,1),400);
            }
            if(Job.index==8)
            {
                if(Route==1)return _hands.BeginReload(_actor.ActionRecall,Core(1,0)?1:Core(1,1)?3:2,Core(1,0)?3:6,Core(1,0)?2:_hands.Snapshot.capacity);
                if(Route==2)
                {
                    if(!Enemy(enemy)||_grenade.Active||!Spend(3))return false;
                    return _grenade.Attach(CharacterKit.Id(enemy),Core(2,0)?4:Core(2,1)?8:6,Core(2,1)?12:10,Core(2,0)?1:Core(2,1)?3:2);
                }
                if(_snipe.Ready)
                {
                    var held=Resolve(_snipe.Snapshot.target);
                    bool valid=Enemy(held)&&!(held.GetComponent<BossController>()?.HpDamageBlocked??false);
                    if(!_snipe.Launch(valid,out _))return false;
                    bool hit=Attack(skill,held,Core(3,0)?200:Core(3,1)?750:500);
                    if(_snipe.ResolveShot(hit))Mark(held);return true;
                }
                return Enemy(enemy)&&_snipe.Begin(CharacterKit.Id(enemy),Core(3,1)?5:3,Core(3,1)?2:1,12,Core(3,0)?3:1);
            }
            if(Job.index==9)
            {
                if(Route==1)return RangerConvergence(skill,enemy);
                if(Route==2)return RangerBurst(skill,enemy);
                if(_actor.ActionRecall)
                {if(_season.Snapshot.delayed||!Spend(20))return false;return _season.Delay(Core(3,1)?6:Core(3,0)?2:4);}
                if(!Enemy(enemy)||!Spend(Core(3,1)?50:40))return false;
                int season=_season.Advance();bool landed=Attack(skill,enemy,300,season==2?100:0);
                if(season==0){Shield(transform,"spring",Power*.04f);if(patient!=transform)Shield(patient,"spring",Power*.04f);}
                if(season==1&&landed)Mark(enemy);if(season==3)Speed();return true;
            }
            if(Job.index==10)
            {
                if(Route==1)
                {if(!_meteor.BeginOrResume(Enemy(enemy),60,Core(1,0)?5:3,Core(1,0)?.6f:1,16))return false;_meteorTarget=CharacterKit.Id(enemy);return true;}
                if(Route==2)
                {
                    if(_instant.Active)return _instant.Toggle(.25f);
                    if(_instant.Snapshot.cooldown>0||!Spend(Core(2,0)?40:60))return false;
                    return _instant.Begin(Core(2,0)?2:Core(2,1)?5:3,20,Core(2,0)?18:Core(2,1)?40:30);
                }
                if(_gravity.Active)
                {if(!_gravity.Toggle(out bool protect))return false;if(protect){Shield(transform,"gravity",Power*.04f);if(patient!=transform)Shield(patient,"gravity",Power*.04f);}return true;}
                if(!_actor.ActionGroundPoint.HasValue||!Spend(60))return false;
                return _gravity.Begin(Point(_actor.ActionGroundPoint.Value),Core(3,0)?16:12);
            }
            if(Job.index==11)
            {
                if(Route==1)
                {
                    if(_actor.ActionRecall){_memory.ToggleLock();return true;}
                    int count=Core(1,0)?1:2;if(!Enemy(enemy)||!CombatRange.InRange(transform,enemy,skill.range)||!_memory.CanReplay(count)||!Spend(count))return false;
                    _memory.Take(count,out var records);
                    if(Core(1,1)){QueueMemoryReplay(records,enemy);return true;}
                    Transform lastHit=null;
                    foreach(string key in records)lastHit=ReplayAttack(key,enemy,Core(1,0)?320:220);
                    if(lastHit!=null)Mark(lastHit);return true;
                }
                if(Route==2)
                {
                    if(!_actor.ActionRecall){if(_projection.Active||!Spend(1))return false;return _projection.Begin(Core(2,0)?16:10);}
                    if(_projection.Active||!Enemy(enemy)||!Spend(3))return false;
                    _projection.Execute(out bool empowered);if(Attack(skill,enemy,empowered&&Core(2,1)?650:500))ReaperProtection();return true;
                }
                return UsePrism(_actor.ActionPrism??(_actor.ActionGroundPoint.HasValue?
                    new DamagePrismCommand{point=Point(_actor.ActionGroundPoint.Value),normal=Point(transform.forward)}:null));
            }
            return false;
        }
        private void QueueMemoryReplay(string[] records,Transform target)
        {
            string set=Guid.NewGuid().ToString("N");
            for(int n=0;n<records.Length;n++)
            {
                float at=Now+.3f*(n+1);
                _effects.Put(new CharacterTimedEffect{id="memory.replay:"+set+":"+n,kind="memory.replay",skill=records[n],
                    target=CharacterKit.Id(target),next=at,end=at,interval=1,value=n==records.Length-1?280:220,aux=n==records.Length-1?1:0});
            }
        }
        private Transform ReplayAttack(string key,Transform target,float power)
        {
            if(!CombatRange.LivingEnemy(target))return null;
            var skill=Skill(key);float stagger=key=="g22"?40:0;
            if(key!="g21")return Attack(skill,target,power,stagger,CombatDamageKind.Extra,launched:true)?target:null;
            // Replay the selected technique's footprint using current replay
            // coefficients, never the original hit's crit/buff-adjusted damage.
            var center=Point(target.position);Transform first=null;
            var candidates=new[]{target}.Concat(CharacterKit.Enemies()).Where(t=>t!=null).Distinct().ToArray();
            foreach(var enemy in candidates)
                if(NineKeyDamageData.ComboAreaHit(3,center,center,Point(enemy.position),3,CombatRange.Radius(enemy))&&
                    Attack(skill,enemy,power*.8f,0,CombatDamageKind.Extra,launched:true)&&first==null)first=enemy;
            return first;
        }
        private void TickMemoryReplay()
        {
            foreach(var e in _effects.Data.effects.Where(x=>x.kind=="memory.replay").OrderBy(x=>x.next).ToArray())
            {
                if(CharacterEffectState.TakeTicks(e,Now)==0)continue;
                _effects.Remove(e.id); // Consume before damage and cooperation callbacks.
                var target=Resolve(e.target);
                var landed=ReplayAttack(e.skill,target,e.value);
                if(landed!=null&&e.aux==1)Mark(landed);
            }
        }
        private void DetonateGrenade(int targetId)
        {
            var target=Resolve(targetId);
            if(Attack(Skill("r2"),target,400,0,CombatDamageKind.Extra,launched:true))Effect("fracture","fracture",target,100,6);
        }
        private bool RangerBurst(CharacterSkillDefinition skill,Transform enemy)
        {
            if(!Enemy(enemy))return false;
            if(_burst.Active)
            {
                if(!_burst.Fire(.25f,out bool strong,out bool final))return false;
                // Prototype tradeoff: front-load the set or invest in combo-linked followups.
                float power=Core(2,0)?(strong?240:150):Core(2,1)?(strong?360:180):(strong?300:180);
                Attack(skill,enemy,power);if(final)Speed();return true;
            }
            if(!Spend(50))return false;
            _burst.Begin(Core(2,1)?20:16,.25f);
            Attack(skill,enemy,Core(2,0)?300:180);return true;
        }
        private bool RangerConvergence(CharacterSkillDefinition skill,Transform enemy)
        {
            bool frequent=Core(1,1);
            if(!Enemy(enemy)||!Spend(frequent?30:40))return false;
            int convergence=_arrows.ConsumeConvergence(frequent?1:3);
            float power=(frequent?180:250)+convergence*(Core(1,0)?50:80);
            var origin=Point(transform.position);var aim=Point(enemy.position);
            foreach(var target in new[]{enemy}.Concat(CharacterKit.Enemies()).Where(t=>t!=null).Distinct())
            {
                if(target!=enemy&&!NineKeyDamageData.ComboAreaHit(1,origin,aim,Point(target.position),EffectiveRange(skill),CombatRange.Radius(target)))continue;
                // One paid shot pierces the line; its primary target owns the
                // single cooperation mark, regardless of collateral hit count.
                if(Attack(skill,target,power)&&target==enemy&&convergence>0)Mark(enemy);
            }
            return true;
        }
        private void ResolveSeal(DamageSealResolution result)
        {
            var target=Resolve(result.Target);
            float baseDamage=Core(3,0)?160:240;
            if(!Attack(Skill("r3"),target,baseDamage+(result.Enhanced?(Core(3,1)?240:160):0),0,CombatDamageKind.Extra,launched:true)||!result.Enhanced)return;
            if(result.Trigger==DamageSealTrigger.PatternMoveEnd)Speed();else foreach(var ally in Party())Shield(ally,"seal",Power*.03f);
        }
        public void BossActionEnded(int target,DamageSealTrigger trigger)
        {if(!Active||RaidCombatAuthority.Replica)return;Bind();if(_seal.OnActionEnded(target,trigger,out var resolved))_pendingSeals.Add(resolved);}
        public static void NotifyBossAction(Transform target,DamageSealTrigger trigger)
        {
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!CombatRange.LivingEnemy(target))return;
            foreach(var member in PartyLife.OrderedMembers())
                if(CombatRange.LivingPartyMember(member))member.GetComponent<NineKeyDamageKit>()?.BossActionEnded(CharacterKit.Id(target),trigger);
        }
    }
}
