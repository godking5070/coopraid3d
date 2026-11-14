using System;
using UnityEngine;

namespace RaidRogue
{
    // Stage 2 normal values are initial tuning, not story canon.
    [Serializable]
    public sealed class FloraNormalSettings
    {
        [Min(1)] public float health = 36000;
        [Min(1)] public float arenaRadius = 17.5f;
        [Min(.5f)] public float patternInterval = 3.2f;
        [Min(.5f)] public float castSeconds = 3.5f;
        [Min(.5f)] public float recoverySeconds = 2.5f;
        [Min(0)] public float tankDamage = 420;
        [Min(0)] public float sweepDamage = 280;
        [Min(0)] public float rootDamage = 270;
        [Min(0)] public float petalDamage = 260;
        [Min(0)] public float stackDamage = 960;
        [Min(0)] public float chargeDamage = 360;
        [Min(.5f)] public float rootRadius = 6;
        [Min(.5f)] public float petalRadius = 2.6f;
        [Min(.5f)] public float stackRadius = 3.4f;
        [Min(.5f)] public float chargeWidth = 2.4f;
        [Min(.5f)] public float chargeLength = 18;
        [Min(.5f)] public float vineSeconds = 10;
        [Min(.5f)] public float vineBreakDistance = 12;
        [Min(0)] public float vineFailureDamage = 300;
        [Range(0,1)] public float gardenThreshold = .5f;
        [Min(1)] public float gardenSeconds = 12;
        [Min(0)] public float seedArrivalSeconds = 2;
        [Min(.5f)] public float seedTriggerRadius = 1.5f;
        [Min(.5f)] public float safeRadius = 5.5f;
        [Min(0)] public float gardenDamage = 3000;
        [Min(0)] public float gardenAttackBonus = .15f;
        [Min(.5f)] public float gardenBuffSeconds = 15;
        [Range(0,1)] public float storyThreshold = .1f;
        public GameObject bossVisualPrefab;
        public RuntimeAnimatorController bossAnimatorController;
        public AudioClip counterCue;
        public AudioClip seedCue;
        public AudioClip vineBreakCue;
        public string irisThrowTrigger = "ThrowSeeds";
        public Vector3 seedOne = new Vector3(-7,0,3);
        public Vector3 seedTwo = new Vector3(7,0,3);
    }

    public enum FloraPattern { None, Pruning, ClockScissors, RisingRoots, PreservedPetals, MemorialBloom, PruningCharge, ParasiticVines, EternalGarden, RootReflux, CrossPruning }
    public enum FloraStep { Rest, Cast, Vine, Garden, Groggy, Followup, Transformation, RootCores, Reposition }
    public enum FloraVineOutcome { Pending, Broken, Cancelled, TimedOut }

    public static class FloraNormalRules
    {
        public static int PairCount(int living) => Math.Max(0,living)/2;
        public static bool GardenDue(bool vinesIntroduced,bool alreadyUsed,float hpRatio,float threshold) =>
            vinesIntroduced && !alreadyUsed && hpRatio <= threshold;
        public static bool ActivateOnce(ref bool activated)
        {
            if(activated)return false;
            activated=true;return true;
        }
        public static FloraVineOutcome ResolveVine(bool firstAlive,bool secondAlive,bool farEnough,bool expired)
        {
            if(!firstAlive || !secondAlive)return FloraVineOutcome.Cancelled;
            if(farEnough)return FloraVineOutcome.Broken;
            return expired?FloraVineOutcome.TimedOut:FloraVineOutcome.Pending;
        }
        public static bool VineBroken(Vector3 a, Vector3 b, float distance) =>
            (new Vector2(a.x-b.x,a.z-b.z)).sqrMagnitude >= distance*distance;
        public static bool InCircle(Vector3 point, Vector3 center, float radius) =>
            (new Vector2(point.x-center.x,point.z-center.z)).sqrMagnitude <= radius*radius;
        public static bool Safe(Vector3 point, Vector3 first, bool firstOpen, Vector3 second, bool secondOpen, float radius) =>
            firstOpen && InCircle(point,first,radius) || secondOpen && InCircle(point,second,radius);
        public static bool InHalf(Vector3 point, Vector3 origin, Vector3 right, float radius, bool positive) =>
            InCircle(point,origin,radius) && Vector3.Dot(point-origin,right)*(positive?1:-1)>0;
    }
}
