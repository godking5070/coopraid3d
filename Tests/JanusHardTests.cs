using System;
using RaidRogue;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute:Attribute { public MinAttribute(float value){} }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute:Attribute { public RangeAttribute(float lo,float hi){} }
    public sealed class GameObject {}
    public sealed class AudioClip {}
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
    public struct Vector2
    {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
     public static Vector2 ClampMagnitude(Vector2 value,float max)
     {float length=(float)Math.Sqrt(value.x*value.x+value.y*value.y);
      return length>max?new Vector2(value.x*max/length,value.y*max/length):value;}}
    public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);}
}
namespace RaidRogue { public sealed class RaidStageCutscenePresentation {} }
public static class JanusHardTests
{
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok){Console.WriteLine("FAIL "+message);Environment.Exit(1);}}
    public static void Main()
    {
        var normal=new JanusNormalSettings();var hard=new JanusHardSettings();
        Check(normal.health==60000&&normal.counterWindow==2.2f,"normal remains unchanged");
        Check(hard.health==60000&&hard.counterWindow==1.6f,"hard tuning distinct");
        Check(Math.Abs(JanusHardRules.Tempo(3.6f,1.2f)-3f)<.0001f,"tempo applied once");
        Check(JanusHardRules.Tempo(3f,.5f)==3f,"invalid tempo cannot lengthen attack");
        Check(hard.postSeconds>hard.postReleaseSeconds+hard.postFirstHold+hard.postFinalHold,"post phases have travel budget");
        Check(hard.chainSecondGap>=2.2f,"red chain return has movement time");
        Check(JanusNormalRules.Inside(hard.alphaCounterAlternate,hard.arenaRadius),"alternate alpha in arena");
        Check(JanusNormalRules.Inside(hard.omegaCounterAlternate,hard.arenaRadius),"alternate omega in arena");
        Check(hard.omegaCounterAlternate.x-hard.alphaCounterAlternate.x>8,"counter sides separated");
        Check(JanusHardRules.DueTransformation(false,.4f,.4f),"40 percent due");
        Check(!JanusHardRules.DueTransformation(true,.2f,.4f),"transformation once");
        Check(!JanusHardRules.DueTransformation(false,.41f,.4f),"above threshold no transformation");
        Check(JanusHardRules.IsPull(JanusHardPostPhase.FirstPull),"first pull");
        Check(!JanusHardRules.IsPull(JanusHardPostPhase.Release),"release is not pull");
        Check(JanusHardRules.IsPull(JanusHardPostPhase.FinalPull),"final pull");
        var actors=new[]{0,1,2,3};var yes=new[]{true,true,true,true};
        Check(JanusHardRules.AllPulling(actors,yes,yes,yes),"four unique holding posts");
        Check(!JanusHardRules.ReleaseComplete(actors,yes),"all holding cannot release");
        var released=new[]{false,false,false,false};
        Check(JanusHardRules.ReleaseComplete(actors,released),"all released");
        for(int i=0;i<4;i++)
        {var partial=new[]{false,false,false,false};partial[i]=true;
         Check(!JanusHardRules.ReleaseComplete(actors,partial),"one still holding blocks release "+i);}
        var partialPull=new[]{true,true,true,false};
        Check(!JanusHardRules.AllPulling(actors,yes,yes,partialPull),"three cannot complete pull");
        Check(!JanusHardRules.AllPulling(new[]{0,1,2,2},yes,yes,yes),"duplicate participant rejected");
        Check(!JanusHardRules.AllPulling(actors,new[]{true,false,true,true},yes,yes),"out of range rejected");
        Check(!JanusHardRules.AllPulling(actors,yes,new[]{true,true,false,true},yes),"dead participant rejected");
        Check(JanusHardRules.DifferentCounterActors(2,3),"two actors counter");
        Check(!JanusHardRules.DifferentCounterActors(2,2),"same actor cannot counter twice");
        Check(!JanusHardRules.DifferentCounterActors(2,-1),"one side success incomplete");
        Console.WriteLine("PASS Janus hard: "+checks+" rule assertions.");
    }
}
