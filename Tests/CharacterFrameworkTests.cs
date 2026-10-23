using System;
using System.Linq;
using RaidRogue;
internal static class CharacterFrameworkTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static CharacterUseContext Ready(float distance=1,bool moving=false)=>new CharacterUseContext(true,true,true,distance,moving);
    static CharacterDefinition Definition()
    {
        var d=new CharacterDefinition{id="test.framework-fixture",name="fixture",playable=true,role=CharacterRole.Tank};
        d.resources=new[]{new JobResourceDefinition{id="gauge",name="Gauge",maximum=100,initial=50},new JobResourceDefinition{id="ammo",name="Ammo",maximum=6,initial=3,integer=true,resetOnStage=false}};
        d.skills=new[]{
            new CharacterSkillDefinition{id="chain.a",implemented=true,handlerId="test",usesGcd=true,comboChain=1,range=2,gcd=1,comboWindow=8,costs=new[]{new ResourceCost{resourceId="gauge",amount=10}}},
            new CharacterSkillDefinition{id="chain.b",implemented=true,handlerId="test",usesGcd=true,comboChain=2,range=2,gcd=1},
            new CharacterSkillDefinition{id="charges",implemented=true,handlerId="test",maximumCharges=2,cooldown=5,usableWhileCasting=true,costs=new[]{new ResourceCost{resourceId="ammo",amount=1}}},
            new CharacterSkillDefinition{id="cast",implemented=true,handlerId="test",castTime=3,cooldown=10,range=20,cancelOnMovement=true,costs=new[]{new ResourceCost{resourceId="gauge",amount=15}}},
            new CharacterSkillDefinition{id="identity.a",implemented=true,handlerId="test"},new CharacterSkillDefinition{id="identity.b",implemented=true,handlerId="test"},
            new CharacterSkillDefinition{id="active",implemented=true,handlerId="test"},new CharacterSkillDefinition{id="off",implemented=true,handlerId="test"},
            new CharacterSkillDefinition{id="ultimate.a",implemented=true,handlerId="test"},new CharacterSkillDefinition{id="ultimate.b",implemented=true,handlerId="test"},
            new CharacterSkillDefinition{id="missing",implemented=false}};
        d.slots=new[]{Slot(CharacterSlot.Gcd1,"chain.a"),Slot(CharacterSlot.Gcd2,"chain.b"),Slot(CharacterSlot.OffGcd1,"charges"),Slot(CharacterSlot.Job1,"cast"),Slot(CharacterSlot.Job2,"missing"),Slot(CharacterSlot.Identity,"identity.a",false),Slot(CharacterSlot.Active,"active",false),Slot(CharacterSlot.OffGcd2,"off",false),Slot(CharacterSlot.Ultimate,"ultimate.a",false)};
        d.routes=new[]{new CharacterRouteDefinition{id="route.a",identitySkillId="identity.a"},new CharacterRouteDefinition{id="route.b",identitySkillId="identity.b"}};
        d.unlocks=new[]{new CharacterUnlockDefinition{id="s1",slot=CharacterSlot.Active,afterStage=1,choices=new[]{"active"}},new CharacterUnlockDefinition{id="s2",slot=CharacterSlot.OffGcd2,afterStage=2,choices=new[]{"off"}},new CharacterUnlockDefinition{id="s3",slot=CharacterSlot.Ultimate,afterStage=3,choices=new[]{"ultimate.a","ultimate.b"}}};return d;
    }
    static CharacterSlotDefinition Slot(CharacterSlot slot,string id,bool open=true)=>new CharacterSlotDefinition{slot=slot,skillId=id,initiallyUnlocked=open};
    static void Catalog()
    {
        Check(CharacterSlots.All.Length==12,"11 class actions plus independent potion");
        Check(CharacterDefinitionValidation.Check(CharacterCatalog.All).Length==0,"built-in definitions valid");
        Check(CharacterCatalog.All.Count(d=>!d.playable)==12,"exactly 12 registered unavailable classes");
        Check(CharacterCatalog.All.Count(d=>d.playable)==4,"four old playable fixtures remain");
        foreach(var d in CharacterCatalog.All.Where(x=>!x.playable))
        {var state=new CharacterRuntimeState(d);Check(!state.TryUse(CharacterSlot.Potion,Ready(),0,s=>throw new Exception("unimplemented actor executed"),out _),"unavailable jobs never execute");}
        Check(CharacterCatalog.Find("job.gunslinger").classification==AttackClassification.RangedPhysical,"ranged physical classified separately");
        Check(CharacterCatalog.Find("job.sylvan-oracle").role==CharacterRole.Healer,"healer identity stable");
        var a=Definition();Check(CharacterDefinitionValidation.Check(new[]{a,a}).Length>0,"duplicate job ID");
        a.slots[0].skillId="absent";Check(CharacterDefinitionValidation.Check(new[]{a}).Length>0,"missing skill reference");
        a=Definition();a.skills[0].costs[0].resourceId="absent";Check(CharacterDefinitionValidation.Check(new[]{a}).Length>0,"missing resource reference");
        a=Definition();a.resources[1].maximum=2.5f;Check(CharacterDefinitionValidation.Check(new[]{a}).Length>0,"integer resource definition");
        a=Definition();a.skills[0].maximumCharges=0;Check(CharacterDefinitionValidation.Check(new[]{a}).Length>0,"zero charges invalid");
        a=Definition();a.skills[0].legacyAction=1;Check(CharacterDefinitionValidation.Check(new[]{a}).Length>0,"new jobs cannot recurse into role-specific legacy dispatcher");
        a=Definition();a.routes[0].identitySkillId="unknown";Check(CharacterDefinitionValidation.Check(new[]{a}).Length>0,"route skill reference validation");
        a=Definition();a.id="job.aegis-paladin";Check(!CharacterCatalog.Register(a,out _),"reserved ID cannot be silently duplicated");
        Check(CharacterCatalog.Register(a,out _,true)&&CharacterCatalog.Find(a.id)==a,"explicit implementation keeps reserved stable ID");
        Check(!CharacterCatalog.Register(Definition(),out _,true),"unregistered implementation cannot replace arbitrary entry");
        Check(!CharacterCatalog.Register(a,out _,true),"second implementation asset rejected");CharacterCatalog.ResetCatalog();
    }
    static void Transactions()
    {
        var d=Definition();var a=new CharacterRuntimeState(d);var b=new CharacterRuntimeState(d);int effects=0;Func<CharacterSkillDefinition,bool> apply=s=>{effects++;return true;};
        foreach(var c in new[]{new CharacterUseContext(false,true,true,1),new CharacterUseContext(true,false,true,1),new CharacterUseContext(true,true,false,1),Ready(2.01f),Ready(float.NaN)})
        {Check(!a.TryUse(CharacterSlot.Gcd1,c,1,apply,out _),"invalid action refused");Check(a.Resource("gauge")==50&&a.Snapshot.gcd==0&&a.Snapshot.combo1==1&&effects==0,"invalid action atomic");}
        Check(!a.TryUse(CharacterSlot.Identity,Ready(),0,apply,out _),"locked slot refused");
        Check(!a.TryUse(CharacterSlot.Job2,Ready(),0,apply,out _),"missing implementation refused");
        Check(!a.TryUse(CharacterSlot.Gcd1,Ready(),1,s=>false,out _)&&a.Resource("gauge")==50&&a.Snapshot.gcd==0,"failed effect has no costs");
        Check(a.TryUse(CharacterSlot.Gcd1,Ready(2),1,apply,out _),"range edge succeeds");
        Check(effects==1&&a.Resource("gauge")==40&&a.Snapshot.combo1==2&&a.Snapshot.combo2==1,"confirmed transaction advances only chain one");
        Check(b.Resource("gauge")==50&&b.Snapshot.gcd==0&&b.Snapshot.combo1==1,"same shared definition independent state");
        Check(!a.TryUse(CharacterSlot.Gcd2,Ready(),1,apply,out _),"shared GCD blocks other chain");
        Check(a.TryUse(CharacterSlot.OffGcd1,Ready(),1,apply,out _),"oGCD works during GCD");
        a.Tick(1);Check(a.TryUse(CharacterSlot.Gcd2,Ready(),1,apply,out _)&&a.Snapshot.combo1==2&&a.Snapshot.combo2==2,"independent chain two");
        a.Tick(8);Check(a.Snapshot.combo1==1&&a.Snapshot.combo2==1,"independent combo expiry");
        Check(!a.Gain("ammo",.5f)&&!a.Gain("gauge",float.NaN)&&!a.Gain("gauge",-1)&&!a.Gain("missing",1),"resource validation");
        Check(a.Gain("ammo",100)&&a.Resource("ammo")==6&&a.Gain("gauge",100)&&a.Resource("gauge")==100,"resource caps");
        var clone=a.Snapshot;clone.resources[0].value=0;clone.skills[0].charges=0;Check(a.Resource("gauge")==100&&a.Snapshot.skills[0].charges==1,"snapshot arrays isolated");
        var charges=new CharacterRuntimeState(d);Check(charges.TryUse(CharacterSlot.OffGcd1,Ready(),1,apply,out _)&&charges.TryUse(CharacterSlot.OffGcd1,Ready(),1,apply,out _),"two charges available");
        int before=effects;Check(!charges.TryUse(CharacterSlot.OffGcd1,Ready(),1,apply,out _)&&effects==before,"third charge rejected");
        charges.Tick(4.99f);Check(!charges.TryUse(CharacterSlot.OffGcd1,Ready(),1,apply,out _),"not recharged early");
        charges.Tick(.02f);Check(charges.TryUse(CharacterSlot.OffGcd1,Ready(),1,apply,out _)&&charges.Resource("ammo")==0,"recharge and integer cost");
        charges.Tick(100);Check(!charges.TryUse(CharacterSlot.OffGcd1,Ready(),1,apply,out _),"resource check even after cooldown");
        for(int i=0;i<400;i++){var first=new CharacterRuntimeState(d);var second=new CharacterRuntimeState(d);first.Gain("gauge",i);first.TryUse(CharacterSlot.OffGcd1,Ready(),0,apply,out _);Check(second.Resource("gauge")==50&&second.Resource("ammo")==3&&second.Snapshot.skills[2].charges==2,"many instances isolated");}
    }
    static void CastingAndRecovery()
    {
        var d=Definition();var a=new CharacterRuntimeState(d);int effects=0;Func<CharacterSkillDefinition,bool> apply=s=>{effects++;return true;};
        Check(a.TryUse(CharacterSlot.Job1,Ready(),42,apply,out _)&&effects==0&&a.Resource("gauge")==50,"cast only reserves pending state");
        Check(!a.FinishCast(Ready(),apply,out _),"cast cannot finish early");
        a.Tick(1,true);Check(a.Snapshot.castSkillId==""&&a.Resource("gauge")==50&&a.Snapshot.skills[3].charges==1,"movement cancellation no costs");
        a.TryUse(CharacterSlot.Job1,Ready(),42,apply,out _);a.Tick(1,false,false);Check(a.Snapshot.castSkillId==""&&effects==0,"death cancels");
        a.TryUse(CharacterSlot.Job1,Ready(),42,apply,out _);a.Tick(1);var recovery=a.Snapshot;
        var peer=new CharacterRuntimeState(d);Check(peer.Restore(recovery)&&peer.Snapshot.castRemaining==2&&peer.Snapshot.castTarget==42,"restore current cast, not replay");
        Check(peer.Restore(recovery)&&effects==0,"duplicate snapshot never executes effects");
        a.Tick(2);Check(!a.FinishCast(new CharacterUseContext(true,true,false,1),apply,out _)&&effects==0&&a.Resource("gauge")==50,"target death cancellation no cost");
        a.TryUse(CharacterSlot.Job1,Ready(),42,apply,out _);a.Tick(3);Check(a.FinishCast(Ready(),apply,out _)&&effects==1&&a.Resource("gauge")==35,"cast commits once");
        Check(!a.FinishCast(Ready(),apply,out _)&&effects==1,"duplicate completion refused");
        a.CancelCast();
        var valid=a.Snapshot;var bad=valid.Copy();bad.resources[0].value=float.NaN;Check(!peer.Restore(bad),"reject NaN snapshot");
        bad=valid.Copy();bad.jobId="wrong";Check(!peer.Restore(bad),"wrong job rejected");
        bad=valid.Copy();bad.skills[0].charges=99;Check(!peer.Restore(bad),"charge cap on restore");
        bad=valid.Copy();bad.selections[0].skillId="ultimate.a";Check(!peer.Restore(bad),"wrong slot skill rejected");
        bad=valid.Copy();bad.chosenRules=new[]{"unknown"};Check(!peer.Restore(bad),"unknown unlock rule rejected");
        Check(peer.Restore(valid)&&peer.Resource("gauge")==35,"valid state after malformed frames");
        peer.CancelCast();Check(peer.Resource("gauge")==35&&peer.Snapshot.skills[3].charges==0,"handoff cancels input/cast, preserves resource/cooldown");
    }
    static void GrowthAndReset()
    {
        var a=new CharacterRuntimeState(Definition());
        Check(!a.ChooseRoute("route.b",false)&&a.ChooseRoute("route.b",true)&&a.ChooseRoute("route.a",true)&&a.ChooseRoute("route.b",true),"route changes only before departure");
        Check(a.Unlocked(CharacterSlot.Identity)&&a.Selected(CharacterSlot.Identity).id=="identity.b","route unlocks and selects Z");
        Check(!a.ChooseSkill("s1","active"),"stage unlock before clear rejected");a.ClearedStage(1);
        Check(a.ChooseSkill("s1","active")&&!a.ChooseSkill("s1","active")&&!a.ChooseSkill("s2","off"),"stage1 one-time R");
        a.ClearedStage(3);Check(a.ChooseSkill("s2","off")&&!a.ChooseSkill("s3","unoffered")&&a.ChooseSkill("s3","ultimate.b"),"stage2 F and stage3 V choice");
        a.SetBuildEffect("card.stable-id",2,1.2f);a.Gain("ammo",2);a.TryUse(CharacterSlot.Gcd1,Ready(),0,s=>true,out _);
        var b=new CharacterRuntimeState(Definition());Check(b.Restore(a.Snapshot)&&b.Snapshot.effects[0].stacks==2&&b.Selected(CharacterSlot.Ultimate).id=="ultimate.b","route/cards/unlocks restore");
        a.Reset(CharacterReset.NextStage);Check(a.Resource("gauge")==50&&a.Resource("ammo")==5&&a.Unlocked(CharacterSlot.Ultimate)&&a.Snapshot.effects.Length==1&&a.Snapshot.combo1==1,"stage resource policies plus build retained");
        a.Reset(CharacterReset.Retry);Check(a.Resource("ammo")==3&&a.Snapshot.skills.All(s=>s.recharge==0)&&a.Snapshot.routeId=="route.b"&&a.Snapshot.effects.Length==1,"retry clears combat state, retains growth");
        a.Reset(CharacterReset.NewRun);Check(a.Snapshot.routeId==""&&!a.Unlocked(CharacterSlot.Identity)&&!a.Unlocked(CharacterSlot.Ultimate)&&a.Snapshot.effects.Length==0&&a.Snapshot.clearedStage==0,"new run fully resets build");
    }
    static void MovementCharges()
    {
        var settings=new DashSettings{distance=4,duration=.2f,cooldown=5,maximumCharges=2};var motion=new DashMotion();
        Check(!motion.TryStart(settings,false,1,1,0,1)&&motion.Charges==1&&motion.RechargeRemaining==0,"blocked dash leaves state untouched");
        Check(motion.TryStart(settings,true,1,1,0,1)&&motion.Charges==1,"first movement charge");
        Check(Math.Abs(motion.Step(.2)-4)<.0001,"diagonal distance");
        Check(motion.TryStart(settings,true,0,0,1,0)&&motion.Charges==0,"second charge facing fallback");
        motion.Step(.2);Check(!motion.TryStart(settings,true,1,0,1,0),"empty movement charges rejected");
        var restored=new DashMotion();restored.Restore(motion.Capture());Check(restored.Charges==0&&restored.MaximumCharges==2&&restored.CooldownRemaining==5,"movement charges reconnect");
        var packet=new RaidPacket{Kind=RaidPacketKind.Snapshot,Tick=5};packet.Poses.Add(new RaidPose{Id=1,Role=1,Name="fixture",Dash=motion.Capture()});
        Check(RaidWire.Decode(RaidWire.Encode(packet),out var decoded)&&decoded.Poses[0].Dash.MaximumCharges==2&&decoded.Poses[0].Dash.Charges==0,"wire roundtrip keeps charges/recharge");
        packet.Poses[0].Dash=new DashMotion.Snapshot{MaximumCharges=99,Charges=99};Check(!RaidWire.Decode(RaidWire.Encode(packet),out _),"malformed movement charges rejected");
        restored.TickCooldown(5);Check(restored.Charges==1&&restored.CooldownRemaining==0,"recharge one at time");
        restored.ResetForRetry();Check(restored.Charges==2&&restored.RechargeRemaining==0,"retry refills movement charges");
        var gate=new RaidActionGate();var request=new RaidActionRequest{actor=2,epoch=7,sequence=1,skill=10,jobId="test.framework-fixture",slot=1};
        Check(!gate.Accept(1,7,request)&&gate.Accept(2,7,request)&&!gate.Accept(2,7,request),"common actions use existing owner/replay gate");
    }
    public static int Main()
    {
        try {Catalog();Transactions();CastingAndRecovery();GrowthAndReset();MovementCharges();Console.WriteLine("PASS: "+checks+" character catalog, independent state, atomic use, combos, resources, casts, growth, recovery, reset and movement charge checks.");return 0;}
        catch(Exception e){Console.WriteLine("FAIL: "+e.Message);return 1;}
    }
}
