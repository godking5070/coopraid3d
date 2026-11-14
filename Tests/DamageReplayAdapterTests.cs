using System;
using System.Linq;
using RaidRogue;
sealed class Transform { public bool immune;public bool alive=true;public DamagePoint position;public float damage,stagger; }
static class CharacterKit { public static int Id(Transform t)=>9;public static Transform[] targets=Array.Empty<Transform>();public static Transform[] Enemies()=>targets; }
static class CombatRange {public static float Radius(Transform t)=>.5f;public static bool LivingEnemy(Transform t)=>t!=null&&t.alive;}
sealed class DamageReplayAdapterTests
{
    readonly CharacterEffectState _effects=new CharacterEffectState();
    readonly Transform target=new Transform();
    float Now,total;
    int attacks,marks;
    bool reenter,inRange=true;
    bool Enemy(Transform t)=>CombatRange.LivingEnemy(t)&&inRange;
    DamagePoint Point(DamagePoint p)=>p;
    Transform Resolve(int id)=>id==9?target:null;
    CharacterSkillDefinition Skill(string key)=>new CharacterSkillDefinition{id=key};
    bool Attack(CharacterSkillDefinition s,Transform t,float power,float stagger,CombatDamageKind kind,bool launched=false)
    {
        Check(kind==CombatDamageKind.Extra&&launched,"replay remains automatic and retains original launch range acceptance");
        attacks++;
        if(reenter){reenter=false;TickMemoryReplay();}
        if(!CombatRange.LivingEnemy(t)||t.immune||!launched&&!inRange)return false;total+=power;t.damage+=power;t.stagger+=stagger;return true;
    }
    void Mark(Transform t){marks++;}
    /* ACTUAL_REPLAY_METHODS */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageReplayAdapterTests();a.QueueMemoryReplay(new[]{"g1","g22"},a.target);
            a.TickMemoryReplay();Check(a.attacks==0&&a._effects.Data.effects.Length==2,"split attacks wait for their distinct deadlines");
            a.Now=.3f;a.inRange=false;a.TickMemoryReplay();Check(a.total==220&&a.marks==0&&a._effects.Data.effects.Length==1,"moving outside launch range preserves first replay without last-hit cooperation");
            var saved=a._effects.Data.Copy();var b=new DamageReplayAdapterTests();b._effects.Restore(saved);b.Now=.6f;b.TickMemoryReplay();
            Check(b.total==280&&b.marks==1&&b._effects.Data.effects.Length==0,"remaining second replay restores without replaying consumed first hit");
            b.TickMemoryReplay();Check(b.marks==1&&b.attacks==1,"completed set cannot replay a mark");
            a.target.immune=true;a.Now=.6f;a.TickMemoryReplay();a.target.immune=false;a.Now=10;a.TickMemoryReplay();
            Check(a.total==220&&a.marks==0&&a.attacks==2,"failed last replay consumes its only opportunity and never marks");
            var c=new DamageReplayAdapterTests();c.QueueMemoryReplay(new[]{"g20","g1"},c.target);c.Now=10;c.reenter=true;c.TickMemoryReplay();
            Check(c.attacks==2&&c.total==500&&c.marks==1,"large frame and reentrant callback preserve finite set budget");
            var area=new DamageReplayAdapterTests();var nearby=new Transform{position=new DamagePoint(2,0)};var far=new Transform{position=new DamagePoint(5,0)};
            CharacterKit.targets=new[]{area.target,nearby,nearby,far};
            area.QueueMemoryReplay(new[]{"g1","g21"},area.target);area.Now=1;area.TickMemoryReplay();
            Check(area.target.damage==444&&nearby.damage==224&&far.damage==0,"recorded glass flower replays its area with independent replay coefficients");
            Check(area.marks==1&&area.attacks==3&&area._effects.Data.effects.Length==0,"multiple replay targets do not multiply last-set mark or deferred rights");
            var single=new DamageReplayAdapterTests();CharacterKit.targets=new[]{nearby,far};
            Check(single.ReplayAttack("g22",single.target,320)==single.target&&single.target.stagger==40,"single focused shatter replay keeps technique stagger");
            Check(single.attacks==1&&single.total==320,"focused single replay does not hit unrelated nearby targets");
            var movedArea=new DamageReplayAdapterTests{inRange=false};CharacterKit.targets=new[]{nearby};
            movedArea.QueueMemoryReplay(new[]{"g21"},movedArea.target);movedArea.Now=1;movedArea.TickMemoryReplay();
            Check(movedArea.attacks==2&&movedArea.marks==1&&movedArea.total==448,"out of range caster retains both primary and collateral replay without duplicate synergy");
            var dead=new DamageReplayAdapterTests();dead.QueueMemoryReplay(new[]{"g21"},dead.target);dead.target.alive=false;dead.Now=1;dead.TickMemoryReplay();
            Check(dead.attacks==0&&dead.marks==0&&dead._effects.Data.effects.Length==0,"dead replay anchor consumes pending attack without creating collateral retry");
            Console.WriteLine("PASS actual split-memory replay adapter: "+count+"; target damage and mark are test seams.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
