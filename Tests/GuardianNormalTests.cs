using System;
using RaidRogue;
class GuardianNormalTests
{
    static int checks;
    static void Check(bool value, string reason) { checks++; if(!value)throw new Exception(reason); }
    static void Main()
    {
        var old=new GuardianNormalSettings {health=65000};old.MigrateInitialTuning();Check(old.health==30000&&old.tuningRevision==1,"old default migration");
        old.health=65000;old.MigrateInitialTuning();Check(old.health==65000,"migration once preserves later edits");
        var custom=new GuardianNormalSettings {health=42000};custom.MigrateInitialTuning();Check(custom.health==42000,"custom health preserved");
        var fresh=new GuardianNormalSettings();fresh.MigrateInitialTuning();Check(fresh.health==30000,"fresh default");
        var s=new GuardianNormalState();var c=new GuardianNormalSettings();s.Reset(0,c);
        Check(!s.Due(1.66f)&&s.Due(1.67f),"scaled initial rest");
        foreach(var expected in GuardianNormalState.Introduction){var p=s.Choose(0);Check(p==expected,"introduction");s.Begin(p,10,c);Check(s.Ends==10+c.Duration(p),"duration");s.Rest(20,c);}
        int[] counts=new int[8];var previous=GuardianPattern.Resonance;
        for(int i=0;i<10000;i++){var p=s.Choose(i);Check(p!=previous,"immediate repeat");Check(!((p==GuardianPattern.Resonance||p==GuardianPattern.Punishment)&&(previous==GuardianPattern.Resonance||previous==GuardianPattern.Punishment)),"heavy sequence");counts[Array.IndexOf(GuardianNormalState.Introduction,p)]++;previous=p;}
        foreach(int count in counts)Check(count>1000,"no pattern starved");
        Check(GuardianNormalState.Introduction.Length==8 && GuardianNormalState.Introduction[2]==GuardianPattern.ShieldPush && GuardianNormalState.Introduction[4]==GuardianPattern.LineFracture,"eight introduction order");
        Check(Math.Abs(c.Duration(GuardianPattern.ShieldPush)-3)<.0001 && Math.Abs(c.Duration(GuardianPattern.LineFracture)-2)<.0001,"new windups");
        Check(Math.Abs(c.Duration(GuardianPattern.Sweep)-4/1.2f)<.0001 && Math.Abs(c.Duration(GuardianPattern.Resonance)-5)<.0001,"old tempo");
        Check(Math.Abs(c.LockedDuration(GuardianPattern.FallingStone)-2/1.2f)<.0001 && Math.Abs(c.LockedDuration(GuardianPattern.LineFracture)-1)<.0001,"locked tempo");
        Check(c.Duration(GuardianPattern.Overload)==10 && c.Recovery(GuardianPattern.Punishment)==5.5f,"special and heavy recovery");
        s.Begin(GuardianPattern.FallingStone,10,c);Check(Math.Abs(s.Ends-12.5f)<.0001,"tracking2.5");s.Enter(GuardianStep.Locked,s.Ends,c.LockedDuration(GuardianPattern.FallingStone));Check(!s.Due(14.16f)&&s.Due(14.17f),"lock1.667");
        s.ObserveHealth(.5f);s.ObserveHealth(.1f);Check(!s.OverloadPending&&s.Step==GuardianStep.Locked,"health never queues stagger");s.Rest(15,c);Check(s.Choose(0)!=GuardianPattern.Overload,"rotation excludes overload");s.Begin(GuardianPattern.Overload,20,c);
        Check(s.Step==GuardianStep.Rest,"legacy overload request resumes normal rotation");
        Check(!s.AddStagger(9999,21,c)&&s.Stagger==0,"stagger cannot accumulate or create groggy");
        s.Begin(GuardianPattern.GateCharge,0,c);Check(!s.Counter(2)&&Math.Abs(s.Ends-4/1.2f)<.0001,"no preparation counter");s.Enter(GuardianStep.CounterWindow,s.Ends,2);Check(s.Counter(4.65f)&&s.Ends==6.65f,"counter gives short two-second groggy");Check(!s.Counter(4.66f),"single counter");
        s.Begin(GuardianPattern.GateCharge,0,c);s.Enter(GuardianStep.CounterWindow,4,2);Check(!s.Counter(6),"late counter");
        Check(GuardianNormalState.SharedDamage(1800,4)==450&&GuardianNormalState.SharedDamage(1800,3)==600,"split");
        Check(GuardianNormalState.InRectangle(2.4f,10,2.4f,10)&&!GuardianNormalState.InRectangle(2.41f,10,2.4f,10)&&!GuardianNormalState.InRectangle(0,-.01f,2.4f,10),"rectangle boundaries");
        s.ObserveHealth(.4f);s.Stop();Check(!s.OverloadPending&&s.Step==GuardianStep.Stopped,"death cancellation");s.Reset(30);Check(!s.OverloadUsed&&s.Introduced==0&&s.Choose(0)==GuardianPattern.Punishment,"retry reset");
        Console.WriteLine("PASS: "+checks+" guardian state assertions.");
    }
}
