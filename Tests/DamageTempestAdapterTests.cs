using System;
using System.Linq;
using RaidRogue;

sealed class Transform { public DamagePoint position=new DamagePoint(0,0); }
static class CombatRange { public static bool InRange(Transform a,Transform b,float range)=>true; }
sealed class DamageTempestAdapterTests
{
    DamageTempestPreparationState _tempestPreparation=new DamageTempestPreparationState();
    Transform transform=new Transform(),enemy=new Transform();
    float damage;
    bool valid=true;
    bool _fourthReady;
    bool Active=>true;
    void Bind(){}
    bool Spend(float cost)=>runtime.Spend(Job.Resource,cost);
    RosterJob Job=CharacterRosterData.Jobs.First(j=>j.index==7);
    CharacterRuntimeState runtime;
    bool Core(int route,int variant)=>runtime?.Snapshot.coreTrait==NineKeyDamageData.Core(Job,route,variant);
    bool ThirdAssaultCore=>runtime?.Snapshot.coreTrait==NineKeyDamageData.Id(Job,"core10.a0");
    bool Enemy(Transform t)=>valid&&t!=null;
    float EffectiveRange(CharacterSkillDefinition skill)=>skill.range;
    DamagePoint Point(DamagePoint point)=>point;
    float SplitResidualCloud(CharacterSkillDefinition skill,DamagePoint point,float power)=>power;
    bool Attack(CharacterSkillDefinition skill,Transform target,float power){damage=power;return true;}
    /* ACTUAL_TEMPEST_THIRD */
    /* ACTUAL_TEMPEST_COST */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageTempestAdapterTests();var job=CharacterRosterData.Jobs.First(j=>j.index==7);
            var state=new CharacterRuntimeState(NineKeyDamageData.Build(job));state.ChooseRoute(state.Definition.routes[1].id,true);
            a.runtime=state;
            var group=state.Definition.loadouts.First(l=>l.slot==CharacterSlot.OffGcd1);
            var context=new CharacterUseContext(true,true,true,0);
            state.ChangeLoadout(group.id,NineKeyDamageData.Id(job,"a1"),true);
            a._tempestPreparation.Grant(0,12);a._tempestPreparation.Grant(2,12);
            Check(!state.TryUse(CharacterSlot.OffGcd1,context,1,s=>a.TempestThird(s,a.enemy,"a1"),out _),"insufficient electricity blocks third before effects");
            Check(a.damage==0&&a._tempestPreparation.Snapshot.combo==12,"rejected third preserves preparation");
            state.Gain(job.Resource,40);
            Check(state.TryUse(CharacterSlot.OffGcd1,context,1,s=>a.TempestThird(s,a.enemy,"a1"),out _)&&state.Resource(job.Resource)==20,"manual chase consumes exactly20");
            Check(a.damage==300&&a._tempestPreparation.Snapshot.combo==0&&a._tempestPreparation.Snapshot.cloud==12,"manual chase uses combo preparation but preserves cloud");
            Check(!state.TryUse(CharacterSlot.OffGcd1,context,1,s=>a.TempestThird(s,a.enemy,"a1"),out _)&&state.Resource(job.Resource)==20,"cooldown cannot repeat cost or damage");
            state.ChangeLoadout(group.id,NineKeyDamageData.Id(job,"a0"),true);
            Check(!state.TryUse(CharacterSlot.OffGcd1,context,1,s=>a.TempestThird(s,a.enemy,"a0"),out _),"lobby swap retains previous skill cooldown");
            state.Tick(12);
            a._tempestPreparation.Grant(0,12);
            Check(state.TryUse(CharacterSlot.OffGcd1,context,1,s=>a.TempestThird(s,a.enemy,"a0"),out _)&&state.Resource(job.Resource)==10,"ground assault consumes exactly10 prototype cost");
            Check(a.damage==190*1.25f*1.25f&&a._tempestPreparation.Snapshot.cloud==0,"ground assault consumes both applicable one-shot preparations");
            state.ChangeLoadout(group.id,NineKeyDamageData.Id(job,"a2"),true);
            state.Tick(24); // two sequential charges after the inherited cooldown
            a._tempestPreparation.Grant(0,12);a._tempestPreparation.Grant(2,12);
            a.Attack(state.Selected(CharacterSlot.OffGcd1),a.enemy,100);
            Check(a._tempestPreparation.Snapshot.combo==12&&a._tempestPreparation.Snapshot.cloud==12,"automatic attack path does not consume manual preparations");
            Check(state.TryUse(CharacterSlot.OffGcd1,context,1,s=>a.TempestThird(s,a.enemy,"a2"),out _)&&a.damage==187.5f,"first charged throw consumes manual preparation");
            Check(state.TryUse(CharacterSlot.OffGcd1,context,1,s=>a.TempestThird(s,a.enemy,"a2"),out _)&&a.damage==150,"second charged throw is unenhanced");
            Check(!state.TryUse(CharacterSlot.OffGcd1,context,1,s=>a.TempestThird(s,a.enemy,"a2"),out _)&&state.Resource(job.Resource)==10,"two throws neither spend nor create electricity");
            Check(a._tempestPreparation.TakeRelease()==1.25f&&a._tempestPreparation.TakeRelease()==1,"cloud release once per attack batch");
            a._tempestPreparation.Grant(0,12);a._tempestPreparation.Grant(0,12);a._tempestPreparation.Tick(12);
            Check(a._tempestPreparation.TakeManualThird(false)==1,"refresh never stacks and expires finitely");
            a._tempestPreparation.Grant(0,12);a.valid=false;
            Check(!a.TempestThird(state.Selected(CharacterSlot.OffGcd1),null,"a2")&&a._tempestPreparation.Snapshot.combo==12,"invalid target leaves preparation intact");
            var copy=a._tempestPreparation.Snapshot;copy.combo=float.NaN;
            Check(!a._tempestPreparation.Restore(copy)&&a._tempestPreparation.Snapshot.combo==12,"malformed restore is atomic");
            var enhanced=new CharacterRuntimeState(NineKeyDamageData.Build(job));enhanced.ChooseRoute(enhanced.Definition.routes[2].id,true);
            enhanced.ChangeLoadout(group.id,NineKeyDamageData.Id(job,"a1"),true);
            var core=enhanced.Definition.loadouts.First(l=>l.slot==0&&l.routeId==enhanced.RouteId);
            Check(enhanced.ChangeLoadout(core.id,NineKeyDamageData.Core(job,2,1),true),"concentrated discharge remains compatible across routes");
            a.runtime=enhanced;a.valid=true;a.damage=0;
            var scaled=new CharacterUseContext(true,true,true,0,costScale:a.CostScale(enhanced.Selected(CharacterSlot.OffGcd1)));
            enhanced.Gain(job.Resource,59);
            Check(!enhanced.TryUse(CharacterSlot.OffGcd1,scaled,1,s=>a.TempestThird(s,a.enemy,"a1"),out _)&&a.damage==0&&enhanced.Resource(job.Resource)==59,"59 electricity cannot launch concentrated attack");
            Check(a._tempestPreparation.Snapshot.combo==12,"insufficient concentrated cost preserves manual preparation");
            enhanced.Gain(job.Resource,1);
            Check(enhanced.TryUse(CharacterSlot.OffGcd1,scaled,1,s=>a.TempestThird(s,a.enemy,"a1"),out _)&&enhanced.Resource(job.Resource)==0,"concentrated cost commits exactly60 once");
            Check(a.damage==650*1.25f&&a._tempestPreparation.Snapshot.combo==0,"concentrated attack receives and consumes one manual preparation");
            Check(a.CostScale(enhanced.Definition.Skill(NineKeyDamageData.Id(job,"a2")))==1&&a.CostScale(enhanced.Definition.Skill(NineKeyDamageData.Id(job,"q")))==1,"concentrated core leaves throw and defense costs alone");
            enhanced.ChangeLoadout(core.id,NineKeyDamageData.Core(job,2,0),true);
            enhanced.ChangeLoadout(group.id,NineKeyDamageData.Id(job,"a0"),true);
            Check(enhanced.ChangeLoadout(core.id,NineKeyDamageData.Id(job,"core10.a0"),true),"third assault target can be selected outside assault route");
            enhanced.Tick(24);enhanced.Gain(job.Resource,12);
            var repeated=new CharacterUseContext(true,true,true,0,costScale:a.CostScale(enhanced.Selected(CharacterSlot.OffGcd1)));
            Check(enhanced.TryUse(CharacterSlot.OffGcd1,repeated,1,s=>a.TempestThird(s,a.enemy,"a0"),out _)&&enhanced.Resource(job.Resource)==6,"third assault variant spends six once");
            Check(Math.Abs(a.damage-114)<.01f,"third assault variant lowers individual damage");
            Check(enhanced.TryUse(CharacterSlot.OffGcd1,repeated,1,s=>a.TempestThird(s,a.enemy,"a0"),out _)&&enhanced.Resource(job.Resource)==0,"second charge spends six once");
            var fourth=new CharacterRuntimeState(NineKeyDamageData.Build(job));fourth.ChooseRoute(fourth.Definition.routes[1].id,true);
            a.runtime=fourth;a.valid=true;
            var second=fourth.Selected(CharacterSlot.Gcd2);
            for(int step=1;step<=3;step++)
            {
                fourth.Tick(2);
                Check(fourth.TryUse(CharacterSlot.Gcd2,context,1,s=>{a.UpdateFourthOpportunity(s,step);return true;},out _),"base second combo advances");
            }
            Check(a.IsFourth(second)&&fourth.Snapshot.combo2==1,"completed second combo offers optional fourth without starting another combo");
            a.UpdateFourthOpportunity(fourth.Selected(CharacterSlot.Job1),0);
            a.UpdateFourthOpportunity(fourth.Selected(CharacterSlot.Movement),0);
            Check(a.IsFourth(second),"defense and movement preserve fourth opportunity");
            fourth.Tick(2);fourth.Gain(job.Resource,19);
            var fourthContext=new CharacterUseContext(true,true,true,0,preserveCombo:a.IsFourth(second));
            Check(!fourth.TryUse(CharacterSlot.Gcd2,fourthContext,1,s=>a.FourthStrike(s,a.enemy),out _)&&a.IsFourth(second)&&fourth.Resource(job.Resource)==19,"insufficient fourth cost preserves opportunity and balance");
            fourth.Gain(job.Resource,1);float gauge=fourth.Resource(NineKeyDamageData.Gauge(job));
            Check(fourth.TryUse(CharacterSlot.Gcd2,fourthContext,1,s=>a.FourthStrike(s,a.enemy),out _),"funded fourth executes");
            Check(fourth.Resource(job.Resource)==0&&fourth.Resource(NineKeyDamageData.Gauge(job))==gauge&&!a.IsFourth(second),"fourth spends20 once and gives no finisher gauge");
            Check(fourth.Snapshot.combo2==1&&fourth.Snapshot.gcd>0&&a.damage==240,"fourth consumes GCD and keeps next combo at first step");
            fourth.Tick(2);a.UpdateFourthOpportunity(second,3);
            Check(fourth.TryUse(CharacterSlot.Gcd1,context,1,s=>{a.UpdateFourthOpportunity(s,1);return true;},out _),"other GCD accepted");
            Check(!a.IsFourth(second)&&fourth.Snapshot.combo1==2&&fourth.Snapshot.combo2==1,"other combo removes fourth while retaining independent progression");
            Console.WriteLine("PASS actual Tempest third and framework costs: "+count);
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
