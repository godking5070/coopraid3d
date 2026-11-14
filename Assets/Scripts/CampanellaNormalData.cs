using System;
using UnityEngine;

namespace RaidRogue
{
    // Stage 3 normal values are initial tuning, not story canon.
    [Serializable]
    public sealed class CampanellaNormalSettings
    {
        [Min(1)] public float health=42000;
        [Min(12)] public float arenaRadius=18;
        [Min(.5f)] public float patternInterval=3.2f;
        [Min(.5f)] public float castSeconds=3.5f;
        [Min(.5f)] public float recoverySeconds=2.5f;
        [Min(0)] public float tankDamage=460;
        [Min(0)] public float sweepDamage=300;
        [Min(0)] public float vibrationDamage=290;
        [Min(0)] public float weightDamage=280;
        [Min(0)] public float shareDamage=1000;
        [Min(0)] public float chargeDamage=390;
        [Min(.5f)] public float vibrationRadius=6;
        [Min(.5f)] public float weightRadius=2.7f;
        [Min(.5f)] public float shareRadius=3.5f;
        [Min(.5f)] public float chargeWidth=2.5f;
        [Min(.5f)] public float chargeLength=18;
        [Min(.5f)] public float tuningPrepareSeconds=6;
        [Min(.5f)] public float tuningWindowSeconds=2.5f;
        [Min(.1f)] public float tuningGapSeconds=.7f;
        [Min(.5f)] public float plateRadius=2.4f;
        [Min(0)] public float tuningFailureDamage=260;
        public Vector3[] platePoints={new Vector3(-9,0,9),new Vector3(9,0,9),new Vector3(9,0,-9),new Vector3(-9,0,-9)};
        [Range(0,1)] public float forceBellThreshold=.4f;
        [Min(.5f)] public float forceBellSeconds=10;
        [Min(1)] public float forceBellStagger=350;
        [Min(0)] public float forceBellFailureDamage=850;
        [Min(.5f)] public float forceBellGroggySeconds=8;
        [Min(.5f)] public float forceBellRecoverySeconds=5;
        [Range(0,1)] public float storyThreshold=.1f;
        public GameObject bossVisualPrefab;
        public RuntimeAnimatorController bossAnimatorController;
        public AudioClip suppressedVibrationCue;
        public AudioClip counterCue;
        public AudioClip plateCue;
        public AudioClip incompleteBellCue;
        public string chainTrigger="CampanellaChain";
        public string legTrigger="CampanellaLeg";
        public string vibrationTrigger="CampanellaVibration";
        public string weightTrigger="CampanellaWeight";
        public string shareTrigger="CampanellaShare";
        public string chargeTrigger="CampanellaCharge";
        public string tuneTrigger="CampanellaTune";
        public string forcedBellTrigger="CampanellaForcedBell";
    }

    public enum CampanellaPattern { None, ChainCrush, BronzeSweep, SuppressedVibration, FallingWeight, ResonanceShare, ChainRush, SilentTuning, ForcedBell, Pendulum }
    public enum CampanellaStep { Rest, Cast, TunePrepare, TuneActive, TuneGap, ForcedBell, Groggy,
        HardFollowup, EchoWarning, Transformation, LockPrepare, LockCounter, LockHold, LockOpening,
        LockRecovery, FinalReposition }
    public enum CampanellaPlateResult { Pending, Success, WrongActor, Missing, Failed }

    public static class CampanellaNormalRules
    {
        public static bool InPlate(Vector3 position,Vector3 plate,float radius) =>
            new Vector2(position.x-plate.x,position.z-plate.z).sqrMagnitude<=radius*radius;
        public static CampanellaPlateResult CheckPlate(int assignedActor,int standingActor,bool assignedAlive,bool timedOut)
        {
            if(assignedActor<0)return CampanellaPlateResult.Missing;
            if(assignedAlive && standingActor==assignedActor)return CampanellaPlateResult.Success;
            if(timedOut)return CampanellaPlateResult.Failed;
            return standingActor>=0?CampanellaPlateResult.WrongActor:CampanellaPlateResult.Pending;
        }
        public static int NextStep(int current) => Math.Min(4,current+1);
        public static bool ForceBellDue(bool used,float hpRatio,float threshold) => !used && hpRatio<=threshold;
        public static bool ApplyStagger(ref float current,float points,float required)
        { current=Math.Max(0,current+Math.Max(0,points));return current>=required; }
    }
}
