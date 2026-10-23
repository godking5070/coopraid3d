using UnityEngine;
namespace RaidRogue
{
    [CreateAssetMenu(menuName="Raid Rogue/Character Skill Definition")]
    public sealed class SkillDefinitionSO : ScriptableObject
    {
        public CharacterSkillDefinition definition = new CharacterSkillDefinition();
        public Sprite icon;
    }
}
