using System;
using System.Linq;
using RaidRogue;
public static class AegisRouteTests
{
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    private static void Near(double actual,double expected,string message,double tolerance=.001)=>Check(Math.Abs(actual-expected)<=tolerance,message+" actual="+actual+" expected="+expected);
    private static readonly CharacterUseContext Ready=new CharacterUseContext(true,true,true,0);
    private static CharacterRuntimeState Actor(string route,float holy=100)
    {var actor=new CharacterRuntimeState(AegisPaladinData.Build());Check(actor.ChooseRoute(route,true),"origin selected");actor.Gain(AegisPaladinData.Holy,holy);return actor;}
    private static JobSkillState Timing(CharacterRuntimeState actor,CharacterSlot slot)=>actor.Snapshot.skills.Single(s=>s.id==actor.Selected(slot).id);
    private static bool UseZ(CharacterRuntimeState actor,AegisRouteState effects,CharacterUseContext? context=null)=>actor.TryUse(CharacterSlot.Identity,context??Ready,0,s=>effects.Activate(actor.Snapshot.routeId,0),out _);
    public static void Main()
    {
        try {SelectionAndStats();CostsAndGcd();ShieldAndReflection();Guard();Sun();RecoveryAndLifecycle();SkillTestTools();LatestSpecification();}
        catch(Exception ex){Console.WriteLine("FAIL after "+checks+": "+ex.Message);Environment.Exit(1);}
        Console.WriteLine("PASS: "+checks+" Aegis origin selection, transaction, shield, guard classification, secondary damage, GCD, geometry and recovery checks (no Unity/Steam execution).");
    }
    private static void SelectionAndStats()
    {
        var definition=AegisPaladinData.Build();var a=new CharacterRuntimeState(definition);var b=new CharacterRuntimeState(definition);
        Check(CharacterDefinitionValidation.Check(new[]{definition}).Length==0,"routes have unique IDs and resolved skills");
        Check(definition.routes.Length==3,"three origins only");
        Check(!a.Unlocked(CharacterSlot.Identity),"no implicit identity before choice");
        Check(!a.ChooseRoute("unknown",true)&&!a.ChooseRoute(AegisRouteData.Bulwark,false),"invalid or post-departure origin rejected");
        float hp=1260,max=3000;
        for(int i=0;i<300;i++)
        {
            string route=definition.routes[i%3].id;
            string previous=a.Selected(CharacterSlot.Identity).id;
            Check(a.ChooseRoute(route,true),"lobby can change repeatedly");
            Check(a.Selected(CharacterSlot.Identity).id==AegisRouteData.Identity(route)&&a.Unlocked(CharacterSlot.Identity),"only chosen Z selected/unlocked");
            if(previous!=a.Selected(CharacterSlot.Identity).id)Check(!a.Snapshot.unlocked.Contains(previous),"old route Z removed");
            float next=AegisRouteData.MaximumHp(definition.baseHp,route);
            hp=AegisRouteData.PreserveHealthRatio(hp,max,next);max=next;
            Near(hp/max,.42,"HP ratio retained without healing drift",.00001);
            Near(max,route==AegisRouteData.Bulwark?3600:3000,"always recompute from base, no multiplicative accumulation");
            Check(!a.ChooseRoute(definition.routes[(i+1)%3].id,false),"combat/reward route immutable");
        }
        Near(definition.baseHp,3000,"shared definition never mutated");Near(b.Resource(AegisPaladinData.Holy),0,"other actor not affected");Check(b.Snapshot.routeId=="","other actor origin independent");
        Near(AegisRouteData.PreserveHealthRatio(0,3000,3600),0,"route swap cannot resurrect");
        var frame=a.Snapshot;frame.selections.Single(s=>s.slot==CharacterSlot.Identity).skillId=AegisRouteData.Identity(AegisRouteData.Bulwark);
        Check(!a.Restore(frame),"inconsistent route/Z snapshot rejected atomically");
        foreach(var slot in new[]{CharacterSlot.Active,CharacterSlot.OffGcd2,CharacterSlot.Ultimate})Check(!a.Unlocked(slot),"R F V remain locked");
    }
    private static void CostsAndGcd()
    {
        foreach(var route in new[]{AegisRouteData.Bulwark,AegisRouteData.Sun,AegisRouteData.Mirror})
        {
            var actor=Actor(route,0);var effects=new AegisRouteState();var z=actor.Selected(CharacterSlot.Identity);
            Check(!UseZ(actor,effects),"insufficient holy rejected");Near(actor.Resource(AegisPaladinData.Holy),0,"failure no spend");Near(Timing(actor,CharacterSlot.Identity).recharge,0,"failure no cooldown");
            actor.Gain(AegisPaladinData.Holy,100);
            foreach(var invalid in new[]{new CharacterUseContext(false,true,true,0),new CharacterUseContext(true,false,true,0),new CharacterUseContext(true,true,false,0)})
            {Check(!UseZ(actor,effects,invalid),"death/action/target validation before handler");Near(actor.Resource(AegisPaladinData.Holy),100,"rejection preserves resource");}
            Check(actor.TryUse(CharacterSlot.Gcd1,Ready,1,s=>true,out _),"base attack starts shared GCD");
            float gcd=actor.Snapshot.gcd;
            Check(UseZ(actor,effects),"Z works during GCD");Near(actor.Resource(AegisPaladinData.Holy),100-z.costs[0].amount,"exact one cost");Near(actor.Snapshot.gcd,gcd,"Z does not reset GCD");
            float remaining=actor.Resource(AegisPaladinData.Holy);
            Check(!UseZ(actor,effects),"same tick repeated input cannot execute twice");Near(actor.Resource(AegisPaladinData.Holy),remaining,"duplicate no spend");
            actor.Gain(AegisPaladinData.Holy,100);actor.Tick(z.cooldown);
            if(route==AegisRouteData.Sun){Check(!UseZ(actor,effects),"active transformation refuses even when CD ready");Near(actor.Resource(AegisPaladinData.Holy),100,"failed effect atomic costs");}
        }
    }
    private static void ShieldAndReflection()
    {
        double max=AegisRouteData.MaximumHp(3000,AegisRouteData.Bulwark);
        Near(max*AegisRouteData.BulwarkShield*AegisRouteData.ShieldMultiplier,1080,"caster maxHP then exactly one outgoing shield modifier");
        Near(max*AegisPaladinData.FinisherShieldRatio*AegisRouteData.ShieldMultiplier,540,"GCD2 same modifier");
        Near(1100*AegisPaladinData.CallShieldRatio*AegisRouteData.ShieldMultiplier,82.5,"E same modifier, recipient basis retained");
        Check(!CombatDamageRules.CanKnockback(4,false)&&CombatDamageRules.CanKnockback(4,true),"normal immunity vs explicit bypass");
        Check(CombatDamageRules.CanKnockback(0,false),"normal knockback resumes after expiry");
        var shield=new CombatShieldLedger();shield.GrantTimed(1,"z",1080,3600,4);shield.GrantTimed(1,"z",1080,3600,5);Near(shield.Remaining,1080,"refresh not stacking");
        shield.GrantTimed(1,"e",200,3600,6);shield.GrantTimed(2,"e",300,3600,6);
        int callbacks=0;double total=0;shield.Absorb(1300,(owner,absorbed)=>{callbacks++;total+=absorbed;if(owner==1)Near(absorbed,1280,"one hit groups provider slices before reaction");});
        Check(callbacks==2,"one callback per actual provider");Near(total,1300,"no lost absorption");
        Near(shield.RemoveSource(2),280,"route/job change removes only old source remaining shield");Near(shield.Remaining,0,"removed shield not absorbed");
        shield.GrantTimed(1,"z",1080,1000,4);Near(shield.Remaining,1000,"existing recipient maxHP shield cap retained");
        var state=new AegisRouteState();
        foreach(CombatDamageKind kind in Enum.GetValues(typeof(CombatDamageKind)))
        {
            state.Clear();double damage=state.Reflect(AegisRouteData.Mirror,kind,400,3000);
            Near(damage,CombatDamageRules.CanReact(kind)?100:0,"reflection damage eligibility "+kind);
            if(damage>0){Near(state.Reflect(AegisRouteData.Mirror,kind,200,3000),0,"reflection ICD across hits");state.Advance(.99f);Near(state.Reflect(AegisRouteData.Mirror,kind,200,3000),0,"ICD not early");state.Advance(.011f);Near(state.Reflect(AegisRouteData.Mirror,kind,200,3000),50,"reflection resumes at one second");}
        }
        state.Clear();Near(state.Reflect(AegisRouteData.Mirror,CombatDamageKind.Direct,100000,3000),300,"reflection maxHP10% cap");
        state.Clear();Near(state.Reflect(AegisRouteData.Sun,CombatDamageKind.Direct,400,3000),0,"wrong origin cannot reflect");Near(state.Reflect(AegisRouteData.Mirror,CombatDamageKind.Direct,0,3000),0,"creation/expiry/no absorption cannot proc");
        Near(CombatDamageRules.SunDamage(double.NaN),0,"nonfinite extra rejected");Near(CombatDamageRules.SunDamage(23.5),2.35,"extra uses actual capped original damage");
    }
    private static void Guard()
    {
        var state=new AegisRouteState();
        foreach(CombatDamageKind kind in Enum.GetValues(typeof(CombatDamageKind)))
        {
            state.Clear();state.Activate(AegisRouteData.Mirror,77);
            bool blocked=state.TryGuard(AegisRouteData.Mirror,kind,false,1,400,3000,out double retaliation);
            Check(blocked==(kind==CombatDamageKind.Direct),"guard classification "+kind);Near(retaliation,blocked?200:0,"only direct retaliation");
        }
        var random=new Random(614);
        for(int i=0;i<5000;i++)
        {
            float angle=(float)(random.NextDouble()*360-180);double incoming=random.NextDouble()*10000;
            state.Clear();state.Activate(AegisRouteData.Mirror,91);
            bool blocked=state.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,false,(float)Math.Cos(angle*Math.PI/180),incoming,3000,out double damage);
            Check(blocked==(Math.Abs(angle)<=60.001),"fixed front 120 degrees randomized boundary");
            Near(damage,blocked?Math.Min(incoming*.5,600):0,"first retaliation ratio/cap");
            if(blocked){Check(state.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,false,1,900,3000,out damage),"subsequent direct attacks also blocked");Near(damage,0,"one retaliation per activation");}
            Near(state.GuardYaw,91,"character/camera input cannot change captured yaw");
        }
        state.Clear();state.Activate(AegisRouteData.Mirror,0);
        Check(!state.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,true,1,400,3000,out _),"explicit bypass direct not guarded");
        Check(state.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,false,.5f,400,3000,out _),"60 degree boundary included");
        state.Advance(1.5f);Check(!state.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,false,1,400,3000,out _),"guard expires exactly");
        Check(!CounterRules.Valid(false,true,true,1,1,120),"guard never manufactures counter window");
        Check(!CounterRules.Valid(true,true,true,-1,1,120),"enemy rear guard not a counter");
    }
    private static void Sun()
    {
        var actor=Actor(AegisRouteData.Sun);var effects=new AegisRouteState();Check(UseZ(actor,effects),"transform activated");
        var original=actor.Selected(CharacterSlot.Gcd1);var second=actor.Selected(CharacterSlot.Gcd2);
        Near(effects.Range(AegisRouteData.Sun,true,original.range),12,"transformed range");Near(original.range,3,"shared skill data intact");
        Near(effects.Range(AegisRouteData.Sun,false,3),3,"oGCD range unaffected");Near(effects.GcdScale(AegisRouteData.Sun,false),1,"oGCD not accelerated");
        int hits=0,finishers=0;
        Func<CharacterSkillDefinition,bool> hit=skill=>{for(int enemy=0;enemy<6;enemy++)hits++;if(actor.Snapshot.combo1==3){finishers++;actor.Gain(AegisPaladinData.Holy,15);}return true;};
        for(int i=0;i<3;i++)
        {
            var context=new CharacterUseContext(true,true,true,12,false,effects.Range(AegisRouteData.Sun,true,3),effects.GcdScale(AegisRouteData.Sun,true));
            Check(actor.TryUse(CharacterSlot.Gcd1,context,1,hit,out _),"12m ranged combo accepted");Near(actor.Snapshot.gcd,1.5/1.3,"base /1.3 GCD");actor.Tick(actor.Snapshot.gcd);
        }
        Check(hits==18&&finishers==1,"multi-enemy handler contract advances once per skill");Near(actor.Resource(AegisPaladinData.Holy),15,"AoE does not multiply finisher holy");
        var boosted=new CharacterUseContext(true,true,true,3,false,12,1/1.3f);
        Check(actor.TryUse(CharacterSlot.Gcd2,boosted,1,s=>true,out _),"second combo shares accelerated GCD");Near(actor.Snapshot.gcd,2.5/1.3,"shield /1.3");Check(actor.Snapshot.combo1==1&&actor.Snapshot.combo2==2,"independent combos");
        float running=actor.Snapshot.gcd;effects.Advance(8);Near(actor.Snapshot.gcd,running,"expiry does not reset running cooldown");Near(effects.Range(AegisRouteData.Sun,true,3),3,"base range restored");Near(effects.GcdScale(AegisRouteData.Sun,true),1,"base GCD restored");
        actor.Tick(running);Check(!actor.TryUse(CharacterSlot.Gcd1,new CharacterUseContext(true,true,true,3.01f,false,effects.Range(AegisRouteData.Sun,true,3)),1,hit,out _),"outside restored range rejects before hit");
        Check(CombatRangeMath.CircleTouches(6,3,3)&&!CombatRangeMath.CircleTouches(6.01,3,3),"AoE enemy hitbox boundary shares geometry");
    }
    private static void RecoveryAndLifecycle()
    {
        foreach(var route in new[]{AegisRouteData.Bulwark,AegisRouteData.Sun,AegisRouteData.Mirror})
        {
            var actor=Actor(route);var damage=new AegisDamageState();Check(UseZ(actor,damage.Route),"identity before loss");damage.Activate();damage.Split(400);damage.Advance(.7f);actor.Tick(.7f);
            var copy=new CharacterRuntimeState(actor.Definition);var restored=new AegisDamageState();
            Check(copy.Restore(actor.Snapshot)&&restored.Restore(damage.Capture()),"reconnect full actor and remaining effect snapshot");
            Near(copy.Resource(AegisPaladinData.Holy),actor.Resource(AegisPaladinData.Holy),"reconnect no cost replay");Near(restored.Pending,damage.Pending,"outstanding debt retained");Near(restored.Route.Guard,damage.Route.Guard,"guard remaining restored");Near(restored.Route.Transformation,damage.Route.Transformation,"transform remaining restored");
            Check(restored.Restore(damage.Capture()),"duplicate snapshot replaces, not reapplies");Near(restored.Pending,damage.Pending,"duplicate snapshot debt not doubled");
            float holy=actor.Resource(AegisPaladinData.Holy);actor.ClearOnDeath();damage.Clear();Near(actor.Resource(AegisPaladinData.Holy),holy,"death preserves holy");Near(damage.Route.Guard+damage.Route.Transformation,0,"death clears temporary identities");Near(damage.Pending,0,"death clears debt");
            actor.Reset(CharacterReset.Retry);Check(actor.Snapshot.routeId==route&&actor.Unlocked(CharacterSlot.Identity),"retry keeps origin and Z");Near(actor.Resource(AegisPaladinData.Holy),0,"retry resource reset");Near(Timing(actor,CharacterSlot.Identity).recharge,0,"retry cooldown reset");
            actor.Reset(CharacterReset.NewRun);Check(actor.Snapshot.routeId==""&&!actor.Unlocked(CharacterSlot.Identity),"fresh run may choose new origin");
        }
        var guard=new AegisRouteState();guard.Activate(AegisRouteData.Mirror,38);guard.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,false,1,400,3000,out _);
        var replica=new AegisRouteState();Check(replica.Restore(guard.Capture()),"restore spent retaliation flag");Check(replica.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,false,1,400,3000,out double retal)&&retal==0,"reconnect cannot repeat retaliation");
        var invalid=guard.Capture();invalid.guard=float.NaN;Check(!guard.Restore(invalid),"invalid effect snapshot refused");Near(guard.Guard,1.5,"rejected restore is atomic");
    }
    private static void SkillTestTools()
    {
        var definition=AegisPaladinData.Build();var actor=new CharacterRuntimeState(definition);
        Check(actor.ApplySkillTest(SkillTestOperation.UnlockImplemented,out int count)&&count==0,"implemented base skills already available");
        Check(!actor.Unlocked(CharacterSlot.Identity),"test unlock cannot silently pick an origin");
        Check(new CharacterRuntimeState(definition).Restore(actor.Snapshot),"pre-origin test snapshot remains valid");
        Check(actor.ApplySkillTest(SkillTestOperation.FillResources,out count)&&count==1,"test fills one resource");
        Near(actor.Resource(AegisPaladinData.Holy),100,"test holy maximum");
        Check(!actor.TryUse(CharacterSlot.Identity,Ready,0,s=>true,out _),"holy alone does not bypass locked origin");
        Check(actor.ApplySkillTest(SkillTestOperation.FillResources,out count)&&count==0,"repeated fill cannot exceed maximum");
        foreach(var slot in new[]{CharacterSlot.Active,CharacterSlot.OffGcd2,CharacterSlot.Ultimate})
        {
            Check(!actor.Unlocked(slot),"missing R/F/V stays unavailable");
            Check(!actor.Validate(slot,Ready,out string reason)&&reason.Contains("구현"),"missing content gives accurate reason");
        }
        actor.ChooseRoute(AegisRouteData.Mirror,true);
        Check(actor.ApplySkillTest(SkillTestOperation.UnlockImplemented,out count)&&count==0,"only selected origin remains unlocked");
        Check(!actor.Snapshot.unlocked.Contains(AegisRouteData.Identity(AegisRouteData.Sun)),"other Z never unlocked by test");
        Check(actor.TryUse(CharacterSlot.Identity,Ready,0,s=>true,out _),"normal Z transaction after test fill");
        Near(actor.Resource(AegisPaladinData.Holy),70,"test mode retains actual cost");
        Check(!actor.TryUse(CharacterSlot.Identity,Ready,0,s=>true,out _),"test mode retains cooldown enforcement");
        Check(actor.TryUse(CharacterSlot.Gcd1,Ready,1,s=>true,out _),"start combo before test reset");
        Check(actor.TryUse(CharacterSlot.Movement,Ready,0,s=>true,out _),"spend dash charge before test reset");
        Check(actor.TryUse(CharacterSlot.Potion,Ready,0,s=>true,out _),"spend potion before test reset");
        var before=actor.Snapshot;var dash=Timing(actor,CharacterSlot.Movement);var potion=Timing(actor,CharacterSlot.Potion);
        Check(actor.ApplySkillTest(SkillTestOperation.RefreshJobCooldowns,out count)&&count>=2,"refresh Z and shared GCD");
        Check(Timing(actor,CharacterSlot.Identity).charges==1&&actor.Snapshot.gcd==0,"Z available after explicit refresh");
        Near(actor.Resource(AegisPaladinData.Holy),70,"refresh never grants resource");
        Check(Timing(actor,CharacterSlot.Movement).charges==dash.charges,"refresh leaves actual dash charges alone");
        Near(Timing(actor,CharacterSlot.Movement).recharge,dash.recharge,"refresh leaves dash timer");
        Check(Timing(actor,CharacterSlot.Potion).charges==potion.charges,"refresh leaves potion");
        Near(Timing(actor,CharacterSlot.Potion).recharge,potion.recharge,"refresh leaves potion timer");
        Check(actor.Snapshot.combo1==before.combo1&&actor.Snapshot.comboTime1==before.comboTime1,"refresh retains combo state");
        Check(actor.Snapshot.routeId==before.routeId&&actor.Snapshot.clearedStage==0,"test does not advance progression");
        Check(!actor.ApplySkillTest((SkillTestOperation)999,out count)&&count==0,"invalid test command rejected");
        var casting=actor.Snapshot;casting.castSkillId=actor.Selected(CharacterSlot.Gcd1).id;casting.castRemaining=casting.castTotal=3;
        Check(actor.Restore(casting),"casting fixture");
        Check(!actor.ApplySkillTest(SkillTestOperation.RefreshJobCooldowns,out count),"refresh during cast rejected");
        Check(actor.Snapshot.castRemaining==3&&actor.Snapshot.castSkillId==casting.castSkillId,"failed refresh does not cancel cast");

        // A future implemented progression skill may be tested without pretending a stage was cleared.
        var future=AegisPaladinData.Build();var active=future.Skill(AegisPaladinData.Id(CharacterSlot.Active));
        active.implemented=true;active.handlerId="fixture.active";active.cooldown=10;
        var tester=new CharacterRuntimeState(future);var peer=new CharacterRuntimeState(future);
        Check(tester.ApplySkillTest(SkillTestOperation.UnlockImplemented,out count)&&count==1,"implemented locked selection can be tested");
        Check(tester.Unlocked(CharacterSlot.Active)&&!peer.Unlocked(CharacterSlot.Active),"actor-only unlock, no shared state");
        Check(!future.slots.Single(s=>s.slot==CharacterSlot.Active).initiallyUnlocked,"definition is unchanged");
        Check(tester.Snapshot.chosenRules.Length==0&&tester.Snapshot.clearedStage==0,"unlock does not award cards or stages");
        Check(peer.Restore(tester.Snapshot)&&peer.Unlocked(CharacterSlot.Active),"reconnect snapshot retains test unlock");
        tester.Reset(CharacterReset.Retry);Check(tester.Unlocked(CharacterSlot.Active),"retry keeps unlocked skills under existing rules");
        tester.Reset(CharacterReset.NewRun);Check(!tester.Unlocked(CharacterSlot.Active),"new run clears test unlock");
        Near(tester.Resource(AegisPaladinData.Holy),0,"new run resets test resource");
        var unavailable=new CharacterRuntimeState(CharacterCatalog.Find("job.berserker"));
        foreach(SkillTestOperation command in Enum.GetValues(typeof(SkillTestOperation)))
            Check(!unavailable.ApplySkillTest(command,out count),"test tools do not enable unimplemented jobs");
        Check(!new CharacterRuntimeState(CharacterCatalog.Legacy(1)).ApplySkillTest(SkillTestOperation.RefreshJobCooldowns,out count),"legacy timing owner is not bypassed");
    }
    private static void LatestSpecification()
    {
        var definition=AegisPaladinData.Build();
        var movement=definition.Skill(AegisPaladinData.Id(CharacterSlot.Movement));
        Near(AegisRouteData.MoveSpeed(definition,0),7.02,"90% movement base");
        Near(movement.movement.ResolveDistance(4),2.8,"common dash relationship 70%");
        Near(movement.movement.ResolveDistance(6),4.2,"common dash tuning propagates");
        Near(AegisRouteData.MoveSpeed(definition,1,1.35f),7.8*.9*1.3*1.35,"separate job/card/temporary layers");
        Near(AegisRouteData.GcdDuration(1.5f,.4f,.6f,1/1.3f),1.1/1.3,"existing card reduction before transformation haste");
        Near(AegisRouteData.GcdDuration(1.5f,1.4f,.6f,1),.6,"existing minimum GCD applied before route");
        var hasteActor=new CharacterRuntimeState(definition);
        hasteActor.TryUse(CharacterSlot.Gcd2,Ready,1,s=>true,out _);hasteActor.TryUse(CharacterSlot.OffGcd1,Ready,1,s=>true,out _);
        hasteActor.Tick(1,false,true,1.35f);
        Near(hasteActor.Snapshot.gcd,1.15,"existing party haste accelerates shared GCD only");
        Near(Timing(hasteActor,CharacterSlot.OffGcd1).recharge,7,"party haste does not accelerate oGCD");
        Near(hasteActor.Snapshot.comboTime2,7,"combo expiry retains real combat seconds");
        hasteActor.Tick(.25f,false,true,float.NaN);Near(hasteActor.Snapshot.gcd,.9,"invalid haste cannot poison runtime state");
        Check(movement.maximumCharges==2&&movement.cooldown==10,"two sequential 10s charges");
        Near(AegisRouteData.Mitigate(1000,definition.physicalDefense),750,"base tank mitigation unchanged");
        Near(AegisRouteData.DefenseRating(definition.magicDefense,AegisRouteData.Bulwark),120,"magic defense 120%");
        Near(AegisRouteData.Mitigate(1000,AegisRouteData.DefenseRating(definition.physicalDefense,AegisRouteData.Bulwark)),1000*300.0/420,"defense rating, not 20% damage reduction");
        Near(AegisRouteData.ControlDuration(4,definition.tenacity),3,"tenacity reduces ordinary control duration");
        Check(!CombatDamageRules.CanApplyControl(4,false)&&CombatDamageRules.CanApplyControl(4,true)&&CombatDamageRules.CanApplyControl(0,false),"control immunity expiry and bypass");
        for(int i=0;i<300;i++)
        {
            string route=definition.routes[i%3].id;
            Near(AegisRouteData.MaximumHp(definition.baseHp,route),route==AegisRouteData.Bulwark?3600:3000,"origin recalculation maxHP stable");
            Near(AegisRouteData.DefenseRating(definition.physicalDefense,route),route==AegisRouteData.Bulwark?120:100,"origin recalculation defense stable");
            Near(AegisRouteData.MoveSpeed(definition,1),7.8*.9*1.3,"origin cannot compound movement");
            Near(movement.movement.ResolveDistance(4),2.8,"movement penalty does not compound dash");
        }
        var settings=new DashSettings{distance=movement.movement.ResolveDistance(4),duration=.2f,cooldown=10,maximumCharges=2};
        var dash=new DashMotion();Check(dash.TryStart(settings,true,1,1,0,1),"begin dash before counter reward");
        Near(dash.Step(.1),1.4,"first half of diagonal dash");dash.RefillCharges(settings);
        Check(dash.Active&&dash.Charges==2&&dash.RechargeRemaining==0,"counter refill preserves active trajectory");
        Near(dash.Step(.1),1.4,"counter refill does not extend dash");
        Check(!dash.Active,"dash ends at original time");
        dash.TryStart(settings,true,0,1,0,1);dash.Step(.2);dash.TryStart(settings,true,0,1,0,1);dash.Step(.2);
        Check(dash.Charges==0,"empty dash before opportunity");
        var opportunity=new CounterOpportunity();opportunity.Open(10,3);int id=opportunity.Id,refills=0;
        Check(!opportunity.Consume(id,11,false),"normal hit cannot consume counter window");
        for(int i=0;i<8;i++)if(opportunity.Consume(id,11,true)){dash.RefillCharges(settings);refills++;}
        Check(refills==1&&dash.Charges==2,"one opportunity gives exactly one complete refill");
        opportunity.Open(12,3);Check(!opportunity.Consume(id,13,true),"old opportunity callback cannot consume newer window");
        Check(!opportunity.Consume(opportunity.Id,float.NaN,true),"invalid server time rejected");
        Check(!opportunity.Consume(opportunity.Id,-1,true),"negative time cannot claim an opportunity");
        Check(!opportunity.Consume(opportunity.Id,15,true),"counter at expired window rejected");
        opportunity.Open(16,3);id=opportunity.Id;opportunity.Clear();Check(!opportunity.Consume(id,17,true),"retry/phase clears counter opportunity");
        var replicaDash=new DashMotion();replicaDash.Restore(dash.Capture());Check(replicaDash.Charges==2&&replicaDash.RechargeRemaining==0,"full counter charges replicate");

        var caster=new CharacterRuntimeState(definition);int hits=0;
        Check(!caster.TryUse(CharacterSlot.Counter,new CharacterUseContext(true,true,true,3.1f),1,s=>{hits++;return true;},out _),"invalid counter range does not start rush cast");
        Check(caster.Snapshot.castSkillId==""&&Timing(caster,CharacterSlot.Counter).charges==1,"failed rush costs nothing");
        Check(caster.TryUse(CharacterSlot.Counter,Ready,1,s=>{hits++;return true;},out _),"counter starts short rush cast");
        Check(hits==0&&Timing(caster,CharacterSlot.Counter).charges==1,"counter hit and cooldown wait for arrival");
        caster.Tick(AegisPaladinData.CounterRushDuration,true);Check(caster.FinishCast(Ready,s=>{hits++;return true;},out _),"own rush movement does not cancel counter");
        Check(hits==1&&Timing(caster,CharacterSlot.Counter).charges==0,"counter completion commits once");
        Check(!caster.FinishCast(Ready,s=>{hits++;return true;},out _)&&hits==1,"completion callback cannot replay");
        caster.Reset(CharacterReset.Retry);caster.TryUse(CharacterSlot.Counter,Ready,1,s=>true,out _);caster.CancelCast();
        Check(!caster.FinishCast(Ready,s=>true,out _)&&Timing(caster,CharacterSlot.Counter).charges==1,"interrupted rush no attack or cooldown");

        var gate=new CombatEventGate();var damage=new AegisDamageState();damage.Route.Activate(AegisRouteData.Mirror,0);
        int blocked=0,holy=0,retaliations=0;double passiveReflection=0;
        for(int i=0;i<5;i++)if(gate.Accept(12)&&damage.Route.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,false,1,400,3000,out double reflected))
        {blocked++;if(damage.ShieldAbsorbed(400))holy+=5;if(reflected>0)retaliations++;passiveReflection+=damage.Route.Reflect(AegisRouteData.Mirror,CombatDamageKind.Direct,400,3000);}
        Check(blocked==1&&holy==5&&retaliations==1,"one hit cannot duplicate guard, resource or retaliation");
        Near(passiveReflection,100,"actual guard grants one capped passive reflection");
        Near(damage.Route.Reflect(AegisRouteData.Mirror,CombatDamageKind.Direct,200,3000),0,"guard and shield reflection share ICD");
        Check(!damage.ShieldAbsorbed(100),"guard and shield paths share resource ICD");
        damage.Advance(1.1f);
        Check(gate.Accept(13)&&damage.Route.TryGuard(AegisRouteData.Mirror,CombatDamageKind.Direct,false,1,200,3000,out double repeated)&&repeated==0,"second attack blocked but no second shockwave");
        Check(damage.ShieldAbsorbed(200),"next real block after ICD gives resource");
        Near(damage.Route.Reflect(AegisRouteData.Mirror,CombatDamageKind.Direct,200,3000),50,"second block may reflect after ICD without repeating Z wave");
        var copyDamage=new AegisDamageState();Check(copyDamage.Restore(damage.Capture()),"reconnect replaces guard and passive ICD state");
        Near(copyDamage.Route.Reflect(AegisRouteData.Mirror,CombatDamageKind.Direct,200,3000),0,"reconnect cannot reset reflection ICD");
        damage.Advance(1);Check(!gate.Accept(12),"old callback after guard expiry cannot apply original damage");
        gate.Clear();Check(gate.Accept(12),"new encounter resets recipient event history");
        foreach(var kind in new[]{CombatDamageKind.Deferred,CombatDamageKind.Reflection,CombatDamageKind.Extra,CombatDamageKind.System})
            Check(!CombatDamageRules.CanGuard(kind,false)&&!CombatDamageRules.CanReact(kind),"no recursive secondary defense proc");
        var debt=new AegisDamageState();debt.Activate();
        double confirmed=AegisRouteData.Mitigate(1000,120)-200;
        double immediate=debt.Split(confirmed);Near(immediate+debt.Advance(10),confirmed,"armor then shield then Q conserves confirmed damage");
    }
}
