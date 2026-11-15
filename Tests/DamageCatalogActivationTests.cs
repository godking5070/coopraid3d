using System;
using System.Linq;
using System.Reflection;
using RaidRogue;
namespace UnityEngine
{
    enum RuntimeInitializeLoadType { SubsystemRegistration,BeforeSceneLoad }
    [AttributeUsage(AttributeTargets.Method)] sealed class RuntimeInitializeOnLoadMethodAttribute:Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type){} }
    static class Resources {public static T[] LoadAll<T>(string path)=>Array.Empty<T>();}
    static class Debug {public static void LogError(string text,object context=null){throw new Exception(text);}}
}
namespace RaidRogue
{
    sealed class CharacterDefinitionSO
    { public string name="test";public bool implementsRegisteredJob=false;public CharacterDefinition BuildDefinition()=>null; }
}
static class DamageCatalogActivationTests
{
    static int checks;
    static void Check(bool valid,string why){checks++;if(!valid)throw new Exception(why);}
    static void Main()
    {
        try
        {
            // Execute the real registration hook with empty optional asset seams.
            // This is not a Unity scene initialization or native resource-load test.
            CharacterCatalog.ResetCatalog();
            typeof(CharacterDefinitions).GetMethod("RegisterAssets",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
            Check(CharacterCatalog.All.Count(d=>d.playable&&d.id.StartsWith("job."))==12,"all twelve implemented jobs remain selectable");
            foreach(var job in CharacterRosterData.Jobs.Where(NineKeyDamageData.Supports))
            {
                var definition=CharacterCatalog.Find(job.Id);
                Check(definition!=null&&CharacterSlots.IsNineKey(definition),job.name+" registered as nine key");
                var state=new CharacterRuntimeState(definition);
                Check(state.Selected(CharacterSlot.Gcd1).handlerId==NineKeyDamageData.Handler&&state.Selected(CharacterSlot.OffGcd2).handlerId==NineKeyDamageData.Handler,"selected damage slots use new host adapter");
                Check(!CharacterSlots.Enabled(definition,CharacterSlot.Job2)&&!CharacterSlots.Enabled(definition,CharacterSlot.Active),"old E/Z slots stay disabled");
                foreach(var route in definition.routes)
                    Check(state.ChooseRoute(route.id,true)&&state.Selected(CharacterSlot.Identity).handlerId==NineKeyDamageData.Handler,"route switch uses new identity");
            }
            Check(CharacterDefinitionValidation.Check(CharacterCatalog.All).Length==0,"mixed tank healer damage catalog validates");
            Console.WriteLine("PASS actual character catalog activation: "+checks+"; optional Unity asset loading remains unverified.");
        }
        catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}
    }
}
