using System;
using System.Linq;
using System.Collections.Generic;
using RaidRogue;

sealed class Transform { public int id=9; public bool living=true; }
static class CombatRange { public static bool LivingEnemy(Transform t)=>t!=null&&t.living; }
static class CharacterKit { public static int Id(Transform t)=>t.id; }
sealed class DamageScheduledAdapterTests
{
    CharacterEffectState _effects=new CharacterEffectState();
    float Now;
    Transform target=new Transform();
    List<CombatDamageKind> calls=new List<CombatDamageKind>();
    bool immune,reenter;
    int hits;
    int fractures,shields,speeds;
    float Power=>1000;
    bool Core(int route,int choice)=>false;
    Transform[] Party()=>new[]{target};
    void Shield(Transform t,string key,float power){shields++;}
    void Speed(){speeds++;}
    void Effect(string id,string kind,Transform t,float value,float duration){fractures++;}
    string Key(CharacterSkillDefinition skill)=>skill.id;
    CharacterSkillDefinition Skill(string key)=>new CharacterSkillDefinition{id=key};
    Transform Resolve(int id)=>id==target.id?target:null;
    bool Attack(CharacterSkillDefinition skill,Transform t,float damage,float stagger,CombatDamageKind kind,bool launched=false)
    {
        Check(launched&&kind!=CombatDamageKind.Direct,"scheduled hit stays automatic and does not revalidate player casting distance");
        calls.Add(kind);
        if(reenter){reenter=false;TickScheduledDamage();}
        if(immune||!CombatRange.LivingEnemy(t))return false;
        hits++;return true;
    }
    /* ACTUAL_SCHEDULE_METHODS */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageScheduledAdapterTests();var skill=a.Skill("g22");
            a.ScheduleDamage(skill,a.target,30,1,4,true);a.TickScheduledDamage();
            Check(a.calls.Count==0,"no immediate dot");
            a.Now=1;a.reenter=true;a.TickScheduledDamage();
            Check(a.calls.Count==1&&a.hits==1,"tick consumed before reentrant callback");
            a.immune=true;a.Now=2;a.TickScheduledDamage();
            Check(a.calls.Count==2&&a.hits==1,"immune target consumes failed tick");
            a.immune=false;a.TickScheduledDamage();Check(a.calls.Count==2,"no retry after immunity ends in same tick");
            a.Now=99;a.TickScheduledDamage();
            Check(a.calls.Count==4&&a.calls.All(k=>k==CombatDamageKind.Periodic)&&a._effects.Data.effects.Length==0,"large frame applies only finite original dot budget");
            a.TickScheduledDamage();Check(a.calls.Count==4,"expired dot cannot run again");
            a.ScheduleDamage(skill,a.target,30,1,4,true);a.ScheduleDamage(skill,a.target,30,1,4,true);
            Check(a._effects.Data.effects.Length==1,"same caster same dot refreshes without stacking");
            a.ScheduleDamage(a.Skill("a2"),a.target,100,1.5f,1,false);
            Check(a._effects.Data.effects.Length==2,"different skills keep independent schedules");
            a.Now=100.5f;a.TickScheduledDamage();
            Check(a.calls.Count==6&&a.calls.Last()==CombatDamageKind.Extra,"single delayed strike fires once as Extra");
            a.TickScheduledDamage();Check(a.calls.Count==6,"same frame cannot replay followup");
            a.target.living=false;a.Now=110;a.TickScheduledDamage();
            Check(a._effects.Data.effects.Length==0,"dead target does not retain pending retries");
            a.target.living=true;a.ScheduleDamage(skill,a.target,1,float.NaN,1,true);
            a.ScheduleDamage(skill,a.target,1,1,17,true);
            Check(a._effects.Data.effects.Length==0,"invalid schedule rejected");
            var b=new DamageScheduledAdapterTests();b.ScheduleDamage(skill,b.target,1,1,1,false);
            Check(a._effects.Data.effects.Length==0&&b._effects.Data.effects.Length==1,"caster schedules remain independent");
            var attached=new DamageScheduledAdapterTests();attached.DetonateGrenade(9);
            Check(attached.hits==1&&attached.fractures==1,"attached grenade retains accepted launch range and grants fracture on hit");
            attached.ResolveSeal(new DamageSealResolution(9,DamageSealTrigger.CastEnd,true));
            Check(attached.hits==2&&attached.shields==1,"attached enhanced cast seal detonates without current caster range");
            attached.ResolveSeal(new DamageSealResolution(9,DamageSealTrigger.PatternMoveEnd,true));
            Check(attached.speeds==1,"movement seal keeps its own synergy");
            attached.immune=true;attached.DetonateGrenade(9);attached.ResolveSeal(new DamageSealResolution(9,DamageSealTrigger.CastEnd,true));
            Check(attached.hits==3&&attached.fractures==1&&attached.shields==1,"failed attached explosions grant neither damage nor synergy");
            Console.WriteLine("PASS actual scheduled damage adapter: "+count+"; world damage is a test seam.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
