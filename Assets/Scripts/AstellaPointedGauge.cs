using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>
    /// Clips a full-width textured fill with a moving pointed end. The source presenter
    /// retains its original fill Image; this component changes material values only.
    /// </summary>
    [DefaultExecutionOrder(12000)]
    public sealed class AstellaPointedGauge : MonoBehaviour
    {
        public Image sourceFill;
        public Image artwork;
        [Tooltip("Optional authored highlight sprite source. Its separate renderer is hidden; the shader draws it at the moving tip.")]
        public Image tipHighlight;
        [Tooltip("Normalized fill bounds: x minimum, y minimum, width, height in the original full sprite.")]
        public Vector4 fillBounds = new Vector4(.009f, 14f / 64f, .982f, 36f / 64f);
        [Tooltip("Tip width relative to the full original sprite width; 12px in a 1000px design.")]
        [Range(0, .2f)] public float pointWidth = .012f;
        [Tooltip("Original-design one-pixel padding around the tip highlight, in normalized sprite coordinates.")]
        public Vector2 tipPadding = new Vector2(.001f, 1f / 64f);
        [Range(0, 1)] public float value = 1f;

        private Material _material;
        private Material _originalMaterial;
        private Image _boundArtwork;
        private Image _hiddenSource, _hiddenTip;
        private bool _sourceWasEnabled, _tipWasEnabled;
        private Sprite _boundTipSprite;

        public float Value => Sanitize(sourceFill != null ? sourceFill.fillAmount : value);

        /// <summary>Manual data path for party rows. An assigned sourceFill remains authoritative.</summary>
        public void SetValue(float ratio)
        {
            value = Sanitize(ratio);
            if (isActiveAndEnabled) Refresh();
        }

        private void OnEnable() { Refresh(); }
        private void LateUpdate() { Refresh(); }
        private void OnDisable() { Release(); }
        private void OnDestroy() { Release(); }

        private void Refresh()
        {
            if (artwork == null || artwork == sourceFill) return;
            if (_boundArtwork != artwork || _material == null || artwork.material != _material)
            {
                Release();
                _boundArtwork = artwork;
                _originalMaterial = artwork.material;
                var shader = Shader.Find("Astella/UI/PointedGauge");
                if (shader == null) return;
                _material = _originalMaterial != null && _originalMaterial.shader == shader
                    ? new Material(_originalMaterial)
                    : new Material(shader);
                _material.name = "Pointed gauge / " + artwork.name;
                _material.hideFlags = HideFlags.HideAndDontSave;
                artwork.material = _material;
                artwork.SetMaterialDirty();
            }
            HideSourceRenderers();
            ResolveHighlight();
            Apply(_material);
            // A stencil Mask can cache a modified material. Each gauge has a unique base
            // material, so its modified material is unique too and receives live values.
            var renderedMaterial = artwork.materialForRendering;
            if (renderedMaterial != null && renderedMaterial != _material) Apply(renderedMaterial);
        }

        private void HideSourceRenderers()
        {
            if (_hiddenSource != sourceFill)
            {
                if (_hiddenSource != null) _hiddenSource.enabled = _sourceWasEnabled;
                _hiddenSource = sourceFill;
                if (_hiddenSource != null) _sourceWasEnabled = _hiddenSource.enabled;
            }
            if (_hiddenSource != null) _hiddenSource.enabled = false;
            var tip = tipHighlight != artwork && tipHighlight != sourceFill ? tipHighlight : null;
            if (_hiddenTip != tip)
            {
                if (_hiddenTip != null) _hiddenTip.enabled = _tipWasEnabled;
                _hiddenTip = tip;
                if (_hiddenTip != null) _tipWasEnabled = _hiddenTip.enabled;
                _boundTipSprite = null;
            }
            if (_hiddenTip != null) _hiddenTip.enabled = false;
        }

        private void ResolveHighlight()
        {
            if (_hiddenTip == null || _hiddenTip.sprite == null) return;
            var sprite = _hiddenTip.sprite;
            if (_boundTipSprite != sprite)
            {
                _boundTipSprite = sprite;
                var texture = sprite.texture;
                Rect rect = sprite.textureRect;
                _material.SetTexture("_TipTex", texture);
                _material.SetVector("_TipUV", new Vector4(rect.x / texture.width, rect.y / texture.height,
                    rect.width / texture.width, rect.height / texture.height));
                _material.SetFloat("_TipStrength", 1);
                artwork.SetMaterialDirty();
            }
            _material.SetColor("_TipColor", _hiddenTip.color);
        }

        private void Apply(Material material)
        {
            material.SetFloat("_Fill", Value);
            material.SetVector("_FillBounds", fillBounds);
            var uv = artwork.sprite != null ? DataUtility.GetOuterUV(artwork.sprite) : new Vector4(0, 0, 1, 1);
            material.SetVector("_SpriteUV", new Vector4(uv.x, uv.y, uv.z - uv.x, uv.w - uv.y));
            material.SetFloat("_PointWidth", Mathf.Max(0, pointWidth));
            material.SetVector("_TipPadding", new Vector4(Mathf.Max(0, tipPadding.x), Mathf.Max(0, tipPadding.y), 0, 0));
            if (material != _material && _hiddenTip != null && _hiddenTip.sprite != null)
            {
                material.SetTexture("_TipTex", _material.GetTexture("_TipTex"));
                material.SetVector("_TipUV", _material.GetVector("_TipUV"));
                material.SetFloat("_TipStrength", _material.GetFloat("_TipStrength"));
                material.SetColor("_TipColor", _material.GetColor("_TipColor"));
            }
        }

        private void Release()
        {
            if (_hiddenSource != null) _hiddenSource.enabled = _sourceWasEnabled;
            if (_hiddenTip != null) _hiddenTip.enabled = _tipWasEnabled;
            _hiddenSource = _hiddenTip = null;
            _boundTipSprite = null;
            if (_boundArtwork != null && _boundArtwork.material == _material)
            {
                _boundArtwork.material = _originalMaterial;
                _boundArtwork.SetMaterialDirty();
            }
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
            }
            _material = _originalMaterial = null;
            _boundArtwork = null;
        }

        private static float Sanitize(float ratio) => float.IsNaN(ratio) ? 0 : Mathf.Clamp01(ratio);
    }
}
