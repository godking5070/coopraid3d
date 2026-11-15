using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    // Shared by movement, skills, targeting and the local camera. UI is sampled before gameplay Updates.
    public static class LocalGameplayInput
    {
        private static int _blockedFrame = -1;
        private static int _pointerFrame = -1;
        private static int _escapeFrame = -1;
        private static RoleSelectionManager _roles;
        private static RoguelikeCardManager _cards;
        private static StageManager _stages;
        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _blockedFrame = _pointerFrame = _escapeFrame = -1;
            _roles = null; _cards = null; _stages = null;
        }

        public static bool IsTyping
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == null) return false;
                var field = selected.GetComponentInParent<InputField>();
                if (field != null && field.isFocused) return true;
                var tmp = selected.GetComponentInParent<TMPro.TMP_InputField>();
                return tmp != null && tmp.isFocused;
            }
        }

        public static bool Blocked
        {
            get
            {
                if (_roles == null) _roles = Object.FindFirstObjectByType<RoleSelectionManager>();
                if (_cards == null) _cards = Object.FindFirstObjectByType<RoguelikeCardManager>();
                if (_stages == null) _stages = Object.FindFirstObjectByType<StageManager>();
                if ((RaidSteamRuntime.Instance != null && (RaidSteamRuntime.Instance.OverlayOpen || RaidSteamRuntime.Instance.ConfirmInvite)) || RaidNetworkSession.BlocksInput || !Application.isFocused || Time.timeScale <= 0.001f || IsTyping ||
                    CombatMetricsUI.BlocksUnderlyingInput || LocalCameraSettingsUI.IsOpen ||
                    (_roles != null && _roles.IsRoleModalOpen) || (_cards != null && _cards.IsSelectionOpen) ||
                    (_stages != null && _stages.IsStageTransitioning)) SuppressThisFrame();
                return _blockedFrame == Time.frameCount;
            }
        }

        public static void SuppressThisFrame() => _blockedFrame = Time.frameCount;
        public static void SuppressPointerThisFrame() => _pointerFrame = Time.frameCount;
        public static bool EscapeConsumed => _escapeFrame == Time.frameCount;
        public static void ConsumeEscape() => _escapeFrame = Time.frameCount;

        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) return Mouse.current.position.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePosition;
#endif
                return Vector2.zero;
            }
        }

        public static bool PointerOverUI(Vector2 position)
        {
            if (_pointerFrame == Time.frameCount) return true;
            if (CombatMetricsUI.BlocksPointer(position)) { SuppressPointerThisFrame(); return true; }
            if (EventSystem.current == null) return false;
            Hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, Hits);
            foreach (var hit in Hits)
            {
                if (hit.module is GraphicRaycaster)
                {
                    SuppressPointerThisFrame();
                    return true;
                }
            }
            return false;
        }
    }
}
