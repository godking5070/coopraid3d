using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    public sealed class RaidUIRepeatedItem : MonoBehaviour
    {
        public Button button;
        public Text label;
        public Image fill;
        public RaidUIIcon icon;
        public PartyMemberRowUI party;
        public static RaidUIRepeatedItem Create(RaidUIRepeatedItem template, Transform parent)
        { return template == null ? null : Instantiate(template, parent, false); }
    }
}
