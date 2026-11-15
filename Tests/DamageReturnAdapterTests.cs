using System;
using RaidRogue;

struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public float sqrMagnitude=>x*x+y*y+z*z;
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
}
sealed class TestTransform { public Vector3 position; }
sealed class TestBoss { public int intercepted; public void TryInterceptPlayerMovement(PlayerController p,Vector3 before,Vector3 end){intercepted++;} }
sealed class PlayerController
{
    public bool CanMoveVoluntarily=true,IsDashing,blocked;
    public TestBoss targetBoss=new TestBoss();
    public bool PlanSkillRushToPoint(Vector3 destination,float maximum,out Vector3 end){end=destination;return !blocked;}
}
static class RaidCombatAuthority { public static bool Active; }
sealed class RaidNetworkSession
{
    public static readonly RaidNetworkSession Instance=new RaidNetworkSession();
    public int commits;
    public void CommitSkillRelocation(TestTransform t){commits++;}
}
sealed class DamageReturnAdapterTests
{
    DamageReturnState _return=new DamageReturnState();
    TestTransform transform=new TestTransform();
    PlayerController player=new PlayerController();
    bool ground=true;
    T GetComponent<T>() where T:class=>player as T;
    bool CanPlace(Vector3 point)=>ground;
    DamagePoint Point(Vector3 p)=>new DamagePoint(p.x,p.z);
    Vector3 World(DamagePoint p)=>new Vector3(p.x,transform.position.y,p.z);
    /* ACTUAL_RETURN_METHOD */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageReturnAdapterTests();a.transform.position=new Vector3(2,1,3);
            Check(a.ReturnToAnchor()&&a._return.Active,"first F stores without relocating");
            Check(a.player.targetBoss.intercepted==0&&a._return.Snapshot.cooldown==16,"store starts independent cooldown without movement hit");
            a.transform.position=new Vector3(7,1,3);a.player.blocked=true;
            Check(!a.ReturnToAnchor()&&a._return.Active&&a.transform.position.x==7,"blocked return retains anchor and position");
            a.player.blocked=false;a.ground=false;
            Check(!a.ReturnToAnchor()&&a._return.Active,"lost floor rejects return");
            a.ground=true;a.player.CanMoveVoluntarily=false;
            Check(!a.ReturnToAnchor()&&a._return.Active,"movement lock rejects return");
            a.player.CanMoveVoluntarily=true;a.player.IsDashing=true;
            Check(!a.ReturnToAnchor(),"active dash rejects return");
            a.player.IsDashing=false;RaidCombatAuthority.Active=true;
            Check(a.ReturnToAnchor()&&!a._return.Active&&a.transform.position.x==2,"second F consumes and relocates once");
            Check(RaidNetworkSession.Instance.commits==1&&a.player.targetBoss.intercepted==1,"relocation commits network position and invokes boss movement rules");
            Check(!a.ReturnToAnchor()&&RaidNetworkSession.Instance.commits==1,"repeat cannot create anchor during cooldown");
            a._return.Tick(16);Check(a.ReturnToAnchor(),"cooldown expiry permits next store");
            a.transform.position=new Vector3(9,1,3);a._return.Tick(8);
            Check(!a.ReturnToAnchor()&&!a._return.Active&&a.transform.position.x==9,"expiry never returns or immediately bypasses cooldown");
            var state=new DamageReturnState();
            Check(!state.Store(new DamagePoint(float.NaN,0),8,16),"reject malformed destination");
            Check(state.Store(new DamagePoint(3,4),8,16)&&!state.Store(new DamagePoint(1,1),8,16),"stored point cannot be overwritten");
            var copy=state.Snapshot;copy.point=new DamagePoint(99,99);
            Check(state.Snapshot.point.x==3,"snapshot cannot mutate live anchor");
            Check(!state.Consume(false)&&state.Active,"rejected destination cannot consume return");
            var restored=new DamageReturnState();Check(restored.Restore(state.Snapshot)&&restored.Consume(true)&&!restored.Consume(true),"restore and one-shot consumption");
            Console.WriteLine("PASS actual return adapter and state: "+count+"; collision and network are world seams, not live play.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
