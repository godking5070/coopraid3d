using UnityEngine;
namespace RaidRogue
{
    [CreateAssetMenu(menuName = "Raid/UI Theme")]
    public sealed class RaidUITheme : ScriptableObject
    {
        public Font font;
        [Min(1)] public int fontSize = 18;
        public Color text = Color.white, panel = new Color(.05f,.08f,.12f,.95f), accent = new Color(.9f,.7f,.3f);
        public Color ready = Color.white, unavailable = new Color(.5f,.5f,.5f), selected = new Color(.3f,.8f,1), locked = new Color(.5f,.5f,.5f);
        public Sprite panelSprite, buttonSprite, borderSprite, healthSprite, resourceSprite, cooldownSprite;
        [Tooltip("Use this theme's painted surfaces when explicitly constructing procedural utility panels.")]
        public bool prismSurfaces;
        public Color health = new Color(.3f,.8f,.4f), resource = new Color(.3f,.8f,.8f), cooldown = new Color(0,0,0,.65f);
        [Range(.3f, 1.5f)] public float combatTextScale = 1f;
        public bool compactDamageText;
        [Tooltip("Start the waiting-room build selector collapsed; click its heading to expand.")]
        public bool compactLoadout;
    }
}
