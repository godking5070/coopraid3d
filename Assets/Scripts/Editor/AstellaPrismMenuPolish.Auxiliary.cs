#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    public static partial class AstellaPrismMenuPolish
    {
        // Called only on the migration's copied HUD/views. No actions or runtime values are changed.
        internal static void PolishAuxiliary(GameObject root, Font headingFont, Font bodyFont, Font numericFont)
        {
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                switch (view.viewId)
                {
                    case "metrics": AuxMetrics(view, headingFont, bodyFont, numericFont); break;
                    case "skill-reward": AuxSkillReward(view, headingFont, bodyFont); break;
                    case "wipe":
                        AuxHeading(AuxBound<Text>(view, "_title"), headingFont, 40);
                        AuxBody(AuxBound<Text>(view, "_details"), bodyFont, 22);
                        break;
                    case "resurrection":
                        AuxBody(AuxBound<Text>(view, "_deathText"), bodyFont, 18);
                        break;
                    case "character":
                        var tooltip = AuxBound<Text>(view, "_tooltip");
                        AuxBody(tooltip, bodyFont, 16);
                        if (tooltip != null) tooltip.alignment = TextAnchor.UpperLeft;
                        break;
                }
            }
            foreach (var hud in root.GetComponentsInChildren<AstellaOrnateHUD>(true))
            {
                AuxBody(hud.tooltipText, bodyFont, 18);
                if (hud.tooltipText == null) continue;
                hud.tooltipText.alignment = TextAnchor.UpperLeft;
                hud.tooltipText.resizeTextForBestFit = true;
                hud.tooltipText.resizeTextMinSize = 16; hud.tooltipText.resizeTextMaxSize = 18;
                var rect = hud.tooltipText.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, .5f);
                // The painted frame is 112 units high inside the 150-unit tooltip root.
                rect.offsetMin = new Vector2(22, 32); rect.offsetMax = new Vector2(-22, -32);
            }
            foreach (var hud in root.GetComponentsInChildren<RaidUIRoot>(true))
                foreach (var presenter in hud.presenters.Where(binding =>
                    binding.typeName?.Split(',')[0] == typeof(RoguelikeCardManager).FullName))
                    foreach (var reference in presenter.references)
                    {
                        if (reference.target is not Text text) continue;
                        if (reference.path == "modalHeaderText") AuxHeading(text, headingFont, 30);
                        else if (reference.path.StartsWith("cardTitleTexts/[", StringComparison.Ordinal))
                            AuxHeading(text, headingFont, 25);
                        else if (reference.path.StartsWith("cardDescTexts/[", StringComparison.Ordinal))
                        {
                            AuxBody(text, bodyFont, 18);
                            text.alignment = TextAnchor.UpperLeft;
                            text.lineSpacing = 1.2f;
                            text.resizeTextForBestFit = true;
                            text.resizeTextMinSize = 16; text.resizeTextMaxSize = 18;
                        }
                    }
            foreach (var station in root.GetComponentsInChildren<RaidBuildStationView>(true))
            {
                station.ValidateBindings();
                if (station.title.text == "✦ 여명의 성소 · 전투 준비 ✦")
                    station.title.text = "여명의 성소 · 전투 준비";
                AuxHeading(station.title, headingFont, 24);
                AuxHeading(station.partyTitle, headingFont, 20);
                AuxHeading(station.localHeading, headingFont, 20);
                AuxBody(station.stage, bodyFont, 18);
                AuxBody(station.counterSummary, bodyFont, 15);
                AuxBody(station.tooltip, bodyFont, 14);
                station.tooltip.alignment = TextAnchor.UpperLeft;
                foreach (var member in station.members) AuxBody(member.label, bodyFont, 14);
                foreach (var choice in station.choices) AuxBody(choice.label, bodyFont, 13);
            }
        }

        private static void AuxMetrics(RaidUIView view, Font headingFont, Font bodyFont, Font numericFont)
        {
            var box = AuxBound<RectTransform>(view, "_resultBox");
            if (box == null) throw new InvalidOperationException("Metrics result box binding is missing.");
            // Events ended at y=545 while Close began at y=521 in the 590px panel.
            // Leave every row/button anchored as authored and provide the missing vertical space.
            if (box.sizeDelta.y < 640) box.sizeDelta = new Vector2(box.sizeDelta.x, 640);
            AuxHeading(AuxBound<Text>(view, "_resultTitle"), headingFont, 30);
            AuxBody(AuxBound<Text>(view, "_resultTime"), bodyFont, 18);
            AuxBody(AuxBound<Text>(view, "_resultEvents"), bodyFont, 15);
            for (int column = 0; column < 10; column++)
            {
                var header = box.Find("Column" + column)?.GetComponent<Text>();
                if (header == null) throw new InvalidOperationException("Metrics column heading missing: " + column);
                AuxBody(header, bodyFont, 15);
                header.color = new Color(.86f, .82f, .96f, 1);
                header.alignment = TextAnchor.MiddleCenter;
                header.rectTransform.sizeDelta = new Vector2(header.rectTransform.sizeDelta.x, 38);
            }
            foreach (var reference in view.references)
            {
                var cell = Regex.Match(reference.path, @"^_resultRows/\[\d+\]/\[(\d+)\]$");
                if (!cell.Success || reference.target is not Text text) continue;
                AuxBody(text, bodyFont, 16);
                if (int.Parse(cell.Groups[1].Value) == 0) text.alignment = TextAnchor.MiddleLeft;
                else
                {
                    if (numericFont != null) text.font = numericFont;
                    text.fontStyle = FontStyle.Normal;
                    text.alignment = TextAnchor.MiddleRight;
                    var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(19 / 255f, 16 / 255f, 34 / 255f, 1);
                    outline.effectDistance = new Vector2(1.2f, -1.2f);
                    outline.useGraphicAlpha = true; outline.enabled = true;
                }
            }
        }

        private static void AuxSkillReward(RaidUIView view, Font headingFont, Font bodyFont)
        {
            AuxHeading(AuxBound<Text>(view, "_header"), headingFont, 26);
            AuxBody(AuxBound<Text>(view, "_status"), bodyFont, 17);
            for (int index = 0; index < 3; index++)
            {
                var text = AuxBound<Text>(view, "_labels/[" + index + "]");
                if (text == null) throw new InvalidOperationException("Skill reward description binding missing: " + index);
                AuxBody(text, bodyFont, 18);
                text.alignment = TextAnchor.UpperLeft; text.lineSpacing = 1.15f;
                text.resizeTextForBestFit = true; text.resizeTextMinSize = 16; text.resizeTextMaxSize = 18;
            }
        }

        private static T AuxBound<T>(RaidUIView view, string path) where T : UnityEngine.Object =>
            view.references.FirstOrDefault(reference => reference.path == path)?.target as T;

        private static void AuxHeading(Text text, Font font, int size)
        {
            if (text == null) return;
            if (font != null) text.font = font;
            text.fontStyle = FontStyle.Normal; text.fontSize = size;
            text.color = new Color(.94f, .92f, .97f, 1);
            var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(19 / 255f, 16 / 255f, 34 / 255f, 1);
            float distance = size >= 30 ? 1.3f : 1f;
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = true; outline.enabled = true;
            foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
        }

        private static void AuxBody(Text text, Font font, int size)
        {
            if (text == null) return;
            if (font != null) text.font = font;
            text.fontStyle = FontStyle.Normal; text.fontSize = size;
            text.color = new Color(.94f, .92f, .97f, 1);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 1.1f;
            foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
        }
    }
}
#endif
