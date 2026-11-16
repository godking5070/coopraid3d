#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    // Called only by the explicit authoring migration on preserved UI copies.
    public static partial class AstellaPrismMenuPolish
    {
        internal static void EnsureSelectionControls(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true).Where(view => view.viewId == "settings"))
            {
                var oldActions = view.actions.Select(action => (action.id, action.button)).ToArray();
                if (!view.actions.Any(action => action.id == "HUDLayout"))
                {
                    var template = view.actions.Single(action => action.id == "Apply").button;
                    if (template == null) throw new InvalidOperationException("Missing Apply button template.");
                    var button = SelectionCloneButton(template, template.transform.parent, "HUDLayout", "HUD 배치 편집");
                    view.actions.Add(new RaidUIView.ActionReference { id = "HUDLayout", button = button });
                }
                else if (view.actions.Single(action => action.id == "HUDLayout").button == null)
                    throw new InvalidOperationException("The existing HUDLayout action has no button.");
                var form = view.GetComponentsInChildren<RaidUIForm>(true).Single(item => item.id == "Controls.controls");
                var oldControls = form.controls.Select(control => (control.id, control.reference)).ToArray();
                var parent = (RectTransform)form.transform;
                var labelTemplate = form.Find<Text>("label.binding.Potion");
                var primaryTemplate = form.Find<Button>("button.binding.Potion.0");
                var secondaryTemplate = form.Find<Button>("button.binding.Potion.1");
                var clearTemplate = form.Find<Button>("button.clear.Potion");
                if (labelTemplate == null || primaryTemplate == null || secondaryTemplate == null || clearTemplate == null)
                    throw new InvalidOperationException("Missing complete settings binding row template.");
                // Exactly the two compatibility shifts performed by ExtendControlsForm,
                // before adding any objects that already use the current row coordinates.
                if (form.Find<Button>("button.row.이동 방식") == null)
                {
                    foreach (Transform child in parent)
                        if (child is RectTransform rect) rect.anchoredPosition += new Vector2(0, -90);
                    var label = SelectionCloneLabel(labelTemplate, parent, "MovementModeLabel", "이동 방식");
                    SelectionRect(label.rectTransform, new Vector2(-205, -5), new Vector2(350, 40));
                    label.fontSize = 20;
                    var button = SelectionCloneButton(primaryTemplate, parent, "MovementMode", "우클릭 이동");
                    SelectionRect((RectTransform)button.transform, new Vector2(205, -25), new Vector2(365, 36));
                    form.Remember("label.이동 방식", label); form.Remember("button.row.이동 방식", button);
                }
                if (form.Find<Button>("button.binding.Interact.0") == null)
                {
                    bool afterInteraction = false;
                    var moved = new HashSet<RectTransform>();
                    foreach (var definition in LocalBindingRules.Actions)
                    {
                        if (definition.Id == "Interact") { afterInteraction = true; continue; }
                        if (!afterInteraction) continue;
                        foreach (string key in new[] { "label.binding." + definition.Id, "button.binding." + definition.Id + ".0",
                            "button.binding." + definition.Id + ".1", "button.clear." + definition.Id })
                        {
                            var rect = form.Find<Component>(key)?.transform as RectTransform;
                            if (rect != null && moved.Add(rect)) rect.anchoredPosition += new Vector2(0, -43);
                        }
                    }
                    int index = Array.FindIndex(LocalBindingRules.Actions, action => action.Id == "Interact");
                    if (index < 0) throw new InvalidOperationException("The Interact binding definition is missing.");
                    float y = 175 + index * 43;
                    var label = SelectionCloneLabel(labelTemplate, parent, "InteractLabel", LocalBindingRules.Actions[index].Name);
                    SelectionRect(label.rectTransform, new Vector2(-230, -y + 16), new Vector2(330, 34));
                    var primary = SelectionCloneButton(primaryTemplate, parent, "InteractPrimary", "G");
                    var secondary = SelectionCloneButton(secondaryTemplate, parent, "InteractSecondary", "");
                    var clear = SelectionCloneButton(clearTemplate, parent, "InteractClear", "×");
                    SelectionRect((RectTransform)primary.transform, new Vector2(35, -y), new Vector2(170, 36));
                    SelectionRect((RectTransform)secondary.transform, new Vector2(225, -y), new Vector2(170, 36));
                    SelectionRect((RectTransform)clear.transform, new Vector2(355, -y), new Vector2(60, 36));
                    form.Remember("label.binding.Interact", label); form.Remember("button.binding.Interact.0", primary);
                    form.Remember("button.binding.Interact.1", secondary); form.Remember("button.clear.Interact", clear);
                }
                foreach (string key in new[] { "label.이동 방식", "button.row.이동 방식", "label.binding.Interact",
                    "button.binding.Interact.0", "button.binding.Interact.1", "button.clear.Interact" })
                    if (form.controls.Count(control => control.id == key) != 1 ||
                        (key.StartsWith("label.") ? form.Find<Text>(key) == null : form.Find<Button>(key) == null))
                        throw new InvalidOperationException("Incomplete or duplicate authored settings row: " + key);
                parent.sizeDelta = new Vector2(parent.sizeDelta.x, Mathf.Max(parent.sizeDelta.y, 190 + LocalBindingRules.Actions.Length * 43));
                for (int i = 0; i < oldActions.Length; i++)
                    if (view.actions[i].id != oldActions[i].id || view.actions[i].button != oldActions[i].button)
                        throw new InvalidOperationException("Existing settings action order/reference changed.");
                for (int i = 0; i < oldControls.Length; i++)
                    if (form.controls[i].id != oldControls[i].id || form.controls[i].reference != oldControls[i].reference)
                        throw new InvalidOperationException("Existing settings control order/reference changed.");
                Debug.Log("PRISM SETTINGS CONTROLS: appended " + (view.actions.Count - oldActions.Length) +
                    " action and " + (form.controls.Count - oldControls.Length) + " form controls; existing registry prefix preserved.");
            }
        }

        private static Button SelectionCloneButton(Button template, Transform parent, string name, string caption)
        {
            var clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false); clone.name = name;
            var button = clone.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent();
            var label = clone.GetComponentInChildren<Text>(true);
            if (label == null) throw new InvalidOperationException("The cloned settings button has no label.");
            label.text = caption;
            var item = clone.GetComponent<RaidUIRepeatedItem>() ?? clone.AddComponent<RaidUIRepeatedItem>();
            item.button = button; item.label = label;
            return button;
        }
        private static Text SelectionCloneLabel(Text template, Transform parent, string name, string caption)
        {
            var clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false); clone.name = name;
            var label = clone.GetComponent<Text>(); label.text = caption; return label;
        }

        internal static void PolishSelection(GameObject root, Font headingFont, Font bodyFont)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if (view.viewId == "origin") SelectionPolishOrigin(view, headingFont, bodyFont);
                if (view.viewId == "settings") SelectionPolishSettings(view, headingFont, bodyFont);
            }
            PolishSettingsSliders(root);
        }

        internal static void PolishSettingsSliders(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true).Where(view => view.viewId == "settings"))
                foreach (var slider in view.GetComponentsInChildren<Slider>(true))
                {
                    var track = slider.transform.Find("Track")?.GetComponent<Image>();
                    var handle = slider.handleRect != null ? slider.handleRect.GetComponent<Image>() : null;
                    var fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
                    if (track == null || handle == null)
                        throw new InvalidOperationException("Missing authored settings slider track/handle: " + slider.name);
                    if (slider.direction == Slider.Direction.BottomToTop || slider.direction == Slider.Direction.TopToBottom)
                        throw new InvalidOperationException("Expected a horizontal settings slider: " + slider.name);
                    string contract = EditorJsonUtility.ToJson(slider);
                    SelectionSliderTint(track, new Color(116 / 255f, 97 / 255f, 143 / 255f, 1));
                    if (fill != null) SelectionSliderTint(fill, new Color(181 / 255f, 155 / 255f, 225 / 255f, 1));
                    SelectionSliderTint(handle, new Color(226 / 255f, 220 / 255f, 236 / 255f, 1));
                    var rect = slider.handleRect;
                    var parent = rect.parent as RectTransform;
                    if (parent == null) throw new InvalidOperationException("The settings slider handle has no RectTransform parent.");
                    parent.ForceUpdateRectTransforms();
                    float parentHeight = parent.rect.height;
                    if (parentHeight <= 0 || float.IsNaN(parentHeight) || float.IsInfinity(parentHeight))
                        throw new InvalidOperationException("The settings slider handle parent has no positive height: " + slider.name);
                    // Slider.UpdateVisuals drives the cross-axis anchors to 0..1.
                    // Keep that contract and compensate sizeDelta so the visible handle
                    // stays 14x28 when the value changes instead of growing to 14x52.
                    var minimum = rect.anchorMin; minimum.y = 0; rect.anchorMin = minimum;
                    var maximum = rect.anchorMax; maximum.y = 1; rect.anchorMax = maximum;
                    var pivot = rect.pivot; pivot.y = .5f; rect.pivot = pivot;
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 0);
                    rect.sizeDelta = new Vector2(14, 28 - parentHeight);
                    if (EditorJsonUtility.ToJson(slider) != contract)
                        throw new InvalidOperationException("Slider styling must preserve its value, events and bindings: " + slider.name);
                }
        }

        private static void SelectionSliderTint(Image image, Color color)
        {
            image.color = color;
            foreach (var style in image.GetComponents<RaidUIStyle>()) style.useTheme = false;
        }

        private static void SelectionPolishOrigin(RaidUIView view, Font headingFont, Font bodyFont)
        {
            var panel = SelectionBound<RectTransform>(view, "_panel");
            var heading = SelectionBound<Text>(view, "_heading");
            var status = SelectionBound<Text>(view, "_status");
            SelectionRect(panel, Vector2.zero, new Vector2(1160, 720));
            SelectionRect(heading.rectTransform, new Vector2(0, 320), new Vector2(1080, 46));
            SelectionHeading(heading, headingFont, 32);
            var subtitle = panel.GetComponentsInChildren<Text>(true).Single(text => text.transform.parent == panel &&
                text != heading && text != status && text.rectTransform.anchoredPosition.y > 150);
            SelectionRect(subtitle.rectTransform, new Vector2(0, 268), new Vector2(1080, 38));
            SelectionBody(subtitle, bodyFont, 17, TextAnchor.MiddleCenter);
            for (int i = 0; i < 12; i++)
            {
                var button = SelectionBound<Button>(view, "_choices/[" + i + "]");
                var label = SelectionBound<Text>(view, "_choiceLabels/[" + i + "]");
                SelectionRect((RectTransform)button.transform, new Vector2((i % 4 - 1.5f) * 278, 160 - i / 4 * 144), new Vector2(264, 132));
                SelectionRect(label.rectTransform, Vector2.zero, new Vector2(236, 108));
                SelectionBody(label, bodyFont, 18, TextAnchor.UpperLeft);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 15; label.resizeTextMaxSize = 18;
            }
            for (int i = 0; i < 3; i++)
            {
                var button = SelectionBound<Button>(view, "_routeChoices/[" + i + "]");
                var label = SelectionBound<Text>(view, "_routeLabels/[" + i + "]");
                SelectionRect((RectTransform)button.transform, new Vector2((i - 1) * 374, -5), new Vector2(350, 390));
                SelectionRect(label.rectTransform, Vector2.zero, new Vector2(316, 354));
                SelectionBody(label, bodyFont, 19, TextAnchor.MiddleLeft);
            }
            SelectionRect(status.rectTransform, new Vector2(0, -250), new Vector2(1080, 38));
            SelectionBody(status, bodyFont, 18, TextAnchor.MiddleCenter);
            foreach (var action in view.actions.Where(action => action.id == "back" || action.id == "finish"))
            {
                if (action.button == null) throw new InvalidOperationException("Missing origin footer: " + action.id);
                SelectionRect((RectTransform)action.button.transform, new Vector2(action.id == "back" ? -205 : 205, -314), new Vector2(370, 52));
                SelectionButtonLabel(action.button, bodyFont, 20);
            }
        }

        private static void SelectionPolishSettings(RaidUIView view, Font headingFont, Font bodyFont)
        {
            var panel = SelectionBound<RectTransform>(view, "_panel");
            var title = panel.GetComponentsInChildren<Text>(true).Single(text => text.transform.parent == panel &&
                text.rectTransform.anchoredPosition.y > 300);
            SelectionRect(title.rectTransform, new Vector2(0, 318), new Vector2(840, 44));
            SelectionHeading(title, headingFont, 28);
            title.alignment = TextAnchor.MiddleLeft;
            foreach (string tab in new[] { "화면", "사운드", "조작", "카메라" })
            {
                var button = view.actions.Single(action => action.id == tab).button;
                if (button == null) throw new InvalidOperationException("Missing settings tab: " + tab);
                ((RectTransform)button.transform).sizeDelta = new Vector2(208, 44);
                SelectionButtonLabel(button, bodyFont, 18);
            }
            var status = SelectionBound<Text>(view, "_status");
            SelectionRect(status.rectTransform, new Vector2(0, -253), new Vector2(840, 54));
            SelectionBody(status, bodyFont, 16, TextAnchor.UpperLeft);
            status.resizeTextForBestFit = true; status.resizeTextMinSize = 14; status.resizeTextMaxSize = 16;
            bool hasHud = view.actions.Any(action => action.id == "HUDLayout" && action.button != null);
            string[] ids = hasHud ? new[] { "Apply", "Cancel", "Defaults", "HUDLayout" } : new[] { "Apply", "Cancel", "Defaults" };
            float[] positions = hasHud ? new[] { -330f, -150f, 90f, 330f } : new[] { -285f, -65f, 220f };
            float[] widths = hasHud ? new[] { 160f, 160f, 280f, 180f } : new[] { 190f, 210f, 300f };
            for (int i = 0; i < ids.Length; i++)
            {
                var button = view.actions.Single(action => action.id == ids[i]).button;
                if (button == null) throw new InvalidOperationException("Missing settings footer: " + ids[i]);
                SelectionRect((RectTransform)button.transform, new Vector2(positions[i], -318), new Vector2(widths[i], 48));
                SelectionButtonLabel(button, bodyFont, 18);
            }
            foreach (var form in view.GetComponentsInChildren<RaidUIForm>(true))
                foreach (var control in form.controls)
                    if (control.reference is Text label)
                    {
                        SelectionBody(label, bodyFont, label.fontSize, control.id == "label.hint" ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
                        if (form.id == "Controls.controls" && control.id == "label.hint")
                            label.rectTransform.sizeDelta = new Vector2(label.rectTransform.sizeDelta.x, 64);
                    }
            foreach (string field in new[] { "_confirmText", "_conflictText" })
            {
                var label = SelectionBound<Text>(view, field);
                SelectionBody(label, bodyFont, field == "_confirmText" ? 24 : 22, TextAnchor.MiddleCenter);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 18; label.resizeTextMaxSize = label.fontSize;
            }
        }

        private static T SelectionBound<T>(RaidUIView view, string field) where T : UnityEngine.Object
            => view.references.FirstOrDefault(reference => reference.path == field)?.target as T
                ?? throw new InvalidOperationException("Missing authored selection binding: " + view.viewId + "/" + field);
        private static void SelectionRect(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchoredPosition = position; rect.sizeDelta = size; }
        private static void SelectionBody(Text text, Font font, int size, TextAnchor alignment)
        {
            if (font != null) { text.font = font; foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false; }
            text.fontSize = size; text.alignment = alignment; text.lineSpacing = 1.12f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
        }
        private static void SelectionButtonLabel(Button button, Font bodyFont, int size)
        {
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) throw new InvalidOperationException("Missing button label: " + button.name);
            label.rectTransform.sizeDelta = ((RectTransform)button.transform).sizeDelta - new Vector2(20, 8);
            SelectionBody(label, bodyFont, size, TextAnchor.MiddleCenter);
        }
        private static void SelectionHeading(Text text, Font font, int size)
        {
            SelectionBody(text, font, size, TextAnchor.MiddleCenter); text.fontStyle = FontStyle.Normal;
            text.resizeTextForBestFit = false;
            var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
            outline.enabled = true; outline.effectColor = new Color(19 / 255f, 16 / 255f, 34 / 255f, 1); outline.useGraphicAlpha = true;
            float distance = size >= 30 ? 1.3f : 1f;
            outline.effectDistance = new Vector2(distance, -distance);
        }
    }
}
#endif
