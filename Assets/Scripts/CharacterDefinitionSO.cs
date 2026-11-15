using UnityEngine;
namespace RaidRogue
{
    // Authoring only. No resource amount, timers, charges or per-player build state lives in assets.
    [CreateAssetMenu(menuName="Astella/Character Definition")]
    public sealed class CharacterDefinitionSO : ScriptableObject
    {
        public bool implementsRegisteredJob; // Explicitly replaces one unimplemented registry entry; duplicate assets are rejected.
        public CharacterDefinition definition = new CharacterDefinition();
        public SkillDefinitionSO[] skillAssets = new SkillDefinitionSO[0];
        public GameObject visualPrefab;
        public Sprite icon;
        public CharacterDefinition BuildDefinition()
        {
            var copy=JsonUtility.FromJson<CharacterDefinition>(JsonUtility.ToJson(definition));
            if ((skillAssets?.Length ?? 0)>0) copy.skills=System.Array.ConvertAll(skillAssets,s=>s!=null?JsonUtility.FromJson<CharacterSkillDefinition>(JsonUtility.ToJson(s.definition)):null);
            return copy;
        }
        private void OnValidate()
        { foreach (var error in CharacterDefinitionValidation.Check(new[]{BuildDefinition()})) Debug.LogWarning(error,this); }
    }
}
