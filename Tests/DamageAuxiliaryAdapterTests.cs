using System;
using System.Collections.Generic;
using RaidRogue;

sealed class Transform { public int id=1; }
static class CharacterKit { public static int Id(Transform t)=>t.id; }
sealed class CombatCounterTarget
{
    public static readonly CombatCounterTarget instance=new CombatCounterTarget();
    public bool signal,front=true;public int successes;public float stagger;
    public static CombatCounterTarget Ensure(Transform t)=>instance;
    public bool TryCounter(Transform source,float range,float amount){if(!signal||!front)return false;successes++;return true;}
    public void AddStagger(float value){stagger+=value;}
}
sealed class DamageAuxiliaryAdapterTests
{
    sealed class Actor
    {
        public HashSet<string> traits=new HashSet<string>();
        public bool HasAuxiliary(string id)=>traits.Contains(id);
    }
    Actor _actor=new Actor();
    Transform transform=new Transform();
    CharacterEffectState _effects=new CharacterEffectState();
    float Now;
    bool hit=true;float appliedStagger;
    bool Attack(CharacterSkillDefinition skill,Transform target,float power,float stagger=0)
    {if(!hit)return false;appliedStagger+=stagger;return true;}
    bool Has(string key)=>_effects.Find(key+"@1",Now)!=null;
    /* ACTUAL_AUXILIARY_METHODS */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageAuxiliaryAdapterTests();
            Check(a.TacticalDuration(6)==6&&a.TacticalDuration(3)==3,"unselected support trait leaves duration unchanged");
            a._actor.traits.Add(NineKeyDamageData.Support);
            Check(a.TacticalDuration(6)==9&&a.TacticalDuration(3)==4.5f,"support duration modifies both mark/shield and movement timers");
            Check(a.PersonalDefense()&&a._effects.Data.effects.Length==1,"support duration does not add recovery or alter Q");
            Check(a._effects.Find("q@1",0).end==4,"F support does not extend Q");
            a._actor.traits.Add(NineKeyDamageData.Recovery);a.PersonalDefense();
            var recovery=a._effects.Find("recovery.breath@1",0);
            Check(recovery!=null&&recovery.kind=="healingReceived"&&recovery.target==1&&recovery.value==.15f&&recovery.end==6,"Q recovery is self-only finite received-heal multiplier");
            Check(a._effects.Data.effects.Length==2,"repeated defense replaces effects instead of stacking");
            a.Now=2;a.PersonalDefense();
            Check(a._effects.Find("recovery.breath@1",7)!=null&&a._effects.Find("recovery.breath@1",8)==null,"refresh updates expiry with no permanent recovery");
            a._actor.traits.Add(NineKeyDamageData.Guard);a.PersonalDefense();
            Check(a._effects.Find("q@1",2).end==8&&a._effects.Find("recovery.breath@1",2).end==8,"defense duration and recovery use independent authored timers");
            var role=new DamageAuxiliaryAdapterTests();var skill=new CharacterSkillDefinition{range=15};
            Check(role.RoleStagger(skill,role.transform)&&role.appliedStagger==100,"baseline stagger remains100");
            Check(role.RoleProtectionDuration("c2")==6,"baseline role protection lasts6");
            role._actor.traits.Add(NineKeyDamageData.Response);
            role.RoleStagger(skill,role.transform);
            Check(role.appliedStagger==225,"response trait improves only chosen stagger role");
            Check(role.RoleProtectionDuration("c2")==9&&role.RoleProtectionDuration("a2")==6,"response extends role shield but not auxiliary shield");
            var counter=CombatCounterTarget.instance;counter.signal=false;
            role.RoleCounter(skill,role.transform);
            Check(counter.successes==0&&counter.stagger==40,"response does not turn unsignaled attack into counter");
            Check(role._effects.Find("counter.recovery@1",0).end==.15f,"response gives finite reduced counter recovery");
            Check(role.CounterRecovering(CharacterSlot.Gcd1)&&role.CounterRecovering(CharacterSlot.Identity),"counter recovery delays offensive followups");
            foreach(var slot in new[]{CharacterSlot.Job1,CharacterSlot.Counter,CharacterSlot.Movement,CharacterSlot.Potion})
                Check(!role.CounterRecovering(slot),"counter recovery preserves emergency input "+slot);
            role.Now=.15f;Check(!role.CounterRecovering(CharacterSlot.Gcd1),"recovery expires without permanent input lock");
            counter.signal=true;counter.front=false;role.RoleCounter(skill,role.transform);
            Check(counter.successes==0,"response preserves front-facing condition");
            counter.front=true;role.RoleCounter(skill,role.transform);
            Check(counter.successes==1,"valid signaled front counter succeeds once");
            var baseline=new DamageAuxiliaryAdapterTests();baseline.RoleCounter(skill,baseline.transform);
            Check(baseline._effects.Find("counter.recovery@1",0).end==.3f,"unselected recovery retains baseline delay");
            baseline._effects.Clear();baseline.hit=false;
            Check(!baseline.RoleCounter(skill,baseline.transform)&&!baseline.Has("counter.recovery"),"failed attack does not grant recovery state or counter");
            Console.WriteLine("PASS actual support-duration and Q recovery methods: "+count+"; received-heal world path is separately compiled.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
