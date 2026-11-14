using System;
using UnityEngine;
namespace RaidRogue
{
    [DefaultExecutionOrder(450)]
    public sealed class AegisPaladin : MonoBehaviour
    {
        public readonly AegisDamageState DamageState=new AegisDamageState();
        private CharacterActor _actor;private PlayerController _player;private GameObject _gear;
        private BossController _lastThreatBoss;
        private Transform _sunMarkTarget;
        private readonly System.Collections.Generic.Dictionary<int,Vector3> _glyphPrevious=new System.Collections.Generic.Dictionary<int,Vector3>();
        private readonly LineRenderer[] _glyphLines=new LineRenderer[2];
        private Material _glyphMaterial;
        public string ThreatBossLabel
        {
            get{var boss=_lastThreatBoss!=null?_lastThreatBoss.transform:RaidCombatAuthority.Resolve(DamageState.LastThreatBossId);return CombatRange.LivingEnemy(boss)?boss.name:"없음";}
        }
        public bool HasAuxiliary(string id)=>CharacterSlots.IsNineKey(_actor?.Definition)&&_actor.HasAuxiliary(id);
        public float ThreatRange=>AegisPaladinData.AuxiliaryThreatRange(HasAuxiliary(AegisPaladinData.FarCall));
        public float CooldownScale(CharacterSkillDefinition skill)=>AegisPaladinData.AuxiliaryCooldownScale(skill?.id,HasAuxiliary(AegisPaladinData.Steadfast));
        public bool CanUseThreatCall(Transform target)=>target!=null&&CombatRange.InRange(transform,target,ThreatRange)&&
            (CombatRange.LivingEnemy(target)||target!=transform&&CombatRange.LivingPartyMember(target)&&target.GetComponent<PartyLife>()?.IsTank==true&&
             _lastThreatBoss!=null&&CombatRange.LivingEnemy(_lastThreatBoss.transform)&&_lastThreatBoss.CombatThreatOf(transform)>0);
        private LineRenderer _guardLine;private Material _guardMaterial;
        private Material _metal,_platinum;private float _debtTextTimer,_debtTextAmount;
        private static readonly Color HolyColor=new Color(1,.88f,.4f);
        private static readonly Color PlatinumColor=new Color(.83f,.85f,.9f);
        private static readonly CombatComboResourcePolicy NineKeyComboResource=
            new CombatComboResourcePolicy(AegisPaladinData.Holy,0,0,AegisPaladinData.NineKeyFinisherHoly,0,0,AegisPaladinData.NineKeyFinisherHoly);
        private static readonly CombatComboResourcePolicy LegacyComboResource=
            new CombatComboResourcePolicy(AegisPaladinData.Holy,0,0,AegisPaladinData.FinisherHoly,0,0,0);
        private static readonly CombatComboResourcePolicy SunSwordResource=new CombatComboResourcePolicy(AegisPaladinData.Holy,0,0,30,0,0,0);
        private static readonly CombatComboResourcePolicy SunShieldResource=new CombatComboResourcePolicy(AegisPaladinData.Holy,0,0,20,0,0,0);
        public string RouteId=>IsActive?_actor.RouteId:"";
        public bool NewSun=>IsActive&&CharacterSlots.IsNineKey(_actor.Definition)&&RouteId==AegisRouteData.Sun;
        public bool NewBulwark=>IsActive&&CharacterSlots.IsNineKey(_actor.Definition)&&RouteId==AegisRouteData.Bulwark;
        public bool NewMirror=>IsActive&&CharacterSlots.IsNineKey(_actor.Definition)&&RouteId==AegisRouteData.Mirror;
        public bool Citadel=>NewBulwark&&_actor.RuntimeState.CoreTrait==AegisPaladinData.Citadel;
        public bool Patrol=>NewBulwark&&_actor.RuntimeState.CoreTrait==AegisPaladinData.Patrol;
        public int GlyphCapacity=>Citadel?1:2;
        public float GlyphRadius=>Citadel?3:AegisGlyphState.Radius;
        public int ResponseCapacity=>NewMirror&&_actor.RuntimeState.CoreTrait==AegisPaladinData.Flash?1:2;
        public bool Continuous=>NewSun&&_actor.RuntimeState.CoreTrait==AegisPaladinData.Continuous;
        public bool Guardian=>NewSun&&_actor.RuntimeState.CoreTrait==AegisPaladinData.Guardian;
        public float SecondCost=>Continuous&&!DamageState.Sun.SecondShield?AegisPaladinData.ContinuousCost:AegisSunState.Cost;
        public float OutgoingShieldScale=>NewBulwark?1:AegisRouteData.ShieldScale(RouteId);
        public bool Transformed=>!NewSun&&RouteId==AegisRouteData.Sun&&DamageState.Route.Transformed;
        public bool IdentityBlocked=>NewBulwark&&DamageState.Glyphs.Glyphs.Count>=GlyphCapacity||Transformed||!NewMirror&&RouteId==AegisRouteData.Mirror&&DamageState.Route.Guard>0;
        public float EffectiveRange(CharacterSkillDefinition skill)=>skill==null?0:skill.id==AegisPaladinData.Id(CharacterSlot.Job2)&&CharacterSlots.IsNineKey(_actor.Definition)?ThreatRange:NewSun?skill.range:DamageState.Route.Range(RouteId,skill.comboChain>0,skill.range);
        public float GcdScale(CharacterSkillDefinition skill)
        {
            if(skill==null||!skill.usesGcd||skill.gcd<=0)return 1;
            var combat=_player?.combatController;
            float reduction=combat!=null?combat.baseGcdDuration-combat.currentGcdDuration:0;
            float minimum=combat!=null?combat.minGcdDuration:.6f;
            return AegisRouteData.GcdDuration(skill.gcd,reduction,minimum,NewSun?1:DamageState.Route.GcdScale(RouteId,skill.comboChain>0))/skill.gcd/Mathf.Max(.25f,1+CharacterKit.Bonus(transform,"haste"));
        }
        public string RouteName=>Array.Find(_actor?.Definition?.routes??Array.Empty<CharacterRouteDefinition>(),r=>r.id==RouteId)?.name??"기원 선택 필요";
        public bool IsActive=>_actor?.Definition?.id==AegisPaladinData.JobId;
        public float PhysicalDefense=>IsActive?NewBulwark?_actor.Definition.physicalDefense:AegisRouteData.DefenseRating(_actor.Definition.physicalDefense,RouteId):0;
        public float MagicDefense=>IsActive?NewBulwark?_actor.Definition.magicDefense:AegisRouteData.DefenseRating(_actor.Definition.magicDefense,RouteId):0;
        public float Tenacity=>IsActive?_actor.Definition.tenacity:0;
        public static AegisPaladin Ensure(GameObject go)=>go.GetComponent<AegisPaladin>()??go.AddComponent<AegisPaladin>();
        private void Awake()
        {
            _actor=GetComponent<CharacterActor>();_player=GetComponent<PlayerController>();
            AegisExtensions.Ensure(gameObject);
            _actor.RegisterHandler("aegis.identity",(skill,target)=>Identity(skill));
            foreach(var slot in CharacterSlots.All)if(slot<CharacterSlot.Identity&&slot!=CharacterSlot.Movement)
            {var captured=slot;_actor.RegisterHandler("aegis."+slot,(skill,target)=>Execute(captured,skill,target));}
        }
        public void ClearTransient(){DamageState.Clear();_glyphPrevious.Clear();foreach(var line in _glyphLines)if(line!=null)line.enabled=false;_lastThreatBoss=null;_sunMarkTarget=null;_player?.CancelSkillRush();_debtTextAmount=_debtTextTimer=0;if(_guardLine!=null)_guardLine.enabled=false;}
        public void ReleaseRouteEffects()
        {
            ClearTransient();if(RaidCombatAuthority.Replica)return;
            int source=CombatMetricsManager.SourceId(transform);
            foreach(var member in PartyLife.OrderedMembers())
            {
                member.GetComponent<PlayerController>()?.RemoveShieldsFrom(source);
                member.GetComponent<PartyBotAI>()?.RemoveShieldsFrom(source);
                member.GetComponent<CombatStatusEffects>()?.RemoveProtectionFrom(source);
            }
        }
        public void ApplyRouteChoice()
        {
            if(!IsActive||RaidCombatAuthority.Replica||_player==null)return;
            ReleaseRouteEffects();
            RecalculateStats();
        }
        public void RecalculateStats()
        {
            if(!IsActive||RaidCombatAuthority.Replica||_player==null)return;
            float maximum=(NewBulwark?_actor.Definition.baseHp:AegisRouteData.MaximumHp(_actor.Definition.baseHp,RouteId))*(1+CharacterKit.Bonus(transform,"maxHp"));
            _player.currentHp=AegisRouteData.PreserveHealthRatio(_player.currentHp,_player.maxHp,maximum);_player.maxHp=maximum;
            float weight=CharacterRosterRules.HeavyArmorSpeed(_player.currentShield>0,CharacterEvolutionCards.Has(_actor,false),CharacterEvolutionCards.Has(_actor,true));
            _player.moveSpeed=AegisRouteData.MoveSpeed(_actor.Definition,_player.ghostStepStacks)*(weight/_actor.Definition.moveSpeedMultiplier)*(1+CharacterKit.Bonus(transform,"speed"));
        }
        private bool Identity(CharacterSkillDefinition skill)
        {
            if(!IsActive||RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||_player==null||_player.IsDead||IdentityBlocked&&!_actor.ActionRecall||skill.id!=AegisRouteData.Identity(RouteId))return false;
            if(NewSun){DamageState.Sun.Toggle();FloatingTextPopup.Spawn(transform.position+Vector3.up*2,DamageState.Sun.Shield?"여명의 맹세 · 방패":"여명의 맹세 · 검",HolyColor,false);return true;}
            if(NewMirror)
            {
                var support=_actor.ActionSupportTarget;
                if(!CombatRange.LivingPartyMember(support)||!CombatRange.InRange(transform,support,AegisMirrorState.Range))support=transform;
                if(!DamageState.Mirror.Begin(CombatMetricsManager.SourceId(support)))return false;
                if(support!=transform)_player.PlaySkillLineVisual(support.position,HolyColor);
                FloatingTextPopup.Spawn(support.position+Vector3.up*2,"거울 수호",HolyColor,false);return true;
            }
            if(NewBulwark)
            {
                if(_actor.ActionRecall)
                {
                    if(!Patrol||!DamageState.Glyphs.RecallUnused())return false;
                    _actor.GainResource(AegisPaladinData.Holy,20);
                    FloatingTextPopup.Spawn(transform.position+Vector3.up*2,"미사용 문양 회수 · 성광 +20",HolyColor,false);return true;
                }
                var point=_actor.ActionGroundPoint;
                if(!point.HasValue||!CanPlaceGlyph(point.Value))return false;
                return DamageState.Glyphs.Install(point.Value.x,point.Value.z,GlyphCapacity);
            }
            if(!DamageState.Route.Activate(RouteId,transform.eulerAngles.y))return false;
            if(RouteId==AegisRouteData.Bulwark)
            {
                foreach(var member in PartyLife.OrderedMembers())if(CombatRange.LivingPartyMember(member))
                {
                    CombatStatusEffects.Shield(member,transform,skill.id,_player.maxHp*AegisRouteData.BulwarkShield,AegisRouteData.BulwarkDuration);
                    CombatStatusEffects.Ensure(member.gameObject).GrantKnockbackImmunity(AegisRouteData.BulwarkDuration,transform);
                    CombatStatusEffects.Ensure(member.gameObject).GrantControlImmunity(AegisRouteData.BulwarkDuration,transform);
                }
                Pulse(transform.position,3);
            }
            FloatingTextPopup.Spawn(transform.position+Vector3.up*2.8f,skill.name,HolyColor,true);return true;
        }
        public void ReflectAbsorption(double absorbed,CombatHitContext hit)
        {
            if(NewMirror)return;
            if(!IsActive||RaidCombatAuthority.Replica||_player==null||_player.IsDead||!CombatRange.LivingEnemy(hit.Attacker))return;
            double reflected=DamageState.Route.Reflect(RouteId,hit.Kind,absorbed,_player.maxHp);
            if(reflected>0)CombatSecondaryDamage.Apply(transform,hit.Attacker,reflected,AegisRouteData.Mirror+".shield-reflect/"+hit.SkillId,CombatDamageKind.Reflection);
        }
        public bool TryGuard(CombatHitContext hit,float confirmedIncoming)
        {
            if(NewMirror)return false;
            if(!IsActive||RaidCombatAuthority.Replica||_player==null||_player.IsDead||!CombatRange.LivingEnemy(hit.Attacker))return false;
            Vector3 direction=hit.Attacker.position-transform.position;direction.y=0;
            Vector3 facing=Quaternion.Euler(0,DamageState.Route.GuardYaw,0)*Vector3.forward;
            float dot=direction.sqrMagnitude<.00001f?1:Vector3.Dot(facing,direction.normalized);
            if(!DamageState.Route.TryGuard(RouteId,hit.Kind,hit.IgnoreGuard,dot,confirmedIncoming,_player.maxHp,out double retaliation))return false;
            ShieldAbsorbed(confirmedIncoming); // Same one-second defense-resource gate as real shield absorption.
            FloatingTextPopup.Spawn(transform.position+Vector3.up*2.4f,"JUST GUARD · 피해 0",Color.cyan,true);
            var counter=CombatCounterTarget.Ensure(hit.Attacker);
            bool countered=counter.TryCounter(transform,AegisPaladinData.MeleeRange,AegisPaladinData.CounterStagger+AegisRouteData.CounterExtraStagger,attackFacing:facing);
            ReflectAbsorption(confirmedIncoming,hit); // Passive block reflection shares the shield reflection ICD.
            if(retaliation>0)
            {
                Vector3 center=transform.position;
                var boss=StageManager.Instance?.boss;
                if(boss!=null&&InsideBurst(center,boss.transform,AegisRouteData.RetaliationRadius))GuardShockwaveHit(boss.transform,hit,retaliation,countered);
                foreach(var add in FindObjectsByType<BossAddMinion>(FindObjectsSortMode.None))if(InsideBurst(center,add.transform,AegisRouteData.RetaliationRadius))GuardShockwaveHit(add.transform,hit,retaliation,countered);
                Pulse(center,AegisRouteData.RetaliationRadius);
                _player.PlaySkillLineVisual(hit.Attacker.position,Color.cyan);
            }
            return true;
        }
        private void GuardShockwaveHit(Transform enemy,CombatHitContext origin,double amount,bool countered)
        {
            float actual=CombatSecondaryDamage.Apply(transform,enemy,amount,AegisRouteData.Identity(RouteId)+"/"+origin.SkillId,CombatDamageKind.Reflection);
            if(actual>0&&!(countered&&enemy==origin.Attacker))CombatCounterTarget.Ensure(enemy).AddStagger(AegisPaladinData.CounterStagger+AegisPaladinData.CounterBonusStagger);
        }
        public void ShieldAbsorbed(double amount)
        {if(!RaidCombatAuthority.Replica&&IsActive&&CombatRange.LivingPartyMember(transform)&&DamageState.ShieldAbsorbed(amount,!CharacterSlots.IsNineKey(_actor.Definition)))_actor.GainResource(AegisPaladinData.Holy,AegisPaladinData.AbsorbHoly);}
        private void Update()
        {
            if(_gear!=null)_gear.SetActive(IsActive);
            if(!IsActive){if(_guardLine!=null)_guardLine.enabled=false;return;}if(_gear==null)BuildGear();
            if(_player!=null&&_player.normalBodyColor!=PlatinumColor)_player.ApplyCharacterAppearance(PlatinumColor,HolyColor);
            UpdateRouteVisuals();
            DrawGlyphs();
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||_player==null||_player.IsDead)return;
            float due=(float)DamageState.Advance(Time.deltaTime);
            if(NewBulwark)VisitGlyphs();
            if(NewSun)foreach(var followup in DamageState.Sun.TakeProtection(Time.deltaTime))
                foreach(var member in PartyLife.OrderedMembers())
                    if(CombatMetricsManager.SourceId(member)==followup.target&&CombatRange.LivingPartyMember(member)&&CombatRange.InRange(transform,member,AegisSunState.AllyRange))
                        CombatStatusEffects.Shield(member,transform,"aegis.oath.protection",followup.amount,AegisSunState.ShieldDuration,true);
            if(due>0){_player.ApplyDeferredDamage(due);_debtTextAmount+=due;}
            _debtTextTimer+=Time.deltaTime;
            if(_debtTextTimer>=1){if(_debtTextAmount>.01f)FloatingTextPopup.Spawn(transform.position+Vector3.up*2,"지연 피해 -"+_debtTextAmount.ToString("0.#"),new Color(1,.65f,.2f),false);_debtTextTimer=_debtTextAmount=0;}
        }
        private bool Execute(CharacterSlot slot,CharacterSkillDefinition skill,Transform target)
        {
            if(!IsActive||RaidCombatAuthority.Replica||_player==null||_player.IsDead||StageManager.PlayerSkillsSuspended)return false;
            if(slot==CharacterSlot.Job1){bool nine=CharacterSlots.IsNineKey(_actor.Definition);DamageState.Activate(nine,HasAuxiliary(AegisPaladinData.Steadfast));FloatingTextPopup.Spawn(transform.position+Vector3.up*2,nine?"불굴의 신념 · 피해 감소":"불굴의 신념 · 피해 분산",HolyColor,false);return true;}
            if(slot==CharacterSlot.Job2&&CharacterSlots.IsNineKey(_actor.Definition))
            {
                if(!CanUseThreatCall(target))return false;
                if(CombatRange.LivingEnemy(target))
                {
                    var boss=target.GetComponent<BossController>();
                    if(boss!=null){boss.AddCombatThreat(transform,0,true);boss.SetAggroTarget(transform);RememberThreatBoss(boss);}
                    else target.GetComponent<BossAddMinion>().TauntBy(transform,_actor.Definition.name);
                    return true;
                }
                if(!CombatRange.LivingPartyMember(target)||target==transform||target.GetComponent<PartyLife>()?.IsTank!=true||
                    _lastThreatBoss==null||!CombatRange.LivingEnemy(_lastThreatBoss.transform))return false;
                return _lastThreatBoss.TransferCombatThreat(transform,target,.8f); // Prototype tuning, not final balance.
            }
            if(!CombatRange.LivingEnemy(target)||!CombatRange.InRange(transform,target,EffectiveRange(skill)))return false;
            if(slot==CharacterSlot.Job2)
            {
                var boss=target.GetComponent<BossController>();if(boss!=null){boss.AddCombatThreat(transform,0,true);boss.SetAggroTarget(transform);}
                else target.GetComponent<BossAddMinion>().TauntBy(transform,_actor.Definition.name);
                foreach(var member in PartyLife.OrderedMembers())
                {
                    Vector3 delta=member.position-transform.position;delta.y=0;
                    if(!CombatRange.LivingPartyMember(member)||delta.magnitude>AegisPaladinData.CallRadius)continue;
                    CombatStatusEffects.Ensure(member.gameObject).CleanseOne(AegisPaladinData.DispelTags);
                    float max=member.GetComponent<PlayerController>()?.maxHp??member.GetComponent<PartyBotAI>().maxHp;
                    CombatStatusEffects.Shield(member,transform,skill.id,max*AegisPaladinData.CallShieldRatio,AegisPaladinData.CallShieldDuration);
                }
                _player.PlaySkillLineVisual(target.position,HolyColor);return true;
            }
            if(slot==CharacterSlot.Counter)
            {
                Vector3 forwardTarget=target.position-transform.position;forwardTarget.y=0;
                if(Vector3.Dot(transform.forward,forwardTarget.normalized)<0)return false;
                var counterTarget=CombatCounterTarget.Ensure(target);int opportunity=counterTarget.OpportunityId;
                bool counter=counterTarget.CanCounter(transform,skill.range);
                if(!Hit(target,skill.id,skill.name,AegisPaladinData.CounterPower,0,1))return false;
                if(!counter||!counterTarget.TryCounter(transform,skill.range,AegisPaladinData.CounterStagger+(RouteId==AegisRouteData.Mirror&&!NewMirror?AegisRouteData.CounterExtraStagger:0),opportunity))
                    CombatCounterTarget.Ensure(target).AddStagger(AegisPaladinData.CounterStagger);
                return true;
            }
            if(slot==CharacterSlot.OffGcd1)
            {
                if(CharacterSlots.IsNineKey(_actor.Definition))
                {
                    if(skill.id==AegisPaladinData.Hand){CombatStatusEffects.Shield(target,transform,skill.id,_player.maxHp*.1f,6,true);return true;}
                    bool slam=skill.id==AegisPaladinData.Slam;
                    if(!Hit(target,skill.id,skill.name,slam?140:180,slam?100:20,1))return false;
                    if(!slam)_actor.GainResource(AegisPaladinData.Holy,10);
                    return true;
                }
                Vector3 center=target.position;
                if(!Hit(target,skill.id,skill.name,AegisPaladinData.BurstPower,AegisPaladinData.BurstStagger,AegisPaladinData.BurstThreatMultiplier))return false;
                var boss=StageManager.Instance?.boss;
                if(boss!=null&&boss.transform!=target&&InsideBurst(center,boss.transform))Hit(boss.transform,skill.id,skill.name,AegisPaladinData.BurstPower,AegisPaladinData.BurstStagger,AegisPaladinData.BurstThreatMultiplier);
                foreach(var add in FindObjectsByType<BossAddMinion>(FindObjectsSortMode.None))if(add.transform!=target&&InsideBurst(center,add.transform))Hit(add.transform,skill.id,skill.name,AegisPaladinData.BurstPower,AegisPaladinData.BurstStagger,AegisPaladinData.BurstThreatMultiplier);
                Pulse(center,AegisPaladinData.BurstRadius);return true;
            }
            var state=_actor.Snapshot;bool sword=slot==CharacterSlot.Gcd1;int stage=sword?state.combo1:state.combo2;
            string name=(sword?AegisPaladinData.SwordNames:AegisPaladinData.ShieldNames)[stage-1];
            int power=(sword?AegisPaladinData.SwordPower:AegisPaladinData.ShieldPower)[stage-1];
            float stagger=(sword?AegisPaladinData.SwordStagger:AegisPaladinData.ShieldStagger)[stage-1];
            bool response=NewMirror&&sword&&stage==3&&DamageState.Mirror.Count>0;
            if(response)power+=Mathf.RoundToInt(AegisMirrorState.ResponsePower(state.coreTrait,DamageState.Mirror.FirstAge));
            if(!sword&&(skill.id==AegisPaladinData.Wave||skill.id==AegisPaladinData.Judgment))
            {name=AegisPaladinData.SecondName(skill.id,stage);power=Mathf.RoundToInt(power*.8f);if(skill.id==AegisPaladinData.Judgment)stagger*=2;}
            Vector3 comboCenter=target.position;
            bool secondShield=NewSun&&DamageState.Sun.SecondShield;
            float cost=SecondCost;float attackScale=Continuous?.5f:1;
            bool enhanced=NewSun&&!sword&&stage==3&&_actor.SpendResource(AegisPaladinData.Holy,cost);
            if(enhanced&&!secondShield)power+=Mathf.RoundToInt(AegisSunState.SwordBonus*attackScale);
            if(!Hit(target,skill.id,name,power,stagger,1))
            {if(enhanced)_actor.GainResource(AegisPaladinData.Holy,cost);return false;}
            if(NewSun&&!sword&&stage==1)DamageState.Sun.BeginSecond();
            if(response)
            {
                int protectedTarget=DamageState.Mirror.Consume();
                if(state.coreTrait==AegisPaladinData.Reflection)
                    foreach(var member in PartyLife.OrderedMembers())
                        if(CombatMetricsManager.SourceId(member)==protectedTarget&&CombatRange.LivingPartyMember(member)&&CombatRange.InRange(transform,member,AegisMirrorState.Range))
                            CombatStatusEffects.Shield(member,transform,"aegis.mirror.followup",90,6,true);
            }
            if(Transformed)
            {
                var boss=StageManager.Instance?.boss;
                if(boss!=null&&boss.transform!=target&&InsideBurst(comboCenter,boss.transform,AegisRouteData.SunRadius))Hit(boss.transform,skill.id,name,power,stagger,1);
                foreach(var add in FindObjectsByType<BossAddMinion>(FindObjectsSortMode.None))if(add.transform!=target&&InsideBurst(comboCenter,add.transform,AegisRouteData.SunRadius))Hit(add.transform,skill.id,name,power,stagger,1);
                Pulse(comboCenter,AegisRouteData.SunRadius);
            }
            if(stage==3)
            {
                if(CharacterSlots.IsNineKey(_actor.Definition))_actor.ResolvePaladinUltimateGauge(skill);
                if(NewSun)
                {
                    if(sword)
                    {
                        _actor.ResolveComboResource(skill,DamageState.Sun.Shield?SunShieldResource:SunSwordResource,true);
                        if(DamageState.Sun.Shield)CombatStatusEffects.Shield(transform,transform,"aegis.oath.small",_player.maxHp*AegisSunState.SmallShieldRatio,AegisSunState.ShieldDuration,true);
                    }
                    else
                    {
                        if(enhanced&&secondShield)
                        {
                            ProtectOath(transform);
                            var ally=_actor.ActionSupportTarget;
                            if(ally!=transform&&CombatRange.LivingPartyMember(ally)&&CombatRange.InRange(transform,ally,AegisSunState.AllyRange))
                                ProtectOath(ally);
                        }
                        else if(enhanced)
                        {
                            var members=PartyLife.OrderedMembers();var ids=new System.Collections.Generic.List<int>();
                            foreach(var member in members)if(CombatRange.LivingPartyMember(member))ids.Add(CombatMetricsManager.SourceId(member));
                            DamageState.Sun.SetMark(RaidCombatAuthority.Id(target),CombatMetricsManager.SourceId(transform),ids,members.Count,attackScale);
                            _sunMarkTarget=target;
                            FloatingTextPopup.Spawn(target.position+Vector3.up*2,"협공 문장",HolyColor,false);
                        }
                        DamageState.Sun.FinishSecond();
                    }
                }
                else if(CharacterSlots.IsNineKey(_actor.Definition))_actor.ResolveComboResource(skill,NineKeyComboResource,true);
                else if(sword){_actor.ResolveComboResource(skill,LegacyComboResource,true);_actor.ReduceCooldowns(AegisPaladinData.ReducedSkills,AegisPaladinData.CooldownReduction);}
                if(!sword&&skill.id==AegisPaladinData.Id(CharacterSlot.Gcd2))CombatStatusEffects.Shield(transform,transform,skill.id,_player.maxHp*AegisPaladinData.FinisherShieldRatio,AegisPaladinData.FinisherShieldDuration,true);
            }
            return true;
        }
        private bool InsideBurst(Vector3 center,Transform enemy,float radius=AegisPaladinData.BurstRadius)
        {return CombatRange.LivingEnemy(enemy)&&CombatRange.CircleTouches(center,enemy,radius);}
        public static float ApplyMirrorProtection(Transform recipient,float damage,CombatHitContext hit)
        {
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!CombatRange.LivingPartyMember(recipient)||!CombatRange.LivingEnemy(hit.Attacker))return damage;
            int target=CombatMetricsManager.SourceId(recipient);bool protectedHit=false;
            foreach(var member in PartyLife.OrderedMembers())
            {
                var mirror=member.GetComponent<AegisPaladin>();
                if(mirror==null||!mirror.NewMirror||!CombatRange.LivingPartyMember(member)||
                    member!=recipient&&(mirror.DamageState.Mirror.Support!=target||!CombatRange.InRange(member,recipient,AegisMirrorState.Range)))continue;
                int before=mirror.DamageState.Mirror.Count;
                if(!mirror.DamageState.Mirror.Protect(hit.Kind,hit.IgnoreGuard,hit.Defense==CombatDefenseType.Bypass,damage,mirror.ResponseCapacity))continue;
                protectedHit=true;
                if(mirror.DamageState.Mirror.Count>before)FloatingTextPopup.Spawn(member.position+Vector3.up*2,"정밀 수호 · 응보",HolyColor,false);
            }
            // Identical protection from multiple casters does not multiply damage reduction.
            return protectedHit?damage*(1-AegisMirrorState.Reduction):damage;
        }
        private void ProtectOath(Transform target)
        {
            float amount=_player.maxHp*AegisSunState.ShieldRatio*(Guardian?.5f:1);
            CombatStatusEffects.Shield(target,transform,"aegis.oath.protection",amount,AegisSunState.ShieldDuration,true);
            if(Guardian)DamageState.Sun.QueueProtection(CombatMetricsManager.SourceId(target),amount);
        }
        private bool Hit(Transform enemy,string origin,string name,int power,float stagger,float threat)
            =>DealAttack(enemy,origin,name,power,stagger,threat)>0 ||
                AiSpectatorDamage.ZeroHpDamageMode(transform)&&CombatRange.LivingPartyMember(transform)&&
                CombatRange.LivingEnemy(enemy)&&!StageManager.PlayerSkillsSuspended;
        public float DealAttack(Transform enemy,string origin,string name,float power,float stagger,float threat,CombatDamageKind kind=CombatDamageKind.Direct,bool scaleAttack=true)
        {
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!CombatRange.LivingPartyMember(transform)||!CombatRange.LivingEnemy(enemy))return 0;
            int damage=Mathf.RoundToInt(power*_player.combatController.GetPlayerDamageBuffMultiplier());
            float before,after;var boss=enemy.GetComponent<BossController>();
            if(boss!=null){before=boss.currentHp;boss.ReceiveRoleSkillAttack("이지스 팔라딘",name,0,damage,HolyColor,false,source:transform,origin:origin,kind:kind,scaleAttack:scaleAttack);after=boss.currentHp;}
            else {var add=enemy.GetComponent<BossAddMinion>();before=add.currentHp;add.ReceivePlayerAttack("이지스 팔라딘",name,damage,HolyColor,false,source:transform,origin:origin,kind:kind,scaleAttack:scaleAttack);after=add.currentHp;}
            float actual=Mathf.Max(0,before-after);
            if(actual>0&&boss!=null&&kind==CombatDamageKind.Direct)RememberThreatBoss(boss);
            if(actual<=0)
            {
                if(stagger>0&&AiSpectatorDamage.ZeroHpDamageMode(transform))CombatCounterTarget.Ensure(enemy).AddStagger(stagger);
                return 0;
            }
            if(!NewSun&&RouteId==AegisRouteData.Sun&&kind==CombatDamageKind.Direct&&AegisExtensionData.TriggersSun(origin))CombatSecondaryDamage.Apply(transform,enemy,CombatDamageRules.SunDamage(actual),origin,CombatDamageKind.Extra,true);
            if(boss!=null&&threat>1)boss.AddCombatThreat(transform,actual*(threat-1));
            if(stagger>0)CombatCounterTarget.Ensure(enemy).AddStagger(stagger);
            if(kind==CombatDamageKind.Direct){_player.FaceAttackTarget(enemy);_player.PlaySkillLineVisual(enemy.position,HolyColor);
                if(_player.hasVampiricBlade)_player.TriggerVampiricBladeHeal();}return actual;
        }
        public void ShowPulse(Vector3 center,float radius)=>Pulse(center,radius);
        public static void ResolveCooperation(Transform source,Transform target,string origin,CombatDamageKind kind)
        {
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||kind!=CombatDamageKind.Direct||!CombatRange.LivingPartyMember(source)||!CombatRange.LivingEnemy(target))return;
            var actor=source.GetComponent<CharacterActor>();
            bool direct=actor?.OwnsDirectSkillHit(origin)==true||actor?.Definition?.legacyRole>0&&origin=="input.legacy.skill";
            if(!direct)return;
            foreach(var member in PartyLife.OrderedMembers())
            {
                var paladin=member.GetComponent<AegisPaladin>();
                if(paladin==null||!paladin.NewSun||!CombatRange.LivingPartyMember(member)||(paladin._sunMarkTarget!=target&&(paladin.DamageState.Sun.MarkTarget==0||paladin.DamageState.Sun.MarkTarget!=RaidCombatAuthority.Id(target))))continue;
                float power=paladin.DamageState.Sun.Consume(CombatMetricsManager.SourceId(source),true);
                if(power>0)CombatSecondaryDamage.Apply(member,target,power,"aegis.oath.cooperation",CombatDamageKind.Extra);
            }
        }
        private void RememberThreatBoss(BossController boss)
        {_lastThreatBoss=boss;DamageState.LastThreatBossId=RaidCombatAuthority.Id(boss.transform);}
        private void Pulse(Vector3 center,float radius)
        {
            // Cosmetic lines only. Host relay preserves exactly-once damage separation.
            for(int i=0;i<16;i++){float a=i*Mathf.PI/8,b=(i+1)*Mathf.PI/8;Vector3 p=new Vector3(center.x,.15f,center.z)+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius,q=new Vector3(center.x,.15f,center.z)+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*radius;
                if(RaidCombatAuthority.Host)RaidNetworkSession.Instance.BroadcastEffect(p,q,HolyColor,"",false,1);
                var obj=new GameObject("DawnlightPulse");var line=obj.AddComponent<LineRenderer>();line.positionCount=2;line.SetPosition(0,p);line.SetPosition(1,q);line.startWidth=line.endWidth=.06f;line.startColor=line.endColor=HolyColor;line.sharedMaterial=new Material(Shader.Find("Sprites/Default"));obj.AddComponent<RaidTransientVisual>();
            }
        }
        private void UpdateRouteVisuals()
        {
            var shield=_gear.transform.Find("GreatShield");
            shield.localScale=Transformed?new Vector3(.12f,.14f,1.15f):new Vector3(.65f,1.1f,.16f);
            shield.localPosition=Transformed?new Vector3(-.6f,.25f,.55f):new Vector3(-.6f,.1f,.35f);
            bool guard=!NewMirror&&RouteId==AegisRouteData.Mirror&&DamageState.Route.Guard>0;
            if(guard&&_guardLine==null)
            {
                var go=new GameObject("AegisGuardCone");go.transform.SetParent(transform,false);_guardLine=go.AddComponent<LineRenderer>();
                _guardMaterial=new Material(Shader.Find("Sprites/Default"));_guardLine.sharedMaterial=_guardMaterial;_guardLine.useWorldSpace=true;_guardLine.startWidth=_guardLine.endWidth=.09f;_guardLine.positionCount=27;
            }
            if(_guardLine==null)return;_guardLine.enabled=guard;if(!guard)return;
            Color color=DamageState.Route.GuardFeedback>0?Color.white:new Color(.3f,.85f,1,.65f);_guardLine.startColor=_guardLine.endColor=color;
            Vector3 center=new Vector3(transform.position.x,.16f,transform.position.z);_guardLine.SetPosition(0,center);_guardLine.SetPosition(26,center);
            for(int i=0;i<=24;i++){float angle=(DamageState.Route.GuardYaw-AegisRouteData.GuardAngle/2+AegisRouteData.GuardAngle*i/24)*Mathf.Deg2Rad;_guardLine.SetPosition(i+1,center+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(CombatRange.Radius(transform)+1.4f));}
        }
        private void BuildGear()
        {
            _gear=new GameObject("AegisSwordAndShield");_gear.transform.SetParent(transform,false);
            _metal=new Material(Shader.Find("Sprites/Default"));_metal.color=HolyColor;
            _platinum=new Material(Shader.Find("Sprites/Default"));_platinum.color=new Color(.83f,.85f,.9f);
            Piece("Sword",new Vector3(.55f,.25f,.55f),new Vector3(.12f,.14f,1.15f));
            Piece("Guard",new Vector3(.55f,.25f,.05f),new Vector3(.45f,.16f,.12f));
            Piece("GreatShield",new Vector3(-.6f,.1f,.35f),new Vector3(.65f,1.1f,.16f));
            Piece("PlatinumBreastplate",new Vector3(0,.3f,.26f),new Vector3(.68f,.7f,.2f),true);
            Piece("PlatinumShoulderL",new Vector3(-.42f,.58f,0),new Vector3(.28f,.22f,.38f),true);
            Piece("PlatinumShoulderR",new Vector3(.42f,.58f,0),new Vector3(.28f,.22f,.38f),true);
        }
        private void Piece(string name,Vector3 position,Vector3 size,bool armor=false)
        {var p=GameObject.CreatePrimitive(PrimitiveType.Cube);p.name=name;p.transform.SetParent(_gear.transform,false);p.transform.localPosition=position;p.transform.localScale=size;Destroy(p.GetComponent<Collider>());p.GetComponent<Renderer>().sharedMaterial=armor?_platinum:_metal;}
        public bool TryPointerGlyph(out Vector3 point)
        {
            point=default;var camera=Camera.main;if(camera==null||LocalGameplayInput.PointerOverUI(LocalGameplayInput.MousePosition))return false;
            var plane=new Plane(Vector3.up,Vector3.zero);
            var ray=camera.ScreenPointToRay(LocalGameplayInput.MousePosition);
            if(!plane.Raycast(ray,out float distance))return false;
            point=ray.GetPoint(distance);return CanPlaceGlyph(point);
        }
        public bool CanPlaceGlyph(Vector3 point)
        {
            if(!NewBulwark||!CharacterDefinitionValidation.Finite(point.x)||!CharacterDefinitionValidation.Finite(point.z)||_player==null)return false;
            Vector2 p=new Vector2(point.x,point.z),self=new Vector2(transform.position.x,transform.position.z);
            float arena=RaidApproachField.Instance?.Showing==true?RaidApproachField.Instance.FieldRadius:_player.arenaBoundaryRadius;
            if((p-self).sqrMagnitude>AegisGlyphState.Range*AegisGlyphState.Range||p.magnitude>arena)return false;
            var boss=StageManager.Instance?.boss;
            if(boss?.JustitiaEncounter==true&&(JustitiaNormalRules.Clamp(point,boss.JustitiaHalfWidth,boss.JustitiaHalfDepth,0)-point).sqrMagnitude>.001f)return false;
            if(boss?.JanusEncounter==true&&p.magnitude>boss.janus.arenaRadius)return false;
            foreach(var hit in Physics.RaycastAll(new Vector3(point.x,transform.position.y+3,point.z),Vector3.down,6,~0,QueryTriggerInteraction.Ignore))
                if(hit.normal.y>.8f&&hit.collider.GetComponentInParent<PartyLife>()==null&&hit.collider.GetComponentInParent<BossController>()==null&&hit.collider.GetComponentInParent<BossAddMinion>()==null)return true;
            return false;
        }
        private void VisitGlyphs()
        {
            var alive=new System.Collections.Generic.HashSet<int>();
            foreach(var member in PartyLife.OrderedMembers())
            {
                if(!CombatRange.LivingPartyMember(member))continue;
                int id=CombatMetricsManager.SourceId(member);alive.Add(id);Vector3 current=member.position;
                if(!_glyphPrevious.TryGetValue(id,out Vector3 previous)||(current-previous).sqrMagnitude>100)previous=current;
                for(int i=0;i<DamageState.Glyphs.Glyphs.Count;i++)
                    if(DamageState.Glyphs.Visit(i,id,previous.x,previous.z,current.x,current.z,GlyphRadius))
                        CombatStatusEffects.Shield(member,transform,"aegis.glyph.protection",_player.maxHp*(Citadel?.15f:AegisGlyphState.ShieldRatio),AegisGlyphState.ShieldDuration,true);
                _glyphPrevious[id]=current;
            }
            foreach(int id in new System.Collections.Generic.List<int>(_glyphPrevious.Keys))if(!alive.Contains(id))_glyphPrevious.Remove(id);
        }
        private void DrawGlyphs()
        {
            for(int i=0;i<2;i++)
            {
                bool active=NewBulwark&&i<DamageState.Glyphs.Glyphs.Count&&!StageManager.PlayerSkillsSuspended;
                if(!active){if(_glyphLines[i]!=null)_glyphLines[i].enabled=false;continue;}
                if(_glyphLines[i]==null)
                {
                    if(_glyphMaterial==null)_glyphMaterial=new Material(Shader.Find("Sprites/Default"));
                    var go=new GameObject("DawnGlyph"+i);go.transform.SetParent(transform,false);
                    var line=go.AddComponent<LineRenderer>();line.useWorldSpace=true;line.loop=true;line.positionCount=32;line.startWidth=line.endWidth=.08f;
                    line.startColor=line.endColor=HolyColor;line.sharedMaterial=_glyphMaterial;_glyphLines[i]=line;
                }
                var glyph=DamageState.Glyphs.Glyphs[i];_glyphLines[i].enabled=true;
                float radius=GlyphRadius;
                for(int j=0;j<32;j++){float angle=j*Mathf.PI/16;_glyphLines[i].SetPosition(j,new Vector3(glyph.x+Mathf.Cos(angle)*radius,.08f,glyph.z+Mathf.Sin(angle)*radius));}
            }
        }
        private void OnDestroy(){if(_metal!=null)Destroy(_metal);if(_platinum!=null)Destroy(_platinum);if(_guardMaterial!=null)Destroy(_guardMaterial);if(_glyphMaterial!=null)Destroy(_glyphMaterial);}
    }
}
