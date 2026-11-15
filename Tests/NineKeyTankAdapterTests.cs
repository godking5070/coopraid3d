using System;
using System.Collections.Generic;
using System.Linq;
using RaidRogue;

namespace RaidRogue
{
    /* ACTUAL_TANK_SNAPSHOT */
    public static class JsonUtility
    {
        public static string ToJson(object value)=>new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value);
        public static T FromJson<T>(string value)=>new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(value);
    }
    public sealed class Transform
    {
        public bool alive=true,enemy;public float distance,hp=1000;
        public BossController boss;public PartyLife life;public BossAddMinion add;
        public T GetComponent<T>() where T:class=>(typeof(T)==typeof(BossController)?(object)boss:typeof(T)==typeof(PartyLife)?life:(object)add) as T;
    }
    public sealed class PartyLife {public bool IsTank;}
    public sealed class BossAddMinion {public int taunts;public void TauntBy(Transform source,string name){taunts++;}}
    public sealed class BossController
    {
        public Transform transform,aggroTarget,lockedCastTarget;public float currentHp=1000;
        private readonly Dictionary<Transform,float> _combatThreat=new Dictionary<Transform,float>();
        public float CombatThreatOf(Transform source)=>source!=null&&_combatThreat.TryGetValue(source,out float value)?value:0;
        /* ACTUAL_THREAT_METHODS */
    }
    public static class StageManager {public static bool CombatSuspended;public static bool PlayerSkillsSuspended=>CombatSuspended;}
    public sealed class CombatCounterTarget
    {
        public static CombatCounterTarget Ensure(Transform target)=>new CombatCounterTarget();
        public bool TryCounter(Transform target,float range,float stagger)=>false;
        public void AddStagger(float amount){}
    }
    public static class CombatRange
    {
        public static bool LivingEnemy(Transform t)=>t!=null&&t.alive&&t.enemy;
        public static bool LivingPartyMember(Transform t)=>t!=null&&t.alive&&!t.enemy;
        public static bool InRange(Transform source,Transform t,float range)=>t!=null&&t.distance<=range;
    }
    public static class Mathf
    {public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);}
    public static class RaidCombatAuthority {public static bool Replica;}
    public static class CharacterKit {public static List<Transform> Targets=new List<Transform>();public static List<Transform> Enemies()=>Targets;}
    public sealed class TankAdapterActor
    {
        public CharacterRuntimeState Runtime;
        public CharacterStateSnapshot Snapshot=>Runtime.Snapshot;
        public CharacterDefinition Definition=>Runtime.Definition;
        public bool claim=true;
        public Transform ActionSupportTarget;
        public Func<CharacterSlot,Transform,Transform,bool> dispatch;
        public bool Execute(CharacterSlot slot,Transform target,out string reason,Transform support=null){reason="";return dispatch(slot,target,support);}
        public CharacterSkillDefinition Selected(CharacterSlot slot)=>Runtime.Selected(slot);
        public bool HasAuxiliary(string id)=>Runtime.HasAuxiliary(id);
        public bool ClaimTankFinisher(CharacterSkillDefinition skill){bool result=claim;claim=false;return result;}
    }
    public sealed class TankAdapterFixture
    {
        public readonly Transform transform=new Transform();
        public readonly RosterJob Job;
        public readonly TankAdapterActor _actor;
        public readonly CharacterRuntimeState _runtime;
        public NineKeyTankState _route;
        private readonly AegisSunState _cooperation=new AegisSunState();
        public readonly CharacterEffectState _effects=new CharacterEffectState();
        public bool _release,hit=true;
        private string _deferredDirectSkill;
        public float time;public int provedHits;public bool throwOnAttack;public Action onAttack;
        private int _bossId,_chargeTarget,_chargeSupport,_staggerTarget;private float _staggerRemaining;
        public float healed,shielded,damage,staggered;public int marks,attacks;
        public bool Active=>true;
        public bool Blood=>Job.index==1;
        public int Route=>Array.FindIndex(_actor.Definition.routes,r=>r.id==_runtime.RouteId)+1;
        private bool Core(int r,int v)=>_actor.Snapshot.coreTrait==NineKeyTankData.Core(Job,r,v);
        private float MaxHp=>1000;
        private float Now=>time;
        private IEnumerable<Transform> Party()=>CharacterKit.Targets.Where(CombatRange.LivingPartyMember);
        public TankAdapterFixture(int jobIndex,int route,int core=-1)
        {
            Job=CharacterRosterData.Jobs.Single(j=>j.index==jobIndex);
            _runtime=new CharacterRuntimeState(NineKeyTankData.Build(Job));_runtime.ChooseRoute(_runtime.Definition.routes[route-1].id,true);
            var snap=_runtime.Snapshot;snap.coreTrait=core<0?"":NineKeyTankData.Core(Job,route,core);_runtime.Restore(snap);
            _actor=new TankAdapterActor{Runtime=_runtime};_route=new NineKeyTankState(_runtime,Job);
            _actor.dispatch=Command;
        }
        private static int Id(Transform t)=>t==null?0:t.GetHashCode();
        private static Transform Resolve(int id)=>CharacterKit.Targets.FirstOrDefault(t=>Id(t)==id);
        public void Deferred(CharacterTimedEffect effect)=>ResolveBarrage(effect);
        public bool ValidEffects(CharacterKitSnapshot snapshot)=>new CharacterEffectState().Restore(snapshot)&&ValidEffectSnapshot(snapshot);
        private void Bind(){}
        private bool ValidSupport(Transform t)=>CombatRange.LivingPartyMember(t)&&CombatRange.InRange(transform,t,20);
        private float EffectiveRange(CharacterSkillDefinition s)=>s.range;
        private float Heal(Transform t,float amount)
        {if(!CombatRange.LivingPartyMember(t))return 0;float actual=Math.Min(1000-t.hp,amount);t.hp+=actual;healed+=actual;return actual;}
        private void Shield(Transform t,string key,float amount,float duration=6){if(CombatRange.LivingPartyMember(t))shielded+=amount;}
        private float Attack(Transform t,CharacterSkillDefinition s,float power,float stagger=0,CombatDamageKind kind=CombatDamageKind.Direct)
        {if(throwOnAttack)throw new Exception("test attack callback");if(!hit||!CombatRange.LivingEnemy(t)||!CombatRange.InRange(transform,t,s.range))return 0;if(OwnsDeferredDirectHit(s.id))provedHits++;onAttack?.Invoke();damage+=power;staggered+=stagger;attacks++;return power;}
        private void Mark(Transform t,float scale=1){marks++;}
        private void Effect(string key,string kind,Transform target,float value,float duration,float interval=1)
        {_effects.Put(new CharacterTimedEffect{id=key+"@"+Id(target),skill=key,target=Id(target),kind=kind,value=value,end=Now+duration,next=Now+interval,interval=interval});}
        public bool R(Transform target,Transform support=null,bool release=false){_release=release;try{return Identity(_actor.Definition.Skill(NineKeyTankData.Id(Job,"r"+Route)),target,support);}finally{_release=false;}}
        public bool Third(int variant,Transform target)=>AuxiliarySkill("a"+variant,_actor.Definition.Skill(NineKeyTankData.Id(Job,"a"+variant)),target);
        public bool Command(CharacterSlot slot,Transform target,Transform support=null)
        {
            BeforeInput(slot);if(Unavailable(slot).Length>0)return false;_actor.ActionSupportTarget=support;
            try{return _runtime.TryUse(slot,new CharacterUseContext(true,true,true,target?.distance??0),Id(target),s=>Execute(s,target),out _);}
            finally{_actor.ActionSupportTarget=null;}
        }
        public bool Final(int chain,int variant,Transform target,Transform support=null)
        {
            var snapshot=_runtime.Snapshot;if(chain==1){snapshot.combo1=3;snapshot.comboTime1=100;}else{snapshot.combo2=3;snapshot.comboTime2=100;}
            if(!_runtime.Restore(snapshot))throw new Exception("fixture restore");_actor.claim=true;
            return Combo(_actor.Definition.Skill(NineKeyTankData.Id(Job,chain==1?"g1":"g2"+variant)),target,support);
        }
        /* ACTUAL_TANK_METHODS */
    }
}
static class NineKeyTankAdapterTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Main()
    {
        try
        {
            var enemy=new Transform{enemy=true};var ally=new Transform{hp=0};
            for(int job=1;job<=2;job++)for(int route=1;route<=3;route++)
            {
                var empty=new TankAdapterFixture(job,route);
                Check(empty.Unavailable(CharacterSlot.Identity).Length>0,"R empty resource is explained before execution");
                foreach(var response in new[]{CharacterSlot.Job1,CharacterSlot.Counter,CharacterSlot.Movement,CharacterSlot.OffGcd2})
                    Check(empty.Unavailable(response)=="","resource warning never blocks required response");
            }
            for(int job=1;job<=2;job++)
            {
                var threat=new TankAdapterFixture(job,1);threat.transform.life=new PartyLife{IsTank=true};
                var otherTank=new Transform{life=new PartyLife{IsTank=true}};
                var ordinary=new Transform{life=new PartyLife{IsTank=false}};
                var bossTarget=new Transform{enemy=true};var boss=new BossController{transform=bossTarget,lockedCastTarget=ordinary};bossTarget.boss=boss;
                CharacterKit.Targets=new List<Transform>{bossTarget,otherTank,ordinary,threat.transform};
                Check(!threat.Command(CharacterSlot.OffGcd2,otherTank),"F no remembered boss rejects without cooldown");
                boss.AddCombatThreat(otherTank,100);
                Check(threat.Command(CharacterSlot.OffGcd2,bossTarget)&&boss.aggroTarget==threat.transform&&boss.CombatThreatOf(threat.transform)>boss.CombatThreatOf(otherTank),"F taunt secures top threat");
                Check(!threat.Command(CharacterSlot.OffGcd2,otherTank),"taunt and transfer share cooldown");
                threat._runtime.Tick(20);float own=boss.CombatThreatOf(threat.transform),others=boss.CombatThreatOf(otherTank);
                Check(!threat.Command(CharacterSlot.OffGcd2,ordinary)&&!threat.Command(CharacterSlot.OffGcd2,threat.transform),"non tank and self transfer rejected without spending ready cooldown");
                otherTank.distance=30;Check(!threat.Command(CharacterSlot.OffGcd2,otherTank),"out of range transfer rejected");otherTank.distance=0;
                Check(threat.Command(CharacterSlot.OffGcd2,otherTank)&&boss.CombatThreatOf(threat.transform)==own*.2f&&boss.CombatThreatOf(otherTank)==others+own*.8f,"F transfers remembered boss threat exactly once");
                Check(boss.lockedCastTarget==ordinary&&boss.aggroTarget==otherTank,"threat swap leaves locked cast target intact");
                Check(threat._runtime.Resource(threat.Job.Resource)==0,"F works at empty blood or magazine");
            }
            foreach(var slot in new[]{CharacterSlot.Job1,CharacterSlot.Counter,CharacterSlot.Movement,CharacterSlot.OffGcd2})
            {
                var response=new TankAdapterFixture(1,2);response._runtime.Gain(response.Job.Resource,100);response.R(enemy);
                response.BeforeInput(slot);
                Check(!response._route.Snapshot.charging&&response._runtime.Resource(response.Job.Resource)==100&&response.damage==0,"priority input cancels and returns unspent reservation without attack");
                if(slot==CharacterSlot.Job1)Check(response.Command(slot,response.transform)&&response._effects.Data.effects.Any(e=>e.skill=="guard"),"Q effect actually executes after charging cancellation");
                if(slot==CharacterSlot.Counter)Check(response.Command(slot,enemy)&&response.attacks==1,"counter attack actually executes after charging cancellation");
                var spinning=new TankAdapterFixture(1,3);spinning._runtime.Gain(spinning.Job.Resource,60);spinning.R(enemy);spinning.BeforeInput(slot);
                Check(!spinning._route.Snapshot.whirling&&spinning._runtime.Resource(spinning.Job.Resource)==60,"response cancels channel without phantom tick");
                var dancer=new TankAdapterFixture(2,2);dancer._route.FinishCombo(1);dancer._route.FinishCombo(2);dancer.R(enemy);dancer._route.PrepareDance(true);dancer.BeforeInput(slot);
                Check(dancer._route.Snapshot.danceTime==8&&dancer._route.Snapshot.danceReady>0,"response preserves dance within original overall timer");
            }
            for(int job=1;job<=2;job++)for(int route=1;route<=3;route++)for(int core=0;core<2;core++)
            {
                var replica=new TankAdapterFixture(job,route,core);
                replica._runtime.Gain(replica.Job.Resource,job==1?100:4);replica._route.SynchronizeExternalResources();
                if(job==2&&route==3)replica._runtime.Gain(NineKeyTankData.Heat(replica.Job),80);
                Check(replica.R(enemy,ally),"snapshot fixture route activation");
                if(job==1)replica.Third(2,ally);
                string routeJson=replica.Capture();float resource=replica._runtime.Resource(replica.Job.Resource);
                Check(replica.Restore(routeJson)&&replica.Capture()==routeJson&&replica._runtime.Resource(replica.Job.Resource)==resource,"all six routes/twelve cores full effect snapshot roundtrip without gain");
            }
            ally.hp=0;
            var healer=new TankAdapterFixture(1,1);healer._runtime.Gain(healer.Job.Resource,100);
            Check(healer.R(ally)&&healer._runtime.Resource(healer.Job.Resource)==70&&ally.hp==100,"B1 valid ally uses30 fixed heal");
            ally.distance=30;Check(!healer.R(ally)&&healer._runtime.Resource(healer.Job.Resource)==70,"invalid designated ally never falls back or spends");ally.distance=0;
            healer.transform.hp=500;Check(healer.R(enemy)&&healer.transform.hp==620,"enemy target heals self at fixed own coefficient");
            var clot=new TankAdapterFixture(1,1,0);clot._runtime.Gain(clot.Job.Resource,30);Check(clot.R(null)&&clot.shielded==60,"clotted blood caps overheal protection");
            var share=new TankAdapterFixture(1,1,1);share._runtime.Gain(share.Job.Resource,30);share.transform.hp=0;ally.hp=0;
            Check(share.R(enemy,ally)&&share.transform.hp==84&&ally.hp==36&&share.healed==120,"distribution splits original budget");
            var charge=new TankAdapterFixture(1,2);charge._runtime.Gain(charge.Job.Resource,100);Check(charge.R(enemy),"R handler begins reservation");
            Check(!charge.Release(CharacterSlot.Gcd2,enemy,null,out _)&&charge._route.Snapshot.reserved==60,"wrong release slot cannot spend or refund R reservation");
            charge.BeforeInput(CharacterSlot.Counter);Check(charge._runtime.Resource(charge.Job.Resource)==100&&!charge.R(enemy,null,true)&&charge.damage==0,"counter cancels reservation without phantom attack");
            charge.R(enemy);charge._route.Advance(3,out _,out _);Check(charge.R(enemy,null,true)&&charge.damage>0&&charge._runtime.Resource(charge.Job.Resource)==40,"release handler fires once");
            Check(!charge.R(enemy,null,true),"duplicate release handler rejected");
            var release=new TankAdapterFixture(1,2);release._runtime.Gain(release.Job.Resource,100);release.Command(CharacterSlot.Identity,enemy);
            Check(release.Release(CharacterSlot.Identity,enemy,null,out _)&&release.attacks==1&&release._runtime.Resource(release.Job.Resource)==40,"release enters real common commit once");
            Check(!release.Release(CharacterSlot.Identity,enemy,null,out _)&&release.attacks==1,"duplicate host release cannot attack again");
            var secondCharge=new TankAdapterFixture(1,2);var beforeSecond=secondCharge._runtime.Snapshot;beforeSecond.combo2=3;beforeSecond.comboTime2=100;secondCharge._runtime.Restore(beforeSecond);
            Check(secondCharge.BeginSecondCharge(enemy,null)&&secondCharge._runtime.Snapshot.combo2==3&&secondCharge._runtime.Snapshot.gcd==0,"second charge start reserves no blood/GCD/combo");
            Check(secondCharge.Release(CharacterSlot.Gcd2,enemy,null,out _)&&secondCharge._runtime.Snapshot.combo2==1&&secondCharge._runtime.Snapshot.gcd==2&&secondCharge._runtime.Resource(secondCharge.Job.Resource)==20,"second release commits combo/GCD and grants only landed finisher blood");
            var missedRelease=new TankAdapterFixture(1,2);missedRelease._runtime.Gain(missedRelease.Job.Resource,60);missedRelease.R(enemy);enemy.distance=50;
            Check(!missedRelease.Release(CharacterSlot.Identity,enemy,null,out _)&&missedRelease._runtime.Resource(missedRelease.Job.Resource)==60&&!missedRelease._route.Snapshot.charging&&missedRelease.attacks==0,"invalid release target cancels with one refund and no attack");enemy.distance=0;
            var cannon=new TankAdapterFixture(2,1,1);for(int i=0;i<2;i++){cannon._route.FinishCombo(2);cannon._route.FinishCombo(1);}
            Check(cannon.R(enemy)&&cannon.attacks==1&&cannon._effects.Data.effects.Count(e=>e.kind=="barrage")==1&&cannon.marks==1,"all fire first pair then finite deferred pair");
            cannon.BeforeInput(CharacterSlot.Job1);Check(!cannon._effects.Data.effects.Any(e=>e.kind=="barrage")&&cannon._runtime.Resource(cannon.Job.Resource)==0,"Q removes remaining fire without refunding committed rounds");
            CharacterKit.Targets=new List<Transform>{enemy};
            var delayed=new TankAdapterFixture(2,1,1);for(int i=0;i<4;i++)delayed._route.FinishCombo(1);
            delayed.R(enemy);var pending=delayed._effects.Data.effects.Single();
            Check(delayed.ValidEffects(delayed._effects.Data.Copy()),"pending manual barrage snapshot accepted");
            var altered=delayed._effects.Data.Copy();altered.effects[0].radius=2;
            Check(!delayed.ValidEffects(altered),"invalid cooperation flag rejected");
            altered=delayed._effects.Data.Copy();altered.effects[0].value=-1;
            Check(!delayed.ValidEffects(altered),"negative pending damage rejected");
            altered=delayed._effects.Data.Copy();altered.effects[0].ticks=1;
            Check(!delayed.ValidEffects(altered),"already consumed barrage cannot restore as pending");
            Check(!healer.ValidEffects(delayed._effects.Data.Copy()),"different tank cannot inherit barrage");
            string captured=delayed.Capture();
            Check(delayed.Restore(captured)&&delayed.Restore(captured)&&delayed.Capture()==captured,"actual full combat snapshot roundtrip/reapply is idempotent");
            var forged=JsonUtility.FromJson<NineKeyTankCombatSnapshot>(captured);forged.effects.effects[0].kind="unknown";
            Check(!delayed.Restore(JsonUtility.ToJson(forged))&&delayed.Capture()==captured,"full snapshot rejection leaves all state untouched");
            Check(!delayed.Restore("{bad json")&&delayed.Capture()==captured,"malformed combat JSON rejected without mutation");
            pending=delayed._effects.Data.effects.Single();
            delayed.Deferred(pending);Check(delayed.attacks==1&&!delayed.OwnsDeferredDirectHit(pending.skill),"pending attack cannot borrow proof before its due time");
            delayed.time=1;delayed.onAttack=()=>delayed.Deferred(pending);delayed.Deferred(pending);delayed.Deferred(pending);
            Check(delayed.attacks==2&&delayed.provedHits==1&&!delayed.OwnsDeferredDirectHit(pending.skill),"deferred manual hit proof is scoped and one-shot even with reentrant callback");
            var cancelled=new TankAdapterFixture(2,1,1);for(int i=0;i<4;i++)cancelled._route.FinishCombo(1);
            cancelled.R(enemy);pending=cancelled._effects.Data.effects.Single();cancelled.BeforeInput(CharacterSlot.Counter);cancelled.time=1;cancelled.Deferred(pending);
            Check(cancelled.attacks==1&&cancelled.provedHits==0,"cancelled pending shot cannot be revived with stale reference");
            var failure=new TankAdapterFixture(2,1,1);for(int i=0;i<4;i++)failure._route.FinishCombo(1);
            failure.R(enemy);pending=failure._effects.Data.effects.Single();failure.time=1;failure.throwOnAttack=true;
            try{failure.Deferred(pending);}catch(Exception){}
            Check(!failure.OwnsDeferredDirectHit(pending.skill)&&failure._effects.Data.effects.Length==0,"callback failure closes proof and consumes pending attack");
            var dance=new TankAdapterFixture(2,2);dance._route.FinishCombo(1);dance._route.FinishCombo(2);
            var pairs=new List<TankAdapterFixture>();
            foreach(var order in new[]{new[]{1,1},new[]{2,1},new[]{1,2},new[]{2,2}})
            {
                var pair=new TankAdapterFixture(2,1);pair._route.FinishCombo(order[0]);pair._route.FinishCombo(order[1]);
                Check(pair.R(enemy),"each FIFO pair fires");pairs.Add(pair);
                var divided=new TankAdapterFixture(2,1,0);divided._route.FinishCombo(order[0]);divided._route.FinishCombo(order[1]);
                Check(divided.R(enemy)&&divided.R(enemy)&&divided.damage==pair.damage&&divided.staggered==pair.staggered&&divided.marks==pair.marks,"split pair preserves total damage/stagger and cooperation budget");
            }
            Check(pairs[0].damage>pairs[1].damage&&pairs[1].damage>pairs[2].damage&&pairs[1].marks==1,"pierce ordering distinguishes direct and cooperation budgets");
            Check(pairs[3].staggered>pairs[2].staggered&&pairs[2].staggered>pairs[0].staggered,"crush ordering distinguishes stagger budgets");
            Check(dance.R(enemy)&&dance.damage==0&&!dance.R(enemy),"dance start not a free attack; R alone cannot shoot");
            for(int i=0;i<3;i++){dance._route.PrepareDance(true);Check(dance.R(enemy),"prepared dance shot fires");}
            Check(dance.marks==1&&dance._runtime.Resource(dance.Job.Resource)==0,"only last dance shot grants cooperation and no self reload");
            var furnace=new TankAdapterFixture(2,3);furnace._route.FinishCombo(1);furnace.hit=false;
            Check(furnace.Final(2,0,enemy)&&furnace._runtime.Resource(furnace.Job.Resource)==0&&furnace._runtime.Resource(NineKeyTankData.Heat(furnace.Job))==30,"missed furnace emission still spends and heats");
            Check(furnace._runtime.Resource(NineKeyTankData.Gauge(furnace.Job))==10,"miss did not add second finisher gauge");
            furnace.hit=true;furnace._runtime.Gain(NineKeyTankData.Heat(furnace.Job),50);
            Check(furnace.R(null,ally)&&furnace.shielded>0&&furnace._runtime.Resource(NineKeyTankData.Heat(furnace.Job))==40,"hot vent protects without enemy");
            var scatter=new TankAdapterFixture(2,1);CharacterKit.Targets=new List<Transform>{enemy,new Transform{enemy=true},new Transform{enemy=true}};
            Check(scatter.Final(2,0,enemy)&&scatter.attacks==3&&scatter._runtime.Resource(scatter.Job.Resource)==1,"scatter multiple hits grant one round");
            Check(scatter._runtime.Resource(NineKeyTankData.Gauge(scatter.Job))==10,"scatter gauge once per action");
            var reload=new TankAdapterFixture(2,3);for(int i=0;i<4;i++)reload.Third(0,reload.transform);
            Check(!reload.Third(0,reload.transform)&&reload._runtime.Resource(NineKeyTankData.Heat(reload.Job))==0,"full reload rejected and never changes heat");
            Console.WriteLine("PASS: actual nine-key tank effect adapter "+checks+" checks (managed world stubs).");
        }
        catch(Exception e){Console.WriteLine("FAIL: "+e.Message);Environment.Exit(1);}
    }
}
