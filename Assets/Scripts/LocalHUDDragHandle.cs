using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidRogue
{
    public sealed class LocalHUDDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        public LocalHUDLayoutController owner;
        public string id;
        public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)owner.Select(id);}
        public void OnBeginDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)owner.BeginDrag(id,e.pressPosition);}
        public void OnDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)owner.Drag(id,e.position);}
    }
}
