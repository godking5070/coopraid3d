using System;
using System.Collections.Generic;
using System.Linq;
using RaidRogue;
public static class AegisPaladinTests
{
    static int checks;
    static void Check(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
    static void Near(double a,double b,string why,double tolerance=.0001){Check(Math.Abs(a-b)<=tolerance,why+" actual="+a+" expected="+b);}
    static readonly CharacterUseContext In=new CharacterUseContext(true,true,true,3);
    static bool Use(CharacterRuntimeState actor,CharacterSlot slot,Func<CharacterSkillDefinition,bool> hit=null)=>actor.TryUse(slot,In,42,hit??(_=>true),out _);
    static JobSkillState Timing(CharacterRuntimeState actor,CharacterSlot slot)=>actor.Snapshot.skills.Single(s=>s.id==actor.Selected(slot).id);
    public static void Main()
    {
        var definition=AegisPaladinData.Build();
        Check(CharacterDefinitionValidation.Check(new[]{definition}).Length==0,"valid complete references");
        Check(CharacterCatalog.Register(definition,out _,true),"explicit registered job implementation");
        Check(!CharacterCatalog.Register(definition,out _,true),"duplicate registration rejected");
        Check(CharacterCatalog.All.Count(d=>!d.playable)==11,"only paladin newly playable");
        Check(CharacterSlots.Binding(CharacterSlot.Job1)=="Skill4"&&CharacterSlots.Binding(CharacterSlot.Potion)=="Potion","Q job and X potion use distinct existing binding actions");
        Check(definition.slots.Count(s=>s.initiallyUnlocked&&definition.Skill(s.skillId).implemented)==8,"seven class skills plus existing potion");
        var a=new CharacterRuntimeState(definition);var b=new CharacterRuntimeState(definition);
        foreach(var slot in new[]{CharacterSlot.Identity,CharacterSlot.Active,CharacterSlot.OffGcd2,CharacterSlot.Ultimate})Check(!Use(a,slot),"future skill locked");
        Near(a.Resource(AegisPaladinData.Holy),0,"locked skills don't spend/gain resource");
        int hits=0;float power=0;
        Func<CharacterSkillDefinition,bool> sword=s=>{hits++;int stage=a.Snapshot.combo1;power+=AegisPaladinData.SwordPower[stage-1];if(stage==3){a.Gain(AegisPaladinData.Holy,AegisPaladinData.FinisherHoly);a.ReduceCooldowns(AegisPaladinData.ReducedSkills,1);}return true;};
        Check(!a.TryUse(CharacterSlot.Gcd1,new CharacterUseContext(true,true,true,3.01f),42,sword,out _),"out of boundary range rejected before effects");
        Near(a.Snapshot.gcd,0,"failed hit no gcd");Check(a.Snapshot.combo1==1&&hits==0,"failed hit no combo/no damage");
        Check(!Use(a,CharacterSlot.Gcd1,_=>false),"effect refusing actual hit doesn't commit");
        Check(a.Snapshot.combo1==1&&a.Snapshot.gcd==0,"zero applied damage doesn't commit");
        Check(Use(a,CharacterSlot.Gcd1,sword),"sword first");Near(a.Snapshot.gcd,1.5,"sword gcd");
        Check(!Use(a,CharacterSlot.Gcd2),"shield shares gcd");Check(Use(a,CharacterSlot.OffGcd1),"burst usable during gcd");Check(Use(a,CharacterSlot.Job1),"faith usable during gcd");
        a.Tick(1.5f);Check(Use(a,CharacterSlot.Gcd2),"shield first");Near(a.Snapshot.gcd,2.5,"shield gcd");
        Check(a.Snapshot.combo1==2&&a.Snapshot.combo2==2,"independent stages");
        a.Tick(2.5f);Check(Use(a,CharacterSlot.Gcd1,sword),"sword second after shield");a.Tick(1.5f);
        float burst=Timing(a,CharacterSlot.OffGcd1).recharge,faith=Timing(a,CharacterSlot.Job1).recharge;
        Check(Use(a,CharacterSlot.Gcd1,sword),"third hit");Near(power,470,"sword powers");Near(a.Resource(AegisPaladinData.Holy),15,"third gains once");
        Near(Timing(a,CharacterSlot.OffGcd1).recharge,burst-1,"burst reduction");Near(Timing(a,CharacterSlot.Job1).recharge,faith-1,"faith reduction");Near(a.Snapshot.gcd,1.5,"new shared gcd not reduced");
        Check(!Use(a,CharacterSlot.Gcd1,sword)&&hits==3,"repeated same instant cannot replay finisher");
        Near(b.Resource(AegisPaladinData.Holy),0,"independent resource");Near(Timing(b,CharacterSlot.Job1).recharge,0,"independent cooldown");
        a.ReduceCooldowns(AegisPaladinData.ReducedSkills,999);Check(Timing(a,CharacterSlot.Job1).charges==1&&Timing(a,CharacterSlot.Job1).recharge==0,"clamp remaining CD and ready once");
        a.ReduceCooldowns(AegisPaladinData.ReducedSkills,999);Check(Timing(a,CharacterSlot.Job1).charges==1,"ready skill cannot gain extra charge");
        a.ClearOnDeath();Near(a.Resource(AegisPaladinData.Holy),15,"holy preserved through death");Check(a.Snapshot.combo1==1&&a.Snapshot.combo2==1,"death clears both combos");
        Check(Use(b,CharacterSlot.Gcd2),"shield combo independent actor");b.Tick(2.5f);Check(Use(b,CharacterSlot.Gcd2),"shield second");b.Tick(2.5f);Check(Use(b,CharacterSlot.Gcd2),"shield third");Check(b.Snapshot.combo2==1,"shield completion");Near(AegisPaladinData.ShieldPower.Sum(),600,"shield powers");Check(AegisPaladinData.ShieldStagger.Sum()>AegisPaladinData.SwordStagger.Sum(),"shield higher stagger");
        b.Tick(8);Check(b.Snapshot.combo1==1&&b.Snapshot.combo2==1,"combo expiry");
        var dying=new CharacterRuntimeState(definition);Check(Use(dying,CharacterSlot.Job1),"defense use before death");dying.Gain(AegisPaladinData.Holy,15);dying.ClearOnDeath();Near(Timing(dying,CharacterSlot.Job1).recharge,24,"death doesn't reset cooldown");Near(dying.Resource(AegisPaladinData.Holy),15,"death keeps current resource");
        var restored=new CharacterRuntimeState(definition);Check(restored.Restore(a.Snapshot),"confirmed actor restoration");Near(restored.Resource(AegisPaladinData.Holy),15,"restored holy");
        a.Reset(CharacterReset.Retry);Near(a.Resource(AegisPaladinData.Holy),0,"retry clears holy");Check(a.Snapshot.skills.All(s=>s.charges==definition.Skill(s.id).maximumCharges),"retry restores all charges");
        a.Gain(AegisPaladinData.Holy,150);Near(a.Resource(AegisPaladinData.Holy),100,"gauge caps");a.Reset(CharacterReset.NewRun);Near(a.Resource(AegisPaladinData.Holy),0,"new run clears holy");
        var debt=new AegisDamageState();debt.Activate();Near(debt.Split(400),200,"half immediate");Near(debt.Pending,200,"half deferred");
        Near(debt.Advance(5),100,"linear repayment");Near(debt.Split(100),50,"second independent hit");Near(debt.Advance(3),75,"overlapping debt batches");Check(debt.FaithRemaining==0,"buff ended");Near(debt.Split(80),80,"no new split after expiry");Near(debt.Advance(2),50,"first batch fully paid");Near(debt.Pending,25,"second batch remains after buff");
        var copy=new AegisDamageState();Check(copy.Restore(debt.Capture()),"reconnect debt clone");Near(copy.Advance(5),25,"restored only outstanding damage");Near(debt.Pending,25,"restored clone doesn't mutate host");Near(copy.Advance(5),0,"paid debt cannot replay");
        var invalid=debt.Capture();invalid.hits[0].remaining=double.NaN;Check(!debt.Restore(invalid),"invalid restoration rejected atomically");Near(debt.Pending,25,"invalid frame doesn't overwrite pending debt");
        debt.Clear();Near(debt.Pending,0,"death/retry/end clear pending damage");Near(debt.FaithRemaining,0,"transient buff cleared");
        Check(!debt.ShieldAbsorbed(0),"shield creation/expiry/no absorption no holy");Check(debt.ShieldAbsorbed(1),"first real absorption");Check(!debt.ShieldAbsorbed(100),"same hit or shield slices cannot duplicate holy");debt.Advance(.99f);Check(!debt.ShieldAbsorbed(1),"ICD before one second");debt.Advance(.011f);Check(debt.ShieldAbsorbed(.01),"ICD elapsed even tiny actual absorption");
        var shields=new CombatShieldLedger();shields.GrantTimed(7,"combo",300,3000,5);shields.GrantTimed(7,"combo",300,3000,6);Near(shields.Remaining,300,"same provider skill refresh no stacking");
        shields.GrantTimed(7,"call",150,3000,6);shields.GrantTimed(8,"call",150,3000,6);Near(shields.Remaining,600,"different skill/provider shields coexist");
        var attribution=new Dictionary<int,double>();shields.Absorb(500,(id,used)=>{if(!attribution.ContainsKey(id))attribution[id]=0;attribution[id]+=used;});Near(attribution[7],450,"actual absorption source attribution");Near(attribution[8],50,"second source attribution");Near(shields.Expire(5),0,"refreshed expiry");Near(shields.Expire(6),100,"only remaining shield expires");Near(shields.Remaining,0,"expiry removes remaining");Near(attribution[7],450,"expiry isn't absorption");
        shields.Grant(9,2800);shields.GrantTimed(7,"combo",300,3000,12);Near(shields.Remaining,3000,"shield cap");shields.GrantTimed(7,"combo",300,3000,14);Near(shields.Remaining,3000,"refresh respects other source cap");shields.Reconcile(3000);shields.Expire(14);Near(shields.Remaining,2800,"legacy shield has no new expiry");
        var debuffs=new List<CombatDebuff>{new CombatDebuff{id="mechanic",tag="poison",dispellable=false,remaining=5},new CombatDebuff{id="a",tag="poison",dispellable=true,remaining=5},new CombatDebuff{id="b",tag="slow",dispellable=true,remaining=5}};
        int index=CombatDispel.FirstAllowed(debuffs,AegisPaladinData.DispelTags);Check(index==1,"skip mechanic debuff");debuffs.RemoveAt(index);Check(debuffs.Count==2&&debuffs[0].id=="mechanic","only one dispelled");Check(CombatDispel.FirstAllowed(debuffs,new[]{"other"})==-1,"configured allowed tags");
        Check(!CounterRules.Valid(false,true,true,1,1,120),"normal hit is not counter");Check(!CounterRules.Valid(true,true,false,1,1,120),"counter range required");Check(!CounterRules.Valid(true,true,true,-1,1,120),"counter requires enemy front");Check(!CounterRules.Valid(true,true,true,1,-1,120),"counter requires attacker facing");Check(CounterRules.Valid(true,true,true,1,1,120),"explicit valid window counter");
        var movement=definition.Skill(AegisPaladinData.Id(CharacterSlot.Movement));var dash=new DashMotion();var settings=new DashSettings{distance=movement.movement.ResolveDistance(new DashSettings().distance),duration=movement.movement.duration,cooldown=movement.cooldown,maximumCharges=movement.maximumCharges};
        Check(dash.TryStart(settings,true,1,1,0,1),"first charge");Near(Math.Sqrt(dash.DirectionX*dash.DirectionX+dash.DirectionZ*dash.DirectionZ),1,"diagonal normalized");Near(dash.Step(.2),2.8,"70% of common 4m in .2 seconds");dash.Cancel();Check(dash.TryStart(settings,true,0,0,0,-1),"second charge facing fallback");Near(dash.DirectionZ,-1,"fallback facing");dash.Cancel();Check(!dash.TryStart(settings,true,1,0,0,1),"empty charges rejected");dash.TickCooldown(10);Check(dash.Charges==1,"one sequential recharge");var dashPeer=new DashMotion();dashPeer.Restore(dash.Capture());Check(dashPeer.Charges==1,"reconnect charges");dash.TickCooldown(10);Check(dash.Charges==2,"second recharge");
        var random=new Random(7441);
        for(int trial=0;trial<200;trial++)
        {
            debt=new AegisDamageState();debt.Activate();double confirmed=0,paid=0;
            for(int i=0;i<80;i++)
            {
                double hit=random.NextDouble()*500;confirmed+=hit;paid+=debt.Split(hit);paid+=debt.Advance((float)(random.NextDouble()*.15));
                Near(paid+debt.Pending,confirmed,"conservation at every tick",.001);
            }
            paid+=debt.Advance(30);Near(paid,confirmed,"no lost or duplicated debt",.001);Near(debt.Advance(30),0,"no recursive debt");
        }
        var gate=new RaidActionGate();var req=new RaidActionRequest{actor=2,epoch=3,sequence=1,skill=10,slot=(int)CharacterSlot.Gcd1,jobId=definition.id};
        Check(!gate.Accept(1,3,req),"wrong owner rejected");Check(gate.Accept(2,3,req),"owner accepted");Check(!gate.Accept(2,3,req),"duplicate rejected");Check(!gate.Accept(2,4,req),"stale epoch rejected");
        Check(gate.Accept(2,3,new RaidActionRequest{actor=2,epoch=3,sequence=2,skill=12,jobId=definition.id}),"lobby job request protocol");
        Console.WriteLine("PASS: "+checks+" Aegis definition, combo, shield, holy ICD, debt conservation, dispel, counter, charges and authority checks (no Unity/Steam execution).");
    }
}
