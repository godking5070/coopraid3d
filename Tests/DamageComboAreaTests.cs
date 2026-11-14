using System;
using System.Linq;
using RaidRogue;
sealed class Transform { public DamagePoint position;public bool immune;public float damage,stagger; }
static class CombatRange { public static float Radius(Transform t)=>.5f; }
static class CharacterKit { public static Transform[] targets;public static Transform[] Enemies()=>targets; }
sealed class DamageComboAreaTests
{
    readonly Transform transform=new Transform();
    RosterJob Job=CharacterRosterData.Jobs.Single(j=>j.index==8);
    int Route=1;
    DamageProjectionState _projection=new DamageProjectionState();
    bool multi;
    DamagePoint[] prismPath;
    bool Core(int route,int choice)=>route==3&&choice==1&&multi;
    DamagePoint[] PrismPath()=>prismPath;
    DamagePoint Point(DamagePoint p)=>p;
    bool Enemy(Transform t)=>t!=null;
    float EffectiveRange(CharacterSkillDefinition s)=>Job.index==11&&Route==2&&!_projection.Active?3:20;
    bool Attack(CharacterSkillDefinition s,Transform t,float power,float stagger,CombatDamageKind kind=CombatDamageKind.Direct)
    {if(t==null||t.immune)return false;t.damage+=power;t.stagger+=stagger;return true;}
    /* ACTUAL_COMBO_AREA */
    static int count;
    static void Check(bool valid,string why){count++;if(!valid)throw new Exception(why);}
    static void Main()
    {
        try
        {
            var kit=new DamageComboAreaTests();var skill=new CharacterSkillDefinition{comboChain=2};
            var primary=new Transform{position=new DamagePoint(10,0)};
            var behind=new Transform{position=new DamagePoint(15,0)};
            var side=new Transform{position=new DamagePoint(10,4)};
            var rear=new Transform{position=new DamagePoint(-5,0)};
            CharacterKit.targets=new[]{primary,behind,behind,side,rear};
            Check(kit.ComboAttack(skill,primary,3,0)==primary,"one primary completion candidate returned");
            Check(primary.damage==180&&behind.damage==180,"penetration reaches aligned targets only once each");
            Check(side.damage==0&&rear.damage==0,"penetration excludes lateral and rear targets");
            primary.damage=behind.damage=0;
            Check(kit.ComboAttack(skill,primary,3,1)==primary,"spread still has one completion candidate");
            Check(primary.damage==144&&behind.damage==144&&side.damage==144&&rear.damage==0,"spread uses wider footprint and reduced direct damage");
            Check(primary.stagger==40&&behind.stagger==40&&side.stagger==40,"each spread hit has stagger without counter success");
            primary.immune=true;behind.immune=true;
            Check(kit.ComboAttack(skill,primary,1,1)==side,"immune primary allows one actually landed collateral completion");
            side.immune=true;Check(kit.ComboAttack(skill,primary,1,1)==null,"all failed hits produce no completion candidate");
            primary.immune=false;primary.damage=0;side.immune=false;side.damage=0;
            kit.ComboAttack(skill,primary,1,2);
            Check(primary.damage==100&&side.damage==0,"silver damage over time combo retains single target base attack");
            Check(!NineKeyDamageData.ComboAreaHit(1,default,default,new DamagePoint(1,0),20,.5f),"zero aim cannot create arbitrary direction");
            Check(!NineKeyDamageData.ComboAreaHit(2,default,new DamagePoint(1,0),new DamagePoint(21,0),20,.5f),"spread is bounded by range");
            Check(NineKeyDamageData.ComboAreaHit(1,default,new DamagePoint(0,1),new DamagePoint(0,15),20,.5f),"geometry rotates with aim");
            kit.Job=CharacterRosterData.Jobs.Single(j=>j.index==10);
            primary.damage=behind.damage=side.damage=0;primary.stagger=side.stagger=0;
            behind.immune=false;side.position=new DamagePoint(10,2);
            Check(kit.ComboAttack(skill,primary,3,1)==primary,"gravity wave returns only one completion target");
            Check(primary.damage==144&&side.damage==144&&behind.damage==0,"gravity wave impacts target centered area rather than whole forward cone");
            Check(primary.stagger==40&&side.stagger==40,"gravity wave retains stagger enhancement per valid hit");
            kit.Job=CharacterRosterData.Jobs.Single(j=>j.index==11);kit.Route=2;
            primary.damage=side.damage=0;
            Check(kit.ComboAttack(skill,primary,3,1)==null,"unprojected reaper area cannot hit remote targets");
            primary.position=new DamagePoint(2,0);side.position=new DamagePoint(-2,0);
            kit.ComboAttack(skill,primary,3,1);
            Check(primary.damage==144&&side.damage==144,"direct reaper area surrounds actual body");
            primary.position=new DamagePoint(10,0);side.position=new DamagePoint(10,2);primary.damage=side.damage=0;
            kit._projection.Begin(10);kit.ComboAttack(skill,primary,3,1);
            Check(primary.damage==144&&side.damage==144,"paid projection moves area to remote attack target");
            kit.Route=3;kit._projection=new DamageProjectionState();primary.damage=side.damage=0;
            kit.ComboAttack(skill,primary,1,1);
            Check(primary.damage==80&&side.damage==80,"architect base projection remains ranged without paid reaper state");
            Check(NineKeyDamageData.ComboArea(10,1,1)==0&&NineKeyDamageData.ComboArea(11,2,2)==0,"first combo and shatter choice do not inherit area variant");
            kit.prismPath=new[]{new DamagePoint(0,0),new DamagePoint(2,0),new DamagePoint(2,2),new DamagePoint(0,2)};
            primary.position=new DamagePoint(0,2);primary.damage=primary.stagger=0;
            kit.PrismFinisher(skill,primary);
            Check(primary.damage==160&&primary.stagger==60,"baseline reflected finisher retains original budget");
            kit.multi=true;primary.damage=primary.stagger=0;kit.PrismFinisher(skill,primary);
            Check(primary.damage==220&&primary.stagger==80,"multi refraction rewards actual second reflected segment hit");
            primary.position=new DamagePoint(2,1);primary.damage=primary.stagger=0;kit.PrismFinisher(skill,primary);
            Check(primary.damage==160&&primary.stagger==60,"hit before second mirror does not receive two-mirror bonus");
            primary.position=new DamagePoint(1,0);primary.damage=primary.stagger=0;kit.PrismFinisher(skill,primary);
            Check(primary.damage==0&&primary.stagger==0,"incoming segment remains excluded from reflected bonus");
            kit.prismPath=new[]{new DamagePoint(0,0),new DamagePoint(2,0),new DamagePoint(2,1)};
            primary.position=new DamagePoint(0,2);kit.PrismFinisher(skill,primary);
            Check(primary.damage==0,"blocked second reflection cannot award multi refraction");
            kit.Job=CharacterRosterData.Jobs.Single(j=>j.index==6);
            for(int route=1;route<=3;route++)
            {
                kit.Route=route;primary.damage=primary.stagger=0;primary.position=new DamagePoint(2,0);
                kit.ComboAttack(skill,primary,1,1);kit.ComboAttack(skill,primary,2,1);kit.ComboAttack(skill,primary,3,1);
                Check(primary.damage==304&&primary.stagger==0,"ranged dagger direct coefficient applies in every route without borrowing stagger");
                primary.damage=0;primary.position=new DamagePoint(12,0);
                Check(kit.ComboAttack(skill,primary,3,1)==primary&&primary.damage==144,"ranged dagger has the same coefficient at distance and preserves completion target");
                primary.damage=0;kit.ComboAttack(skill,primary,3,0);
                Check(primary.damage==180,"melee bleed choice retains its existing direct coefficient");
                primary.damage=0;kit.ComboAttack(new CharacterSkillDefinition{comboChain=1},primary,3,1);
                Check(primary.damage==180,"first combo is not reduced by second-combo choice");
            }
            Console.WriteLine("PASS actual combo footprints and direct coefficients: "+count+"; damage world and collision are managed seams.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
