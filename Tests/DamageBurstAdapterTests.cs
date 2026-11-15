using System;
using System.Linq;
using System.Collections.Generic;
using RaidRogue;
sealed class Transform { public DamagePoint position; }
static class CharacterKit { public static Transform[] enemies=Array.Empty<Transform>(); public static Transform[] Enemies()=>enemies; }
static class CombatRange { public static float Radius(Transform target)=>0; }
sealed class DamageBurstAdapterTests
{
    readonly Transform transform=new Transform();
    readonly List<Transform> hits=new List<Transform>();
    DamagePoint Point(DamagePoint p)=>p;
    float EffectiveRange(CharacterSkillDefinition skill)=>20;
    readonly DamageBurstState _burst=new DamageBurstState();
    readonly DamageArrowState _arrows=new DamageArrowState();
    float resource=50,power;
    int variant,speed,marks;
    bool valid=true,landed=true;
    bool Core(int route,int choice)=>(route==1||route==2)&&variant==choice;
    bool Enemy(Transform enemy)=>valid&&enemy!=null;
    bool Spend(float value){if(resource<value)return false;resource-=value;return true;}
    bool Attack(CharacterSkillDefinition skill,Transform target,float damage){power=damage;if(landed)hits.Add(target);return landed;}
    void Mark(Transform target){marks++;}
    void Speed(){speed++;}
    /* ACTUAL_BURST_METHOD */
    static int count;
    static void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var skill=new CharacterSkillDefinition();var enemy=new Transform();
            for(int variant=0;variant<=1;variant++)
            {
                var a=new DamageBurstAdapterTests{variant=variant};
                Check(a.RangerBurst(skill,enemy)&&a.resource==0&&a.power==(variant==0?300:180),"first bundle spends50 and reflects core priority");
                Check(a._burst.Snapshot.remaining==(variant==0?16:20),"pursuit extends storage window");
                Check(!a.RangerBurst(skill,enemy)&&a._burst.Snapshot.bundles==2,"rapid repeat cannot bypass bundle input delay");
                a._burst.Tick(.25f);a._burst.CompleteCombo();a.valid=false;
                Check(!a.RangerBurst(skill,enemy)&&a._burst.Snapshot.precise,"invalid target preserves prepared reinforcement");
                a.valid=true;Check(a.RangerBurst(skill,enemy)&&a.power==(variant==0?240:360)&&a.resource==0,"combo-linked followup has reduced fast-core or enhanced pursuit power without new cost");
                a._burst.Tick(.25f);
                Check(a.RangerBurst(skill,enemy)&&a.power==(variant==0?150:180)&&a.speed==1,"unreinforced final bundle grants movement once");
                Check(!a.RangerBurst(skill,enemy)&&a.speed==1,"empty resource cannot start a free fourth bundle");
                a.resource=50;a.RangerBurst(skill,enemy);a._burst.CompleteCombo();a._burst.Tick(30);
                Check(!a._burst.Active&&a.speed==1,"unused followups expire without free final synergy");
            }
            var frequent=new DamageBurstAdapterTests{variant=1,resource=90};
            frequent._arrows.Restore(new DamageArrowSnapshot{convergence=3});
            Check(frequent.RangerConvergence(skill,enemy)&&frequent.power==260&&frequent.resource==60&&frequent._arrows.Snapshot.convergence==2,"frequent convergence consumes only one stack for smaller shot");
            frequent.RangerConvergence(skill,enemy);frequent.RangerConvergence(skill,enemy);
            Check(frequent.resource==0&&frequent._arrows.Snapshot.convergence==0&&frequent.marks==3,"three paid shots use three distinct stacks without free resource loop");
            Check(!frequent.RangerConvergence(skill,enemy)&&frequent.marks==3,"empty wind prevents free extra shot");
            var wide=new DamageBurstAdapterTests{variant=0,resource=40};wide._arrows.Restore(new DamageArrowSnapshot{convergence=3});
            Check(wide.RangerConvergence(skill,enemy)&&wide.power==400&&wide._arrows.Snapshot.convergence==0&&wide.marks==1,"wide window trait spends full stack for one larger shot");
            frequent.resource=30;frequent.RangerConvergence(skill,enemy);
            Check(frequent.power==180&&frequent.marks==3,"zero stack still allows base shot without cooperation mark");
            frequent.resource=30;frequent.valid=false;frequent._arrows.Restore(new DamageArrowSnapshot{convergence=2});
            Check(!frequent.RangerConvergence(skill,enemy)&&frequent.resource==30&&frequent._arrows.Snapshot.convergence==2,"invalid target consumes neither wind nor stored convergence");
            frequent.valid=true;frequent.landed=false;frequent.RangerConvergence(skill,enemy);
            Check(frequent.resource==0&&frequent._arrows.Snapshot.convergence==1&&frequent.marks==3,"failed launched shot spends one stack without refund or mark");
            Check(frequent._arrows.ConsumeConvergence(0)==0&&frequent._arrows.Snapshot.convergence==1,"invalid partial consumption cannot alter stored stacks");
            var primary=new Transform{position=new DamagePoint(0,5)};
            var behind=new Transform{position=new DamagePoint(0,12)};
            var side=new Transform{position=new DamagePoint(2,10)};
            var beyond=new Transform{position=new DamagePoint(0,21)};
            var rear=new Transform{position=new DamagePoint(0,-3)};
            CharacterKit.enemies=new[]{primary,behind,behind,side,beyond,rear};
            var piercing=new DamageBurstAdapterTests{variant=0,resource=40};
            piercing._arrows.Restore(new DamageArrowSnapshot{convergence=2});
            Check(piercing.RangerConvergence(skill,primary)&&piercing.hits.SequenceEqual(new[]{primary,behind}),"convergence pierces collinear enemies once and excludes side rear and distant targets");
            Check(piercing.resource==0&&piercing._arrows.Snapshot.convergence==0&&piercing.marks==1,"collateral does not multiply convergence cost or cooperation marks");
            CharacterKit.enemies=Array.Empty<Transform>();
            Console.WriteLine("PASS actual ranger burst/convergence adapter: "+count+"; world attack is a test seam.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
