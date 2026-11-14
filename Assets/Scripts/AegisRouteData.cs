using System;
using System.Collections.Generic;
namespace RaidRogue
{
    public sealed class AegisRecallInput
    {
        public const float HoldSeconds=.45f;
        private bool _armed;private float _started;
        // 1=short release, 2=long press. Blocking or losing control cancels the gesture.
        public int Step(bool pressed,bool held,float now,bool blocked)
        {
            if(blocked||!CharacterDefinitionValidation.Finite(now)){_armed=false;return 0;}
            if(pressed){_armed=true;_started=now;}
            if(!_armed)return 0;
            if(now-_started>=HoldSeconds){_armed=false;return 2;}
            if(!held){_armed=false;return 1;}return 0;
        }
    }
    [Serializable] public sealed class AegisMirrorSnapshot
    {
        public float remaining,precise;
        public int support;
        public bool earned;
        public int[] responses=Array.Empty<int>();
        public float[] ages=Array.Empty<float>();
    }
    public sealed class AegisMirrorState
    {
        public const float Cost=30,Duration=2,Precision=.6f,Reduction=.3f,Bonus=180,Range=20;
        private AegisMirrorSnapshot _data=new AegisMirrorSnapshot();
        public float Remaining=>_data.remaining;
        public float Precise=>_data.precise;
        public int Support=>_data.support;
        public int Count=>_data.responses.Length;
        public int First=>Count>0?_data.responses[0]:0;
        public float FirstAge=>Count>0?_data.ages[0]:60;
        public static float ResponsePower(string trait,float age)=>trait==AegisPaladinData.Reflection?90:trait==AegisPaladinData.Flash&&age<=4?270:Bonus;
        public bool Begin(int support)
        {if(support==0)return false;_data.support=support;_data.remaining=Duration;_data.precise=Precision;_data.earned=false;return true;}
        public bool Protect(CombatDamageKind kind,bool ignoreGuard,bool bypass,float amount,int maximum=2)
        {
            if(_data.remaining<=0||!CombatDamageRules.CanGuard(kind,ignoreGuard||bypass)||!CombatDamageRules.FinitePositive(amount))return false;
            if(_data.precise>0&&!_data.earned)
            {
                _data.earned=true;
                if(Count<Math.Max(1,Math.Min(2,maximum))){var queue=new List<int>(_data.responses){_data.support};_data.responses=queue.ToArray();var ages=new List<float>(_data.ages){0};_data.ages=ages.ToArray();}
            }
            return true;
        }
        public int Consume()
        {int target=First;if(target==0)return 0;var queue=new List<int>(_data.responses);queue.RemoveAt(0);_data.responses=queue.ToArray();var ages=new List<float>(_data.ages);ages.RemoveAt(0);_data.ages=ages.ToArray();return target;}
        public void Advance(float seconds)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<0)return;
            _data.remaining=Math.Max(0,_data.remaining-seconds);_data.precise=Math.Max(0,_data.precise-seconds);
            for(int i=0;i<_data.ages.Length;i++)_data.ages[i]=Math.Min(60,_data.ages[i]+seconds);
            if(_data.remaining==0){_data.support=0;_data.earned=false;}
        }
        public void Clear()=>_data=new AegisMirrorSnapshot();
        public AegisMirrorSnapshot Capture()=>new AegisMirrorSnapshot{remaining=_data.remaining,precise=_data.precise,support=_data.support,earned=_data.earned,responses=(int[])_data.responses.Clone(),ages=(float[])_data.ages.Clone()};
        public bool Restore(AegisMirrorSnapshot value)
        {
            if(value==null){Clear();return true;}
            if(!CharacterDefinitionValidation.Finite(value.remaining)||value.remaining<0||value.remaining>Duration||
                !CharacterDefinitionValidation.Finite(value.precise)||value.precise<0||value.precise>Precision||value.precise>value.remaining||
                value.remaining>0&&value.support==0||value.responses==null||value.responses.Length>2||Array.IndexOf(value.responses,0)>=0)return false;
            var ages=value.ages;
            if(ages==null||ages.Length==0){ages=new float[value.responses.Length];for(int i=0;i<ages.Length;i++)ages[i]=60;}
            if(ages.Length!=value.responses.Length)return false;
            foreach(float age in ages)if(!CharacterDefinitionValidation.Finite(age)||age<0||age>60)return false;
            _data=new AegisMirrorSnapshot{remaining=value.remaining,precise=value.precise,support=value.support,earned=value.earned,responses=(int[])value.responses.Clone(),ages=(float[])ages.Clone()};return true;
        }
    }
    [Serializable] public sealed class AegisGlyph
    {
        public float x,z,remaining;
        public int[] used=Array.Empty<int>();
        public AegisGlyph Copy()=>new AegisGlyph{x=x,z=z,remaining=remaining,used=(int[])used.Clone()};
    }
    public sealed class AegisGlyphState
    {
        public const float Cost=40,Range=20,Radius=2,Lifetime=20,ShieldRatio=.1f,ShieldDuration=6;
        public const int Maximum=2;
        private readonly List<AegisGlyph> _glyphs=new List<AegisGlyph>();
        public IReadOnlyList<AegisGlyph> Glyphs=>_glyphs;
        public bool Full=>_glyphs.Count>=Maximum;
        public static bool DecodePoint(string value,out float x,out float z)
        {
            x=z=0;var parts=(value??"").Split('|');
            return parts.Length==2&&float.TryParse(parts[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out x)&&
                float.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out z)&&
                CharacterDefinitionValidation.Finite(x)&&CharacterDefinitionValidation.Finite(z);
        }
        public bool Install(float x,float z,int maximum=Maximum)
        {
            if(_glyphs.Count>=Math.Max(1,Math.Min(Maximum,maximum))||!CharacterDefinitionValidation.Finite(x)||!CharacterDefinitionValidation.Finite(z))return false;
            _glyphs.Add(new AegisGlyph{x=x,z=z,remaining=Lifetime});return true;
        }
        public bool RecallUnused()
        {int index=_glyphs.FindIndex(g=>g.used.Length==0);if(index<0)return false;_glyphs.RemoveAt(index);return true;}
        public bool Visit(int index,int member,float fromX,float fromZ,float toX,float toZ,float radius=Radius)
        {
            if(index<0||index>=_glyphs.Count||member==0)return false;
            foreach(float n in new[]{fromX,fromZ,toX,toZ})if(!CharacterDefinitionValidation.Finite(n))return false;
            var glyph=_glyphs[index];if(glyph.remaining<=0||Array.IndexOf(glyph.used,member)>=0||glyph.used.Length>=8)return false;
            double dx=toX-fromX,dz=toZ-fromZ,len=dx*dx+dz*dz;
            double t=len<.000001?0:Math.Max(0,Math.Min(1,((glyph.x-fromX)*dx+(glyph.z-fromZ)*dz)/len));
            double x=fromX+t*dx-glyph.x,z=fromZ+t*dz-glyph.z;
            if(!CharacterDefinitionValidation.Finite(radius)||radius<=0||x*x+z*z>radius*radius)return false;
            var used=new List<int>(glyph.used){member};glyph.used=used.ToArray();return true;
        }
        public void Advance(float seconds)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<0)return;
            foreach(var glyph in _glyphs)glyph.remaining=Math.Max(0,glyph.remaining-seconds);
            _glyphs.RemoveAll(g=>g.remaining<=0);
        }
        public void Clear()=>_glyphs.Clear();
        public AegisGlyph[] Capture()=>_glyphs.ConvertAll(g=>g.Copy()).ToArray();
        public bool Restore(AegisGlyph[] value)
        {
            if(value==null){Clear();return true;} // Snapshots from before the glyph migration.
            if(value.Length>Maximum)return false;
            foreach(var g in value)if(g==null||!CharacterDefinitionValidation.Finite(g.x)||!CharacterDefinitionValidation.Finite(g.z)||
                !CharacterDefinitionValidation.Finite(g.remaining)||g.remaining<=0||g.remaining>Lifetime||g.used==null||g.used.Length>8||
                new HashSet<int>(g.used).Count!=g.used.Length||Array.IndexOf(g.used,0)>=0)return false;
            Clear();foreach(var g in value)_glyphs.Add(g.Copy());return true;
        }
    }
    [Serializable] public sealed class AegisFollowupShield
    { public int target; public float amount,remaining; public AegisFollowupShield Copy()=>(AegisFollowupShield)MemberwiseClone(); }
    [Serializable] public sealed class AegisSunSnapshot
    {
        public bool shield,secondRecorded,secondShield;
        public float markRemaining,markPower;
        public int markTarget;
        public int[] eligible=Array.Empty<int>();
        public AegisFollowupShield[] followups=Array.Empty<AegisFollowupShield>();
    }
    public sealed class AegisSunState
    {
        public const float Cost=40, MarkDuration=6, AllyRange=20, ShieldDuration=6;
        // Prototype effect budgets, separately exposed for four/eight-player tuning.
        public const float SwordBonus=180, ShieldRatio=.1f, SmallShieldRatio=.03f, FourBudget=120, EightBudget=160;
        private AegisSunSnapshot _data=new AegisSunSnapshot();
        public bool Shield=>_data.shield;
        public bool SecondShield=>_data.secondRecorded?_data.secondShield:_data.shield;
        public bool SecondRecorded=>_data.secondRecorded;
        public int MarkTarget=>_data.markTarget;
        public int MarkRights=>_data.eligible.Length;
        public float MarkRemaining=>_data.markRemaining;
        public float FirstReward=>Shield?20:30;
        public void Toggle()=>_data.shield=!_data.shield;
        public void BeginSecond(){_data.secondRecorded=true;_data.secondShield=_data.shield;}
        public void FinishSecond(){_data.secondRecorded=false;_data.secondShield=false;}
        public void Clear()=>_data=new AegisSunSnapshot();
        public void SetMark(int target,int owner,IEnumerable<int> members,int partySize,float scale=1)
        {
            var eligible=new HashSet<int>();foreach(int id in members)if(id!=0&&id!=owner&&eligible.Count<7)eligible.Add(id);
            _data.eligible=new List<int>(eligible).ToArray();_data.markTarget=target;_data.markRemaining=MarkDuration;
            _data.markPower=(partySize>4?EightBudget:FourBudget)/Math.Max(1,Math.Min(8,partySize)-1)*scale;
        }
        public void QueueProtection(int target,float amount)
        {
            // Same caster/effect replaces pending protection for this target; no infinite queue.
            var list=new List<AegisFollowupShield>(_data.followups);list.RemoveAll(x=>x.target==target);
            if(target!=0&&amount>0&&CharacterDefinitionValidation.Finite(amount)&&list.Count<8)list.Add(new AegisFollowupShield{target=target,amount=amount,remaining=AegisPaladinData.FollowupDelay});
            _data.followups=list.ToArray();
        }
        public AegisFollowupShield[] TakeProtection(float seconds)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<0)return Array.Empty<AegisFollowupShield>();
            var due=new List<AegisFollowupShield>();var pending=new List<AegisFollowupShield>();
            foreach(var shield in _data.followups){shield.remaining=Math.Max(0,shield.remaining-seconds);if(shield.remaining==0)due.Add(shield.Copy());else pending.Add(shield);}
            _data.followups=pending.ToArray();return due.ToArray();
        }
        public float Consume(int member,bool direct)
        {
            if(!direct||_data.markRemaining<=0||member==0)return 0;
            int index=Array.IndexOf(_data.eligible,member);if(index<0)return 0;
            var list=new List<int>(_data.eligible);list.RemoveAt(index);_data.eligible=list.ToArray();return _data.markPower;
        }
        public void Advance(float seconds)
        {
            if(!CharacterDefinitionValidation.Finite(seconds)||seconds<0)return;
            _data.markRemaining=Math.Max(0,_data.markRemaining-seconds);
            if(_data.markRemaining==0){_data.eligible=Array.Empty<int>();_data.markTarget=0;_data.markPower=0;}
        }
        public AegisSunSnapshot Capture()=>new AegisSunSnapshot{shield=_data.shield,secondRecorded=_data.secondRecorded,secondShield=_data.secondShield,
            markRemaining=_data.markRemaining,markPower=_data.markPower,markTarget=_data.markTarget,eligible=(int[])_data.eligible.Clone(),followups=Array.ConvertAll(_data.followups,x=>x.Copy())};
        public bool Restore(AegisSunSnapshot s)
        {
            if(s==null||s.eligible==null||s.eligible.Length>7||!CharacterDefinitionValidation.Finite(s.markRemaining)||s.markRemaining<0||s.markRemaining>MarkDuration||
                !CharacterDefinitionValidation.Finite(s.markPower)||s.markPower<0||s.markPower>EightBudget||new HashSet<int>(s.eligible).Count!=s.eligible.Length||Array.IndexOf(s.eligible,0)>=0)return false;
            var followups=s.followups??Array.Empty<AegisFollowupShield>(); // Older job snapshots have no pending-protection field.
            if(followups.Length>8)return false;
            var targets=new HashSet<int>();foreach(var f in followups)if(f==null||f.target==0||!targets.Add(f.target)||!CharacterDefinitionValidation.Finite(f.amount)||f.amount<=0||!CharacterDefinitionValidation.Finite(f.remaining)||f.remaining<=0||f.remaining>AegisPaladinData.FollowupDelay)return false;
            _data=new AegisSunSnapshot{shield=s.shield,secondRecorded=s.secondRecorded,secondShield=s.secondShield,markRemaining=s.markRemaining,markPower=s.markPower,markTarget=s.markTarget,eligible=(int[])s.eligible.Clone(),followups=Array.ConvertAll(followups,x=>x.Copy())};return true;
        }
    }
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
        public static string PassiveSummary(string route)=>route==Bulwark?"HP·방어 스탯 +20% / 생성 보호막 +50%":route==Sun?"직접 공격 태양 불꽃 +10%":route==Mirror?"가드·보호막 흡수 25% 반사 / 카운터 무력화 +60":"기원 미선택";
        public static string Description(string route)
        {
            if(route==Bulwark)return $"불멸의 성벽 — 파티 보호\n상시: 최대 HP +{(HpMultiplier-1)*100:0}%, 물리·마법 방어 스탯 +20%, 생성 보호막 +{(ShieldMultiplier-1)*100:0}%\n아이기스의 성채: 생존 파티 전원에게 시전자 최대 HP {BulwarkShield*100:0}% 보호막 (보정 1회), 피격 이상·넉백 면역 {BulwarkDuration:0.#}초\n성광 {BulwarkCost:0} · 쿨타임 {BulwarkCooldown:0.#}초";
            if(route==Sun)return $"태양의 집행관 — 검술과 변신\n상시: 직접 공격 확정 피해의 {SunExtra*100:0}% 태양 불꽃\n여명 각성 — 쌍성검: {EffectSummary(route)}\n성광 {SunCost:0} · 쿨타임 {SunCooldown:0.#}초 · 변신 중 재사용 불가";
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
    public enum CombatDamageKind { Unclassified, Direct, Area, Periodic, Reflection, Extra, Deferred, System, Transfer }
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
