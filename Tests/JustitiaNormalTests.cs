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

public static class JustitiaNormalTests
{
    private static int _checks;
    private static void Check(bool valid,string message){_checks++;if(!valid)throw new Exception(message);}
    public static void Main()
    {
        int left,right;
        foreach(var pool in new[]{JustitiaNormalRules.Four,JustitiaNormalRules.Three,JustitiaNormalRules.Two})
            foreach(var mass in pool)Check(JustitiaNormalRules.HasEqualPartition(mass),"candidate has no equal partition");
        foreach(var pool in new[]{JustitiaNormalRules.Four,JustitiaNormalRules.Three,JustitiaNormalRules.Two})
            foreach(var mass in pool)
            {
                var sides=Enumerable.Repeat(JustitiaSide.Center,mass.Length).ToArray();
                var automatedAll=Enumerable.Repeat(true,mass.Length).ToArray();
                int plan=JustitiaNormalRules.BestMask(mass,sides,sides,automatedAll);
                Check(plan>0,"AI-only party has a plan");
                var assigned=Enumerable.Range(0,mass.Length).Select(i=>(plan&(1<<i))!=0?JustitiaSide.Left:JustitiaSide.Right).ToArray();
                Check(JustitiaNormalRules.Balanced(mass,assigned,out left,out right),"AI-only plan balances masses");
            }
        Check(!JustitiaNormalRules.HasEqualPartition(new[]{5,1,1,1}),"even sum alone is insufficient");
        Check(!JustitiaNormalRules.HasEqualPartition(new[]{0,1,1}),"zero mass rejected");
        Check(JustitiaNormalRules.Candidate(1,0).Length==0,"solo candidate deferred");
        Check(JustitiaNormalRules.Side(0,.65f)==JustitiaSide.Center,"middle unassigned");
        Check(JustitiaNormalRules.Side(-.66f,.65f)==JustitiaSide.Left,"left world axis");
        Check(JustitiaNormalRules.Side(.66f,.65f)==JustitiaSide.Right,"right world axis");
        Check(JustitiaNormalRules.Balanced(new[]{3,1,1,1},new[]{JustitiaSide.Left,JustitiaSide.Right,JustitiaSide.Right,JustitiaSide.Right},out left,out right)&&left==3&&right==3,"3 vs 1+1+1");
        Check(!JustitiaNormalRules.Balanced(new[]{3,1,1,1},new[]{JustitiaSide.Left,JustitiaSide.Right,JustitiaSide.Right,JustitiaSide.Center},out left,out right),"unassigned blocks success");
        Check(!JustitiaNormalRules.Balanced(new[]{1,1},new[]{JustitiaSide.Center,JustitiaSide.Center},out left,out right),"0 vs 0 blocks success");
        var masses=new[]{3,1,1,1};
        var human=new[]{JustitiaSide.Left,JustitiaSide.Center,JustitiaSide.Center,JustitiaSide.Center};
        var current=new[]{JustitiaSide.Left,JustitiaSide.Right,JustitiaSide.Left,JustitiaSide.Right};
        var automated=new[]{false,true,true,true};
        int mask=JustitiaNormalRules.BestMask(masses,human,current,automated);
        Check(mask==1,"AI respects human's left choice and stable minimum moves");
        human[0]=JustitiaSide.Right;
        mask=JustitiaNormalRules.BestMask(masses,human,current,automated);
        Check(mask==14,"AI adapts to human's right choice");
        Check(JustitiaNormalRules.Clamp(new UnityEngine.Vector3(50,2,-50),18,12,1).x==17,"rectangle x clamp");
        Console.WriteLine("PASS Justitia normal: "+_checks+" rule assertions.");
    }
}
