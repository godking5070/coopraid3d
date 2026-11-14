using System;
using System.Linq;
using RaidRogue;
sealed class Noop { public void Reset(){} public void ClearResolvedCombatHits(){} }
sealed class NineKeyTankKit { public void Clear(){} }
sealed class NineKeyHealerKit { public void Clear(){} }
sealed class AegisPaladin { public void ClearTransient(){} public void RecalculateStats(){} }
sealed class CharacterKit { public void Clear(){} public void Recalculate(){} }
sealed class CombatStatusEffects { public void Clear(){} }
static class RaidCombatAuthority { public static bool Replica; }
sealed class StageManager { public static StageManager Instance; public string RunId="run",ChapterId="part1"; }
sealed class NineKeyDamageKit
{
    public static bool Supports(CharacterDefinition definition)=>CharacterSlots.IsNineKey(definition)&&NineKeyDamageData.Supports(CharacterRosterData.Find(definition?.id));
    public CharacterRuntimeState runtime;public DamageSeasonState _season;public DamageHandsState _hands;
    public string observedCore,observedRoute;public int clears;
    RosterJob Job=>CharacterRosterData.Find(runtime.Definition.id);
    bool Core(int r,int v)=>runtime.Snapshot.coreTrait==NineKeyDamageData.Core(Job,r,v);
    public void Clear(){clears++;observedCore=runtime.Snapshot.coreTrait;observedRoute=runtime.Snapshot.routeId;/* ACTUAL_RESET_FIELDS */}
}
sealed class ActorFixture
{
    public CharacterRuntimeState _state;public NineKeyDamageKit kit;
    public CharacterDefinition Definition=>_state?.Definition;
    Noop _actionExecution=new Noop(),_player=new Noop(),_bot=new Noop();object _executingSkill;
    void BindLegacy(){}
    T GetComponent<T>() where T:class=>kit as T;
    /* ACTUAL_RESET_ENCOUNTER */
}
static class DamageEncounterResetTests
{
    static int checks;static void Check(bool condition,string why){checks++;if(!condition)throw new Exception(why);}
    static void Main()
    {
        foreach(var job in CharacterRosterData.Jobs.Where(j=>j.index>=6&&j.index<=11))
        for(int route=1;route<=3;route++)
        for(int variant=0;variant<=1;variant++)
        {
            var state=new CharacterRuntimeState(NineKeyDamageData.Build(job));
            string id=state.Definition.routes[route-1].id;state.ChooseRoute(id,true);
            if(variant==1)
            {
                foreach(string slot in new[]{"g2","a","c","f"})
                    Check(state.ChangeLoadout(NineKeyDamageData.Id(job,"loadout."+slot),NineKeyDamageData.Id(job,slot+"1"),true),"alternate skill selected before departure");
                Check(state.ChangeLoadout(NineKeyDamageData.Id(job,"loadout.core"+route),NineKeyDamageData.Core(job,route,1),true),"variant one selected before departure");
                Check(state.ChangeAuxiliary("auxiliary.1",state.Definition.auxiliaryTraits[2].id,true),"first nondefault auxiliary selected");
                Check(state.ChangeAuxiliary("auxiliary.2",state.Definition.auxiliaryTraits[3].id,true),"second nondefault auxiliary selected");
            }
            var build=state.Snapshot;
            state.BeginRun("previous-run",CharacterRewardKey.CurrentChapter);
            state.ClearedStage(3);var unlock=state.Definition.unlocks.Single();
            Check(state.ChooseSkill(unlock.id,unlock.choices[0],state.RewardKey(unlock),true),"prior-run ultimate unlocked through real progression");
            state.SetBuildEffect("prior.run.effect",1,1);
            foreach(var resource in state.Definition.resources)state.Gain(resource.id,state.Maximum(resource.id));
            var context=new CharacterUseContext(true,true,true,0);
            Check(state.TryUse(CharacterSlot.Job1,context,1,s=>true,out string failure),"prior-run defense cooldown used: "+failure);
            Check(state.TryUse(CharacterSlot.Gcd1,context,1,s=>true,out failure),"prior-run combo started: "+failure);
            Check(state.Snapshot.gcd>0&&state.Snapshot.combo1>1,"prior-run transient state is dirty");
            StageManager.Instance=new StageManager();
            var kit=new NineKeyDamageKit{runtime=state};var actor=new ActorFixture{_state=state,kit=kit};
            foreach(var reason in new[]{CharacterReset.NewRun,CharacterReset.Retry,CharacterReset.NextStage})
            {
                actor.ResetEncounter(reason);
                Check(state.Snapshot.routeId==id,"route survives encounter boundary");
                Check(kit.observedCore==state.Snapshot.coreTrait&&kit.observedCore.Length>0,"DPS reset sees final core");
                Check(kit.observedRoute==id,"DPS reset sees final route");
                Check(state.Snapshot.coreTrait==build.coreTrait,"selected core survives boundary");
                Check(state.Snapshot.auxiliaryTraits.SequenceEqual(build.auxiliaryTraits),"both selected auxiliaries survive boundary");
                foreach(var selected in build.selections)
                    Check(state.Selected(selected.slot).id==selected.skillId,"selected skill survives boundary: "+selected.slot);
                Check(kit._season.Snapshot.period==(job.index==9&&route==3&&variant==0?6:8),"actual season initialization uses final core");
                Check(kit._season.Snapshot.season==0&&!kit._season.Snapshot.delayed,"new encounter starts spring without delay");
                Check(kit._hands.Snapshot.capacity==(job.index==8&&route==1&&variant==1?5:3),"actual hand capacity uses preserved core");
                Check(kit._hands.Snapshot.left==0&&kit._hands.Snapshot.right==0&&kit._hands.Snapshot.reloading==0,"hand ammunition and reload reset");
                var snapshot=state.Snapshot;
                Check(snapshot.runId=="run"&&snapshot.chapterId=="part1","new run identity assigned");
                Check(snapshot.clearedStage==0&&snapshot.clearedEncounters.Length==0&&snapshot.chosenRules.Length==0&&snapshot.chosenRewardKeys.Length==0&&snapshot.automaticChoices.Length==0,"prior progression discarded");
                Check(snapshot.effects.Length==0,"prior run build effects discarded");
                Check(!state.Unlocked(CharacterSlot.Ultimate)&&state.Unlocked(CharacterSlot.Identity),"ultimate relocked while selected route identity remains available");
                foreach(var selected in build.selections.Where(s=>state.Definition.slots.Any(d=>d.slot==s.slot&&d.initiallyUnlocked)))
                    Check(state.Unlocked(selected.slot),"selected baseline replacement remains unlocked");
                Check(snapshot.resources.All(r=>r.value==state.Definition.resources.Single(d=>d.id==r.id).initial),"resources reset to configured start values");
                Check(snapshot.skills.All(s=>s.charges==state.MaximumCharges(s.id)&&s.recharge==0),"cooldowns and charge budgets reset for preserved build");
                Check(snapshot.gcd==0&&snapshot.combo1==1&&snapshot.combo2==1&&snapshot.castSkillId.Length==0,"combo cast and GCD reset");
            }
            Check(kit.clears==3,"one DPS reset per boundary");
            RaidCombatAuthority.Replica=true;actor.ResetEncounter(CharacterReset.NewRun);RaidCombatAuthority.Replica=false;
            Check(kit.clears==3,"replica never independently resets");
        }
        CoupledBuilds();
        Console.WriteLine("Damage encounter reset checks PASS: "+checks);
    }
    static void CoupledBuilds()
    {
        var night=CharacterRosterData.Jobs.Single(j=>j.index==6);
        var state=new CharacterRuntimeState(NineKeyDamageData.Build(night));
        state.ChooseRoute(state.Definition.routes[0].id,true);
        Check(!state.ChangeAuxiliary("auxiliary.1",NineKeyDamageData.Support,true),"support auxiliary is incompatible with default resource F");
        Check(state.ChangeLoadout(NineKeyDamageData.Id(night,"loadout.f"),NineKeyDamageData.Id(night,"f1"),true),"equip cooperative F before support auxiliary");
        Check(state.ChangeAuxiliary("auxiliary.1",NineKeyDamageData.Support,true),"equip support auxiliary with compatible F");
        var actor=new ActorFixture{_state=state,kit=new NineKeyDamageKit{runtime=state}};
        actor.ResetEncounter(CharacterReset.NewRun);
        Check(state.HasAuxiliary(NineKeyDamageData.Support),"coupled support auxiliary survives new run");
        Check(state.Selected(CharacterSlot.OffGcd2).id==NineKeyDamageData.Id(night,"f1"),"new run never substitutes resource F for coupled support F");
        Check(state.Definition.buildCompatibility(state.Snapshot)=="","preserved support build remains compatible");

        var tempest=CharacterRosterData.Jobs.Single(j=>j.index==7);
        state=new CharacterRuntimeState(NineKeyDamageData.Build(tempest));
        state.ChooseRoute(state.Definition.routes[0].id,true);
        string core=NineKeyDamageData.Id(tempest,"core30.f2");
        Check(!state.ChangeLoadout(NineKeyDamageData.Id(tempest,"loadout.core1"),core,true),"F seed core cannot use default resource F");
        Check(state.ChangeLoadout(NineKeyDamageData.Id(tempest,"loadout.f"),NineKeyDamageData.Id(tempest,"f2"),true),"equip F seed before seed-specific core");
        Check(state.ChangeLoadout(NineKeyDamageData.Id(tempest,"loadout.core1"),core,true),"cross-route F seed core selectable with matching F");
        actor=new ActorFixture{_state=state,kit=new NineKeyDamageKit{runtime=state}};
        actor.ResetEncounter(CharacterReset.NewRun);
        Check(state.Snapshot.coreTrait==core&&actor.kit.observedCore==core,"F-specific core survives new run and is seen by DPS reset");
        Check(state.Selected(CharacterSlot.OffGcd2).id==NineKeyDamageData.Id(tempest,"f2"),"F seed retained together with its core");
        Check(state.Definition.buildCompatibility(state.Snapshot)=="","preserved cross-route Tempest build remains compatible");
    }
}
