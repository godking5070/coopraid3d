#if UNITY_EDITOR
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    public static partial class AstellaPrismUXRevision
    {
        static readonly Color AuxInk = new Color(.035f, .045f, .065f, .99f);
        static readonly Color AuxSheet = new Color(.065f, .075f, .1f, 1);
        static readonly Color AuxAccent = new Color(.64f, .52f, .84f, 1);
        static readonly Color AuxMuted = new Color(.59f, .63f, .7f, 1);

        static void RedesignAuxiliary(GameObject root, Font heading, Font body)
        {
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if (view.viewId == "metrics")
                {
                    var box = AuxReference<RectTransform>(view, "_resultBox");
                    AuxFlat(box, AuxInk);
                    AuxRule(box, "UX Result Accent", new Vector2(.5f, 1), new Vector2(0, -1), new Vector2(box.sizeDelta.x, 2), AuxAccent);
                    foreach (var button in view.GetComponentsInChildren<Button>(true)) AuxButton(button, false);
                    for (int column = 0; column < 10; column++)
                    {
                        var label = box.Find("Column" + column)?.GetComponent<Text>();
                        if (label != null) { label.color = AuxMuted; AuxFreeze(label); }
                    }
                    for (int row = 0; row < 4; row++)
                    {
                        var label = AuxReference<Text>(view, "_resultRows/[" + row + "]/[0]");
                        if (label == null) continue;
                        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(box, label.rectTransform);
                        AuxRule(box, "UX Result Row " + row, new Vector2(.5f, .5f),
                            new Vector2(0, bounds.min.y - 8), new Vector2(box.sizeDelta.x - 48, 1), new Color(.17f, .19f, .24f, 1));
                    }
                }
                else if (view.viewId == "wipe")
                {
                    var screen = AuxReference<RectTransform>(view, "_root");
                    AuxFlat(screen, new Color(.035f, .025f, .035f, .97f));
                    var title = AuxReference<Text>(view, "_title");
                    if (title != null) { title.fontSize = 48; title.color = new Color(.9f, .85f, .85f, 1); }
                    AuxRule(screen, "UX Wipe Rule", new Vector2(.5f, .5f), new Vector2(0, 62), new Vector2(64, 2), new Color(.62f, .33f, .38f, 1));
                    foreach (var button in view.GetComponentsInChildren<Button>(true)) AuxButton(button, false);
                }
                else if (view.viewId == "resurrection")
                {
                    var panel = AuxReference<RectTransform>(view, "_deathPanel");
                    if (panel != null)
                    {
                        AuxFlat(panel, AuxInk);
                        AuxRule(panel, "UX Spectator Accent", new Vector2(0, .5f), new Vector2(1, 0), new Vector2(2, panel.sizeDelta.y), AuxAccent);
                        foreach (var button in panel.GetComponentsInChildren<Button>(true)) AuxButton(button, false);
                    }
                }
            }
            foreach (var hud in root.GetComponentsInChildren<AstellaOrnateHUD>(true))
            {
                if (hud.tooltipRoot == null) continue;
                // Keep the approved root position, width and text padding. Retire only surface decoration.
                foreach (var image in hud.tooltipRoot.GetComponentsInChildren<Image>(true))
                {
                    AuxFreeze(image);
                    if (image.name == "Panel") { image.sprite = null; image.type = Image.Type.Simple; image.color = AuxInk; }
                    else if (image.name == AstellaPrismSurfaceStyler.FrameName || image.name.Contains("Filigree") || image.name.Contains("Ornament")) image.color = Color.clear;
                }
            }
            foreach (var station in root.GetComponentsInChildren<RaidBuildStationView>(true))
            {
                AuxFlat(station.transform, AuxInk);
                foreach (var member in station.members)
                {
                    var row = member.label != null ? member.label.transform.parent : null;
                    AuxFlat(row, new Color(.055f, .065f, .085f, 1));
                    if (row is RectTransform rect)
                        AuxRule(rect, "UX Party Divider", new Vector2(.5f, 0), new Vector2(0, 0), new Vector2(rect.sizeDelta.x, 1), new Color(.15f, .17f, .22f, 1));
                }
                foreach (var choice in station.choices) AuxButton(choice.button, false);
                AuxButton(station.routeChange, false); AuxButton(station.close, false);
                // Skill artwork and fallback glyphs retain the existing owner and colors.
                var width = ((RectTransform)station.transform).sizeDelta.x;
                AuxRule(station.transform, "UX Station Accent", new Vector2(.5f, 1), Vector2.zero, new Vector2(width, 2), AuxAccent);
                AuxRule(station.transform, "UX Station Partition", new Vector2(.5f, .5f), new Vector2(-208, -10), new Vector2(1, 560), new Color(.15f, .17f, .22f, 1));
            }
        }

        static void RepairResultRules(GameObject root)
        {
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true).Where(view => view.viewId == "metrics"))
            {
                var box = AuxReference<RectTransform>(view, "_resultBox");
                for (int row = 0; row < 4; row++)
                {
                    var label = AuxReference<Text>(view, "_resultRows/[" + row + "]/[0]");
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(box, label.rectTransform);
                    AuxRule(box, "UX Result Row " + row, new Vector2(.5f, .5f), new Vector2(0, bounds.min.y - 8),
                        new Vector2(box.sizeDelta.x - 48, 1), new Color(.17f, .19f, .24f, 1));
                }
            }
        }

        static T AuxReference<T>(RaidUIView view, string path) where T : Object
        {
            var value = view.references.FirstOrDefault(reference => reference.path == path)?.target;
            if (value is T typed) return typed;
            var owner = value as GameObject ?? (value as Component)?.gameObject;
            return owner != null && typeof(Component).IsAssignableFrom(typeof(T)) ? owner.GetComponent(typeof(T)) as T : null;
        }
        static void AuxFreeze(Graphic graphic)
        { foreach (var style in graphic.GetComponents<RaidUIStyle>()) style.useTheme = false; }
        static void AuxFlat(Transform transform, Color color)
        {
            if (transform == null) return;
            var surface = transform.GetComponent<Image>();
            if (surface != null) { AuxFreeze(surface); surface.sprite = null; surface.type = Image.Type.Simple; surface.color = color; }
            foreach (var image in transform.GetComponentsInChildren<Image>(true))
                if (image.name == AstellaPrismSurfaceStyler.FrameName) { image.color = Color.clear; AuxFreeze(image); }
        }
        static void AuxButton(Button button, bool primary)
        {
            if (button == null) return;
            AuxFlat(button.transform, primary ? new Color(.28f, .22f, .4f, 1) : AuxSheet);
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.2f, 1.2f, 1.3f, 1);
            colors.selectedColor = new Color(1.2f, 1.2f, 1.3f, 1); colors.pressedColor = new Color(.8f, .8f, .9f, 1);
            colors.disabledColor = new Color(.5f, .5f, .55f, .55f); button.colors = colors;
        }
        static void AuxRule(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var found = parent.Find(name);
            var rect = found as RectTransform;
            if (rect == null)
            {
                rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
            }
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var graphic = rect.GetComponent<Image>(); graphic.color = color; graphic.raycastTarget = false;
            rect.SetAsFirstSibling();
        }
    }
}
#endif
