using System;
using System.Linq;
using RaidRogue;

sealed class Transform { public int id=9; public bool alive=true, immune;public DamagePoint position; }
static class CharacterKit {public static Transform[] targets=Array.Empty<Transform>();public static Transform[] Enemies()=>targets;}
static class CombatRange {public static float Radius(Transform t)=>.5f;public static bool LivingEnemy(Transform t)=>t!=null&&t.alive;}
sealed class DamageArrowAdapterTests
{
    readonly DamageArrowState _arrows=new DamageArrowState();
    readonly Transform target=new Transform();
    readonly RosterJob Job=CharacterRosterData.Jobs.First(j=>j.index==9);
    float staggerApplied;
    int attempts,hits,completions,finishers,extras;
    bool inRange=true,reenter;
    bool Enemy(Transform t)=>CombatRange.LivingEnemy(t)&&inRange;
    DamagePoint Point(DamagePoint p)=>p;
    CharacterSkillDefinition Skill(string key)=>new CharacterSkillDefinition{id=key};
    string Key(CharacterSkillDefinition s)=>s.id;
    Transform Resolve(int id)=>id==target.id?target:null;
    bool Attack(CharacterSkillDefinition s,Transform t,float damage,float stagger=0,
        CombatDamageKind kind=CombatDamageKind.Direct,bool deferred=false,bool launched=false)
    {
        attempts++;
        if(reenter){reenter=false;TickArrows(1);}
        if(t==null||!t.alive||t.immune||(!launched&&!inRange))return false;
        hits++;
        staggerApplied+=stagger;
        if(kind==CombatDamageKind.Extra)extras++;
        else Check(deferred,"queued manual arrow retains direct-input proof");
        return true;
    }
    void CompleteDirect(CharacterSkillDefinition s,Transform t,int chain,int step,int choice)
    {
        completions++;
        if(step==3)finishers++;
        Check(chain==1?s.id=="g1":s.id=="g2"+choice,"saved skill and chain survive later inputs");
    }
    /* ACTUAL_ARROW_METHOD */
    void Pair(int curve=3,int straight=3)
    {
        Check(_arrows.LaunchCurve(target.id,curve,"g22",4),"curve launches");
        TickArrows(2);
        Check(_arrows.ReserveStraight(target.id,straight,"g1",2.5f),"straight reserves");
    }
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageArrowAdapterTests();a.Pair();a.inRange=false;a.reenter=true;a.TickArrows(2);
            Check(a.hits==3&&a.extras==1&&a.finishers==2&&a._arrows.Snapshot.convergence==1,
                "moving outside launch range preserves both impacts, two finishers and one convergence bonus");
            a.TickArrows(100);Check(a.attempts==3&&a.completions==2,"callbacks and later ticks cannot repeat spent arrivals");
            var b=new DamageArrowAdapterTests();b.Pair();b.target.immune=true;b.TickArrows(2);
            Check(b.hits==0&&b.completions==0&&b._arrows.Snapshot.convergence==0,"immunity prevents resources and convergence");
            b.target.immune=false;b.TickArrows(10);Check(b.attempts==2,"immune impacts are not retried");
            var c=new DamageArrowAdapterTests();c.Pair();c.target.alive=false;c.TickArrows(2);
            Check(c.hits==0&&c.completions==0&&c._arrows.Snapshot.pending.Length==0,"dead target consumes arrivals without rewards");
            var d=new DamageArrowAdapterTests();d.Pair(1,2);d.inRange=false;d.TickArrows(2);
            Check(d.completions==2&&d.finishers==0&&d.extras==1,"nonfinishers retain saved steps; bonus never becomes a manual completion");
            Check(d.staggerApplied==0,"wind-knot followup choice does not inherit stagger choice");
            var e=new DamageArrowAdapterTests();e._arrows.LaunchCurve(e.target.id,3,"g21",4);
            e.TickArrows(2);e._arrows.ReserveStraight(e.target.id,3,"g1",2.5f);e.inRange=false;e.TickArrows(2);
            Check(e.staggerApplied==40&&e.extras==1&&e.finishers==2,"spread arrow stagger survives delayed impact; straight and bonus do not copy it");
            var f=new DamageArrowAdapterTests();f._arrows.LaunchCurve(f.target.id,3,"g21",4);f.target.immune=true;f.TickArrows(4);
            Check(f.staggerApplied==0,"immune curved arrow cannot apply stagger");
            var g=new DamageArrowAdapterTests();var nearby=new Transform{position=new DamagePoint(2,0)};var far=new Transform{position=new DamagePoint(6,0)};
            CharacterKit.targets=new[]{g.target,nearby,nearby,far};
            g._arrows.LaunchCurve(g.target.id,3,"g21",4);g.TickArrows(2);g._arrows.ReserveStraight(g.target.id,3,"g1",2.5f);g.inRange=false;g.TickArrows(2);
            Check(g.hits==4&&g.finishers==2&&g.staggerApplied==80,"delayed spread hits two enemies once without multiplying two manual finishers");
            Check(g._arrows.Snapshot.convergence==1&&g.extras==1,"collateral does not add convergence or bonus arrows");
            var h=new DamageArrowAdapterTests();h.target.immune=true;CharacterKit.targets=new[]{nearby};
            h._arrows.LaunchCurve(h.target.id,3,"g21",4);h.TickArrows(2);h._arrows.ReserveStraight(h.target.id,3,"g1",2.5f);h.TickArrows(2);
            Check(h.finishers==1&&h._arrows.Snapshot.convergence==0&&h.extras==0,"collateral-only curve grants its one completion but cannot substitute for paired target convergence");
            h.TickArrows(5);Check(h.finishers==1,"collateral completion cannot repeat after arrival is consumed");
            var piercing=new DamageArrowAdapterTests();piercing.target.position=new DamagePoint(10,5);
            var along=new Transform{position=new DamagePoint(14,5)};
            var side=new Transform{position=new DamagePoint(14,8)};
            var behind=new Transform{position=new DamagePoint(-2,5)};
            var beyond=new Transform{position=new DamagePoint(24,5)};
            CharacterKit.targets=new[]{along,along,side,behind,beyond};
            Check(piercing._arrows.LaunchCurve(piercing.target.id,3,"g20",4,new DamagePoint(0,5),20),"piercing launch stores world geometry");
            var saved=piercing._arrows.Snapshot;
            var restored=new DamageArrowAdapterTests();restored.target.position=piercing.target.position;restored.inRange=false;
            Check(restored._arrows.Restore(saved),"pending piercing restores after caster movement");
            saved.pending[0].origin=new DamagePoint(100,100);saved.pending[0].range=1;
            restored.TickArrows(4);
            Check(restored.hits==2&&restored.finishers==1&&restored.extras==0,"saved launch line hits primary and one collateral once, excludes side behind and out of range without extra resources");
            restored.TickArrows(4);Check(restored.hits==2,"restored piercing cannot repeat");
            var invalid=piercing._arrows.Snapshot;invalid.pending[0].range=float.NaN;
            Check(!piercing._arrows.Restore(invalid)&&piercing._arrows.Snapshot.pending[0].range==20,"invalid range restore preserves existing pending arrow");
            invalid=piercing._arrows.Snapshot;invalid.pending[0].origin=new DamagePoint(float.PositiveInfinity,0);
            Check(!piercing._arrows.Restore(invalid),"nonfinite launch origin rejected");
            Check(!piercing._arrows.LaunchCurve(9,3,"g20",4,default,51),"unbounded piercing range rejected");
            Console.WriteLine("PASS actual arrow arrival adapter: "+count+"; world damage is a test seam.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
