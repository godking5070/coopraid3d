using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // The tab presenter owns tab activation; this authored marker only reflects it.
    public sealed class AstellaUITabIndicator : MonoBehaviour
    {
        public GameObject tab;
        public Graphic indicator;
        public Graphic caption;
        public Color activeCaption = new Color(.94f, .92f, .98f, 1);
        public Color idleCaption = new Color(.62f, .64f, .71f, 1);

        private void LateUpdate()
        {
            bool selected = tab != null && tab.activeSelf;
            if (indicator != null && indicator.enabled != selected) indicator.enabled = selected;
            if (caption != null)
            {
                Color color = selected ? activeCaption : idleCaption;
                if (caption.color != color) caption.color = color;
            }
        }
    }
}
