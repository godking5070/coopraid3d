using System;
namespace RaidRogue
{
    public enum BossRaidwideEvent { None,Begin,Hit }
    /* ACTUAL_RAIDWIDE_STATE */
    struct Vector3
    {public static Vector3 up;public static Vector3 operator +(Vector3 a,Vector3 b)=>a;public static Vector3 operator *(Vector3 a,float b)=>a;}
    struct Color { public static Color cyan; }
    static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v)); }
    sealed class Transform { public bool Alive=true;public Vector3 position;public CombatCounterTarget Meter; }
    enum AddMinionType { JanusOmega,Other }
    sealed class BossAddMinion { public AddMinionType addType;public BossController ownerBoss; }
    static class CombatRange { public static bool LivingEnemy(Transform t)=>t!=null&&t.Alive; }
    static class RaidCombatAuthority { public static bool Replica;public static float ServerTime; }
    static class StageManager { public static bool CombatSuspended; }
    static class AegisPaladinData { public const float StaggerThreshold=100,CounterGroggy=8,StaggerGroggy=8; }
    static class CharacterDefinitionValidation { public static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value); }
    static class CombatDamageRules { public static bool FinitePositive(float value)=>CharacterDefinitionValidation.Finite(value)&&value>0; }
    sealed class CounterOpportunity
    {public float End;public int Clears;public void Clear(){End=0;Clears++;}public void RestoreEnd(float end){End=end;} }
    sealed class CharacterTrainingSession
    {public static bool Active;public static CharacterTrainingSession Instance;public float Recorded;public void RecordStagger(float points){Recorded+=points;} }
    static class FloatingTextPopup { public static int Count;public static void Spawn(Vector3 p,string text,Color color,bool big,bool important=false){Count++;} }
    sealed class BossController
    {
        public Transform transform=new Transform();
        public bool IsGroggy,StaggerLocked,IsBossDefeatedCollapse,Accept=true;public int Triggers;public float StaggerThreshold=100;
        public bool TryApplyStaggerGroggy()
        {if(!Accept||StaggerLocked||IsGroggy)return false;Triggers++;IsGroggy=true;return true;}
    }
    sealed class CombatCounterTarget
    {
        public float Stagger {get;private set;}
        readonly CounterOpportunity _opportunity=new CounterOpportunity();
        public BossController Boss=new BossController();public BossAddMinion Add;public Transform transform=new Transform();
        public T GetComponent<T>() where T:class=>typeof(T)==typeof(BossController)?Boss as T:Add as T;
        public static CombatCounterTarget Ensure(Transform target)=>target.Meter;
        public void OpenForTest(){_opportunity.End=10;}
        public bool HasWindow=>_opportunity.End>0;
        /* ACTUAL_STAGGER_METHODS */
    }
    static class StaggerAdapterTests
    {
        static int checks;static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
        static void Main()
        {
            var target=new CombatCounterTarget();float threshold=AegisPaladinData.StaggerThreshold;
            target.AddStagger(30);Check(target.Stagger==30&&target.Boss.Triggers==0,"ordinary attack accumulates below threshold");
            foreach(float invalid in new[]{0f,-1,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {target.AddStagger(invalid);Check(target.Stagger==30,"invalid stagger never changes stored meter");}
            target.Boss.StaggerLocked=true;target.AddStagger(1000);Check(target.Stagger==30&&target.Boss.Triggers==0,"mandatory mechanic preserves old meter and discards incoming stagger");
            target.Boss.StaggerLocked=false;target.AddStagger(20);Check(target.Stagger==50,"after mechanic only newly accepted stagger is accumulated");
            target.OpenForTest();target.AddStagger(threshold);Check(target.Boss.Triggers==1&&target.Stagger==0&&!target.HasWindow,"threshold consumes one meter, interrupts counter window and triggers once");
            target.AddStagger(threshold*4);Check(target.Boss.Triggers==1&&target.Stagger==0,"groggy cannot build or repeatedly trigger stagger");
            target.Boss.IsGroggy=false;target.AddStagger(25);Check(target.Stagger==25,"ordinary meter resumes after groggy");
            target.transform.Alive=false;target.AddStagger(25);Check(target.Stagger==25,"dead target cannot receive stagger");target.transform.Alive=true;
            RaidCombatAuthority.Replica=true;target.AddStagger(50);Check(target.Stagger==25,"replica never adds authoritative stagger");
            target.Restore(60,9);Check(target.Stagger==60&&target.HasWindow,"replica receives meter and counter window display state");
            target.AddStagger(1000);Check(target.Stagger==60&&target.Boss.Triggers==1,"replicated meter cannot trigger client groggy");
            RaidCombatAuthority.Replica=false;target.Restore(80,8);Check(target.Stagger==60,"host ignores display restore");
            target.ResetState();Check(target.Stagger==0&&!target.HasWindow,"retry reset discards old meter and counter window");
            target.AddStagger(threshold);Check(target.Boss.Triggers==2,"fresh attempt can trigger a new threshold");
            target.Boss.IsGroggy=false;target.Boss.Accept=false;target.AddStagger(float.MaxValue);
            Check(target.Stagger==threshold&&target.Boss.Triggers==2,"failed groggy transition caps and preserves threshold without overflow");
            target.Boss.Accept=true;target.AddStagger(1);Check(target.Stagger==0&&target.Boss.Triggers==3,"next valid hit retries exactly one failed transition");
            target.Boss.IsGroggy=false;StageManager.CombatSuspended=true;target.AddStagger(20);Check(target.Stagger==0,"suspended encounter cannot farm stagger");StageManager.CombatSuspended=false;
            var clock=new BossRaidwideState();var settings=new BossRaidwideSettings{firstDelay=1,interval=5,castSeconds=2};clock.Reset(settings);
            Check(!clock.DueWithin(.5f,true,settings),"ordinary stagger remains available before mandatory raidwide is due");
            clock.Advance(.5f,true,settings);Check(clock.DueWithin(.5f,true,settings),"same-frame mandatory raidwide threshold is visible before Advance");
            Check(!clock.DueWithin(.5f,false,settings),"raidwide priority requires valid boss readiness");
            Check(clock.Advance(.5f,true,settings)==BossRaidwideEvent.Begin&&clock.Active,"due raidwide actually begins");
            Check(!clock.DueWithin(10,true,settings),"active raidwide is not a second pending cast");
            Check(clock.Advance(2,true,settings)==BossRaidwideEvent.Hit&&!clock.Active,"mandatory raidwide completes once");
            Check(!clock.DueWithin(4,true,settings)&&clock.DueWithin(5,true,settings),"next mandatory priority follows reset interval");
            settings.enabled=false;Check(!clock.DueWithin(10,true,settings),"disabled raidwide cannot lock ordinary stagger");
            var omega=new CombatCounterTarget{Boss=null,Add=new BossAddMinion{addType=AddMinionType.JanusOmega,ownerBoss=target.Boss}};
            target.Boss.transform.Meter=target;target.ResetState();omega.AddStagger(25);
            Check(target.Stagger==25&&omega.Stagger==0,"Omega forwards stagger to the shared Alpha meter exactly once");
            target.Boss.StaggerLocked=true;omega.AddStagger(25);Check(target.Stagger==25,"Omega cannot bypass owner mandatory mechanic gate");
            target.Boss.StaggerLocked=false;omega.transform.Alive=false;omega.AddStagger(25);
            Check(target.Stagger==25,"dead Omega cannot forward stagger to its living owner");
            Console.WriteLine("PASS actual shared stagger adapter: "+checks+"; boss-specific gates and native multiplayer require separate checks.");
        }
    }
}
