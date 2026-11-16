using System;
using System.Linq;
using Astella.PrismV4;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>Called only by explicit constructors for utility UI without an authored prefab.</summary>
    public static class AstellaPrismProceduralSurface
    {
        public static void ApplyNew(Transform root)
        {
            var theme = RaidUIPresentation.Settings?.theme;
            if (root == null || theme == null || !theme.prismSurfaces) return;
            foreach (var image in root.GetComponentsInChildren<Image>(true).ToArray()) ApplyNew(image, theme);
        }

        public static void ApplyNew(Image image)
        {
            var theme = RaidUIPresentation.Settings?.theme;
            if (theme != null && theme.prismSurfaces) ApplyNew(image, theme);
        }

        private static void ApplyNew(Image image, RaidUITheme theme)
        {
            if (image == null || image.GetComponent<RaidUIStyle>() != null ||
                image.GetComponentInParent<PrismV4Bindings>(true) != null || image is PrismV4Gauge ||
                image.type == Image.Type.Filled || image.color.a <= .15f) return;
            string name = image.name.ToLowerInvariant();
            if (new[] { "icon", "artwork", "marker", "mask", "fill", "shield", "aggro", "shade", "overlay" }
                .Any(name.Contains) || image.GetComponent<RaidUIIcon>() != null) return;
            if (image.transform.Find("Prism Utility Frame") != null) return;
            var rect = image.rectTransform;
            float width = Mathf.Max(Mathf.Abs(rect.rect.width), Mathf.Abs(rect.sizeDelta.x));
            float height = Mathf.Max(Mathf.Abs(rect.rect.height), Mathf.Abs(rect.sizeDelta.y));
            if (width > 0 && width <= 7 || height > 0 && height <= 7) return;
            bool button = image.GetComponent<Selectable>() != null;
            var face = button ? theme.buttonSprite : theme.panelSprite;
            if (face == null || theme.borderSprite == null) return;
            image.sprite = face; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 2;
            image.color = Color.white;
            var trim = new GameObject("Prism Utility Frame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            trim.transform.SetParent(image.transform, false);
            var edge = trim.GetComponent<Image>();
            edge.sprite = theme.borderSprite; edge.type = Image.Type.Sliced; edge.pixelsPerUnitMultiplier = 2;
            edge.raycastTarget = false; edge.color = Color.white;
            var edgeRect = edge.rectTransform;
            edgeRect.anchorMin = Vector2.zero; edgeRect.anchorMax = Vector2.one;
            edgeRect.offsetMin = edgeRect.offsetMax = Vector2.zero;
            trim.GetComponent<LayoutElement>().ignoreLayout = true;
        }
    }
}
