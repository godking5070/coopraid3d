using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaidRogue
{
    /// <summary>
    /// Opt-in authored slot frame states. This component never changes skill artwork,
    /// layout, colors, cooldown timing or gameplay callbacks.
    /// </summary>
    [DefaultExecutionOrder(11500)]
    public sealed class AstellaSkillSlotSkin : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public Button button;
        public Image frame, hoverOverlay, pressedOverlay, disabledOverlay;
        public Sprite normalFrame, hoverFrame, pressedFrame, focusedFrame, disabledFrame;
        public bool animateKeyboard = true;
        [Tooltip("Show cooldown with the existing radial shade and normal frame; disabled art is reserved for unavailable/locked slots.")]
        public bool cooldownUsesNormalFrame = true;
        public AstellaOrnateHUD hud;
        public int slotIndex;

        private bool _hovered, _pointerDown, _selected;
        private float _keyboardUntil;
        private Sprite _authoredFrame;
        private CharacterActor _actor;
        private PlayerCombatController _combat;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (frame == null && button != null) frame = button.targetGraphic as Image;
            _authoredFrame = frame != null ? frame.sprite : null;
        }
        private void OnEnable()
        {
            _hovered = _pointerDown = false;
            _keyboardUntil = 0;
            _selected = button != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == button.gameObject;
        }
        private void OnDisable()
        {
            _hovered = _pointerDown = _selected = false;
            _keyboardUntil = 0;
            Overlay(hoverOverlay, false); Overlay(pressedOverlay, false); Overlay(disabledOverlay, false);
        }

        private void LateUpdate()
        {
            bool editing = LocalHUDLayoutController.IsEditing;
            bool unavailable = !editing && button != null && (!button.IsActive() || !button.IsInteractable());
            bool cooling = IsCooling();
            bool disabled = unavailable && !(cooldownUsesNormalFrame && cooling);
            bool keyboardPressed = false;
            if (animateKeyboard && !editing && !unavailable && !LocalGameplayInput.Blocked)
            {
                string binding = Binding();
                if (!string.IsNullOrEmpty(binding))
                {
                    if (LocalInputBindings.Pressed(binding)) _keyboardUntil = Time.unscaledTime + .14f;
                    keyboardPressed = LocalInputBindings.Held(binding) || Time.unscaledTime < _keyboardUntil;
                }
            }
            bool acceptsFeedback = editing || !unavailable;
            bool pressed = acceptsFeedback && (_pointerDown && _hovered || keyboardPressed);
            bool hovered = acceptsFeedback && _hovered && !pressed;
            bool focused = acceptsFeedback && _selected && !pressed && !hovered;
            Sprite sprite = disabled ? disabledFrame : pressed ? pressedFrame : hovered ? hoverFrame : focused ? focusedFrame : normalFrame;
            if (sprite == null) sprite = normalFrame != null ? normalFrame : _authoredFrame;
            if (frame != null && sprite != null && frame.sprite != sprite) frame.sprite = sprite;
            Overlay(hoverOverlay, hovered || focused);
            Overlay(pressedOverlay, pressed);
            Overlay(disabledOverlay, disabled);
        }

        private bool IsCooling()
        {
            var slot = Slot();
            var shade = slot?.cooldownShade;
            return slot != null && !slot.sharedResurrection && shade != null && shade.gameObject.activeSelf && shade.fillAmount > .001f;
        }
        private AstellaOrnateHUD.SlotBinding Slot() => hud != null && hud.slots != null && slotIndex >= 0 && slotIndex < hud.slots.Length ? hud.slots[slotIndex] : null;
        private string Binding()
        {
            var slot = Slot();
            if (slot == null) return "";
            if (!string.IsNullOrEmpty(slot.bindingOverride)) return slot.bindingOverride;
            if (slot.sharedResurrection) return "Skill5";
            var combat = hud.hotbarSource != null ? hud.hotbarSource.combatController : null;
            if (_combat != combat || _actor == null)
            {
                _combat = combat;
                _actor = combat != null ? combat.GetComponent<CharacterActor>() : null;
            }
            return _actor?.Definition != null ? CharacterSlots.Binding(_actor.Definition, slot.slot) : CharacterSlots.Binding(slot.slot);
        }
        private void Overlay(Image image, bool enabled)
        { if (image != null && image != frame && image.enabled != enabled) image.enabled = enabled; }

        public void OnPointerEnter(PointerEventData eventData) { _hovered = true; }
        public void OnPointerExit(PointerEventData eventData) { _hovered = false; _pointerDown = false; }
        public void OnPointerDown(PointerEventData eventData)
        { if (eventData.button == PointerEventData.InputButton.Left) _pointerDown = true; }
        public void OnPointerUp(PointerEventData eventData)
        { if (eventData.button == PointerEventData.InputButton.Left) _pointerDown = false; }
        public void OnSelect(BaseEventData eventData) { _selected = true; }
        public void OnDeselect(BaseEventData eventData) { _selected = false; }
    }
}
