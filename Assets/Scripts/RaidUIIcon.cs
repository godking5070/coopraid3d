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
        public void Show(string id)
        {
            if (_id == id) return; _id = id;
            if (image == null) return;
            image.sprite = overrideSprite != null ? overrideSprite : RaidUIPresentation.Settings?.Icon(id);
            image.enabled = image.sprite != null; // Text labels remain a usable fallback.
        }
        private void OnValidate() { _id = null; Show(previewId); }
    }
}
