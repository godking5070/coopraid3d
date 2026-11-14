using System;
using UnityEngine;

namespace RaidRogue
{
    // Initial hard tuning. The normal settings object is never mutated by hard combat.
    [Serializable]
    public sealed class FloraHardSettings
    {
        public FloraNormalSettings combat = new FloraNormalSettings();
        [Min(0)] public float healthOverride = 0; // Zero uses normal HP.
        [Min(1)] public float attackTempo = 1.2f;
        [Range(.3f,.5f)] public float impactWarning = .4f;
        [Min(.1f)] public float counterWindow = 1.6f;
        [Min(.5f)] public float tankFollowupSeconds = 2.4f; // Effective 2s at tempo 1.2.
        [Min(.5f)] public float sweepFollowupSeconds = 2.4f;
        [Min(.5f)] public float rootFollowupSeconds = 2.4f;
        [Min(.5f)] public float crossFollowupSeconds = 2.4f;
        [Min(.5f)] public float petalThornSeconds = 2;
        [Min(0)] public float petalThornDamage = 120;
        [Range(0,1)] public float transformationThreshold = .4f;
        [Min(.5f)] public float transformationSeconds = 8;
        [Min(.5f)] public float rootCoreSeconds = 12;
        [Min(1)] public float rootCoreHealth = 1800;
        [Min(0)] public float rootCoreFailureDamage = 850;
        [Min(.5f)] public float rootCoreGroggySeconds = 6;
        [Min(.5f)] public float specialRecoverySeconds = 5;
        [Range(0,1)] public float finalGardenThreshold = .25f;
        [Min(.5f)] public float finalRepositionSeconds = 2;
        public Vector3 firstCore = new Vector3(-8,0,-3);
        public Vector3 secondCore = new Vector3(8,0,-3);
        public GameObject transformedVisualPrefab;
        public RuntimeAnimatorController transformedAnimatorController;
        public RaidStageCutscenePresentation transformationPresentation;
        public KeyCode cutsceneSkipKey = KeyCode.Space;
        public AudioClip transformationCue;
        public AudioClip coreCue;
        public AudioClip attackPrepCue;
        public AudioClip attackImpactCue;
        public AudioClip followupCue;
        public string pruningTrigger = "FloraPruning";
        public string scissorsTrigger = "FloraScissors";
        public string rootsTrigger = "FloraRoots";
        public string refluxTrigger = "FloraReflux";
        public string crossTrigger = "FloraCross";
        public string followupTrigger = "FloraFollowup";
    }
}
