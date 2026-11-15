using System;
using UnityEngine;

namespace RaidRogue
{
    [Serializable] public sealed class JanusHardSettings : JanusNormalSettings
    {
        [Min(1)] public float attackTempo=1.2f;
        [Min(.1f)] public float impactWarning=.4f;
        [Min(.5f)] public float tankSecondGap=2,halfSecondGap=2.6f,ringSecondGap=2.5f;
        [Min(.5f)] public float crossSecondGap=2.8f,chainSecondGap=3.2f;
        [Min(.5f)] public float residueSeconds=1.6f,residueWidth=2.5f;
        [Min(0)] public float residueDamage=120;
        [Min(.5f)] public float postFirstHold=1.5f,postReleaseSeconds=4,postFinalHold=1.5f;
        [Min(.5f)] public float postRegroupSeconds=4,transformationSeconds=8;
        [Range(0,1)] public float transformationThreshold=.4f;
        public Vector3 alphaCounterAlternate=new Vector3(-9,1,-7),omegaCounterAlternate=new Vector3(9,1,-7);
        [Min(1)] public int[] patternWeights={1,1,1,1,1,1,1,1};
        public RaidStageCutscenePresentation transformationPresentation;
        public GameObject transformedAlphaPrefab,transformedOmegaPrefab;
        public AudioClip transformationCue,releaseCue,finalPullCue;
        public string crossTrigger="JanusHardCross",chainWaveTrigger="JanusHardChainWave";
        public JanusHardSettings()
        {
            health=60000;castSeconds=3.4f;patternInterval=3.5f;
            tankDamage=600;halfDamage=380;lineDamage=400;circleDamage=380;markDamage=360;stackDamage=1200;
            postSeconds=32;postFailureDamage=1000;postOverlapSeconds=1.5f;
            counterPreparation=5;counterWindow=1.6f;counterChargeDamage=480;
        }
    }
    public enum JanusHardPostPhase { None, FirstPull, Release, FinalPull }
    public static class JanusHardRules
    {
        public static float Tempo(float seconds,float multiplier)=>seconds/Math.Max(1,multiplier);
        public static bool DueTransformation(bool transformed,float hp,float threshold)=>!transformed&&hp<=threshold;
        public static bool IsPull(JanusHardPostPhase phase)=>phase==JanusHardPostPhase.FirstPull||phase==JanusHardPostPhase.FinalPull;
        public static bool ReleaseComplete(int[] actors,bool[] holding)
        {
            if(actors==null||holding==null||actors.Length!=4||holding.Length!=4)return false;
            for(int i=0;i<4;i++)if(actors[i]>=0&&holding[i])return false;
            return true;
        }
        public static bool DifferentCounterActors(int alpha,int omega)=>alpha>=0&&omega>=0&&alpha!=omega;
        public static bool AllPulling(int[] actors,bool[] inRange,bool[] alive,bool[] holding)
        {
            if(!JanusNormalRules.AllPosts(actors,inRange,alive)||holding==null||holding.Length!=4)return false;
            for(int i=0;i<4;i++)if(!holding[i])return false;
            return true;
        }
    }
}
