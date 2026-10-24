using System;
using System.Collections.Generic;
namespace RaidRogue
{
    // Stable IDs and initial tuning. No actor state is stored in these definitions.
    public static class AegisRouteData
    {
        public const string Bulwark="aegis.origin.bulwark", Sun="aegis.origin.sun", Mirror="aegis.origin.mirror";
        public const float HpMultiplier=1.2f, DefenseMultiplier=1.2f, ShieldMultiplier=1.5f;
        public const float BulwarkCost=50, BulwarkCooldown=1, BulwarkShield=.2f, BulwarkDuration=4;
        public const float SunCost=100, SunCooldown=1, SunDuration=8, SunExtra=.1f, SunHaste=1.3f, SunRange=12, SunRadius=3;
        public const float MirrorCost=30, MirrorCooldown=6, GuardDuration=1.5f, GuardAngle=120;
        public const float ReflectRatio=.25f, ReflectInterval=1, ReflectCap=.1f, CounterExtraStagger=60;
        public const float RetaliationRatio=.5f, RetaliationCap=.2f, RetaliationRadius=6;
        public static string Identity(string route)=>route+".identity";
        public static float HealthScale(string route)=>route==Bulwark?HpMultiplier:1;
        public static float MaximumHp(float baseHp,string route)=>(float)Math.Round((double)baseHp*HealthScale(route),2);
        public static float ShieldScale(string route)=>route==Bulwark?ShieldMultiplier:1;
        public static float DefenseScale(string route)=>route==Bulwark?DefenseMultiplier:1;
        public static float DefenseRating(float basis,string route)=>(float)Math.Round(basis*DefenseScale(route),3);
        public static float Mitigate(float damage,float rating)=>Math.Max(0,damage)*AegisPaladinData.DefenseCurve/(AegisPaladinData.DefenseCurve+Math.Max(0,rating));
        public static float ControlDuration(float duration,float tenacity)=>Math.Max(0,duration)*AegisPaladinData.TenacityCurve/(AegisPaladinData.TenacityCurve+Math.Max(0,tenacity));
        public static float MoveSpeed(CharacterDefinition definition,int ghostStepStacks,float temporaryMultiplier=1)
            =>definition.moveSpeed*definition.moveSpeedMultiplier*(float)Math.Pow(1.3,Math.Max(0,ghostStepStacks))*temporaryMultiplier;
        public static float GcdDuration(float basis,float cardReduction,float minimum,float originScale)
            =>Math.Max(minimum,basis-Math.Max(0,cardReduction))*originScale;
        public static float PreserveHealthRatio(float hp,float oldMax,float newMax)=>oldMax<=0?0:Math.Max(0,Math.Min(1,hp/oldMax))*newMax;
        public static string EffectSummary(string route)
        {
            if(route==Bulwark)return $"파티 보호막 / 일반 피격 이상·넉백 면역 {BulwarkDuration:0.#}초";
            if(route==Sun)return $"{SunDuration:0.#}초간 검기 {SunRange:0.#}m / 반경 {SunRadius:0.#}m · GCD 1·2 간격 ÷{SunHaste:0.#}";
            if(route==Mirror)return $"고정 전방 {GuardAngle:0}° 직접 공격 가드 {GuardDuration:0.#}초 · 첫 타격 반경 {RetaliationRadius:0.#}m 충격파";
            return "대기실에서 기원을 선택하세요";
        }
        public static string PassiveSummary(string route)=>route==Bulwark?"HP·방어 스탯 +20% / 생성 보호막 +50%":route==Sun?"기본 공격 태양 불꽃 +10%":route==Mirror?"가드·보호막 흡수 25% 반사 / 카운터 무력화 +60":"기원 미선택";
        public static string Description(string route)
        {
            if(route==Bulwark)return $"불멸의 성벽 — 파티 보호\n상시: 최대 HP +{(HpMultiplier-1)*100:0}%, 물리·마법 방어 스탯 +20%, 생성 보호막 +{(ShieldMultiplier-1)*100:0}%\n아이기스의 성채: 생존 파티 전원에게 시전자 최대 HP {BulwarkShield*100:0}% 보호막 (보정 1회), 피격 이상·넉백 면역 {BulwarkDuration:0.#}초\n성광 {BulwarkCost:0} · 쿨타임 {BulwarkCooldown:0.#}초";
            if(route==Sun)return $"태양의 집행관 — 검술과 변신\n상시: 기본 공격 1~4 확정 피해의 {SunExtra*100:0}% 태양 불꽃\n여명 각성 — 쌍성검: {EffectSummary(route)}\n성광 {SunCost:0} · 쿨타임 {SunCooldown:0.#}초 · 변신 중 재사용 불가";
            if(route==Mirror)return $"응보의 거울방패 — 반사와 저스트 가드\n상시: 가드·자기 보호막 흡수의 {ReflectRatio*100:0}% 반사 ({ReflectInterval:0.#}초 간격, 최대 HP {ReflectCap*100:0}% 상한), 카운터 추가 무력화 {CounterExtraStagger:0}\n절대 반사경: {EffectSummary(route)}, 첫 공격의 {RetaliationRatio*100:0}% (최대 HP {RetaliationCap*100:0}% 상한)\n성광 {MirrorCost:0} · 쿨타임 {MirrorCooldown:0.#}초";
            return "기원 선택 필요";
        }
        public static void Attach(CharacterDefinition definition,List<CharacterSkillDefinition> skills)
        {
            var ids=new[]{Bulwark,Sun,Mirror};var names=new[]{"불멸의 성벽","태양의 집행관","응보의 거울방패"};
            var znames=new[]{"아이기스의 성채","여명 각성: 쌍성검","절대 반사경"};
            var costs=new[]{BulwarkCost,SunCost,MirrorCost};var cooldowns=new[]{BulwarkCooldown,SunCooldown,MirrorCooldown};
            definition.routes=new CharacterRouteDefinition[3];
            for(int i=0;i<3;i++)
            {
                string id=Identity(ids[i]);
                skills.Add(new CharacterSkillDefinition{id=id,name=znames[i],description=Description(ids[i]),handlerId="aegis.identity",implemented=true,target=SkillTargetRule.Self,cooldown=cooldowns[i],costs=new[]{new ResourceCost{resourceId=AegisPaladinData.Holy,amount=costs[i]}}});
                definition.routes[i]=new CharacterRouteDefinition{id=ids[i],name=names[i],identitySkillId=id,unlockSkills=new[]{id}};
            }
        }
    }
    // Unknown/system damage is deliberately not guardable. Secondary damage never triggers reactions.
    public enum CombatDamageKind { Unclassified, Direct, Area, Periodic, Reflection, Extra, Deferred, System }
    public enum CombatDefenseType { Physical, Magic, Bypass }
    // IDs are allocated when the host resolves a hit, shared by all callbacks for that hit.
    // Each recipient remembers resolved IDs until the encounter reset; replicas never apply hits.
    public sealed class CombatEventGate
    {
        private readonly HashSet<ulong> _resolved=new HashSet<ulong>();
        public bool Accept(ulong id)=>id==0||_resolved.Add(id);
        public void Clear()=>_resolved.Clear();
    }
    public sealed class CounterOpportunity
    {
        public int Id {get;private set;}
        public float End {get;private set;}
        public void Open(float now,float seconds){if(!CharacterDefinitionValidation.Finite(now)||now<0||!CharacterDefinitionValidation.Finite(seconds)||seconds<=0)return;Id++;End=now+seconds;}
        public bool Consume(int id,float now,bool valid){if(!valid||!CharacterDefinitionValidation.Finite(now)||now<0||id!=Id||End<=now)return false;End=0;return true;}
        public void Clear(){Id++;End=0;}
        public void RestoreEnd(float end)=>End=end;
    }
    public static class CombatDamageRules
    {
        public static bool CanReact(CombatDamageKind kind)=>kind==CombatDamageKind.Direct||kind==CombatDamageKind.Area;
        public static bool CanKnockback(float immunity,bool bypass)=>bypass||immunity<=0;
        public static bool CanApplyControl(float immunity,bool bypass)=>bypass||immunity<=0;
        public static bool CanGuard(CombatDamageKind kind,bool bypass)=>kind==CombatDamageKind.Direct&&!bypass;
        public static bool InGuardCone(float facingDot)=>CharacterDefinitionValidation.Finite(facingDot)&&facingDot>=Math.Cos(AegisRouteData.GuardAngle*Math.PI/360)-.00001;
        public static double SunDamage(double actual)=>FinitePositive(actual)?actual*AegisRouteData.SunExtra:0;
        public static bool FinitePositive(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v)&&v>0;
    }
    [Serializable] public sealed class AegisRouteSnapshot
    {
        public float transformation, guard, guardYaw, reflectLock, guardFeedback;
        public bool retaliated;
        public AegisRouteSnapshot Copy()=>(AegisRouteSnapshot)MemberwiseClone();
    }
    public sealed class AegisRouteState
    {
        private AegisRouteSnapshot _state=new AegisRouteSnapshot();
        public bool Transformed=>_state.transformation>0;
        public float Transformation=>_state.transformation;
        public float Guard=>_state.guard;
        public float GuardYaw=>_state.guardYaw;
        public float GuardFeedback=>_state.guardFeedback;
        public bool Activate(string route,float yaw)
        {
            if(route==AegisRouteData.Sun){if(Transformed)return false;_state.transformation=AegisRouteData.SunDuration;return true;}
            if(route==AegisRouteData.Mirror){if(_state.guard>0||!CharacterDefinitionValidation.Finite(yaw))return false;_state.guard=AegisRouteData.GuardDuration;_state.guardYaw=yaw;_state.retaliated=false;_state.guardFeedback=0;return true;}
            return route==AegisRouteData.Bulwark;
        }
        public float Range(string route,bool combo,float original)=>route==AegisRouteData.Sun&&Transformed&&combo?AegisRouteData.SunRange:original;
        public float GcdScale(string route,bool combo)=>route==AegisRouteData.Sun&&Transformed&&combo?1/AegisRouteData.SunHaste:1;
        public double Reflect(string route,CombatDamageKind kind,double absorbed,float maxHp)
        {
            if(route!=AegisRouteData.Mirror||!CombatDamageRules.CanReact(kind)||!CombatDamageRules.FinitePositive(absorbed)||!CombatDamageRules.FinitePositive(maxHp)||_state.reflectLock>0)return 0;
            _state.reflectLock=AegisRouteData.ReflectInterval;return Math.Min(absorbed*AegisRouteData.ReflectRatio,maxHp*AegisRouteData.ReflectCap);
        }
        public bool TryGuard(string route,CombatDamageKind kind,bool bypass,float facingDot,double incoming,float maxHp,out double retaliation)
        {
            retaliation=0;
            if(route!=AegisRouteData.Mirror||_state.guard<=0||!CombatDamageRules.CanGuard(kind,bypass)||!CombatDamageRules.InGuardCone(facingDot)||!CombatDamageRules.FinitePositive(incoming)||!CombatDamageRules.FinitePositive(maxHp))return false;
            _state.guardFeedback=.3f;
            if(!_state.retaliated){_state.retaliated=true;retaliation=Math.Min(incoming*AegisRouteData.RetaliationRatio,maxHp*AegisRouteData.RetaliationCap);}return true;
        }
        public void Advance(float dt)
        {
            if(!CharacterDefinitionValidation.Finite(dt)||dt<0)return;
            _state.transformation=Math.Max(0,_state.transformation-dt);_state.guard=Math.Max(0,_state.guard-dt);
            _state.reflectLock=Math.Max(0,_state.reflectLock-dt);_state.guardFeedback=Math.Max(0,_state.guardFeedback-dt);
        }
        public void Clear()=>_state=new AegisRouteSnapshot();
        public AegisRouteSnapshot Capture()=>_state.Copy();
        public static bool Valid(AegisRouteSnapshot s)=>s!=null&&Good(s.transformation)&&Good(s.guard)&&Good(s.reflectLock)&&Good(s.guardFeedback)&&CharacterDefinitionValidation.Finite(s.guardYaw);
        private static bool Good(float n)=>CharacterDefinitionValidation.Finite(n)&&n>=0;
        public bool Restore(AegisRouteSnapshot s){if(!Valid(s))return false;_state=s.Copy();return true;}
    }
}
