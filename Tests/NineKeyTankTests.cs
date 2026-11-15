using System;
using System.Linq;
using System.Web.Script.Serialization;
using RaidRogue;

static class NineKeyTankTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Main()
    {
        try
        {
            foreach(var job in CharacterRosterData.Jobs.Where(NineKeyTankData.Supports))
            {
                var definition=NineKeyTankData.Build(job);
                var errors=CharacterDefinitionValidation.Check(new[]{definition});
                Check(errors.Length==0,"definition valid: "+string.Join(";",errors));
                Check(definition.slots.Count(s=>s.slot!=CharacterSlot.Potion)==9,"nine combat keys only");
                Check(!definition.slots.Any(s=>s.slot==CharacterSlot.Job2||s.slot==CharacterSlot.Active),"no extra E/Z slots");
                Check(definition.resources.First(r=>r.id==job.Resource).maximum==(job.index==1?100:4),"blood and magazine bounds");
                Check(definition.routes.All(r=>r.capacityOverrides.Length==0),"no legacy six round route override");
                Check(definition.loadouts.Count(r=>r.slot==0)==3&&definition.loadouts.Where(r=>r.slot==0).All(r=>r.choices.Length==2),"two core directions per route");
                Check(definition.unlocks.Length==1&&definition.unlocks[0].afterStage==3,"V retains story gate, no obsolete R/F rewards");
                foreach(var route in definition.routes)
                {
                    var state=new CharacterRuntimeState(definition);Check(state.ChooseRoute(route.id,true),"choose new route");
                    Check(state.Unlocked(CharacterSlot.Identity)&&state.Unlocked(CharacterSlot.OffGcd2),"R route and F available");
                    Check(!state.Unlocked(CharacterSlot.Ultimate),"V cannot bypass story gate");
                    foreach(var slot in new[]{CharacterSlot.Counter,CharacterSlot.Job1,CharacterSlot.Movement,CharacterSlot.OffGcd2})
                        Check(state.Selected(slot).costs.Length==0,"mandatory response independent of resource");
                    foreach(var rule in definition.loadouts.Where(r=>string.IsNullOrEmpty(r.routeId)||r.routeId==route.id))
                    foreach(var choice in rule.choices)
                    {
                        Check(state.ChangeLoadout(rule.id,choice,true),"each supported loadout choice works");
                        Check(new CharacterRuntimeState(definition).Restore(state.Snapshot),"new build roundtrip");
                    }
                }
                foreach(int routeIndex in Enumerable.Range(0,3))foreach(int variant in Enumerable.Range(0,3))
                {
                    var old=new CharacterRuntimeState(CharacterRosterData.Definition(job));
                    Check(old.ChooseRoute(old.Definition.routes[routeIndex].id,true),"legacy route fixture");old.BeginRun("nine-tank",CharacterRewardKey.CurrentChapter);
                    for(int stage=0;stage<=3;stage++)
                    {
                        if(stage>0){old.ClearedStage(stage);var rule=old.Definition.unlocks[stage-1];Check(old.ChooseSkill(rule.id,rule.choices[variant%rule.choices.Length]),"legacy choice fixture");}
                        old.Gain(job.Resource,100);
                        var snapshot=old.Snapshot;var upgraded=new CharacterRuntimeState(definition);
                        Check(upgraded.Restore(snapshot),"legacy stage/route/choice migrates");
                        Check(snapshot.selections.Length==12&&snapshot.resources.Length==1,"migration leaves old snapshot intact");
                        Check(upgraded.RouteId==snapshot.routeId&&upgraded.Snapshot.clearedStage==stage,"migration retains progression");
                        Check(upgraded.Resource(job.Resource)==(job.index==1?100:4),"old resource clamped only to new cap");
                        Check(upgraded.Resource(NineKeyTankData.Gauge(job))==0,"migration cannot grant V charge");
                        Check(upgraded.Selected(CharacterSlot.OffGcd2).id==NineKeyTankData.Id(job,"f"),"old offensive F cannot replace taunt");
                        Check(upgraded.Unlocked(CharacterSlot.Ultimate)==(stage>=3),"migration preserves V lock");
                        Check(new CharacterRuntimeState(definition).Restore(upgraded.Snapshot),"migration idempotent");
                    }
                }
                var cooldown=new CharacterRuntimeState(CharacterRosterData.Definition(job));
                Check(cooldown.TryUse(CharacterSlot.Job2,new CharacterUseContext(true,true,true,0),1,s=>true,out _),"legacy E cooldown fixture");
                var converted=new CharacterRuntimeState(definition);Check(converted.Restore(cooldown.Snapshot),"cooldown migration");
                Check(!converted.TryUse(CharacterSlot.OffGcd2,new CharacterUseContext(true,true,true,0),1,s=>true,out _),"migration preserves taunt cooldown");
                var malformed=cooldown.Snapshot;malformed.skills[0].charges=-1;
                Check(!new CharacterRuntimeState(definition).Restore(malformed),"malformed old save rejected before migration");
                RouteStateChecks(job,definition);
                SnapshotChecks(job,definition);
                foreach(int partySize in new[]{4,8})
                {
                    var cooperation=new AegisSunState();cooperation.SetMark(99,1,Enumerable.Range(1,partySize),partySize);
                    Check(cooperation.Consume(1,true)==0&&cooperation.Consume(2,false)==0,"owner and secondary hit cannot consume tank cooperation");
                    float total=0;foreach(int member in Enumerable.Range(2,partySize-1)){total+=cooperation.Consume(member,true);Check(cooperation.Consume(member,true)==0,"one teammate cannot take others rights");}
                    Check(Math.Abs(total-(partySize==4?120:160))<.001f,"separate four/eight player tank mark budget");
                }
            }
            Console.WriteLine("PASS: nine-key tank definitions/migration/route snapshots "+checks+" checks (managed state).");
        }
        catch(Exception e){Console.WriteLine("FAIL: "+e.Message);Environment.Exit(1);}
    }
    static void SnapshotChecks(RosterJob job,CharacterDefinition definition)
    {
        var json=new JavaScriptSerializer();
        for(int route=1;route<=3;route++)for(int core=0;core<2;core++)
        {
            var actor=new CharacterRuntimeState(definition);actor.ChooseRoute(definition.routes[route-1].id,true);
            actor.ChangeLoadout(job.Id+".loadout.core"+route,NineKeyTankData.Core(job,route,core),true);
            actor.Gain(job.Resource,job.index==1?100:4);
            var state=new NineKeyTankState(actor,job);
            if(job.index==1&&route==2)state.StartCharge(0);
            if(job.index==1&&route==3)state.StartWhirl();
            if(job.index==2&&route==1)state.Barrage(out _);
            if(job.index==2&&route==2){state.StartDance();state.PrepareDance(true);}
            if(job.index==2&&route==3)state.FireFurnaceSecond();
            for(int tick=0;tick<24;tick++)
            {
                var before=state.Snapshot;
                var copy=json.Deserialize<NineKeyTankSnapshot>(json.Serialize(before));
                var restoredActor=new CharacterRuntimeState(definition);
                Check(restoredActor.Restore(json.Deserialize<CharacterStateSnapshot>(json.Serialize(actor.Snapshot))),"JSON actor snapshot");
                var restored=new NineKeyTankState(restoredActor,job);
                Check(restored.Restore(copy)&&restored.Restore(copy),"route JSON repeated restore accepts reachable state without reward");
                Check(restoredActor.Resource(job.Resource)==actor.Resource(job.Resource),"snapshot reapplication grants no resources");
                state.Advance(.17f,out int hits,out int waves);restored.Advance(.17f,out int replayHits,out int replayWaves);
                Check(hits==replayHits&&waves==replayWaves&&json.Serialize(state.Snapshot)==json.Serialize(restored.Snapshot),"restored route advances identically");
            }
            var bad=state.Snapshot;bad.reserved=17;bad.splitShots=1;
            string unchanged=json.Serialize(state.Snapshot);
            Check(!state.Restore(bad)&&json.Serialize(state.Snapshot)==unchanged,"invalid reservation rejected atomically");
            bad=state.Snapshot;bad.danceTime=0;bad.danceShots=1;
            Check(!state.Restore(bad),"shots without active dance rejected");
            bad=state.Snapshot;bad.linkTime=0;bad.linkedAmmo=1;
            Check(!state.Restore(bad),"link without timer rejected");
            bad=state.Snapshot;bad.chargeTime=float.NaN;
            Check(!state.Restore(bad),"nonfinite route state rejected");
        }
    }
    static void RouteStateChecks(RosterJob job,CharacterDefinition definition)
    {
        CharacterRuntimeState Actor(int route,bool core=false)
        {
            var actor=new CharacterRuntimeState(definition);actor.ChooseRoute(definition.routes[route-1].id,true);
            if(!core){var copy=actor.Snapshot;copy.coreTrait="";Check(actor.Restore(copy),"baseline route without optional core fixture");}
            return actor;
        }
        if(job.index==1)
        {
            var actor=Actor(2);actor.Gain(job.Resource,100);var state=new NineKeyTankState(actor,job);
            Check(state.StartCharge(0)&&actor.Resource(job.Resource)==40&&state.Snapshot.reserved==60,"charge reserves 60 immediately");
            Check(!state.StartCharge(0),"second reservation denied");
            state.Advance(.5f,out _,out _);state.CancelCharge();
            Check(actor.Resource(job.Resource)==100&&!state.ReleaseCharge(out _,out _),"priority cancel refunds and never fires");
            state.CancelCharge();Check(actor.Resource(job.Resource)==100,"cancel refund idempotent");
            Check(state.StartCharge(0),"charge after cancellation");state.Advance(3,out _,out _);
            Check(state.ReleaseCharge(out int stage,out int chain)&&stage==3&&chain==0&&actor.Resource(job.Resource)==40,"full release consumes reserved cost");
            state.CancelCharge();Check(actor.Resource(job.Resource)==40,"fired charge never refunds");
            state.FinishCombo(1);Check(actor.Resource(job.Resource)==60&&state.Snapshot.prepared==1,"first finisher earns blood and preparation");
            Check(state.StartCharge(2)&&actor.Resource(job.Resource)==60,"second finisher charge has no R reservation");
            state.CancelCharge();Check(actor.Resource(job.Resource)==60,"cancelled second charge neither gains nor consumes blood");
            Check(state.StartCharge(2),"second charge can retry");state.ReleaseCharge(out _,out _);state.FinishCombo(2);
            Check(actor.Resource(job.Resource)==80,"successful second finisher receives exactly base reward");
            var splitActor=Actor(2,true);splitActor.Gain(job.Resource,100);var split=new NineKeyTankState(splitActor,job);
            split.StartCharge(0);split.ReleaseCharge(out _,out _);
            Check(split.Snapshot.reserved==30&&splitActor.Resource(job.Resource)==40,"split first shot spends only first reserved half");
            var restored=new NineKeyTankState(splitActor,job);Check(restored.Restore(split.Snapshot),"partial split snapshot preserves pending reservation");
            restored.CancelCharge();Check(splitActor.Resource(job.Resource)==70,"only unspent split half refunded");
            var spinActor=Actor(3);spinActor.Gain(job.Resource,100);var spin=new NineKeyTankState(spinActor,job);
            Check(spin.StartWhirl(),"whirl starts with available tick cost");spin.Advance(6,out int ticks,out int waves);
            Check(ticks==30&&waves==3&&spinActor.Resource(job.Resource)==10&&!spin.Snapshot.whirling,"six seconds spends90 with three paid waves");
            for(int i=0;i<30;i++){spin.StartWhirl();spin.Advance(.1f,out ticks,out waves);Check(ticks==0&&waves==0,"rapid toggle cannot create free hit or wave");spin.StopWhirl();}
            spinActor.Spend(job.Resource,7);spin.StartWhirl();spin.Advance(1,out ticks,out waves);
            Check(ticks==1&&waves==0&&spinActor.Resource(job.Resource)==0&&!spin.Snapshot.whirling,"insufficient next tick stops before unpaid attack");
        }
        else
        {
            var actor=Actor(1);var state=new NineKeyTankState(actor,job);
            Check(!state.Barrage(out _),"empty magazine rejects R");state.FinishCombo(2);state.FinishCombo(1);
            Check(state.Barrage(out var shots)&&shots.Length==1&&shots[0].Cooperation&&actor.Resource(job.Resource)==0,"crush pierce order grants cooperation pair");
            state.FinishCombo(1);state.FinishCombo(2);state.EmergencyReload();state.EmergencyReload();
            Check(state.Snapshot.ammo.SequenceEqual(new[]{1,2,2,2})&&!state.EmergencyReload(),"emergency copies last combo type and refuses full magazine");
            state.FinishCombo(1);Check(state.Snapshot.lastAmmo==2,"finisher at full magazine does not replace last actually combo-loaded type");
            Check(state.Barrage(out shots)&&shots[0].Stagger&&!shots[0].Cooperation&&state.Snapshot.ammo.SequenceEqual(new[]{2,2}),"FIFO consumes first two only");
            actor.Gain(job.Resource,2);state.SynchronizeExternalResources();
            Check(state.Snapshot.ammo.SequenceEqual(new[]{2,2,2,2})&&new NineKeyTankState(actor,job).Restore(state.Snapshot),"external fill preserves FIFO and produces restorable count");
            actor.Spend(job.Resource,2);state.SynchronizeExternalResources();
            Check(state.Snapshot.ammo.SequenceEqual(new[]{2,2}),"external reduction truncates without inventing rounds");
            var invalid=state.Snapshot;invalid.ammo=new[]{1};Check(!state.Restore(invalid),"snapshot count must match authoritative magazine resource");
            var splitActor=Actor(1,true);var split=new NineKeyTankState(splitActor,job);split.FinishCombo(2);split.FinishCombo(1);split.FinishCombo(2);
            split.Barrage(out shots);Check(!shots[0].Cooperation&&split.Snapshot.linkTime==4,"first split shot creates one link");
            split.Barrage(out shots);Check(shots[0].Cooperation&&split.Snapshot.linkTime==0,"second shot completes then clears pair");
            split.Barrage(out shots);Check(!shots[0].Cooperation&&shots[0].First==0,"third shot cannot reuse second shot pair");
            var danceActor=Actor(2);var dance=new NineKeyTankState(danceActor,job);dance.FinishCombo(1);dance.FinishCombo(2);
            Check(dance.StartDance()&&danceActor.Resource(job.Resource)==0&&!dance.StartDance(),"dance consumes two once and cannot overlap");
            Check(!dance.FireDance(out _),"R spam without direct hit never fires");
            for(int i=0;i<3;i++){dance.PrepareDance(true);Check(dance.FireDance(out bool last)&&last==(i==2),"one prepared hit per shot, last on third");Check(!dance.FireDance(out _),"shot never prepares another shot");}
            Check(danceActor.Resource(job.Resource)==0&&dance.Snapshot.danceTime==0,"dance cannot reload itself");
            dance.FinishCombo(1);dance.FinishCombo(2);dance.StartDance();dance.PrepareDance(true);dance.Advance(8,out _,out _);
            Check(!dance.FireDance(out _)&&dance.Snapshot.danceShots==0,"overall time limit ends retained readiness");
            var furnaceActor=Actor(3);var furnace=new NineKeyTankState(furnaceActor,job);
            Check(!furnace.FireFurnaceSecond()&&!furnace.Vent(out _),"empty furnace cannot heat or vent");
            for(int i=0;i<4;i++)furnace.FinishCombo(1);
            for(int i=0;i<3;i++)Check(furnace.FireFurnaceSecond(),"each emitted finisher spends one round");
            Check(furnaceActor.Resource(NineKeyTankData.Heat(job))==90&&furnaceActor.Resource(job.Resource)==1,"heat comes from emission even without hit receipt");
            Check(furnace.Vent(out bool hot)&&hot&&furnaceActor.Resource(NineKeyTankData.Heat(job))==50,"hot classification uses pre vent heat");
            furnace.FinishCombo(1);Check(furnaceActor.Resource(NineKeyTankData.Heat(job))==35&&!furnace.InComfortBand,"cooling precedes comfort classification");
            furnace.FireFurnaceSecond();furnace.FinishCombo(1);Check(furnace.InComfortBand,"post cooling 50 inside comfort band");
            furnace.EmergencyReload();Check(furnaceActor.Resource(NineKeyTankData.Heat(job))==50,"emergency reload never cools");
        }
    }
}
