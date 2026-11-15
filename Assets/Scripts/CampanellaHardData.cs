using System;
using UnityEngine;

namespace RaidRogue
{
    // Initial hard tuning. Normal settings are never modified by the hard encounter.
    [Serializable]
    public sealed class CampanellaHardSettings
    {
        public CampanellaNormalSettings combat = new CampanellaNormalSettings();
        [Min(0)] public float healthOverride; // Zero uses the Stage 3 normal HP.
        [Min(1)] public float attackTempo = 1.2f;
        [Tooltip("ChainCrush, BronzeSweep, SuppressedVibration, FallingWeight, ResonanceShare, ChainRush, SilentTuning, Pendulum. Each weight is 1–4 draws per bag.")]
        public int[] patternWeights={1,1,1,1,1,1,1,1};
        [Range(.3f,.5f)] public float impactWarning = .4f;
        [Min(.1f)] public float counterWindow = 1.6f;
        [Min(.5f)] public float followupSeconds = 2.4f; // 2 seconds at 1.2 tempo.
        [Min(.5f)] public float echoWarningSeconds = 1.5f;
        [Min(.5f)] public float echoRadius = 3.2f;
        [Min(0)] public float echoDamage = 270;
        [Min(0)] public float tuningFailureDamage = 390;
        [Min(.5f)] public float lingeringWeightSeconds = 2;
        [Min(.1f)] public float lingeringWeightTickSeconds = 1;
        [Min(0)] public float lingeringWeightDamage = 120;
        [Range(0,1)] public float transformationThreshold = .4f;
        [Min(.5f)] public float transformationSeconds = 8;
        [Min(.5f)] public float specialRecoverySeconds = 5;
        [Range(0,1)] public float finalThreshold = .25f;
        [Min(.5f)] public float finalRepositionSeconds = 3;
        [Min(.5f)] public float pendulumFollowupSeconds = 2.4f;
        [Min(0)] public float pendulumDamage = 400;
        [Min(.5f)] public float pendulumWidth = 2.5f;
        [Min(.5f)] public float pendulumLength = 18;
        [Min(.5f)] public float lockPrepareSeconds = 5;
        [Min(.1f)] public float lockCounterSeconds = 1.8f;
        [Min(.5f)] public float lockHoldSeconds = 9;
        [Min(.5f)] public float lockCoreSeconds = 9;
        [Min(1)] public float lockCoreHealth = 650;
        [Min(.5f)] public float lockCoreRadius = 2.5f;
        [Min(.5f)] public float lockPulseInterval = 2.5f;
        [Min(0)] public float lockPulseDamage = 190;
        [Min(0)] public float lockSuccessShockwave = 180;
        [Min(0)] public float lockFailureShockwave = 650;
        [Min(.5f)] public float lockGroggySeconds = 2;
        [Min(.5f)] public float lockRecoverySeconds = 5;
        [Min(.5f)] public float tankCircleRadius = 2.7f;
        [Min(0)] public float counterCooldownMargin = 10.5f;
        public Vector3 bellContact = new Vector3(0,1.65f,2.9f);
        public Vector3 coreOffset = new Vector3(0,0,-4);
        public GameObject transformedVisualPrefab;
        public RuntimeAnimatorController transformedAnimatorController;
        public RaidStageCutscenePresentation transformationPresentation;
        public KeyCode cutsceneSkipKey = KeyCode.Space;
        public AudioClip transformationCue, counterCue, pulseCue, releaseCue, failureCue, echoCue;
        public AudioClip attackPrepCue, attackImpactCue, followupCue;
        public string pendulumTrigger="CampanellaPendulum", followupTrigger="CampanellaFollowup";
        public AnimationClip bellPressClip, bellHoldClip, bellReleaseClip, bellInterruptClip;
        public string bellContactPath = "BellClapperContact";
        public GuardianClashJobVisual aegis = new GuardianClashJobVisual {
            weaponContactPath="AegisSwordAndShield/GreatShield", visualRootPath="AegisSwordAndShield",
            contactLocalOffset=new Vector3(0,0,.08f), fallbackWeaponEuler=new Vector3(-55,0,0)
        };
        public GuardianClashJobVisual berserker = new GuardianClashJobVisual {
            weaponContactPath="berserker_NaturalWeapon/Greatsword", visualRootPath="berserker_NaturalWeapon",
            contactLocalOffset=new Vector3(0,0,.06f), fallbackWeaponEuler=new Vector3(0,0,90)
        };
        public GuardianClashJobVisual dragon = new GuardianClashJobVisual {
            weaponContactPath="dragon-breaker_NaturalWeapon/DragonfireGauntlet",
            visualRootPath="dragon-breaker_NaturalWeapon"
        }; // Neutral placeholder; no authored pose is assumed.
    }

    public static class CampanellaHardRules
    {
        public static float Tempo(float seconds,float multiplier) => seconds/Math.Max(1,multiplier);
        public static bool EchoDue(bool succeeded,float remaining) => succeeded && remaining<=0;
        public static bool TransformDue(bool forcedResolved,bool transformed,bool alive) => forcedResolved && !transformed && alive;
        public static bool FinalDue(bool transformed,bool firstLockDone,bool finalDone,float hp,float threshold) =>
            transformed && firstLockDone && !finalDone && hp<=threshold;
        public static bool CounterEligible(bool open,bool alive,bool tank,float distance,float radius) =>
            open && alive && tank && distance<=radius;
        public static float ClampCoreDamage(float health,float damage) => Math.Max(0,health-Math.Max(0,damage));
    }
}
