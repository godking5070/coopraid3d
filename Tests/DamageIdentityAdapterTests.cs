using System;
using System.Linq;
using System.Collections.Generic;
using RaidRogue;

struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
}
static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
sealed class BossController { public bool HpDamageBlocked; }
sealed class Transform
{
    public int id;public bool living=true;public Vector3 position,forward=new Vector3(0,0,1);
    public BossController boss=new BossController();
    public T GetComponent<T>() where T:class=>boss as T;
}
sealed class PlayerController
{
    public bool legal=true;public int rushes;public Vector3 destination;
    public bool PlanSkillRushToPoint(Vector3 point,float range,out Vector3 end){end=point;return legal;}
    public void BeginSkillRush(Vector3 end,float duration){destination=end;rushes++;}
}
static class CombatRange
{
    public static bool LivingEnemy(Transform t)=>t!=null&&t.living;
    public static bool InRange(Transform a,Transform b,float range)=>a!=null&&b!=null&&Vector3.Distance(a.position,b.position)<=range;
    public static float Radius(Transform t)=>.5f;
}
static class CharacterKit
{
    public static Transform[] enemies=Array.Empty<Transform>();
    public static int Id(Transform t)=>t?.id??0;
    public static Transform[] Enemies()=>enemies;
}
sealed class DamageIdentityAdapterTests
{
    sealed class Actor
    {
        public CharacterRuntimeState runtime;
        public Transform ActionSupportTarget;
        public Vector3? ActionGroundPoint;
        public bool ActionRecall;
        public DamagePrismCommand ActionPrism;
        public CharacterStateSnapshot Snapshot=>runtime.Snapshot;
        public bool HasAuxiliary(string id)=>runtime.HasAuxiliary(id);
    }
    readonly RosterJob Job;
    readonly int Route;
    readonly CharacterRuntimeState _runtime;
    readonly NineKeyDamageResources _resources;
    readonly Actor _actor;
    readonly Transform transform=new Transform{id=1},enemy=new Transform{id=9,position=new Vector3(2,0,0)},ally=new Transform{id=2};
    readonly PlayerController player=new PlayerController();
    readonly CharacterEffectState _effects=new CharacterEffectState();
    readonly DamageWeaknessState _weakness=new DamageWeaknessState();
    readonly DamageSealState _seal=new DamageSealState();
    readonly DamageGrenadeState _grenade=new DamageGrenadeState();
    readonly DamageHandsState _hands=new DamageHandsState();
    readonly DamageSnipeState _snipe;
    readonly DamageMeteorState _meteor;
    readonly DamageBurstState _burst=new DamageBurstState();
    readonly DamageSeasonState _season=new DamageSeasonState();
    readonly DamageInstantState _instant=new DamageInstantState();
    readonly DamageMemoryState _memory=new DamageMemoryState();
    readonly DamageProjectionState _projection=new DamageProjectionState();
    readonly DamageArrowState _arrows=new DamageArrowState();
    readonly DamageSpearState _spears=new DamageSpearState();
    readonly DamageGravityState _gravity=new DamageGravityState();
    readonly DamagePrismState _prism=new DamagePrismState();
    readonly DamageTempestPreparationState _tempestPreparation=new DamageTempestPreparationState();
    int _meteorTarget,marks,shields,speeds,protections;
    float Now,Power=1000,damage,stagger;
    bool Active=>true;
    bool UsesPrismInput=>Job.index==11&&Route==3;
    bool GroundAssaultActive=>Has("assault");
    void Bind(){}
    T GetComponent<T>() where T:class=>player as T;
    bool Core(int r,int v)=>_runtime.Snapshot.coreTrait==NineKeyDamageData.Core(Job,r,v);
    Transform Resolve(int id)=>new[]{transform,enemy,ally}.FirstOrDefault(t=>t.id==id);
    DamagePoint Point(Vector3 p)=>new DamagePoint(p.x,p.z);
    Vector3 World(DamagePoint p)=>new Vector3(p.x,0,p.z);
    bool CanPlace(Vector3 point)=>player.legal&&CombatRange.InRange(transform,new Transform{position=point},25);
    bool Has(string key)=>_effects.Find(key+"@1",Now)!=null;
    CharacterSkillDefinition Skill(string key)=>_runtime.Definition.Skill(NineKeyDamageData.Id(Job,key));
    void Mark(Transform t){marks++;}
    void Shield(Transform t,string key,float amount){if(t!=null)shields++;}
    void Speed(){speeds++;}
    void ReaperProtection(){protections++;}
    Transform[] Party()=>new[]{transform,ally};
    bool Attack(CharacterSkillDefinition skill,Transform target,float power,float force=0,CombatDamageKind kind=CombatDamageKind.Direct,bool launched=false,DamagePoint? field=null,float fieldRadius=0)
    {
        if(!CombatRange.LivingEnemy(target)||target.boss.HpDamageBlocked)return false;
        if(field.HasValue?Vector3.Distance(target.position,World(field.Value))>fieldRadius:!launched&&!CombatRange.InRange(transform,target,EffectiveRange(skill)))return false;
        damage+=power;stagger+=force;return true;
    }
    /* ACTUAL_IDENTITY_METHODS */
    DamageIdentityAdapterTests(int job,int route,int variant=0)
    {
        Job=CharacterRosterData.Jobs.First(j=>j.index==job);Route=route;
        _runtime=new CharacterRuntimeState(NineKeyDamageData.Build(Job));
        Check(_runtime.ChooseRoute(_runtime.Definition.routes[route-1].id,true),"choose identity route");
        _actor=new Actor{runtime=_runtime,ActionSupportTarget=ally};
        if(job==7&&route==2&&variant==1)
        {
            var third=_runtime.Definition.loadouts.First(l=>l.slot==CharacterSlot.OffGcd1);
            _runtime.ChangeLoadout(third.id,NineKeyDamageData.Id(Job,"a1"),true);
        }
        var core=_runtime.Definition.loadouts.First(l=>l.slot==0&&l.routeId==_runtime.RouteId);
        Check(_runtime.ChangeLoadout(core.id,NineKeyDamageData.Core(Job,route,variant),true),"select actual compatible core");
        _resources=new NineKeyDamageResources(_runtime,Job);
        _snipe=new DamageSnipeState(_resources);_meteor=new DamageMeteorState(_resources);
        _hands.Configure(job==8&&Core(1,1)?5:3);
        _season.Reset(job==9&&Core(3,0)?6:8);
        CharacterKit.enemies=new[]{enemy};
    }
    float Balance=>_runtime.Resource(Job.Resource);
    void Fund(float amount)=>_runtime.Gain(Job.Resource,amount);
    bool R()=>Identity(Skill("r"+Route),enemy);
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            Night();Tempest();Guns();Ranger();Astro();Mirror();
            Console.WriteLine("PASS actual eighteen-route Identity adapter: "+count+"; Unity targets, damage, support effects and rush motion are managed seams.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
    static void Night()
    {
        var a=new DamageIdentityAdapterTests(6,1);Check(!a.R()&&a.damage==0,"N1 unfunded R rejected");
        a.Fund(5);a._weakness.Grant(9,true,true,1);a.enemy.living=false;
        Check(!a.R()&&a.Balance==5&&a._weakness.Count(9)==1,"N1 invalid target preserves investment");
        a.enemy.living=true;Check(a.R()&&a.Balance==3&&a.damage==500&&a.marks==1,"N1 concentrated execution spends two and one target information");
        Check(a.R()&&a.Balance==1&&a.damage==750&&a.marks==1,"N1 uninformed repeat is basic attack without extra mark");
        var b=new DamageIdentityAdapterTests(6,2);b._resources.RecordComboUse(2);b.Fund(2);
        Check(b.R()&&b.Balance==0&&b.damage==312.5f&&b.stagger==100&&b.speeds==1,"N2 short-dance two-beat finisher applies core and role");
        Check(!b.R()&&b.speeds==1,"N2 empty repeat cannot attack");b.Fund(1);
        Check(b.R()&&b.Has("dance.reach")&&b.speeds==1,"N2 one beat grants reach but respects movement gate");
        var three=new DamageIdentityAdapterTests(6,2,1);three.Fund(3);
        Check(three.R()&&Math.Abs(three.damage-585)<.01f&&three.Balance==0,"N2 third-act three beats use enhanced budget");
        var c=new DamageIdentityAdapterTests(6,3);c.Fund(4);
        Check(c.R()&&c.Balance==2&&c.damage==80&&c._seal.Snapshot.trigger==DamageSealTrigger.CastEnd,"N3 short seal prepays core budget");
        Check(!c.R()&&c.Balance==2,"N3 active seal cannot be overwritten");
        c._seal.OnActionEnded(9,DamageSealTrigger.CastEnd,out var resolved);c.ResolveSeal(resolved);
        Check(c.damage==400&&c.shields==2,"N3 successful cast completion reconciles prepaid damage and protects party");
        c._actor.ActionRecall=true;Check(c.R()&&c._seal.Snapshot.trigger==DamageSealTrigger.PatternMoveEnd,"N3 long input chooses semantic movement condition");
        c._seal.Tick(9,out resolved);c.ResolveSeal(resolved);
        Check(c.damage==640&&c.speeds==0,"N3 expired movement seal pays only basic remainder without movement synergy");
    }
    static void Tempest()
    {
        var a=new DamageIdentityAdapterTests(7,1);a.Fund(60);
        Check(!a.R()&&a.Balance==60,"T1 requires ground point before cost");a._actor.ActionGroundPoint=new Vector3(3,0,0);a.player.legal=false;
        Check(!a.R()&&a.Balance==60,"T1 invalid rush path preserves electricity");a.player.legal=true;a._tempestPreparation.Grant(2,12);
        Check(a.R()&&a.Balance==30&&a.player.rushes==1&&a.GroundAssaultActive,"T1 repeated-assault core starts one paid ground rush");
        Check(a._effects.Find("assault@1",0).value==300&&a._tempestPreparation.Snapshot.cloud==0,"T1 release consumes cloud once into pending hit budget");
        Check(!a.R()&&a.Balance==30&&a.player.rushes==1,"T1 overlapping assault rejected");
        var b=new DamageIdentityAdapterTests(7,2);b.Fund(79);Check(!b.R()&&!b.Has("acceleration"),"T2 requires eighty");b.Fund(21);
        Check(b.R()&&b.Balance==20&&b._effects.Find("acceleration@1",0).end==12,"T2 starts finite acceleration");
        Check(!b.R()&&b.Balance==20,"T2 active acceleration cannot spend again");
        var c=new DamageIdentityAdapterTests(7,3,1);c.Fund(100);c._actor.ActionGroundPoint=new Vector3(2,0,0);
        Check(c.R()&&c.R()&&c.Balance==20&&c._spears.Snapshot.spears.All(s=>s.compressed&&s.remaining==26),"T3 compressed core places two independently paid fields");
        Check(!c.R()&&c.Balance==20,"T3 full placement does not charge or replace");
        for(int i=1;i<=3;i++)c._spears.Charge(2,i,true);c._spears.Tick(2);c._actor.ActionRecall=true;
        Check(c.R()&&c.Balance==50&&c.damage==720&&c.speeds==1,"T3 recalls compressed budgets and bounded thirty refund");
        Check(!c.R()&&c.Balance==50&&c.damage==720,"T3 empty recall cannot repeat refund or damage");
    }
    static void Guns()
    {
        var a=new DamageIdentityAdapterTests(8,1);Check(a.R()&&a._hands.Snapshot.reloading==1&&a._hands.Snapshot.remaining==1,"G1 empty hands load without F using speed core");
        Check(!a.R(),"G1 only one reload at a time");a._hands.Tick(1);
        Check(a._hands.Ammo(1)==2&&a._hands.Ammo(2)==0,"G1 speed core loads two rounds");
        a._hands.Tick(2);a._actor.ActionRecall=true;Check(a.R()&&a._hands.Snapshot.reloading==2,"G1 long input selects opposite hand");
        var b=new DamageIdentityAdapterTests(8,2,1);b.Fund(2);Check(!b.R(),"G2 insufficient ammunition rejected");b.Fund(4);
        Check(b.R()&&b.Balance==3&&b._grenade.Snapshot.remaining==8&&b._grenade.Snapshot.adjustment==3,"G2 planned-explosion core creates adjusted timer");
        Check(!b.R()&&b.Balance==3,"G2 R cannot manually detonate or overwrite");b._grenade.CompleteCombo(1,out _);
        Check(b._grenade.Snapshot.remaining==5,"G2 real combo adjustment remains connected to prepared grenade");
        var c=new DamageIdentityAdapterTests(8,3);c.Fund(6);
        Check(c.R()&&c.Balance==3&&c._resources.Snapshot.reserved==3&&c.damage==0,"G3 reserves ammunition without firing");
        Check(!c.R()&&c.Balance==3,"G3 repeated preparation cannot reserve twice");c._snipe.Tick(1);
        Check(c._snipe.Ready&&c._resources.Snapshot.reserved==0,"G3 completed aim commits cost and stores shot");c.enemy.boss.HpDamageBlocked=true;
        Check(!c.R()&&c._snipe.Snapshot.shots==3,"G3 temporarily immune target preserves prepared shots");c.enemy.boss.HpDamageBlocked=false;
        Check(c.R()&&c.R()&&c.R()&&c.damage==600&&c.marks==1&&!c._snipe.Active&&c.Balance==3,"G3 split core spends prepared three shots with one mark and no extra ammunition");
    }
    static void Ranger()
    {
        var a=new DamageIdentityAdapterTests(9,1,1);a.Fund(100);
        a._arrows.LaunchCurve(9,1,"g20",1);a._arrows.ReserveStraight(9,1,"g1",2.5f);a._arrows.Tick(1,(p,s)=>true);
        a._arrows.LaunchCurve(9,1,"g20",1);a._arrows.ReserveStraight(9,1,"g1",2.5f);a._arrows.Tick(1,(p,s)=>true);
        Check(a.R()&&a.Balance==70&&a._arrows.Snapshot.convergence==1&&a.damage==260&&a.marks==1,"S1 frequent core spends thirty and only one convergence");
        Check(a.R()&&a.R()&&a.Balance==10&&a.marks==2&&a.damage==700,"S1 zero convergence still permits base R without extra mark");
        Check(!a.R()&&a.Balance==10,"S1 insufficient resource does not launch");
        var b=new DamageIdentityAdapterTests(9,2,1);b.Fund(50);
        Check(b.R()&&b.Balance==0&&b._burst.Snapshot.bundles==2&&b._burst.Snapshot.remaining==20,"S2 pursuit core creates two finite followups");
        Check(!b.R()&&b.damage==180,"S2 input delay blocks immediate reentry");b._burst.Tick(.25f);b._burst.CompleteCombo();
        Check(b.R()&&b.damage==540&&b.Balance==0&&!b._burst.Snapshot.precise,"S2 one prepared followup consumes precision without resource");b._burst.Tick(.25f);
        Check(b.R()&&b.damage==720&&b.speeds==1&&!b._burst.Active&&!b.R(),"S2 final bundle ends set and cannot free-repeat");
        var c=new DamageIdentityAdapterTests(9,3,1);c.Fund(100);c._actor.ActionRecall=true;
        Check(c.R()&&c.Balance==80&&c._season.Snapshot.remaining==14,"S3 staying core lengthens one season");
        Check(!c.R()&&c.Balance==80,"S3 repeated same-visit delay cannot spend or extend");c._actor.ActionRecall=false;
        Check(c.R()&&c.Balance==30&&c._season.Snapshot.season==1&&c.shields==2&&!c._season.Snapshot.delayed,"S3 spring fires support then advances and clears visit delay");
    }
    static void Astro()
    {
        var a=new DamageIdentityAdapterTests(10,1);a.Fund(100);
        Check(a.R()&&a.Balance==40&&a._meteor.Snapshot.segments==5&&a._meteorTarget==9,"C1 short-sentence core reserves once and chooses five segments");
        Check(!a.R()&&a.Balance==40,"C1 casting reentry cannot restart");a._meteor.Tick(.6f,true);a._meteor.Interrupt();
        Check(a.R()&&a.Balance==40&&a._meteor.Snapshot.completed==1,"C1 resume keeps completed segment without reserving again");
        Check(a._meteor.Tick(3,true)&&a._resources.Snapshot.reserved==0&&!a._meteor.Active,"C1 completion commits one reservation");
        var b=new DamageIdentityAdapterTests(10,2);b.Fund(100);
        Check(b.R()&&b.Balance==60&&b._instant.Snapshot.rights==2&&b._instant.Snapshot.cooldown==18,"C2 distributed core creates two rights at lower cost");
        Check(b.R()&&!b._instant.Snapshot.enabled&&b.Balance==60,"C2 active R toggles free despite preparation cooldown");
        Check(!b.R()&&b.Balance==60,"C2 toggle input limit enforced");b._instant.Tick(.25f);
        Check(b.R()&&b._instant.Consume(true)&&b._instant.Consume(true)&&!b._instant.Active&&!b.R(),"C2 consumed rights cannot be replaced during cooldown");
        var c=new DamageIdentityAdapterTests(10,3);c.Fund(100);
        Check(!c.R()&&c.Balance==100,"C3 new field needs a location");c._actor.ActionGroundPoint=new Vector3(2,0,0);
        Check(c.R()&&c.Balance==40&&c._gravity.Snapshot.remaining==16,"C3 stable core extends single field");
        Check(c.R()&&c._gravity.Snapshot.expanded&&c.shields==2&&c.Balance==40,"C3 first expansion protects without repeated field cost");
        Check(c.R()&&c.R()&&c.shields==2&&c.Balance==40,"C3 mode cycling cannot generate repeated shields");
    }
    static void Mirror()
    {
        var a=new DamageIdentityAdapterTests(11,1,1);a.Fund(3);
        Check(!a.R()&&a.Balance==3,"M1 cannot replay without real records");a._memory.Record("g1",true,2,12);a._memory.Record("g20",true,2,12);
        a._actor.ActionRecall=true;Check(a.R()&&a._memory.Snapshot.locked&&a.Balance==3,"M1 long input locks without spending");a._actor.ActionRecall=false;
        Check(a.R()&&a.Balance==1&&a._memory.Snapshot.records.Length==0&&!a._memory.Snapshot.locked&&a.damage==0,"M1 split core consumes records before queued playback");
        Check(a._effects.Data.effects.Count(e=>e.kind=="memory.replay")==2&&!a.R(),"M1 two queued attacks cannot reuse removed records");
        a.Now=.6f;a.TickMemoryReplay();Check(a.damage==500&&a.marks==1&&a._effects.Data.effects.Length==0,"M1 actual delayed playback finishes with one mark");
        var b=new DamageIdentityAdapterTests(11,2,1);b.Fund(3);
        Check(b.R()&&b.Balance==2&&b._projection.Active,"M2 short input spends one for projection");b._actor.ActionRecall=true;
        Check(!b.R()&&b.Balance==2,"M2 active projection blocks execution");b._projection.Tick(11);b._projection.CompleteSecond(true);b.Fund(1);
        Check(b.R()&&b.Balance==0&&b.damage==650&&b.protections==1&&!b._projection.Snapshot.empoweredExecution,"M2 direct-finish core empowers one paid execution");
        var c=new DamageIdentityAdapterTests(11,3);c.Fund(3);
        Check(!c.R()&&c.Balance==3,"M3 requires placement command");
        c._actor.ActionPrism=new DamagePrismCommand{point=new DamagePoint(2,0),normal=new DamagePoint(0,1)};
        Check(c.R()&&c.Balance==2&&c._prism.Snapshot.mirrors.Length==1,"M3 single-focus core creates one mirror");
        int original=c._prism.Snapshot.mirrors[0].id;c._prism.Tick(2);
        c._actor.ActionPrism=new DamagePrismCommand{mirrorId=original,point=new DamagePoint(3,0),normal=new DamagePoint(1,0)};
        Check(c.R()&&c.Balance==2&&c._prism.Snapshot.mirrors[0].remaining==18,"M3 existing mirror adjustment is free without lifetime renewal");
        Check(!c.R()&&c.Balance==2,"M3 re-adjustment has a finite input gate");
        c._actor.ActionPrism=new DamagePrismCommand{point=new DamagePoint(4,0),normal=new DamagePoint(1,0)};
        Check(c.R()&&c.Balance==1&&c._prism.Snapshot.mirrors.Length==1&&c._prism.Snapshot.mirrors[0].id!=original,"M3 maximum replaces oldest only after new paid placement");
    }
}
