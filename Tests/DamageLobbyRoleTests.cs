using System;
using System.Linq;
using RaidRogue;
sealed class DamageLobbyRoleTests
{
    /* ACTUAL_LOBBY_ROLE */
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            foreach(var job in CharacterRosterData.Jobs.Where(j=>j.index>=6))
            {
                var state=new CharacterRuntimeState(NineKeyDamageData.Build(job));
                for(int route=1;route<=3;route++)
                {
                    state.ChooseRoute(state.Definition.routes[route-1].id,true);
                    for(int role=0;role<3;role++)
                    {
                        Check(state.ChangeLoadout(NineKeyDamageData.Id(job,"loadout.c"),NineKeyDamageData.Id(job,"c"+role),true),"actual role loadout change is accepted");
                        string result=LobbyFourth(state.Definition,state.Snapshot,out bool counter);
                        Check(counter==(role==0)&&result==(role==0?"4 카운터":role==1?"4 무력화":"4 동료 보호"),"confirmed slot four is classified for every DPS route");
                        var replica=new CharacterRuntimeState(state.Definition);Check(replica.Restore(state.Snapshot),"confirmed build restores as replica");
                        Check(LobbyFourth(replica.Definition,replica.Snapshot,out bool replicated)==result&&replicated==counter,"replica and host display the same role");
                    }
                }
            }
            foreach(var job in CharacterRosterData.Jobs.Where(j=>j.index>=1&&j.index<=5))
            {
                var definition=job.index<=2?NineKeyTankData.Build(job):NineKeyHealerData.Build(job);
                var state=new CharacterRuntimeState(definition);
                Check(LobbyFourth(definition,state.Snapshot,out bool counter)=="4 카운터"&&counter,"tank and healer fixed counters are listed");
            }
            var paladin=new CharacterRuntimeState(AegisPaladinData.BuildNineKey());
            Check(LobbyFourth(paladin.Definition,paladin.Snapshot,out bool aegis)=="4 카운터"&&aegis,"paladin counter is listed");
            var legacy=new CharacterRuntimeState(CharacterCatalog.Find("test.blade"));
            Check(LobbyFourth(legacy.Definition,legacy.Snapshot,out bool oldCounter)=="4 카운터"&&oldCounter,"implemented legacy counter remains visible");
            var damage=new CharacterRuntimeState(NineKeyDamageData.Build(CharacterRosterData.Jobs.First(j=>j.index==6)));
            var snapshot=damage.Snapshot;
            snapshot.unlocked=Array.Empty<string>();
            Check(LobbyFourth(damage.Definition,snapshot,out bool locked)=="4 미해금"&&!locked,"locked counter is not counted as equipped capability");
            snapshot=damage.Snapshot;snapshot.selections=Array.Empty<JobSlotSelection>();
            Check(LobbyFourth(damage.Definition,snapshot,out bool absent)=="4 미선택"&&!absent,"missing selection is not assumed to be counter");
            Check(LobbyFourth(damage.Definition,null,out bool pending)=="4 정보 확인 중"&&!pending,"pending network state is explicitly unknown");
            snapshot=damage.Snapshot;snapshot.jobId="other";
            Check(LobbyFourth(damage.Definition,snapshot,out bool mismatched)=="4 정보 확인 중"&&!mismatched,"mismatched definition is not counted");
            Check(LobbyCounterSummary(Array.Empty<string>()).Contains("없음"),"no equipped counter gives advisory only");
            Check(LobbyCounterSummary(new[]{"탱커","딜러"}).Contains("탱커, 딜러"),"all confirmed counter holders remain visible");
            Console.WriteLine("PASS confirmed lobby role assignments: "+checks+"; native layout and live replication unverified.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
