#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Astella.PrismV4;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    /// <summary>
    /// Explicit cosmetic pass for preserved prefab copies. Never changes text, fonts,
    /// transforms, selectable references/listeners, input routing or gameplay state colors.
    /// </summary>
    public static class AstellaPrismSurfaceStyler
    {
        public const string FrameName = "Prism Surface Frame";
        private static readonly Regex SemanticNames = new Regex(
            @"\b(hp|health|resource|shield|aggro|threat|death|dead|warning|cooldown|cast|charge|gauge|minimap|marker|icon|art|artwork|emblem|portrait|symbol|glyph|buff|debuff|rarity)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SurfaceNames = new Regex(
            @"\b(panel|background|backdrop|backplate|bg|body|sheet|badge|overlay|menu|window|modal|root|surface|card|container|box|content|button|input|field|toggle|viewport|scroll)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static int Style(GameObject root, RaidUITheme theme, Sprite face, Sprite frame, Sprite buttonFace, Sprite buttonFrame)
        {
            if (root == null) return 0;
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (face == null || frame == null || buttonFace == null || buttonFrame == null)
                throw new ArgumentException("Painted surface and button face/frame sprites are required.");

            var protectedGraphics = new HashSet<Graphic>();
            var controlBackgrounds = new HashSet<Image>();
            var buttonBackgrounds = new HashSet<Image>();
            var structuralOverrides = new HashSet<Image>();
            var explicitScrims = new HashSet<Image>();
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                string scrimField = view.viewId == "settings" ? "_modal" : view.viewId == "connection" ? "_overlay" :
                    view.viewId == "metrics" ? "_results" : view.viewId == "wipe" || view.viewId == "skill-reward" ? "_root" : null;
                foreach (var reference in view.references)
                {
                    if (reference == null || reference.target == null) continue;
                    if (scrimField != null && reference.path == scrimField)
                    {
                        Image scrim = ReferenceImage(reference.target);
                        if (scrim != null) explicitScrims.Add(scrim);
                    }
                    if (view.viewId == "metrics" && reference.path == "_panel")
                    {
                        Image panel = ReferenceImage(reference.target);
                        if (panel != null) structuralOverrides.Add(panel);
                    }
                    // Death state text and gauges stay semantic. Only this bound panel's
                    // backing surface and its actual button backgrounds are cosmetic.
                    if (view.viewId == "resurrection" && reference.path == "_deathPanel")
                    {
                        Image panel = ReferenceImage(reference.target);
                        if (panel == null) continue;
                        structuralOverrides.Add(panel);
                        foreach (var button in panel.GetComponentsInChildren<Button>(true))
                            if (button.targetGraphic is Image background && !(background is PrismV4Gauge) &&
                                background.type != Image.Type.Filled && !SemanticNames.IsMatch(Words(background.name)))
                                structuralOverrides.Add(background);
                    }
                }
            }
            // The current EnemyList is embedded as a presenter, rather than a RaidUIView.
            // Its private panel field is restored from this explicit binding at runtime.
            foreach (var uiRoot in root.GetComponentsInChildren<RaidUIRoot>(true))
                foreach (var binding in uiRoot.presenters)
                {
                    if (binding == null || !(binding.embeddedPresenter is EnemyListUI) &&
                        !string.Equals(binding.typeName?.Split(',')[0], typeof(EnemyListUI).FullName, StringComparison.Ordinal)) continue;
                    foreach (var reference in binding.references)
                        if (reference != null && reference.path == "_panelRoot" && reference.target != null)
                        {
                            Image panel = ReferenceImage(reference.target);
                            if (panel != null) structuralOverrides.Add(panel);
                        }
                }
            foreach (var icon in root.GetComponentsInChildren<RaidUIIcon>(true))
                if (icon.image != null) protectedGraphics.Add(icon.image);
            foreach (var item in root.GetComponentsInChildren<RaidUIRepeatedItem>(true))
                if (item.fill != null) protectedGraphics.Add(item.fill);
            foreach (var station in root.GetComponentsInChildren<RaidBuildStationView>(true))
            {
                BuildIcon(station.ownRoute, structuralOverrides, protectedGraphics);
                foreach (var choice in station.choices ?? Array.Empty<RaidBuildStationView.Choice>())
                    if (choice != null) BuildIcon(choice.icon, structuralOverrides, protectedGraphics);
                foreach (var member in station.members ?? Array.Empty<RaidBuildStationView.Member>())
                    if (member != null)
                    {
                        if (member.label != null && member.label.transform.parent != null &&
                            member.label.transform.parent.TryGetComponent<Image>(out var rowBackground)) structuralOverrides.Add(rowBackground);
                        foreach (var icon in member.icons ?? Array.Empty<RaidBuildStationView.Icon>()) BuildIcon(icon, structuralOverrides, protectedGraphics);
                    }
                // The runtime writes these presentation colors onto counter-choice backgrounds.
                // Keep equipped/missing meaning, with the selected state in the new accent family.
                station.counterEquippedColor = new Color(theme.accent.r * .42f, theme.accent.g * .42f, theme.accent.b * .42f, station.counterEquippedColor.a);
                station.counterMissingColor = new Color(.10f, .08f, .15f, station.counterMissingColor.a);
            }
            foreach (var minimap in root.GetComponentsInChildren<RaidMinimapUI>(true))
                if (minimap.Panel != null && minimap.Panel.TryGetComponent<Image>(out var panelBackground)) structuralOverrides.Add(panelBackground);
            foreach (var bridge in root.GetComponentsInChildren<AstellaPrismHUDBridge>(true))
                if (bridge.hud != null && bridge.hud.partyRevivalRoot != null &&
                    bridge.hud.partyRevivalRoot.TryGetComponent<Image>(out var revivalBackground)) structuralOverrides.Add(revivalBackground);
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                if (selectable.targetGraphic is Image background)
                {
                    controlBackgrounds.Add(background);
                    if (selectable is Button) buttonBackgrounds.Add(background);
                }
                if (selectable is Toggle toggle && toggle.graphic != null) protectedGraphics.Add(toggle.graphic);
                if (selectable is Slider slider)
                {
                    Protect(slider.fillRect, protectedGraphics);
                    Protect(slider.handleRect, protectedGraphics);
                }
                if (selectable is Scrollbar scrollbar) Protect(scrollbar.handleRect, protectedGraphics);
            }
            foreach (var scroll in root.GetComponentsInChildren<ScrollRect>(true))
                if (scroll.TryGetComponent<Image>(out var background)) controlBackgrounds.Add(background);

            int changed = 0;
            // Snapshot before creating decorative children. A repeat pass skips our own frames.
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (Protected(image, root.transform, protectedGraphics) || image.color.a <= .001f ||
                    image is PrismV4Gauge || image.type == Image.Type.Filled || image.GetComponent<Mask>() != null) continue;
                string name = Words(image.name);
                bool structuralOverride = structuralOverrides.Contains(image);
                bool explicitScrim = explicitScrims.Contains(image);
                if (!structuralOverride && !explicitScrim && SemanticPath(image.transform, root.transform)) continue;

                if (explicitScrim || IsScrim(image))
                {
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    image.color = new Color(.035f, .027f, .07f, image.color.a);
                    var oldFrame = image.transform.Find(FrameName);
                    if (oldFrame != null && oldFrame.TryGetComponent<Image>(out var oldGraphic)) oldGraphic.color = Color.clear;
                    ConfigureExistingStyle(image, theme);
                    changed++;
                    continue;
                }

                Rect rect = image.rectTransform.rect;
                bool divider = name.Contains("divider") || name.Contains("separator") || name.Contains("accent") || name.Contains("header line") ||
                    (rect.width > 8 && rect.height > 0 && rect.height <= 3) || (rect.height > 8 && rect.width > 0 && rect.width <= 3);
                if (divider)
                {
                    image.color = WithAlpha(theme.accent, image.color.a);
                    ConfigureExistingStyle(image, theme);
                    changed++;
                    continue;
                }

                bool control = controlBackgrounds.Contains(image);
                bool button = buttonBackgrounds.Contains(image);
                bool structural = structuralOverride || SurfaceNames.IsMatch(name) || image.gameObject == root ||
                    name == "lan status" || name == "run flow status";
                bool borderOnly = !structuralOverride && !control && (name.Contains("frame") || name.Contains("border") || name.Contains("outline"));
                if (!control && !structural && !borderOnly) continue;
                // Small status pips/scroll handles and arbitrary illustrated images are not panel surfaces.
                if (rect.width > 0 && rect.height > 0 && (rect.width < 24 || rect.height < 16)) continue;
                if (!control && !NeutralSurface(image.color)) continue;

                float alpha = image.color.a;
                image.sprite = borderOnly ? frame : button ? buttonFace : face;
                image.type = Image.Type.Sliced;
                image.fillCenter = !borderOnly;
                image.pixelsPerUnitMultiplier = 2;
                image.color = new Color(1, 1, 1, alpha);
                if (!borderOnly) DecorateFrame(image, button ? buttonFrame : frame, alpha);
                if (structuralOverride)
                    foreach (var outline in image.GetComponents<Outline>()) outline.enabled = false;
                ConfigureExistingStyle(image, theme);
                changed++;
            }

            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (Protected(text, root.transform, protectedGraphics) || SemanticPath(text.transform, root.transform) || text.color.a <= .001f) continue;
                Color old = text.color;
                if (!NeutralText(old)) continue;
                string name = Words(text.name);
                bool secondary = name.Contains("subtitle") || name.Contains("hint") || name.Contains("muted") ||
                    name.Contains("description") || name.Contains("detail") || name.Contains("placeholder");
                Color next = secondary ? Color.Lerp(theme.text, theme.accent, .4f) : theme.text;
                next.a = old.a;
                if (text.color == next) continue;
                text.color = next;
                ConfigureExistingStyle(text, theme);
                changed++;
            }
            return changed;
        }

        private static void DecorateFrame(Image surface, Sprite sprite, float alpha)
        {
            var child = surface.transform.Find(FrameName);
            if (child == null)
            {
                var created = new GameObject(FrameName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                created.layer = surface.gameObject.layer;
                created.transform.SetParent(surface.transform, false);
                child = created.transform;
            }
            var rect = (RectTransform)child;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
            rect.SetAsFirstSibling();
            var image = child.GetComponent<Image>() ?? child.gameObject.AddComponent<Image>();
            image.enabled = true; image.sprite = sprite; image.type = Image.Type.Sliced; image.fillCenter = false;
            image.color = new Color(1, 1, 1, alpha);
            image.pixelsPerUnitMultiplier = 2;
            image.raycastTarget = false;
            var layout = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
        }

        private static void Protect(RectTransform root, HashSet<Graphic> graphics)
        {
            if (root == null) return;
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphics.Add(graphic);
        }

        private static Image ReferenceImage(UnityEngine.Object target)
        {
            if (target is Image image) return image;
            if (target is GameObject gameObject) return gameObject.GetComponent<Image>();
            if (target is Component component) return component.GetComponent<Image>();
            return null;
        }

        private static void BuildIcon(RaidBuildStationView.Icon icon, HashSet<Image> backgrounds, HashSet<Graphic> protectedGraphics)
        {
            if (icon == null) return;
            if (icon.root != null && icon.root.TryGetComponent<Image>(out var background)) backgrounds.Add(background);
            if (icon.art != null) protectedGraphics.Add(icon.art);
            if (icon.fallback != null) protectedGraphics.Add(icon.fallback);
        }

        private static bool Protected(Graphic graphic, Transform root, HashSet<Graphic> graphics)
        {
            if (graphics.Contains(graphic)) return true;
            for (var parent = graphic.transform; parent != null; parent = parent.parent)
            {
                if (parent.name == FrameName || parent.GetComponent<PrismV4Bindings>() != null) return true;
                foreach (var group in parent.GetComponents<CanvasGroup>()) if (group.alpha <= .001f) return true;
                if (parent == root) break;
            }
            return false;
        }

        private static bool SemanticPath(Transform element, Transform root)
        {
            int depth = 0;
            for (var parent = element; parent != null; parent = parent.parent)
            {
                string name = Words(parent.name);
                if (SemanticNames.IsMatch(name)) return true;
                // Named status indicators keep semantic colors; a structural LAN_Status panel remains styleable.
                if (depth == 0 && name.Contains("status") && !SurfaceNames.IsMatch(name) && name != "lan status" && name != "run flow status") return true;
                if (parent == root || ++depth >= 3) break;
            }
            return false;
        }

        private static bool IsScrim(Image image)
        {
            var rect = image.rectTransform;
            bool stretched = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one &&
                rect.offsetMin.sqrMagnitude <= 16 && rect.offsetMax.sqrMagnitude <= 16;
            if (!stretched) return false;
            string name = Words(image.name);
            bool named = name.Contains("overlay") || name.Contains("scrim") || name.Contains("backdrop") || name.Contains("modal") || name.Contains("main menu") || name.Contains("full screen");
            return named || rect.rect.width >= 1000 && rect.rect.height >= 650;
        }

        private static bool NeutralSurface(Color color)
        {
            float minimum = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            float maximum = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            bool dark = maximum <= .55f;
            bool gray = maximum - minimum <= .14f;
            bool gold = color.r >= color.g && color.g >= color.b && color.r - color.g < .25f && color.g - color.b < .35f;
            return dark || gray || gold;
        }

        private static bool NeutralText(Color color)
        {
            float minimum = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            float maximum = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (maximum < .35f) return false;
            bool gray = maximum - minimum <= .14f;
            bool mutedBlue = color.r <= color.g && color.g <= color.b && maximum - minimum <= .25f;
            bool gold = color.r >= color.g && color.g >= color.b && color.r - color.g < .25f && color.g - color.b < .35f;
            return gray || mutedBlue || gold;
        }

        private static void ConfigureExistingStyle(Graphic graphic, RaidUITheme theme)
        {
            // Do not add RaidUIStyle: its presence opts out of existing runtime Color(...) writes.
            var style = graphic.GetComponent<RaidUIStyle>();
            if (style == null || style.graphic != null && style.graphic != graphic) return;
            style.themeOverride = theme;
            style.useTheme = false;
            // ready/unavailable/selected/locked/useStateColors and all component references stay authored.
        }

        private static string Words(string name)
        {
            name = Regex.Replace(name ?? "", "([A-Z]+)([A-Z][a-z])", "$1 $2");
            name = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
            return name.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        }

        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
    }
}
#endif
