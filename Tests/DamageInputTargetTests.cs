using System;
using System.Globalization;
namespace RaidRogue
{
    enum CharacterSlot { Identity,Job1,Counter,Movement }
    static class CharacterSlots { public static string Binding(Definition d,CharacterSlot s)=>s.ToString(); }
    static class Time { public static float unscaledTime; }
    static class LocalInputBindings
    { public static bool Down,Hold;public static bool Pressed(string s)=>s=="Identity"&&Down;public static bool Held(string s)=>s=="Identity"&&Hold; }
    static class CharacterDefinitionValidation { public static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v); }
    sealed class AegisRecallInput
    {
        public const float HoldSeconds=.45f;private bool _armed;private float _started;
        /* ACTUAL_GESTURE_STEP */
    }
    sealed class Transform
    {
        public int Id;public bool Alive=true,Enemy=true;public BossController Boss;
        public T GetComponent<T>() where T:class=>Boss as T;
    }
    sealed class BossController { public Transform transform; }
    static class CombatRange { public static bool LivingEnemy(Transform t)=>t!=null&&t.Alive&&t.Enemy; }
    sealed class StageManager { public static StageManager Instance=new StageManager();public BossController boss; }
    sealed class TargetingManager
    {
        public static TargetingManager Instance=new TargetingManager();
        public Transform _attackTarget,SupportTarget;public bool HasSupportSelection;
        public static bool Visible=true;static bool BossCanBeTargeted()=>Visible;
        /* ACTUAL_TARGET_RESOLVER */
    }
    struct Vector3 { public float x,z;public static Vector3 up,zero; }
    struct Ray { public Vector3 GetPoint(float distance)=>default; }
    struct Plane { public Plane(Vector3 a,Vector3 b){}public bool Raycast(Ray r,out float distance){distance=0;return false;} }
    sealed class Camera { public static Camera main;public Ray ScreenPointToRay(Vector3 p)=>default; }
    static class LocalGameplayInput { public static Vector3 MousePosition;public static bool PointerOverUI(Vector3 p)=>false; }
    sealed class CombatController { public void ReportNetworkFailure(string failure){} }
    sealed class PlayerController { public CombatController combatController=new CombatController(); }
    sealed class Definition { public string id="test"; }
    sealed class CharacterActor
    {
        public Definition Definition=new Definition();public Transform Received,Support;public bool Long;public int Calls;
        public bool Execute(CharacterSlot slot,Transform target,out string failure,Transform support,Vector3? point,bool longInput)
        {Received=target;Support=support;Long=longInput;Calls++;failure="";return true;}
    }
    static class RaidCombatAuthority
    {
        public static bool Active,Blocked,IsLocal=true;public static bool Local(CharacterActor actor)=>IsLocal;
        public static bool InputBlocked(CharacterActor actor)=>Blocked;public static int Id(Transform target)=>target?.Id??0;
    }
    sealed class RaidNetworkSession
    {
        public static RaidNetworkSession Instance=new RaidNetworkSession();public int Target;public string Rule,Payload;
        public void RequestCharacterAction(int action,string definition,int slot,int target,string rule,string payload)
        {Target=target;Rule=rule;Payload=payload;}
    }
    sealed class NineKeyDamageKit
    {
        public bool Active=true,UsesLongInput=true,UsesGround,UsesPrismInput,HasPlayer=true;
        private readonly AegisRecallInput _identityInput=new AegisRecallInput();private bool _prismPressed;private int _prismSelected;
        public CharacterActor _actor=new CharacterActor();public Transform transform=new Transform{Id=1,Enemy=false};
        public T GetComponent<T>() where T:class=>HasPlayer?new PlayerController() as T:null;
        bool CanPlace(Vector3 point)=>true;
        void UpdatePrismInput(bool blocked,string binding){}
        void HidePrismPresentation(){}
        public void Disable()=>OnDisable();public void ResetBoundary()=>ResetInput();
        /* ACTUAL_REQUEST_IDENTITY */
        /* ACTUAL_INPUT_LIFECYCLE */
    }
    static class DamageInputTargetTests
    {
        static int checks;static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
        static void Main()
        {
            var kit=new NineKeyDamageKit();var tm=TargetingManager.Instance;
            var boss=new BossController{transform=new Transform{Id=10}};boss.transform.Boss=boss;StageManager.Instance.boss=boss;
            kit.RequestIdentity();Check(kit._actor.Received==boss.transform,"cleared attack selection retains ordinary attack boss fallback");
            var enemy=new Transform{Id=20};tm._attackTarget=enemy;kit.RequestIdentity();Check(kit._actor.Received==enemy,"explicit enemy overrides fallback");
            enemy.Alive=false;kit.RequestIdentity();Check(kit._actor.Received==null,"invalid explicit enemy never silently retargets boss");
            tm._attackTarget=null;TargetingManager.Visible=false;kit.RequestIdentity();Check(kit._actor.Received==null,"hidden boss does not become fallback");
            TargetingManager.Visible=true;var ally=new Transform{Id=30,Enemy=false};tm.HasSupportSelection=true;tm.SupportTarget=ally;
            kit.RequestIdentity();Check(kit._actor.Received==boss.transform&&kit._actor.Support==ally,"support selection preserves independent fallback enemy");
            kit.RequestIdentity(true);Check(kit._actor.Long&&kit._actor.Support==ally,"long gesture preserves support target");
            RaidCombatAuthority.Active=true;kit.RequestIdentity();Check(RaidNetworkSession.Instance.Target==10&&RaidNetworkSession.Instance.Payload=="30","remote short request carries resolved boss and support");
            kit.RequestIdentity(true);Check(RaidNetworkSession.Instance.Rule=="damage.long"&&RaidNetworkSession.Instance.Target==10,"remote long request carries same resolved boss");
            TargetingManager.Instance=null;RaidCombatAuthority.Active=false;kit.RequestIdentity();Check(kit._actor.Received==null&&kit._actor.Support==kit.transform,"missing targeting component does not invent enemy");
            int before=kit._actor.Calls;RaidCombatAuthority.Blocked=true;kit.RequestIdentity();Check(kit._actor.Calls==before,"blocked gameplay cannot request identity");
            RaidCombatAuthority.Blocked=false;
            void Arm(){Time.unscaledTime=0;LocalInputBindings.Down=true;LocalInputBindings.Hold=true;kit.UpdateInput();LocalInputBindings.Down=false;}
            void Release(){Time.unscaledTime=2;LocalInputBindings.Hold=false;kit.UpdateInput();}
            foreach(string boundary in new[]{"nonlocal","no player","other route","disabled","reset","prism"})
            {
                Arm();before=kit._actor.Calls;
                if(boundary=="nonlocal"){RaidCombatAuthority.IsLocal=false;kit.UpdateInput();RaidCombatAuthority.IsLocal=true;}
                if(boundary=="no player"){kit.HasPlayer=false;kit.UpdateInput();kit.HasPlayer=true;}
                if(boundary=="other route"){kit.UsesLongInput=false;kit.UpdateInput();kit.UsesLongInput=true;}
                if(boundary=="disabled")kit.Disable();
                if(boundary=="reset")kit.ResetBoundary();
                if(boundary=="prism"){kit.UsesPrismInput=true;kit.UpdateInput();kit.UsesPrismInput=false;}
                Release();Check(kit._actor.Calls==before,boundary+" must discard previous armed R before control returns");
            }
            Arm();Time.unscaledTime=.1f;LocalInputBindings.Hold=false;kit.UpdateInput();Check(kit._actor.Calls==before+1&&!kit._actor.Long,"fresh short gesture still executes");
            Arm();Time.unscaledTime=.5f;kit.UpdateInput();Check(kit._actor.Long,"fresh held gesture still executes long");
            Console.WriteLine("PASS actual DPS identity input target routing: "+checks+"; native input remains unverified.");
        }
    }
}
