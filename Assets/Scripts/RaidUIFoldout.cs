using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Optional authored detail panel. Its saved layout and initial visibility are retained.
    public sealed class RaidUIFoldout : MonoBehaviour
    {
        public Button button;
        public GameObject panel;
        public Text caption;
        public string closedCaption = "직업 정보 +";
        public string openCaption = "직업 정보 −";
        private void Awake() { if (button != null) button.onClick.AddListener(Toggle); }
        private void OnEnable() { RefreshCaption(); }
        private void OnDestroy() { if (button != null) button.onClick.RemoveListener(Toggle); }
        private void Toggle()
        {
            if (panel == null) return;
            panel.SetActive(!panel.activeSelf);
            RefreshCaption();
        }
        private void RefreshCaption()
        {
            if (caption != null && panel != null) caption.text = panel.activeSelf ? openCaption : closedCaption;
        }
    }
}
