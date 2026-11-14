using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    [Serializable] public sealed class JustitiaHardSettings : JustitiaNormalSettings
    {
        [Min(1)] public float attackTempo=1.2f;
        [Min(.1f)] public float impactWarning=.4f;
        [Min(.5f)] public float doubleHitGap=2f,halfGap=2.6f,ringGap=2.5f;
        [Min(.5f)] public float doubleEdgeGap=2.4f,evidenceGap=3.2f;
        [Min(1)] public float doubleEdgeRadius=12f,ringOuterRadius=14f,weightOffset=9f;
        [Min(.1f)] public float residueSeconds=2.2f,residueRadius=2.7f;
        [Min(0)] public float residueDamage=120;
        [Min(.5f)] public float remeasureSeconds=11f,remeasureRecovery=4f;
        [Min(.5f)] public float unfairWeightSeconds=11f,weightArrivalSeconds=1.5f;
        [Min(1)] public int unfairWeightMass=2;
        [Range(0,1)] public float transformationThreshold=.4f,climaxThreshold=.35f;
        [Min(.5f)] public float transformationSeconds=8f,climaxRegroupSeconds=5f;
        [Min(.5f)] public float pulseInterval=2.5f;
        [Min(0)] public float pulseDamage=140;
        [Min(1)] public int[] patternWeights={1,1,1,1,1,1,1,1,1};
        public RaidStageCutscenePresentation transformationPresentation;
        public GameObject transformedVisualPrefab;
        public GameObject weightVisualPrefab;
        public RuntimeAnimatorController transformedAnimatorController;
        public AudioClip transformationCue,weightCue,pulseCue;
        public string doubleEdgeTrigger="JustitiaDoubleEdge",evidenceTrigger="JustitiaEvidence",weightTrigger="JustitiaWeight";
        public JustitiaHardSettings()
        {
            health=52000; tankDamage=600;halfDamage=380;markDamage=350;centerDamage=380;
            stackDamage=1200;chargeDamage=480;balanceFailureDamage=720;verdictFailureDamage=1020;
            counterWindow=1.6f;verdictStagger=500;
        }
    }
    public enum JustitiaHardBalanceKind { None, Remeasure, WeightIntro, ClimaxWeight }
    public enum JustitiaHardNext { None, SecondBalance, Verdict }
    public enum JustitiaHardSpecial { None, Transformation, WeightIntro, ClimaxWeight }
    public static class JustitiaHardRules
    {
        public static float Tempo(float seconds,float scale)=>seconds/Math.Max(1,scale);
        public static JustitiaHardSpecial Due(bool transformed,bool introDone,bool climaxStarted,float hp,float transformAt,float climaxAt)
        {
            if(!transformed&&hp<=transformAt)return JustitiaHardSpecial.Transformation;
            if(transformed&&!introDone)return JustitiaHardSpecial.WeightIntro;
            if(transformed&&introDone&&!climaxStarted&&hp<=climaxAt)return JustitiaHardSpecial.ClimaxWeight;
            return JustitiaHardSpecial.None;
        }
        public static bool ValidWeight(int[] mass,int side,int weight)
        {
            if(mass==null||mass.Length<2||mass.Any(v=>v<=0)||weight<=0||side==0)return false;
            int total=mass.Sum();
            for(int mask=1;mask<(1<<mass.Length)-1;mask++)
            {int left=0;for(int i=0;i<mass.Length;i++)if((mask&(1<<i))!=0)left+=mass[i];
             if(left+(side<0?weight:0)==total-left+(side>0?weight:0))return true;}
            return false;
        }
        public static bool BalancedWeight(int[] mass,JustitiaSide[] sides,int side,int weight,out int left,out int right)
        {
            left=right=0;
            // The normal test requires equal player totals; weighted rounds do not.
            if(mass==null||sides==null||mass.Length!=sides.Length||mass.Length<2||mass.Any(v=>v<=0))return false;
            left=right=0;
            bool assigned=true;
            for(int i=0;i<mass.Length;i++)
                if(sides[i]==JustitiaSide.Left)left+=mass[i];
                else if(sides[i]==JustitiaSide.Right)right+=mass[i];
                else if(sides[i]==JustitiaSide.Center)assigned=false;
                else return false;
            return assigned&&left>0&&right>0&&left+(side<0?weight:0)==right+(side>0?weight:0);
        }
        public static int BestMask(int[] mass,JustitiaSide[] human,JustitiaSide[] current,bool[] automated,int side,int weight)
        {
            if(mass==null||human==null||current==null||automated==null||mass.Length!=human.Length||mass.Length!=current.Length||mass.Length!=automated.Length)return -1;
            int best=-1,costBest=int.MaxValue,total=mass.Sum();
            for(int mask=1;mask<(1<<mass.Length)-1;mask++)
            {
                int left=0,cost=0;bool valid=true;
                for(int i=0;i<mass.Length;i++)
                {bool onLeft=(mask&(1<<i))!=0;if(onLeft)left+=mass[i];
                 if(!automated[i]&&human[i]!=JustitiaSide.Center&&onLeft!=(human[i]==JustitiaSide.Left))valid=false;
                 if(automated[i]&&current[i]!=JustitiaSide.Center&&onLeft!=(current[i]==JustitiaSide.Left))cost++;}
                if(!valid||left+(side<0?weight:0)!=total-left+(side>0?weight:0))continue;
                if(cost<costBest){best=mask;costBest=cost;}
            }
            return best;
        }
    }
}
