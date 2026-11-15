using System;
using RaidRogue;

namespace UnityEngine
{
    public sealed class MinAttribute:Attribute{public MinAttribute(float n){}}
    public sealed class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
    public sealed class TooltipAttribute:Attribute{public TooltipAttribute(string s){}}
    public class GameObject{}
    public class RuntimeAnimatorController{}
    public class AudioClip{}
    public enum KeyCode{Space,Escape}
    public struct Vector3
    {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
    public static class Mathf{public static float Clamp(float x,float min,float max)=>Math.Max(min,Math.Min(max,x));}
}
namespace RaidRogue{public sealed class RaidStageCutscenePresentation{}}

internal static class MarionetteHardTests
{
    private static int count;
    private static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    private static int Main()
    {
        var normal=new MarionetteNormalSettings();var hard=new MarionetteHardSettings();
        Check(hard.healthOverride==0&&normal.health==46000,"unset hard HP inherits current normal HP");
        Check(hard.combat!=normal,"hard combat settings are separate");
        Check(Math.Abs(MarionetteHardRules.Tempo(3.6f,1.2f)-3)<.001,"single tempo division");
        Check(hard.rescueSeconds==15&&normal.rescueSeconds==12,"rescue timer is not tempo scaled");
        Check(hard.counterWindow==1.6f,"counter window separate");
        Check(hard.transformationThreshold==.4f&&hard.climaxThreshold==.3f,"threshold order");
        Check(MarionetteHardRules.TransformDue(false,.39f,.4f),"40 percent transformation due");
        Check(!MarionetteHardRules.TransformDue(true,.2f,.4f),"transformation once");
        Check(!MarionetteHardRules.ClimaxDue(false,false,.2f,.3f),"climax waits for transformation");
        Check(MarionetteHardRules.ClimaxDue(true,false,.29f,.3f),"climax after transformation");
        Check(!MarionetteHardRules.ClimaxDue(true,true,.1f,.3f),"climax once");
        Check(MarionetteHardRules.Recordable(MarionettePattern.HalfCurtain),"half can be recorded");
        Check(MarionetteHardRules.Recordable(MarionettePattern.TragicCenter),"center can be recorded");
        foreach(var excluded in new[]{MarionettePattern.LeadExecution,MarionettePattern.AudienceApplause,
            MarionettePattern.StageCharge,MarionettePattern.TraitorRole,MarionettePattern.Climax})
            Check(!MarionetteHardRules.Recordable(excluded),"excluded memory "+excluded);
        Check(MarionetteHardRules.RecoilDue(true,true)&&!MarionetteHardRules.RecoilDue(false,true),"recoil only on successful rescue");
        Check(!MarionetteHardRules.PulseDue(9.1f,9)&&MarionetteHardRules.PulseDue(9,9),"pulse at announced time");
        Check(MarionetteHardRules.CrossingX(0,3.5f,15,false)==0,"first line starts center");
        Check(MarionetteHardRules.CrossingX(3.5f,3.5f,15,false)==13,"first line ends right inside stage");
        Check(MarionetteHardRules.CrossingX(3.5f,3.5f,15,true)==-13,"second line ends left inside stage");
        Check((15-2)/hard.crossingTravelSeconds<7.02f,"slowest base mover outruns one line with correct positioning");
        Check(2*hard.stringHealth>0&&hard.recoilWarningSeconds>=2,"two destructible strings and noninstant recoil");
        Console.WriteLine("PASS: "+count+" Marionette hard timing, threshold and geometry assertions.");return 0;
    }
}
