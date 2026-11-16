using System;
using UnityEngine;
using UnityEngine.UI;

namespace Astella.PrismV4
{
    /// <summary>Passive references for a handoff module. No gameplay updates, action handlers or design overrides.</summary>
    [AddComponentMenu("UI/Astella Prism V4 Bindings")]
    public sealed class PrismV4Bindings : MonoBehaviour
    {
        public enum Module { Boss, Player, PartyRow, SkillSlot, Center, Preview }
        public Module module;
        public PrismV4Gauge healthFill, jobResourceFill, bossFill, castFill, ultimateChargeFill;
        public Text nameText, valueText, resourceText, keyText, cooldownText, castNameText, castTimeText;
        public Image artwork, frame, cooldownShade, hoverOverlay, disabledOverlay, selectedOverlay, deadOverlay;
        public Sprite criticalHealthSprite;
        public Button button;
        public GameObject readyGlow, unavailableOverlay;
        public PrismV4Bindings[] skillSlots = Array.Empty<PrismV4Bindings>();
        public PrismV4Bindings[] partyRows = Array.Empty<PrismV4Bindings>();
        [Tooltip("Documentation only. Connect job resource and central ultimate independently in the main project.")]
        [TextArea] public string integrationNote;
    }
}
