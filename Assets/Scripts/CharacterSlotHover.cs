using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidRogue
{
    // A dedicated MonoScript asset is required when the slot is saved as a prefab.
    public sealed class CharacterSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action enter, leave;
        public void OnPointerEnter(PointerEventData e) => enter?.Invoke();
        public void OnPointerExit(PointerEventData e) => leave?.Invoke();
        private void OnDisable() => leave?.Invoke();
    }
}
