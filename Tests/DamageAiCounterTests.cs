using System;
using RaidRogue;
struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator -(Vector3 a)=>new Vector3(-a.x,-a.y,-a.z);
    public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    public static Vector3 up=>new Vector3(0,1,0);
    public float sqrMagnitude=>Dot(this,this);
    public Vector3 normalized{get{var value=this;value.Normalize();return value;}}
    public void Normalize(){float m=(float)Math.Sqrt(Dot(this,this));if(m>0){x/=m;y/=m;z/=m;}}
}
struct Quaternion
{
    public Vector3 forward;
    public static Quaternion LookRotation(Vector3 direction,Vector3 up)=>new Quaternion{forward=direction};
}
sealed class Transform
{
    public Vector3 position,forward=new Vector3(0,0,1);
    public Quaternion rotation{set{forward=value.forward;}}
    public bool alive=true;
    public object counter,boss,add;
    public T GetComponent<T>() where T:class=>(counter as T)??(boss as T)??(add as T);
}
static class CombatRange
{
    public static bool LivingEnemy(Transform t)=>t!=null&&t.alive;
    public static bool InRange(Transform a,Transform b,float r){var d=a.position-b.position;return Vector3.Dot(d,d)<=r*r;}
}
sealed class BossController
{
    public bool GuardianNormal,ready=true,GuardianClashCounterWindow,clashAllowed=true,CampanellaLockCounterWindow,lockAllowed=true,JanusEncounter,janusAllowed=true;
    public bool GuardianAiCounterReady(Transform t)=>ready;
    public bool GuardianClashCounterAllowed(Transform t)=>clashAllowed;
    public bool CampanellaLockCounterAllowed(Transform t)=>lockAllowed;
    public bool JanusCounterAllowed(Transform t,bool add)=>janusAllowed;
}
enum AddMinionType{None,JanusOmega}
sealed class BossAddMinion {public AddMinionType addType;public BossController ownerBoss;}
sealed class CombatCounterTarget
{
    public Transform transform;
    public bool open=true;
    public bool WindowOpen=>open&&CombatRange.LivingEnemy(transform);
    T GetComponent<T>() where T:class=>transform.GetComponent<T>();
    /* ACTUAL_COUNTER_ELIGIBILITY */
}
sealed class CharacterActor
{
    public Transform transform=new Transform();
    public PlayerController player;
    public T GetComponent<T>() where T:class=>player as T;
    public CharacterSkillDefinition role;
    public CharacterSkillDefinition Selected(CharacterSlot slot)=>role;
    public float EffectiveRange(CharacterSkillDefinition skill)=>skill.range;
}
sealed class PlayerController
{
    public Transform transform;
    public bool HasMovementIntent,IsMovingXZ,IsDashing,IsBeingRescued,IsBeingKnockedBack;
    /* ACTUAL_ATTACK_FACING */
}
sealed class NineKeyDamageKit{public RosterJob Job;}
sealed class DamageAiCounterTests
{
    /* ACTUAL_AI_COUNTER */
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var kit=new NineKeyDamageKit{Job=CharacterRosterData.Jobs[6]};
            var actor=new CharacterActor{role=new CharacterSkillDefinition{id=NineKeyDamageData.Id(kit.Job,"c0"),range=3}};
            var boss=new BossController();var enemy=new Transform{boss=boss};
            var counter=new CombatCounterTarget{transform=enemy};enemy.counter=counter;
            actor.transform.position=new Vector3(0,0,2);actor.transform.forward=new Vector3(0,0,-1);
            Check(DamageCounterReady(actor,kit,enemy,boss),"front facing eligible counter is admitted");
            actor.transform.position=new Vector3(0,0,-2);actor.transform.forward=new Vector3(0,0,1);
            Check(!DamageCounterReady(actor,kit,enemy,boss),"rear counter remains unspent despite open signal");
            actor.transform.position=new Vector3(2,0,0);actor.transform.forward=new Vector3(-1,0,0);
            Check(!DamageCounterReady(actor,kit,enemy,boss),"side counter is withheld");
            actor.transform.position=new Vector3(0,0,2);actor.transform.forward=new Vector3(0,0,1);
            Check(!DamageCounterReady(actor,kit,enemy,boss),"actor facing away cannot spend counter");
            actor.transform.forward=new Vector3(0,0,-1);actor.transform.position=new Vector3(0,0,8);
            Check(!DamageCounterReady(actor,kit,enemy,boss),"melee range is respected");
            actor.role.range=15;Check(DamageCounterReady(actor,kit,enemy,boss),"ranged front counter is allowed");
            counter.open=false;Check(!DamageCounterReady(actor,kit,enemy,boss),"closed signal is not counterable");counter.open=true;
            enemy.alive=false;Check(!DamageCounterReady(actor,kit,enemy,boss),"dead target is rejected");enemy.alive=true;
            boss.GuardianNormal=true;boss.ready=false;Check(!DamageCounterReady(actor,kit,enemy,boss),"guardian-specific readiness still gates AI");boss.GuardianNormal=false;
            boss.JanusEncounter=true;boss.janusAllowed=false;Check(!DamageCounterReady(actor,kit,enemy,boss),"Janus assigned counter restriction is preserved");boss.JanusEncounter=false;
            boss.CampanellaLockCounterWindow=true;boss.lockAllowed=false;Check(!DamageCounterReady(actor,kit,enemy,boss),"Campanella lock ownership is respected");boss.CampanellaLockCounterWindow=false;
            actor.role.id=NineKeyDamageData.Id(kit.Job,"c1");Check(!DamageCounterReady(actor,kit,enemy,boss),"stagger choice never masquerades as counter");
            Check(!DamageCounterReady(actor,kit,null,boss),"missing target is rejected");
            actor.player=new PlayerController{transform=actor.transform};
            actor.transform.position=new Vector3(0,0,2);actor.transform.forward=new Vector3(0,0,1);
            FaceDamageCounter(actor,kit,enemy);
            Check(actor.transform.forward.z==1,"non-counter role keeps its facing");
            actor.role.id=NineKeyDamageData.Id(kit.Job,"c0");counter.open=false;
            FaceDamageCounter(actor,kit,enemy);
            Check(actor.transform.forward.z==1,"closed counter signal does not turn actor");counter.open=true;
            var start=actor.transform.position;
            FaceDamageCounter(actor,kit,enemy);
            Check(actor.transform.forward.z==-1&&DamageCounterReady(actor,kit,enemy,boss),"stationary actor at front turns through actual attack-facing API and becomes counter eligible");
            Check((actor.transform.position-start).sqrMagnitude==0,"facing never teleports or moves actor");
            for(int movement=0;movement<5;movement++)
            {
                actor.player.HasMovementIntent=movement==0;actor.player.IsMovingXZ=movement==1;
                actor.player.IsDashing=movement==2;actor.player.IsBeingRescued=movement==3;actor.player.IsBeingKnockedBack=movement==4;
                actor.transform.forward=new Vector3(0,0,1);FaceDamageCounter(actor,kit,enemy);
                Check(actor.transform.forward.z==1&&!DamageCounterReady(actor,kit,enemy,boss),"movement/dash/forced movement owns facing "+movement);
            }
            actor.player.IsBeingKnockedBack=false;
            actor.transform.position=new Vector3(0,0,-2);actor.transform.forward=new Vector3(0,0,-1);
            FaceDamageCounter(actor,kit,enemy);
            Check(actor.transform.forward.z==1&&!DamageCounterReady(actor,kit,enemy,boss),"turning toward enemy cannot bypass rear-position restriction");
            FaceDamageCounter(actor,kit,null);
            Check(actor.transform.forward.z==1,"missing target cannot rotate actor");
            Console.WriteLine("PASS actual DPS AI counter eligibility: "+checks+"; native transforms/navigation remain unverified.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
