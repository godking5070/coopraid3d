using UnityEngine;

namespace RaidRogue
{
    /// <summary>Opt-in runtime fit for one authored composition; child layout and style remain untouched.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [DefaultExecutionOrder(12500)]
    public sealed class AstellaPrismCanvasFit : MonoBehaviour
    {
        public Vector2 referenceSize = new Vector2(1672, 941);
        [Min(.01f)] public float designScale = 1f;
        [Tooltip("Symmetric inset on each axis, measured in the parent RectTransform's units.")]
        public Vector2 padding = Vector2.zero;

        private RectTransform _rect;
        private Vector3 _authoredScale;
        private DrivenRectTransformTracker _tracker;
        private bool _captured, _changed, _applying;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            _rect = GetComponent<RectTransform>();
            if (_rect == null || !Finite(_rect.localScale)) return;
            _authoredScale = _rect.localScale;
            _captured = true; _changed = false;
            _tracker.Add(this, _rect, DrivenTransformProperties.ScaleX | DrivenTransformProperties.ScaleY | DrivenTransformProperties.ScaleZ);
            Fit();
        }
        private void LateUpdate() { if (Application.isPlaying) Fit(); }
        private void OnRectTransformDimensionsChange() { if (Application.isPlaying && isActiveAndEnabled) Fit(); }
        private void OnTransformParentChanged() { if (Application.isPlaying && isActiveAndEnabled) Fit(); }

        private void Fit()
        {
            if (!_captured || _applying || _rect == null || _rect.parent is not RectTransform parent) return;
            Vector2 size = parent.rect.size;
            if (!Positive(referenceSize.x) || !Positive(referenceSize.y) || !Positive(designScale) ||
                !Positive(size.x) || !Positive(size.y) || !Finite(padding.x) || !Finite(padding.y) || padding.x < 0 || padding.y < 0) return;
            float width = size.x - padding.x * 2, height = size.y - padding.y * 2;
            if (!Positive(width) || !Positive(height)) return;
            float scale = Mathf.Min(width / referenceSize.x, height / referenceSize.y) * designScale;
            if (!Positive(scale)) return;
            Vector3 next = Vector3.one * scale;
            if ((_rect.localScale - next).sqrMagnitude < .00000001f) return;
            _applying = true;
            try { _rect.localScale = next; _changed = true; }
            finally { _applying = false; }
        }

        private void OnDisable()
        {
            _tracker.Clear();
            if (_captured && _changed && _rect != null) _rect.localScale = _authoredScale;
            _captured = _changed = false;
        }
        private static bool Positive(float value) => Finite(value) && value > .000001f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
