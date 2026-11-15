using System;
using RaidRogue;
using UnityEngine;
public sealed class RaidStageCutscenePresentation{}
namespace UnityEngine {
 public enum KeyCode{Escape,Space}
 public sealed class MinAttribute:Attribute{public MinAttribute(float v){}}
 public sealed class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
 public sealed class HeaderAttribute:Attribute{public HeaderAttribute(string v){}}
 public sealed class TooltipAttribute:Attribute{public TooltipAttribute(string v){}}
 public class GameObject{} public class AudioClip{} public class RuntimeAnimatorController{}
 public struct Vector3{public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
 public struct Vector2{public float x,y;public Vector2(float a,float b){x=a;y=b;}public static Vector2 ClampMagnitude(Vector2 p,float r)=>p;}
 public static class Mathf{public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);public static float Clamp(float x,float a,float b)=>Math.Max(a,Math.Min(x,b));}
}
static class RaidCombatAuthority{public static bool Replica;}
static class StageManager{public static bool CombatSuspended;}
enum BossFSMState{IdleChasing,CastingSkill,Groggy}
sealed class BossFixture
{
 public bool MarionetteEncounter=true,JustitiaEncounter=true,JanusEncounter=true,MarionetteHard,JustitiaHard,JanusHard;
 public float currentHp=100,HpRatio=1;public BossFSMState fsmState=BossFSMState.CastingSkill;
 public MarionetteStep _marStep=MarionetteStep.Cast;public MarionettePattern _marPattern=MarionettePattern.LeadExecution;
 public JustitiaStep _justStep=JustitiaStep.Cast;public JustitiaPattern _justPattern=JustitiaPattern.Execution;
 public JanusStep _janusStep=JanusStep.Cast;
 public bool _marDamageStaggerGroggy,_justDamageStaggerGroggy,_janusDamageStaggerGroggy;
 public bool _marHardTransformed,_marHardClimaxStarted,_marHardClimaxPreparing,_marHardMemoryPending,_marClimaxUsed=true,_marHardRestProtected;
 public bool _justHardTransformed,_justHardIntroDone,_justHardClimaxStarted,_justVerdictUsed=true;
 public bool _janusPostUsed=true,_janusHardTransformed;
 public MarionetteNormalSettings MarTuning=new MarionetteNormalSettings();public MarionetteHardSettings marionetteHard=new MarionetteHardSettings();
 public JustitiaNormalSettings JustTuning=new JustitiaNormalSettings();public JustitiaHardSettings justitiaHard=new JustitiaHardSettings();
 public JanusNormalSettings JanusTuning=new JanusNormalSettings();public JanusHardSettings janusHard=new JanusHardSettings();
 public float _marRemaining,_marRest,_justRemaining,_justRest,_janusRemaining,_janusRest,_groggyRemainingTime,_castRemainingTime=3,_castTotalDuration=3;
 string _currentCastSkillName="cast";
 public int notifications,clears,massClears;
 void NotifyDamageCastEnded(){notifications++;}void ClearMarionetteAttack(){clears++;}void ClearJustitiaAttack(){clears++;}void ClearJustitiaMass(){massClears++;}void ClearJanusAttack(){clears++;}
 /* ACTUAL_METHODS */
 public bool Locked(int boss)=>boss==4?MarionetteStaggerLocked:boss==5?JustitiaStaggerLocked:JanusStaggerLocked;
 public bool Try(int boss,float seconds)=>boss==4?TryMarionetteStaggerGroggy(seconds):boss==5?TryJustitiaStaggerGroggy(seconds):TryJanusStaggerGroggy(seconds);
 public void Tick(int boss,float dt){if(boss==4)TickMar(dt);else if(boss==5)TickJust(dt);else TickJanus(dt);}
 public bool Groggy(int boss)=>boss==4?_marStep==MarionetteStep.Groggy:boss==5?_justStep==JustitiaStep.Groggy:_janusStep==JanusStep.Groggy;
 public bool Flag(int boss)=>boss==4?_marDamageStaggerGroggy:boss==5?_justDamageStaggerGroggy:_janusDamageStaggerGroggy;
 public float Remaining(int boss)=>boss==4?_marRemaining:boss==5?_justRemaining:_janusRemaining;
 public float Rest(int boss)=>boss==4?_marRest:boss==5?_justRest:_janusRest;
 public void Hard(int boss){if(boss==4)MarionetteHard=true;else if(boss==5)JustitiaHard=true;else JanusHard=true;}
 public void CounterGroggy(int boss){if(boss==4){_marStep=MarionetteStep.Groggy;_marRemaining=4;}else if(boss==5){_justStep=JustitiaStep.Groggy;_justRemaining=4;}else{_janusStep=JanusStep.Groggy;_janusRemaining=4;}}
}
static class StaggerBossLastTests
{
 static int checks;static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static void Main()
 {
  for(int b=4;b<=6;b++)
  {
   var x=new BossFixture();Check(!x.Locked(b),"ordinary cast permits stagger");Check(x.Try(b,2.5f),"ordinary cast enters stagger");
   Check(x.Groggy(b)&&x.Flag(b)&&x.Remaining(b)==2.5f,"actual FSM and general-stagger flag set");
   Check(x.notifications==1&&x.clears==1&&x._castRemainingTime==0&&x._castTotalDuration==0,"actual End path clears cast and notifies once");
   Check(x.Locked(b)&&!x.Try(b,5),"groggy cannot accumulate or extend itself");x.Tick(b,1);Check(x.Remaining(b)==1.5f,"actual groggy branch ticks timer");
   x.Tick(b,2);Check(!x.Groggy(b)&&!x.Flag(b)&&x.Rest(b)>0,"natural expiry returns existing recovery path");
   foreach(float seconds in new[]{0f,-1f,float.NaN,float.PositiveInfinity}){x=new BossFixture();Check(!x.Try(b,seconds)&&x.clears==0,"invalid duration no mutation");}
   x=new BossFixture();RaidCombatAuthority.Replica=true;Check(!x.Try(b,2)&&x.clears==0,"replica cannot enter stagger");RaidCombatAuthority.Replica=false;
   StageManager.CombatSuspended=true;Check(!x.Try(b,2)&&x.clears==0,"suspended combat cannot enter stagger");StageManager.CombatSuspended=false;
   x.currentHp=0;Check(x.Locked(b)&&!x.Try(b,2),"dead boss locked");
   x=new BossFixture();x.Hard(b);x.HpRatio=.1f;Check(x.Locked(b)&&!x.Try(b,2),"HP mandatory transformation outranks stagger");
   x=new BossFixture();x.Hard(b);Check(x.Try(b,4),"hard ordinary attack permits stagger before threshold");x.HpRatio=.1f;x.Tick(b,.1f);
   Check(!x.Groggy(b)&&!x.Flag(b)&&x.Rest(b)==0,"new mandatory HP condition cancels general groggy without recovery delay");
   x=new BossFixture();x.Hard(b);x.CounterGroggy(b);x.HpRatio=.1f;x.Tick(b,.1f);Check(x.Groggy(b)&&x.Remaining(b)>3,"counter groggy keeps its original timing");
  }
  foreach(MarionetteStep step in Enum.GetValues(typeof(MarionetteStep))){var x=new BossFixture{_marStep=step};bool normal=step==MarionetteStep.Rest||step==MarionetteStep.Cast||step==MarionetteStep.HardFollowup||step==MarionetteStep.HardResidue;Check(x.MarionetteStaggerLocked!=normal,"Marionette exhaustive step gate "+step);}
  foreach(var p in new[]{MarionettePattern.TraitorRole,MarionettePattern.Climax,MarionettePattern.Reenact,MarionettePattern.GoldenCrossing})Check(new BossFixture{_marPattern=p}.MarionetteStaggerLocked,"Marionette mandatory preparation locked");
  Check(new BossFixture{MarionetteHard=true,_marHardMemoryPending=true}.MarionetteStaggerLocked,"pending reenact locked");
  Check(new BossFixture{MarionetteHard=true,_marHardClimaxPreparing=true}.MarionetteStaggerLocked,"rescue-climax chain locked");
  foreach(JustitiaStep step in Enum.GetValues(typeof(JustitiaStep))){var x=new BossFixture{_justStep=step};bool normal=step==JustitiaStep.Rest||step==JustitiaStep.Cast||step==JustitiaStep.HardFollowup||step==JustitiaStep.HardResidue;Check(x.JustitiaStaggerLocked!=normal,"Justitia exhaustive step gate "+step);}
  foreach(var p in new[]{JustitiaPattern.Balance,JustitiaPattern.UnfairWeight,JustitiaPattern.Verdict})Check(new BossFixture{_justPattern=p}.JustitiaStaggerLocked,"Justitia mandatory preparation locked");
  Check(new BossFixture{JustitiaHard=true,_justHardTransformed=true}.JustitiaStaggerLocked,"post transformation weight intro locked");
  foreach(JanusStep step in Enum.GetValues(typeof(JanusStep))){var x=new BossFixture{_janusStep=step};bool normal=step==JanusStep.Rest||step==JanusStep.Cast||step==JanusStep.HardFollowup;Check(x.JanusStaggerLocked!=normal,"Janus exhaustive step gate "+step);}
  Check(new BossFixture{_janusPostUsed=false,HpRatio=.1f}.JanusStaggerLocked,"Janus posts HP threshold locked before entry");
  var mar=new BossFixture{MarionetteHard=true};mar.HpRatio=mar.marionetteHard.transformationThreshold;
  Check(mar.MarionetteStaggerLocked,"Marionette exact transformation threshold locked");mar.HpRatio+=.001f;
  Check(!mar.MarionetteStaggerLocked,"Marionette above transformation threshold permits normal combat");
  mar._marHardTransformed=true;mar.HpRatio=mar.marionetteHard.climaxThreshold;
  Check(mar.MarionetteStaggerLocked,"Marionette post-transform climax threshold locked");mar._marHardClimaxStarted=true;
  Check(!mar.MarionetteStaggerLocked,"completed mandatory chain does not permanently lock normal combat");
  var just=new BossFixture{JustitiaHard=true,_justHardTransformed=true,_justHardIntroDone=true};just.HpRatio=just.justitiaHard.climaxThreshold;
  Check(just.JustitiaStaggerLocked,"Justitia exact climax weight threshold locked");just._justHardClimaxStarted=true;
  Check(!just.JustitiaStaggerLocked,"Justitia completed climax permits normal combat");
  var janus=new BossFixture{JanusHard=true,_janusHardTransformed=true,_janusPostUsed=false};janus.HpRatio=janus.JanusTuning.postThreshold;
  Check(janus.JanusStaggerLocked,"Janus exact posts threshold locked");janus._janusPostUsed=true;
  Check(!janus.JanusStaggerLocked,"Janus completed posts and transformation permit normal combat");
  Console.WriteLine("PASS last-three boss actual stagger gates/FSM: "+checks);
 }
}
