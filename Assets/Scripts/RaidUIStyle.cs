using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Authored graphics keep their own Inspector values. Theme application is explicit, never per-frame.
    public sealed class RaidUIStyle : MonoBehaviour
    {
        public enum ThemeRole { None, Panel, Button, Border, Health, Resource, Cooldown, Text }
        public ThemeRole role;
        [Tooltip("Off: preserve individual Inspector values. On: apply selected theme once at Awake.")]
        public bool useTheme;
        public RaidUITheme themeOverride;
        public Graphic graphic;
        [Tooltip("Gameplay state colors, independent of the default graphic color.")]
        public bool useStateColors;
        public Color ready = UnityEngine.Color.white, unavailable = UnityEngine.Color.gray, selected = UnityEngine.Color.cyan, locked = UnityEngine.Color.gray;
        private void Awake() { if (useTheme) ApplyTheme(); }
        [ContextMenu("Apply Theme Now")]
        public void ApplyTheme()
        {
            var theme = themeOverride != null ? themeOverride : RaidUIPresentation.Settings?.theme;
            if (theme == null) return;
            if (graphic == null) graphic = GetComponent<Graphic>();
            if (graphic is Text text) { if (theme.font != null) text.font = theme.font; text.fontSize = theme.fontSize; text.color = theme.text; }
            if (graphic is Image image)
            {
                Sprite sprite = null;
                switch (role)
                {
                    case ThemeRole.Panel: sprite = theme.panelSprite; image.color = theme.panel; break;
                    case ThemeRole.Button: sprite = theme.buttonSprite; break;
                    case ThemeRole.Border: sprite = theme.borderSprite; image.color = theme.accent; break;
                    case ThemeRole.Health: sprite = theme.healthSprite; image.color = theme.health; break;
                    case ThemeRole.Resource: sprite = theme.resourceSprite; image.color = theme.resource; break;
                    case ThemeRole.Cooldown: sprite = theme.cooldownSprite; image.color = theme.cooldown; break;
                }
                if (sprite != null) image.sprite = sprite;
            }
            ready = theme.ready; unavailable = theme.unavailable; selected = theme.selected; locked = theme.locked;
        }
        public static void Color(Graphic graphic, Color fallback)
        {
            if (graphic == null) return;
            // A prefab author owns its base color. Legacy procedural views retain their former behavior.
            if (!graphic.TryGetComponent<RaidUIStyle>(out var style)) graphic.color = fallback;
        }
        public static void State(Graphic graphic, bool enabled, bool isLocked = false)
        {
            if (graphic != null && graphic.TryGetComponent<RaidUIStyle>(out var style) && style.useStateColors)
                graphic.color = isLocked ? style.locked : enabled ? style.ready : style.unavailable;
        }
        public static bool Authored(Component element) => element != null && element.GetComponentInParent<RaidUIRoot>() != null;
    }
}
