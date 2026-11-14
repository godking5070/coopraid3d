using System;
using RaidRogue;

sealed class DamageSeedAdapterTests
{
    DamageSeedState _seed=new DamageSeedState();
    DamageTempestPreparationState _tempestPreparation=new DamageTempestPreparationState();
    sealed class Pose { public DamagePoint position=new DamagePoint(2,3); }
    Pose transform=new Pose();
    bool ground=true;
    int explosions;
    int seedVariant=-1;
    float lastDamage;
    bool SeedCore(int variant)=>seedVariant==variant;
    bool CanPlace(DamagePoint p)=>ground;
    DamagePoint Point(DamagePoint p)=>p;
    float InstallationDuration(float duration)=>duration; // No auxiliary in this input fixture.
    void SeedDamage(DamagePoint p,float damage){Check(!_seed.Active&&damage>0,"consume before explosion callback");explosions++;lastDamage=damage;}
    /* ACTUAL_SEED_INPUT */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageSeedAdapterTests();a.ground=false;
            Check(!a.SeedInput()&&!a._seed.Active,"bad ground does not deploy");a.ground=true;
            Check(a.SeedInput()&&a._seed.Active&&a.explosions==0,"deployment has no free explosion");
            var spear=new DamageSpearState();spear.Place(new DamagePoint(5,5),20,40,false,0);spear.Charge(2,1,true);
            Check(a._seed.Tick(.5f,out bool expired)==0&&!expired,"no premature tick");
            Check(a._seed.Tick(.5f,out expired)==1&&!expired,"one periodic tick");
            Check(a.SeedInput()&&!a._seed.Active&&a.explosions==1,"F re-input detonates early once");
            Check(spear.Snapshot.spears.Length==1&&spear.Snapshot.spears[0].charge==1,"F detonation does not recall or charge R spears");
            Check(!a.SeedInput()&&a.explosions==1,"cooldown prevents deploy-detonate repetition");
            Check(a._seed.Tick(15,out expired)==0&&!expired,"early detonation leaves no late explosion");
            Check(a.SeedInput(),"new deployment after cooldown");
            spear.Recall();Check(a._seed.Active,"R recall leaves F seed intact");
            var state=a._seed.Snapshot;var restored=new DamageSeedState();
            Check(restored.Restore(state),"active state roundtrip");
            Check(restored.Tick(100,out expired)==6&&expired&&!restored.Active,"large tick bounds periodic events by lifetime and expires once");
            Check(restored.Tick(100,out expired)==0&&!expired&&!restored.Detonate(out _),"expiry cannot detonate twice");
            state.nextTick=0;Check(!a._seed.Restore(state)&&a._seed.Active,"invalid restore is atomic");
            state=a._seed.Snapshot;state.point=new DamagePoint(float.NaN,0);Check(!a._seed.Restore(state),"invalid coordinate rejected");
            var compressed=new DamageSeedAdapterTests{seedVariant=1};
            Check(compressed.SeedInput()&&compressed._seed.Snapshot.remaining==8,"compressed F has longer finite life");
            Check(compressed._seed.Tick(2,out expired)==0&&compressed._seed.Snapshot.stored==60,"compressed ticks store partial budget instead of automatic damage");
            Check(compressed.SeedInput()&&compressed.lastDamage==240&&compressed._seed.Snapshot.stored==0,"early explosion consumes stored budget once");
            Check(!compressed.SeedInput()&&compressed.explosions==1,"compressed budget cannot replay");
            var longSeed=new DamageSeedState();longSeed.Place(new DamagePoint(0,0),13,16,true,240);
            Check(longSeed.Tick(13,out expired)==0&&expired&&longSeed.Snapshot.stored==240,"extended life never expands compression storage cap");
            Check(longSeed.Tick(1,out expired)==0&&!expired,"natural explosion only once");
            var floating=new DamageSeedAdapterTests{seedVariant=0};floating.SeedInput();floating._seed.Tick(1,out _);
            floating._seed.Move(new DamagePoint(8,9));
            Check(floating._seed.Snapshot.point.x==8&&floating._seed.Snapshot.remaining==3,"floating follows completion without renewing reduced life");
            Console.WriteLine("PASS actual seed input and state: "+count+"; damage world adapter remains separately compiled.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
