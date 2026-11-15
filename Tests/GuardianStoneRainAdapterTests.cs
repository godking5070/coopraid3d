using System;
using System.Collections.Generic;
using RaidRogue;

namespace RaidRogue
{
    // Engine dependencies only. The runner inserts the real boss emission, tick and cleanup methods.
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 forward=>new Vector3(0,0,1);
        public static Vector3 positiveInfinity=>new Vector3(float.PositiveInfinity,0,float.PositiveInfinity);
    }
    public class GameObject {public bool active=true;public void SetActive(bool active){this.active=active;}}
    public class Transform {public Vector3 position;}
    public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
    public static class RaidCombatAuthority {public static bool Replica;}
    public static class BattleResurrection
    {public static float FlatDistance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.z-b.z)*(a.z-b.z));}
    public enum CombatDamageKind {Area}
    public enum AoETelegraphType {Circle_Radius6}
    public class AoETelegraph
    {
        public Transform transform=new Transform();public GameObject gameObject=new GameObject();
        public static AoETelegraph Spawn(AoETelegraphType type,Vector3 point,Vector3 direction,float delay,float inner,float radius)
            =>new AoETelegraph{transform=new Transform{position=point}};
        public void SetGuideStyle(bool locked){}
    }
    public class BossController
    {
        class GuardianStoneDrop {public AoETelegraph ground,warning;public float impactAt;}
        readonly List<GuardianStoneDrop> _stoneDrops=new List<GuardianStoneDrop>();
        readonly List<AoETelegraph> _hardStoneGround=new List<AoETelegraph>(),_hardStoneImpacts=new List<AoETelegraph>();
        readonly Dictionary<Transform,Vector3> _hardStoneEscapeDestinations=new Dictionary<Transform,Vector3>();
        readonly GuardianStoneRainSchedule _stoneRain=new GuardianStoneRainSchedule();
        readonly int[] _guardianHits=new int[20];
        int _hardStoneStage,_stoneRainSerial;float _hardStoneStarted;
        public bool GuardianHard;public GuardianHardSettings guardianHard=new GuardianHardSettings();
        public GuardianNormalSettings Tuning=new GuardianNormalSettings();
        public readonly List<Transform> members=new List<Transform>();
        public Action onDamage;public int damageCount;
        bool HardStoneRainActive=>_hardStoneStage!=0;
        List<Transform> GetLivingPartyMemberTransforms()=>members;
        void DamagePartyMember(Transform member,float damage,CombatDamageKind kind){damageCount++;onDamage?.Invoke();}
        void PlayGuardianLockCue(){} void PlayHardImpactCue(){} void Destroy(GameObject obj){obj.active=false;}
        /* ACTUAL_STONE_METHODS */
        public void Begin(float now)=>StartHardStoneRain(now);
        public void Tick(float now)=>TickHardStoneRain(now);
        public void Cancel()=>ClearHardStoneRain();
        public int Pending=>_stoneDrops.Count;
        public bool Active=>HardStoneRainActive;
        public Vector3 FirstPoint=>_stoneDrops[0].ground.transform.position;
        public bool AllVisible {get{foreach(var drop in _stoneDrops)if(!drop.ground.gameObject.active)return false;return true;}}
    }
}

static class GuardianStoneRainAdapterTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static BossController Make(bool hard=false)
    {
        var boss=new BossController{GuardianHard=hard};boss.Tuning.EnsureTempoDefaults();boss.guardianHard.EnsureDefaults();
        if(hard)boss.Tuning=boss.guardianHard.combat;
        for(int i=0;i<4;i++)boss.members.Add(new Transform{position=new Vector3(i*10,0,0)});
        return boss;
    }
    static void Main()
    {
        foreach(bool hard in new[]{false,true})
        {
            var boss=Make(hard);float interval=hard?boss.guardianHard.stoneWaveGapSeconds:boss.Tuning.stoneWaveInterval;
            boss.Begin(0);Check(boss.Pending==4,"wave snapshots every living member");
            var initial=boss.FirstPoint;
            foreach(var member in boss.members)member.position=new Vector3(member.position.x,0,8);
            boss.Tick(interval);Check(boss.Pending==8,"new wave exists before prior wave impacts");
            Check(boss.FirstPoint.x==initial.x&&boss.FirstPoint.z==initial.z,"locked drops do not follow players");
            Check(boss.AllVisible,"earlier circles survive new waves");
            float lead=hard?boss.guardianHard.stoneLockedSeconds:boss.Tuning.LockedDuration(GuardianPattern.FallingStone);
            boss.Tick(lead+.01f);Check(boss.damageCount==0&&boss.Pending==4,"first impact resolves independently at old position");
            boss.Cancel();boss.Tick(100);Check(!boss.Active&&boss.Pending==0&&boss.damageCount==0,"cancel clears future impacts");
            boss.Begin(101);Check(boss.Pending==4,"retry emits a fresh first wave");boss.Cancel();
        }
        var wipe=Make();wipe.Begin(0);wipe.Tick(.75f);wipe.onDamage=wipe.Cancel;
        wipe.Tick(1.11f);Check(wipe.damageCount==1&&wipe.Pending==0&&!wipe.Active,"synchronous wipe during impact cannot hit twice or access cleared list");
        var replica=Make();RaidCombatAuthority.Replica=true;replica.Begin(0);replica.Tick(100);
        Check(replica.damageCount==0&&replica.Pending==0,"replica never emits or deals rain damage");RaidCombatAuthority.Replica=false;
        Console.WriteLine("PASS: "+checks+" actual guardian stone-rain method checks (engine dependencies stubbed).");
    }
}
