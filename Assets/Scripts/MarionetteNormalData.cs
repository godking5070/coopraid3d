using System;
using UnityEngine;

namespace RaidRogue
{
    // Stage 4 normal tuning. Values here are initial implementation values, not story canon.
    [Serializable] public sealed class MarionetteNormalSettings
    {
        [Min(1)] public float health=46000;
        [Min(10)] public float stageHalfSize=15;
        [Min(.5f)] public float patternInterval=3.2f;
        [Min(.5f)] public float castSeconds=3.5f;
        [Min(.5f)] public float recoverySeconds=2.5f;
        [Min(0)] public float tankDamage=470;
        [Min(0)] public float halfDamage=300;
        [Min(0)] public float centerDamage=300;
        [Min(0)] public float lightDamage=290;
        [Min(0)] public float stackDamage=1000;
        [Min(0)] public float chargeDamage=390;
        [Min(.5f)] public float centerRadius=6;
        [Min(.5f)] public float lightRadius=2.7f;
        [Min(.5f)] public float stackRadius=3.5f;
        [Min(.5f)] public float chargeWidth=2.5f;
        [Min(.5f)] public float chargeLength=18;
        [Min(.5f)] public float counterWindow=2.3f;
        [Min(.5f)] public float rescueSeconds=12;
        [Min(1)] public float stringHealth=2000;
        [Min(0)] public float rescueFailureDamage=450;
        [Range(0,1)] public float climaxThreshold=.3f;
        [Min(.5f)] public float climaxSeconds=12;
        [Min(1)] public float climaxStagger=420;
        [Min(0)] public float climaxFailureDamage=850;
        [Min(.5f)] public float climaxGroggySeconds=8;
        [Min(.5f)] public float climaxRecoverySeconds=5;
        [Range(0,1)] public float storyThreshold=.1f;
        public GameObject bossVisualPrefab;
        public RuntimeAnimatorController bossAnimatorController;
        public GameObject rearPuppetStagePrefab;
        public GameObject firewallPrefab;
        public AudioClip counterCue,controlCue,rescueCue,climaxCue,firewallCue;
        public string tankTrigger="MarionetteTank",halfTrigger="MarionetteHalf",centerTrigger="MarionetteCenter";
        public string lightTrigger="MarionetteLight",stackTrigger="MarionetteStack",chargeTrigger="MarionetteCharge";
        public string controlTrigger="MarionetteControl",climaxTrigger="MarionetteClimax";
    }

    public enum MarionettePattern { None, LeadExecution, HalfCurtain, TragicCenter, TrackingLight, AudienceApplause, StageCharge, TraitorRole, Climax, Reenact, GoldenCrossing }
    public enum MarionetteStep { Rest, Cast, Control, Climax, Groggy, HardFollowup, HardResidue, ReenactDelay, ReenactCast, CrossingFirst, CrossingGap, CrossingSecond, Recoil, Transformation, ClimaxRegroup }

    public static class MarionetteNormalRules
    {
        public static bool RescueAllowed(int livingCount) => livingCount>=2;
        public static bool ClimaxDue(bool used,float hp,float threshold) => !used && hp<=threshold;
        public static bool ApplyStagger(ref float current,float points,float required)
        { current=Math.Max(0,current+Math.Max(0,points));return current>=required; }
        public static Vector3 ClampToStage(Vector3 p,float halfSize,float margin=1)
        { p.x=Mathf.Clamp(p.x,-halfSize+margin,halfSize-margin);p.z=Mathf.Clamp(p.z,-halfSize+margin,halfSize-margin);return p; }
    }
}
