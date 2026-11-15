#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace RaidRogue.Editor
{
    // Compiled only for the editor, but outside Editor/ so Unity can attach the fixture.
    // Never included in the player or any saved prefab.
    public sealed class RaidUIBindingProbe : MonoBehaviour
    {
        public Text title;
        public Renderer optionalRenderer;
        public Renderer removedRenderer;
        public GameObject removedObject;
        public readonly Button[] buttons=new Button[2];
        public readonly List<Text[]> rows=new List<Text[]>();
        public readonly Dictionary<CharacterSlot,Text> labels=new Dictionary<CharacterSlot,Text>();
        public HotbarSlotView nested=new HotbarSlotView();
    }
}
#endif
