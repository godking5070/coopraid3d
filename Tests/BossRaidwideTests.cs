using System;
using System.Collections.Generic;
using RaidRogue;

internal static class BossRaidwideTests
{
    private static int checks;
    private static void Check(bool value,string reason)
    { checks++;if(!value)throw new Exception(reason); }
    private static void Main()
    {
        foreach(float maximum in new float[]{80000,72000,84000,92000,104000,120000})
        {
            var recovery=new BossTransformationHealthState();
            Check(recovery.TryRestore(true,true,maximum*.4f,maximum,out float health)&&health==maximum,"hard transformation restores full health");
            Check(!recovery.TryRestore(true,true,maximum*.6f,maximum,out health)&&health==maximum*.6f,"repeated completion never heals again");
            recovery.Reset();
            Check(!recovery.TryRestore(false,true,maximum*.4f,maximum,out health),"client cannot restore boss HP");
            Check(!recovery.TryRestore(true,false,maximum*.4f,maximum,out health),"normal transformation is unchanged");
            Check(!recovery.TryRestore(true,true,0,maximum,out health)&&health==0,"transformation never revives defeated boss");
            Check(recovery.TryRestore(true,true,maximum*.3f,maximum,out health)&&health==maximum,"retry reset allows one new recovery");
        }
        var settings=new BossRaidwideSettings();
        var state=new BossRaidwideState();
        var names=new HashSet<string>();
        for(int stage=1;stage<=6;stage++)
        {
            Check(names.Add(settings.Name(stage)),"each boss has its own cast name");
            state.Reset(settings);
            Check(state.Advance(14,true,settings)==BossRaidwideEvent.None,"first attack not early");
            Check(state.Advance(100,false,settings)==BossRaidwideEvent.None,"active mechanic defers attack");
            Check(state.Advance(0,true,settings)==BossRaidwideEvent.Begin,"queued attack begins at safe boundary");
            Check(state.Active&&state.Remaining==3,"three second warning");
            Check(state.Advance(2.9f,false,settings)==BossRaidwideEvent.None,"no damage before cast completes");
            Check(state.Advance(.2f,false,settings)==BossRaidwideEvent.Hit,"one hit on completion");
            Check(!state.Active&&state.Advance(0,true,settings)==BossRaidwideEvent.None,"no duplicate hit");
            Check(state.Advance(24,true,settings)==BossRaidwideEvent.None,"repeat interval measured after hit");
            Check(state.Advance(1,true,settings)==BossRaidwideEvent.Begin,"repeat attack after interval");
            state.Reset(settings);
            Check(!state.Active&&state.Advance(0,true,settings)==BossRaidwideEvent.None,"retry cancels cast and resets timer");
        }
        Check(settings.Damage(false)==350&&settings.Damage(true)==500,"normal and hard damage");
        settings.firstDelay=0;state.Reset(settings);
        Check(state.Advance(0,true,settings)==BossRaidwideEvent.Begin,"custom first delay");
        Check(state.Advance(float.NaN,true,settings)==BossRaidwideEvent.None&&state.Remaining==3,"invalid delta cannot resolve cast");
        settings.enabled=false;
        Check(state.Advance(10,true,settings)==BossRaidwideEvent.None&&!state.Active,"disabling cancels without damage");
        settings.enabled=true;settings.castSeconds=0;state.Reset(settings);
        Check(state.Advance(0,true,settings)==BossRaidwideEvent.Begin&&state.Remaining>0,"minimum warning with zero duration");
        Console.WriteLine("PASS: "+checks+" raidwide scheduling, warning, one-shot, retry and six-stage tuning assertions.");
    }
}
