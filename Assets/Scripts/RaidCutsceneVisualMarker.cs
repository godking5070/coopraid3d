using System;
using UnityEngine.Timeline;

namespace RaidRogue
{
    public enum RaidCutsceneVisualEvent { RevealBoss, HideBoss }

    // Host reads the marker time. Timeline playback itself never grants rewards or starts combat.
    [Serializable]
    public sealed class RaidCutsceneVisualMarker : Marker
    {
        public RaidCutsceneVisualEvent visualEvent;
    }
}
