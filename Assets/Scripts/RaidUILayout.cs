using UnityEngine;

namespace RaidRogue
{
    // Optional fold behavior is authored here, rather than resetting a panel to hardcoded dimensions.
    public sealed class RaidUILayout : MonoBehaviour
    {
        public float collapsedHeight = 40;
        private bool _folded;
        private Vector2 _min, _max, _size;
        private Vector3 _position;
        public static void Copy(RectTransform destination, RectTransform source)
        {
            destination.anchorMin=source.anchorMin; destination.anchorMax=source.anchorMax; destination.pivot=source.pivot;
            destination.sizeDelta=source.sizeDelta; destination.anchoredPosition3D=source.anchoredPosition3D;
            destination.localRotation=source.localRotation; destination.localScale=source.localScale;
        }
        public static void Fold(RectTransform panel, bool collapsed)
        {
            if(panel==null)return;
            var layout=panel.GetComponent<RaidUILayout>()??panel.gameObject.AddComponent<RaidUILayout>();
            if(layout._folded==collapsed)return;
            if(collapsed)
            {
                layout._min=panel.anchorMin;layout._max=panel.anchorMax;layout._size=panel.sizeDelta;layout._position=panel.anchoredPosition3D;
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,layout.collapsedHeight);
            }
            else {panel.anchorMin=layout._min;panel.anchorMax=layout._max;panel.sizeDelta=layout._size;panel.anchoredPosition3D=layout._position;}
            layout._folded=collapsed;
        }
    }
}
