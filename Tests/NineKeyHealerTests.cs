using System;
using System.Linq;
using RaidRogue;

static class NineKeyHealerTests
{
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            foreach(var job in CharacterRosterData.Jobs.Where(NineKeyHealerData.Supports))
            {
                var definition=NineKeyHealerData.Build(job);
                var errors=CharacterDefinitionValidation.Check(new[]{definition});Check(errors.Length==0,"new healer definition valid: "+string.Join(";",errors));
                Check(definition.slots.Count(s=>s.slot!=CharacterSlot.Potion)==9,"nine combat keys");
                Check(definition.loadouts.Count(l=>l.slot==0)==3&&definition.auxiliaryTraits.Length==6,"three core sets and six auxiliary candidates");
                Check(definition.Skill(NineKeyHealerData.Id(job,"q")).maximumCharges==2,"base Q has two sequential charges");
                var charges=new CharacterRuntimeState(definition);string qid=NineKeyHealerData.Id(job,"q");
                Check(charges.MaximumCharges(qid)==3,"reserve trait changes actor capacity without changing base definition");
                for(int n=0;n<3;n++)Check(charges.TryUse(CharacterSlot.Job1,new CharacterUseContext(true,true,true,0),1,s=>true,out _),"three reserved emergency uses");
                Check(!charges.TryUse(CharacterSlot.Job1,new CharacterUseContext(true,true,true,0),1,s=>true,out _),"fourth Q rejected");
                charges.Tick(22.5f);Check(charges.Snapshot.skills.First(s=>s.id==qid).charges==1,"reserve sequential recharge uses 25 percent longer duration");
                float remaining=charges.Snapshot.skills.First(s=>s.id==qid).recharge;
                Check(charges.ChangeAuxiliary("auxiliary.1",NineKeyHealerData.Safe,true)&&charges.MaximumCharges(qid)==2&&charges.Snapshot.skills.First(s=>s.id==qid).recharge==remaining,"unequip preserves pending recharge rather than resetting it");
                charges.Reset(CharacterReset.NextStage);
                Check(charges.ChangeAuxiliary("auxiliary.1",NineKeyHealerData.Reserve,true)&&charges.Snapshot.skills.First(s=>s.id==qid).charges==2&&charges.Snapshot.skills.First(s=>s.id==qid).recharge==22.5f,"equipping reserve cannot fabricate an immediate extra charge");
                Check(new CharacterRuntimeState(definition).Restore(charges.Snapshot),"dynamic charge capacity roundtrip");
                Check(definition.Skill(qid).maximumCharges==2,"per actor trait never mutates shared catalog");
                for(int route=0;route<3;route++)
                {
                    var actor=new CharacterRuntimeState(definition);actor.ChooseRoute(definition.routes[route].id,true);
                    Check(actor.Unlocked(CharacterSlot.OffGcd2)&&!actor.Unlocked(CharacterSlot.Ultimate),"basic F available and V gate preserved");
                    foreach(var loadout in definition.loadouts.Where(l=>string.IsNullOrEmpty(l.routeId)||l.routeId==actor.RouteId))
                    foreach(var choice in loadout.choices)
                    {
                        Check(actor.ChangeLoadout(loadout.id,choice,true),"loadout candidate selectable");
                        Check(new CharacterRuntimeState(definition).Restore(actor.Snapshot),"new choice roundtrip");
                        if(loadout.slot==CharacterSlot.Gcd2)Check(actor.Selected(loadout.slot).target==SkillTargetRule.LivingAlly&&actor.Selected(loadout.slot).costs.Length==0,"every second combo is free ally healing");
                    }
                    foreach(var first in definition.auxiliaryTraits)foreach(var second in definition.auxiliaryTraits.Where(t=>t.id!=first.id))
                    {
                        var snapshot=actor.Snapshot;snapshot.auxiliaryTraits=new[]{first.id,second.id};snapshot.skills.First(s=>s.id==NineKeyHealerData.Id(job,"q")).charges=2;Check(actor.Restore(snapshot),"all distinct auxiliary pairs restore");
                    }
                    var resources=new NineKeyHealerResources(actor,job);resources.CompleteCombo(1);float one=actor.Resource(job.Resource);resources.CompleteCombo(2);
                    Check(actor.Resource(job.Resource)==one*2,"attack/heal completion gives equal resource, no effective heal argument");
                    Check(actor.Resource(NineKeyHealerData.Gauge(job))==20,"separate ultimate gauge");
                    for(int stage=0;stage<=3;stage++)
                    {
                        var legacy=new CharacterRuntimeState(CharacterRosterData.Definition(job));legacy.BeginRun("healer-test",CharacterRewardKey.CurrentChapter);legacy.ChooseRoute(legacy.Definition.routes[route].id,true);
                        for(int n=1;n<=stage;n++){legacy.ClearedStage(n);foreach(var unlock in legacy.PendingChoices())legacy.ChooseSkill(unlock.id,unlock.choices.Last(),legacy.RewardKey(unlock));}
                        legacy.Gain(job.Resource,50);var original=legacy.Snapshot;var migrated=new CharacterRuntimeState(definition);
                        Check(migrated.Restore(original),"legacy healer migration accepted");
                        Check(migrated.RouteId==original.routeId&&migrated.Unlocked(CharacterSlot.Ultimate)==(stage==3),"migration retains route and V gate");
                        Check(migrated.Selected(CharacterSlot.Gcd2).target==SkillTargetRule.LivingAlly&&migrated.Selected(CharacterSlot.OffGcd2).id==NineKeyHealerData.Id(job,"f"),"old second attack and tactical F replaced");
                        Check(migrated.Resource(NineKeyHealerData.Gauge(job))==0&&original.selections.Length==12,"migration grants no ultimate and leaves source untouched");
                        Check(new CharacterRuntimeState(definition).Restore(migrated.Snapshot),"migration idempotent");
                        Check(migrated.Snapshot.skills.First(s=>s.id==qid).charges==migrated.MaximumCharges(qid),"ready legacy boundary uses selected auxiliary charge capacity");
                        if(job.index==5&&route==1)Check(migrated.Resource(job.Resource)==0,"old elixir cannot masquerade as chosen ingredients");
                    }
                }
            }
            var apothecary=CharacterRosterData.Jobs.Single(j=>j.index==5);var mix=new CharacterRuntimeState(NineKeyHealerData.Build(apothecary));mix.ChooseRoute(mix.Definition.routes[1].id,true);var chemistry=new NineKeyHealerResources(mix,apothecary);
            Check(chemistry.Supply()&&chemistry.Snapshot.ingredients.Single()==2,"reserve supply defaults to blue without history");
            chemistry.CompleteCombo(1);chemistry.CompleteCombo(1);chemistry.CompleteCombo(2);
            Check(mix.Resource(apothecary.Resource)==3&&chemistry.Snapshot.ingredients.Count(c=>c==2)==2,"full recipe replaces one opposite color without extra resource");
            Check(chemistry.Prescribe(out int red)&&red==1&&mix.Resource(apothecary.Resource)==0&&!chemistry.Prescribe(out _),"ratio prescription spends exactly three once");
            for(int i=0;i<3;i++)chemistry.CompleteCombo(2);Check(!chemistry.Supply(),"full identical ingredient supply is a no-op");
            var wrong=chemistry.Snapshot;wrong.ingredients=new[]{1};Check(!chemistry.Restore(wrong),"ingredient count mismatch rejected");
            var bard=CharacterRosterData.Jobs.Single(j=>j.index==4);var music=new CharacterRuntimeState(NineKeyHealerData.Build(bard));music.ChooseRoute(music.Definition.routes[2].id,true);music.Gain(bard.Resource,3);var song=new NineKeyHealerResources(music,bard);
            Check(song.BeginVerse()&&music.Resource(bard.Resource)==2&&!song.BeginVerse(),"verse reserves one and forbids double reservation");
            song.CancelVerse();song.CancelVerse();Check(music.Resource(bard.Resource)==3,"cancel returns exactly once");
            song.BeginVerse();Check(song.CompleteVerse()&&!song.CompleteVerse(),"completed verse commits once");song.CancelVerse();Check(music.Resource(bard.Resource)==2,"completed verse never refunds");
            PreparationChecks();
            Console.WriteLine("PASS healer definitions/migration/resources/preparation: "+count);
        }
        catch(Exception e){Console.WriteLine("FAIL: "+e.Message);Environment.Exit(1);}
    }
    static void PreparationChecks()
    {
        var offlineGarden=new HealerGardenState();Check(offlineGarden.Plant(-17,3,4)&&offlineGarden.Restore(offlineGarden.Snapshot,3),"negative Unity instance IDs survive garden state");
        var offlineLink=new HealerLinkState();Check(offlineLink.Begin(-17,true,12)&&offlineLink.Move(-18,true,0)&&offlineLink.Restore(offlineLink.Snapshot),"negative Unity instance IDs survive branched links");
        var offlineRequests=new HealerConductorState();Check(offlineRequests.Request(-17,-18,3,8)&&offlineRequests.Restore(offlineRequests.Snapshot,-18,3),"negative Unity instance IDs survive requests");
        var offlineSurgery=new HealerSurgeryState();Check(offlineSurgery.Treat(-17,12,.25f,out _)&&offlineSurgery.Restore(offlineSurgery.Snapshot),"negative Unity instance IDs survive surgery");
        var offlineLayers=new HealerLayerState();Check(offlineLayers.Begin(new[]{-17,-18},3,100,12)&&offlineLayers.Restore(offlineLayers.Snapshot),"negative Unity instance IDs survive layer recipients");
        var garden=new HealerGardenState();
        Check(garden.Plant(1,2,4)&&garden.Plant(2,2,4),"two seedlings installed");
        Check(!garden.Plant(1,3,4)&&!garden.Plant(3,2,4),"duplicates and capacity reject");
        Check(!garden.Accelerate(1)&&!garden.Accelerate(1)&&garden.Accelerate(1)&&!garden.Accelerate(1),"only third direct heal blooms once");
        Check(garden.Tick(12,4).SequenceEqual(new[]{2})&&garden.Tick(12,4).Length==0,"natural catchup blooms once and early bloom has no remaining ticks");
        garden.Plant(9,3,4);var seeds=garden.Snapshot;seeds[0].stage=2;
        Check(garden.Snapshot[0].stage==0,"seed snapshot detached");
        Check(!garden.Restore(new[]{new HealerSeed{target=9,stage=0,nextGrowth=float.NaN}},3)&&garden.Snapshot[0].target==9,"invalid snapshot atomic");
        var harvest=new HealerHarvestState();
        Check(harvest.Begin(12,3)&&!harvest.Begin(12,3),"one harvest reservation");
        for(int i=0;i<8;i++)harvest.DirectInput();
        Check(harvest.Snapshot.growth==3&&!harvest.Tick(11,out _),"growth capped and harvest remains live");
        Check(harvest.Tick(1,out int growth)&&growth==3&&!harvest.Harvest(out _)&&!harvest.Tick(1,out _),"expiry consumes once");
        Check(!harvest.Restore(new HealerHarvestSnapshot{remaining=1,maximum=5}),"malformed harvest rejected");
        Check(harvest.Begin(12,2)&&harvest.Harvest(out growth)&&growth==0,"early zero growth harvest is valid");
        var conductor=new HealerConductorState();
        Check(!conductor.Request(1,1,3,8)&&conductor.Request(2,1,3,8)&&!conductor.Request(2,1,3,8),"self and duplicate pending requests rejected");
        Check(conductor.Respond(2,10)&&!conductor.Respond(2,10),"response transitions once");
        Check(conductor.Request(3,1,3,8)&&conductor.Request(4,1,3,8)&&!conductor.Request(5,1,3,8),"pending and replies share capacity");
        conductor.Tick(8,10);
        Check(conductor.Replies==3&&conductor.ConsumeReplies()==3&&conductor.ConsumeReplies()==0,"timeout produces same reply and finisher consumes once");
        conductor.Request(2,1,3,8);conductor.Tick(19,10);Check(conductor.Replies==0&&conductor.Snapshot.Length==0,"long frame consumes reply expiry too");
        var bard=CharacterRosterData.Jobs.Single(j=>j.index==4);var actor=new CharacterRuntimeState(NineKeyHealerData.Build(bard));actor.ChooseRoute(actor.Definition.routes[2].id,true);actor.Gain(bard.Resource,3);
        var resources=new NineKeyHealerResources(actor,bard);var music=new HealerSongState(resources);
        Check(music.Begin(2,3)&&actor.Resource(bard.Resource)==2,"song reserves first verse immediately");
        Check(music.Tick(2).SequenceEqual(new[]{1})&&actor.Resource(bard.Resource)==1,"first completion reserves next verse");
        music.Cancel();music.Cancel();Check(actor.Resource(bard.Resource)==2&&!music.Active&&music.Tick(100).Length==0,"cancel refunds only unfinished verse once and never auto completes");
        Check(music.Begin(2,3)&&music.Tick(100).SequenceEqual(new[]{1,2})&&!music.Active&&actor.Resource(bard.Resource)==0,"catchup cannot play unpaid third verse");
        Check(!music.Restore(new HealerSongSnapshot{remaining=1,interval=2,maximum=3}),"song restore needs matching reserved resource");
        var surgery=new HealerSurgeryState();
        Check(surgery.Treat(2,12,.25f,out var treatment)&&treatment.Step==1&&treatment.SamePatient,"first treatment immediate");
        Check(!surgery.Treat(3,12,.25f,out _),"repeat input delay enforced");
        surgery.Tick(1);float remaining=surgery.Snapshot.remaining;
        Check(!surgery.Treat(0,12,.25f,out _)&&surgery.Snapshot.nextStep==2,"invalid patient cannot consume stage");
        Check(surgery.Treat(3,12,.25f,out treatment)&&treatment.Step==2&&treatment.ChangedPatient&&!treatment.SamePatient&&surgery.Snapshot.remaining==remaining,"different patient consumes next right without renewing lifetime");
        surgery.Tick(1);Check(surgery.Treat(2,12,.25f,out treatment)&&treatment.Step==3&&!treatment.SamePatient&&!surgery.Active,"return to initial patient cannot regain uninterrupted same-patient bonus");
        surgery.Treat(2,12,.25f,out _);surgery.Tick(12);Check(!surgery.Active&&surgery.Snapshot.nextStep==0,"unused treatments expire");
        var link=new HealerLinkState();
        Check(link.Begin(2,true,12)&&!link.Begin(3,true,12),"one live link duration");
        link.Tick(3);Check(link.Move(3,true,.25f)&&link.Snapshot.remaining==9,"branch assignment does not renew lifetime");
        Check(!link.Move(4,false,.25f),"retarget rate limit");
        link.Tick(1);Check(!link.Move(3,false,.25f)&&link.Move(4,false,.25f),"branches cannot share one patient");
        var saved=link.Snapshot;saved.first=99;Check(link.Snapshot.first==4,"link snapshot detached");
        Check(!link.Restore(new HealerLinkSnapshot{first=1,second=1,branched=true,remaining=3})&&link.Snapshot.first==4,"invalid link restore atomic");
        link.Tick(100);Check(!link.Active&&link.Snapshot.first==0,"link expires without retargeting");
        foreach(int partySize in new[]{4,8})
        {
            var layers=new HealerLayerState();var other=new HealerLayerState();int[] targets=Enumerable.Range(1,partySize).ToArray();
            Check(layers.Begin(targets,3,100,12)&&other.Begin(targets,3,100,12),"two same-class healers keep independent layer sources");
            Check(!layers.Begin(targets,3,100,12),"active layers cannot be overwritten");
            Check(!layers.Absorbed(1,0,out _)&&layers.Snapshot.recipients[0].layers==3,"another source absorbing all damage does not consume a layer");
            Check(layers.Absorbed(1,1,out float next)&&next==100&&layers.Snapshot.recipients[0].layers==2,"one small absorbed hit consumes one whole layer, next only after hit");
            Check(layers.Snapshot.recipients[0].Budget==200&&other.Snapshot.recipients[0].Budget==300,"unused active residue discarded without touching another caster");
            Check(layers.SetActiveBudget(1,40)&&layers.Snapshot.recipients[0].Budget==140,"settlement tracks cap-admitted active protection");
            Check(layers.SetActiveBudget(1,130)&&layers.Snapshot.recipients[0].Budget==230&&layers.Snapshot.recipients[0].perLayer==100,"amplified active budget does not amplify future base layers recursively");
            Check(layers.SetActiveBudget(1,40),"restore cap-admitted budget for settlement");
            var settlement=layers.Settle();Check(settlement.Sum(r=>r.Budget)==(partySize-1)*300+140&&!layers.Active&&layers.Settle().Length==0,"settlement consumes finite own budget once for original recipients");
            Check(other.Tick(12).Length==partySize&&!other.Active&&other.Settle().Length==0,"expiry clears all layers without automatic healing");
        }
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-1})
        {
            Check(!garden.Plant(10,3,bad)&&!harvest.Begin(bad,3)&&!conductor.Request(10,1,3,bad)&&!music.Begin(bad,3),"invalid preparation duration rejected");
        }
    }
}
