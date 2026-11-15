using System;
using RaidRogue;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute:Attribute { public MinAttribute(float value){} }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute:Attribute { public RangeAttribute(float lo,float hi){} }
    public sealed class GameObject {}
    public sealed class AudioClip {}
    public struct Vector3
    {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
    public struct Vector2
    {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
     public static Vector2 ClampMagnitude(Vector2 value,float max)
     {float length=(float)Math.Sqrt(value.x*value.x+value.y*value.y);
      return length>max?new Vector2(value.x*max/length,value.y*max/length):value;}}
    public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);}
}
public static class JanusNormalTests
{
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok){Console.WriteLine("FAIL "+message);Environment.Exit(1);}}
    public static void Main()
    {
        var settings=new JanusNormalSettings();
        Check(settings.health==60000&&settings.postThreshold==.6f,"normal initial tuning");
        Check(settings.postPositions.Length==4,"four posts");
        foreach(var point in settings.postPositions)Check(JanusNormalRules.Inside(point,settings.arenaRadius),"post inside semicircle");
        Check(!JanusNormalRules.Inside(new UnityEngine.Vector3(0,0,1),20),"gate side excluded");
        var clamped=JanusNormalRules.Clamp(new UnityEngine.Vector3(30,0,5),19);
        Check(JanusNormalRules.Inside(clamped,19),"clamp stays playable");
        var post=new[]{-1,-1,-1,-1};var living=new[]{true,true,true,true};
        Check(JanusNormalRules.CanPost(0,0,post,living),"first actor can claim");post[0]=0;
        Check(!JanusNormalRules.CanPost(1,0,post,living),"one actor cannot claim two posts");
        Check(!JanusNormalRules.CanPost(0,1,post,living),"one post cannot claim two actors");
        post[1]=1;post[2]=2;post[3]=3;
        Check(JanusNormalRules.AllPosts(post,new[]{true,true,true,true},living),"four unique overlapping pulls");
        Check(!JanusNormalRules.AllPosts(post,new[]{true,true,false,true},living),"range exit resets overlap");
        living[2]=false;Check(!JanusNormalRules.AllPosts(post,new[]{true,true,true,true},living),"dead actor excluded");
        living[2]=true;post[3]=1;
        Check(!JanusNormalRules.AllPosts(post,new[]{true,true,true,true},living),"duplicate actor rejected");
        Check(JanusNormalRules.CounterSuccess(3,4),"different actors counter");
        Check(!JanusNormalRules.CounterSuccess(3,3),"same actor cannot counter both");
        Check(!JanusNormalRules.CounterSuccess(3,-1),"one counter not full success");
        Check(JanusNormalRules.CounterSuccess(true,true),"two counter results complete");
        Check(!JanusNormalRules.CounterSuccess(true,false),"one counter result incomplete");
        Check(!JanusNormalRules.CounterSuccess(false,false),"zero counter results incomplete");
        Check(JanusNormalRules.PostDue(false,true,.6f,.6f),"60 percent post due once");
        Check(!JanusNormalRules.PostDue(true,true,.3f,.6f),"post never repeats");
        Check(!JanusNormalRules.PostDue(false,false,0,.6f),"kill has priority over post");
        Check(JanusNormalRules.DuplicateArea(true,true,true,true,true,true),"same area event on both targets counted once");
        Check(JanusNormalRules.DuplicateArea(true,true,true,false,true,true),"target-dependent damage still one shared area event");
        Check(!JanusNormalRules.DuplicateArea(true,true,true,true,false,true),"same-target multihit allowed");
        Check(!JanusNormalRules.DuplicateArea(false,true,true,true,true,true),"later tick allowed");
        Check(!JanusNormalRules.DuplicateArea(true,true,true,true,true,false),"direct hits separate");
        Check(JanusNormalRules.AcceptPairedAreaHit(1,0),"first area hit accepted");
        Check(!JanusNormalRules.AcceptPairedAreaHit(1,1),"paired second target ignored");
        Check(JanusNormalRules.AcceptPairedAreaHit(2,1),"independent second area hit accepted");
        Console.WriteLine("PASS Janus normal: "+checks+" rule assertions.");
    }
}
