using System;
using System.Linq;
using RaidRogue;

sealed class DepartureNoop { public void Reset(){} public void ClearResolvedCombatHits(){} }
sealed class NineKeyTankKit { public void Clear(){} }
sealed class NineKeyHealerKit { public void Clear(){} }
sealed class AegisPaladin { public void ClearTransient(){} public void RecalculateStats(){} }
sealed class CharacterKit { public void Clear(){} public void Recalculate(){} }
sealed class CombatStatusEffects { public void Clear(){} }
sealed class NineKeyDamageKit { public static bool Supports(CharacterDefinition d)=>CharacterSlots.IsNineKey(d)&&NineKeyDamageData.Supports(CharacterRosterData.Find(d?.id)); public void Clear(){} }
static class RaidCombatAuthority { public static bool Replica; }
sealed class StageManager { public static StageManager Instance=new StageManager(); public string RunId=Guid.NewGuid().ToString("N"),ChapterId=CampaignCatalog.PartOne; }
sealed class DepartureActorFixture
{
    public CharacterRuntimeState _state;
    public CharacterDefinition Definition=>_state.Definition;
    DepartureNoop _actionExecution=new DepartureNoop(),_player=new DepartureNoop(),_bot=new DepartureNoop(); object _executingSkill;
    void BindLegacy(){} T GetComponent<T>() where T:class=>null;
    /* ACTUAL_RESET_ENCOUNTER */
}
static class DepartureBuildTests
{
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static string Build(CharacterStateSnapshot s)=>s.routeId+"/"+s.coreTrait+"/"+string.Join(",",s.auxiliaryTraits)+"/"+string.Join(",",s.selections.Where(x=>x.slot!=CharacterSlot.Ultimate).Select(x=>x.slot+"="+x.skillId));
    static void Main()
    {
        try { Run(); }
        catch(Exception error) { Console.Error.WriteLine(error.GetType().FullName+": "+error.Message); if(error.InnerException!=null)Console.Error.WriteLine(error.InnerException.Message); Environment.ExitCode=1; }
    }
    static void Run()
    {
        var definitions=new[]{AegisPaladinData.BuildNineKey()}.Concat(CharacterRosterData.Jobs.Where(j=>j.index<=5).Select(j=>NineKeyTankData.Supports(j)?NineKeyTankData.Build(j):NineKeyHealerData.Build(j))).ToArray();
        Check(definitions.Length==6,"all three tanks and three healers");
        foreach(var definition in definitions)
        for(int route=0;route<definition.routes.Length;route++)
        for(int variant=0;variant<2;variant++)
        {
            var state=new CharacterRuntimeState(definition);
            Check(state.ChooseRoute(definition.routes[route].id,true),"departure origin");
            foreach(var rule in definition.loadouts.Where(r=>string.IsNullOrEmpty(r.routeId)||r.routeId==state.RouteId))
                Check(state.ChangeLoadout(rule.id,rule.choices[variant==0?0:rule.choices.Length-1],true),"choose build "+definition.id+"/"+rule.id);
            if(variant==1)
            {
                var alternate=definition.auxiliaryTraits.Reverse().Take(2).ToArray();
                Check(state.ChangeAuxiliary("auxiliary.1",alternate[0].id,true),"nondefault first auxiliary");
                Check(state.ChangeAuxiliary("auxiliary.2",alternate[1].id,true),"nondefault second auxiliary");
            }
            string chosen=Build(state.Snapshot);
            state.BeginRun(Guid.NewGuid().ToString("N"),CampaignCatalog.PartOne);
            state.ClearedStage(3);
            foreach(var reward in state.PendingChoices())Check(state.ChooseSkill(reward.id,reward.choices[0],state.RewardKey(reward)),"prior run reward");
            Check(state.Unlocked(CharacterSlot.Ultimate),"prior run V was unlocked");
            state.SetBuildEffect("old.run.card",1,1);
            foreach(var resource in definition.resources)state.Gain(resource.id,state.Maximum(resource.id));
            var actor=new DepartureActorFixture{_state=state};
            foreach(var reset in new[]{CharacterReset.NewRun,CharacterReset.Retry,CharacterReset.NextStage})
            {
                actor.ResetEncounter(reset);
                var snapshot=state.Snapshot;
                Check(Build(snapshot)==chosen,"selected build survives "+reset+" for "+definition.id+" route="+route+" variant="+variant);
                Check(!state.Unlocked(CharacterSlot.Ultimate),"prior-run V entitlement cleared");
                Check(snapshot.effects.Length==0&&snapshot.chosenRules.Length==0&&snapshot.clearedEncounters.Length==0,"prior progress and card effects cleared");
                Check(snapshot.runId==StageManager.Instance.RunId&&snapshot.chapterId==CampaignCatalog.PartOne,"new run identity");
                Check(snapshot.gcd==0&&snapshot.castSkillId==""&&snapshot.combo1==1&&snapshot.combo2==1,"transient state reset");
                Check(snapshot.skills.All(s=>s.recharge==0&&s.charges==state.MaximumCharges(s.id)),"cooldowns and selected Q charge capacity reset");
            }
        }
        Console.WriteLine("PASS: "+checks+" tank/healer departure build checks using the production actor reset body and real definitions/runtime. Component effects, engine input and live replication remain separate.");
    }
}
