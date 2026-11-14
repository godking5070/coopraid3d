using System;
using RaidRogue;

namespace UnityEngine
{
    public sealed class MinAttribute:Attribute{public MinAttribute(float n){}}
    public sealed class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
    public sealed class HeaderAttribute:Attribute{public HeaderAttribute(string n){}}
    public sealed class TooltipAttribute:Attribute{public TooltipAttribute(string n){}}
    public class GameObject{}
    public class RuntimeAnimatorController{}
    public class AudioClip{}
    public class AnimationClip{}
    public enum KeyCode{Space}
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
namespace RaidRogue
{
    public class RaidStageCutscenePresentation{}
    public class GuardianClashJobVisual
    {
        public string weaponContactPath,visualRootPath;
        public UnityEngine.Vector3 contactLocalOffset,fallbackWeaponEuler;
        public UnityEngine.AnimationClip catchClip;
    }
}
internal static class CampanellaHardTests
{
    private static int checks;
    private static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    private static int Main()
    {
        var normal=new CampanellaNormalSettings();var hard=new CampanellaHardSettings();
        Check(!ReferenceEquals(normal,hard.combat),"separate normal and hard settings");
        Check(hard.patternWeights.Length==8 && Array.TrueForAll(hard.patternWeights,w=>w>=1),"eight weighted candidates");
        Check(hard.healthOverride==0 && normal.health==42000,"normal HP fallback");
        Check(Math.Abs(CampanellaHardRules.Tempo(normal.castSeconds,hard.attackTempo)-2.916667)<.001,"cast tempo applied once");
        Check(Math.Abs(CampanellaHardRules.Tempo(normal.patternInterval,hard.attackTempo)-2.666667)<.001,"interval tempo applied once");
        Check(CampanellaHardRules.Tempo(hard.followupSeconds,hard.attackTempo)==2,"second strike initial interval");
        Check(hard.counterWindow==1.6f && normal.forceBellSeconds==10,"counter split from stagger time");
        Check(hard.echoWarningSeconds*7.02f>hard.echoRadius+1.5f,"echo exit possible without dash");
        Check(CampanellaHardRules.EchoDue(true,0) && !CampanellaHardRules.EchoDue(false,0),"echo only after success");
        Check(CampanellaHardRules.TransformDue(true,false,true),"forced bell resolved before transformation");
        Check(!CampanellaHardRules.TransformDue(false,false,true) && !CampanellaHardRules.TransformDue(true,false,false),"no early or post-kill transformation");
        Check(CampanellaHardRules.FinalDue(true,true,false,.25f,.25f),"25 percent final due");
        Check(!CampanellaHardRules.FinalDue(false,true,false,.1f,.25f) &&
              !CampanellaHardRules.FinalDue(true,false,false,.1f,.25f) &&
              !CampanellaHardRules.FinalDue(true,true,true,.1f,.25f),"final waits for transformation and introduction, one shot");
        Check(CampanellaHardRules.CounterEligible(true,true,true,2.7f,2.7f),"tank at circle edge");
        Check(!CampanellaHardRules.CounterEligible(true,true,false,1,2.7f) &&
              !CampanellaHardRules.CounterEligible(false,true,true,1,2.7f) &&
              !CampanellaHardRules.CounterEligible(true,false,true,1,2.7f),"no other-role, idle or dead counter");
        Check(hard.aegis.weaponContactPath.Contains("GreatShield") &&
              hard.berserker.weaponContactPath.Contains("Greatsword"),"job contact references");
        Check(hard.dragon.catchClip==null,"dragon pose remains authorable placeholder");
        Check(hard.lockPrepareSeconds+hard.lockCounterSeconds>hard.counterCooldownMargin-4,"real preparation is visible");
        Console.WriteLine("PASS: "+checks+" Campanella hard tuning and sequencing assertions.");return 0;
    }
}
