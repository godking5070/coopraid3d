using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Astella.PrismV4
{
    /// <summary>
    /// Reveals a full-size RGBA fill without stretching its texture. The sprite alpha
    /// supplies the fixed polygon; only the moving front is clipped. No shader or game types required.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("UI/Astella Prism V4 Gauge")]
    public sealed class PrismV4Gauge : Image
    {
        public enum Direction { LeftToRight, BottomToTop }

        [Tooltip("Optional existing data Image. Read only: its sprite, transform, enabled state and fillAmount are never changed.")]
        public Image sourceFill;
        [Range(0, 1)] public float previewValue = 1;
        [Tooltip("Full sprite normalized inner bounds, in bottom-left UV coordinates.")]
        public Rect innerBounds = new Rect(0, 0, 1, 1);
        [Tooltip("Moving triangular front width / full sprite dimension along the fill direction. Zero gives a straight front.")]
        [Range(0, .25f)] public float pointWidth;
        public Direction direction;

        private float _lastRatio = -1;
        private readonly List<Vector2> _polygon = new List<Vector2>(8);

        public float Ratio => Sanitize(sourceFill != null && sourceFill != this ? sourceFill.fillAmount : previewValue);

        public void Bind(Image source)
        {
            sourceFill = source == this ? null : source;
            _lastRatio = -1;
            SetVerticesDirty();
        }

        public void SetValue(float value)
        {
            previewValue = Sanitize(value);
            SetVerticesDirty();
        }

        private void LateUpdate()
        {
            float ratio = Ratio;
            if (Mathf.Approximately(ratio, _lastRatio)) return;
            _lastRatio = ratio;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            float ratio = Ratio;
            if (ratio <= 0) return;
            BuildClipPolygon(ratio, innerBounds, pointWidth, direction, _polygon);

            var rect = GetPixelAdjustedRect();
            Sprite artwork = overrideSprite != null ? overrideSprite : sprite;
            Vector4 uv = artwork != null ? DataUtility.GetOuterUV(artwork) : new Vector4(0, 0, 1, 1);
            for (int i = 0; i < _polygon.Count; i++)
            {
                Vector2 point = _polygon[i];
                var vertex = UIVertex.simpleVert;
                vertex.position = new Vector3(rect.xMin + rect.width * point.x, rect.yMin + rect.height * point.y, 0);
                vertex.uv0 = new Vector4(Mathf.Lerp(uv.x, uv.z, point.x), Mathf.Lerp(uv.y, uv.w, point.y), 0, 0);
                vertex.color = color;
                helper.AddVert(vertex);
            }
            bool transpose = direction == Direction.BottomToTop && ratio < 1;
            for (int i = 1; i < _polygon.Count - 1; i++)
            {
                if (transpose) helper.AddTriangle(0, i, i + 1);
                else helper.AddTriangle(0, i + 1, i);
            }
        }

        /// <summary>Pure geometry path shared by the renderer and offline endpoint verification.</summary>
        public static void BuildClipPolygon(float ratio, Rect bounds, float tipWidth, Direction fillDirection, List<Vector2> polygon)
        {
            ratio = Sanitize(ratio);
            polygon.Clear();
            if (ratio <= 0) return;
            if (ratio >= 1)
            {
                // Full means the original alpha polygon including BOTH original triangle tips.
                polygon.Add(Vector2.zero);
                polygon.Add(Vector2.right);
                polygon.Add(Vector2.one);
                polygon.Add(Vector2.up);
            }
            else
            {
                float minimum = Mathf.Clamp01(fillDirection == Direction.LeftToRight ? bounds.xMin : bounds.yMin);
                float maximum = Mathf.Clamp01(fillDirection == Direction.LeftToRight ? bounds.xMax : bounds.yMax);
                float crossMinimum = Mathf.Clamp01(fillDirection == Direction.LeftToRight ? bounds.yMin : bounds.xMin);
                float crossMaximum = Mathf.Clamp01(fillDirection == Direction.LeftToRight ? bounds.yMax : bounds.xMax);
                float front = Mathf.Lerp(minimum, Mathf.Max(minimum, maximum), ratio);
                float center = (crossMinimum + crossMaximum) * .5f;
                float halfHeight = Mathf.Max((crossMaximum - crossMinimum) * .5f, .000001f);
                float tip = Mathf.Min(Mathf.Max(tipWidth, 0), Mathf.Max(front - minimum, 0));

                Add(polygon, fillDirection, 0, 0);
                Add(polygon, fillDirection, Edge(front, tip, 0, center, halfHeight), 0);
                if (crossMinimum > 0) Add(polygon, fillDirection, Edge(front, tip, crossMinimum, center, halfHeight), crossMinimum);
                Add(polygon, fillDirection, front, center);
                if (crossMaximum < 1) Add(polygon, fillDirection, Edge(front, tip, crossMaximum, center, halfHeight), crossMaximum);
                Add(polygon, fillDirection, Edge(front, tip, 1, center, halfHeight), 1);
                Add(polygon, fillDirection, 0, 1);
            }
        }

        private static void Add(List<Vector2> polygon, Direction fillDirection, float along, float across)
        {
            polygon.Add(fillDirection == Direction.LeftToRight ? new Vector2(along, across) : new Vector2(across, along));
        }

        private static float Edge(float front, float tip, float cross, float center, float halfHeight)
        {
            return front - tip * Mathf.Clamp01(Mathf.Abs(cross - center) / halfHeight);
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);
        }
    }
}
