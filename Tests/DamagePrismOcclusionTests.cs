using System;
using RaidRogue;
struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 up=>new Vector3(0,1,0);
    public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator /(Vector3 a,float n)=>new Vector3(a.x/n,a.y/n,a.z/n);
}
class PartyLife {} class BossController {} class BossAddMinion {}
class Collider
{
    public object owner;
    public T GetComponentInParent<T>() where T:class=>owner as T;
}
struct RaycastHit { public Collider collider;public float distance; }
enum QueryTriggerInteraction { Ignore }
static class Physics
{
    public static RaycastHit[] hits=Array.Empty<RaycastHit>();
    public static int calls;public static Vector3 origin,direction;public static float range;
    public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float r,int mask,QueryTriggerInteraction triggers)
    {calls++;origin=o;direction=d;range=r;return hits;}
}
sealed class DamagePrismOcclusionTests
{
    DamagePrismState _prism=new DamagePrismState();
    bool UsesPrismInput=true,groundAllowed=true;
    int shards=3;
    void Bind(){}
    bool CanPlace(Vector3 point)=>groundAllowed;
    bool Core(int route,int choice)=>false;
    bool Spend(int cost){if(shards<cost)return false;shards-=cost;return true;}
    private Vector3 World(DamagePoint p)=>new Vector3(p.x,2,p.z);
    /* ACTUAL_PRISM_OBSTRUCTION */
    /* ACTUAL_PRISM_INPUT */
    static int count;
    static void Check(bool valid,string why){count++;if(!valid)throw new Exception(why);}
    static RaycastHit Hit(float distance,object owner=null)=>new RaycastHit{distance=distance,collider=new Collider{owner=owner}};
    static void Main()
    {
        try
        {
            var adapter=new DamagePrismOcclusionTests();
            Physics.hits=new[]{Hit(4),Hit(.1f,new PartyLife()),Hit(.2f,new BossController()),Hit(.3f,new BossAddMinion()),Hit(2)};
            Check(adapter.PrismObstruction(default,new DamagePoint(5,0))==2,"nearest terrain wins despite unsorted hits and closer combatants");
            Check(Physics.origin.y==3&&Physics.direction.x==1&&Physics.range==5,"horizontal beam ray uses actor elevation plus one and segment length");
            Physics.hits=new[]{Hit(.1f,new PartyLife()),Hit(.2f,new BossController()),Hit(.3f,new BossAddMinion())};
            Check(!adapter.PrismObstruction(default,new DamagePoint(5,0)).HasValue,"party boss and adds never become terrain occluders");
            int calls=Physics.calls;
            Check(!adapter.PrismObstruction(default,default).HasValue&&Physics.calls==calls,"zero length segment performs no invalid raycast");
            Physics.hits=Array.Empty<RaycastHit>();
            Check(!adapter.PrismObstruction(default,new DamagePoint(0,5)).HasValue&&Physics.direction.z==1,"empty world keeps ray unobstructed in either direction");
            Physics.hits=new[]{Hit(.5f)};
            var prism=new DamagePrismState();prism.Place(new DamagePoint(2,0),new DamagePoint(-1,1),2,12);
            var clipped=DamagePrismState.Clip(prism.Trace(default,8),adapter.PrismObstruction);
            Check(clipped.Length==2&&!DamagePrismState.Hits(clipped,new DamagePoint(2,2),.1f),"actual adapter collision terminates incoming beam before reflection");
            var command=new DamagePrismCommand{point=new DamagePoint(2,0),normal=new DamagePoint(0,1)};
            Check(adapter.UsePrism(command)&&adapter.shards==2,"new oriented mirror spends exactly one shard");
            adapter._prism.Tick(3);command.mirrorId=1;command.point=new DamagePoint(4,2);command.normal=new DamagePoint(1,0);adapter.shards=0;
            Check(adapter.UsePrism(command)&&adapter.shards==0,"existing mirror adjusts with empty resource");
            Check(adapter._prism.Snapshot.mirrors[0].remaining==17&&adapter._prism.Snapshot.mirrors[0].point.x==4&&adapter._prism.Snapshot.mirrors[0].normal.x==1,"adjust changes position and normal without renewing lifetime");
            Check(!adapter.UsePrism(command),"adjustment cooldown rejects repeated command");
            adapter._prism.Tick(.25f);command.mirrorId=77;
            Check(!adapter.UsePrism(command)&&adapter._prism.Snapshot.mirrors.Length==1,"foreign or expired mirror id does not become free placement");
            command.mirrorId=1;adapter.groundAllowed=false;
            Check(!adapter.UsePrism(command),"host rejects illegal ground even on free adjustment");
            adapter.groundAllowed=true;adapter.UsesPrismInput=false;
            Check(!adapter.UsePrism(command),"other routes reject prism commands");
            adapter.UsesPrismInput=true;command.mirrorId=0;
            Check(!adapter.UsePrism(command)&&adapter._prism.Snapshot.mirrors.Length==1,"new placement with empty shards cannot replace existing mirror");
            adapter.shards=2;
            Check(adapter.UsePrism(command)&&adapter.shards==1,"second placement spends one shard");
            command.mirrorId=2;
            Check(adapter.UsePrism(command)&&adapter._prism.Snapshot.firstId==2&&adapter.shards==1,"selecting existing mirror chooses first reflection without resource cost");
            Console.WriteLine("PASS actual prism obstruction adapter: "+count+"; Physics hits are managed seams, scene geometry remains unverified.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
