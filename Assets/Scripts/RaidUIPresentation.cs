using System;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    [CreateAssetMenu(menuName = "Raid/UI Presentation")]
    public sealed class RaidUIPresentation : ScriptableObject
    {
        [Serializable] public sealed class ViewEntry { public string id; public RaidUIView prefab; }
        [Serializable] public sealed class IconEntry { public string id; public Sprite sprite; }
        public RaidUIRoot hudPrefab;
        public ViewEntry[] views = Array.Empty<ViewEntry>();
        [Header("IDs from Character definitions; e.g. skill.id, job.id, potion, status identifier")]
        public IconEntry[] icons = Array.Empty<IconEntry>();
        public Sprite fallbackIcon;
        public RaidUITheme theme;
        public RaidUIRepeatedItem skillTemplate, hotbarTemplate, partyTemplate, resourceTemplate, statusTemplate;
        public RaidUIRepeatedItem formButtonTemplate, formTextTemplate;
        private static RaidUIPresentation _settings;
        public static RaidUIPresentation Settings => _settings != null ? _settings : (_settings = Resources.Load<RaidUIPresentation>("UI/RaidUIPresentation"));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset() { _settings = null; }
        public RaidUIView View(string id) => Array.Find(views, x => x.id == id)?.prefab;
        public Sprite Icon(string id) => Array.Find(icons, x => x.id == id)?.sprite ?? fallbackIcon;
    }
}
