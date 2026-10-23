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
            // Optional definitions live in Resources/Characters. No inspector scene references required.
            foreach(var asset in Resources.LoadAll<CharacterDefinitionSO>("Characters"))
            {
                var definition=asset.BuildDefinition();
                if(!CharacterCatalog.Register(definition,out string error,asset.implementsRegisteredJob))Debug.LogError("Character definition rejected: "+asset.name+" · "+error,asset);
            }
        }
    }
}
