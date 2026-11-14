using System;
using System.Linq;
using RaidRogue;
sealed class Transform { public bool immune; }
static class CharacterKit {public static int Id(Transform t)=>9;}
sealed class DamageUltimateTests
{
    RosterJob Job=CharacterRosterData.Jobs.Single(j=>j.index==6);
    CharacterEffectState _effects=new CharacterEffectState();
    Transform target=new Transform();float Now,total;int direct,extra;bool reenter;
    CharacterSkillDefinition Skill(string key)=>new CharacterSkillDefinition{id=key};
    Transform Resolve(int id)=>id==9?target:null;
    bool Attack(CharacterSkillDefinition skill,Transform t,float power,float stagger=0,CombatDamageKind kind=CombatDamageKind.Direct,bool launched=false)
    {
        if(kind==CombatDamageKind.Direct)direct++;else {extra++;Check(launched,"deferred ultimate retains launch acceptance");}
        if(reenter&&kind==CombatDamageKind.Extra){reenter=false;TickUltimate();}
        if(t==null||t.immune)return false;total+=power;return true;
    }
    /* ACTUAL_ULTIMATE */
    static int count;
    static void Check(bool valid,string why){count++;if(!valid)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var a=new DamageUltimateTests();Check(a.UseUltimate(a.Skill("v"),a.target)&&a.total==120&&a._effects.Data.effects.Length==3,"night ultimate starts with one manual strike and three finite followups");
            a.TickUltimate();Check(a.extra==0,"future strikes are not immediately completed");
            a.Now=.15f;a.TickUltimate();Check(a.total==240&&a._effects.Data.effects.Length==2,"first dagger consumes one scheduled strike");
            var b=new DamageUltimateTests();b._effects.Restore(a._effects.Data.Copy());b.Now=1;b.reenter=true;b.TickUltimate();
            Check(b.total==560&&b.extra==2&&b.direct==0&&b._effects.Data.effects.Length==0,"restored pending strikes never replay first hits or recursively duplicate finisher");
            b.TickUltimate();Check(b.total==560,"finished ultimate has no repeated attack");
            a.target.immune=true;a.Now=1;a.TickUltimate();a.target.immune=false;a.TickUltimate();
            Check(a.total==240&&a.extra==3,"immune late hits consume their opportunity without retry");
            var mirror=new DamageUltimateTests{Job=CharacterRosterData.Jobs.Single(j=>j.index==11)};
            mirror.UseUltimate(mirror.Skill("v"),mirror.target);mirror.Now=1;mirror.TickUltimate();
            Check(mirror.total==800&&mirror.direct==1&&mirror.extra==2,"mirror ultimate has one direct and two nonrecursive illusion/crystal strikes");
            var other=new DamageUltimateTests{Job=CharacterRosterData.Jobs.Single(j=>j.index==7)};
            other.UseUltimate(other.Skill("v"),other.target);
            Check(other.total==800&&other._effects.Data.effects.Length==0,"tempest retains focused single ultimate");
            var invalid=new DamageUltimateTests();invalid.target.immune=true;
            Check(!invalid.UseUltimate(invalid.Skill("v"),invalid.target)&&invalid._effects.Data.effects.Length==0,"rejected initial attack does not grant free future strikes");
            Console.WriteLine("PASS actual ultimate sequencing: "+count+"; native animation and damage remain world seams.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
