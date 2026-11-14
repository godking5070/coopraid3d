using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    [Serializable] public class JustitiaNormalSettings
    {
        [Min(1)] public float health=52000;
        [Min(10)] public float halfWidth=18;
        [Min(7)] public float halfDepth=12;
        [Min(.2f)] public float centerDeadZone=.65f;
        [Min(.5f)] public float patternInterval=3.2f,castSeconds=3.6f,recoverySeconds=3f;
        [Min(0)] public float tankDamage=520,halfDamage=330,markDamage=300,centerDamage=330,stackDamage=1100,chargeDamage=430;
        [Min(.5f)] public float centerRadius=6,markRadius=2.7f,stackRadius=3.5f,chargeWidth=2.6f,chargeLength=19;
        [Min(.5f)] public float markLockSeconds=1.6f;
        [Min(.5f)] public float counterWindow=2.2f,balanceSeconds=11f,balanceFailureDamage=620,balanceRecoverySeconds=4f;
        [Range(0,1)] public float verdictThreshold=.35f,storyThreshold=.1f;
        [Min(.5f)] public float verdictSeconds=10,verdictGroggySeconds=8,verdictRecoverySeconds=5;
        [Min(1)] public float verdictStagger=460;
        [Min(0)] public float verdictFailureDamage=900;
        public GameObject bossVisualPrefab;
        public RuntimeAnimatorController bossAnimatorController;
        public AudioClip counterCue,balanceCue,verdictCue;
        public string tankTrigger="JustitiaTank",halfTrigger="JustitiaHalf",markTrigger="JustitiaMark",
            centerTrigger="JustitiaCenter",stackTrigger="JustitiaStack",chargeTrigger="JustitiaCharge",
            balanceTrigger="JustitiaBalance",verdictTrigger="JustitiaVerdict";
    }
    public enum JustitiaPattern { None, Execution, HalfSlash, JudgmentMarks, ScaleVibration, SharedGuilt, Charge, Balance, Verdict, DoubleEdged, EvidenceErasure, UnfairWeight }
    public enum JustitiaStep { Rest, Cast, Balance, Verdict, Groggy, HardFollowup, HardResidue, HardTransformation, HardRegroup }
    public enum JustitiaSide { Center, Left, Right }
    public static class JustitiaNormalRules
    {
        public static readonly int[][] Four={new[]{3,1,1,1},new[]{2,2,1,1},new[]{3,2,2,1}};
        public static readonly int[][] Three={new[]{2,1,1},new[]{3,2,1}};
        public static readonly int[][] Two={new[]{1,1},new[]{2,2}};
        public static int[] Candidate(int living,int index)
        {var pool=living==4?Four:living==3?Three:living==2?Two:null;return pool==null?Array.Empty<int>():pool[Math.Abs(index)%pool.Length].ToArray();}
        public static bool HasEqualPartition(int[] mass)
        {
            if(mass==null||mass.Length<2||mass.Any(v=>v<=0))return false;
            int total=mass.Sum();if(total%2!=0)return false;
            for(int mask=1;mask<(1<<mass.Length)-1;mask++)
            {int left=0;for(int i=0;i<mass.Length;i++)if((mask&(1<<i))!=0)left+=mass[i];if(left*2==total)return true;}
            return false;
        }
        public static JustitiaSide Side(float x,float deadZone)=>x < -deadZone?JustitiaSide.Left:x > deadZone?JustitiaSide.Right:JustitiaSide.Center;
        public static bool Balanced(int[] mass,JustitiaSide[] side,out int left,out int right)
        {
            left=right=0;if(mass==null||side==null||mass.Length<2||mass.Length!=side.Length)return false;
            bool allAssigned=true;
            for(int i=0;i<mass.Length;i++)
            {if(mass[i]<=0)return false;
             if(side[i]==JustitiaSide.Center){allAssigned=false;continue;}
             if(side[i]==JustitiaSide.Left)left+=mass[i];else if(side[i]==JustitiaSide.Right)right+=mass[i];else return false;}
            return allAssigned&&left>0&&right>0&&left==right;
        }
        public static int BestMask(int[] mass,JustitiaSide[] humanSide,JustitiaSide[] currentSide,bool[] automated)
        {
            if(mass==null||humanSide==null||currentSide==null||automated==null||mass.Length!=humanSide.Length||mass.Length!=currentSide.Length||mass.Length!=automated.Length)return -1;
            int best=-1,bestCost=int.MaxValue;
            for(int mask=1;mask<(1<<mass.Length)-1;mask++)
            {
                int left=0,cost=0;bool valid=true;
                for(int i=0;i<mass.Length;i++)
                {bool onLeft=(mask&(1<<i))!=0;if(onLeft)left+=mass[i];
                 if(!automated[i]&&humanSide[i]!=JustitiaSide.Center&&onLeft!=(humanSide[i]==JustitiaSide.Left))valid=false;
                 if(automated[i]&&currentSide[i]!=JustitiaSide.Center&&onLeft!=(currentSide[i]==JustitiaSide.Left))cost++;}
                if(!valid||left*2!=mass.Sum())continue;
                if(cost<bestCost){best=mask;bestCost=cost;}
            }
            return best;
        }
        public static Vector3 Clamp(Vector3 point,float halfWidth,float halfDepth,float margin=1)
        {point.x=Mathf.Clamp(point.x,-halfWidth+margin,halfWidth-margin);point.z=Mathf.Clamp(point.z,-halfDepth+margin,halfDepth-margin);return point;}
    }
}
