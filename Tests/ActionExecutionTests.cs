using System;
using System.Collections.Generic;
using RaidRogue;

namespace RaidRogue
{
    // Only engine/scene dependencies are stubbed. Actor methods below are read
    // from production source by the runner on every invocation.
    public class Transform { public bool enemy,alive=true; }
    public enum SkillTargetRule { Self, LivingEnemy, LivingAlly }
    public enum RaidBattlePhase { Lobby, Combat }
    public sealed class CharacterSkillDefinition { public string handlerId; public int comboChain; public SkillTargetRule target; }
    public sealed class Resource { public string id="light"; public bool integer=true; }
    public sealed class Definition { public Resource[] resources={new Resource()}; }
    public sealed class Snapshot { public int combo1=3,combo2=3; }
    public sealed class State
    {
        public Snapshot Snapshot=new Snapshot(); public float value;
        public bool Gain(string id,float amount){value=Math.Min(100,value+amount);return true;}
    }
    public static class RaidCombatAuthority { public static bool Replica,Active; }
    public sealed class RaidNetworkSession { public static RaidNetworkSession Instance=new RaidNetworkSession();public bool CombatRunning; }
    public static class RaidLobbyRoom { public static bool PracticeActive; }
    public sealed class Progress { public RaidBattlePhase Phase=RaidBattlePhase.Combat; }
    public sealed class StageManager { public static StageManager Instance=new StageManager();public Progress Progress=new Progress(); }
    public static class CombatRange
    {
        public static bool LivingEnemy(Transform t)=>t!=null&&t.alive&&t.enemy;
        public static bool LivingPartyMember(Transform t)=>t!=null&&t.alive&&!t.enemy;
    }
    public sealed class CommonPotion { public void EndRevivalProtection(){} }
    public sealed class CharacterActor
    {
        internal State _state=new State();
        private CombatActionExecution _actionExecution;
        private CharacterSkillDefinition _executingSkill;
        private readonly Dictionary<string,Func<CharacterSkillDefinition,Transform,bool>> _handlers=new Dictionary<string,Func<CharacterSkillDefinition,Transform,bool>>();
        public Definition Definition=new Definition();
        private int GetInstanceID()=>-17;
        private T GetComponent<T>() where T:class=>null;
        public bool Invoke(CharacterSkillDefinition s,Transform t,Func<CharacterSkillDefinition,Transform,bool> effect)
        {_handlers[s.handlerId]=effect;return Apply(s,t);}
        /* ACTUAL_ACTION_METHODS */
    }
}

internal static class ActionExecutionTests
{
    private static int checks;
    private static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
    public static int Main()
    {
        try
        {
            var a=new CharacterActor();
            var attack=new CharacterSkillDefinition{handlerId="direct",comboChain=1,target=SkillTargetRule.LivingEnemy};
            var heal=new CharacterSkillDefinition{handlerId="heal",comboChain=2,target=SkillTargetRule.LivingAlly};
            var enemy=new Transform{enemy=true};var ally=new Transform();
            var policy=new CombatComboResourcePolicy("light",0,0,20,0,0,20);
            Check(!a.ResolveComboResource(attack,policy,true),"outside handler rejected");
            Check(a.Invoke(attack,enemy,(s,t)=>{
                t.alive=false; // lethal direct hit still counts: target was valid when approved.
                Check(a.ResolveComboResource(s,policy,true),"lethal hit rewards once");
                Check(!a.ResolveComboResource(s,policy,true),"extra hit cannot reward again");return true;
            })&&a._state.value==20,"actual actor bridge pays once");
            Check(!a.ResolveComboResource(attack,policy,true),"closed scope rejects delayed callback");
            enemy.alive=true;
            Check(a.Invoke(attack,enemy,(s,t)=>{Check(!a.ResolveComboResource(s,policy,false),"miss has no resource");return true;})&&a._state.value==20,"miss leaves gauge unchanged");
            Check(a.Invoke(heal,ally,(s,t)=>a.ResolveComboResource(s,policy,true))&&a._state.value==40,"direct overheal does not need effective healing amount");
            Check(a.Invoke(attack,enemy,(s,t)=>{Check(!a.ResolveComboResource(heal,policy,true),"another skill cannot borrow scope");return true;}),"skill identity guard");
            StageManager.Instance.Progress.Phase=RaidBattlePhase.Lobby;
            Check(a.Invoke(heal,ally,(s,t)=>{Check(!a.ResolveComboResource(s,policy,true),"lobby has no resource");return true;})&&a._state.value==40,"no precombat farming");
            RaidLobbyRoom.PracticeActive=true;
            Check(a.Invoke(attack,enemy,(s,t)=>a.ResolveComboResource(s,policy,true))&&a._state.value==60,"dummy practice permits its normal combo resource loop");
            RaidLobbyRoom.PracticeActive=false;
            Check(a.Invoke(heal,ally,(s,t)=>{Check(!a.ResolveComboResource(s,policy,true),"leaving dummy practice restores the lobby resource gate");return true;})&&a._state.value==60,"inactive dummy cannot farm precombat resources");
            StageManager.Instance.Progress.Phase=RaidBattlePhase.Combat;
            RaidCombatAuthority.Replica=true;
            Check(!a.Invoke(attack,enemy,(s,t)=>throw new Exception("replica handler executed")),"replica never executes effects");
            RaidCombatAuthority.Replica=false;RaidCombatAuthority.Active=true;
            Check(a.Invoke(heal,ally,(s,t)=>{Check(!a.ResolveComboResource(s,policy,true),"network lobby overrides local phase");return true;}),"host phase gate");
            RaidNetworkSession.Instance.CombatRunning=true;
            Check(a.Invoke(heal,ally,(s,t)=>a.ResolveComboResource(s,policy,true)),"host combat resource");
            RaidCombatAuthority.Active=false;
            try{a.Invoke(attack,enemy,(s,t)=>throw new InvalidOperationException());}catch(InvalidOperationException){}
            Check(!a.ResolveComboResource(attack,policy,true),"exception closes scope");
            Check(a.Invoke(heal,ally,(s,t)=>a.ResolveComboResource(s,policy,true)),"next action after exception works");
            var fractional=new CombatComboResourcePolicy("light",0,0,.5f,0,0,.5f);
            Check(a.Invoke(heal,ally,(s,t)=>{
                Check(!a.ResolveComboResource(s,fractional,true),"invalid integer policy rejected before claim");
                return a.ResolveComboResource(s,policy,true);
            }),"valid policy still usable after invalid policy");
            Check(a._state.value==100,"resource cap respected");
            var scoped=new CombatActionExecution(1);
            Check(scoped.Begin(CombatActionPurpose.Attack,1,3,true,true),"scope begins");
            Check(!scoped.Begin(CombatActionPurpose.Heal,2,3,true,true),"nested effect cannot replace action");
            scoped.Reset();Check(!scoped.TryResolveResource(policy,true,out _),"encounter reset invalidates active result");
            Check(scoped.Begin(CombatActionPurpose.Attack,1,3,true,true)&&scoped.TryResolveResource(policy,true,out float amount)&&amount==20,"new encounter action works");
            Console.WriteLine("PASS: actual actor action execution "+checks+" checks.");return 0;
        }
        catch(Exception e){Console.WriteLine("FAIL: "+e);return 1;}
    }
}
