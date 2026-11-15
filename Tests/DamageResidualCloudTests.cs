using System;
using System.Linq;
using RaidRogue;
sealed class Transform { public float x,z; public bool immune; }
static class CharacterKit
{
    public static Transform target=new Transform();
    public static Transform[] Enemies()=>new[]{target};
}
sealed class Actor
{
    public CharacterStateSnapshot Snapshot=new CharacterStateSnapshot();
    public Actor RuntimeState=>this;
    public string CoreTrait=>Snapshot.coreTrait;
}
sealed class DamageResidualCloudTests
{
    readonly RosterJob Job=CharacterRosterData.Jobs.First(j=>j.index==7);
    readonly Actor _actor=new Actor();
    readonly CharacterEffectState _effects=new CharacterEffectState();
    float Now,total;
    int attempts;
    bool reenter;
    bool Core(int route,int variant)=>_actor.Snapshot.coreTrait==NineKeyDamageData.Core(Job,route,variant);
    string Key(CharacterSkillDefinition s)=>CharacterRosterData.Key(s.id);
    CharacterSkillDefinition Skill(string key)=>new CharacterSkillDefinition{id=NineKeyDamageData.Id(Job,key)};
    bool Attack(CharacterSkillDefinition s,Transform t,float power,float stagger,CombatDamageKind kind,DamagePoint? field=null,float fieldRadius=0)
    {
        attempts++;
        Check(kind==CombatDamageKind.Periodic&&stagger==0&&field.HasValue,"cloud ticks retain periodic provenance and fixed field origin");
        if(reenter){reenter=false;TickResidualClouds();}
        if(t.immune||Math.Abs(t.x-field.Value.x)>fieldRadius||Math.Abs(t.z-field.Value.z)>fieldRadius)return false;
        total+=power;return true;
    }
    /* ACTUAL_CLOUD_METHODS */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageResidualCloudTests();a._actor.Snapshot.coreTrait=NineKeyDamageData.Core(a.Job,1,1);
            float direct=a.SplitResidualCloud(a.Skill("r1"),new DamagePoint(0,0),400);
            Check(direct==280&&a._effects.Data.effects.Length==1,"R splits existing power instead of adding free damage");
            Check(a.SplitResidualCloud(a.Skill("a0"),new DamagePoint(0,0),190)==190&&a._effects.Data.effects.Length==1,"R core does not affect third skill");
            a.TickResidualClouds();Check(a.attempts==0,"no immediate free tick");
            a.Now=1;a.reenter=true;a.TickResidualClouds();Check(a.total==40,"reentrant tick cannot repeat paid damage");
            CharacterKit.target.x=20;a.Now=2;a.TickResidualClouds();Check(a.total==40,"field stays at its original position when target moves");
            CharacterKit.target.x=0;CharacterKit.target.immune=true;a.Now=3;a.TickResidualClouds();
            Check(a.total==40&&a._effects.Data.effects.Length==0,"immune last tick expires without retry");
            CharacterKit.target.immune=false;a.Now=10;a.TickResidualClouds();Check(a.attempts==3,"expired field cannot deal damage later");
            var b=new DamageResidualCloudTests();b._actor.Snapshot.coreTrait=NineKeyDamageData.Id(b.Job,"core11.a2");
            float first=b.SplitResidualCloud(b.Skill("a2"),new DamagePoint(0,0),150);
            float second=b.SplitResidualCloud(b.Skill("a2"),new DamagePoint(0,0),150);
            Check(b._effects.Data.effects.Length==2,"two paid spear charges retain independent finite clouds");
            b.Now=100;b.TickResidualClouds();
            Check(first+second+b.total==300&&b.attempts==6&&b._effects.Data.effects.Length==0,"large frame preserves two casts' finite total damage budget");
            Check(b.SplitResidualCloud(b.Skill("r1"),new DamagePoint(0,0),400)==400,"third core leaves R alone");
            Console.WriteLine("PASS actual residual cloud adapter: "+count+"; collision/world damage is a test seam.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
