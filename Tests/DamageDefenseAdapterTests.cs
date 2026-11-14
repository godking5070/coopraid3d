using System;
using System.Collections.Generic;
using System.Linq;
using RaidRogue;
enum PlayerRole { Guardian_Tank, Other }
sealed class AegisPaladin { public bool IsActive; public float PhysicalDefense=0,MagicDefense=0; }
sealed class CharacterKit { public bool Active; public float Defense=0; }
sealed class NineKeyTankKit { public bool Active; }
sealed class NineKeyHealerKit { public bool Active; }
sealed class NineKeyDamageKit { public bool Active; }
sealed class CharacterActor { public CharacterDefinition Definition; }
sealed class DamageDefenseAdapterTests
{
    readonly Dictionary<Type,object> components=new Dictionary<Type,object>();
    T GetComponent<T>() where T:class=>components.TryGetValue(typeof(T),out var value)?value as T:null;
    PlayerRole currentRole=PlayerRole.Other;
    float tankPassiveDefenseRate=.2f,damageReductionRate=.3f;
    bool IsDefensiveBuffActive;
    /* ACTUAL_DAMAGE_DEFENSE */
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Main()
    {
        try
        {
            foreach(var job in CharacterRosterData.Jobs.Where(NineKeyDamageData.Supports))
            {
                var a=new DamageDefenseAdapterTests();var definition=NineKeyDamageData.Build(job);
                a.components[typeof(CharacterActor)]=new CharacterActor{Definition=definition};
                a.components[typeof(NineKeyDamageKit)]=new NineKeyDamageKit{Active=true};
                Check(a.CalculateFinalDamageTaken(100)==AegisRouteData.Mitigate(100,definition.physicalDefense),"new DPS physical defense remains applied: "+job.name);
                Check(a.CalculateFinalDamageTaken(100,CombatDefenseType.Magic)==AegisRouteData.Mitigate(100,definition.magicDefense),"new DPS magic defense remains applied");
                definition.magicDefense=definition.physicalDefense+100;
                Check(a.CalculateFinalDamageTaken(100,CombatDefenseType.Magic)<a.CalculateFinalDamageTaken(100),"different magic armor uses its own rating rather than physical armor");
                Check(a.CalculateFinalDamageTaken(100,CombatDefenseType.Bypass)==100,"armor bypass is not converted into normal mitigation");
                a.IsDefensiveBuffActive=true;
                Check(Math.Abs(a.CalculateFinalDamageTaken(100)-AegisRouteData.Mitigate(100,definition.physicalDefense)*.7f)<.001f,"existing personal buff multiplies once after armor");
            }
            var legacy=new DamageDefenseAdapterTests();legacy.currentRole=PlayerRole.Guardian_Tank;
            Check(legacy.CalculateFinalDamageTaken(100)==80,"legacy tank without a new kit retains passive defense");
            legacy.components[typeof(CharacterKit)]=new CharacterKit{Active=true,Defense=50};
            Check(legacy.CalculateFinalDamageTaken(100)==AegisRouteData.Mitigate(100,50),"legacy roster armor does not stack with role passive");
            Console.WriteLine("PASS actual player DPS defense adapter: "+checks+"; component lookup is a managed seam.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
