using System;
using System.Linq;
using RaidRogue;

// The production completion method is injected by the runner. Only the world
// shield sink is substituted; resource and projection states are production code.
sealed class DamageReaperAdapterTests
{
    readonly RosterJob job=CharacterRosterData.Jobs.First(j=>j.index==11);
    CharacterRuntimeState actor;
    NineKeyDamageResources _resources;
    DamageProjectionState _projection=new DamageProjectionState();
    int protections;
    int allyProtections;
    bool gate;
    object transform=new object();
    float Power=>1000;
    sealed class Support { public object ActionSupportTarget; }
    Support _actor=new Support();
    static int assertions;
    bool Has(string key)=>key=="reaper.protection.gate"&&gate;
    void Shield(object target,string key,float value)
    {
        Check(key=="reaper"&&value==30,"prototype shield budget");
        if(target==transform)protections++;else if(target!=null)allyProtections++;
    }
    void Effect(string key,string kind,object target,float value,float duration)
    {
        Check(key=="reaper.protection.gate"&&kind=="gate"&&target==transform&&duration==6,"shared finite protection gate");gate=true;
    }
    /* ACTUAL_REAPER_PROTECTION */
    /* ACTUAL_REAPER_COMPLETION */
    static void Check(bool value,string why){assertions++;if(!value)throw new Exception(why);}
    DamageReaperAdapterTests()
    {
        actor=new CharacterRuntimeState(NineKeyDamageData.Build(job));
        actor.ChooseRoute(actor.Definition.routes[1].id,true);
        _resources=new NineKeyDamageResources(actor,job);
    }
    void Project()
    {
        Check(_projection.Begin(1)&&_projection.LaunchSecond(),"prepare a paid projected step");
        _resources.MarkProjectedSecond();
    }
    static void Main()
    {
        try
        {
            var missed=new DamageReaperAdapterTests();missed.Project();
            missed._projection.Tick(2); // expiry does not erase paid-combo history
            Check(missed._projection.Snapshot.usedInCombo&&missed._resources.Snapshot.projectedSecond,"expiry preserves both provenance states");
            missed.FinishReaperCombo(false);
            Check(!missed._resources.Snapshot.projectedSecond&&!missed._projection.Snapshot.usedInCombo,"missed last step clears both histories");
            Check(missed.actor.Resource(missed.job.Resource)==0&&missed.actor.Resource(NineKeyDamageData.Gauge(missed.job))==0&&missed.protections==0,"miss grants no resource, gauge or shield");
            missed._resources.DirectCombo(2,3,0);missed.FinishReaperCombo(true);
            Check(missed.actor.Resource(missed.job.Resource)==1&&missed._projection.Snapshot.empoweredExecution,"next direct finisher gains a shard and execution preparation");
            Check(missed.protections==0,"direct second completion is not projection protection");
            var landed=new DamageReaperAdapterTests();landed.Project();landed._projection.Tick(2);
            landed._resources.DirectCombo(2,3,0);landed.FinishReaperCombo(true);
            Check(landed.actor.Resource(landed.job.Resource)==0&&!landed._projection.Snapshot.empoweredExecution,"expired projection finishing in melee still cannot refund shard or prepare execution");
            Check(landed.protections==1&&!landed._projection.Active,"valid projected combo completion provides protection once");
            landed._actor.ActionSupportTarget=new object();landed.ReaperProtection();
            Check(landed.protections==1&&landed.allyProtections==0,"execution uses same gate as projected completion");
            landed.gate=false;landed.ReaperProtection();
            Check(landed.protections==2&&landed.allyProtections==1,"after gate expiry both self and valid companion receive protection");
            landed.gate=false;landed._actor.ActionSupportTarget=landed.transform;landed.ReaperProtection();
            Check(landed.protections==3&&landed.allyProtections==1,"self selection never duplicates protection");
            landed.FinishReaperCombo(false);
            Check(landed.protections==3,"empty cleanup does not repeat projection protection");
            Console.WriteLine("PASS actual Reaper completion adapter: "+assertions);
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
