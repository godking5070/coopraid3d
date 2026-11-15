using System;
using System.Collections.Generic;
using System.Linq;
using RaidRogue;

sealed class Transform
{
    public CharacterActor actor;public PartyBotAI bot;public NetworkRaidAvatar avatar;public bool alive=true;
    public PartyLife life=new PartyLife();public Vector3 position,forward=new Vector3{z=1};
    public T GetComponent<T>() where T:class =>typeof(T)==typeof(CharacterActor)?actor as T:typeof(T)==typeof(PartyBotAI)?bot as T:typeof(T)==typeof(PartyLife)?life as T:avatar as T;
}
struct Vector3
{
    public float x,y,z;
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3{x=a.x+b.x,y=a.y+b.y,z=a.z+b.z};
    public static Vector3 operator *(Vector3 a,float b)=>new Vector3{x=a.x*b,y=a.y*b,z=a.z*b};
}
sealed class PartyLife { public bool IsTank,IsHealer; }
static class CombatRange { public static bool LivingPartyMember(Transform actor)=>actor!=null&&actor.alive; }
sealed class CharacterActor
{
    public CharacterDefinition Definition;public CharacterStateSnapshot Snapshot;
    public CharacterSkillDefinition Selected(CharacterSlot slot)
    {var selected=Snapshot.selections.FirstOrDefault(s=>s.slot==slot);return selected==null?null:Definition.Skill(selected.skillId);}
}
sealed class PartyBotAI { public int role; }
sealed class NetworkRaidAvatar { public int Role;public bool Automated; }
static class NineKeyDamageKit
{
    public static bool Supports(CharacterDefinition d)=>CharacterSlots.IsNineKey(d)&&NineKeyDamageData.Supports(CharacterRosterData.Find(d?.id));
}
static class PartyAiTactics
{
    /* ACTUAL_COUNTER_POSITION_POLICY */
}
sealed class GuardianFixture
{
    public readonly List<Transform> party=new List<Transform>();
    List<Transform> GetLivingPartyMemberTransforms()=>party.Where(t=>t.alive).ToList();
    /* ACTUAL_GUARDIAN_COUNTER_SELECTION */
}
enum JanusStep { Posts, CounterPrepare, CounterWindow, Cast }
sealed class JanusFixture
{
    public bool JanusEncounter=true;public JanusStep _janusStep=JanusStep.CounterPrepare;
    public Transform transform=new Transform(),_janusAlphaCounterActor,_janusOmegaCounterActor;
    public int _janusAlphaCounter=-1;public Omega _janusOmega=new Omega();
    public sealed class Omega { public Transform transform=new Transform{position=new Vector3{x=10}}; }
    public bool Destination(Transform actor,int role,out Vector3 destination)=>TryJanusCounterDestination(actor,role,out destination);
    /* ACTUAL_JANUS_COUNTER_PATH */
}
static class DamageCounterPositionTests
{
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static Transform Member(int index,int route,int choice,int role,bool automated=true)
    {
        var job=CharacterRosterData.Jobs.Single(j=>j.index==index);
        var state=new CharacterRuntimeState(NineKeyDamageData.Build(job));state.ChooseRoute(state.Definition.routes[route-1].id,true);
        Check(state.ChangeLoadout(NineKeyDamageData.Id(job,"loadout.c"),NineKeyDamageData.Id(job,"c"+choice),true),"counter-role build choice accepted");
        return new Transform{actor=new CharacterActor{Definition=state.Definition,Snapshot=state.Snapshot},avatar=new NetworkRaidAvatar{Role=role,Automated=automated}};
    }
    static void Main()
    {
        foreach(int index in Enumerable.Range(6,6))for(int route=1;route<=3;route++)for(int choice=0;choice<3;choice++)
        {
            var member=Member(index,route,choice,index<8?3:4);
            Check(PartyAiTactics.CanTakeCounterPosition(member)==(choice==0),"only equipped counter uses dedicated position across all DPS routes");
            member.actor.Snapshot.unlocked=Array.Empty<string>();
            Check(!PartyAiTactics.CanTakeCounterPosition(member),"locked role skill cannot reserve counter positioning");
        }
        Check(!PartyAiTactics.CanTakeCounterPosition(null),"missing actor cannot be counter candidate");
        var legacy=new Transform{bot=new PartyBotAI{role=2}};
        Check(PartyAiTactics.CanTakeCounterPosition(legacy),"actor-less legacy bot keeps existing counter policy");
        foreach(var job in CharacterRosterData.Jobs.Where(j=>j.index<6))
        {
            var definition=CharacterRosterData.Definition(job);definition.controlLayout=CharacterControlLayout.NineKey;
            var member=new Transform{actor=new CharacterActor{Definition=definition,Snapshot=new CharacterStateSnapshot{unlocked=Array.Empty<string>()}}};
            Check(PartyAiTactics.CanTakeCounterPosition(member),"tank/healer policy is not reinterpreted by optional DPS counter helper");
        }
        var unavailable=Member(6,1,0,3);unavailable.actor.Selected(CharacterSlot.Counter).implemented=false;
        Check(!PartyAiTactics.CanTakeCounterPosition(unavailable),"unimplemented selected counter cannot reserve position");
        var guardian=new GuardianFixture();
        var melee=Member(6,1,1,3);var ranged=Member(10,1,2,4);
        var tank=new Transform{bot=new PartyBotAI{role=0}};var healer=new Transform{bot=new PartyBotAI{role=1}};
        guardian.party.AddRange(new[]{melee,ranged,tank,healer});
        Check(guardian.GuardianCounterRank(melee)==-1&&guardian.GuardianCounterRank(ranged)==-1,"noncounter DPS no longer occupy top-two Guardian candidates");
        Check(guardian.GuardianCounterRank(tank)==0&&guardian.GuardianCounterRank(healer)==1,"tank and healer retain backup slots when DPS opt out");
        var equipped=Member(7,1,0,3);guardian.party[0]=equipped;
        Check(guardian.GuardianCounterRank(equipped)==0&&guardian.GuardianCounterRank(tank)==1&&guardian.GuardianCounterRank(healer)==-1,"equipped DPS preserves original role ordering");
        equipped.actor.Snapshot.unlocked=Array.Empty<string>();
        Check(guardian.GuardianCounterRank(equipped)==-1&&guardian.GuardianCounterRank(healer)==1,"locked DPS counter does not displace healer backup");
        var human=Member(8,1,0,3,false);guardian.party[0]=human;
        Check(guardian.GuardianCounterRank(human)==-1&&guardian.GuardianCounterRank(tank)==0,"human still excluded from automatic Guardian priority");
        guardian.party[0]=legacy;
        Check(guardian.GuardianCounterRank(legacy)==0&&guardian.GuardianCounterRank(tank)==1,"legacy damage bot retains existing priority");
        legacy.alive=false;
        Check(guardian.GuardianCounterRank(legacy)==-1&&guardian.GuardianCounterRank(healer)==1,"dead candidates never occupy backup slots");
        var janus=new JanusFixture();var noCounter=Member(11,1,2,4);
        Check(!janus.Destination(noCounter,4,out _),"Janus preparation does not send protection DPS into counter front");
        janus._janusStep=JanusStep.CounterWindow;
        Check(janus.JanusAiCounterTarget(noCounter)==null&&!janus.Destination(noCounter,4,out _),"Janus open counter window excludes noncounter DPS from both target and position");
        var alpha=Member(6,1,0,3);var omega=Member(10,1,0,4);
        Check(janus.JanusAiCounterTarget(alpha)==janus.transform&&janus.JanusAiCounterTarget(omega)==janus._janusOmega.transform,"equipped melee/ranged preserve Alpha/Omega preference");
        Check(janus.Destination(omega,4,out var point)&&point.x==10&&point.z==2.2f,"open-window position preserves selected boss and approach distance");
        janus._janusStep=JanusStep.CounterPrepare;
        Check(janus.Destination(omega,4,out point)&&point.x==10&&point.z==3.8f,"equipped preparation preserves original Omega staging point");
        omega.actor.Snapshot.unlocked=Array.Empty<string>();
        Check(!janus.Destination(omega,4,out _),"locked counter cannot use preparation fallback");
        janus._janusStep=JanusStep.CounterWindow;janus._janusAlphaCounterActor=alpha;janus._janusAlphaCounter=1;
        Check(janus.JanusAiCounterTarget(alpha)==null,"already successful player cannot take second counter");
        Check(janus.JanusAiCounterTarget(tank)==janus._janusOmega.transform&&janus.JanusAiCounterTarget(healer)==janus._janusOmega.transform,"legacy tank/healer backups retain remaining-boss routing");
        janus._janusOmegaCounterActor=healer;
        Check(janus.JanusAiCounterTarget(tank)==null,"both completed counters leave no target");
        janus._janusStep=JanusStep.Posts;
        Check(!janus.Destination(tank,1,out _),"counter helper does not take over assigned post positioning");
        Console.WriteLine("PASS actual DPS counter positioning and Guardian priority: "+checks+"; scene movement remains unverified.");
    }
}
