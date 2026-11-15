using System;
using UnityEngine;

namespace RaidRogue
{
    [Serializable] public sealed class MarionetteHardSettings
    {
        public MarionetteNormalSettings combat=new MarionetteNormalSettings();
        [Min(0)] public float healthOverride; // Zero inherits the current Stage 4 normal HP.
        [Min(1)] public float attackTempo=1.2f;
        [Tooltip("Lead, Half, Center, Light, Share, Charge, Rescue, Crossing; 1–4 draws each. Recorded Reenact is queued separately.")]
        public int[] patternWeights={1,1,1,1,1,1,1,1};
        [Range(.3f,.5f)] public float impactWarning=.4f;
        [Min(.1f)] public float counterWindow=1.6f;
        [Min(.5f)] public float followupSeconds=2;
        [Min(.5f)] public float secondTankInterval=2;
        [Min(.5f)] public float lightResidueSeconds=2;
        [Min(.5f)] public float lightResidueRadius=2.7f;
        [Min(0)] public float lightResidueDamage=110;
        [Min(.5f)] public float reenactDelay=2.5f;
        [Min(.5f)] public float reenactCastSeconds=2.5f;
        [Min(0)] public float reenactDamage=320;
        public Vector3[] reenactPoints={new Vector3(-7,0,6),new Vector3(7,0,6),new Vector3(0,0,-7)};
        [Min(.5f)] public float crossingWidth=2;
        [Min(.5f)] public float crossingTravelSeconds=3.5f;
        [Min(.5f)] public float crossingGapSeconds=2;
        [Min(0)] public float crossingDamage=340;
        [Min(.5f)] public float rescueSeconds=15;
        [Min(1)] public float stringHealth=1200;
        [Min(.1f)] public float pullSpeed=.6f;
        [Min(.5f)] public float recoilWarningSeconds=2.5f;
        [Min(.5f)] public float recoilRadius=3;
        [Min(0)] public float recoilDamage=240;
        [Range(0,1)] public float transformationThreshold=.4f;
        [Min(.5f)] public float transformationSeconds=8;
        [Min(.5f)] public float transformationRecoverySeconds=3;
        [Range(0,1)] public float climaxThreshold=.3f;
        [Min(.5f)] public float climaxRegroupSeconds=3.5f;
        [Min(.5f)] public float climaxPulseInterval=3;
        [Min(0)] public float climaxPulseDamage=150;
        [Min(.5f)] public float climaxPulseWarning=1.5f;
        public GameObject transformedVisualPrefab;
        public RuntimeAnimatorController transformedAnimatorController;
        public RaidStageCutscenePresentation transformationPresentation;
        public KeyCode cutsceneSkipKey=KeyCode.Space;
        public AudioClip transformationCue,followupCue,reenactCue,crossingCue,recoilCue,pulseCue;
        public string followupTrigger="MarionetteFollowup",reenactTrigger="MarionetteReenact",crossingTrigger="MarionetteCrossing";
    }

    public static class MarionetteHardRules
    {
        public static float Tempo(float seconds,float multiplier)=>seconds/Math.Max(1,multiplier);
        public static bool TransformDue(bool done,float hp,float threshold)=>!done&&hp<=threshold;
        public static bool ClimaxDue(bool transformed,bool done,float hp,float threshold)=>transformed&&!done&&hp<=threshold;
        public static bool Recordable(MarionettePattern pattern)=>pattern==MarionettePattern.HalfCurtain||pattern==MarionettePattern.TragicCenter;
        public static bool RecoilDue(bool rescued,bool hard)=>rescued&&hard;
        public static bool PulseDue(float remaining,float nextAt)=>remaining<=nextAt;
        public static float CrossingX(float elapsed,float travel,float halfSize,bool reverse)
        {float t=Math.Max(0,Math.Min(1,elapsed/Math.Max(.1f,travel)));return (reverse?-1:1)*(halfSize-2)*t;}
    }
}
