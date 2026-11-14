using System;
using System.Linq;
using RaidRogue;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute:Attribute { public MinAttribute(float value){} }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute:Attribute { public RangeAttribute(float low,float high){} }
    public sealed class GameObject {}
    public sealed class RuntimeAnimatorController {}
    public sealed class AudioClip {}
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    }
    public static class Mathf { public static float Clamp(float value,float low,float high)=>Math.Min(high,Math.Max(low,value)); }
}
namespace RaidRogue { public sealed class RaidStageCutscenePresentation {} }
public static class JustitiaHardTests
{
    private static int _checks;
    private static void Check(bool valid,string message){_checks++;if(!valid)throw new Exception(message);}
    public static void Main()
    {
        var normal=new JustitiaNormalSettings();var hard=new JustitiaHardSettings();
        Check(normal.health==52000&&hard.health==normal.health,"hard HP temporary normal baseline");
        Check(normal.counterWindow==2.2f&&hard.counterWindow==1.6f,"separate counter windows");
        Check(normal.verdictStagger==460&&hard.verdictStagger==500,"separate stagger");
        Check(Math.Abs(JustitiaHardRules.Tempo(3.6f,1.2f)-3)<.001f,"single general tempo scale");
        Check(JustitiaHardRules.Due(false,false,false,.34f,.4f,.35f)==JustitiaHardSpecial.Transformation,"40 before 35");
        Check(JustitiaHardRules.Due(true,false,false,.34f,.4f,.35f)==JustitiaHardSpecial.WeightIntro,"intro before climax");
        Check(JustitiaHardRules.Due(true,true,false,.34f,.4f,.35f)==JustitiaHardSpecial.ClimaxWeight,"climax after intro");
        Check(JustitiaHardRules.Due(true,true,true,.1f,.4f,.35f)==JustitiaHardSpecial.None,"special once");
        Check(!JustitiaHardRules.ValidWeight(new[]{2,2,2,2},1,2),"unpartitionable even total rejected");
        Check(!JustitiaHardRules.ValidWeight(new[]{1,1},1,2),"both sides need a participant");
        foreach(var mass in new[]{new[]{3,1,1,1},new[]{2,2,1,1},new[]{3,2,2,1},new[]{2,1,1},new[]{3,2,1},new[]{3,1}})
        foreach(int side in new[]{-1,1})
        {
            Check(JustitiaHardRules.ValidWeight(mass,side,2),"selected candidate must be solvable");
            var none=Enumerable.Repeat(JustitiaSide.Center,mass.Length).ToArray();
            var automated=Enumerable.Repeat(true,mass.Length).ToArray();
            int mask=JustitiaHardRules.BestMask(mass,none,none,automated,side,2);
            Check(mask>0,"AI four or reduced party has a weighted plan");
            var assigned=Enumerable.Range(0,mass.Length).Select(i=>(mask&(1<<i))!=0?JustitiaSide.Left:JustitiaSide.Right).ToArray();
            Check(JustitiaHardRules.BalancedWeight(mass,assigned,side,2,out int l,out int r),"weighted plan reaches equal totals");
            Check(l+(side<0?2:0)==r+(side>0?2:0),"weight counted on marked side");
        }
        Check(!JustitiaHardRules.BalancedWeight(new[]{3,1,1,1},new[]{JustitiaSide.Center,JustitiaSide.Center,JustitiaSide.Center,JustitiaSide.Center},1,2,out _,out _),"center/zero totals rejected");
        var people=new[]{3,1,1,1};var human=new[]{JustitiaSide.Left,JustitiaSide.Center,JustitiaSide.Center,JustitiaSide.Center};
        var automated3=new[]{false,true,true,true};
        int plan=JustitiaHardRules.BestMask(people,human,human,automated3,1,2);
        Check(plan>0&&(plan&1)!=0,"AI honors a player's public side");
        Console.WriteLine("PASS Justitia hard: "+_checks+" rule assertions.");
    }
}
