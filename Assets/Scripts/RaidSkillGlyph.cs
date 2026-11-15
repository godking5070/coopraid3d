using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>Resolution-independent weapon glyphs assembled from native UI strokes.</summary>
    public sealed class RaidSkillGlyph : MonoBehaviour
    {
        public int kind;
        public Color color = Color.white;

        public void Draw()
        {
            if (transform.childCount > 0) return;
            if (kind == 2)
            {
                Stroke(-.32f, .38f, .32f, .38f);
                Stroke(-.32f, .38f, -.26f, -.12f);
                Stroke(.32f, .38f, .26f, -.12f);
                Stroke(-.26f, -.12f, 0, -.4f);
                Stroke(.26f, -.12f, 0, -.4f);
                Stroke(0, .22f, 0, -.15f);
            }
            else
            {
                Stroke(-.3f, -.34f, .27f, .32f);
                Stroke(.27f, .32f, .1f, .28f);
                Stroke(.27f, .32f, .25f, .12f);
                Stroke(-.27f, -.13f, -.09f, -.29f);
                if (kind == 0) Stroke(-.32f, .17f, -.04f, .39f);
                if (kind == 1) { Stroke(-.1f, -.38f, .38f, .16f); Stroke(-.37f, .28f, -.12f, .28f); }
                if (kind == 3) { Stroke(-.4f, .12f, -.05f, .12f); Stroke(-.4f, -.04f, -.19f, -.04f); }
            }
        }

        private void Stroke(float x1, float y1, float x2, float y2)
        {
            var go = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            Vector2 a = new Vector2(x1, y1) * 42;
            Vector2 b = new Vector2(x2, y2) * 42;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = (a + b) * .5f;
            rt.sizeDelta = new Vector2((b - a).magnitude, 2.4f);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }
    }
}
