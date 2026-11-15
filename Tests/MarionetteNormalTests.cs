using System;
using RaidRogue;

namespace UnityEngine
{
    public sealed class MinAttribute:Attribute{public MinAttribute(float n){}}
    public sealed class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
    public sealed class HeaderAttribute:Attribute{public HeaderAttribute(string s){}}
    public class GameObject{}
    public class RuntimeAnimatorController{}
    public class AudioClip{}
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    }
    public static class Mathf
    {
        public static float Clamp(float v,float min,float max)=>Math.Max(min,Math.Min(max,v));
    }
}

internal static class MarionetteNormalTests
{
    private static int checks;
    private static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    private static int Main()
    {
        var s=new MarionetteNormalSettings();
        Check(s.stageHalfSize==15,"30 x 30 initial stage");
        Check(s.climaxSeconds==12,"12 second climax");
        Check(s.rescueSeconds>0 && s.stringHealth>0,"rescue has time and destructible HP");
        Check(!MarionetteNormalRules.RescueAllowed(0) && !MarionetteNormalRules.RescueAllowed(1),"solo cannot be trapped");
        Check(MarionetteNormalRules.RescueAllowed(2) && MarionetteNormalRules.RescueAllowed(4),"survivors can rescue");
        Check(MarionetteNormalRules.ClimaxDue(false,.3f,.3f),"30% starts once");
        Check(!MarionetteNormalRules.ClimaxDue(true,.1f,.3f),"used climax never repeats");
        Check(!MarionetteNormalRules.ClimaxDue(false,.31f,.3f),"above threshold waits");
        float stagger=419;
        Check(!MarionetteNormalRules.ApplyStagger(ref stagger,-50,420)&&stagger==419,"negative stagger ignored");
        Check(MarionetteNormalRules.ApplyStagger(ref stagger,1,420)&&stagger==420,"actual stagger reaches threshold");
        var edge=MarionetteNormalRules.ClampToStage(new UnityEngine.Vector3(30,1,-30),15,1);
        Check(edge.x==14 && edge.z==-14 && edge.y==1,"square bounds preserve height");
        var inside=MarionetteNormalRules.ClampToStage(new UnityEngine.Vector3(12,1,-11),15,1);
        Check(inside.x==12 && inside.z==-11,"inside stage is unchanged");
        Check(s.chargeLength/2+2<s.stageHalfSize,"charge line leaves side space");
        Check(s.centerRadius+2<s.stageHalfSize-1,"center dodge reachable");
        Check(MarionetteDollsText.Line(MarionetteDollsVoice.Analytical).Contains("전멸의 기록"),"investigation branch");
        Console.WriteLine("PASS: "+checks+" Marionette normal rule and geometry assertions.");
        return 0;
    }
}
