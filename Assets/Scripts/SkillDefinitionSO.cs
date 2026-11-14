using UnityEngine;
namespace RaidRogue
{
    [CreateAssetMenu(menuName="Astella/Character Skill Definition")]
    public sealed class SkillDefinitionSO : ScriptableObject
    {
        public CharacterSkillDefinition definition = new CharacterSkillDefinition();
        public Sprite icon;
    }
}
