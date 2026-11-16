using System;
using System.Collections.Generic;
namespace RaidRogue
{
    // Executes extracted production members. Unity component lookup, scene lifetime,
    // hit callbacks and transport containers are seams, not a second game simulator.
    enum RaidSessionMode { Menu,Single,Lan }
    enum RaidBattlePhase { Lobby,Loading,Combat,Reward }
    enum AddMinionType { Other,JanusOmega }
    enum CombatDamageKind { Unclassified,Direct,Extra }
    sealed class GameObject { public bool activeInHierarchy=true; }
    sealed class Transform
    {
        public readonly GameObject gameObject=new GameObject();public bool Alive=true;
        public readonly Dictionary<Type,object> Components=new Dictionary<Type,object>();
        public T GetComponent<T>() where T:class=>Components.TryGetValue(typeof(T),out var value)?value as T:null;
    }
    static class Mathf {public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);}
    static class RaidCombatAuthority {public static bool Replica;}
    sealed class Progress {public RaidBattlePhase Phase=RaidBattlePhase.Lobby;}
    sealed class StageManager
    {public static StageManager Instance;public static bool CombatSuspended=true;public Progress Progress=new Progress();public BossController boss;}
    sealed class BossController
    {
        public Transform transform=new Transform();public float currentHp=1000,maxHp=1000;public bool IsBossDefeatedCollapse;
        public float ReceiveJanusOmegaDamage(float amount,Transform source,string origin,CombatDamageKind kind,bool scaled){currentHp-=amount;return amount;}
    }
    sealed class NetworkRaidAvatar {public int Id;public Transform transform=new Transform();}
    sealed class RaidNetworkSession
    {
        public static bool Multiplayer;public bool Ending,IsHost=true;public RaidSessionMode Mode=RaidSessionMode.Single;public RaidBattlePhase BattlePhase=RaidBattlePhase.Lobby;
        public Dictionary<int,BossAddMinion> _combatAdds=new Dictionary<int,BossAddMinion>();
        public Dictionary<int,NetworkRaidAvatar> _avatars=new Dictionary<int,NetworkRaidAvatar>();private int _nextEnemyId=-2;
        /* COMBAT_ID */
        /* COMBAT_TARGET */
    }
    sealed class RaidLobbyRoom
    {
        public static RaidLobbyRoom Instance;public bool _showing=true;public RaidNetworkSession _session;public StageManager _stage;public BossAddMinion _dummy;
        public const int DummyCombatId=-900000;public static BossAddMinion Dummy=>Instance?._dummy;
        /* ROOM_ACTIVE */
        /* ROOM_PRACTICE */
    }
    static class CombatRange
    {
        /* LIVING_ENEMY */
        public static bool LivingPartyMember(Transform unit)=>unit!=null&&unit.Alive&&unit.gameObject.activeInHierarchy;
    }
    static class AiSpectatorDamage {public static float AtHp(Transform source,float amount,bool scaled)=>amount;}
    static class CombatMetricsManager {public static int Calls;public static void Damage(Transform source,float before,float after,bool add,string origin,CombatDamageKind kind){Calls++;}}
    static class CharacterKit
    {public static int Calls;public static float Actual;public static void AfterAttack(Transform source,Transform target,float amount,string origin,CombatDamageKind kind){Calls++;Actual=amount;}}
    sealed class BossAddMinion
    {
        public bool IsLobbyDummy,isDead;public float currentHp=1000,maxHp=1000;public AddMinionType addType;public BossController ownerBoss;
        public Transform transform=new Transform();public GameObject gameObject=>transform.gameObject;
        public float Hit(float amount,Transform source)=>ApplyAddHpDamage(amount,source,"test",CombatDamageKind.Direct);
        /* APPLY_DAMAGE */
    }
    static class LobbyPracticeTests
    {
        static int checks;static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
        static BossAddMinion Add(bool dummy)
        {var add=new BossAddMinion{IsLobbyDummy=dummy};add.transform.Components[typeof(BossAddMinion)]=add;return add;}
        static void Main()
        {
            Check(!RaidLobbyRoom.PracticeActive,"no room cannot enable practice");
            var session=new RaidNetworkSession();var stage=new StageManager();StageManager.Instance=stage;
            var dummy=Add(true);var room=new RaidLobbyRoom{_session=session,_stage=stage,_dummy=dummy};RaidLobbyRoom.Instance=room;
            Check(RaidLobbyRoom.PracticeActive,"single lobby allows practice");
            room._showing=false;Check(!RaidLobbyRoom.PracticeActive,"hidden room disables practice");room._showing=true;
            session.Mode=RaidSessionMode.Menu;Check(!RaidLobbyRoom.PracticeActive,"menu backdrop cannot enable skills");session.Mode=RaidSessionMode.Single;
            session.Ending=true;Check(!RaidLobbyRoom.PracticeActive,"ending session disables practice");session.Ending=false;
            room._dummy=null;Check(!RaidLobbyRoom.PracticeActive,"uninitialized dummy blocks practice");room._dummy=dummy;
            dummy.gameObject.activeInHierarchy=false;Check(!RaidLobbyRoom.PracticeActive,"inactive dummy blocks practice");dummy.gameObject.activeInHierarchy=true;
            foreach(var phase in new[]{RaidBattlePhase.Loading,RaidBattlePhase.Combat,RaidBattlePhase.Reward})
            {stage.Progress.Phase=phase;Check(!RaidLobbyRoom.PracticeActive,"single non-lobby cannot practice: "+phase);}
            stage.Progress.Phase=RaidBattlePhase.Lobby;
            RaidNetworkSession.Multiplayer=true;session.Mode=RaidSessionMode.Lan;
            Check(RaidLobbyRoom.PracticeActive,"network lobby allows practice");
            session.BattlePhase=RaidBattlePhase.Loading;Check(!RaidLobbyRoom.PracticeActive,"network departure closes practice despite stale local progress");
            session.BattlePhase=RaidBattlePhase.Lobby;RaidNetworkSession.Multiplayer=false;session.Mode=RaidSessionMode.Single;
            var boss=new BossController();boss.transform.Components[typeof(BossController)]=boss;stage.boss=boss;
            var ordinary=Add(false);
            Check(CombatRange.LivingEnemy(dummy.transform),"practice dummy is targetable");
            Check(!CombatRange.LivingEnemy(boss.transform),"hidden encounter boss is not an eligible lobby enemy");
            Check(!CombatRange.LivingEnemy(ordinary.transform),"ordinary encounter adds are not eligible lobby enemies");
            Check(!CombatRange.LivingEnemy(null),"null target rejected");
            var source=new Transform();float actual=dummy.Hit(125,source);
            Check(actual==125&&dummy.currentHp==875,"practice applies real hit amount");
            Check(CharacterKit.Calls==1&&CharacterKit.Actual==125,"confirmed hit callback retained for resource and synergy");
            Check(CombatMetricsManager.Calls==0,"practice hit excluded from encounter damage ledger");
            actual=dummy.Hit(float.MaxValue,source);Check(actual==874&&dummy.currentHp==1&&!dummy.isDead,"lethal practice hit leaves one HP without death");
            actual=dummy.Hit(20,source);Check(actual==0&&dummy.currentHp==1,"repeated hits at floor cannot manufacture effective damage");
            int calls=CharacterKit.Calls;
            foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {Check(dummy.Hit(invalid,source)==0&&dummy.currentHp==1&&CharacterKit.Calls==calls,"invalid damage cannot mutate dummy or fire callback");}
            dummy.currentHp=1000;source.Alive=false;Check(dummy.Hit(10,source)==0&&dummy.currentHp==1000,"dead actor cannot hit dummy");source.Alive=true;
            RaidCombatAuthority.Replica=true;Check(dummy.Hit(10,source)==0&&dummy.currentHp==1000,"replica cannot author damage");RaidCombatAuthority.Replica=false;
            session.Ending=true;Check(dummy.Hit(10,source)==0&&dummy.currentHp==1000,"session end blocks stale hit");session.Ending=false;
            Check(ordinary.Hit(10,source)==0&&ordinary.currentHp==1000,"lobby exception does not damage ordinary adds");
            int id=session.CombatId(dummy.transform);Check(id==RaidLobbyRoom.DummyCombatId&&session._combatAdds.Count==0,"dummy ID never registers a normal network add");
            Check(session.CombatTarget(id)==dummy.transform,"active practice resolves reserved target");
            room._showing=false;Check(session.CombatTarget(id)==null,"departure rejects stale reserved target");
            Check(!CombatRange.LivingEnemy(dummy.transform),"departed dummy is not an enemy");
            Check(CombatRange.LivingEnemy(boss.transform)&&CombatRange.LivingEnemy(ordinary.transform),"outside lobby normal boss and add targeting remain intact");
            StageManager.CombatSuspended=false;Check(ordinary.Hit(25,source)==25&&CombatMetricsManager.Calls==1,"ordinary combat damage still records metrics");
            room._showing=true;var replacement=Add(true);room._dummy=replacement;
            Check(session.CombatTarget(id)==replacement.transform,"new room dummy resolves without stale object registration");
            room._dummy=null;Check(session.CombatTarget(id)==null,"room teardown clears reserved resolution");
            Console.WriteLine("PASS actual lobby practice adapters: "+checks+". Unity lifecycle, visual hit feedback and live transport remain unverified.");
        }
    }
}
