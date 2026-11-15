using System;
using System.Linq;
using RaidRogue;

sealed class DamagePartyPreparationTests
{
    /* ACTUAL_METEOR_COMPLETION_DELAY */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var job=CharacterRosterData.Jobs.First(j=>j.index==10);
            var runtime=new CharacterRuntimeState(NineKeyDamageData.Build(job));
            runtime.ChooseRoute(runtime.Definition.routes[0].id,true);
            runtime.Gain(job.Resource,100);
            var resources=new NineKeyDamageResources(runtime,job);
            var meteor=new DamageMeteorState(resources);
            Check(MeteorCompletionDelay(null)<0&&MeteorCompletionDelay(meteor.Snapshot)<0,"no advertised completion without a preparation");
            Check(meteor.BeginOrResume(true,60,3,1,16),"begin regular meteor");
            Check(MeteorCompletionDelay(meteor.Snapshot)==3,"initial completion includes all segments");
            meteor.Tick(1.25f,true);
            var snapshot=meteor.Snapshot;
            Check(snapshot.completed==1&&MeteorCompletionDelay(snapshot)==1.75f,"partial current segment is subtracted from all unfinished segments");
            float resource=runtime.Resource(job.Resource);
            for(int n=0;n<20;n++)MeteorCompletionDelay(snapshot);
            Check(runtime.Resource(job.Resource)==resource&&snapshot.completed==1&&snapshot.progress==.25f&&snapshot.casting,"presentation does not consume resource or alter copied preparation");
            meteor.Interrupt();
            Check(MeteorCompletionDelay(meteor.Snapshot)==2&&!meteor.Snapshot.casting,"paused estimate preserves completed segments and discards unfinished progress");
            meteor.BeginOrResume(true,60,3,1,16);meteor.Tick(2,true);
            Check(MeteorCompletionDelay(meteor.Snapshot)<0,"completed shot removes party preparation");
            runtime.Gain(job.Resource,100);meteor.BeginOrResume(true,60,5,.6f,16);
            meteor.Tick(.9f,true);
            Check(Math.Abs(MeteorCompletionDelay(meteor.Snapshot)-2.1f)<.0001f,"short sentence uses actual selected segment count and duration");
            meteor.Interrupt();meteor.Tick(20,true);
            Check(MeteorCompletionDelay(meteor.Snapshot)<0,"expired stored spell removes party preparation");
            var insufficient=new DamageMeteorSnapshot{segments=3,segmentTime=1,remaining=.5f,casting=true};
            Check(MeteorCompletionDelay(insufficient)==3&&MeteorCompletionDelay(insufficient)>insufficient.remaining,"expiration is not falsely advertised as successful completion time");
            insufficient.progress=float.NaN;
            Check(MeteorCompletionDelay(insufficient)<0,"nonfinite progress cannot render a countdown");
            Console.WriteLine("PASS actual party meteor completion estimates: "+count+"; native party-row rendering and live replication remain unverified.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
