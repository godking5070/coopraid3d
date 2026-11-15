using System;
using RaidRogue;

static class GuardianTempoTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static void Near(float value,float expected,string reason)=>Check(Math.Abs(value-expected)<.0001f,reason);
    static void Main()
    {
        var normal=new GuardianNormalSettings();normal.EnsureTempoDefaults();
        Near(normal.Duration(GuardianPattern.Sweep),2.2f/1.2f,"normal preparation is quick");
        Near(normal.Recovery(GuardianPattern.Sweep),.9f,"short ordinary recovery");
        Near(normal.Recovery(GuardianPattern.Punishment),1.2f,"short heavy recovery");
        Near(normal.stoneWaveInterval,.75f,"normal emission cadence");
        Check(normal.stoneWaves==6,"normal continuous rain length");
        Check(normal.stoneWaveInterval<normal.LockedDuration(GuardianPattern.FallingStone),"next normal circle appears before impact");
        normal.sweepPrepare=9;normal.EnsureTempoDefaults();Near(normal.sweepPrepare,9,"later authored timing preserved");
        var custom=new GuardianNormalSettings{recoverySeconds=3.7f,sweepPrepare=5.7f,tempo=1.4f};custom.EnsureTempoDefaults();
        Near(custom.recoverySeconds,3.7f,"custom recovery preserved");Near(custom.sweepPrepare,5.7f,"custom cast preserved");Near(custom.tempo,1.4f,"custom multiplier preserved");
        var hard=new GuardianHardSettings();hard.EnsureDefaults();
        Near(hard.combat.Recovery(GuardianPattern.Sweep),.75f,"hard quick recovery");
        Near(hard.lockdownEraseAt-hard.lockdownLineAt,3.3f,"escape window remains usable");
        Check(hard.stoneWaveGapSeconds<hard.stoneLockedSeconds,"hard waves overlap");
        hard.stoneWaveGapSeconds=.8f;hard.sweepSecondDelay=.77f;hard.EnsureDefaults();
        Near(hard.stoneWaveGapSeconds,.8f,"custom rain cadence preserved");Near(hard.sweepSecondDelay,.77f,"migration runs once");
        var previousNormal=new GuardianNormalSettings{tempoRevision=1,recoverySeconds=1.8f,recoverySecondsHeavy=2};previousNormal.EnsureTempoDefaults();
        Near(previousNormal.Recovery(GuardianPattern.Sweep),.9f,"revision-one scene receives shorter intervals");
        Near(previousNormal.Recovery(GuardianPattern.Punishment),1.2f,"revision-one heavy interval migrates");
        previousNormal.recoverySeconds=2.4f;previousNormal.EnsureTempoDefaults();Near(previousNormal.recoverySeconds,2.4f,"later interval edits preserved");
        var previousHard=new GuardianHardSettings{tempoRevision=1,fullRecovery=1.6f,clashRecoverySeconds=1.5f};
        previousHard.combat.recoverySeconds=1.2f;previousHard.combat.recoverySecondsHeavy=1.6f;previousHard.EnsureDefaults();
        Near(previousHard.combat.Recovery(GuardianPattern.Sweep),.75f,"hard saved interval migrates");
        Near(previousHard.combat.Recovery(GuardianPattern.Punishment),1,"hard heavy recovery");
        Near(previousHard.fullRecovery,.9f,"full lockdown short recovery");
        Near(previousHard.clashRecoverySeconds,.9f,"clash short recovery");
        Check(GuardianShieldWaveGeometry.Hits(0,0,1.6f,10),"cross intersection is one dangerous area");
        Check(GuardianShieldWaveGeometry.Hits(0,9,1.6f,10)&&GuardianShieldWaveGeometry.Hits(0,-9,1.6f,10),"front and back arms");
        Check(GuardianShieldWaveGeometry.Hits(9,0,1.6f,10)&&GuardianShieldWaveGeometry.Hits(-9,0,1.6f,10),"side arms");
        Check(!GuardianShieldWaveGeometry.Hits(3,3,1.6f,10)&&!GuardianShieldWaveGeometry.Hits(-3,-3,1.6f,10),"diagonal melee positions remain safe");
        Check(!GuardianShieldWaveGeometry.Hits(0,10.01f,1.6f,10),"cross has bounded length");
        Check(GuardianShieldWaveGeometry.Hits(1.6f,10,1.6f,10)&&!GuardianShieldWaveGeometry.Hits(1.61f,10,1.6f,10),"rectangular warning boundary");
        Check(GuardianTempo.CanHastenErase(true,4,4,3,.55f),"all four escaped: finish quickly");
        Check(!GuardianTempo.CanHastenErase(true,4,3,3,.55f),"last player still receives escape window");
        Check(!GuardianTempo.CanHastenErase(false,4,4,3,.55f),"failed breach cannot auto-complete");
        Check(!GuardianTempo.CanHastenErase(true,0,0,3,.55f),"wipe cannot complete escape");
        Check(GuardianTempo.CanHastenErase(true,2,2,3,.55f),"dead members do not hold the group outside");
        Check(!GuardianTempo.CanHastenErase(true,4,4,.3f,.55f),"do not extend an imminent impact");
        var rain=new GuardianStoneRainSchedule();rain.Start(10,.55f,8);
        Check(!rain.TryEmit(9.9f)&&rain.TryEmit(10),"first wave begins on schedule");
        Check(!rain.TryEmit(10)&&!rain.TryEmit(10.54f)&&rain.TryEmit(10.55f),"independent emission clock");
        Check(rain.TryEmit(15)&&!rain.TryEmit(15),"late host emits one wave, not a stacked backlog");
        for(int i=0;i<10;i++)rain.TryEmit(20+i);
        Check(!rain.Emitting&&rain.Emitted==8,"finite stream ends");
        rain.Start(0,.75f,6);rain.TryEmit(0);rain.Clear();
        Check(!rain.Emitting&&!rain.TryEmit(999),"wipe/retry clears queued waves");
        rain.Start(100,.75f,6);Check(rain.Emitted==0&&rain.TryEmit(100),"retry starts fresh");
        Console.WriteLine("PASS: "+checks+" guardian tempo/continuous rain checks.");
    }
}
