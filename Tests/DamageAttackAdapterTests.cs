using System;
using System.Linq;
using RaidRogue;

struct Vector3
{
    public float x,z;
    public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.z-b.z)*(a.z-b.z));
}
struct Color { public static Color cyan=>default; }
static class Mathf { public static int RoundToInt(float value)=>(int)Math.Round(value); }
sealed class Transform
{
    public Vector3 position;public bool alive=true;public BossController boss;public BossAddMinion add;
    public T GetComponent<T>() where T:class =>typeof(T)==typeof(BossController)?boss as T:add as T;
}
sealed class BossController
{
    public float currentHp=10000;public bool HpDamageBlocked;
    public void ReceiveRoleSkillAttack(string job,string skill,int unused,int amount,Color color,bool flag,Transform source,string origin,CombatDamageKind kind)
    {currentHp-=amount;}
}
sealed class BossAddMinion
{
    public float currentHp=10000;
    public void ReceivePlayerAttack(string job,string skill,int amount,Color color,bool flag,Transform source,string origin,CombatDamageKind kind)
    {currentHp-=amount;}
}
static class CombatRange
{
    public static bool LivingEnemy(Transform target)=>target!=null&&target.alive;
    public static bool InRange(Transform source,Transform target,float range)=>Vector3.Distance(source.position,target.position)<=range;
}
static class AiSpectatorDamage { public static bool ZeroHpDamageMode(Transform source)=>false; }
sealed class CombatCounterTarget
{
    public static CombatCounterTarget Ensure(Transform target)=>new CombatCounterTarget();
    public void AddStagger(float value){}
}
sealed class DamageAttackAdapterTests
{
    sealed class Actor
    {
        public bool reach;
        public bool HasAuxiliary(string id)=>reach&&id==NineKeyDamageData.Reach;
    }
    Actor _actor=new Actor();Transform transform=new Transform();string _deferredOrigin="";
    RosterJob Job=>CharacterRosterData.Jobs.First(j=>j.index==10);
    float EffectiveRange(CharacterSkillDefinition skill)=>20;
    Vector3 World(DamagePoint point)=>new Vector3{x=point.x,z=point.z};
    /* ACTUAL_DAMAGE_ATTACK */
    static int checks;
    static void Check(bool condition,string why){checks++;if(!condition)throw new Exception(why);}
    static void Main()
    {
        foreach(bool boss in new[]{true,false})foreach(bool reach in new[]{false,true})
        foreach(int chain in new[]{0,1,2})foreach(var kind in new[]{CombatDamageKind.Direct,CombatDamageKind.Periodic,CombatDamageKind.Extra})
        {
            var adapter=new DamageAttackAdapterTests();adapter._actor.reach=reach;
            var target=new Transform{boss=boss?new BossController():null,add=boss?null:new BossAddMinion()};
            var skill=new CharacterSkillDefinition{id="test.attack",name="test",comboChain=chain};
            Check(adapter.Attack(skill,target,100,kind:kind),"valid direct/dot/extra hit reaches actual adapter receiver");
            float remaining=boss?target.boss.currentHp:target.add.currentHp;
            float expected=reach&&chain==2&&kind==CombatDamageKind.Direct?95:100;
            Check(remaining==10000-expected,"reach tradeoff applies only to manual second-combo damage, never DoT, replay or prism bonus");
        }
        var delayed=new DamageAttackAdapterTests();delayed._actor.reach=true;
        var remote=new Transform{position=new Vector3{x=30},boss=new BossController()};
        var second=new CharacterSkillDefinition{id="g20",name="curve",comboChain=2};
        Check(delayed.Attack(second,remote,100,kind:CombatDamageKind.Direct,deferred:true,launched:true)&&remote.boss.currentHp==9905,
            "delayed manually launched second-combo arrow still pays direct damage tradeoff");
        Check(delayed._deferredOrigin=="","deferred direct provenance restores after attack");
        remote.boss.HpDamageBlocked=true;
        Check(!delayed.Attack(second,remote,100,kind:CombatDamageKind.Extra,launched:true)&&remote.boss.currentHp==9905,
            "removing extra damage penalty does not bypass boss immunity");
        Console.WriteLine("PASS actual DPS damage-kind auxiliary scope: "+checks+"; world damage receivers remain managed seams.");
    }
}
