#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    /// <summary>Authored decoration only. Call on a copied HUD/view before saving.</summary>
    public static class AstellaPrismRemainingLayouts
    {
        private const string EmblemName = "Prism Intro Emblem";
        private const string TitleName = "Prism Intro Title";
        private static readonly Color Header = new Color(.86f, .82f, .96f, 1);
        private static readonly Color Secondary = new Color(.69f, .7f, .8f, 1);
        private static readonly Color Paper = new Color(.94f, .92f, .97f, 1);

        public static void Refine(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
                if (view.viewId == "connection") RefineConnection(view);
            foreach (var enemyList in root.GetComponentsInChildren<EnemyListUI>(true))
                enemyList.fitBackgroundToRows = true;
            // The existing top-right control stack already fits 1920x1080.
            // Keep positions, widths, tooltip, metrics and combat transforms as
            // authored; only copied enemy backgrounds opt in to row-fit height.
        }

        private static void RefineConnection(RaidUIView view)
        {
            var home = Bound<GameObject>(view, "_homeMenu");
            if (home == null)
                throw new InvalidOperationException("The connection view has no preserved Home binding.");

            foreach (var chart in view.GetComponentsInChildren<RectTransform>(true))
                if (chart.name == "Astella_StarChart") chart.gameObject.SetActive(false);

            var emblem = home.transform.Find(EmblemName) as RectTransform;
            if (emblem == null)
                emblem = new GameObject(EmblemName, typeof(RectTransform)).GetComponent<RectTransform>();
            Center(emblem, home.transform, new Vector2(-300, -28), new Vector2(256, 256));
            emblem.SetAsFirstSibling();
            emblem.gameObject.SetActive(true);
            var group = emblem.GetComponent<CanvasGroup>();
            if (group == null) group = emblem.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1;
            group.interactable = false;
            group.blocksRaycasts = false;

            PaintedLayer(emblem, "Painted backplate", "Hub/hub-background");
            PaintedLayer(emblem, "Painted prism", "Hub/hub-crystal");
            PaintedLayer(emblem, "Painted silver frame", "Hub/hub-frame");

            var originalTitle = Bound<Text>(view, "_menuTitle");
            var subtitle = Bound<Text>(view, "_menuSubtitle");
            if (originalTitle != null) originalTitle.color = Header;
            if (subtitle != null) subtitle.color = Secondary;

            var titleRect = home.transform.Find(TitleName) as RectTransform;
            if (titleRect == null)
                titleRect = new GameObject(TitleName, typeof(RectTransform), typeof(Text)).GetComponent<RectTransform>();
            Center(titleRect, home.transform, new Vector2(-300, -204), new Vector2(340, 44));
            var title = titleRect.GetComponent<Text>() ?? titleRect.gameObject.AddComponent<Text>();
            title.text = "ASTELLA";
            title.font = originalTitle != null && originalTitle.font != null ? originalTitle.font
                : view.GetComponentsInChildren<Text>(true).Select(text => text.font).FirstOrDefault(font => font != null)
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            title.fontSize = 32;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = Paper;
            title.raycastTarget = false;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.gameObject.SetActive(true);

            foreach (var text in view.GetComponentsInChildren<Text>(true))
                if (text.text != null && text.text.Contains("P A R T")) text.color = Secondary;
            foreach (var line in view.GetComponentsInChildren<Image>(true))
                if (line.name == "HeaderLine")
                {
                    line.color = new Color(.62f, .56f, .76f, .4f);
                    line.raycastTarget = false;
                }
            // Home cards, Continue, the legacy runtime-hidden training entry,
            // references and every Button action remain intact.
        }

        private static void PaintedLayer(RectTransform parent, string name, string spriteName)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/UI/AstellaPrismV4/Sprites/" + spriteName + ".png");
            if (sprite == null) throw new InvalidOperationException("Missing painted Prism sprite: " + spriteName);
            var rect = parent.Find(name) as RectTransform;
            if (rect == null) rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.SetAsLastSibling();
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
            rect.gameObject.SetActive(true);
        }

        private static T Bound<T>(RaidUIView view, string field) where T : UnityEngine.Object
            => view.references?.FirstOrDefault(reference => reference != null && reference.path == field)?.target as T;

        private static void Center(RectTransform rect, Transform parent, Vector2 position, Vector2 size)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            var element = rect.GetComponent<LayoutElement>();
            if (element != null) element.ignoreLayout = true;
        }

        public static string[] Report(GameObject root)
            => root == null ? Array.Empty<string>() : root.GetComponentsInChildren<RectTransform>(true)
                .Where(rect => rect.name == EmblemName || rect.name == TitleName || rect.name == "Astella_StarChart")
                .Select(rect => rect.name + " / active=" + rect.gameObject.activeSelf + " / " + rect.anchoredPosition)
                .ToArray();
    }
}
#endif
