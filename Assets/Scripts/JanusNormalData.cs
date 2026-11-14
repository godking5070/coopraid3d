using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    [Serializable] public class JanusNormalSettings
    {
        [Min(1)] public float health=60000,arenaRadius=20;
        [Min(.5f)] public float castSeconds=3.4f,patternInterval=3.5f,recoverySeconds=2.5f;
        [Min(0)] public float tankDamage=520,halfDamage=340,lineDamage=360,circleDamage=340,markDamage=320,stackDamage=1100;
        [Min(.5f)] public float halfRadius=12,lineWidth=2.5f,lineLength=20,circleRadius=6,markRadius=2.6f,stackRadius=3.5f;
        [Range(0,1)] public float postThreshold=.6f,storyThreshold=.1f;
        [Min(1)] public float postSeconds=22,postOverlapSeconds=2,postRecoverySeconds=5,postFailureDamage=850;
        [Min(.5f)] public float postInteractRange=2.1f;
        [Min(1)] public float counterPreparation=4,counterWindow=2.2f,counterRecoverySeconds=5,counterGroggySeconds=6,counterRepeatGap=12;
        [Min(0)] public float counterChargeDamage=420;
        public Vector3 alphaPosition=new Vector3(-5,1,-4),omegaPosition=new Vector3(5,1,-4);
        public Vector3[] postPositions={new Vector3(-12,0,-8),new Vector3(-5,0,-15),new Vector3(5,0,-15),new Vector3(12,0,-8)};
        public GameObject alphaVisualPrefab,omegaVisualPrefab,postVisualPrefab,chainVisualPrefab;
        public AudioClip postCue,counterCue,chainBreakCue;
        public string alphaTankTrigger="JanusAlphaStrike",alphaHalfTrigger="JanusAlphaSweep",omegaLineTrigger="JanusOmegaLine",omegaCircleTrigger="JanusOmegaCircle",markTrigger="JanusMark",stackTrigger="JanusStack",postTrigger="JanusPosts",counterTrigger="JanusTwinCounter";
        public int storySpeaker=0; // 0 Alpha, 1 Omega.
    }
    public enum JanusPattern { None, AlphaStrike,AlphaSweep,OmegaLine,OmegaCircle,SealMark,TwinOath,ChainPosts,TwinCounter,ShieldSpearCross,RedChainWave }
    public enum JanusStep { Rest,Cast,Posts,CounterPrepare,CounterWindow,Groggy,HardFollowup,HardTransformation,HardRegroup }
    public static class JanusNormalRules
    {
        public static bool Inside(Vector3 point,float radius) => point.z<=0 && point.x*point.x+point.z*point.z<=radius*radius;
        public static Vector3 Clamp(Vector3 point,float radius)
        {point.z=Mathf.Min(-.2f,point.z);var flat=Vector2.ClampMagnitude(new Vector2(point.x,point.z),Mathf.Max(1,radius));
         point.z=Mathf.Min(-.2f,flat.y);
         float limit=(float)Math.Sqrt(Math.Max(0,radius*radius-point.z*point.z));
         point.x=Math.Max(-limit,Math.Min(limit,flat.x));return point;}
        public static bool CanPost(int post,int actor,int[] assigned,bool[] alive)
        {
            if(post<0||post>=4||actor<0||actor>=4||assigned==null||assigned.Length!=4||alive==null||alive.Length!=4||!alive[actor])return false;
            for(int i=0;i<4;i++)if(assigned[i]==actor)return i==post;
            return assigned[post]<0;
        }
        public static bool AllPosts(int[] assigned,bool[] inRange,bool[] alive)
        {
            if(assigned==null||inRange==null||alive==null||assigned.Length!=4||inRange.Length!=4||alive.Length!=4)return false;
            var unique=new HashSet<int>();
            for(int i=0;i<4;i++)if(assigned[i]<0||assigned[i]>=4||!unique.Add(assigned[i])||!alive[assigned[i]]||!inRange[i])return false;
            return true;
        }
        public static bool CounterSuccess(int alpha,int omega) => alpha>=0&&omega>=0&&alpha!=omega;
        public static bool CounterSuccess(bool alpha,bool omega) => alpha&&omega;
        public static bool PostDue(bool used,bool alive,float ratio,float threshold) => !used&&alive&&ratio<=threshold;
        public static bool DuplicateArea(bool sameFrame,bool sameSource,bool sameOrigin,bool sameDamage,bool oppositeBoss,bool area)
            => sameFrame&&sameSource&&sameOrigin&&oppositeBoss&&area;
        public static bool AcceptPairedAreaHit(int ordinalOnTarget,int ordinalOtherTarget)
            => ordinalOnTarget>ordinalOtherTarget;
    }
}
