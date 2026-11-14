using System;
using System.Linq;
using System.Collections.Generic;
using RaidRogue;
namespace RaidRogue
{
    /* ACTUAL_HEALER_SNAPSHOT */
    public static class JsonUtility
    {
        public static string ToJson(object value)=>new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value);
        public static T FromJson<T>(string value)=>new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(value);
    }
    public static class CharacterKit {public static int Id(Transform t)=>t?.id??0;public static Transform Resolve(int id)=>PartyLife.members.FirstOrDefault(t=>t.id==id);}
    public sealed class TargetingManager {public static TargetingManager Instance;public bool HasSupportSelection;public Transform SupportTarget;}
    public sealed class Transform
    {
        public Vector3 position;
        public int id=-17;public bool alive=true,enemy;public float distance,hp=1000;public CombatStatusEffects status=new CombatStatusEffects();public object gameObject=>this;
    }
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));}
    public sealed class CombatStatusEffects
    {
        public int cleanses;
        public static CombatStatusEffects Ensure(object value)=>((Transform)value).status;
        public bool CleanseOne(string[] tags){cleanses++;return true;}
    }
    public static class RaidCombatAuthority{public static bool Replica;}
    public static class StageManager{public static bool CombatSuspended;public static bool PlayerSkillsSuspended=>CombatSuspended;}
    public static class Mathf{public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);}
    public static class PartyLife{public static List<Transform> members=new List<Transform>();public static List<Transform> OrderedMembers()=>members;}
    public static class CombatRange
    {
        public static bool LivingPartyMember(Transform t)=>t!=null&&t.alive&&!t.enemy;
        public static bool InRange(Transform a,Transform b,float range)=>b!=null&&b.distance<=range;
    }
    public sealed class CombatCounterTarget
    {
        public static CombatCounterTarget Ensure(Transform t)=>new CombatCounterTarget();
        public bool TryCounter(Transform t,float range,float stagger)=>false;public void AddStagger(float amount){}
    }
    public sealed class HealerAdapterActor
    {
        public CharacterRuntimeState Runtime;public CharacterStateSnapshot Snapshot=>Runtime.Snapshot;
        public CharacterRuntimeState RuntimeState=>Runtime;
        public Vector3? ActionGroundPoint;public bool ActionRecall;public CharacterSkillDefinition Selected(CharacterSlot slot)=>Runtime.Selected(slot);
        public bool claim=true,direct=true;public Transform ActionSupportTarget;
        public bool HasAuxiliary(string id)=>Runtime.HasAuxiliary(id);
        public bool ClaimHealerFinisher(CharacterSkillDefinition skill){bool result=claim;claim=false;return result;}
        public bool ClaimHealerDirect(CharacterSkillDefinition skill){bool result=direct;direct=false;return result;}
    }
    public sealed class HealerAdapterFixture
    {
        public Transform transform=new Transform();public RosterJob Job;public HealerAdapterActor _actor;
        public CharacterRuntimeState _runtime;public NineKeyHealerResources _resources;
        public float time,healed,shielded;private float _safeReady;private string _encore="";
        public List<(string key,string kind,Transform target,float value,float duration)> effects=new List<(string,string,Transform,float,float)>();
        public int growth,finish,directHeals,attacks;public bool hit=true;
        public bool Active=>true;private float Now=>time;private float Power=>1000;
        public int Route=>Array.FindIndex(_runtime.Definition.routes,r=>r.id==_runtime.RouteId)+1;
        private bool Core(int route,int variant)=>_runtime.Snapshot.coreTrait==NineKeyHealerData.Core(Job,route,variant);
        public HealerGardenState _garden=new HealerGardenState();public HealerHarvestState _harvest=new HealerHarvestState();
        public HealerConductorState _conductor=new HealerConductorState();public HealerSongState _song;
        public HealerLinkState _link=new HealerLinkState();public HealerSurgeryState _surgery=new HealerSurgeryState();
        public HealerLayerState _layers=new HealerLayerState();public CharacterEffectState _effects=new CharacterEffectState();
        private float _flowerMoveReady,_gardenTick;private int _songTarget;private HashSet<int> _flowerInside=new HashSet<int>();
        public HealerAdapterFixture(int job)
        {
            Job=CharacterRosterData.Jobs.Single(j=>j.index==job);_runtime=new CharacterRuntimeState(NineKeyHealerData.Build(Job));
            _actor=new HealerAdapterActor{Runtime=_runtime};_resources=new NineKeyHealerResources(_runtime,Job);_song=new HealerSongState(_resources);
            PartyLife.members=new List<Transform>{transform};RaidCombatAuthority.Replica=false;StageManager.CombatSuspended=false;
        }
        private void Bind(){}private float EffectiveRange(CharacterSkillDefinition skill)=>skill.range;
        private bool Valid(Transform t,float range)=>CombatRange.LivingPartyMember(t)&&CombatRange.InRange(transform,t,range);
        private IEnumerable<Transform> Party(float range=30)=>PartyLife.members.Where(t=>Valid(t,range));
        private float Heal(Transform t,float amount,bool direct=false)
        {if(!CombatRange.LivingPartyMember(t))return 0;float actual=Math.Min(amount,1000-t.hp);t.hp+=actual;healed+=actual;if(direct)directHeals++;return actual;}
        private void Shield(Transform t,string key,float amount,float duration=6){shielded+=amount;}
        private void Effect(string key,string kind,Transform t,float value,float duration){effects.RemoveAll(e=>e.key==key&&e.target==t);effects.Add((key,kind,t,value,duration));_effects.Put(new CharacterTimedEffect{id=key+"@"+t.id,skill=key,kind=kind,target=t.id,value=value,end=Now+duration,next=Now+1,interval=1});}
        private bool Attack(CharacterSkillDefinition s,Transform t,float power,float stagger=0,CombatDamageKind kind=CombatDamageKind.Direct){attacks++;return hit&&t!=null&&t.enemy;}
        private CharacterTimedEffect[] Flowers=>_effects.Data.effects.Where(e=>e.kind=="flower"&&e.end>Now).OrderBy(e=>e.end).ToArray();
        private bool NearFlower(Transform t)=>Flowers.Any(f=>Vector3.Distance(t.position,new Vector3(f.x,f.y,f.z))<=f.radius);
        private bool CanPlaceFlower(Vector3 point)=>Vector3.Distance(point,transform.position)<=25;
        private Dictionary<int,float> layerLedger=new Dictionary<int,float>();
        private float LayerRemaining(Transform t)=>layerLedger.TryGetValue(t.id,out float amount)?amount:0;
        private void RemoveLayer(Transform t){if(t!=null)layerLedger.Remove(t.id);}
        private void RegisterLayer(Transform t,float amount){layerLedger[t.id]=amount;_layers.SetActiveBudget(t.id,amount);}
        private void RemoveOwnedProtection(){layerLedger.Clear();}
        public void Advance(float delta){time+=delta;TickRoutes(delta);}
        public void Configure(int route,int core=0){_runtime.ChooseRoute(_runtime.Definition.routes[route-1].id,true);var rule=_runtime.Definition.loadouts.Single(l=>l.slot==0&&l.routeId==_runtime.RouteId);_runtime.ChangeLoadout(rule.id,rule.choices[core],true);_resources=new NineKeyHealerResources(_runtime,Job);ResetRoutes();}
        public bool Use(string key,Transform target)=>Execute(_runtime.Definition.Skill(NineKeyHealerData.Id(Job,key)),target);
        public void Step(int step){var s=_runtime.Snapshot;s.combo1=s.combo2=step;if(!_runtime.Restore(s))throw new Exception("step restore");_actor.claim=_actor.direct=true;}
        /* ACTUAL_HEALER_METHODS */
        /* ACTUAL_HEALER_RESTORE */
        /* ACTUAL_HEALER_MOVEMENT */
        /* ACTUAL_HEALER_ROUTES */
    }
}
static class HealerAdapterTests
{
    static int count;static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {try{Run();}catch(Exception error){Console.WriteLine("FAIL healer adapter: "+error.Message);Environment.Exit(1);}}
    static void Run()
    {
        foreach(int job in new[]{3,4,5})
        {
            foreach(string choice in new[]{"g20","g21","g22"})
            {
                var f=new HealerAdapterFixture(job);var patient=new Transform{hp=0};PartyLife.members.Add(patient);
                f.Step(1);Check(f.Use(choice,patient)&&patient.hp>0,"every second-combo choice heals immediately");
                Check(f._runtime.Resource(f.Job.Resource)==0,"first heal does not grant completion resource");
                patient.hp=1000;f.Step(3);float before=f._runtime.Resource(f.Job.Resource);
                Check(f.Use(choice,patient)&&f._runtime.Resource(f.Job.Resource)==before+(job==5?20:1),"overheal finisher earns fixed resource");
                float gained=f._runtime.Resource(f.Job.Resource);f.Use(choice,patient);Check(f._runtime.Resource(f.Job.Resource)==gained,"replayed action cannot earn another completion");
                patient.alive=false;f.Step(3);Check(!f.Use(choice,patient)&&f._runtime.Resource(f.Job.Resource)==gained,"invalid patient cannot heal or earn resource");
            }
            var basic=new HealerAdapterFixture(job);var ally=new Transform{hp=0};PartyLife.members.Add(ally);
            Check(basic.Use("q",ally)&&ally.hp==160&&ally.status.cleanses==1,"Q direct heal and one cleanse");
            ally.distance=100;float hp=ally.hp;Check(!basic.Use("q",ally)&&ally.hp==hp&&ally.status.cleanses==1,"out of range Q refuses silently redirecting to self");
            ally.distance=0;basic._actor.ActionSupportTarget=new Transform{alive=false};Check(basic.Use("f",null)&&ally.hp>hp,"F heals despite invalid selected support and no enemy");
            RaidCombatAuthority.Replica=true;hp=ally.hp;Check(!basic.Use("f",null)&&ally.hp==hp,"replica cannot execute healing");RaidCombatAuthority.Replica=false;
            StageManager.CombatSuspended=true;Check(!basic.Use("f",null),"suspended combat cannot execute healing");StageManager.CombatSuspended=false;
            basic.hit=false;basic.Step(3);float resource=basic._runtime.Resource(basic.Job.Resource);basic.Use("g1",new Transform{enemy=true});Check(basic._runtime.Resource(basic.Job.Resource)==resource,"attack miss cannot earn resources");
        }
        var bard=new HealerAdapterFixture(4);var patient2=new Transform{hp=0};PartyLife.members.Add(patient2);bard._actor.ActionSupportTarget=patient2;
        Check(!bard.Use("a1",patient2),"Encore refuses missing Q/F history");bard.Use("q",patient2);
        float shield=bard.shielded;int cleanse=patient2.status.cleanses;float healed=bard.healed;int effects=bard.effects.Count;
        Check(bard.Use("a1",null)&&bard.healed-healed==80&&bard.shielded==shield&&patient2.status.cleanses==cleanse&&bard.effects.Count==effects,"Encore only replays half base direct healing");
        var residual=new HealerAdapterFixture(3);Check(residual._runtime.ChangeAuxiliary("auxiliary.1",NineKeyHealerData.Residual,true)&&residual._runtime.ChangeAuxiliary("auxiliary.2",NineKeyHealerData.Safe,true),"residual and safe trait fixture");
        residual.Use("q",residual.transform);Check(residual.effects.Single(e=>e.key=="residual").value==10,"overheal conversion capped and split into finite ticks");
        float firstShield=residual.shielded;residual.Use("q",residual.transform);Check(residual.shielded==firstShield,"safe trait rate limit blocks repeated shield");
        residual.time=8;residual.Use("q",residual.transform);Check(residual.shielded>firstShield,"safe trait becomes available after gate");
        SnapshotChecks();
        RouteChecks();
        var mobile=new HealerAdapterFixture(3);mobile._runtime.ChangeAuxiliary("auxiliary.1",NineKeyHealerData.Mobile,true);
        var near=new Transform{id=-19,distance=9};PartyLife.members.Add(near);float mobileResource=mobile._runtime.Resource(mobile.Job.Resource);
        mobile.MovementStarted(-19);Check(mobile.effects.Count(e=>e.key=="mobile")==2&&mobile._runtime.Resource(mobile.Job.Resource)==mobileResource,"movement supports self and selected nearby ally without resource reward");
        mobile.effects.Clear();near.distance=11;mobile.MovementStarted(-19);Check(mobile.effects.Count==1&&mobile.effects[0].target==mobile.transform,"distant selected ally does not redirect to another recipient");
        mobile.effects.Clear();RaidCombatAuthority.Replica=true;mobile.MovementStarted(-19);Check(mobile.effects.Count==0,"replica movement prediction cannot grant support");RaidCombatAuthority.Replica=false;
        Console.WriteLine("PASS actual healer execution/snapshot methods: "+count+"; Unity scene/lifecycle/transport remain separate checks.");
    }
    static void SnapshotChecks()
    {
        foreach(int job in new[]{3,4,5})foreach(int route in new[]{1,2,3})
        {
            var f=new HealerAdapterFixture(job);f._runtime.ChooseRoute(f._runtime.Definition.routes[route-1].id,true);
            f._resources=new NineKeyHealerResources(f._runtime,f.Job);f._song=new HealerSongState(f._resources);
            f._resources.CompleteCombo(2);f._resources.CompleteCombo(1);
            if(job==3&&route==1)f._garden.Plant(-19,2,4);
            if(job==3&&route==3){f._harvest.Begin(12,2);f._harvest.DirectInput();}
            if(job==4&&route==1)f._conductor.Request(-19,-17,1,8);
            if(job==4&&route==2)f._layers.Begin(new[]{-19},2,100,12);
            if(job==4&&route==3)f._song.Begin(1.5f,2);
            if(job==5&&route==1)f._link.Begin(-19,false,12);
            if(job==5&&route==3)f._surgery.Treat(-19,12,.25f,out _);
            string json=f.Capture();float resource=f._runtime.Resource(f.Job.Resource);
            Check(f.Restore(json)&&f.Capture()==json,"actual JSON snapshot roundtrip route "+job+"/"+route);
            Check(f.Restore(json)&&f._runtime.Resource(f.Job.Resource)==resource&&f.healed==0&&f.shielded==0,"snapshot replay cannot cast or refund");
            var bad=JsonUtility.FromJson<NineKeyHealerCombatSnapshot>(json);bad.route=route%3+1;
            Check(!f.Restore(JsonUtility.ToJson(bad))&&f.Capture()==json,"wrong-route snapshot rejects atomically");
            bad=JsonUtility.FromJson<NineKeyHealerCombatSnapshot>(json);bad.gardenTick=2;
            Check(!f.Restore(JsonUtility.ToJson(bad))&&f.Capture()==json,"invalid timer rejects atomically");
            bad=JsonUtility.FromJson<NineKeyHealerCombatSnapshot>(json);bad.effects.effects=new[]{new CharacterTimedEffect{id="unexpected@-19",target=-19,kind="resurrect",skill="unexpected",end=5,next=1,interval=1}};
            Check(!f.Restore(JsonUtility.ToJson(bad))&&f.Capture()==json,"unrecognized effect kind rejects atomically");
            if(job==4&&route==3)
            {
                bad=JsonUtility.FromJson<NineKeyHealerCombatSnapshot>(json);bad.resources.verseReserved=false;
                Check(!f.Restore(JsonUtility.ToJson(bad))&&f.Capture()==json,"song must carry one matching paid reservation");
            }
        }
    }
    static void RouteChecks()
    {
        for(int core=0;core<2;core++)for(int job=3;job<=5;job++)for(int route=1;route<=3;route++)
        {
            var f=new HealerAdapterFixture(job);f.Configure(route,core);
            var ally=new Transform{id=-19,hp=0};PartyLife.members.Add(ally);f._actor.ActionSupportTarget=ally;
            var enemy=new Transform{id=-30,enemy=true};string r="r"+route;
            if(job==5&&route==2){for(int i=0;i<3;i++)f._resources.CompleteCombo(2);}else f._runtime.Gain(f.Job.Resource,100);
            if(job==3&&route==2)f._actor.ActionGroundPoint=new Vector3(0,0,0);
            Check(f.Use(r,enemy),"route starts through actual R handler "+job+"/"+route+" core"+core);
            if(job==3&&route==1)
            {
                float balance=f._runtime.Resource(f.Job.Resource);Check(!f.Use(r,enemy)&&balance==f._runtime.Resource(f.Job.Resource),"duplicate seed refuses without spending");
                for(int i=0;i<3;i++){f.Step(1);f.Use("g20",ally);}
                Check(f._garden.Snapshot.Length==0&&ally.hp>120,"direct primary heals accelerate bloom and remove remaining plant");
                float hp=ally.hp;f.Advance(20);Check(ally.hp==hp,"early bloom cannot retain abandoned HoT");
            }
            if(job==3&&route==2)
            {
                f._actor.ActionGroundPoint=new Vector3(1,0,0);Check(f.Use(r,enemy),"flower installs or replaces oldest");
                int flowers=f._effects.Data.effects.Count(e=>e.kind=="flower");Check(flowers==(core==0?1:2),"flower capacity follows core");
                ally.hp=0;f.Step(3);f.Use("g20",ally);Check(ally.hp==(core==0?120:105),"overlapping flowers give one relay heal in addition to base heal");
                if(core==1){float end=f._effects.Data.effects.First(e=>e.kind=="flower").end,balance=f._runtime.Resource(f.Job.Resource);f._actor.ActionRecall=true;f._actor.ActionGroundPoint=new Vector3(3,0,0);Check(f.Use(r,enemy)&&f._runtime.Resource(f.Job.Resource)==balance&&f._effects.Data.effects.First(e=>e.kind=="flower").end==end,"long flower move spends no resource and preserves life");}
            }
            if(job==3&&route==3)
            {
                for(int i=0;i<4;i++){f.Step(1);f.Use("g20",ally);}ally.hp=0;
                Check(f.Use(r,null)&&!f._harvest.Active&&ally.hp>0,"manual harvest heals even without enemy");float hp=ally.hp;f.Advance(30);Check(ally.hp==hp,"manual harvest consumes expiry payout");
            }
            if(job==4&&route==1)
            {
                float balance=f._runtime.Resource(f.Job.Resource);Check(!f.Use(r,enemy)&&f._runtime.Resource(f.Job.Resource)==balance,"duplicate pending request blocked without spend");
                f.Advance(8);Check(f._conductor.Replies==1,"unanswered request times out into same reply");
                float shields=f.shielded;f.Step(3);f.Use("g20",ally);Check(f._conductor.Replies==0&&f.shielded>shields,"healing completion consumes reply into party shield");
            }
            if(job==4&&route==2)
            {
                Check(f._layers.Snapshot.recipients.All(e=>e.layers==(core==0?2:4)),"layer count follows core");
                ally.distance=100;Check(f.Use(r,enemy)&&!f._layers.Active&&ally.hp==75,"settlement reaches original living recipient at any current distance and consumes own budget");
            }
            if(job==4&&route==3)
            {
                float reserved=f._runtime.Resource(f.Job.Resource);f.BeforeInput(CharacterSlot.Job1);Check(!f._song.Active&&f._runtime.Resource(f.Job.Resource)==reserved+1,"Q immediately cancels and refunds unfinished verse");f.BeforeInput(CharacterSlot.Job1);Check(f._runtime.Resource(f.Job.Resource)==reserved+1,"repeat cancellation cannot duplicate refund");
                f.Use(r,enemy);f.Advance(core==0?1.5f:2);Check(ally.hp>0&&f._song.Snapshot.completed==1,"completed verse heals and progresses once");
                float balance=f._runtime.Resource(f.Job.Resource);f.Clear();Check(!f._song.Active&&f._runtime.Resource(f.Job.Resource)==balance,"encounter clear discards pending reservation without reset-time refund");
            }
            if(job==5&&route==1)
            {
                var other=new Transform{id=-20,hp=0};PartyLife.members.Add(other);f.Advance(2);f._actor.ActionSupportTarget=other;float balance=f._runtime.Resource(f.Job.Resource),remaining=f._link.Snapshot.remaining;
                Check(f.Use(r,enemy)&&f._runtime.Resource(f.Job.Resource)==balance&&f._link.Snapshot.remaining==remaining&&other.hp==0,"link reassignment neither pays again nor heals nor renews");
                f.Step(1);f.Use("g1",enemy);Check(other.hp>0,"direct attack heals linked patient with fixed amount");
            }
            if(job==5&&route==2)
            {
                Check(f._runtime.Resource(f.Job.Resource)==0&&ally.hp==90&&f.effects.Any(e=>e.key=="recipe.hot"),"three blue ingredients produce base heal plus finite regeneration");
            }
            if(job==5&&route==3)
            {
                Check(ally.hp==120&&f._surgery.Snapshot.nextStep==2,"first surgical R heals immediately");
                var second=new Transform{id=-20,hp=0};PartyLife.members.Add(second);f._actor.ActionSupportTarget=second;f.Advance(1);float balance=f._runtime.Resource(f.Job.Resource);
                Check(f.Use(r,enemy)&&second.hp>=100&&f._runtime.Resource(f.Job.Resource)==balance,"next patient receives prepaid second treatment");
                f.Advance(1);Check(f.Use(r,enemy)&&!f._surgery.Active&&f.effects.Any(e=>e.key=="prognosis"),"third treatment issues finite prognosis then ends sequence");
            }
            f.Clear();Check(f._effects.Data.effects.Length==0&&f._garden.Snapshot.Length==0&&!f._harvest.Active&&!f._link.Active&&!f._layers.Active&&!f._surgery.Active,"clear removes all transient preparations");
        }
        for(int red=0;red<=3;red++)
        {
            var f=new HealerAdapterFixture(5);f.Configure(2,1);var ally=new Transform{id=-19,hp=0};PartyLife.members.Add(ally);
            for(int i=0;i<3;i++)f._resources.CompleteCombo(i<red?1:2);
            Check(f.Use("r2",null)&&ally.hp==90&&f._runtime.Resource(f.Job.Resource)==0,"each composition retains base healing without enemy");
            Check(red==0?f.effects.Any(e=>e.kind=="heal"):red==1?f.shielded>0:red==2?f.effects.Any(e=>e.kind=="reduction"):f.effects.Count(e=>e.kind=="cooperation")==1,"composition selects its intended extra effect");
        }
    }
}
