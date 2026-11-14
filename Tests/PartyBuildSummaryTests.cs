using System;
using System.Linq;
using RaidRogue;
internal static class PartyBuildSummaryTests
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    private static CharacterDefinition Definition(RosterJob job)=>NineKeyTankData.Supports(job)?NineKeyTankData.Build(job):
        NineKeyHealerData.Supports(job)?NineKeyHealerData.Build(job):NineKeyDamageData.Build(job);
    public static int Main()
    {
        try
        {
            var definitions=new[]{AegisPaladinData.BuildNineKey()}.Concat(CharacterRosterData.Jobs.Select(Definition)).ToArray();
            foreach(var definition in definitions)
            {
                var state=new CharacterRuntimeState(definition);
                var pending=RaidPartyBuildSummary.Read(definition,state.Snapshot);
                Check(pending.Route=="미선택","unselected route is never fabricated: "+definition.id);
                foreach(var route in definition.routes)
                {
                    Check(state.ChooseRoute(route.id,true),"route fixture");
                    var snapshot=state.Snapshot;var original=snapshot.Copy();
                    var summary=RaidPartyBuildSummary.Read(definition,snapshot);
                    Check(summary.Job==definition.name&&summary.Route==route.name,"shows actual job and selected route");
                    Check(summary.Role==(definition.role==CharacterRole.Tank?"탱커":definition.role==CharacterRole.Healer?"힐러":"딜러"),"role comes from definition, never party slot");
                    Check(summary.CounterEquipped&&summary.Counter=="카운터 장착","default counter capability marked for every current job");
                    var cooling=snapshot.Copy();var counterId=cooling.selections.First(s=>s.slot==CharacterSlot.Counter).skillId;
                    var timing=cooling.skills.First(s=>s.id==counterId);timing.charges=0;timing.recharge=timing.rechargeTotal=10;
                    Check(RaidPartyBuildSummary.Read(definition,cooling).CounterEquipped,"equipped counter badge is independent of practice cooldown");
                    Check(summary.Combo.StartsWith(state.Selected(CharacterSlot.Gcd2).name)&&summary.Utility.StartsWith(state.Selected(CharacterSlot.OffGcd1).name),"representative skills match selected loadout");
                    Check(summary.Identity.StartsWith(state.Selected(CharacterSlot.Identity).name)&&summary.Tactical.StartsWith(state.Selected(CharacterSlot.OffGcd2).name),"identity and tactical skills match selected route and loadout");
                    Check(snapshot.unlocked.SequenceEqual(original.unlocked)&&snapshot.selections.Zip(original.selections,(a,b)=>a.slot==b.slot&&a.skillId==b.skillId).All(v=>v)&&snapshot.coreTrait==original.coreTrait,"summary does not mutate authoritative snapshot");
                    var restored=new CharacterRuntimeState(definition);
                    Check(restored.Restore(snapshot),"host-restored snapshot fixture");
                    var remote=RaidPartyBuildSummary.Read(definition,restored.Snapshot);
                    Check(remote.Route==summary.Route&&remote.Combo==summary.Combo&&remote.CounterEquipped==summary.CounterEquipped,"restored client build produces matching summary");
                }
                var counterRule=definition.loadouts.FirstOrDefault(rule=>rule.slot==CharacterSlot.Counter);
                if(counterRule!=null)
                    foreach(var choice in counterRule.choices)
                    {
                        Check(state.ChangeLoadout(counterRule.id,choice,true),"role skill swap fixture");
                        var skill=definition.Skill(choice);var summary=RaidPartyBuildSummary.Read(definition,state.Snapshot);
                        Check(summary.CounterEquipped==skill.isCounter,"equipped counter differs from stagger/protection in same slot");
                        Check(summary.RoleSkill.StartsWith(skill.name),"role skill label changes with equipment");
                        if(!skill.isCounter)Check(summary.Counter=="카운터 미장착","non-counter in slot 4 is explicitly not equipped");
                    }
                var comboRule=definition.loadouts.FirstOrDefault(rule=>rule.slot==CharacterSlot.Gcd2);
                if(comboRule!=null&&comboRule.choices.Length>1)
                {
                    Check(state.ChangeLoadout(comboRule.id,comboRule.choices[1],true),"combo swap fixture");
                    Check(RaidPartyBuildSummary.Read(definition,state.Snapshot).Combo.StartsWith(definition.Skill(comboRule.choices[1]).name),"party summary tracks representative combo changes");
                }
                var locked=state.Snapshot;
                var counterSelection=locked.selections.First(s=>s.slot==CharacterSlot.Counter);
                var defaultCounter=definition.skills.First(s=>s.implemented&&s.isCounter);
                counterSelection.skillId=defaultCounter.id;locked.unlocked=locked.unlocked.Where(id=>id!=defaultCounter.id).ToArray();
                var lockedSummary=RaidPartyBuildSummary.Read(definition,locked);
                Check(!lockedSummary.CounterEquipped&&lockedSummary.Counter=="카운터 잠김","locked counter does not count as usable equipment");
                var mismatched=state.Snapshot;mismatched.jobId="different.job";
                Check(!RaidPartyBuildSummary.Read(definition,mismatched).CounterEquipped&&RaidPartyBuildSummary.Read(definition,mismatched).Job=="동기화 대기","mismatched remote data is not guessed");
                Check(!RaidPartyBuildSummary.Read(definition,null).CounterEquipped,"missing peer snapshot is not marked equipped");
            }
            foreach(var slug in new[]{"sylvan-oracle","nightblade","spirit-ranger"})
                Check(definitions.Any(d=>d.id=="job."+slug),"selected test roster is covered: "+slug);
            Console.WriteLine("PASS: "+checks+" real-catalog party build, route, selected skill, counter capability and restored-state display checks.");
            Console.WriteLine("Boundary: no Unity layout, readiness transport or four-player live session is executed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
