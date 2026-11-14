using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    public sealed class RaidUIIcon : MonoBehaviour
    {
        public Image image;
        [Tooltip("Optional per-element override; otherwise resolve the current stable data ID in UI Presentation.")]
        public Sprite overrideSprite;
        public string previewId;
        private string _id;
        private bool _resolved;
        private Image _resolvedImage;
        private Sprite _resolvedOverride;
        public void Show(string id)
        {
            if (image == null) return;
            // Null is also a valid first request (override/fallback). Do not cache
            // requests made before the target image has been connected.
            if (_resolved && _id == id && _resolvedImage == image && _resolvedOverride == overrideSprite) return;
            _id = id;
            _resolved = true;
            _resolvedImage = image;
            _resolvedOverride = overrideSprite;
            image.sprite = overrideSprite != null ? overrideSprite : RaidUIPresentation.Settings?.Icon(id);
            image.enabled = image.sprite != null; // Text labels remain a usable fallback.
        }
        private void OnValidate() { _resolved = false; Show(previewId); }
    }
}
