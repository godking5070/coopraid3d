using System;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Presentation references only. Gameplay choices stay in CharacterFrameworkUI.
    public sealed class RaidBuildStationView : MonoBehaviour
    {
        [Serializable] public sealed class Icon
        {
            public RectTransform root;
            public Image art;
            public Text fallback;
            public Sprite overrideSprite;
            [NonSerialized] public string tooltip;
        }
        [Serializable] public sealed class Choice
        {
            public Button button;
            public Text label;
            public Icon icon;
        }
        [Serializable] public sealed class Member
        {
            public Text label;
            public Icon[] icons;
            public Text counterBadge;
        }
        public Text title, partyTitle, localHeading, counterSummary, stage, tooltip;
        public Icon ownRoute;
        public Button routeChange, close;
        public Choice[] choices = Array.Empty<Choice>();
        public Member[] members = Array.Empty<Member>();
        public bool fitToCanvas = true;
        public Color counterEquippedColor = new Color(.38f,.29f,.12f);
        public Color counterMissingColor = new Color(.12f,.14f,.19f);

        public void ValidateBindings()
        {
            if(title==null||partyTitle==null||localHeading==null||counterSummary==null||stage==null||tooltip==null||routeChange==null||close==null)
                throw new InvalidOperationException("ConstellationBuild: missing text/button binding.");
            if(choices==null||choices.Length!=7||members==null||members.Length!=4)
                throw new InvalidOperationException("ConstellationBuild requires 7 choices and 4 party rows.");
            CheckIcon(ownRoute);
            foreach(var choice in choices)
            {
                if(choice==null||choice.button==null||choice.label==null)throw new InvalidOperationException("Missing build choice binding.");
                CheckIcon(choice.icon);
            }
            foreach(var member in members)
            {
                if(member==null||member.label==null||member.counterBadge==null||member.icons==null||member.icons.Length!=7)
                    throw new InvalidOperationException("Missing party build row binding.");
                foreach(var icon in member.icons)CheckIcon(icon);
            }
        }
        private static void CheckIcon(Icon icon)
        {
            if(icon==null||icon.root==null||icon.art==null||icon.fallback==null)
                throw new InvalidOperationException("Missing build icon binding.");
        }
    }
}
