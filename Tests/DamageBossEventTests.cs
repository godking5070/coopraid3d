using System;
namespace UnityEngine { public sealed class Transform {} }
namespace RaidRogue
{
    public enum DamageSealTrigger { CastEnd=1, PatternMoveEnd=2 }
    public static class RaidCombatAuthority { public static bool Replica; }
    public static class StageManager { public static bool CombatSuspended; }
    public static class NineKeyDamageKit
    {
        public static int Casts,Moves;
        public static void NotifyBossAction(UnityEngine.Transform target,DamageSealTrigger trigger)
        {if(trigger==DamageSealTrigger.CastEnd)Casts++;else Moves++;}
    }
    public partial class BossController
    {
        public float currentHp=100;
        private string _currentCastSkillName="";
        private float _castTotalDuration;
        private readonly UnityEngine.Transform transform=new UnityEngine.Transform();
        public void SetCast(string name,float seconds){_currentCastSkillName=name;_castTotalDuration=seconds;}
        public void FinishCast()=>NotifyDamageCastEnded();
        public void FinishMove()=>NotifyDamagePatternMoveEnded();
        /* ACTUAL_BOSS_ACTION_METHODS */
    }
}
static class DamageBossEventTests
{
    static int count;
    static void Check(bool value,string text){count++;if(!value)throw new Exception(text);}
    static void Main()
    {
        try
        {
            var boss=new RaidRogue.BossController();boss.FinishCast();Check(RaidRogue.NineKeyDamageKit.Casts==0,"no unnamed action reward");
            boss.SetCast("windup",0);boss.FinishCast();Check(RaidRogue.NineKeyDamageKit.Casts==0,"no zero-duration cast signal");
            boss.SetCast("windup",2);boss.FinishCast();Check(RaidRogue.NineKeyDamageKit.Casts==1,"semantic completion emits cast signal");
            RaidRogue.RaidCombatAuthority.Replica=true;boss.FinishCast();boss.FinishMove();Check(RaidRogue.NineKeyDamageKit.Casts==1&&RaidRogue.NineKeyDamageKit.Moves==0,"replica never emits gameplay events");
            RaidRogue.RaidCombatAuthority.Replica=false;RaidRogue.StageManager.CombatSuspended=true;boss.FinishCast();boss.FinishMove();Check(RaidRogue.NineKeyDamageKit.Casts==1&&RaidRogue.NineKeyDamageKit.Moves==0,"suspended retry/lobby suppresses success");
            RaidRogue.StageManager.CombatSuspended=false;boss.currentHp=0;boss.FinishCast();boss.FinishMove();Check(RaidRogue.NineKeyDamageKit.Casts==1&&RaidRogue.NineKeyDamageKit.Moves==0,"death suppresses cast and movement success");
            boss.currentHp=100;boss.SetCast("",0);boss.FinishMove();Check(RaidRogue.NineKeyDamageKit.Moves==1,"explicit pattern movement does not need a cast bar");
            Console.WriteLine("PASS actual boss DPS event gates: "+count+"; semantic caller paths are reviewed separately.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
