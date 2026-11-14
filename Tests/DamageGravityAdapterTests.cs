using System;
using RaidRogue;
sealed class Transform { public float x; public bool immune; }
static class CharacterKit
{
    public static readonly Transform target=new Transform();
    public static Transform[] Enemies()=>new[]{target};
}
sealed class DamageGravityAdapterTests
{
    DamageGravityState _gravity=new DamageGravityState();
    int variant;
    float damage,staggerApplied;
    bool Core(int r,int n)=>r==3&&variant==n;
    CharacterSkillDefinition Skill(string key)=>new CharacterSkillDefinition{id=key};
    bool Attack(CharacterSkillDefinition s,Transform t,float power,float stagger,CombatDamageKind kind,DamagePoint? field=null,float fieldRadius=0)
    {
        Check(kind==CombatDamageKind.Extra&&field.HasValue,"gravity remains automatic field damage without direct completion");
        if(t.immune||Math.Abs(t.x-field.Value.x)>fieldRadius)return false;
        damage+=power;staggerApplied+=stagger;return true;
    }
    /* ACTUAL_GRAVITY_TICK */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var stable=new DamageGravityAdapterTests{variant=0};stable._gravity.Begin(new DamagePoint(0,0),16);
            Check(stable._gravity.Toggle(out bool protection)&&protection,"first expansion permits protection");
            CharacterKit.target.x=8;stable.TickGravity(1);
            Check(stable.damage==40&&stable.staggerApplied==0,"stable orbit expanded field reaches8 without compressed stagger");
            stable._gravity.Toggle(out _);stable._gravity.Toggle(out protection);Check(!protection,"switching never renews first-expansion protection");
            var extreme=new DamageGravityAdapterTests{variant=1};extreme._gravity.Begin(new DamagePoint(0,0),12);
            CharacterKit.target.x=2.5f;extreme.TickGravity(1);Check(extreme.damage==0,"extreme compression excludes target outside2");
            CharacterKit.target.x=1;extreme.TickGravity(1);Check(extreme.damage==110&&extreme.staggerApplied==20,"extreme compression improves landed damage and stagger");
            extreme._gravity.Toggle(out _);CharacterKit.target.x=8;extreme.TickGravity(1);
            Check(extreme.damage==110,"extreme expanded field retains7 range instead of stable orbit range");
            extreme._gravity.Follow(new DamagePoint(8,0),true);extreme.TickGravity(1);
            Check(extreme.damage==150&&extreme._gravity.Snapshot.remaining==8,"finisher relocation changes center without extending life");
            CharacterKit.target.immune=true;extreme.TickGravity(8);CharacterKit.target.immune=false;extreme.TickGravity(10);
            Check(extreme.damage==150&&!extreme._gravity.Active,"immune ticks expire without deferred retry");
            Console.WriteLine("PASS actual gravity tick adapter: "+count+"; world collision is a test seam.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
