using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidRogue
{
    /// <summary>Pointer-only bridge for an authored skill tooltip; never submits or replaces a skill action.</summary>
    public sealed class AstellaOrnateSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public AstellaOrnateHUD hud;
        public int slotIndex;
        public bool invokeAction;
        public void OnPointerEnter(PointerEventData eventData) { if (hud != null) hud.ShowTooltip(slotIndex); }
        public void OnPointerExit(PointerEventData eventData) { if (hud != null) hud.HideTooltip(); }
        public void OnPointerClick(PointerEventData eventData)
        { if (invokeAction && hud != null && eventData.button == PointerEventData.InputButton.Left) hud.UseSlot(slotIndex); }
        private void OnDisable() { if (hud != null) hud.HideTooltip(); }
    }
}
