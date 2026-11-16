#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    public static partial class AstellaPrismUXRevision
    {
        private static readonly Color UXMenuPaper = new Color(.93f, .92f, .96f, 1);
        private static readonly Color UXMenuMuted = new Color(.64f, .65f, .72f, 1);
        private static readonly Color UXMenuAccent = new Color(.69f, .56f, .89f, 1);

        // Run before preservation capture. The existing runtime binder finds this
        // action and therefore does not generate a differently styled late button.
        internal static void EnsureMenuUXControls(GameObject root)
        {
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true).Where(view => view.viewId == "connection"))
            {
                const string id = "Stage 1 난이도: 노멀";
                var existing = view.actions.Select(action => (action.id, action.button)).ToArray();
                var matches = view.actions.Where(action => action.id == id).ToArray();
                if (matches.Length > 1 || matches.Length == 1 && matches[0].button == null)
                    throw new InvalidOperationException("Invalid authored difficulty action.");
                if (matches.Length == 0)
                {
                    var template = UXMenuBound<Button>(view, "_single");
                    var parent = UXMenuBound<GameObject>(view, "_createMenu").transform;
                    var clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                    clone.name = "Stage1Difficulty";
                    var button = clone.GetComponent<Button>();
                    button.onClick = new Button.ButtonClickedEvent();
                    var label = clone.GetComponentInChildren<Text>(true);
                    if (label == null) throw new InvalidOperationException("The difficulty template has no label.");
                    label.text = id;
                    var repeated = clone.GetComponent<RaidUIRepeatedItem>();
                    if (repeated != null) { repeated.button = button; repeated.label = label; }
                    view.actions.Add(new RaidUIView.ActionReference { id = id, button = button });
                }
                for (int index = 0; index < existing.Length; index++)
                    if (view.actions[index].id != existing[index].id || view.actions[index].button != existing[index].button)
                        throw new InvalidOperationException("An existing connection action changed while adding difficulty.");
                if (view.actions.Count(action => action.id == id && action.button != null) != 1 ||
                    view.actions.Count != existing.Length + (matches.Length == 0 ? 1 : 0))
                    throw new InvalidOperationException("Difficulty action registration is not unique.");
            }
        }

        // Editor authoring on copied menus only. Runtime presenters keep their
        // references, callbacks, content, visibility and input state.
        internal static void RedesignMenus(GameObject root, Font heading, Font body)
        {
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if (view.viewId == "connection") UXMenuConnection(view, heading, body);
                if (view.viewId == "settings") UXMenuSettings(view, heading, body);
            }
        }

        // Narrow follow-up on the reviewed copies: no new tab wiring or button
        // ownership changes, and no replay of the full menu authoring pass.
        internal static void RepairMenuDetails(GameObject root, Font heading, Font body)
        {
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if (view.viewId == "connection")
                {
                    UXMenuHomeCardRects(UXMenuBound<Button>(view, "_hostEntry"), true);
                    UXMenuHomeCardRects(UXMenuBound<Button>(view, "_joinEntry"), false);
                    UXMenuConnectionDialogs(view, body);
                }
                if (view.viewId == "settings")
                    foreach (var rect in view.GetComponentsInChildren<RectTransform>(true).Where(rect => rect.name.StartsWith("UX Row Divider ", StringComparison.Ordinal)))
                        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            }
        }

        private static void UXMenuConnection(RaidUIView view, Font heading, Font body)
        {
            var overlay = UXMenuBound<GameObject>(view, "_overlay");
            UXMenuClearFrames(overlay.transform);
            UXMenuSurface(overlay.GetComponent<Image>(), new Color(.035f, .042f, .061f, 1));
            foreach (var button in overlay.GetComponentsInChildren<Button>(true)) UXMenuButton(button, 0, body, 18);
            foreach (var field in overlay.GetComponentsInChildren<InputField>(true))
                UXMenuSurface(field.targetGraphic as Image, new Color(.09f, .105f, .14f, 1));
            var layout = UXMenuBound<RectTransform>(view, "_menuLayout");
            UXMenuRect(layout, Vector2.zero, new Vector2(1200, 840));
            var title = UXMenuBound<Text>(view, "_menuTitle");
            UXMenuRect(title.rectTransform, new Vector2(0, 280), new Vector2(1100, 74));
            UXMenuText(title, heading, 46, TextAnchor.MiddleLeft, UXMenuPaper);
            var subtitle = UXMenuBound<Text>(view, "_menuSubtitle");
            UXMenuRect(subtitle.rectTransform, new Vector2(0, 220), new Vector2(1100, 44));
            UXMenuText(subtitle, body, 18, TextAnchor.MiddleLeft, UXMenuMuted);
            foreach (var text in layout.GetComponentsInChildren<Text>(true).Where(text => text.transform.parent == layout && text.text.Contains("P A R T")))
            {
                UXMenuRect(text.rectTransform, new Vector2(250, 352), new Vector2(600, 28));
                UXMenuText(text, body, 13, TextAnchor.MiddleRight, UXMenuMuted);
            }
            var back = UXMenuBound<Button>(view, "_menuBack");
            UXMenuRect((RectTransform)back.transform, new Vector2(-488, 352), new Vector2(150, 44));
            UXMenuButton(back, 0, body, 18);
            foreach (var image in layout.GetComponentsInChildren<Image>(true).Where(image => image.name == "HeaderLine"))
            {
                UXMenuSurface(image, new Color(.46f, .43f, .58f, .23f));
                UXMenuRect(image.rectTransform, new Vector2(0, 177), new Vector2(1100, 1));
            }

            var home = UXMenuBound<GameObject>(view, "_homeMenu");
            var emblem = home.transform.Find("Prism Intro Emblem") as RectTransform;
            if (emblem != null) UXMenuRect(emblem, new Vector2(-330, -14), new Vector2(340, 340));
            var brand = home.transform.Find("Prism Intro Title")?.GetComponent<Text>();
            if (brand != null)
            {
                UXMenuRect(brand.rectTransform, new Vector2(-330, -223), new Vector2(460, 48));
                UXMenuText(brand, heading, 34, TextAnchor.MiddleCenter, UXMenuPaper);
            }
            var host = UXMenuBound<Button>(view, "_hostEntry");
            UXMenuRect((RectTransform)host.transform, new Vector2(255, 80), new Vector2(500, 150));
            UXMenuButton(host, 2, body, 18); UXMenuHomeCard(host, true, heading, body);
            var continued = UXMenuBound<Button>(view, "_homeContinue");
            UXMenuRect((RectTransform)continued.transform, new Vector2(255, -35), new Vector2(500, 54));
            UXMenuButton(continued, 1, body, 20);
            var save = UXMenuBound<Text>(view, "_homeSave");
            UXMenuRect(save.rectTransform, new Vector2(255, -82), new Vector2(500, 34));
            UXMenuText(save, body, 14, TextAnchor.MiddleLeft, UXMenuMuted);
            var join = UXMenuBound<Button>(view, "_joinEntry");
            UXMenuRect((RectTransform)join.transform, new Vector2(255, -173), new Vector2(500, 118));
            UXMenuButton(join, 0, body, 18); UXMenuHomeCard(join, false, heading, body);
            var utility = new[] { "저장 관리", "설정", "게임 종료" };
            for (int index = 0; index < utility.Length; index++)
            {
                var button = UXMenuAction(view, utility[index]);
                UXMenuRect((RectTransform)button.transform, new Vector2(-455 + index * 195, -320), new Vector2(175, 44));
                UXMenuButton(button, 0, body, 16);
            }
            UXMenuDecoration(home.transform, "UX Home Footer Rule", new Vector2(0, -280), new Vector2(1100, 1), new Color(.46f, .43f, .58f, .17f));

            foreach (string field in new[] { "_single", "_friends", "_lanHost", "_lanJoin", "_ready", "_beginBattle" })
                UXMenuButton(UXMenuBound<Button>(view, field), 2, body, 20);
            UXMenuButton(UXMenuBound<Button>(view, "_steamCreate"), 1, body, 20);
            var difficulty = UXMenuAction(view, "Stage 1 난이도: 노멀");
            UXMenuRect((RectTransform)difficulty.transform, new Vector2(0, -212), new Vector2(680, 42));
            UXMenuButton(difficulty, 1, body, 17);
            UXMenuButton(UXMenuBound<Button>(view, "_recover"), 1, body, 17);
            foreach (var text in new[] { UXMenuBound<Text>(view, "_steamStatus"), UXMenuBound<Text>(view, "_status") })
                UXMenuText(text, body, 16, text.alignment, UXMenuMuted);
            UXMenuConnectionDialogs(view, body);
            // The active run's status strip and combat HUD are outside _overlay.
        }

        private static void UXMenuHomeCard(Button button, bool primary, Font heading, Font body)
        {
            var labels = UXMenuHomeCardRects(button, primary);
            int[] size = primary ? new[] { 11, 29, 16, 15 } : new[] { 11, 24, 15, 13 };
            for (int index = 0; index < labels.Length; index++)
            {
                UXMenuText(labels[index], index == 1 ? heading : body, size[index], TextAnchor.MiddleLeft,
                    index == 1 || primary ? UXMenuPaper : UXMenuMuted);
            }
            foreach (var image in button.GetComponentsInChildren<Image>(true))
                if (image.transform != button.transform && (image.name == "Accent" || image.name == "Divider")) UXMenuTint(image, Color.clear);
            UXMenuDecoration(button.transform, "UX Action Accent", new Vector2(-248, 0), new Vector2(primary ? 4 : 2, primary ? 150 : 92),
                primary ? UXMenuAccent : new Color(.43f, .41f, .52f, .6f));
        }

        private static Text[] UXMenuHomeCardRects(Button button, bool primary)
        {
            var labels = button.GetComponentsInChildren<Text>(true)
                .Where(text => text.transform.parent == button.transform).OrderByDescending(text => text.rectTransform.anchoredPosition.y).ToArray();
            if (labels.Length != 4) throw new InvalidOperationException("Expected four preserved home card text fields.");
            float[] y = primary ? new[] { 55f, 24f, -18f, -56f } : new[] { 45f, 16f, -21f, -51f };
            float[] height = primary ? new[] { 18f, 52f, 43f, 24f } : new[] { 18f, 44f, 38f, 20f };
            for (int index = 0; index < labels.Length; index++)
                UXMenuRect(labels[index].rectTransform, new Vector2(4, y[index]), new Vector2(448, height[index]));
            return labels;
        }

        private static void UXMenuConnectionDialogs(RaidUIView view, Font body)
        {
            // These roots are siblings of _overlay. Their long save/recovery
            // messages retain their existing available area and runtime writers.
            foreach (string field in new[] { "_campaignDialog", "_confirmation", "_recoveryPanel" })
            {
                var dialog = UXMenuBound<GameObject>(view, field);
                UXMenuClearFrames(dialog.transform);
                UXMenuSurface(dialog.GetComponent<Image>(), new Color(.027f, .034f, .052f, .98f));
                foreach (var button in dialog.GetComponentsInChildren<Button>(true)) UXMenuButton(button, 0, body, 18);
            }
            foreach (string id in new[] { "확인", "나가고 초대 참가", "다시 연결" })
                UXMenuButton(UXMenuAction(view, id), 2, body, 18);
            UXMenuButton(UXMenuAction(view, "닫기 / 취소"), 1, body, 18);
            UXMenuButton(UXMenuAction(view, "현재 방 유지"), 1, body, 18);
        }

        private static void UXMenuSettings(RaidUIView view, Font heading, Font body)
        {
            var modal = UXMenuBound<GameObject>(view, "_modal");
            var panel = UXMenuBound<RectTransform>(view, "_panel");
            UXMenuClearFrames(modal.transform);
            UXMenuSurface(modal.GetComponent<Image>(), new Color(.014f, .018f, .029f, .9f));
            UXMenuSurface(panel.GetComponent<Image>(), new Color(.058f, .065f, .088f, 1));
            UXMenuRect(panel, Vector2.zero, new Vector2(1240, 760));
            foreach (var button in modal.GetComponentsInChildren<Button>(true)) UXMenuButton(button, 1, body, 17);
            var title = panel.GetComponentsInChildren<Text>(true).Single(text => text.transform.parent == panel && text.rectTransform.anchoredPosition.y > 300);
            UXMenuRect(title.rectTransform, new Vector2(0, 326), new Vector2(1144, 52));
            UXMenuText(title, heading, 30, TextAnchor.MiddleLeft, UXMenuPaper);
            UXMenuDecoration(panel, "UX Settings Header Rule", new Vector2(0, 282), new Vector2(1144, 1), new Color(.4f, .39f, .48f, .28f));
            UXMenuDecoration(panel, "UX Settings Navigation Divider", new Vector2(-328, -4), new Vector2(1, 535), new Color(.4f, .39f, .48f, .23f));
            string[] tabs = { "화면", "사운드", "조작", "카메라" };
            for (int index = 0; index < tabs.Length; index++)
            {
                var button = UXMenuAction(view, tabs[index]);
                UXMenuRect((RectTransform)button.transform, new Vector2(-470, 218 - index * 68), new Vector2(220, 54));
                UXMenuButton(button, 0, body, 20);
                var page = UXMenuBound<GameObject>(view, "_tabs/[" + index + "]");
                var tabRect = (RectTransform)page.transform;
                UXMenuRect(tabRect, new Vector2(142, 11), new Vector2(870, 510));
                UXMenuSurface(page.GetComponent<Image>(), Color.clear);
                var scroll = page.GetComponent<ScrollRect>();
                if (scroll?.viewport == null) throw new InvalidOperationException("Missing settings tab scroll viewport.");
                UXMenuRect(scroll.viewport, Vector2.zero, new Vector2(850, 500));
                var indicator = UXMenuDecoration(button.transform, "UX Active Category", new Vector2(-108, 0), new Vector2(3, 26), UXMenuAccent);
                indicator.enabled = page.activeSelf;
                var state = button.GetComponent<AstellaUITabIndicator>() ?? button.gameObject.AddComponent<AstellaUITabIndicator>();
                state.tab = page; state.indicator = indicator; state.caption = button.GetComponentInChildren<Text>(true);
                state.activeCaption = UXMenuPaper; state.idleCaption = UXMenuMuted;
            }
            var hud = UXMenuAction(view, "HUDLayout");
            UXMenuRect((RectTransform)hud.transform, new Vector2(-470, -252), new Vector2(220, 48));
            UXMenuButton(hud, 0, body, 17);
            var status = UXMenuBound<Text>(view, "_status");
            UXMenuRect(status.rectTransform, new Vector2(142, -273), new Vector2(830, 46));
            UXMenuText(status, body, 15, TextAnchor.UpperLeft, UXMenuMuted);
            string[] footer = { "Defaults", "Cancel", "Apply" };
            float[] x = { -116, 281, 474 };
            float[] widths = { 285, 160, 180 };
            for (int index = 0; index < footer.Length; index++)
            {
                var button = UXMenuAction(view, footer[index]);
                UXMenuRect((RectTransform)button.transform, new Vector2(x[index], -331), new Vector2(widths[index], 48));
                UXMenuButton(button, footer[index] == "Apply" ? 2 : 0, body, 17);
            }
            foreach (var form in view.GetComponentsInChildren<RaidUIForm>(true).Where(form => form.id.EndsWith(".controls", StringComparison.Ordinal)))
            {
                foreach (var control in form.controls.Where(control => control.id.StartsWith("button.row.", StringComparison.Ordinal)))
                    if (control.reference is Button row)
                    {
                        var divider = UXMenuDecoration(row.transform.parent, "UX Row Divider " + control.id,
                            new Vector2(0, ((RectTransform)row.transform).anchoredPosition.y - 29), new Vector2(790, 1), new Color(.35f, .36f, .44f, .18f));
                        divider.rectTransform.anchorMin = divider.rectTransform.anchorMax = new Vector2(.5f, 1);
                    }
            }
            // Confirm/rebind subdialogs retain their existing centered control
            // positions; a neutral scrim keeps them distinct from the settings page.
            foreach (string field in new[] { "_confirmation", "_conflict" })
            {
                var dialog = UXMenuBound<GameObject>(view, field);
                UXMenuSurface(dialog.GetComponent<Image>(), new Color(.035f, .043f, .064f, .99f));
                UXMenuRect((RectTransform)dialog.transform, Vector2.zero, new Vector2(1240, 760));
            }
        }

        private static Button UXMenuAction(RaidUIView view, string id) => view.actions.SingleOrDefault(action => action.id == id)?.button
            ?? throw new InvalidOperationException("Missing menu action: " + id);
        private static T UXMenuBound<T>(RaidUIView view, string path) where T : UnityEngine.Object =>
            view.references.FirstOrDefault(reference => reference.path == path)?.target as T
            ?? throw new InvalidOperationException("Missing menu reference: " + path);
        private static void UXMenuRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
        private static void UXMenuText(Text text, Font font, int size, TextAnchor anchor, Color color)
        {
            if (font != null) text.font = font;
            text.fontSize = size; text.fontStyle = FontStyle.Normal; text.alignment = anchor; text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 1.12f; text.resizeTextForBestFit = false;
            foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
            foreach (var outline in text.GetComponents<Outline>())
                outline.effectDistance = new Vector2(Mathf.Min(1.2f, Mathf.Abs(outline.effectDistance.x)), -Mathf.Min(1.2f, Mathf.Abs(outline.effectDistance.y)));
        }
        private static void UXMenuButton(Button button, int emphasis, Font body, int size)
        {
            if (button == null) return;
            UXMenuSurface(button.targetGraphic as Image, Color.white);
            UXMenuClearFrames(button.transform);
            var colors = button.colors;
            colors.normalColor = emphasis == 2 ? new Color(.33f, .25f, .49f, 1) : emphasis == 1 ? new Color(.11f, .12f, .16f, 1) : new Color(.08f, .09f, .13f, .08f);
            colors.highlightedColor = emphasis == 2 ? new Color(.45f, .34f, .63f, 1) : new Color(.19f, .19f, .27f, 1);
            colors.selectedColor = emphasis == 2 ? new Color(.43f, .33f, .61f, 1) : new Color(.19f, .19f, .27f, 1);
            colors.pressedColor = new Color(.27f, .22f, .38f, 1);
            colors.disabledColor = new Color(.075f, .08f, .105f, .65f);
            colors.fadeDuration = .12f;
            button.colors = colors;
            // The two home actions retain four independently laid out fields.
            // Their dedicated composition below owns those text rectangles.
            if (button.GetComponentsInChildren<Text>(true).Count(text => text.transform.parent == button.transform) > 1) return;
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) return;
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(18, 4); rect.offsetMax = new Vector2(-18, -4);
            UXMenuText(label, body, size, TextAnchor.MiddleLeft, UXMenuPaper);
        }
        private static void UXMenuClearFrames(Transform root)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                if (image.name == "Prism Surface Frame") UXMenuTint(image, Color.clear);
        }
        private static void UXMenuSurface(Image image, Color color)
        {
            if (image == null) return;
            image.sprite = null; image.overrideSprite = null; image.type = Image.Type.Simple; image.preserveAspect = false;
            UXMenuTint(image, color);
        }
        private static void UXMenuTint(Image image, Color color)
        {
            image.color = color;
            foreach (var style in image.GetComponents<RaidUIStyle>()) style.useTheme = false;
            foreach (var outline in image.GetComponents<Outline>()) outline.enabled = false;
        }
        private static Image UXMenuDecoration(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var child = parent.Find(name) as RectTransform;
            if (child == null)
            {
                var created = new GameObject(name, typeof(RectTransform), typeof(Image));
                created.layer = parent.gameObject.layer; child = (RectTransform)created.transform; child.SetParent(parent, false);
            }
            child.anchorMin = child.anchorMax = child.pivot = new Vector2(.5f, .5f);
            UXMenuRect(child, position, size);
            var image = child.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
    }
}
#endif
