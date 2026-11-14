using System;
using RaidRogue;
/* ENUMS */
/* RULES */
enum BossFSMState { IdleChasing, Groggy }
static class RaidCombatAuthority { public static bool Replica; }
static class StageManager { public static bool CombatSuspended; }
static class AegisPaladinData { public const float CounterGroggy=2; }
static class Color { public static int yellow; }
static class Debug { public static void Log(string s){} }
static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
sealed class FloraSettings { public float gardenThreshold=.5f,recoverySeconds=2,patternInterval=3; }
sealed class FloraHardSettings { public float transformationThreshold=.4f,finalGardenThreshold=.25f,specialRecoverySeconds=4; }
sealed class CampSettings { public float forceBellRecoverySeconds=4,recoverySeconds=2,patternInterval=3; }
sealed class CampHardSettings { public float transformationThreshold=.4f,finalThreshold=.25f,attackTempo=1,specialRecoverySeconds=4; }
// World/visual cleanup is replaced. Gates, Try methods, End methods, priority
// clauses, Guardian state and milestone rules are production source extracts.
sealed class BossFixture
{
 public bool GuardianNormal=true,GuardianHard,FloraEncounter=true,FloraHard,CampanellaEncounter=true,CampanellaHard;
 public float currentHp=100,HpRatio=1,GuardianNow=10;
 public GuardianNormalState _guardian=new GuardianNormalState();public GuardianHardState _guardianHard=new GuardianHardState();
 public GuardianNormalSettings Tuning=new GuardianNormalSettings();
 public FloraSettings FloraTuning=new FloraSettings();public FloraHardSettings floraHard=new FloraHardSettings();
 public CampSettings CampTuning=new CampSettings();public CampHardSettings campanellaHard=new CampHardSettings();
 public bool _guardianGeneralStagger,_guardianCombatStarted,_floraGeneralStagger,_campGeneralStagger;
 public bool _floraTransformed,_floraCoreDone,_floraVinesIntroduced,_floraGardenDone,_floraFinalDone,_floraFinalActive,_floraCoreRecoveryPending;
 public bool _campForceUsed,_campHardForcedResolved,_campHardTransformed,_campHardFirstLockDone,_campHardFinalDone,_campHardFinalActive,_campSpecialRecoveryPending,_campHardRestProtected;
 public FloraStep _floraStep;public FloraPattern _floraPattern;public CampanellaStep _campStep;public CampanellaPattern _campPattern;
 public BossFSMState fsmState;public float _groggyRemainingTime,_castRemainingTime,_castTotalDuration,_autoAttackTimer,_floraRemaining,_floraRest,_campRemaining,_campRest;
 public string _currentCastSkillName="";public int _guardianPatternHits,notifications,cleanups,stoneClears;
 public BossFixture(){_guardian.Reset(GuardianNow,Tuning);}
 void NotifyDamageCastEnded(){if(_castTotalDuration>0)notifications++;}
 void ClearHardStoneRain(){stoneClears++;}
 void EnterGuardianGroggy(string label){ClearGuardianMechanics();fsmState=BossFSMState.Groggy;_groggyRemainingTime=_guardian.Remaining(GuardianNow);_currentCastSkillName=label;}
 void SetMechanicAlert(string s,int color,float duration){}
 void ClearGuardianMechanics(bool retain=false){cleanups++;_guardianGeneralStagger=false;}
 void CleanupActiveTelegraphAndTether(){}void FinishHardImpact(float now){}
 void ClearFloraAttack(){cleanups++;}void ClearFloraCrossGuides(){}void BeginFloraFinalReposition(){}
 float FloraHardTempo(float seconds)=>seconds;
 void ClearCampanellaAttack(){cleanups++;}
 /* METHODS */
 /* PRIORITIES */
 public void EndG()=>EndGuardian(GuardianNow);public void EndF()=>EndFlora();public void EndC()=>EndCampanella();
}
static class StaggerBossFirstTests
{
 static int checks;
 static void Check(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
 static void Main()
 {
  foreach(int boss in new[]{1,2,3})
  {
   foreach(float seconds in new[]{0f,-1,float.NaN,float.PositiveInfinity}){var x=new BossFixture();Check(!Start(x,boss,seconds),"invalid duration rejected");Check(x.cleanups==0,"invalid input has no cleanup");}
   var b=new BossFixture();RaidCombatAuthority.Replica=true;Check(!Start(b,boss,3),"replica denied");RaidCombatAuthority.Replica=false;
   StageManager.CombatSuspended=true;Check(!Start(b,boss,3),"suspended denied");StageManager.CombatSuspended=false;
   b.currentHp=0;Check(!Start(b,boss,3),"dead denied");b.currentHp=100;
   b._castTotalDuration=3;Check(Start(b,boss,3.25f),"ordinary action enters groggy");
   Check(b.fsmState==BossFSMState.Groggy&&b._groggyRemainingTime==3.25f,"requested duration enters existing FSM");
   Check(b.cleanups==1&&b.notifications==1,"world cleared and action ended once");Check(!Start(b,boss,3),"already groggy rejected");
   End(b,boss);Check(b.fsmState==BossFSMState.IdleChasing&&!Flag(b,boss),"actual End restores idle and clears general flag");
  }
  foreach(var pattern in new[]{GuardianPattern.Lockdown,GuardianPattern.Transformation,GuardianPattern.FullLockdown})
  {var b=new BossFixture{GuardianHard=true};b._guardian.Begin(pattern,10,b.Tuning);Check(!Start(b,1,3),"guardian mandatory pattern cannot interrupt");}
  foreach(float hp in new[]{.7f,.4f,.25f})
  {var b=new BossFixture{GuardianHard=true};if(hp<.7f)b._guardianHard.Next(.7f);if(hp<.4f){b._guardianHard.TryStartTransformation(.4f);b._guardianHard.FinishTransformation();}b.HpRatio=hp;Check(!Start(b,1,3),"guardian same-hit milestone HP priority");}
  foreach(var step in new[]{FloraStep.Transformation,FloraStep.RootCores,FloraStep.Reposition})
  {var b=new BossFixture{_floraStep=step};Check(!Start(b,2,3),"flora required step");}
  foreach(var pattern in new[]{FloraPattern.ParasiticVines,FloraPattern.EternalGarden})
  {var b=new BossFixture{_floraStep=FloraStep.Cast,_floraPattern=pattern};Check(!Start(b,2,3),"flora required cast");}
  var normalGarden=new BossFixture{_floraVinesIntroduced=true,HpRatio=.5f};Check(!Start(normalGarden,2,3),"normal garden same-hit HP");
  foreach(int phase in new[]{0,1,2,3})
  {var b=new BossFixture{FloraHard=true,HpRatio=phase==0?.4f:phase==3?.25f:.5f,_floraTransformed=phase>0,_floraCoreDone=phase>1,_floraVinesIntroduced=phase>1,_floraGardenDone=phase==3};Check(!Start(b,2,3),"flora transformation core first/final garden due");}
  foreach(var step in new[]{CampanellaStep.Transformation,CampanellaStep.LockPrepare,CampanellaStep.LockCounter,CampanellaStep.LockHold,CampanellaStep.LockOpening,CampanellaStep.LockRecovery,CampanellaStep.FinalReposition})
  {var b=new BossFixture{_campStep=step};Check(!Start(b,3,3),"camp required step");}
  foreach(var pattern in new[]{CampanellaPattern.SilentTuning,CampanellaPattern.ForcedBell})
  {var b=new BossFixture{_campStep=CampanellaStep.Cast,_campPattern=pattern};Check(!Start(b,3,3),"camp required cast");}
  foreach(int phase in new[]{0,1,2,3})
  {var b=new BossFixture{CampanellaHard=true,HpRatio=phase==0?.4f:phase==3?.25f:1,_campForceUsed=phase>0,_campHardForcedResolved=phase>0,_campHardTransformed=phase>1,_campHardFirstLockDone=phase==3};Check(!Start(b,3,3),"camp transformation first lock final due");}
  foreach(int boss in new[]{1,2,3})
  {
   var b=new BossFixture{GuardianHard=true,FloraHard=true,CampanellaHard=true};Check(Start(b,boss,5),"general groggy before milestone");b.HpRatio=.4f;Priority(b,boss);Check(!Flag(b,boss)&&b.fsmState==BossFSMState.IdleChasing,"pending required mechanic preempts general groggy");
   Check(boss==1?b._guardian.Due(b.GuardianNow):boss==2?b._floraRest==0:b._campRest==0,"required selector may run without rest delay");
   var counter=new BossFixture{GuardianHard=true,FloraHard=true,CampanellaHard=true,HpRatio=.4f,fsmState=BossFSMState.Groggy,_floraStep=FloraStep.Groggy,_campStep=CampanellaStep.Groggy};counter._guardian.Enter(GuardianStep.Groggy,10,2);Priority(counter,boss);Check(counter.fsmState==BossFSMState.Groggy&&counter.cleanups==0,"counter/mechanic reward groggy preserved");
  }
  Check(!Start(new BossFixture{_floraFinalActive=true},2,3),"flora final chain remains locked between steps");
  Check(!Start(new BossFixture{_campHardFinalActive=true},3,3),"camp final chain remains locked between steps");
  var finishedFlora=new BossFixture{FloraHard=true,HpRatio=.1f,_floraTransformed=true,_floraCoreDone=true,_floraVinesIntroduced=true,_floraGardenDone=true,_floraFinalDone=true,_floraPattern=FloraPattern.EternalGarden};
  Check(Start(finishedFlora,2,3),"completed flora milestones do not permanently lock rest");
  var finishedCamp=new BossFixture{CampanellaHard=true,HpRatio=.1f,_campForceUsed=true,_campHardForcedResolved=true,_campHardTransformed=true,_campHardFirstLockDone=true,_campHardFinalDone=true,_campHardRestProtected=true,_campPattern=CampanellaPattern.SilentTuning};
  Check(Start(finishedCamp,3,3),"completed tuning protected rest allows ordinary stagger");
  Console.WriteLine("First three boss stagger checks: "+checks+" PASS");
 }
 static bool Start(BossFixture b,int n,float seconds)=>n==1?b.TryGuardianStaggerGroggy(seconds):n==2?b.TryFloraStaggerGroggy(seconds):b.TryCampanellaStaggerGroggy(seconds);
 static bool Flag(BossFixture b,int n)=>n==1?b._guardianGeneralStagger:n==2?b._floraGeneralStagger:b._campGeneralStagger;
 static void End(BossFixture b,int n){if(n==1)b.EndG();else if(n==2)b.EndF();else b.EndC();}
 static void Priority(BossFixture b,int n){if(n==1)b.PriorityGuardian();else if(n==2)b.PriorityFlora();else b.PriorityCampanella();}
}
