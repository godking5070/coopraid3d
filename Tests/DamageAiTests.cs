using System;
using System.Linq;
using RaidRogue;
sealed class Transform { public int id; public float distance; public bool alive=true; public DamagePoint position; }
static class CharacterKit {public static int Id(Transform t)=>t==null?0:t.id;}
static class CombatRange {public static bool InRange(Transform a,Transform b,float range)=>b!=null&&b.distance<=range;public static bool LivingEnemy(Transform t)=>t!=null&&t.alive;}
sealed class DamageAiTests
{
    RosterJob Job;
    CharacterRuntimeState _runtime;
    CharacterRuntimeState _actor=>_runtime;
    NineKeyDamageResources _resources;
    readonly Transform transform=new Transform();
    string Unavailable(CharacterSlot slot)=>"";
    bool IsFourth(CharacterSkillDefinition skill)=>false;
    float EffectiveRange(CharacterSkillDefinition skill)=>3;
    DamagePoint Point(DamagePoint p)=>p;
    DamagePrismCommand AiPrismCommand(Transform enemy)=>null; // World validation tested separately.
    int Route;
    bool Active=true,preparing,accelerated;
    bool AiPreparing=>preparing;
    void Bind(){}
    bool Has(string key)=>key=="acceleration"&&accelerated;
    bool Core(int r,int n)=>_runtime.Snapshot.coreTrait==NineKeyDamageData.Core(Job,r,n);
    DamageSealState _seal=new DamageSealState();
    DamageSpearState _spears=new DamageSpearState();
    DamageHandsState _hands=new DamageHandsState();
    DamageGrenadeState _grenade=new DamageGrenadeState();
    DamageSnipeState _snipe;
    DamageArrowState _arrows=new DamageArrowState();
    DamageBurstState _burst=new DamageBurstState();
    DamageMeteorState _meteor;
    DamageInstantState _instant=new DamageInstantState();
    DamageGravityState _gravity=new DamageGravityState();
    DamageMemoryState _memory=new DamageMemoryState();
    DamageProjectionState _projection=new DamageProjectionState();
    DamagePrismState _prism=new DamagePrismState();
    DamageAiTests(int job,int route)
    {
        Job=CharacterRosterData.Jobs.First(j=>j.index==job);Route=route;
        _runtime=new CharacterRuntimeState(NineKeyDamageData.Build(Job));_runtime.ChooseRoute(_runtime.Definition.routes[route-1].id,true);
        var r=new NineKeyDamageResources(_runtime,Job);_resources=r;_snipe=new DamageSnipeState(r);_meteor=new DamageMeteorState(r);
    }
    /* ACTUAL_AI_IDENTITY */
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            for(int job=6;job<=11;job++)for(int route=1;route<=3;route++)
            {
                var a=new DamageAiTests(job,route);
                Check(a.AiWantsIdentity(out _)==(job==8&&route==1),"zero resource only permits free hand reload");
                a._runtime.Gain(a.Job.Resource,100);a.preparing=true;
                Check(!a.AiWantsIdentity(out _),"preparing attack blocks automatic R spam");
            }
            var gun=new DamageAiTests(8,1);
            Check(gun.AiWantsIdentity(out _),"empty hands request reload");
            gun._hands.BeginReload(false,2,6,3);
            Check(!gun.AiWantsIdentity(out _),"active hand reload does not restart");
            gun._hands.Tick(2);Check(!gun.AiWantsIdentity(out _),"reload cooldown remains respected");
            gun._hands.Tick(4);Check(gun.AiWantsIdentity(out _),"other empty hand can reload after cooldown");
            var mixed=new DamageAiTests(8,1);mixed._hands.Configure(3,3);
            mixed._hands.Fire(1);mixed._hands.Fire(2);mixed._hands.Fire(2);mixed._hands.Fire(2);mixed._hands.Fire(1);
            Check(mixed.AiWantsIdentity(out bool oppositeHand)&&oppositeHand,"empty right hand chosen with long R despite latest left shot");
            Check(mixed._hands.BeginReload(oppositeHand,2,6,3)&&mixed._hands.Snapshot.reloading==2,"AI decision reaches intended reload through actual hand state");
            Check(mixed._hands.Fire(1)&&!mixed._hands.Fire(2),"remaining loaded hand fires while empty hand reloads");
            Check(!mixed.AiWantsIdentity(out _),"both now empty cannot restart active reload");
            mixed._hands.Tick(6);
            Check(mixed.AiWantsIdentity(out bool shortReload)&&!shortReload,"after reload the remaining empty last-fired hand uses short R");
            var mirrored=new DamageAiTests(8,1);mirrored._hands.Configure(3,3);
            mirrored._hands.Fire(2);mirrored._hands.Fire(1);mirrored._hands.Fire(1);mirrored._hands.Fire(1);mirrored._hands.Fire(2);
            Check(mirrored.AiWantsIdentity(out oppositeHand)&&oppositeHand&&mirrored._hands.ReloadTarget(oppositeHand)==1,"empty left hand is handled symmetrically");
            var full=new DamageAiTests(8,1);full._hands.Configure(3,3);
            Check(!full.AiWantsIdentity(out _),"full hands do not request pointless reload");
            var tempest=new DamageAiTests(7,3);
            tempest._spears.Place(new DamagePoint(0,0),20,40,false,0);
            Check(!tempest.AiWantsIdentity(out _),"unfunded uncharged spear waits");
            tempest._spears.Charge(2,1,true);tempest._spears.Charge(2,2,true);tempest._spears.Charge(2,3,true);
            Check(tempest.AiWantsIdentity(out bool recall)&&recall,"charged spears choose long recall even without new resource");
            var burst=new DamageAiTests(9,2);burst._burst.Begin(16,.25f);burst._burst.Tick(.25f);
            Check(!burst.AiWantsIdentity(out _),"burst waits for combo precision while time remains");
            burst._burst.CompleteCombo();Check(burst.AiWantsIdentity(out _),"precision releases a followup bundle");
            var accel=new DamageAiTests(7,2);accel._runtime.Gain(accel.Job.Resource,100);accel.accelerated=true;
            Check(!accel.AiWantsIdentity(out _),"active acceleration is not recast");
            var archer=new DamageAiTests(9,1);var oldEnemy=new Transform{id=1};var newEnemy=new Transform{id=2};
            archer._arrows.LaunchCurve(1,1,"g20",2);
            Check(archer.AiCombo(oldEnemy)==CharacterSlot.Gcd1,"same target in reservation window selects straight arrow");
            Check(archer.AiCombo(newEnemy)==CharacterSlot.Gcd2,"switching target starts its own curve instead of using another target reservation");
            Check(archer.AiCombo(null)==CharacterSlot.Gcd2,"missing target cannot match pending arrow");
            Check(archer._arrows.Snapshot.pending[0].straightStep==0,"AI planning does not consume reservation");
            archer._arrows.ReserveStraight(1,1,"g1",2.5f);
            Check(archer.AiCombo(oldEnemy)==CharacterSlot.Gcd2,"already paired arrow cannot request another straight");
            archer._arrows.LaunchCurve(2,1,"g20",4);
            Check(archer.AiCombo(newEnemy)==CharacterSlot.Gcd2,"new curve waits until its own window opens");
            archer._arrows.Tick(1.5f,(p,straight)=>true);
            Check(archer.AiCombo(newEnemy)==CharacterSlot.Gcd1,"exact window boundary selects current target straight");
            var reaper=new DamageAiTests(11,2);var remote=new Transform{id=8,distance=12};
            Check(!reaper.AiWantsIdentity(out _,remote),"unfunded reaper cannot prepare projection");
            reaper._runtime.Gain(reaper.Job.Resource,1);
            Check(reaper.AiWantsIdentity(out bool execution,remote)&&!execution,"remote target chooses one-shard projection instead of waiting for execution");
            Check(reaper._runtime.Resource(reaper.Job.Resource)==1,"projection planning does not spend shards");
            reaper._projection.Begin(16);
            Check(!reaper.AiWantsIdentity(out _,remote)&&reaper.AiCombo(remote)==CharacterSlot.Gcd2,"active projection is not restarted and prioritizes remaining second combo");
            reaper._projection.Tick(20);reaper._runtime.Gain(reaper.Job.Resource,2);
            remote.distance=2;
            Check(reaper.AiWantsIdentity(out execution,remote)&&execution,"safe melee range invests full shards in execution");
            remote.distance=30;reaper._runtime.Spend(reaper.Job.Resource,2);
            Check(!reaper.AiWantsIdentity(out _,remote)&&reaper.AiCombo(remote)==CharacterSlot.Gcd1,"out-of-projection-range target does not waste a single shard");
            remote.distance=12;remote.alive=false;
            Check(!reaper.AiWantsIdentity(out _,remote),"dead distant target does not trigger paid projection");
            var gravity=new DamageAiTests(10,3);var moving=new Transform{id=10,position=new DamagePoint(4,0)};
            gravity._gravity.Begin(default,16);
            Check(gravity.AiWantsIdentity(out _,moving),"compressed field expands to catch a target within expanded reach without new mana");
            Check(!gravity._gravity.Snapshot.expanded&&!gravity._gravity.Snapshot.protectionUsed,"AI planning does not toggle or grant protection");
            gravity._gravity.Toggle(out _);
            Check(!gravity.AiWantsIdentity(out _,moving),"expanded field does not repeatedly toggle while covering target");
            moving.position=new DamagePoint(2.9f,0);
            Check(!gravity.AiWantsIdentity(out _,moving),"compression return margin avoids boundary oscillation");
            moving.position=new DamagePoint(2,0);
            Check(gravity.AiWantsIdentity(out _,moving),"target inside narrow radius allows compression");
            moving.position=new DamagePoint(12,0);
            Check(!gravity.AiWantsIdentity(out _,moving)&&gravity.AiCombo(moving)==CharacterSlot.Gcd2,"target outside both modes requests second-combo relocation instead of pointless toggles");
            gravity._gravity.Follow(moving.position,true);
            Check(gravity.AiWantsIdentity(out _,moving),"after actual finisher relocation expanded field can compress on new center");
            moving.alive=false;Check(!gravity.AiWantsIdentity(out _,moving),"dead target does not trigger mode changes");
            var memory=new DamageAiTests(11,1);
            memory._runtime.ChangeLoadout(NineKeyDamageData.Id(memory.Job,"loadout.core1"),NineKeyDamageData.Core(memory.Job,1,1),true);
            memory._memory.Record("g1",true,2,2);memory._memory.Record("g20",true,2,4);memory._memory.ToggleLock();
            memory._runtime.Gain(memory.Job.Resource,2);
            Check(memory.AiWantsIdentity(out bool unlock)&&!unlock,"complete locked set chooses replay rather than discard");
            memory._memory.Tick(2);
            Check(memory.AiWantsIdentity(out unlock)&&unlock,"partially expired locked set requests free unlock");
            Check(memory._memory.Snapshot.locked&&memory._memory.Snapshot.records.Length==1,"planning preserves surviving record and lock");
            memory._memory.ToggleLock();memory._memory.Record("g1",true,2,4);
            Check(memory.AiWantsIdentity(out unlock)&&!unlock,"after unlock and actual finisher AI resumes replay");
            memory._memory.Tick(10);memory._memory.ToggleLock();memory._runtime.Spend(memory.Job.Resource,2);
            Check(memory.AiWantsIdentity(out unlock)&&unlock,"empty locked state unlocks without shards");
            memory._memory.ToggleLock();
            Check(!memory.AiWantsIdentity(out _),"empty unlocked state waits for new records rather than toggling repeatedly");
            var instant=new DamageAiTests(10,2);instant._instant.Begin(3,20,30);
            Check(instant.AiWantsIdentity(out _,newEnemy),"stationary AI preserves future instant rights with OFF request");
            instant._instant.Toggle(.25f);
            Check(!instant.AiWantsIdentity(out _,newEnemy,true),"movement cannot bypass toggle input delay");
            Check(instant.AiCombo(newEnemy,true)==CharacterSlot.Gcd1,"moving while OFF uses mobile first combo");
            instant._instant.Tick(.25f);
            Check(instant.AiWantsIdentity(out bool instantLong,newEnemy,true)&&!instantLong,"OFF handoff enables rights for movement despite preparation cooldown");
            Check(instant._instant.Snapshot.rights==3&&instant._runtime.Resource(instant.Job.Resource)==0,"AI mode planning spends neither mana nor rights");
            instant._instant.Toggle(.25f);
            Check(instant.AiCombo(newEnemy,true)==CharacterSlot.Gcd2,"enabled movement chooses instant second combo");
            instant._instant.Tick(.25f);
            Check(!instant.AiWantsIdentity(out _,newEnemy,true),"moving ON does not toggle repeatedly");
            Check(instant.AiWantsIdentity(out _,newEnemy),"stopping early preserves remaining rights");
            instant._instant.Toggle(.25f);instant._instant.Tick(13);
            Check(instant.AiWantsIdentity(out _,newEnemy),"near-expiry rights turn ON even while stationary");
            instant._instant.Toggle(.25f);
            Check(instant.AiCombo(newEnemy)==CharacterSlot.Gcd2,"near-expiry rights choose second combo before expiration");
            instant._instant.Consume(true);instant._instant.Consume(true);instant._instant.Consume(true);
            Check(!instant.AiWantsIdentity(out _,newEnemy,true)&&instant.AiCombo(newEnemy,true)==CharacterSlot.Gcd1,"spent rights cannot bypass preparation cooldown and movement keeps basic attack");
            Console.WriteLine("PASS actual DPS AI identity and combo decisions: "+checks+"; scene navigation and execution remain separate.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
