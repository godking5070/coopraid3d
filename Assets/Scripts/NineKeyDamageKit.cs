using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    // Host-owned nine-key adapter registered by CharacterDefinitions. Old kits
    // remain excluded for this layout; runtime play validation is tracked separately.
    [DefaultExecutionOrder(450)]
    public sealed partial class NineKeyDamageKit : MonoBehaviour
    {
        private CharacterActor _actor;
        private CharacterRuntimeState _runtime;
        private NineKeyDamageResources _resources;
        private readonly CharacterEffectState _effects=new CharacterEffectState();
        private DamageWeaknessState _weakness;private DamageSealState _seal;private DamageGrenadeState _grenade;
        private DamageHandsState _hands;private DamageSnipeState _snipe;private DamageMeteorState _meteor;
        private DamageBurstState _burst;private DamageSeasonState _season;private DamageInstantState _instant;
        private DamageMemoryState _memory;private DamageProjectionState _projection;private DamageArrowState _arrows;
        private DamageReturnState _return;
        private DamageSeedState _seed;
        private DamageTempestPreparationState _tempestPreparation;
        private bool _fourthReady;
        private DamageSpearState _spears;private DamageGravityState _gravity;private DamagePrismState _prism;
        private int _meteorTarget;private string _deferredOrigin="";
        private readonly System.Collections.Generic.List<DamageSealResolution> _pendingSeals=new System.Collections.Generic.List<DamageSealResolution>();
        public static bool Supports(CharacterDefinition d)=>CharacterSlots.IsNineKey(d)&&NineKeyDamageData.Supports(CharacterRosterData.Find(d?.id));
        public bool Active=>Supports(_actor?.Definition);
        public RosterJob Job=>CharacterRosterData.Find(_actor?.Definition?.id);
        private int Route=>!Active?0:Array.FindIndex(_actor.Definition.routes,r=>r.id==_actor.RouteId)+1;
        private float Now=>RaidCombatAuthority.ServerTime;
        private float Power=>GetComponent<PlayerController>()?.maxHp??1;
        private bool Core(int route,int variant)=>_actor.RuntimeState.CoreTrait==NineKeyDamageData.Core(Job,route,variant);
        private bool SeedCore(int variant)=>Job.index==7&&_actor.RuntimeState.CoreTrait==NineKeyDamageData.Id(Job,"core3"+variant+".f2");
        private bool ThirdAssaultCore=>Job.index==7&&_actor.RuntimeState.CoreTrait==NineKeyDamageData.Id(Job,"core10.a0");
        public static NineKeyDamageKit Ensure(GameObject go)=>go.GetComponent<NineKeyDamageKit>()??go.AddComponent<NineKeyDamageKit>();
        private void Awake(){_actor=GetComponent<CharacterActor>();_actor.RegisterHandler(NineKeyDamageData.Handler,Execute);}
        private void Bind()
        {
            if(!Active||_actor.RuntimeState==null||_runtime==_actor.RuntimeState)return;
            RemoveOwnedProtection();_runtime=_actor.RuntimeState;_resources=new NineKeyDamageResources(_runtime,Job);ResetStates();
        }
        private void ResetStates()
        {
            _fourthReady=false;ResetInput();
            _effects.Clear();_pendingSeals.Clear();_weakness=new DamageWeaknessState();_seal=new DamageSealState();_grenade=new DamageGrenadeState();
            _hands=new DamageHandsState();_hands.Configure(Job.index==8&&Core(1,1)?5:3);
            _snipe=new DamageSnipeState(_resources);_meteor=new DamageMeteorState(_resources);_meteorTarget=0;
            _burst=new DamageBurstState();_season=new DamageSeasonState();_season.Reset(Job.index==9&&Core(3,0)?6:8);
            _instant=new DamageInstantState();_memory=new DamageMemoryState();_projection=new DamageProjectionState();_return=new DamageReturnState();
            _arrows=new DamageArrowState();_spears=new DamageSpearState();_seed=new DamageSeedState();_gravity=new DamageGravityState();_prism=new DamagePrismState();_deferredOrigin="";
            _tempestPreparation=new DamageTempestPreparationState();
        }
        private void RemoveOwnedProtection()
        {
            if(_runtime==null||RaidCombatAuthority.Replica)return;
            int source=CombatMetricsManager.SourceId(transform);
            foreach(var ally in PartyLife.OrderedMembers())
            {ally.GetComponent<PlayerController>()?.RemoveShieldsFrom(source);ally.GetComponent<PartyBotAI>()?.RemoveShieldsFrom(source);}
        }
        public void Clear()
        {
            ResetInput();HidePrismPresentation();
            RemoveOwnedProtection();_effects.Clear();_pendingSeals.Clear();_deferredOrigin="";
            if(!Active){_runtime=null;_resources=null;return;}
            _runtime=_actor.RuntimeState;if(_runtime==null)return;
            _resources=new NineKeyDamageResources(_runtime,Job);ResetStates();
        }
        public bool OwnsDeferredDirectHit(string origin)=>!RaidCombatAuthority.Replica&&Active&&_deferredOrigin.Length>0&&origin==_deferredOrigin;
        private CharacterSkillDefinition Skill(string key)=>_actor.Definition.Skill(NineKeyDamageData.Id(Job,key));
        private string Key(CharacterSkillDefinition skill)=>CharacterRosterData.Key(skill.id);
        private Transform Resolve(int id)=>CharacterKit.Resolve(id);
        private DamagePoint Point(Vector3 v)=>new DamagePoint(v.x,v.z);
        private Vector3 World(DamagePoint p)=>new Vector3(p.x,transform.position.y,p.z);
        private Transform[] Party()=>PartyLife.OrderedMembers().Where(t=>CombatRange.LivingPartyMember(t)&&CombatRange.InRange(transform,t,30)).ToArray();
        private bool ValidAlly(Transform t)=>CombatRange.LivingPartyMember(t)&&CombatRange.InRange(transform,t,25);
        private void Shield(Transform t,string key,float amount,float duration=6){if(ValidAlly(t))CombatStatusEffects.Shield(t,transform,Job.Id+".nine."+key,amount,duration,true);}
        private void Effect(string key,string kind,Transform target,float value,float duration)
        {
            if(target==null)return;int id=CharacterKit.Id(target);
            _effects.Put(new CharacterTimedEffect{id=key+"@"+id,skill=key,kind=kind,target=id,value=value,end=Now+duration,next=Now+1,interval=1});
        }
        private bool Has(string key)=>_effects.Find(key+"@"+CharacterKit.Id(transform),Now)!=null;
        private void Speed(float duration=3){foreach(var ally in Party())Effect("speed","speed",ally,.15f,duration);}
        private void Mark(Transform target,float duration=6)
        {
            if(!CombatRange.LivingEnemy(target))return;
            duration=TempestMarkDuration(duration);
            var allies=Party().Where(t=>t!=transform).ToArray();int budget=PartyLife.OrderedMembers().Count>4?160:120;
            for(int i=0;i<allies.Length;i++)
            {
                var ally=allies[i];
                // Attack rounds individual hits to integer HP. Distribute the
                // remainder once so a seven-companion mark cannot round up
                // seven times and exceed its party-wide budget.
                int share=budget/allies.Length+(i<budget%allies.Length?1:0);
                Effect("coop","cooperation",ally,share,duration);
                _effects.Find("coop@"+CharacterKit.Id(ally),Now).skill="coop:"+CharacterKit.Id(target);
            }
        }
        private bool Attack(CharacterSkillDefinition skill,Transform target,float damage,float stagger=0,CombatDamageKind kind=CombatDamageKind.Direct,bool deferred=false,DamagePoint? field=null,float fieldRadius=0,bool launched=false)
        {
            if(skill==null||!CombatRange.LivingEnemy(target))return false;
            if(field.HasValue)
            {if(!field.Value.Valid||fieldRadius<=0||Vector3.Distance(target.position,World(field.Value))>fieldRadius)return false;}
            else if(!launched&&!CombatRange.InRange(transform,target,EffectiveRange(skill)))return false;
            if(kind==CombatDamageKind.Direct&&skill.comboChain==2&&_actor.HasAuxiliary(NineKeyDamageData.Reach))damage*=.95f;
            var boss=target.GetComponent<BossController>();var add=target.GetComponent<BossAddMinion>();
            if(boss!=null&&boss.HpDamageBlocked||boss==null&&add==null)return false;
            float before=boss!=null?boss.currentHp:add.currentHp;
            string previous=_deferredOrigin;if(deferred&&kind==CombatDamageKind.Direct)_deferredOrigin=skill.id;
            try
            {
                if(boss!=null)boss.ReceiveRoleSkillAttack(Job.name,skill.name,0,Mathf.RoundToInt(damage),Color.cyan,false,source:transform,origin:skill.id,kind:kind);
                else add.ReceivePlayerAttack(Job.name,skill.name,Mathf.RoundToInt(damage),Color.cyan,false,source:transform,origin:skill.id,kind:kind);
            }
            finally{_deferredOrigin=previous;}
            bool hit=(boss!=null?boss.currentHp:add.currentHp)<before||AiSpectatorDamage.ZeroHpDamageMode(transform);
            if(hit&&stagger>0)CombatCounterTarget.Ensure(target).AddStagger(stagger);return hit;
        }
        private bool Execute(CharacterSkillDefinition skill,Transform target)
        {
            if(!Active||RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!CombatRange.LivingPartyMember(transform))return false;
            Bind();string key=Key(skill);
            if(Job.index==11&&key=="f2")return ReturnToAnchor();
            if(Job.index==7&&key=="f2")return SeedInput();
            if(skill.comboChain>0)return Combo(skill,target);
            if(key=="q")return PersonalDefense();
            if(key=="v")return UseUltimate(skill,target);
            if(key.StartsWith("r",StringComparison.Ordinal))
            {
                if(UsesGround&&!_actor.ActionRecall&&(!_actor.ActionGroundPoint.HasValue||!CanPlace(_actor.ActionGroundPoint.Value)))return false;
                float before=_runtime.Resource(Job.Resource);bool used=Identity(skill,target);
                ProtectAfterRoot(used,before);return used;
            }
            if(key=="c2"||skill.target==SkillTargetRule.LivingAlly)
            {
                if(!ValidAlly(target))return false;
                float duration=key.StartsWith("f",StringComparison.Ordinal)?TacticalDuration(6):RoleProtectionDuration(key);
                Shield(target,key,Power*.06f,duration);
                if((key=="c2"||Job.index==9&&key=="f2")&&target!=transform)Shield(transform,key,Power*.06f,duration);
                return true;
            }
            if(key=="c0")return RoleCounter(skill,target);
            if(key=="c1")return RoleStagger(skill,target);
            if(key=="f0")return Job.index==8&&Route==1?_hands.Supply():_resources.Supply();
            if(key=="f1"&&(Job.index==6||Job.index==7))
            {
                if(!Enemy(target))return false;
                Effect("duet","duet",target,160,TempestMarkDuration(TacticalDuration(6)));return true;
            }
            if(key.StartsWith("f",StringComparison.Ordinal))
            {if(skill.target==SkillTargetRule.LivingEnemy){Mark(target,TacticalDuration(6));return CombatRange.LivingEnemy(target);}Speed(TacticalDuration(3));return true;}
            if(key.StartsWith("a",StringComparison.Ordinal))
            {
                if(Job.index==7)return TempestThird(skill,target,key);
                if(Job.index==8&&key=="a2")
                {if(!Enemy(target))return false;ScheduleDamage(skill,target,210,1.5f,1,false);return true;}
                Attack(skill,target,150,key=="a1"&&Job.index!=6?80:0);
                if(Job.index==9&&key=="a2"&&Enemy(target))ScheduleDamage(skill,target,110,1,1,false);
                return true;
            }
            return false;
        }
        private float RoleProtectionDuration(string key)
        {return key=="c2"&&_actor.HasAuxiliary(NineKeyDamageData.Response)?9:6;}
        private bool RoleCounter(CharacterSkillDefinition skill,Transform target)
        {
            if(!Attack(skill,target,100))return false;
            // Prototype recovery limits offensive followups only. Emergency defense,
            // role response and movement remain available through Unavailable.
            Effect("counter.recovery","gate",transform,1,_actor.HasAuxiliary(NineKeyDamageData.Response)?.15f:.3f);
            var counter=CombatCounterTarget.Ensure(target);
            if(!counter.TryCounter(transform,skill.range,40))counter.AddStagger(40);
            return true;
        }
        private bool RoleStagger(CharacterSkillDefinition skill,Transform target)
        {return Attack(skill,target,100,_actor.HasAuxiliary(NineKeyDamageData.Response)?125:100);}
        private bool UseUltimate(CharacterSkillDefinition skill,Transform target)
        {
            if(Job.index!=6&&Job.index!=11)return Attack(skill,target,800);
            if(!Attack(skill,target,Job.index==6?120:200))return false;
            if(Job.index==6)
            {
                UltimateStrike(target,120,.15f);UltimateStrike(target,120,.3f);UltimateStrike(target,440,.45f);
            }
            else {UltimateStrike(target,300,.2f);UltimateStrike(target,300,.4f);}
            return true;
        }
        private void UltimateStrike(Transform target,float power,float delay)
        {
            float at=Now+delay;
            _effects.Put(new CharacterTimedEffect{id="ultimate.sequence:"+Guid.NewGuid().ToString("N"),kind="ultimate.sequence",
                skill="v",target=CharacterKit.Id(target),value=power,next=at,end=at,interval=1});
        }
        private void TickUltimate()
        {
            foreach(var effect in _effects.Data.effects.Where(e=>e.kind=="ultimate.sequence").OrderBy(e=>e.next).ToArray())
            {
                if(CharacterEffectState.TakeTicks(effect,Now)==0)continue;
                _effects.Remove(effect.id);
                Attack(Skill("v"),Resolve(effect.target),effect.value,0,CombatDamageKind.Extra,launched:true);
            }
        }
        private bool Combo(CharacterSkillDefinition skill,Transform target)
        {
            int chain=skill.comboChain,step=chain==1?_actor.Snapshot.combo1:_actor.Snapshot.combo2;
            int choice=chain==2?Key(skill).Last()-'0':0;
            if(IsFourth(skill))return FourthStrike(skill,target);
            if(Job.index==10&&Route==2&&chain==2&&!_runtime.CompletingCast&&_instant.Consume(true))
            {
                if(!Has("instant.speed.gate"))
                {Effect("instant.speed","speed",transform,.15f,3);if(ValidAlly(_actor.ActionSupportTarget))Effect("instant.speed","speed",_actor.ActionSupportTarget,.15f,3);Effect("instant.speed.gate","gate",transform,1,6);}
            }
            if(Job.index==8&&Route==1&&!_hands.Fire(chain))return false;
            UpdateFourthOpportunity(skill,step);
            _resources.RecordComboUse(chain);
            if(Job.index==9&&Route==1)
            {
                if(chain==2)return _arrows.LaunchCurve(CharacterKit.Id(target),step,Key(skill),4,Point(transform.position),EffectiveRange(skill));
                if(_arrows.ReserveStraight(CharacterKit.Id(target),step,Key(skill),Core(1,0)?3:2.5f))return true;
            }
            if(Job.index==11&&Route==2&&chain==2&&_projection.LaunchSecond())_resources.MarkProjectedSecond();
            Transform landed=ComboAttack(skill,target,step,choice);
            if(Has("dance.reach"))_effects.Remove("dance.reach@"+CharacterKit.Id(transform));
            if(Has("tempest.reach"))_effects.Remove("tempest.reach@"+CharacterKit.Id(transform));
            bool claimed=landed!=null&&_actor.ClaimDamageDirect(skill);
            if(claimed)CompleteDirect(skill,landed,chain,step,choice);
            if(Job.index==11&&Route==2&&chain==2&&step==3)FinishReaperCombo(claimed);
            return true;
        }
        private Transform ComboAttack(CharacterSkillDefinition skill,Transform target,int step,int choice)
        {
            float power=step==3?180:100,stagger=NineKeyDamageData.ComboStagger(Job.index,skill.comboChain,choice);
            // The ranged dagger option trades direct efficiency for reach. This
            // is a skill coefficient, not a proximity penalty or resource rule.
            if(Job.index==6&&skill.comboChain==2&&choice==1)power*=.8f;
            int shape=NineKeyDamageData.ComboArea(Job.index,skill.comboChain,choice);
            if(shape==0)return Attack(skill,target,power,stagger)?target:null;
            if(!Enemy(target))return null;
            DamagePoint origin=Point(transform.position),aim=Point(target.position);
            if(Job.index==11&&Route==2&&!_projection.Active)aim=origin;
            float range=EffectiveRange(skill);if(shape==2||shape==3)power*=.8f;
            Transform first=null;
            // Snapshot candidates before hit callbacks; a manual input owns one
            // resource/completion claim regardless of how many enemies it strikes.
            var candidates=new[]{target}.Concat(CharacterKit.Enemies()).Where(t=>t!=null).Distinct().ToArray();
            foreach(var enemy in candidates)
                if(ComboAreaContains(shape,origin,aim,enemy,range)&&Attack(skill,enemy,power,stagger)&&first==null)first=enemy;
            return first;
        }
        private bool ComboAreaContains(int shape,DamagePoint origin,DamagePoint aim,Transform enemy,float range)
        {
            return NineKeyDamageData.ComboAreaHit(shape,origin,aim,Point(enemy.position),range,CombatRange.Radius(enemy));
        }
        public bool IsFourth(CharacterSkillDefinition skill)
        {
            if(!Active||skill==null)return false;Bind();
            return Job.index==7&&Core(2,0)&&skill.comboChain==2&&_fourthReady;
        }
        private void UpdateFourthOpportunity(CharacterSkillDefinition skill,int step)
        {
            if(Job.index==7&&Core(2,0)&&skill.usesGcd)_fourthReady=skill.comboChain==2&&step==3;
        }
        private bool FourthStrike(CharacterSkillDefinition skill,Transform target)
        {
            if(!IsFourth(skill)||!Enemy(target)||!CombatRange.InRange(transform,target,EffectiveRange(skill))||!Spend(20))return false;
            _fourthReady=false; // Consume before hit callbacks; no completion/resource/preparation path.
            Attack(skill,target,240);
            return true;
        }
        private float TacticalDuration(float duration)
        {return duration*(_actor.HasAuxiliary(NineKeyDamageData.Support)?1.5f:1);}
        private bool PersonalDefense()
        {
            Effect("q","reduction",transform,.35f,_actor.HasAuxiliary(NineKeyDamageData.Guard)?6:4);
            if(_actor.HasAuxiliary(NineKeyDamageData.Recovery))Effect("recovery.breath","healingReceived",transform,.15f,6);
            return true;
        }
        private void CompleteDirect(CharacterSkillDefinition skill,Transform target,int chain,int step,int choice)
        {
            int previous=_resources.Snapshot.lastCompletedChain;
            _resources.DirectCombo(chain,step,choice,Has("acceleration"));
            if(chain==2&&step==3)
            {
                if(Job.index==6&&choice==0||Job.index==7&&choice==1||Job.index==8&&choice==2||Job.index==10&&choice==2)
                    ScheduleDamage(skill,target,30,1,4,true);
                if(Job.index==9&&choice==2)ScheduleDamage(skill,target,100,1,1,false);
            }
            if(Job.index==8&&Route==1)
            {
                var ready=_effects.Find("hand."+chain+"@"+CharacterKit.Id(transform),Now);
                if(ready!=null)
                {
                    _effects.Remove(ready.id);
                    if(!Has("hand.mark.gate")){Mark(target);Effect("hand.mark.gate","gate",transform,1,12);}
                }
            }
            if(Job.index==6&&Route==1&&step==3)
            {
                var boss=target.GetComponent<BossController>();bool favorable=boss==null||boss.GetPositionalZone(transform.position,out _)!=PositionalZone.Front;
                _weakness.Grant(CharacterKit.Id(target),favorable,boss!=null,Core(1,0)?1:Core(1,1)?3:2);
            }
            if(Job.index==7)
            {
                if(chain==2&&step==3)_tempestPreparation.Grant(choice,12);
                _spears.Charge(chain,step,true);
                if(step==3&&chain==1&&Core(3,0))_spears.Move(Point(target.position));
                if(step==3&&chain==1&&SeedCore(0))_seed.Move(Point(target.position));
                if(Route==2&&step==3&&previous!=0&&previous!=chain&&Has("acceleration"))Attack(skill,target,100,0,CombatDamageKind.Extra);
                if(Route==2&&step==3&&previous!=0&&previous!=chain)SignalTempestResponse(_actor.ActionSupportTarget);
            }
            if(Job.index==8&&Route==2&&step==3&&_grenade.CompleteCombo(chain,out int detonated)&&detonated!=0)DetonateGrenade(detonated);
            if(Job.index==9&&Route==2&&step==3)_burst.CompleteCombo();
            if(Job.index==10&&Route==3&&chain==2&&step==3)_gravity.Follow(Point(target.position),true);
            if(Job.index==11)
            {
                if(Route==1&&step==3)_memory.Record(Key(skill),true,Core(1,0)?1:2,12);
                if(Route==3&&chain==2&&step==3)PrismFinisher(skill,target);
            }
        }
        private void PrismFinisher(CharacterSkillDefinition skill,Transform target)
        {
            int reflections=DamagePrismState.ReflectionsToHit(PrismPath(),Point(target.position),CombatRange.Radius(target));
            if(reflections==0)return;
            bool multiple=Core(3,1)&&reflections==2;
            Attack(skill,target,multiple?220:160,multiple?80:60,CombatDamageKind.Extra);
        }
        private void FinishReaperCombo(bool validFinalHit)
        {
            bool projected=_projection.Snapshot.usedInCombo;
            _projection.CompleteSecond(validFinalHit);
            _resources.EndSecondCombo();
            if(projected&&validFinalHit)ReaperProtection();
        }
        private bool ReturnToAnchor()
        {
            var player=GetComponent<PlayerController>();
            if(player==null||!player.CanMoveVoluntarily||player.IsDashing)return false;
            if(!_return.Active)
                return CanPlace(transform.position)&&_return.Store(Point(transform.position),8,16);
            Vector3 destination=World(_return.Snapshot.point);
            if(!CanPlace(destination)||!player.PlanSkillRushToPoint(destination,25,out var end)||(end-destination).sqrMagnitude>.05f)return false;
            if(!_return.Consume(true))return false;
            Vector3 before=transform.position;transform.position=end;
            if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.CommitSkillRelocation(transform);
            player.targetBoss?.TryInterceptPlayerMovement(player,before,end);
            return true;
        }
        private bool SeedInput()
        {
            if(_seed.Active)
            {
                float stored=_seed.Snapshot.stored;
                if(!_seed.Detonate(out var point))return false;
                SeedDamage(point,(180+stored)*_tempestPreparation.TakeRelease());return true;
            }
            return CanPlace(transform.position)&&_seed.Place(Point(transform.position),InstallationDuration(SeedCore(0)?4:SeedCore(1)?8:6),16,SeedCore(1),240);
        }
        private bool TempestThird(CharacterSkillDefinition skill,Transform target,string key)
        {
            if(!Enemy(target)||!CombatRange.InRange(transform,target,EffectiveRange(skill)))return false;
            // The framework validates and commits 10/20 electricity or one of two
            // free spear charges. Automatic cross-finish lightning never calls here.
            float scale=_tempestPreparation.TakeManualThird(key=="a0");
            if(key=="a0"&&ThirdAssaultCore)scale*=.6f;
            float power=(key=="a0"?190:key=="a1"?(Core(2,1)?650:240):150)*scale;
            Attack(skill,target,SplitResidualCloud(skill,Point(target.position),power));
            return true;
        }
        private float SplitResidualCloud(CharacterSkillDefinition skill,DamagePoint point,float power)
        {
            if(Job.index!=7||!point.Valid||!DamageTime.Positive(power))return power;
            string key=Key(skill);
            bool selected=key=="r1"?Core(1,1):
                (key=="a0"||key=="a2")&&_actor.RuntimeState.CoreTrait==NineKeyDamageData.Id(Job,"core11."+key);
            if(!selected)return power;
            // Move 30% of this attack's budget into three finite ticks. Unique
            // instance IDs retain each paid cast, including the two spear charges.
            _effects.Put(new CharacterTimedEffect{id="residual.cloud:"+Guid.NewGuid().ToString("N"),
                kind="residual.cloud",skill=key,value=power*.1f,x=point.x,z=point.z,radius=4,
                next=Now+1,end=Now+3,interval=1});
            return power*.7f;
        }
        private void TickResidualClouds()
        {
            foreach(var effect in _effects.Data.effects.Where(e=>e.kind=="residual.cloud").ToArray())
            {
                int ticks=CharacterEffectState.TakeTicks(effect,Now);
                if(effect.next>effect.end)_effects.Remove(effect.id);
                for(int n=0;n<ticks;n++)foreach(var target in CharacterKit.Enemies())
                    Attack(Skill(effect.skill),target,effect.value,0,CombatDamageKind.Periodic,
                        field:new DamagePoint(effect.x,effect.z),fieldRadius:effect.radius);
            }
        }
        private void ScheduleDamage(CharacterSkillDefinition skill,Transform target,float damage,float interval,int ticks,bool periodic)
        {
            if(skill==null||!CombatRange.LivingEnemy(target)||!DamageTime.Positive(damage)||!DamageTime.Positive(interval)||ticks<1||ticks>16)return;
            int id=CharacterKit.Id(target);string kind=periodic?"damage.dot":"damage.followup";
            _effects.Put(new CharacterTimedEffect{id=kind+":"+skill.id+"@"+id,skill=Key(skill),kind=kind,target=id,value=damage,
                next=Now+interval,end=Now+interval*ticks,interval=interval});
        }
        private void TickScheduledDamage()
        {
            foreach(var effect in _effects.Data.effects.Where(e=>e.kind=="damage.dot"||e.kind=="damage.followup").ToArray())
            {
                int ticks=CharacterEffectState.TakeTicks(effect,Now);
                // Consume due ticks before callbacks, including an invalid/immune
                // target. A failed automatic hit is never retried or promoted to direct.
                if(effect.next>effect.end)_effects.Remove(effect.id);
                for(int n=0;n<ticks;n++)
                    Attack(Skill(effect.skill),Resolve(effect.target),effect.value,0,
                        effect.kind=="damage.dot"?CombatDamageKind.Periodic:CombatDamageKind.Extra,launched:true);
            }
        }
        private void SeedDamage(DamagePoint point,float damage)
        {
            foreach(var enemy in CharacterKit.Enemies().Where(t=>Vector3.Distance(t.position,World(point))<=4))
                Attack(Skill("f2"),enemy,damage,0,CombatDamageKind.Extra,field:point,fieldRadius:4);
        }
        private void ReaperProtection()
        {
            // Shared by projected completion and execution, rather than granting
            // two overlapping rewards for spending the same stock in succession.
            if(Has("reaper.protection.gate"))return;
            Shield(transform,"reaper",Power*.03f);
            var patient=_actor.ActionSupportTarget;
            if(patient!=transform)Shield(patient,"reaper",Power*.03f);
            Effect("reaper.protection.gate","gate",transform,1,6);
        }
    }
}
