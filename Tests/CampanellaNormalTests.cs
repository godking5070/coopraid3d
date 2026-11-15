using System;
using RaidRogue;

namespace UnityEngine
{
    public sealed class MinAttribute:Attribute{public MinAttribute(float n){}}
    public sealed class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
    public class GameObject{}
    public class RuntimeAnimatorController{}
    public class AudioClip{}
    public struct Vector2
    {
        public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
        public float sqrMagnitude=>x*x+y*y;
    }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    }
}
internal static class CampanellaNormalTests
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static int Main()
    {
        var config=new CampanellaNormalSettings();
        Check(config.platePoints.Length==4,"four plate positions");
        for(int i=0;i<4;i++)
        {
            var p=config.platePoints[i];
            Check(Math.Sqrt(p.x*p.x+p.z*p.z)+config.plateRadius<config.arenaRadius,"plate inside arena "+i);
            for(int j=0;j<i;j++)
            {var q=config.platePoints[j];Check((p.x-q.x)*(p.x-q.x)+(p.z-q.z)*(p.z-q.z)>config.plateRadius*config.plateRadius*4,"plates separated");}
        }
        Check(CampanellaNormalRules.InPlate(new UnityEngine.Vector3(-9,0,9),config.platePoints[0],config.plateRadius),"prepositioned occupant checked");
        Check(!CampanellaNormalRules.InPlate(new UnityEngine.Vector3(-6.59f,0,9),config.platePoints[0],config.plateRadius),"outside plate radius");
        Check(CampanellaNormalRules.CheckPlate(0,0,true,false)==CampanellaPlateResult.Success,"correct actor succeeds");
        Check(CampanellaNormalRules.CheckPlate(0,0,true,true)==CampanellaPlateResult.Success,"success wins at deadline");
        Check(CampanellaNormalRules.CheckPlate(0,1,true,false)==CampanellaPlateResult.WrongActor,"wrong actor not credited");
        Check(CampanellaNormalRules.CheckPlate(0,1,true,true)==CampanellaPlateResult.Failed,"wrong actor times out");
        Check(CampanellaNormalRules.CheckPlate(0,-1,false,false)==CampanellaPlateResult.Pending,"dead assignee waits for deadline");
        Check(CampanellaNormalRules.CheckPlate(0,-1,false,true)==CampanellaPlateResult.Failed,"dead assignee fails once at deadline");
        Check(CampanellaNormalRules.CheckPlate(-1,-1,false,false)==CampanellaPlateResult.Missing,"unassigned skips");
        Check(CampanellaNormalRules.NextStep(0)==1 && CampanellaNormalRules.NextStep(3)==4 && CampanellaNormalRules.NextStep(4)==4,"ordered steps bounded");
        Check(CampanellaNormalRules.ForceBellDue(false,.4f,.4f),"40 percent trigger");
        Check(!CampanellaNormalRules.ForceBellDue(true,.1f,.4f),"one shot trigger");
        float stagger=349;
        Check(!CampanellaNormalRules.ApplyStagger(ref stagger,-50,350) && stagger==349,"negative stagger ignored");
        Check(CampanellaNormalRules.ApplyStagger(ref stagger,1,350) && stagger==350,"actual stagger threshold");
        Check(7.02f*config.tuningPrepareSeconds>26,"slowest base mover can cross arena between plates without dash");
        Check(CampanellaCloakText.Line(CampanellaCloakVoice.Gentle).Contains("망토마다"),"cloak branch");
        Console.WriteLine("PASS: "+checks+" Campanella normal rule and geometry assertions.");
        return 0;
    }
}
