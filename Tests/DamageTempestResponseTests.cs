using System;
using System.Linq;
using System.Collections.Generic;
using RaidRogue;

sealed class Transform
{
    public int id; public bool valid=true,immune,withinSupport=true;
    public object kit;
    public CharacterActor actor=new CharacterActor();
    public T GetComponent<T>() where T:class=>kit as T??actor as T;
}
sealed class CharacterActor { public bool owns=true;public bool OwnsDirectSkillHit(string origin)=>owns; }
static class CombatRange
{
    public static bool LivingPartyMember(Transform t)=>t!=null&&t.valid;
    public static bool LivingEnemy(Transform t)=>t!=null&&t.valid;
}
static class StageManager { public static bool CombatSuspended; public static bool PlayerSkillsSuspended=>CombatSuspended; }
static class PartyLife { public static Transform[] members=Array.Empty<Transform>();public static List<Transform> OrderedMembers()=>members.ToList(); }
static class Mathf { public static int Max(int a,int b)=>Math.Max(a,b); }
sealed class CombatCounterTarget { public static CombatCounterTarget Ensure(Transform t)=>new CombatCounterTarget();public void AddStagger(float value){} }
static class CharacterKit { public static int Id(Transform t)=>t.id; }
static class RaidCombatAuthority { public static bool Replica; }
sealed class DamageTempestResponseTests
{
    CharacterEffectState _effects=new CharacterEffectState();
    RosterJob Job=CharacterRosterData.Jobs.First(j=>j.index==7);
    int Route=2;
    CharacterRuntimeState _runtime;
    Transform transform=new Transform{id=1};
    float Now,Power=1000;
    int shields;
    int followupAttempts,followupHits;
    float followupPower;
    Transform[] Party()=>PartyLife.OrderedMembers().Where(t=>t.valid&&t.withinSupport).ToArray();
    bool casterInRange=true,followupReenter;
    Transform triggeringSource;
    bool reenter;
    sealed class Actor { public HashSet<string> traits=new HashSet<string>();public bool HasAuxiliary(string id)=>traits.Contains(id); }
    Actor _actor=new Actor();
    bool Active=true;
    void Bind() { }
    bool ValidAlly(Transform t)=>t!=null&&t.valid;
    bool Has(string key)=>_effects.Find(key+"@"+CharacterKit.Id(transform),Now)!=null;
    CharacterSkillDefinition Skill(string key)=>new CharacterSkillDefinition{id=key};
    bool Attack(CharacterSkillDefinition skill,Transform target,float power,float stagger,CombatDamageKind kind,bool launched=false)
    {
        followupAttempts++;
        Check(launched&&kind==CombatDamageKind.Extra,"granted followup bypasses current caster range and remains nonrecursive extra damage");
        if(followupReenter){followupReenter=false;OnDirectAttack(triggeringSource,target,"manual",CombatDamageKind.Direct);}
        if(!CombatRange.LivingEnemy(target)||target.immune||!launched&&!casterInRange)return false;
        followupHits++;followupPower+=power;return true;
    }
    void Shield(Transform t,string key,float amount)
    {
        Check(key=="tempest.response"&&amount==30||key=="tempest.guard"&&amount==40&&t==transform,"bounded recipient protection");shields++;
        if(reenter){reenter=false;RespondToTempest(t);}
    }
    /* ACTUAL_RESPONSE_METHODS */
    DamageTempestResponseTests()
    {_runtime=new CharacterRuntimeState(NineKeyDamageData.Build(Job));_runtime.ChooseRoute(_runtime.Definition.routes[1].id,true);}
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageTempestResponseTests();var ally=new Transform{id=2};var other=new Transform{id=3};
            a.SignalTempestResponse(a.transform);a.SignalTempestResponse(null);
            Check(a._effects.Data.effects.Length==0,"self or missing support never substitutes another companion");
            a.SignalTempestResponse(ally);a.RespondToTempest(other);
            Check(a._runtime.Resource(a.Job.Resource)==0&&a.shields==0,"only selected companion responds");
            a.Now=2;a.reenter=true;a.RespondToTempest(ally);
            Check(a._runtime.Resource(a.Job.Resource)==5&&a.shields==1,"response gives exactly five and one shield even through callback reentry");
            a.RespondToTempest(ally);Check(a._runtime.Resource(a.Job.Resource)==5,"multiple direct hits or heal targets cannot reuse response");
            a.Now=12;a.SignalTempestResponse(other);a.RespondToTempest(other);
            Check(a._runtime.Resource(a.Job.Resource)==5,"gate extends twelve seconds from actual response");
            a.Now=14;a.SignalTempestResponse(other);a.Now=21;a.RespondToTempest(other);
            Check(a._runtime.Resource(a.Job.Resource)==5,"expired response has no automatic refund");
            a.Now=26;a.SignalTempestResponse(ally);ally.valid=false;a.RespondToTempest(ally);
            Check(a.shields==1,"dead or out-of-range companion cannot respond");
            ally.valid=true;a._runtime.Gain(a.Job.Resource,999);a.RespondToTempest(ally);
            Check(a._runtime.Resource(a.Job.Resource)==120&&a.shields==2,"capped income with spare capacity still provides companion protection");
            var b=new DamageTempestResponseTests();b.SignalTempestResponse(ally);b.RespondToTempest(ally);
            Check(b._runtime.Resource(b.Job.Resource)==5&&a._runtime.Resource(a.Job.Resource)==120,"two lancers own independent request and resource states");
            var c=new DamageTempestResponseTests();c.Route=1;c.SignalTempestResponse(ally);c.RespondToTempest(ally);
            Check(c._effects.Data.effects.Length==0&&c.shields==0,"other lancer route does not grant cross-combo response");
            var d=new DamageTempestResponseTests();
            Check(d.InstallationDuration(20)==20&&d.TempestMarkDuration(6)==6,"unselected duration traits leave effects unchanged");
            d._actor.traits.Add("tempest.aux.duration");d._actor.traits.Add("tempest.aux.mark");
            Check(d.InstallationDuration(20)==25&&d.InstallationDuration(6)==11&&d.TempestMarkDuration(6)==9,"distinct install and mark extensions");
            d._actor.traits.Add("tempest.aux.reach");d.MovementStarted();
            Check(d._effects.Find("tempest.reach@1",0)?.end==8&&d._runtime.Resource(d.Job.Resource)==0,"successful movement grants finite range permission without income");
            RaidCombatAuthority.Replica=true;d.Now=2;d.MovementStarted();RaidCombatAuthority.Replica=false;
            Check(d._effects.Find("tempest.reach@1",0).end==8,"replica cannot refresh movement permission");
            d._actor.traits.Add("tempest.aux.guard");d._runtime.Gain(d.Job.Resource,100);
            d.ProtectAfterRoot(true,100);d.ProtectAfterRoot(false,120);
            Check(d.shields==0,"no protection for no spend or rejected R");
            d._runtime.Spend(d.Job.Resource,40);d.ProtectAfterRoot(true,100);
            Check(d.shields==1,"successful spending R grants personal protection");
            d._runtime.Spend(d.Job.Resource,40);d.ProtectAfterRoot(true,60);
            Check(d.shields==1,"rapid spending R shares protection gate");
            d.Now=14;d._runtime.Gain(d.Job.Resource,80);d._runtime.Spend(d.Job.Resource,40);d.ProtectAfterRoot(true,100);
            Check(d.shields==2,"protection gate expires finitely");
            FollowupChecks();
            PartyBudgetChecks();
            Console.WriteLine("PASS actual Tempest response and cooperation methods: "+count+"; native damage and world checks remain unverified.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
    static void FollowupChecks()
    {
        var a=new DamageTempestResponseTests();a.Route=1;a.transform.kit=a;
        var ally=new Transform{id=2};var enemy=new Transform{id=9};
        PartyLife.members=new[]{a.transform,ally};a.casterInRange=false;
        a.Effect("coop","cooperation",ally,120,6);
        a._effects.Find("coop@2",0).skill="coop:9";
        a.triggeringSource=ally;a.followupReenter=true;
        OnDirectAttack(ally,enemy,"manual",CombatDamageKind.Direct);
        Check(a.followupHits==1&&a.followupAttempts==1,"owner retreat does not lose granted cooperation and reentry cannot duplicate permission");
        OnDirectAttack(ally,enemy,"manual",CombatDamageKind.Direct);
        Check(a.followupAttempts==1,"used companion permission cannot repeat");
        a.Effect("duet","duet",enemy,160,6);
        OnDirectAttack(ally,enemy,"manual",CombatDamageKind.Direct);
        Check(a.followupAttempts==1,"duet still requires caster and another companion");
        OnDirectAttack(a.transform,enemy,"manual",CombatDamageKind.Direct);
        Check(a.followupHits==2,"duet resolves after caster's delayed direct hit while caster is now outside original range");
        a.Effect("coop","cooperation",ally,120,6);a._effects.Find("coop@2",0).skill="coop:9";
        enemy.immune=true;OnDirectAttack(ally,enemy,"manual",CombatDamageKind.Direct);
        Check(a.followupHits==2&&a.followupAttempts==3,"immune target never receives followup damage");
        enemy.immune=false;OnDirectAttack(ally,enemy,"manual",CombatDamageKind.Direct);
        Check(a.followupAttempts==3,"failed immune followup is consumed without retry");
        a.Effect("coop","cooperation",ally,120,6);a._effects.Find("coop@2",0).skill="coop:9";
        OnDirectAttack(ally,enemy,"automatic",CombatDamageKind.Extra);
        Check(a.followupAttempts==3&&a._effects.Find("coop@2",0)!=null,"automatic extra damage cannot consume or regenerate permission");
        ally.actor.owns=false;OnDirectAttack(ally,enemy,"unowned",CombatDamageKind.Direct);
        Check(a.followupAttempts==3,"unowned direct origin cannot trigger permission");
        ally.actor.owns=true;RaidCombatAuthority.Replica=true;
        OnDirectAttack(ally,enemy,"manual",CombatDamageKind.Direct);RaidCombatAuthority.Replica=false;
        Check(a.followupAttempts==3,"replica cannot resolve mark");
        enemy.valid=false;OnDirectAttack(ally,enemy,"manual",CombatDamageKind.Direct);
        Check(a.followupAttempts==3,"dead target cannot trigger mark");
        enemy.valid=true;a.Now=7;OnDirectAttack(ally,enemy,"manual",CombatDamageKind.Direct);
        Check(a.followupAttempts==3,"expired mark cannot trigger after owner retreat");
        PartyLife.members=Array.Empty<Transform>();
    }
    static void PartyBudgetChecks()
    {
        foreach(int size in new[]{4,8})
        {
            var owner=new DamageTempestResponseTests{Route=1};owner.transform.kit=owner;
            var members=Enumerable.Range(1,size).Select(id=>id==1?owner.transform:new Transform{id=id}).ToArray();
            PartyLife.members=members;var enemy=new Transform{id=99};float budget=size==4?120:160;
            owner.Mark(enemy);
            var rights=owner._effects.Data.effects.Where(e=>e.kind=="cooperation").ToArray();
            Check(rights.Length==size-1&&rights.All(e=>e.target!=1),"four/eight person marks exclude owner and give one right per companion");
            Check(Math.Abs(rights.Sum(e=>e.value)-budget)<.001f&&rights.All(e=>e.value==Math.Floor(e.value)),"party budget remains exact after individual hit rounding");
            owner.Mark(enemy);
            Check(owner._effects.Data.effects.Count(e=>e.kind=="cooperation")==size-1,"reapplying does not stack unused rights");
            foreach(var member in members.Skip(1))OnDirectAttack(member,enemy,"manual",CombatDamageKind.Direct);
            Check(owner.followupHits==size-1&&Math.Abs(owner.followupPower-budget)<.001f,"all companions collectively spend exactly their party budget");
            foreach(var member in members.Skip(1))OnDirectAttack(member,enemy,"manual",CombatDamageKind.Direct);
            Check(owner.followupHits==size-1,"repeated attacks cannot spend other companions' already consumed rights");
            members[1].valid=false;members[2].withinSupport=false;owner.Mark(enemy);
            rights=owner._effects.Data.effects.Where(e=>e.kind=="cooperation").ToArray();
            Check(rights.Length==size-3&&rights.All(e=>e.target!=2&&e.target!=3),"dead and out-of-support-range companions receive no new permission");
            Check(Math.Abs(rights.Sum(e=>e.value)-budget)<.001f,"eligible companions share selected party-size budget without multiplying it");
            owner._effects.Clear();foreach(var member in members.Skip(1))member.valid=false;
            owner.Mark(enemy);Check(owner._effects.Data.effects.Length==0,"no eligible companion creates no self-triggered mark");
        }
        PartyLife.members=Array.Empty<Transform>();
    }
}
