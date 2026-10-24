using System;
using System.Collections.Generic;
namespace RaidRogue
{
    // IDs, tuning and descriptions are shared; timelines and choices belong to each actor.
    public static class AegisExtensionData
    {
        public const string Spear="aegis.r.spear", Ground="aegis.r.ground", Wings="aegis.r.wings";
        public const string Intervention="aegis.f.intervention", Brand="aegis.f.brand", Retribution="aegis.f.retribution";
        public const string Excalibur="aegis.v.excalibur", Morning="aegis.v.morning";
        public const float SpearCooldown=14, SpearRange=20, SpearPower=400, SpearStagger=100;
        public const float GroundCooldown=18, GroundDuration=8, GroundRadius=5, GroundInterval=1, GroundPower=60, GroundHeal=.02f;
        public const float WingsCooldown=30, WingsDuration=3, WingsRadius=8, WingsAngle=120, WingsReduction=.6f;
        public const float InterventionCooldown=15, InterventionRange=20, InterventionDuration=4, TransferRatio=.5f;
        public const float BrandCooldown=12, BrandRange=20, BrandPower=250, BrandDuration=6, BrandCrit=.1f, CritMultiplier=1.5f;
        public const float RetributionCooldown=10, RetributionRadius=6, RetributionAngle=120, ShieldDamageScale=2;
        public const float UltimateCooldown=120, UltimateRange=20, ExcaliburPower=1800, InvulnerabilityDuration=3;
        public const float MorningPower=1200, MorningDuration=10, MorningReduction=.2f, MorningAttack=.1f;
        public static string Rule(CharacterSlot slot)=>"aegis.unlock."+slot.ToString().ToLowerInvariant();
        public static void Attach(CharacterDefinition definition,List<CharacterSkillDefinition> skills)
        {
            skills.Add(Skill(Spear,"롱기누스의 빛창",SpearCooldown,SpearRange,SkillTargetRule.LivingEnemy,
                $"원거리 성광 창 · 위력 {SpearPower} / 무력화 {SpearStagger}\n사거리 {SpearRange}m · 즉시 공격"));
            skills.Add(Skill(Ground,"축성된 대지",GroundCooldown,0,SkillTargetRule.Self,
                $"발밑 고정 반경 {GroundRadius}m / {GroundDuration}초\n매초 적 위력 {GroundPower}, 생존 아군 최대 HP {GroundHeal*100:0}% 회복"));
            skills.Add(Skill(Wings,"결계의 날개",WingsCooldown,0,SkillTargetRule.Self,
                $"{WingsDuration}초 제자리 수호 · 고정 후방 {WingsAngle}° / {WingsRadius}m\n다른 아군 피해 {WingsReduction*100:0}% 감소 · 대쉬/강제 이동 시 종료"));
            skills.Add(Skill(Intervention,"수호자의 개입",InterventionCooldown,InterventionRange,SkillTargetRule.LivingAlly,
                $"다른 생존 아군에게 안전 접근 / {InterventionRange}m\n{InterventionDuration}초 동안 확정 피해 {TransferRatio*100:0}% 대신 받음 (재이전 없음)"));
            skills.Add(Skill(Brand,"규탄의 낙인",BrandCooldown,BrandRange,SkillTargetRule.LivingEnemy,
                $"위력 {BrandPower} / 사거리 {BrandRange}m\n{BrandDuration}초간 이 적에 대한 파티 치명타 확률 +{BrandCrit*100:0}%p (중복 합산 없음)"));
            skills.Add(Skill(Retribution,"신성한 응징",RetributionCooldown,0,SkillTargetRule.None,
                $"현재 유효 보호막의 {ShieldDamageScale*100:0}% 피해\n전방 {RetributionAngle}° / {RetributionRadius}m · 보호막 유지 · 유효 적 필요"));
            skills.Add(Skill(Excalibur,"여명의 1초 — 엑스칼리버",UltimateCooldown,UltimateRange,SkillTargetRule.LivingEnemy,
                $"Type A · 위력 {ExcaliburPower} / {UltimateRange}m\n즉시 타격 후 자신만 {InvulnerabilityDuration}초 전투 피해 무적 · 성광 비용 없음"));
            skills.Add(Skill(Morning,"첫 번째 아침의 성역",UltimateCooldown,UltimateRange,SkillTargetRule.LivingEnemy,
                $"Type B · 위력 {MorningPower} / {UltimateRange}m\n생존 파티 전원 {MorningDuration}초: 피해 {MorningReduction*100:0}% 감소 / 공격력 +{MorningAttack*100:0}% · 성광 비용 없음"));
            definition.unlocks=new[] {
                new CharacterUnlockDefinition{id=Rule(CharacterSlot.Active),slot=CharacterSlot.Active,afterStage=1,choices=new[]{Spear,Ground,Wings}},
                new CharacterUnlockDefinition{id=Rule(CharacterSlot.OffGcd2),slot=CharacterSlot.OffGcd2,afterStage=2,choices=new[]{Intervention,Brand,Retribution}},
                new CharacterUnlockDefinition{id=Rule(CharacterSlot.Ultimate),slot=CharacterSlot.Ultimate,afterStage=3,choices=new[]{Excalibur,Morning}}
            };
        }
        private static CharacterSkillDefinition Skill(string id,string name,float cooldown,float range,SkillTargetRule target,string text)
            =>new CharacterSkillDefinition{id=id,name=name,implemented=true,handlerId="aegis.extension",cooldown=cooldown,range=range,target=target,
                description=text+$"\n쿨타임 {cooldown:0.#}초 · GCD 독립"};
        // Existing basic-only Sun policy and cooldown-reduction list remain explicit. No new skill is silently added.
        public static bool TriggersSun(string id)=>id==AegisPaladinData.Id(CharacterSlot.Gcd1)||id==AegisPaladinData.Id(CharacterSlot.Gcd2)||id==AegisPaladinData.Id(CharacterSlot.OffGcd1)||id==AegisPaladinData.Id(CharacterSlot.Counter)||id==Spear||id==Brand||id==Retribution||id==Excalibur||id==Morning;
        public static bool InCone(double dx,double dz,double radius,double yaw,double angle)
        {
            double length=Math.Sqrt(dx*dx+dz*dz);if(length>radius+.00001)return false;if(length<.00001)return true;
            double radians=yaw*Math.PI/180;return (dx*Math.Sin(radians)+dz*Math.Cos(radians))/length>=Math.Cos(angle*Math.PI/360)-.00001;
        }
        public static double SharedDamage(double confirmed,CombatDamageKind kind,bool guardianAlive)
            =>guardianAlive&&kind!=CombatDamageKind.Transfer&&kind!=CombatDamageKind.Deferred&&kind!=CombatDamageKind.System&&CombatDamageRules.FinitePositive(confirmed)?confirmed*TransferRatio:0;
        public static float DamageScale(bool wings,bool morning)=>(wings?1-WingsReduction:1)*(morning?1-MorningReduction:1);
        public static float CriticalChance(float baseChance,bool marked)=>Math.Max(0,Math.Min(1,baseChance+(marked?BrandCrit:0)));
        public static float AttackAmount(float power,float attackScale,bool crit)=>power*attackScale*(crit?CritMultiplier:1);
    }
    [Serializable] public sealed class AegisExtensionSnapshot
    {
        public float groundStart,groundEnd,groundNext,groundX,groundZ;
        public float wingStart,wingEnd,wingX,wingZ,wingYaw,interventionEnd;
        public int interventionTarget;
        public AegisExtensionSnapshot Copy()=>(AegisExtensionSnapshot)MemberwiseClone();
    }
    public sealed class AegisExtensionState
    {
        public AegisExtensionSnapshot Data {get;private set;}=new AegisExtensionSnapshot();
        public void StartGround(float now,float x,float z){Data.groundStart=now;Data.groundEnd=now+AegisExtensionData.GroundDuration;Data.groundNext=now+AegisExtensionData.GroundInterval;Data.groundX=x;Data.groundZ=z;}
        // Called only on authority. Replicas restore absolute timestamps and never replay elapsed ticks.
        public int TakeGroundTicks(float now)
        {
            if(!CharacterDefinitionValidation.Finite(now)||Data.groundNext<=0)return 0;int count=0;
            int maximum=(int)Math.Ceiling(AegisExtensionData.GroundDuration/AegisExtensionData.GroundInterval);
            while(Data.groundNext<=now+.00001f&&Data.groundNext<=Data.groundEnd+.00001f&&count<maximum){count++;Data.groundNext+=AegisExtensionData.GroundInterval;}
            if(now>=Data.groundEnd){Data.groundEnd=Data.groundNext=0;}return count;
        }
        public void StartWings(float now,float x,float z,float yaw){Data.wingStart=now;Data.wingEnd=now+AegisExtensionData.WingsDuration;Data.wingX=x;Data.wingZ=z;Data.wingYaw=yaw;}
        public void CancelWings()=>Data.wingEnd=0;
        public void Link(int target,float now){Data.interventionTarget=target;Data.interventionEnd=now+AegisExtensionData.InterventionDuration;}
        public void Unlink(){Data.interventionTarget=0;Data.interventionEnd=0;}
        public void Clear()=>Data=new AegisExtensionSnapshot();
        public bool Restore(AegisExtensionSnapshot value)
        {
            if(value==null)return false;
            foreach(float v in new[]{value.groundStart,value.groundEnd,value.groundNext,value.groundX,value.groundZ,value.wingStart,value.wingEnd,value.wingX,value.wingZ,value.wingYaw,value.interventionEnd})if(!CharacterDefinitionValidation.Finite(v))return false;
            if(value.groundStart<0||value.wingStart<0||value.groundEnd<0||value.groundNext<0||value.wingEnd<0||value.interventionEnd<0)return false;
            if(value.groundEnd>0&&(value.groundEnd<value.groundStart||value.groundEnd>value.groundStart+AegisExtensionData.GroundDuration+.001f||value.groundNext<value.groundStart+AegisExtensionData.GroundInterval-.001f||value.groundNext>value.groundEnd+AegisExtensionData.GroundInterval+.001f))return false;
            if(value.groundEnd==0&&value.groundNext!=0||value.wingEnd>0&&(value.wingEnd<value.wingStart||value.wingEnd>value.wingStart+AegisExtensionData.WingsDuration+.001f)||value.interventionEnd>0&&value.interventionTarget==0)return false;
            Data=value.Copy();return true;
        }
    }
}
