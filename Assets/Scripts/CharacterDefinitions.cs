using UnityEngine;
namespace RaidRogue
{
    public static class CharacterDefinitions
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => CharacterCatalog.ResetCatalog();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAssets()
        {
            if(!CharacterCatalog.Register(AegisPaladinData.BuildNineKey(),out string basicError,true)) Debug.LogError(basicError);
            foreach(var job in CharacterRosterData.Jobs)
                if(!CharacterCatalog.Register(NineKeyTankData.Supports(job)?NineKeyTankData.Build(job):NineKeyHealerData.Supports(job)?NineKeyHealerData.Build(job):NineKeyDamageData.Supports(job)?NineKeyDamageData.Build(job):CharacterRosterData.Definition(job),out string error,true))Debug.LogError(error);
            // Optional definitions live in Resources/Characters. No inspector scene references required.
            foreach(var asset in Resources.LoadAll<CharacterDefinitionSO>("Characters"))
            {
                var definition=asset.BuildDefinition();
                if(!CharacterCatalog.Register(definition,out string error,asset.implementsRegisteredJob))Debug.LogError("Character definition rejected: "+asset.name+" · "+error,asset);
            }
        }
    }
}
