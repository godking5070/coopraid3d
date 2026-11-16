#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    public static partial class AstellaPrismUXRevision
    {
        private static readonly Color ChoiceInk = new Color(.91f, .91f, .94f, 1);
        private static readonly Color ChoiceMuted = new Color(.58f, .62f, .70f, 1);
        private static readonly Color ChoiceAccent = new Color(.66f, .55f, .88f, 1);
        private static readonly Color ChoiceSurface = new Color(.055f, .066f, .086f, 1);

        internal static void RedesignChoices(GameObject root, Font heading, Font body)
        {
            if (root == null || heading == null || body == null) throw new ArgumentNullException(nameof(root));
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if (view.viewId == "origin") ChoiceOrigin(view, heading, body);
                if (view.viewId == "skill-reward") ChoiceSkillReward(view, heading, body);
            }
            foreach (var ui in root.GetComponentsInChildren<RaidUIRoot>(true))
                foreach (var presenter in ui.presenters.Where(binding => binding.typeName?.Split(',')[0] == typeof(RoguelikeCardManager).FullName))
                    ChoiceCardReward(presenter, heading, body);
        }

        private static void ChoiceOrigin(RaidUIView view, Font heading, Font body)
        {
            var panel = ChoiceBound<RectTransform>(view, "_panel");
            ChoiceRect(panel, Vector2.zero, new Vector2(1280, 760));
            ChoiceRetireFrames(view.transform);
            // This screen uses an open roster and one detail column rather than twelve description boxes.
            foreach (var image in view.GetComponents<Image>()) ChoiceFlat(image, new Color(.022f, .030f, .047f, .985f));
            var title = ChoiceBound<Text>(view, "_heading");
            ChoiceRect(title.rectTransform, new Vector2(0, 323), new Vector2(1180, 64));
            ChoiceText(title, heading, 34, TextAnchor.MiddleLeft, ChoiceInk, true);
            var subtitle = panel.GetComponentsInChildren<Text>(true).Single(text => text.transform.parent == panel && text != title &&
                text != ChoiceBound<Text>(view, "_status") && text.rectTransform.anchoredPosition.y > 150);
            ChoiceRect(subtitle.rectTransform, new Vector2(0, 267), new Vector2(1180, 42));
            ChoiceText(subtitle, body, 17, TextAnchor.MiddleLeft, ChoiceMuted);
            subtitle.text = "직업과 기원을 살펴보세요. 선택한 내용은 출발 전까지 변경할 수 있습니다.";
            ChoiceLine(panel, "UX Choice Header Rule", new Vector2(0, 232), new Vector2(1180, 1), new Color(.25f, .29f, .36f, 1));

            var details = view.GetComponent<AstellaJobSelectionDetails>() ?? view.gameObject.AddComponent<AstellaJobSelectionDetails>();
            details.jobSelectedMarkers = new GameObject[12]; details.routeSelectedMarkers = new GameObject[3];
            for (int index = 0; index < 12; index++)
            {
                var button = ChoiceBound<Button>(view, "_choices/[" + index + "]");
                ChoiceRect((RectTransform)button.transform, new Vector2(-498 + index % 3 * 194, 166 - index / 3 * 106), new Vector2(176, 88));
                ChoiceButton(button, ChoiceSurface);
                var label = ChoiceBound<Text>(view, "_choiceLabels/[" + index + "]");
                ChoiceRect(label.rectTransform, new Vector2(0, 0), new Vector2(144, 64));
                ChoiceText(label, body, 17, TextAnchor.MiddleLeft, ChoiceInk);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 15; label.resizeTextMaxSize = 17;
                ChoiceLine(button.transform, "UX Roster Baseline", new Vector2(0, -43), new Vector2(176, 1), new Color(.20f, .23f, .30f, 1));
                details.jobSelectedMarkers[index] = ChoiceMarker(button.transform, "UX Job Selected", new Vector2(176, 88));
            }

            var detailRoot = ChoiceNode(panel, "UX Job Details", new Vector2(320, -4), new Vector2(520, 432));
            details.detailsRoot = detailRoot.gameObject;
            ChoiceLine(detailRoot, "UX Details Separator", new Vector2(-285, 0), new Vector2(1, 432), new Color(.25f, .29f, .36f, 1));
            var detailKicker = ChoiceLabel(detailRoot, "UX Details Kicker", "선택한 직업", new Vector2(0, 199), new Vector2(490, 28), body, 15, ChoiceAccent);
            detailKicker.alignment = TextAnchor.MiddleLeft;
            details.titleText = ChoiceLabel(detailRoot, "UX Details Name", "", new Vector2(0, 137), new Vector2(490, 74), heading, 38, ChoiceInk, true);
            details.roleText = ChoiceLabel(detailRoot, "UX Details Role", "", new Vector2(0, 73), new Vector2(490, 36), body, 20, ChoiceAccent);
            details.descriptionText = ChoiceLabel(detailRoot, "UX Details Description", "", new Vector2(0, -69), new Vector2(490, 210), body, 20, ChoiceInk);
            details.descriptionText.alignment = TextAnchor.UpperLeft; details.descriptionText.lineSpacing = 1.35f;
            details.descriptionText.resizeTextForBestFit = true; details.descriptionText.resizeTextMinSize = 18; details.descriptionText.resizeTextMaxSize = 20;
            ChoiceLabel(detailRoot, "UX Details Next", "선택 확정 후 기원을 고릅니다", new Vector2(0, -200), new Vector2(490, 28), body, 15, ChoiceMuted);

            for (int index = 0; index < 3; index++)
            {
                var button = ChoiceBound<Button>(view, "_routeChoices/[" + index + "]");
                ChoiceRect((RectTransform)button.transform, new Vector2((index - 1) * 402, -6), new Vector2(378, 426));
                ChoiceButton(button, new Color(.060f, .073f, .093f, 1));
                ChoiceLabel(button.transform, "UX Origin Number", "기원 " + (index + 1).ToString("00"), new Vector2(0, 174), new Vector2(326, 30), body, 15, ChoiceAccent);
                ChoiceLine(button.transform, "UX Origin Rule", new Vector2(0, 146), new Vector2(326, 1), new Color(.28f, .31f, .39f, 1));
                var label = ChoiceBound<Text>(view, "_routeLabels/[" + index + "]");
                ChoiceRect(label.rectTransform, new Vector2(0, -14), new Vector2(326, 276));
                ChoiceText(label, body, 20, TextAnchor.UpperLeft, ChoiceInk);
                label.lineSpacing = 1.25f; label.resizeTextForBestFit = true; label.resizeTextMinSize = 18; label.resizeTextMaxSize = 20;
                details.routeSelectedMarkers[index] = ChoiceMarker(button.transform, "UX Origin Selected", new Vector2(378, 426));
            }
            var status = ChoiceBound<Text>(view, "_status");
            ChoiceRect(status.rectTransform, new Vector2(-170, -282), new Vector2(840, 42));
            ChoiceText(status, body, 17, TextAnchor.MiddleLeft, ChoiceMuted);
            foreach (var action in view.actions.Where(action => action.id == "back" || action.id == "finish"))
            {
                bool primary = action.id == "finish";
                ChoiceRect((RectTransform)action.button.transform, new Vector2(primary ? 402 : -438, -335), new Vector2(primary ? 378 : 304, 54));
                ChoiceButton(action.button, primary ? new Color(.80f, .77f, .87f, 1) : new Color(.09f, .10f, .14f, 1));
                var caption = action.button.GetComponentInChildren<Text>(true);
                ChoiceRect(caption.rectTransform, Vector2.zero, new Vector2(primary ? 334 : 260, 42));
                ChoiceText(caption, body, 20, TextAnchor.MiddleCenter, primary ? new Color(.045f, .044f, .065f, 1) : ChoiceInk);
            }
        }

        private static void ChoiceSkillReward(RaidUIView view, Font heading, Font body)
        {
            ChoiceRetireFrames(view.transform);
            var scrim = ChoiceBound<RectTransform>(view, "_root");
            ChoiceFlat(scrim.GetComponent<Image>(), new Color(.018f, .023f, .035f, .985f));
            var panel = ChoiceBound<RectTransform>(view, "_panel");
            ChoiceRect(panel, Vector2.zero, new Vector2(1240, 760));
            var header = ChoiceBound<Text>(view, "_header");
            ChoiceRect(header.rectTransform, new Vector2(0, 320), new Vector2(1140, 66));
            ChoiceText(header, heading, 32, TextAnchor.MiddleLeft, ChoiceInk, true);
            ChoiceLabel(panel, "UX Skill Reward Kicker", "이번 원정에 사용할 스킬을 확정하세요", new Vector2(0, 264), new Vector2(1140, 34), body, 17, ChoiceMuted);
            ChoiceLine(panel, "UX Skill Reward Header Rule", new Vector2(0, 233), new Vector2(1140, 1), new Color(.25f, .29f, .36f, 1));
            var deck = ChoiceNode(panel, "UX Skill Choice Deck", new Vector2(0, -4), new Vector2(1160, 426));
            var layout = deck.GetComponent<HorizontalLayoutGroup>() ?? deck.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter; layout.spacing = 24;
            layout.childControlWidth = false; layout.childControlHeight = false;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            for (int index = 0; index < 3; index++)
            {
                var button = ChoiceBound<Button>(view, "_buttons/[" + index + "]");
                button.transform.SetParent(deck, false);
                ChoiceRect((RectTransform)button.transform, Vector2.zero, new Vector2(366, 426));
                ChoiceButton(button, new Color(.84f, .85f, .87f, 1));
                ChoiceLine(button.transform, "UX Skill Folio Binding", new Vector2(-174, 0), new Vector2(2, 426), new Color(.42f, .44f, .52f, 1));
                ChoiceLine(button.transform, "UX Skill Folio Accent", new Vector2(0, 205), new Vector2(366, 3), ChoiceAccent);
                var label = ChoiceBound<Text>(view, "_labels/[" + index + "]");
                ChoiceRect(label.rectTransform, new Vector2(0, 0), new Vector2(314, 364));
                ChoiceText(label, body, 19, TextAnchor.UpperLeft, new Color(.075f, .085f, .12f, 1));
                label.lineSpacing = 1.25f; label.resizeTextForBestFit = true; label.resizeTextMinSize = 17; label.resizeTextMaxSize = 19;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(deck);
            var status = ChoiceBound<Text>(view, "_status");
            ChoiceRect(status.rectTransform, new Vector2(0, -274), new Vector2(1140, 70));
            ChoiceText(status, body, 17, TextAnchor.MiddleLeft, ChoiceMuted);
            var menu = view.actions.Single(action => action.id == "menu").button;
            ChoiceRect((RectTransform)menu.transform, new Vector2(431, -340), new Vector2(278, 46));
            ChoiceButton(menu, new Color(.075f, .086f, .11f, 1));
            var caption = menu.GetComponentInChildren<Text>(true);
            ChoiceRect(caption.rectTransform, Vector2.zero, new Vector2(254, 36));
            ChoiceText(caption, body, 17, TextAnchor.MiddleCenter, ChoiceMuted);
        }

        private static void ChoiceCardReward(RaidUIRoot.PresenterBinding presenter, Font heading, Font body)
        {
            var modal = presenter.references.Single(reference => reference.path == "cardSelectionModalRoot").target as GameObject;
            if (modal == null) throw new InvalidOperationException("Card reward modal binding is missing.");
            ChoiceRetireFrames(modal.transform);
            ChoiceFlat(modal.GetComponent<Image>(), new Color(.014f, .017f, .028f, .97f));
            var header = presenter.references.Single(reference => reference.path == "modalHeaderText").target as Text;
            ChoiceRect(header.rectTransform, new Vector2(0, 327), new Vector2(1180, 112));
            ChoiceText(header, heading, 30, TextAnchor.MiddleCenter, ChoiceInk, true);
            ChoiceLine(modal.transform, "UX Reward Header Rule", new Vector2(0, 245), new Vector2(1140, 1), new Color(.29f, .25f, .38f, 1));
            for (int index = 0; index < 3; index++)
            {
                var button = presenter.references.Single(reference => reference.path == "cardButtons/[" + index + "]").target as Button;
                var title = presenter.references.Single(reference => reference.path == "cardTitleTexts/[" + index + "]").target as Text;
                var description = presenter.references.Single(reference => reference.path == "cardDescTexts/[" + index + "]").target as Text;
                var banner = presenter.references.Single(reference => reference.path == "cardAccentBanners/[" + index + "]").target as Image;
                ChoiceRect((RectTransform)button.transform, new Vector2((index - 1) * 390, -10), new Vector2(356, 444));
                ChoiceButton(button, new Color(.075f, .060f, .105f, 1));
                ChoiceRect(banner.rectTransform, new Vector2(0, 219), new Vector2(356, 5));
                banner.rectTransform.pivot = new Vector2(.5f, .5f);
                ChoiceFlat(banner, ChoiceAccent);
                ChoiceRect(title.rectTransform, new Vector2(0, 138), new Vector2(300, 106));
                ChoiceText(title, heading, 27, TextAnchor.UpperLeft, ChoiceInk, true);
                title.lineSpacing = 1.2f;
                ChoiceLine(button.transform, "UX Reward Title Rule", new Vector2(0, 67), new Vector2(300, 1), new Color(.27f, .23f, .34f, 1));
                ChoiceRect(description.rectTransform, new Vector2(0, -75), new Vector2(300, 260));
                ChoiceText(description, body, 19, TextAnchor.UpperLeft, new Color(.82f, .82f, .87f, 1));
                description.lineSpacing = 1.25f; description.resizeTextForBestFit = true;
                description.resizeTextMinSize = 17; description.resizeTextMaxSize = 19;
            }
            foreach (var button in modal.GetComponentsInChildren<Button>(true))
            {
                if (presenter.references.Any(reference => reference.path.StartsWith("cardButtons/[", StringComparison.Ordinal) && reference.target == button)) continue;
                ChoiceButton(button, new Color(.075f, .080f, .10f, 1));
                foreach (var text in button.GetComponentsInChildren<Text>(true)) ChoiceText(text, body, text.fontSize, text.alignment, ChoiceMuted);
            }
        }

        private static T ChoiceBound<T>(RaidUIView view, string path) where T : UnityEngine.Object =>
            view.references.FirstOrDefault(reference => reference.path == path)?.target as T
                ?? throw new InvalidOperationException("Missing choice binding: " + view.viewId + "/" + path);

        private static void ChoiceRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
        private static RectTransform ChoiceNode(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var child = parent.Find(name) as RectTransform;
            if (child == null) { child = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); child.SetParent(parent, false); child.gameObject.layer = parent.gameObject.layer; }
            ChoiceRect(child, position, size); return child;
        }
        private static void ChoiceFlat(Image image, Color color)
        {
            if (image == null) return;
            image.sprite = null; image.type = Image.Type.Simple; image.color = color;
            foreach (var effect in image.GetComponents<BaseMeshEffect>()) effect.enabled = false;
            foreach (var style in image.GetComponents<RaidUIStyle>()) style.useTheme = false;
        }
        private static void ChoiceRetireFrames(Transform parent)
        {
            foreach (var frame in parent.GetComponentsInChildren<Image>(true).Where(image => image.name == "Prism Surface Frame"))
            {
                frame.color = Color.clear;
                foreach (var style in frame.GetComponents<RaidUIStyle>()) style.useTheme = false;
            }
        }
        private static void ChoiceButton(Button button, Color background)
        {
            ChoiceFlat(button.targetGraphic as Image, background);
            ChoiceRetireFrames(button.transform);
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.17f, 1.13f, 1.25f, 1);
            colors.pressedColor = new Color(.75f, .68f, .88f, 1); colors.selectedColor = new Color(1.12f, 1.08f, 1.20f, 1);
            colors.disabledColor = new Color(.45f, .45f, .50f, .5f); colors.fadeDuration = .12f;
            button.colors = colors;
        }
        private static void ChoiceText(Text text, Font font, int size, TextAnchor anchor, Color color, bool title = false)
        {
            text.font = font; text.fontStyle = FontStyle.Normal; text.fontSize = size; text.alignment = anchor; text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.lineSpacing = 1.15f;
            text.resizeTextForBestFit = false;
            foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
            foreach (var effect in text.GetComponents<BaseMeshEffect>()) effect.enabled = title && effect is Outline;
            if (title)
            {
                var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
                outline.enabled = true; outline.effectColor = new Color(.015f, .013f, .030f, .9f); outline.effectDistance = new Vector2(1, -1); outline.useGraphicAlpha = true;
            }
        }
        private static Text ChoiceLabel(Transform parent, string name, string value, Vector2 position, Vector2 size, Font font, int fontSize, Color color, bool title = false)
        {
            var rect = ChoiceNode(parent, name, position, size);
            var text = rect.GetComponent<Text>() ?? rect.gameObject.AddComponent<Text>();
            text.raycastTarget = false; text.text = value; ChoiceText(text, font, fontSize, TextAnchor.MiddleLeft, color, title); return text;
        }
        private static Image ChoiceLine(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var rect = ChoiceNode(parent, name, position, size);
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false; ChoiceFlat(image, color); return image;
        }
        private static GameObject ChoiceMarker(Transform parent, string name, Vector2 size)
        {
            var rect = ChoiceNode(parent, name, Vector2.zero, size); rect.SetAsFirstSibling();
            ChoiceLine(rect, "UX Selected Edge", new Vector2(-size.x * .5f + 2, 0), new Vector2(4, size.y), ChoiceAccent);
            ChoiceLine(rect, "UX Selected Baseline", new Vector2(0, -size.y * .5f + 1), new Vector2(size.x, 2), ChoiceAccent);
            rect.gameObject.SetActive(false); return rect.gameObject;
        }
    }
}
#endif
