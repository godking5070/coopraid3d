using System;
using System.Linq;
using RaidRogue;

static class NineKeyDamageTests
{
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            foreach(var job in CharacterRosterData.Jobs.Where(NineKeyDamageData.Supports))
            {
                var d=NineKeyDamageData.Build(job);
                var errors=CharacterDefinitionValidation.Check(new[]{d});Check(errors.Length==0,job.name+": "+string.Join(";",errors));
                Check(d.slots.Count(s=>s.slot!=CharacterSlot.Potion)==9,"exact nine combat keys");
                Check(d.resources.Length==2,"one primary and separate ultimate gauge");
                Check(d.skills.Where(s=>s.id.StartsWith(job.Id+".nine.g2")).All(s=>s.usesGcd&&s.comboChain==2),"every second variant shares combo and GCD contract");
                Check(d.skills.Where(s=>s.id.StartsWith(job.Id+".nine.c")).Where(s=>!s.id.Contains("core")).All(s=>s.costs.Length==0&&s.usableWhileCasting),"all role choices available without primary resource during cast");
                if(job.index==10)Check(d.skills.Where(s=>s.id.StartsWith(job.Id+".nine.g2")).All(s=>s.castTime>0),"every caster second stage has base cast");
                for(int r=0;r<3;r++)
                {
                    var state=new CharacterRuntimeState(d);Check(state.ChooseRoute(d.routes[r].id,true),"route selectable");
                    foreach(var group in d.loadouts.Where(l=>string.IsNullOrEmpty(l.routeId)||l.routeId==state.RouteId))
                    foreach(var choice in group.choices)
                    {
                        if(state.BuildChoiceFailure(group.id,choice).Length>0)
                        {Check(!state.ChangeLoadout(group.id,choice,true),"incompatible selected target is rejected");continue;}
                        Check(state.ChangeLoadout(group.id,choice,true),"choice accepted "+choice);
                        Check(new CharacterRuntimeState(d).Restore(state.Snapshot),"selection roundtrip "+choice);
                    }
                    Check(state.Unlocked(CharacterSlot.OffGcd2)&&!state.Unlocked(CharacterSlot.Ultimate),"F basic; existing V gate preserved");
                    for(int stage=0;stage<=3;stage++)
                    {
                        var old=new CharacterRuntimeState(CharacterRosterData.Definition(job));
                        old.BeginRun("damage-test",CharacterRewardKey.CurrentChapter);old.ChooseRoute(old.Definition.routes[r].id,true);
                        for(int n=1;n<=stage;n++){old.ClearedStage(n);foreach(var unlock in old.PendingChoices())old.ChooseSkill(unlock.id,unlock.choices.Last(),old.RewardKey(unlock));}
                        old.Gain(job.Resource,100);var original=old.Snapshot;var migrated=new CharacterRuntimeState(d);
                        Check(migrated.Restore(original),"legacy restore "+job.name+" route "+r+" stage "+stage);
                        Check(migrated.RouteId==original.routeId,"legacy route retained");
                        Check(migrated.Unlocked(CharacterSlot.Ultimate)==(stage==3),"legacy V unlock retained");
                        Check(migrated.Resource(NineKeyDamageData.Gauge(job))==0&&migrated.Resource(job.Resource)==0,"no reinterpreted old resource or free ultimate");
                        Check(original.selections.Length==12,"source not mutated");
                        Check(new CharacterRuntimeState(d).Restore(migrated.Snapshot),"idempotent migrated restore");
                    }
                }
                Check(d.Skill(NineKeyDamageData.Id(job,"q")).target==SkillTargetRule.Self,"Q always personal defense");
                Check(d.loadouts.Count(l=>l.slot!=0)==4,"four exchange slots");
                Check(d.skills.Where(s=>s.id.StartsWith(job.Id+".nine.core")).Count()==(job.index==7?11:6),"six core variants with explicit Tempest target choices");
            }
            ResourceChecks();PreparationChecks();TacticalChecks();WindowChecks();MirrorChecks();ArrowAndFieldChecks();ControlChecks();CompatibilityChecks();CapacityChecks();CoreChargeChecks();CombatSnapshotChecks();
            for(int job=6;job<=11;job++)for(int choice=0;choice<3;choice++)
            {
                bool expected=(job==6||job==11)?choice==2:(job==8||job==9||job==10)&&choice==1;
                Check((NineKeyDamageData.ComboStagger(job,2,choice)>0)==expected,"stagger belongs to designated class choice");
                Check(NineKeyDamageData.ComboStagger(job,1,choice)==0,"first combo cannot inherit second-choice stagger");
            }
            Console.WriteLine("PASS nine-key DPS definitions, migration and resources: "+count+"; native play validation remains separate.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
    static DamageCombatSnapshot EmptyCombat(CharacterRuntimeState state,RosterJob job,int route)
    {
        var resources=new NineKeyDamageResources(state,job);
        var season=new DamageSeasonState();
        season.Reset(job.index==9&&state.Snapshot.coreTrait==NineKeyDamageData.Core(job,3,0)?6:8);
        return new DamageCombatSnapshot{job=job.Id,route=route,core=state.Snapshot.coreTrait,
            resources=resources.Snapshot,weakness=new DamageWeaknessState().Snapshot,seal=new DamageSealState().Snapshot,
            grenade=new DamageGrenadeState().Snapshot,hands=new DamageHandsState().Snapshot,
            snipe=new DamageSnipeState(resources).Snapshot,meteor=new DamageMeteorState(resources).Snapshot,
            burst=new DamageBurstState().Snapshot,season=season.Snapshot,instant=new DamageInstantState().Snapshot,
            memory=new DamageMemoryState().Snapshot,projection=new DamageProjectionState().Snapshot,arrows=new DamageArrowState().Snapshot,
            anchor=new DamageReturnState().Snapshot,seed=new DamageSeedState().Snapshot,preparation=new DamageTempestPreparationState().Snapshot,
            spears=new DamageSpearState().Snapshot,gravity=new DamageGravityState().Snapshot,prism=new DamagePrismState().Snapshot,
            effects=new CharacterEffectState().Data.Copy()};
    }
    static void SeasonSnapshotChecks()
    {
        var ranger=CharacterRosterData.Jobs.First(j=>j.index==9);
        for(int variant=0;variant<2;variant++)
        {
            var state=new CharacterRuntimeState(NineKeyDamageData.Build(ranger));state.ChooseRoute(state.Definition.routes[2].id,true);
            var core=state.Definition.loadouts.First(l=>l.slot==0&&l.routeId==state.RouteId);
            state.ChangeLoadout(core.id,NineKeyDamageData.Core(ranger,3,variant),true);state.Gain(ranger.Resource,35);
            float period=variant==0?6:8,extension=variant==0?2:6;
            var saved=EmptyCombat(state,ranger,3);var season=new DamageSeasonState();season.Reset(period);
            Check(DamageCombatState.TryRestore(state,ranger,saved,out _),"selected seasonal core initial period restores");
            season.Tick(1.25f);saved.season=season.Snapshot;
            Check(DamageCombatState.TryRestore(state,ranger,saved,out _),"partial season does not require a full initial timer");
            season.Delay(extension);saved.season=season.Snapshot;
            Check(DamageCombatState.TryRestore(state,ranger,saved,out var restored)&&!restored.season.Delay(extension),"paid seasonal extension restores with one-use flag intact");
            saved.season.period=100000;saved.season.remaining=100000;
            Check(!DamageCombatState.TryRestore(state,ranger,saved,out var rejected)&&rejected==null&&state.Resource(ranger.Resource)==35,"inflated season period rejected without changing spendable resource");
            saved.season=season.Snapshot;saved.season.remaining=100000;
            Check(!DamageCombatState.TryRestore(state,ranger,saved,out _),"delayed season cannot bypass finite lifetime through restore");
            saved.season=season.Snapshot;saved.season.period=variant==0?8:6;saved.season.remaining=1;
            Check(!DamageCombatState.TryRestore(state,ranger,saved,out _),"other core period rejected even with short remaining time");
            saved.season=season.Snapshot;saved.season.remaining=period+extension;
            Check(DamageCombatState.TryRestore(state,ranger,saved,out _),"maximum legal immediate delay restores");
            saved.season.remaining+=.0001f;
            Check(DamageCombatState.TryRestore(state,ranger,saved,out _),"sub-millisecond serialization drift at delayed boundary tolerated");
            saved.season.remaining=period+extension+.01f;
            Check(!DamageCombatState.TryRestore(state,ranger,saved,out _),"remaining time beyond boundary tolerance rejected");
            saved.season=season.Snapshot;season.Tick(100);saved.season=season.Snapshot;
            Check(DamageCombatState.TryRestore(state,ranger,saved,out restored)&&!restored.season.Snapshot.delayed,"natural advance restores a fresh finite visit");
            restored.season.Advance();Check(restored.season.Snapshot.remaining==period,"restored short R retains selected fixed period");
        }
    }
    static void CombatSnapshotChecks()
    {
        SeasonSnapshotChecks();
        ScheduledSnapshotChecks();
        UltimateAndMirrorSnapshotChecks();
        var lancer=CharacterRosterData.Jobs.First(j=>j.index==7);
        for(int variant=0;variant<2;variant++)
        {
            var state=new CharacterRuntimeState(NineKeyDamageData.Build(lancer));state.ChooseRoute(state.Definition.routes[2].id,true);
            var core=state.Definition.loadouts.First(l=>l.slot==0&&l.routeId==state.RouteId);
            state.ChangeLoadout(core.id,NineKeyDamageData.Core(lancer,3,variant),true);
            var saved=EmptyCombat(state,lancer,3);var field=new DamageSpearState();field.Place(default,variant==0?14:26,40,variant==1,400);
            saved.spears=field.Snapshot;Check(DamageCombatState.TryRestore(state,lancer,saved,out _),"legitimate spear core budget restores");
            saved.spears.spears[0].tickPower=400;
            Check(!DamageCombatState.TryRestore(state,lancer,saved,out _),"inflated spear periodic damage rejected");
            saved.spears=field.Snapshot;saved.spears.spears[0].storageLimit=4000;
            Check(!DamageCombatState.TryRestore(state,lancer,saved,out _),"inflated spear compression cap rejected");
            saved.spears=field.Snapshot;saved.spears.spears[0].compressed=variant==0;
            Check(!DamageCombatState.TryRestore(state,lancer,saved,out _),"spear compression mode must match selected core");
            var tactics=state.Definition.loadouts.First(l=>l.slot==CharacterSlot.OffGcd2);
            state.ChangeLoadout(tactics.id,NineKeyDamageData.Id(lancer,"f2"),true);
            state.ChangeLoadout(core.id,NineKeyDamageData.Id(lancer,variant==0?"core30.f2":"core31.f2"),true);
            saved=EmptyCombat(state,lancer,3);var seed=new DamageSeedState();seed.Place(default,variant==0?4:8,16,variant==1,240);
            saved.seed=seed.Snapshot;Check(DamageCombatState.TryRestore(state,lancer,saved,out _),"legitimate tactical seed core budget restores");
            saved.seed.storageLimit=2400;
            Check(!DamageCombatState.TryRestore(state,lancer,saved,out _),"inflated seed storage cap rejected");
            saved.seed=seed.Snapshot;saved.seed.compressed=variant==0;
            Check(!DamageCombatState.TryRestore(state,lancer,saved,out _),"seed compression mode must match F core");
            saved.seed=seed.Snapshot;saved.seed.remaining=100;
            Check(!DamageCombatState.TryRestore(state,lancer,saved,out _),"seed lifetime cannot be extended by restore");
            state.ChangeAuxiliary("auxiliary.2","tempest.aux.duration",true);
            saved.seed=seed.Snapshot;saved.seed.remaining+=5;
            Check(DamageCombatState.TryRestore(state,lancer,saved,out _),"legitimate duration auxiliary extends seed lifetime without expanding damage budget");
            saved.seed.remaining+=.1f;
            Check(!DamageCombatState.TryRestore(state,lancer,saved,out _),"duration auxiliary does not remove finite lifetime bound");
        }
        var night=CharacterRosterData.Jobs.Single(j=>j.index==6);
        var nightState=new CharacterRuntimeState(NineKeyDamageData.Build(night));nightState.ChooseRoute(nightState.Definition.routes[0].id,true);
        var ultimate=EmptyCombat(nightState,night,1);
        ultimate.effects.effects=new[]{new CharacterTimedEffect{id="ultimate.sequence:test",kind="ultimate.sequence",skill="v",target=9,value=440,end=.45f,next=.45f,interval=1}};
        Check(DamageCombatState.TryRestore(nightState,night,ultimate,out var pendingUltimate)&&pendingUltimate.effects.Data.effects.Length==1,"remaining ultimate finisher restores without replaying earlier daggers");
        ultimate.effects.effects[0].value=441;
        Check(!DamageCombatState.TryRestore(nightState,night,ultimate,out _),"unbounded ultimate damage is not accepted from snapshot");
        ultimate.effects.effects[0].value=440;ultimate.effects.effects[0].end=ultimate.effects.effects[0].next=1;
        Check(!DamageCombatState.TryRestore(nightState,night,ultimate,out _),"ultimate deferred lifetime cannot be extended by restore");
        ultimate.effects.effects[0].end=ultimate.effects.effects[0].next=.45f;ultimate.effects.effects[0].skill="g1";
        Check(!DamageCombatState.TryRestore(nightState,night,ultimate,out _),"ultimate schedule cannot masquerade as a basic combo");
        foreach(var job in CharacterRosterData.Jobs.Where(NineKeyDamageData.Supports))for(int route=1;route<=3;route++)
        {
            var state=new CharacterRuntimeState(NineKeyDamageData.Build(job));state.ChooseRoute(state.Definition.routes[route-1].id,true);
            var v=EmptyCombat(state,job,route);state.Gain(job.Resource,2);
            Check(DamageCombatState.TryRestore(state,job,v,out var c),"all 18 initial route states restore");
            // Managed JSON seam: verifies DTO fields and candidate restoration,
            // not Unity's native JsonUtility implementation or live transport.
            var json=new System.Web.Script.Serialization.JavaScriptSerializer();
            string encoded=json.Serialize(v);
            var decoded=json.Deserialize<DamageCombatSnapshot>(encoded);
            Check(DamageCombatState.TryRestore(state,job,decoded,out _),"all 18 route DTOs survive managed JSON roundtrip");
            Check(decoded.version==3&&decoded.core==v.core&&decoded.pendingSeals.Length==0,"JSON preserves schema/core and empty deferred completions");
            Check(state.Resource(job.Resource)==2,"restore neither spends nor refunds framework balance");
            v.effects.effects=new[]{new CharacterTimedEffect{id="bad",kind="residual.cloud",skill="r1",value=float.NaN,end=3,next=1}};
            Check(!DamageCombatState.TryRestore(state,job,v,out c)&&c==null&&state.Resource(job.Resource)==2,"late malformed effect leaves balance and live states untouched");
            v=EmptyCombat(state,job,route);v.job="wrong";
            Check(!DamageCombatState.TryRestore(state,job,v,out c),"wrong owner class rejected");
            v.job=job.Id;v.core="wrong";
            Check(!DamageCombatState.TryRestore(state,job,v,out c),"wrong core rejected before restore");
            v=EmptyCombat(state,job,route);v.fourthReady=true;
            Check(DamageCombatState.TryRestore(state,job,v,out c)==(job.index==7&&route==2),"fourth opportunity belongs only to selected Tempest core");
        }
        var astro=CharacterRosterData.Jobs.First(j=>j.index==10);
        var a=new CharacterRuntimeState(NineKeyDamageData.Build(astro));a.ChooseRoute(a.Definition.routes[0].id,true);a.Gain(astro.Resource,100);
        var ar=new NineKeyDamageResources(a,astro);var meteor=new DamageMeteorState(ar);
        meteor.BeginOrResume(true,60,5,.6f,16);meteor.Tick(.7f,true);meteor.Interrupt();
        var snap=EmptyCombat(a,astro,1);snap.resources=ar.Snapshot;snap.meteor=meteor.Snapshot;snap.meteorTarget=9;
        Check(DamageCombatState.TryRestore(a,astro,snap,out var restored)&&a.Resource(astro.Resource)==40,"reserved meteor restored without another debit");
        snap.resources.reserved=100;
        Check(!DamageCombatState.TryRestore(a,astro,snap,out _)&&a.Resource(astro.Resource)==40,"inflated meteor reservation cannot mint extra refund");
        snap.resources.reserved=1;
        Check(!DamageCombatState.TryRestore(a,astro,snap,out _),"underpriced meteor reservation cannot complete a full spell");
        snap.resources.reserved=60;
        Check(restored.meteor.Snapshot.completed==1&&restored.meteor.Snapshot.interrupted,"meteor segments and interruption history survive aggregate restore");
        var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
        var decodedMeteor=serializer.Deserialize<DamageCombatSnapshot>(serializer.Serialize(snap));
        Check(DamageCombatState.TryRestore(a,astro,decodedMeteor,out restored)&&restored.resources.Snapshot.reserved==60&&
            restored.meteor.Snapshot.completed==1&&a.Resource(astro.Resource)==40,"JSON reservation and incomplete spell roundtrip cannot refund or reserve twice");
        snap.meteorTarget=0;Check(!DamageCombatState.TryRestore(a,astro,snap,out _)&&a.Resource(astro.Resource)==40,"active meteor requires saved target without refund on failure");
        snap.meteorTarget=9;snap.resources.reserved=0;snap.resources.reservation="";
        Check(!DamageCombatState.TryRestore(a,astro,snap,out _),"active cast cannot restore without its reservation");
        var clocked=EmptyCombat(a,astro,1);clocked.capturedAt=102;
        clocked.effects.effects=new[]{new CharacterTimedEffect{id="clocked.dot",kind="damage.dot",skill="g22",target=9,value=30,next=101,end=103,interval=1}};
        Check(DamageCombatState.TryRestore(a,astro,clocked,out var unchanged),"same-server clock snapshot restores");
        Check(unchanged.effects.Data.effects[0].next==101&&unchanged.effects.Data.effects[0].end==103,"replica restore preserves server deadlines");
        Check(DamageCombatState.TryRestore(a,astro,clocked,out var rebased,500),"saved battle can rebase to a new clock");
        var timer=rebased.effects.Data.effects[0];
        Check(timer.next==499&&timer.end==501&&timer.ticks==0,"rebasing preserves relative due time without running ticks");
        Check(clocked.effects.effects[0].next==101&&clocked.effects.effects[0].end==103,"rebasing never mutates the source snapshot");
        Check(CharacterEffectState.TakeTicks(timer,500)==2&&CharacterEffectState.TakeTicks(timer,500)==0,"overdue finite ticks consumed once after restore");
        Check(CharacterEffectState.TakeTicks(timer,501)==1&&CharacterEffectState.TakeTicks(timer,900)==0,"last tick expires without replay");
        Check(DamageCombatState.TryRestore(a,astro,clocked,out rebased,0),"new clock may start at zero");
        Check(rebased.effects.Data.effects[0].next==-1&&CharacterEffectState.TakeTicks(rebased.effects.Data.effects[0],0)==2,"negative rebased deadline preserves already due ticks");
        Check(!DamageCombatState.TryRestore(a,astro,clocked,out _,float.NaN)&&!DamageCombatState.TryRestore(a,astro,clocked,out _,-2),"malformed restore clocks rejected");
        clocked.effects.effects[0].end=100000;
        Check(!DamageCombatState.TryRestore(a,astro,clocked,out _),"implausibly long effect deadline rejected");
        clocked.effects.effects[0].end=103;clocked.capturedAt=float.PositiveInfinity;
        Check(!DamageCombatState.TryRestore(a,astro,clocked,out _),"nonfinite capture clock rejected");
        clocked.capturedAt=102;var foreign=clocked.effects.effects[0];
        foreach(string kind in new[]{"assault","acceleration","tempest.response","hand.ready","fracture","duet","range","reach","residual.cloud"})
        {
            foreign.kind=kind;
            Check(!DamageCombatState.TryRestore(a,astro,clocked,out _),"caster cannot restore foreign gameplay effect: "+kind);
        }
        foreign.kind="damage.dot";foreign.skill="g20";
        Check(!DamageCombatState.TryRestore(a,astro,clocked,out _),"caster cannot inject nightblade bleed");
        foreign.skill="g22";foreign.ticks=5;
        Check(!DamageCombatState.TryRestore(a,astro,clocked,out _),"dot tick counter beyond finite budget rejected");
        var ranger=CharacterRosterData.Jobs.First(j=>j.index==9);
        var rangerState=new CharacterRuntimeState(NineKeyDamageData.Build(ranger));rangerState.ChooseRoute(rangerState.Definition.routes[1].id,true);
        var burstSave=EmptyCombat(rangerState,ranger,2);var burst=new DamageBurstState();burst.Begin(16,.25f);burstSave.burst=burst.Snapshot;
        Check(DamageCombatState.TryRestore(rangerState,ranger,burstSave,out _),"normal fast burst storage restores");
        burstSave.burst.remaining=20;
        Check(!DamageCombatState.TryRestore(rangerState,ranger,burstSave,out _),"fast core cannot restore pursuit's longer storage window");
        for(int variant=0;variant<2;variant++)
        {
            var caster=new CharacterRuntimeState(NineKeyDamageData.Build(astro));caster.ChooseRoute(caster.Definition.routes[1].id,true);
            var rule=caster.Definition.loadouts.First(l=>l.slot==0&&l.routeId==caster.RouteId);
            caster.ChangeLoadout(rule.id,NineKeyDamageData.Core(astro,2,variant),true);caster.Gain(astro.Resource,17);
            var saved=EmptyCombat(caster,astro,2);var rights=new DamageInstantState();int maximum=variant==0?2:5;
            rights.Begin(maximum,20,variant==0?18:40);saved.instant=rights.Snapshot;
            Check(DamageCombatState.TryRestore(caster,astro,saved,out _),"selected instant core restores its maximum legitimate rights");
            saved.instant.rights++;
            Check(!DamageCombatState.TryRestore(caster,astro,saved,out var invalidRights)&&invalidRights==null&&caster.Resource(astro.Resource)==17,"over-cap rights fail atomically without touching balance");
            saved.instant=rights.Snapshot;saved.instant.remaining=21;
            Check(!DamageCombatState.TryRestore(caster,astro,saved,out _),"instant lifetime cannot be extended on restore");
            saved.instant=rights.Snapshot;saved.instant.toggleDelay=.3f;
            Check(!DamageCombatState.TryRestore(caster,astro,saved,out _),"instant toggle gate obeys actual input limit");
            saved.instant=rights.Snapshot;saved.instant.cooldown++;
            Check(!DamageCombatState.TryRestore(caster,astro,saved,out _),"instant cooldown must match selected core bound");
        }
        var gun=CharacterRosterData.Jobs.First(j=>j.index==8);
        for(int variant=0;variant<2;variant++)
        {
            var shooter=new CharacterRuntimeState(NineKeyDamageData.Build(gun));shooter.ChooseRoute(shooter.Definition.routes[2].id,true);
            var rule=shooter.Definition.loadouts.First(l=>l.slot==0&&l.routeId==shooter.RouteId);
            shooter.ChangeLoadout(rule.id,NineKeyDamageData.Core(gun,3,variant),true);shooter.Gain(gun.Resource,6);
            var resources=new NineKeyDamageResources(shooter,gun);var shot=new DamageSnipeState(resources);
            shot.Begin(9,variant==0?3:5,variant==0?1:2,12,variant==0?3:1);
            var preparing=EmptyCombat(shooter,gun,3);preparing.resources=resources.Snapshot;preparing.snipe=shot.Snapshot;
            Check(DamageCombatState.TryRestore(shooter,gun,preparing,out _),"selected sniper cost restores while aiming");
            float balance=shooter.Resource(gun.Resource);preparing.resources.reserved=6;
            Check(!DamageCombatState.TryRestore(shooter,gun,preparing,out _)&&shooter.Resource(gun.Resource)==balance,"inflated sniper reservation rejected without refund or debit");
            preparing.resources.reserved=1;
            Check(!DamageCombatState.TryRestore(shooter,gun,preparing,out _),"underpriced sniper preparation rejected");
            shot.Tick(2);
            var saved=EmptyCombat(shooter,gun,3);saved.resources=resources.Snapshot;saved.snipe=shot.Snapshot;
            Check(DamageCombatState.TryRestore(shooter,gun,saved,out _),"legitimate prepared sniper variant restores");
            saved.snipe.shots++;
            Check(!DamageCombatState.TryRestore(shooter,gun,saved,out _),"sniper cannot restore extra shots beyond selected core");
            saved.snipe=shot.Snapshot;saved.snipe.keep=30;saved.snipe.remaining=29;
            Check(!DamageCombatState.TryRestore(shooter,gun,saved,out _),"sniper hold time cannot be extended by snapshot");
        }
    }
    static void UltimateAndMirrorSnapshotChecks()
    {
        foreach(int index in new[]{6,11})
        {
            var job=CharacterRosterData.Jobs.Single(j=>j.index==index);
            var actor=new CharacterRuntimeState(NineKeyDamageData.Build(job));actor.ChooseRoute(actor.Definition.routes[0].id,true);
            var saved=EmptyCombat(actor,job,1);saved.capturedAt=100;
            var powers=index==6?new[]{120f,120f,440f}:new[]{300f,300f};
            var delays=index==6?new[]{.15f,.3f,.45f}:new[]{.2f,.4f};
            CharacterTimedEffect[] Full()=>powers.Select((power,n)=>new CharacterTimedEffect{id="ultimate.test."+n,
                kind="ultimate.sequence",skill="v",target=9,value=power,next=100+delays[n],end=100+delays[n]}).ToArray();
            saved.effects.effects=Full();
            Check(DamageCombatState.TryRestore(actor,job,saved,out _),"actual complete ultimate schedule restores");
            saved.effects.effects=Full().Skip(1).ToArray();
            Check(DamageCombatState.TryRestore(actor,job,saved,out _),"ultimate restores only the unconsumed suffix");
            saved.effects.effects=Full();saved.capturedAt=101;
            Check(DamageCombatState.TryRestore(actor,job,saved,out var overdue,0),"overdue ultimate schedule rebases without replaying early attacks");
            Check(overdue.effects.Data.effects.Sum(e=>CharacterEffectState.TakeTicks(e,0))==powers.Length&&
                overdue.effects.Data.effects.Sum(e=>CharacterEffectState.TakeTicks(e,0))==0,"overdue ultimate rights are consumed exactly once");
            saved.capturedAt=100;saved.effects.effects=Full().Concat(new[]{Full().Last()}).ToArray();
            saved.effects.effects.Last().id="ultimate.extra";
            Check(!DamageCombatState.TryRestore(actor,job,saved,out _),"extra ultimate attack with a unique id exceeds paid set");
            saved.effects.effects=Full();saved.effects.effects.Last().target=10;
            Check(!DamageCombatState.TryRestore(actor,job,saved,out _),"single prepared ultimate cannot acquire another target");
            if(index==6)
            {
                saved.effects.effects=Full();saved.effects.effects[0].value=440;
                Check(!DamageCombatState.TryRestore(actor,job,saved,out _),"normal total count still cannot contain a duplicate ultimate finisher");
                saved.effects.effects=Full();saved.effects.effects[2].value=120;
                Check(!DamageCombatState.TryRestore(actor,job,saved,out _),"normal total count cannot mint a third opening dagger");
            }
        }
        var mirror=CharacterRosterData.Jobs.Single(j=>j.index==11);
        for(int route=1;route<=2;route++)for(int variant=0;variant<2;variant++)
        {
            var actor=new CharacterRuntimeState(NineKeyDamageData.Build(mirror));actor.ChooseRoute(actor.Definition.routes[route-1].id,true);
            var core=actor.Definition.loadouts.First(l=>l.slot==0&&l.routeId==actor.RouteId);
            actor.ChangeLoadout(core.id,NineKeyDamageData.Core(mirror,route,variant),true);
            var saved=EmptyCombat(actor,mirror,route);
            if(route==1)
            {
                var memory=new DamageMemoryState();memory.Record("g1",true,variant==0?1:2,12);memory.ToggleLock();saved.memory=memory.Snapshot;
                Check(DamageCombatState.TryRestore(actor,mirror,saved,out _),"locked records preserve their legitimate twelve-second lifetime");
                saved.memory.records[0].remaining=12.01f;
                Check(!DamageCombatState.TryRestore(actor,mirror,saved,out _),"record lock cannot extend snapshot lifetime");
                memory.Tick(12);saved.memory=memory.Snapshot;
                Check(DamageCombatState.TryRestore(actor,mirror,saved,out _),"expired records with lock retained restore without inventing attacks");
            }
            else
            {
                var projection=new DamageProjectionState();projection.Begin(variant==0?16:10);saved.projection=projection.Snapshot;
                Check(DamageCombatState.TryRestore(actor,mirror,saved,out _),"selected projection core restores its real duration");
                saved.projection.remaining+=.01f;
                Check(!DamageCombatState.TryRestore(actor,mirror,saved,out _),"projection duration cannot exceed selected core");
                projection.LaunchSecond();projection.Tick(20);saved.projection=projection.Snapshot;saved.resources.projectedSecond=true;
                Check(DamageCombatState.TryRestore(actor,mirror,saved,out _),"expired projection keeps paid combo provenance without extending remote rights");
            }
        }
    }
    static void ScheduledSnapshotChecks()
    {
        foreach(var spec in new[]{
            (job:6,key:"g20",power:30f,interval:1f,budget:4),
            (job:7,key:"g21",power:30f,interval:1f,budget:4),
            (job:8,key:"g22",power:30f,interval:1f,budget:4),
            (job:10,key:"g22",power:30f,interval:1f,budget:4),
            (job:8,key:"a2",power:210f,interval:1.5f,budget:1),
            (job:9,key:"a2",power:110f,interval:1f,budget:1),
            (job:9,key:"g22",power:100f,interval:1f,budget:1)})
        {
            var job=CharacterRosterData.Jobs.Single(j=>j.index==spec.job);
            var actor=new CharacterRuntimeState(NineKeyDamageData.Build(job));
            actor.ChooseRoute(actor.Definition.routes[1].id,true);actor.Gain(job.Resource,1);
            var saved=EmptyCombat(actor,job,2);saved.capturedAt=100;
            CharacterTimedEffect Fresh()=>new CharacterTimedEffect{id="schedule.test",kind=spec.budget==4?"damage.dot":"damage.followup",
                skill=spec.key,target=9,value=spec.power,interval=spec.interval,next=100+spec.interval,end=100+spec.interval*spec.budget};
            saved.effects.effects=new[]{Fresh()};
            Check(DamageCombatState.TryRestore(actor,job,saved,out _),"all real periodic and followup budgets restore");
            var effect=saved.effects.effects[0];effect.value*=1000;
            Check(!DamageCombatState.TryRestore(actor,job,saved,out _)&&actor.Resource(job.Resource)==1,"inflated scheduled power rejected atomically");
            saved.effects.effects=new[]{Fresh()};effect=saved.effects.effects[0];effect.end+=100;
            Check(!DamageCombatState.TryRestore(actor,job,saved,out _),"scheduled future ticks cannot exceed original total budget");
            saved.effects.effects=new[]{Fresh()};effect=saved.effects.effects[0];effect.interval=.01f;
            Check(!DamageCombatState.TryRestore(actor,job,saved,out _),"followup and periodic intervals cannot create extra rapid hits");
            saved.effects.effects=new[]{Fresh()};effect=saved.effects.effects[0];effect.ticks=1;
            Check(!DamageCombatState.TryRestore(actor,job,saved,out _),"consumed and future ticks share one finite budget");
            saved.effects.effects=new[]{Fresh()};effect=saved.effects.effects[0];effect.next+=10;effect.end+=10;
            Check(!DamageCombatState.TryRestore(actor,job,saved,out _),"same tick count cannot be rescheduled beyond original lifetime");
            saved.effects.effects=new[]{Fresh()};saved.capturedAt=110;
            Check(DamageCombatState.TryRestore(actor,job,saved,out var overdue,0),"overdue schedule remains valid when rebased to zero");
            var due=overdue.effects.Data.effects[0];
            Check(CharacterEffectState.TakeTicks(due,0)==spec.budget&&CharacterEffectState.TakeTicks(due,0)==0,"overdue attacks execute original budget exactly once");
            saved.capturedAt=102;saved.effects.effects=new[]{Fresh()};effect=saved.effects.effects[0];
            effect.ticks=1;effect.next+=spec.interval;
            Check(DamageCombatState.TryRestore(actor,job,saved,out _),"already consumed tick cannot invalidate legitimate remaining budget");
        }
    }
    static void ResourceChecks()
    {
        foreach(var job in CharacterRosterData.Jobs.Where(NineKeyDamageData.Supports))
        for(int route=1;route<=3;route++)for(int choice=0;choice<3;choice++)
        {
            var actor=new CharacterRuntimeState(NineKeyDamageData.Build(job));actor.ChooseRoute(actor.Definition.routes[route-1].id,true);
            var resource=new NineKeyDamageResources(actor,job);
            Check(!resource.DirectCombo(0,3,choice)&&actor.Resource(job.Resource)==0,"invalid combo does not generate resource");
            for(int step=1;step<=3;step++)resource.DirectCombo(1,step,choice);
            float expected=job.index==6?(route==2?2:1):job.index==7?50:job.index==8?(route==1?0:2):job.index==11?1:20;
            Check(actor.Resource(job.Resource)==expected,"first combo resource "+job.name+" route "+route);
            float second=job.index==6?(route==2?3:1):job.index==7?(choice==0?40:choice==1?20:30)+(route==2?10:0):job.index==8?(route==1?0:1):job.index==11?1:20;
            for(int step=1;step<=3;step++)resource.DirectCombo(2,step,choice);
            Check(actor.Resource(job.Resource)==Math.Min(actor.Maximum(job.Resource),expected+second),"second combo resource "+job.name);
            Check(actor.Resource(NineKeyDamageData.Gauge(job))==20,"ultimate only on finishers");
            Check(resource.Restore(resource.Snapshot),"resource provenance roundtrip");
        }
        var mirror=CharacterRosterData.Jobs.First(j=>j.index==11);var m=new CharacterRuntimeState(NineKeyDamageData.Build(mirror));m.ChooseRoute(m.Definition.routes[1].id,true);var mr=new NineKeyDamageResources(m,mirror);
        mr.MarkProjectedSecond();mr.DirectCombo(1,3,0);mr.DirectCombo(2,3,0);
        Check(m.Resource(mirror.Resource)==1,"projection history survives intervening first combo and suppresses second refund");
        mr.DirectCombo(2,3,0);Check(m.Resource(mirror.Resource)==2,"next unprojected combo restores normal gain");
        var astro=CharacterRosterData.Jobs.First(j=>j.index==10);var a=new CharacterRuntimeState(NineKeyDamageData.Build(astro));a.ChooseRoute(a.Definition.routes[0].id,true);a.Gain(astro.Resource,100);var ar=new NineKeyDamageResources(a,astro);
        Check(ar.Reserve("meteor",60)&&a.Resource(astro.Resource)==40,"reservation immediately removes spendable balance");
        Check(!ar.Reserve("meteor",60)&&!ar.Commit("snipe"),"no overlapping reserve or wrong transaction commit");
        a.Gain(astro.Resource,100);Check(ar.Cancel("meteor")&&a.Resource(astro.Resource)==100&&!ar.Cancel("meteor"),"refund capped and exactly once");
        Check(ar.Reserve("meteor",60)&&ar.Commit("meteor")&&!ar.Cancel("meteor")&&a.Resource(astro.Resource)==40,"completed attack never refunded");
        var lancer=CharacterRosterData.Jobs.First(j=>j.index==7);var l=new CharacterRuntimeState(NineKeyDamageData.Build(lancer));l.ChooseRoute(l.Definition.routes[1].id,true);var lr=new NineKeyDamageResources(l,lancer);
        for(int step=1;step<=3;step++)lr.DirectCombo(1,step,0,true);
        for(int step=1;step<=3;step++)lr.DirectCombo(2,step,1,true);
        Check(l.Resource(lancer.Resource)==40,"accelerated alternating and chain-trait bonuses and fractional ticks all halved");
        lr.Supply();Check(l.Resource(lancer.Resource)==70,"direct F supply is not halved");
        var night=CharacterRosterData.Jobs.First(j=>j.index==6);var n=new CharacterRuntimeState(NineKeyDamageData.Build(night));n.ChooseRoute(n.Definition.routes[1].id,true);var nr=new NineKeyDamageResources(n,night);
        var context=new CharacterUseContext(true,true,true,0);
        Check(n.TryUse(CharacterSlot.Gcd1,context,1,skill=>{nr.DirectCombo(1,1,0);return true;},out _),"first chain starts");
        n.Tick(2);Check(n.TryUse(CharacterSlot.Gcd2,context,1,skill=>{nr.DirectCombo(2,1,0);return true;},out _),"second chain starts independently");
        Check(n.Snapshot.combo1==2&&n.Snapshot.combo2==2,"both partial chains retained");
        float heldGcd=n.Snapshot.gcd;Check(nr.FinishDance(out int beats)&&beats==2&&n.Snapshot.combo1==2&&n.Snapshot.combo2==1&&n.Snapshot.gcd==heldGcd,"early finish resets last chain only and never resets shared GCD");
        Check(n.Resource(NineKeyDamageData.Gauge(night))==0&&!nr.FinishDance(out _),"early finish grants no completion reward or repeated free attack");
        nr.Supply();Check(nr.FinishDance(out beats)&&beats==1&&n.Snapshot.combo1==2,"F-funded dance without recent chain preserves other progress");
    }
    static void CompatibilityChecks()
    {
        foreach(var job in CharacterRosterData.Jobs.Where(j=>NineKeyDamageData.Supports(j)&&j.index!=7))
        {
            var state=new CharacterRuntimeState(NineKeyDamageData.Build(job));state.ChooseRoute(state.Definition.routes[0].id,true);
            Check(!state.ChangeAuxiliary("auxiliary.1",NineKeyDamageData.Support,true),"resource F rejects support duration");
            Check(state.Snapshot.auxiliaryTraits[0]==NineKeyDamageData.Reach,"failed auxiliary is atomic");
            var f=state.Definition.loadouts.First(l=>l.slot==CharacterSlot.OffGcd2);
            Check(state.ChangeLoadout(f.id,NineKeyDamageData.Id(job,"f1"),true)&&state.ChangeAuxiliary("auxiliary.1",NineKeyDamageData.Support,true),"support F enables support duration");
            Check(!state.ChangeLoadout(f.id,NineKeyDamageData.Id(job,"f0"),true),"reverse swap cannot strand unsupported auxiliary");
            var invalid=state.Snapshot;invalid.selections.First(s=>s.slot==CharacterSlot.OffGcd2).skillId=NineKeyDamageData.Id(job,"f0");
            invalid.unlocked=invalid.unlocked.Except(new[]{NineKeyDamageData.Id(job,"f1")}).Union(new[]{NineKeyDamageData.Id(job,"f0")}).ToArray();
            Check(!state.Restore(invalid)&&state.Selected(CharacterSlot.OffGcd2).id==NineKeyDamageData.Id(job,"f1"),"incompatible save rejected atomically");
            if(job.index==11)Check(!state.ChangeLoadout(f.id,NineKeyDamageData.Id(job,"f2"),true),"self return is not companion support duration");
            Check(new CharacterRuntimeState(state.Definition).Restore(state.Snapshot),"valid support build restores");
        }
        var lancer=CharacterRosterData.Jobs.First(j=>j.index==7);var actor=new CharacterRuntimeState(NineKeyDamageData.Build(lancer));
        actor.ChooseRoute(actor.Definition.routes[2].id,true);
        var third=actor.Definition.loadouts.First(l=>l.slot==CharacterSlot.OffGcd1);
        var core=actor.Definition.loadouts.First(l=>l.slot==0&&l.routeId==actor.RouteId);
        Check(!actor.ChangeLoadout(core.id,NineKeyDamageData.Core(lancer,2,1),true),"wrong third prevents concentrated discharge on another route");
        Check(actor.ChangeLoadout(third.id,NineKeyDamageData.Id(lancer,"a1"),true)&&actor.ChangeLoadout(core.id,NineKeyDamageData.Core(lancer,2,1),true),"compatible chase supports concentrated discharge across routes");
        Check(!actor.ChangeLoadout(third.id,NineKeyDamageData.Id(lancer,"a2"),true),"cannot remove required skill while core equipped");
        Check(new CharacterRuntimeState(actor.Definition).Restore(actor.Snapshot),"compatible cross-route trait restores");
        actor.ChooseRoute(actor.Definition.routes[0].id,true);
        core=actor.Definition.loadouts.First(l=>l.slot==0&&l.routeId==actor.RouteId);
        Check(!actor.ChangeLoadout(core.id,NineKeyDamageData.Core(lancer,3,0),true),"R floating core requires R spear identity");
        Check(!actor.ChangeLoadout(core.id,NineKeyDamageData.Id(lancer,"core30.f2"),true),"F floating core requires F seed");
        var tactics=actor.Definition.loadouts.First(l=>l.slot==CharacterSlot.OffGcd2);
        Check(actor.ChangeLoadout(tactics.id,NineKeyDamageData.Id(lancer,"f2"),true)&&actor.ChangeLoadout(core.id,NineKeyDamageData.Id(lancer,"core30.f2"),true),"F floating core allowed on assault route");
        Check(!actor.ChangeLoadout(tactics.id,NineKeyDamageData.Id(lancer,"f0"),true),"cannot remove selected core target skill");
        Check(new CharacterRuntimeState(actor.Definition).Restore(actor.Snapshot),"explicit F core target persists in existing snapshot field");
    }
    static void CapacityChecks()
    {
        var night=CharacterRosterData.Jobs.First(j=>j.index==6);
        var state=new CharacterRuntimeState(NineKeyDamageData.Build(night));state.ChooseRoute(state.Definition.routes[1].id,true);
        var resource=new NineKeyDamageResources(state,night);
        for(int i=0;i<5;i++)resource.DirectCombo(1,1,0);
        Check(state.Maximum(night.Resource)==2&&state.Resource(night.Resource)==2,"short dance caps per-hit income at two");
        Check(resource.FinishDance(out int beats)&&beats==2,"short dance cannot emit a three-beat finisher");
        var core=state.Definition.loadouts.First(l=>l.slot==0&&l.routeId==state.RouteId);
        state.ChangeLoadout(core.id,NineKeyDamageData.Core(night,2,1),true);
        Check(state.Maximum(night.Resource)==3&&state.Resource(night.Resource)==0,"increasing capacity does not grant beats");
        state.Gain(night.Resource,3);state.ChangeLoadout(core.id,NineKeyDamageData.Core(night,2,0),true);
        Check(state.Resource(night.Resource)==2,"changing core clamps existing balance");
        var invalid=state.Snapshot;invalid.resources.First(r=>r.id==night.Resource).value=3;
        Check(!state.Restore(invalid)&&state.Resource(night.Resource)==2,"over-cap saved beats rejected atomically");
        Check(new CharacterRuntimeState(state.Definition).Restore(state.Snapshot),"short dance roundtrip");
        var lancer=CharacterRosterData.Jobs.First(j=>j.index==7);var l=new CharacterRuntimeState(NineKeyDamageData.Build(lancer));l.ChooseRoute(l.Definition.routes[0].id,true);
        Check(l.Maximum(lancer.Resource)==120&&l.Resource(lancer.Resource)==0,"spare capacity increases maximum without initial income");
        l.Gain(lancer.Resource,999);Check(l.Resource(lancer.Resource)==120,"all gains respect expanded maximum");
        Check(new CharacterRuntimeState(l.Definition).Restore(l.Snapshot),"expanded resource roundtrip");
        Check(l.ChangeAuxiliary("auxiliary.2","tempest.aux.reach",true)&&l.Maximum(lancer.Resource)==100&&l.Resource(lancer.Resource)==100,"removing capacity clamps balance");
        Check(l.ChangeAuxiliary("auxiliary.2","tempest.aux.capacity",true)&&l.Resource(lancer.Resource)==100,"re-equipping capacity does not refill discarded resource");
        Check(l.Maximum(NineKeyDamageData.Gauge(lancer))==100,"main capacity trait does not alter ultimate gauge");
    }
    static void CoreChargeChecks()
    {
        var job=CharacterRosterData.Jobs.First(j=>j.index==7);var state=new CharacterRuntimeState(NineKeyDamageData.Build(job));
        state.ChooseRoute(state.Definition.routes[0].id,true);string root=NineKeyDamageData.Id(job,"r1"),third=NineKeyDamageData.Id(job,"a0");
        Check(state.MaximumCharges(root)==2&&state.MaximumCharges(third)==1,"R core increases only R charges");
        Check(state.Snapshot.skills.First(s=>s.id==root).charges==1,"choosing larger capacity does not create a free charge");
        state.Tick(12);var context=new CharacterUseContext(true,true,true,0);
        Check(state.TryUse(CharacterSlot.Identity,context,1,s=>true,out _)&&state.TryUse(CharacterSlot.Identity,context,1,s=>true,out _),"recharged R can use both charges");
        Check(!state.TryUse(CharacterSlot.Identity,context,1,s=>true,out _),"third R blocked by recharge");
        var core=state.Definition.loadouts.First(l=>l.slot==0&&l.routeId==state.RouteId);
        Check(state.ChangeLoadout(core.id,NineKeyDamageData.Id(job,"core10.a0"),true),"explicit third target selectable");
        Check(state.MaximumCharges(root)==1&&state.MaximumCharges(third)==2,"third core cannot also strengthen R");
        Check(state.Snapshot.skills.First(s=>s.id==root).charges==0&&state.Snapshot.skills.First(s=>s.id==third).charges==1,"target switching preserves spent charges and does not fill new capacity");
        state.Tick(12);Check(new CharacterRuntimeState(state.Definition).Restore(state.Snapshot),"core-dependent two-charge snapshot restores");
        var bad=state.Snapshot;bad.coreTrait=NineKeyDamageData.Core(job,1,0);
        Check(!state.Restore(bad)&&state.Snapshot.coreTrait==NineKeyDamageData.Id(job,"core10.a0"),"snapshot cannot carry bonus charges on nonselected target");
        var legacy=new CharacterRuntimeState(CharacterRosterData.Definition(job));legacy.ChooseRoute(legacy.Definition.routes[0].id,true);
        var old=legacy.Snapshot;var migrated=new CharacterRuntimeState(NineKeyDamageData.Build(job));
        Check(migrated.Restore(old)&&migrated.Snapshot.skills.First(s=>s.id==root).charges==2&&migrated.Snapshot.skills.First(s=>s.id==root).recharge==0,"fully rested old build migrates to fully ready new capacity");
        string oldRoot=old.selections.First(s=>s.slot==CharacterSlot.Identity).skillId;
        var spent=old.skills.First(s=>s.id==oldRoot);spent.charges=0;spent.recharge=spent.rechargeTotal=10;
        Check(migrated.Restore(old)&&migrated.Snapshot.skills.First(s=>s.id==root).charges==0&&migrated.Snapshot.skills.First(s=>s.id==root).recharge>=10,"pending old cooldown cannot become a free first charge while bonus charge recharges");
        Check(old.skills.First(s=>s.id==oldRoot).recharge==10&&old.skills.First(s=>s.id==oldRoot).charges==0,"charge migration does not mutate stored source");
    }
    static void ControlChecks()
    {
        var astro=CharacterRosterData.Jobs.First(j=>j.index==10);
        var state=new CharacterRuntimeState(NineKeyDamageData.Build(astro));state.ChooseRoute(state.Definition.routes[1].id,true);
        var normal=new CharacterUseContext(true,true,true,0);int hits=0;
        Check(state.TryUse(CharacterSlot.Gcd2,normal,10,s=>{hits++;return true;},out _)&&hits==0&&state.Snapshot.castRemaining==1.5f,"ordinary second input begins an actual framework cast");
        Check(state.TryUse(CharacterSlot.Movement,normal,0,s=>true,out _)&&state.Snapshot.castRemaining==1.5f,"astro authored Space preserves ordinary second cast");
        state.Tick(1.5f,false);Check(state.FinishCast(normal,s=>{hits++;return true;},out _)&&hits==1,"preserved cast completes exactly once");
        state.Tick(2);Check(state.TryUse(CharacterSlot.Gcd2,normal,10,s=>true,out _),"next ordinary stage casts");
        var dead=new CharacterUseContext(false,true,true,0);
        Check(!state.TryUse(CharacterSlot.Job1,dead,0,s=>true,out _)&&state.Snapshot.castRemaining>0,"invalid priority response retains cast");
        Check(state.TryUse(CharacterSlot.Job1,normal,0,s=>true,out _)&&state.Snapshot.castSkillId=="","valid defense cancels unfinished ordinary spell");
        Check(!state.FinishCast(normal,s=>{hits++;return true;},out _)&&hits==1,"cancelled spell cannot auto complete");
        var instant=new CharacterUseContext(true,true,true,0,true,25,1,0);
        Check(state.TryUse(CharacterSlot.Gcd2,instant,10,s=>{hits++;return true;},out _)&&hits==2&&state.Snapshot.castSkillId=="","validated instant override executes moving second without starting a cast");
        Check(state.MaximumCharges(NineKeyDamageData.Id(astro,"m"))==2,"default mobile auxiliary changes actor movement charges");
        Check(state.ChangeAuxiliary("auxiliary.2",NineKeyDamageData.Guard,true)&&state.MaximumCharges(NineKeyDamageData.Id(astro,"m"))==1,"removing mobile returns base movement capacity");
    }
    static void ArrowAndFieldChecks()
    {
        for(int maximum=1;maximum<=2;maximum++)
        {
            var planned=new DamagePrismState();var target=new DamagePoint(12,0);
            for(int turn=0;turn<6;turn++)
            {
                var planCommand=planned.Plan(default,target,maximum);if(planCommand==null)break;
                Check(planCommand.Valid,"planned mirror planCommand uses a finite unit normal");
                var planPreview=planned.Preview(planCommand,maximum,20);
                Check(planPreview!=null,"planned planCommand executes through ordinary placement or adjustment");
                planned=planPreview;planned.Tick(.25f);
            }
            Check(planned.Snapshot.mirrors.Length==maximum&&DamagePrismState.ReflectionsToHit(planned.Trace(default,30),target,.1f)==maximum,"AI plan creates actual requested reflection count");
            Check(planned.Plan(default,target,maximum)==null,"settled mirror plan does not repeatedly adjust");
            var originalIds=planned.Snapshot.mirrors.Select(m=>m.id).ToArray();float before=planned.Snapshot.mirrors[0].remaining;
            target=new DamagePoint(8,6);
            for(int turn=0;turn<8;turn++)
            {
                var planCommand=planned.Plan(default,target,maximum);if(planCommand==null)break;
                Check(planCommand.mirrorId!=0,"target movement adjusts existing mirrors instead of buying replacements");
                planned=planned.Preview(planCommand,maximum,20);Check(planned!=null,"moving-target plan remains a legal state planCommand");planned.Tick(.25f);
            }
            Check(planned.Snapshot.mirrors.Select(m=>m.id).SequenceEqual(originalIds)&&planned.Snapshot.mirrors[0].remaining<=before,"AI adjustment preserves IDs and never extends lifetime");
            Check(DamagePrismState.ReflectionsToHit(planned.Trace(default,30),target,.1f)==maximum,"adjusted reflection path reaches moved target");
            Check(planned.Plan(default,default,maximum)==null&&planned.Plan(default,new DamagePoint(99,0),maximum)==null,"degenerate and unreachable plans are rejected");
        }
        var arrows=new DamageArrowState();Check(arrows.LaunchCurve(1,3,"g20",4)&&!arrows.ReserveStraight(1,2,"g1",2.5f),"curve not reservable before displayed window");
        arrows.Tick(1.5f,(p,s)=>true);Check(!arrows.ReserveStraight(2,2,"g1",2.5f)&&arrows.ReserveStraight(1,2,"g1",2.5f)&&!arrows.ReserveStraight(1,2,"g1",2.5f),"same target one-to-one reservation");
        Check(arrows.Snapshot.convergence==0&&arrows.Restore(arrows.Snapshot),"reservation alone gives no convergence and restores");
        int calls=0;var result=arrows.Tick(3,(p,s)=>{calls++;Check(arrows.Tick(1,(q,t)=>true).Length==0,"due arrows removed before callbacks");return true;});
        Check(calls==2&&result.Length==1&&result[0].Converged&&result[0].Pair.curveStep==3&&result[0].Pair.straightStep==2,"actual two hits and original launch steps retained");
        Check(arrows.ConsumeConvergence()==1&&arrows.ConsumeConvergence()==0,"convergence consumed once");
        arrows.LaunchCurve(-1,1,"g21",2);arrows.ReserveStraight(-1,3,"g1",2.5f);
        result=arrows.Tick(3,(p,s)=>!s);Check(!result[0].Converged&&arrows.Snapshot.convergence==0,"one failed impact prevents paired bonus");
        var spears=new DamageSpearState();Check(spears.Place(new DamagePoint(1,2),20,10,false,0)&&spears.Place(new DamagePoint(2,3),20,10,true,25)&&!spears.Place(default,20,10,false,0),"two spear cap blocks silently replacing charged investment");
        spears.Charge(1,3,true);spears.Charge(2,4,true);spears.Charge(2,1,false);Check(spears.Snapshot.spears.All(s=>s.charge==0),"only valid basic second inputs charge spears");
        for(int i=0;i<5;i++)spears.Charge(2,2,true);Check(spears.Snapshot.spears.All(s=>s.charge==3),"all active spears charge up to three");
        Check(spears.Tick(4).Length==4&&spears.Snapshot.spears[1].stored==25,"compressed spear replaces automatic ticks with capped storage");
        spears.Move(new DamagePoint(8,9));Check(spears.Snapshot.spears.All(s=>s.remaining==16&&s.point.x==8),"movement preserves lifetimes and charges");
        Check(spears.Restore(spears.Snapshot),"spear snapshot roundtrip");
        var recalled=spears.Recall();Check(recalled.Sum(s=>s.charge*5)==30&&spears.Recall().Length==0,"two invested spears refund at most thirty once");
        spears.Place(default,2,10,true,20);spears.Tick(10);Check(spears.Recall().Length==0,"natural expiry cannot release storage or refund");
        var gravity=new DamageGravityState();Check(gravity.Begin(default,12)&&!gravity.Begin(default,12),"one gravity field");
        Check(gravity.Toggle(out bool protection)&&protection,"first expansion provides protection");gravity.Toggle(out _);gravity.Toggle(out protection);Check(!protection,"mode cycling cannot repeat protection");
        gravity.Tick(3);Check(gravity.Follow(new DamagePoint(3,4),true)&&gravity.Snapshot.remaining==9&&!gravity.Follow(default,false),"only second finisher relocates without renewal");
        Check(gravity.Restore(gravity.Snapshot)&&gravity.Tick(20)==9&&!gravity.Active&&!gravity.Toggle(out _),"finite field ticks and expiry");
        var prism=new DamagePrismState();Check(prism.Place(new DamagePoint(2,0),new DamagePoint(-1,1),2,12)&&prism.Place(new DamagePoint(2,2),new DamagePoint(1,1),2,12),"two oriented mirrors");
        var command=new DamagePrismCommand{mirrorId=2,point=new DamagePoint(-3.5f,4),normal=new DamagePoint(0,1)};
        Check(DamagePrismCommand.Decode(command.Encode(),out var decoded)&&decoded.mirrorId==2&&decoded.point.x==-3.5f&&decoded.normal.z==1,"prism command preserves selected id ground and normal");
        foreach(string bad in new[]{"-1|0|0|1|0","0|NaN|0|1|0","0|0|0|0|0","0|0|0|3|0","0|0|0|Infinity|0","0|0|0|1|0|extra","2147483648|0|0|1|0"})
            Check(!DamagePrismCommand.Decode(bad,out _),"invalid prism command rejected: "+bad);
        Check(prism.CanAdjust(2)&&!prism.CanAdjust(77),"only actor owned mirror id can be adjusted");
        var preview=prism.Preview(command,2,20);
        Check(preview!=null&&preview.Snapshot.firstId==2&&preview.Snapshot.mirrors[1].point.x==-3.5f,"adjust preview uses selected first mirror and proposed position");
        Check(prism.Snapshot.firstId==1&&prism.Snapshot.mirrors[1].point.x==2&&prism.Snapshot.adjustDelay==0,"preview leaves authoritative selection geometry and adjustment gate untouched");
        Check(preview.Snapshot.mirrors[1].remaining==12&&prism.Snapshot.mirrors[1].remaining==12,"adjust preview preserves original lifetime");
        command.mirrorId=0;
        var replacement=prism.Preview(command,2,20);
        Check(replacement.Snapshot.mirrors[1].id==3&&replacement.Snapshot.firstId==3&&prism.ReplacedId(2)==1,"preview follows exact oldest replacement and first mirror rules");
        Check(prism.Snapshot.nextId==3&&prism.Snapshot.mirrors[0].id==1,"preview does not consume id or evict live mirror");
        command.mirrorId=99;Check(prism.Preview(command,2,20)==null,"missing mirror cannot preview as free new placement");
        command.mirrorId=2;Check(preview.Preview(command,2,20)==null,"preview honors adjustment gate just like host execution");
        preview.Tick(2);Check(prism.Snapshot.mirrors[0].remaining==12,"preview timer never advances authoritative mirror timer");
        var path=prism.Trace(default,8);Check(path.Length==4&&DamagePrismState.Hits(path,new DamagePoint(0,2),.1f)&&!DamagePrismState.Hits(path,new DamagePoint(1,0),.1f),"two actual reflections hit target but initial incoming beam is not bonus damage");
        Check(!DamagePrismState.Hits(path,new DamagePoint(7,7),.5f),"failed geometry does not auto track boss");
        var openPath=DamagePrismState.Clip(path,(a,b)=>null);
        Check(openPath.Length==4&&DamagePrismState.Hits(openPath,new DamagePoint(0,2),.1f),"unobstructed beam preserves two reflections");
        int rayCalls=0;
        var incomingWall=DamagePrismState.Clip(path,(a,b)=>{rayCalls++;return .5f;});
        Check(incomingWall.Length==2&&rayCalls==1&&!DamagePrismState.Hits(incomingWall,new DamagePoint(0,2),.1f),"incoming wall prevents all mirror bonuses and later raycasts");
        rayCalls=0;
        var reflectedWall=DamagePrismState.Clip(path,(a,b)=>++rayCalls==2?(float?).5f:null);
        Check(reflectedWall.Length==3&&rayCalls==2&&DamagePrismState.Hits(reflectedWall,new DamagePoint(2,.25f),.1f),"first reflection reaches targets before wall");
        Check(!DamagePrismState.Hits(reflectedWall,new DamagePoint(0,2),.1f),"wall before second mirror prevents its outgoing damage");
        var finalWall=DamagePrismState.Clip(path,(a,b)=>a.z>1?(float?).5f:null);
        Check(finalWall.Length==4&&!DamagePrismState.Hits(finalWall,new DamagePoint(0,2),.1f),"final segment stops at wall rather than full remaining range");
        Check(DamagePrismState.Clip(path,(a,b)=>float.NaN).Length==0&&DamagePrismState.Clip(path,(a,b)=>-1).Length==0&&DamagePrismState.Clip(path,(a,b)=>999).Length==0,"invalid collision distances fail closed");
        Check(prism.Trace(default,8).Length==4,"terrain clipping does not mutate mirror placement or lifetime");
        prism.Tick(3);Check(prism.Adjust(2,new DamagePoint(4,2),new DamagePoint(1,1),.25f)&&!prism.Adjust(1,default,new DamagePoint(1,0),.25f),"adjust preserves lifetime and has own input gate");
        Check(prism.Snapshot.mirrors.All(m=>m.remaining==9)&&prism.Trace(default,8).Length==3,"off-path second mirror does not create a fake reflection");
        Check(prism.Restore(prism.Snapshot,2)&&prism.ReplacedId(2)==1,"snapshot and oldest replacement preview");
        prism.Place(new DamagePoint(1,1),new DamagePoint(1,0),2,12);Check(prism.Snapshot.mirrors.Length==2&&prism.Snapshot.firstId==3,"replacement updates first mirror when replaced");
        prism.Tick(20);Check(prism.Trace(default,8).Length==0&&prism.Snapshot.firstId==0,"expiry clears selected mirror without stray path");
    }
    static void MirrorChecks()
    {
        var memory=new DamageMemoryState();
        Check(!memory.Record("v",true,2,10)&&!memory.Record("g1",false,2,10),"ultimate and missing manual finisher cannot record");
        Check(memory.Record("g1",true,2,10)&&memory.Record("g20",true,2,10)&&memory.Record("g22",true,2,10),"records actual basic finishers");
        Check(memory.Snapshot.records.Select(r=>r.key).SequenceEqual(new[]{"g20","g22"}),"recent two chronological records");
        memory.ToggleLock();Check(!memory.Record("g1",true,2,10),"lock prevents overwriting");
        memory.Tick(4);Check(memory.Snapshot.records.All(r=>r.remaining==6),"lock does not extend lifetime");
        Check(memory.Restore(memory.Snapshot,2)&&memory.Take(2,out var keys)&&keys.SequenceEqual(new[]{"g20","g22"})&&!memory.Snapshot.locked,"consumption removes records and unlocks");
        Check(!memory.Take(2,out _),"records cannot replay twice");
        memory.Record("g1",true,1,2);memory.ToggleLock();memory.Tick(3);Check(!memory.CanReplay(1),"locked record still expires");
        var projection=new DamageProjectionState();Check(projection.Begin(8)&&!projection.Begin(8)&&!projection.Execute(out _),"prepared projection prevents stacking and execution");
        Check(projection.LaunchSecond(),"future second step uses projection");projection.Tick(10);
        Check(!projection.Active&&projection.Snapshot.usedInCombo&&!projection.CompleteSecond(true),"expiry cannot restore paid combo completion resource");
        Check(projection.CompleteSecond(true)&&projection.Snapshot.empoweredExecution,"next entirely direct combo may generate one execution bonus");
        projection.CompleteSecond(true);Check(projection.Execute(out bool bonus)&&bonus&&projection.Execute(out bonus)&&!bonus,"direct bonus cannot stack or duplicate");
        Check(projection.Restore(projection.Snapshot),"projection roundtrip");
    }
    static void WindowChecks()
    {
        var burst=new DamageBurstState();Check(burst.Begin(16,.25f)&&!burst.Begin(16,.25f),"burst cannot overwrite pending rights");
        Check(!burst.Fire(.25f,out _,out _),"burst interval gates repeated input");
        burst.CompleteCombo();burst.CompleteCombo();burst.Tick(.25f);
        Check(burst.Fire(.25f,out bool strengthened,out bool final)&&strengthened&&!final,"multiple completions store just one reinforcement");
        burst.Tick(.25f);Check(burst.Fire(.25f,out strengthened,out final)&&!strengthened&&final&&!burst.Active,"last bundle consumes set, synergy only here");
        Check(!burst.Fire(.25f,out _,out _),"no extra bundle");
        burst.Begin(16,0);burst.CompleteCombo();burst.Tick(20);burst.Begin(16,0);
        Check(burst.Fire(.25f,out strengthened,out _)&&!strengthened,"expired precision not carried into new set");
        var season=new DamageSeasonState();Check(season.Snapshot.season==0&&season.Delay(4)&&!season.Delay(4),"fixed spring start and one delay per visit");
        season.Tick(8);Check(season.Snapshot.season==0&&season.Snapshot.remaining==4,"delay extends this visit");
        season.Tick(4);Check(season.Snapshot.season==1&&!season.Snapshot.delayed,"boundary advances once and releases next visit delay");
        Check(season.Advance()==1&&season.Snapshot.season==2&&season.Snapshot.remaining==8,"manual arrow uses old season then restarts next");
        season.Tick(40);Check(season.Snapshot.season==3&&season.Snapshot.remaining==8,"large tick handles fixed cycle without timing drift");
        Check(season.Restore(season.Snapshot),"season snapshot roundtrip");
        season.Reset(8);Check(season.Snapshot.season==0,"retry restarts spring");
        var instant=new DamageInstantState();Check(instant.Begin(3,20,30)&&!instant.Begin(3,20,30),"finite preparation cannot stack");
        Check(instant.Toggle(.25f)&&!instant.CanConsume&&instant.Snapshot.cooldown==30,"preparation cooldown never blocks toggle");
        Check(!instant.Consume(true)&&!instant.Toggle(.25f),"OFF preserves rights and toggle has own interval");
        instant.Tick(.25f);instant.Toggle(.25f);Check(!instant.Consume(false)&&instant.Snapshot.rights==3,"invalid second cast does not spend right");
        Check(instant.Consume(true)&&instant.Consume(true)&&instant.Consume(true)&&!instant.Consume(true),"three rights only");
        Check(!instant.Begin(3,20,30)&&instant.Snapshot.cooldown>0,"rights depletion does not reset preparation cooldown");
        instant.Tick(30);Check(instant.Begin(3,20,30)&&instant.Restore(instant.Snapshot),"reuse after cooldown and active restore");
        instant.Tick(20);Check(!instant.Active&&!instant.CanConsume&&instant.Snapshot.cooldown==10,"expiry drops rights not remaining cooldown");
    }
    static void TacticalChecks()
    {
        var info=new DamageWeaknessState();
        Check(!info.Grant(1,false,true,2)&&info.Grant(1,true,true,2),"directional info requires flank or rear");
        Check(info.Grant(-2,false,false,2)&&!info.Grant(3,true,true,2),"directionless fallback and total storage cap");
        Check(!info.Consume(3)&&info.Count(1)==1&&info.Consume(1)&&!info.Consume(1),"target-bound info consumes once without current direction");
        Check(info.Restore(info.Snapshot,2),"info snapshot roundtrip");
        var copy=info.Snapshot;copy.targets[0]=0;Check(!info.Restore(copy,2)&&info.Count(-2)==1,"invalid info snapshot leaves state intact");
        info.RemoveEnemy(-2);Check(info.Snapshot.targets.Length==0,"dead target cleanup releases capacity");
        info.Grant(1,true,true,3);info.Grant(1,true,true,3);info.Grant(2,true,true,3);
        int probes=0;info.Prune(id=>{probes++;return id==2;});
        Check(probes==2&&info.Count(1)==0&&info.Count(2)==1,"prune queries each enemy once and removes only dead target information");
        Check(info.Grant(3,true,true,3)&&info.Count(3)==1,"dead target cleanup releases shared capacity without transferring its information");
        info.Prune(null);info.Prune(id=>true);
        Check(info.Count(2)==1&&info.Count(3)==1,"missing cleanup callback and living targets preserve stored opportunities");
        var seal=new DamageSealState();Check(seal.Attach(1,DamageSealTrigger.CastEnd,8)&&!seal.Attach(2,DamageSealTrigger.PatternMoveEnd,8),"one seal cannot be overwritten");
        Check(!seal.OnActionEnded(2,DamageSealTrigger.CastEnd,out _)&&!seal.OnActionEnded(1,DamageSealTrigger.PatternMoveEnd,out _),"only tagged target and semantic event trigger");
        Check(seal.OnActionEnded(1,DamageSealTrigger.CastEnd,out var hit)&&hit.Enhanced&&hit.Target==1,"normal or counter cast completion strengthens seal");
        Check(!seal.OnActionEnded(1,DamageSealTrigger.CastEnd,out _)&&!seal.Tick(20,out _),"condition and expiry cannot both resolve");
        seal.Attach(-2,DamageSealTrigger.PatternMoveEnd,8);Check(seal.Restore(seal.Snapshot)&&seal.Tick(10,out hit)&&!hit.Enhanced&&hit.Target==-2,"expiry is finite basic detonation");
        var grenade=new DamageGrenadeState();Check(grenade.Attach(1,6,10,2)&&!grenade.Attach(2,6,10,2),"one grenade per caster, no overwrite");
        Check(grenade.Predicted(1)==4&&grenade.Predicted(2)==8,"preview timers");
        Check(grenade.CompleteCombo(2,out int target)&&target==0&&grenade.Snapshot.remaining==8,"second finisher delays");
        grenade.CompleteCombo(2,out _);grenade.CompleteCombo(2,out _);Check(grenade.Snapshot.remaining==10,"absolute lifetime caps repeated delay");
        grenade.Tick(4,out _);grenade.CompleteCombo(2,out _);Check(grenade.Snapshot.remaining==6,"late delay cannot renew original lifetime");
        Check(grenade.Restore(grenade.Snapshot),"grenade roundtrip");
        grenade.CompleteCombo(1,out _);grenade.CompleteCombo(1,out _);Check(grenade.CompleteCombo(1,out target)&&target==1&&!grenade.Active,"zero timer immediately consumes grenade");
        Check(!grenade.Tick(20,out _)&&!grenade.CompleteCombo(1,out _),"no duplicate explosion after consumed timer");
        grenade.Attach(1,6,10,2);Check(grenade.Tick(20,out target)&&target==1,"large frame detonates once");
    }
    static void PreparationChecks()
    {
        var hands=new DamageHandsState();Check(!hands.Fire(1)&&!hands.Fire(2),"empty hands block only shots");
        Check(hands.BeginReload(false,2,6,3)&&!hands.BeginReload(true,2,6,3),"one hand job at a time");
        Check(hands.Tick(1)==0&&!hands.Fire(1),"no early reload or firing reloading hand");
        Check(hands.Tick(1)==1&&hands.Total==3&&hands.Tick(1)==0,"reload completes once");
        Check(hands.Fire(1)&&hands.Fire(1)&&hands.Fire(1)&&!hands.Fire(1),"three actual launches consume three ammo");
        hands.Tick(3);Check(hands.BeginReload(true,2,6,3)&&hands.Snapshot.reloading==2,"opposite selection uses last fired hand");
        hands.Tick(2);hands.Tick(4);Check(hands.BeginReload(false,2,6,3)&&hands.Fire(2),"other hand fires while last fired hand reloads");
        Check(hands.Restore(hands.Snapshot),"hand state roundtrip while reloading");
        var bad=hands.Snapshot;bad.left=99;Check(!hands.Restore(bad)&&hands.Snapshot.left==0,"malformed hand restore atomic");
        var gun=CharacterRosterData.Jobs.First(j=>j.index==8);var g=new CharacterRuntimeState(NineKeyDamageData.Build(gun));g.ChooseRoute(g.Definition.routes[2].id,true);g.Gain(gun.Resource,6);var gr=new NineKeyDamageResources(g,gun);var snipe=new DamageSnipeState(gr);
        Check(snipe.Begin(-9,3,1,8,3)&&g.Resource(gun.Resource)==3,"aim reserves ammo and allows negative instance ID");
        snipe.Tick(.5f);Check(snipe.Interrupt()&&!snipe.Interrupt()&&g.Resource(gun.Resource)==6,"unfinished aim refund once");
        snipe.Begin(-9,3,1,8,3);snipe.Tick(1);Check(snipe.Ready&&!snipe.Interrupt()&&g.Resource(gun.Resource)==3,"completed aim persists through defense without refund");
        Check(!snipe.Launch(false,out _)&&snipe.Snapshot.shots==3,"temporarily invalid target preserves prepared shots");
        Check(snipe.Launch(true,out int target)&&target==-9&&!snipe.Launch(true,out _),"no double launch before shot resolution");
        Check(!snipe.ResolveShot(false)&&snipe.Snapshot.shots==2,"miss consumes shot but not mark permission");
        snipe.Launch(true,out _);Check(snipe.ResolveShot(true)&&!snipe.ResolveShot(true),"first actual hit grants exactly one set mark");
        Check(snipe.Restore(snipe.Snapshot),"ready split shot roundtrip");
        snipe.Launch(true,out _);Check(!snipe.ResolveShot(true)&&!snipe.Active,"last split shot cannot duplicate mark");
        snipe.Begin(1,3,1,8,1);snipe.Tick(20);Check(!snipe.Active&&g.Resource(gun.Resource)==0,"large frame expires completed aim without refund or automatic shot");
        var astro=CharacterRosterData.Jobs.First(j=>j.index==10);var a=new CharacterRuntimeState(NineKeyDamageData.Build(astro));a.ChooseRoute(a.Definition.routes[0].id,true);a.Gain(astro.Resource,100);var ar=new NineKeyDamageResources(a,astro);var meteor=new DamageMeteorState(ar);
        Check(meteor.BeginOrResume(true,60,3,1,12),"meteor reserves once");
        Check(!meteor.Tick(1.5f,true)&&meteor.Snapshot.completed==1,"completed segment saved without early hit");
        meteor.Interrupt();Check(meteor.Snapshot.progress==0&&meteor.Snapshot.completed==1,"interrupt discards only incomplete segment");
        Check(!meteor.BeginOrResume(false,60,3,1,12)&&meteor.BeginOrResume(true,60,3,1,12)&&a.Resource(astro.Resource)==40,"valid resume does not reserve twice");
        Check(meteor.Restore(meteor.Snapshot),"unfinished meteor snapshot roundtrip");
        Check(meteor.Tick(2,true,out bool uninterrupted)&&!uninterrupted&&!meteor.Tick(1,true)&&!meteor.Active,"resumed final segment fires once without uninterrupted reward or completed spell storage");
        a.Gain(astro.Resource,60);meteor.BeginOrResume(true,60,3,1,12);meteor.Tick(1,true);meteor.Interrupt();
        Check(!meteor.Tick(20,false)&&!meteor.Active&&a.Resource(astro.Resource)==100,"expired unfinished meteor refunds once");
        Check(!meteor.Tick(20,false)&&a.Resource(astro.Resource)==100,"expiry cannot repeat refund");
        Check(meteor.BeginOrResume(true,60,3,1,12),"new uninterrupted cast reserves normally");
        Check(!meteor.Tick(1,true,out uninterrupted)&&!uninterrupted,"partial segment never reports reward");
        Check(meteor.Restore(meteor.Snapshot)&&meteor.Tick(2,true,out uninterrupted)&&uninterrupted,"uninterrupted progress roundtrip earns reward only at completion");
        Check(!meteor.Tick(1,true,out uninterrupted)&&!uninterrupted,"completion reward cannot replay");
        a.Gain(astro.Resource,60);meteor.BeginOrResume(true,60,3,1,12);
        Check(!meteor.Tick(1,false,out uninterrupted)&&!uninterrupted&&meteor.Snapshot.interrupted,"target loss records interruption even before first segment");
        var malformed=meteor.Snapshot;malformed.interrupted=false;
        Check(!meteor.Restore(malformed)&&meteor.Snapshot.interrupted,"paused cast cannot claim an uninterrupted history");
        meteor.BeginOrResume(true,60,3,1,12);
        Check(meteor.Tick(3,true,out uninterrupted)&&!uninterrupted,"target reacquisition preserves basic completion but not uninterrupted reward");
    }
}
