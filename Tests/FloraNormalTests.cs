using System;
using RaidRogue;

// Small Unity value stubs let the actual encounter rules run in a command-line test.
namespace UnityEngine
{
    public sealed class MinAttribute : Attribute { public MinAttribute(float n) {} }
    public sealed class RangeAttribute : Attribute { public RangeAttribute(float a,float b) {} }
    public class GameObject {}
    public class RuntimeAnimatorController {}
    public class AudioClip {}
    public struct Vector2
    {
        public float x,y;
        public Vector2(float x,float y){this.x=x;this.y=y;}
        public float sqrMagnitude=>x*x+y*y;
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
}

public static class FloraNormalTests
{
    private static int _checks;
    private static void Check(bool condition,string name)
    { _checks++; if(!condition)throw new Exception(name); }
    public static int Main()
    {
        var a=new UnityEngine.Vector3(0,0,0);
        var b=new UnityEngine.Vector3(12,0,0);
        Check(FloraNormalRules.PairCount(4)==2,"four survivors make two pairs");
        Check(FloraNormalRules.PairCount(3)==1,"odd survivor does not block");
        Check(FloraNormalRules.PairCount(1)==0,"lone survivor unpaired");
        Check(FloraNormalRules.VineBroken(a,b,12),"vine breaks at exact threshold");
        Check(!FloraNormalRules.VineBroken(a,new UnityEngine.Vector3(11.99f,0,0),12),"vine still attached below threshold");
        Check(FloraNormalRules.ResolveVine(true,true,false,false)==FloraVineOutcome.Pending,"vine waits while moving");
        Check(FloraNormalRules.ResolveVine(true,true,true,true)==FloraVineOutcome.Broken,"break wins at deadline");
        Check(FloraNormalRules.ResolveVine(false,true,false,true)==FloraVineOutcome.Cancelled,"death cancels without transfer");
        Check(FloraNormalRules.ResolveVine(true,true,false,true)==FloraVineOutcome.TimedOut,"timeout affects unresolved pair");
        Check(FloraNormalRules.GardenDue(true,false,.5f,.5f),"garden queued on threshold");
        Check(!FloraNormalRules.GardenDue(false,false,.4f,.5f),"vine must be introduced");
        Check(!FloraNormalRules.GardenDue(true,true,.4f,.5f),"garden once only");
        bool seed=false;
        Check(FloraNormalRules.ActivateOnce(ref seed),"first seed activation");
        Check(!FloraNormalRules.ActivateOnce(ref seed),"simultaneous second activation rejected");
        Check(FloraNormalRules.Safe(a,a,true,b,false,5),"one open seed protects party");
        Check(!FloraNormalRules.Safe(a,a,false,b,false,5),"closed seeds do not protect");
        Check(!FloraNormalRules.Safe(new UnityEngine.Vector3(6,0,0),a,true,b,false,5),"outside safe radius damaged");
        Console.WriteLine("PASS: "+_checks+" Flora normal rule assertions.");
        return 0;
    }
}
