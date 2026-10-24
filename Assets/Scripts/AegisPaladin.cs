using System;
using UnityEngine;
namespace RaidRogue
{
    [DefaultExecutionOrder(450)]
    public sealed class AegisPaladin : MonoBehaviour
    {
        public readonly AegisDamageState DamageState=new AegisDamageState();
        private CharacterActor _actor;private PlayerController _player;private GameObject _gear;
        private LineRenderer _guardLine;private Material _guardMaterial;
        private Material _metal,_platinum;private float _debtTextTimer,_debtTextAmount;
        private static readonly Color HolyColor=new Color(1,.88f,.4f);
        private static readonly Color PlatinumColor=new Color(.83f,.85f,.9f);
        public string RouteId=>IsActive?_actor.RouteId:"";
        public float OutgoingShieldScale=>AegisRouteData.ShieldScale(RouteId);
        public bool Transformed=>RouteId==AegisRouteData.Sun&&DamageState.Route.Transformed;
        public bool IdentityBlocked=>Transformed||RouteId==AegisRouteData.Mirror&&DamageState.Route.Guard>0;
        public float EffectiveRange(CharacterSkillDefinition skill)=>skill==null?0:DamageState.Route.Range(RouteId,skill.comboChain>0,skill.range);
        public float GcdScale(CharacterSkillDefinition skill)
        {
            if(skill==null||!skill.usesGcd||skill.gcd<=0)return 1;
            var combat=_player?.combatController;
            float reduction=combat!=null?combat.baseGcdDuration-combat.currentGcdDuration:0;
            float minimum=combat!=null?combat.minGcdDuration:.6f;
            return AegisRouteData.GcdDuration(skill.gcd,reduction,minimum,DamageState.Route.GcdScale(RouteId,skill.comboChain>0))/skill.gcd;
        }
        public string RouteName=>Array.Find(_actor?.Definition?.routes??Array.Empty<CharacterRouteDefinition>(),r=>r.id==RouteId)?.name??"기원 선택 필요";
        public bool IsActive=>_actor?.Definition?.id==AegisPaladinData.JobId;
        public float PhysicalDefense=>IsActive?AegisRouteData.DefenseRating(_actor.Definition.physicalDefense,RouteId):0;
        public float MagicDefense=>IsActive?AegisRouteData.DefenseRating(_actor.Definition.magicDefense,RouteId):0;
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
        public void ClearTransient(){DamageState.Clear();_player?.CancelSkillRush();_debtTextAmount=_debtTextTimer=0;if(_guardLine!=null)_guardLine.enabled=false;}
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
            float maximum=AegisRouteData.MaximumHp(_actor.Definition.baseHp,RouteId);
            _player.currentHp=AegisRouteData.PreserveHealthRatio(_player.currentHp,_player.maxHp,maximum);_player.maxHp=maximum;
            _player.moveSpeed=AegisRouteData.MoveSpeed(_actor.Definition,_player.ghostStepStacks);
        }
        private bool Identity(CharacterSkillDefinition skill)
        {
            if(!IsActive||RaidCombatAuthority.Replica||StageManager.CombatSuspended||_player==null||_player.IsDead||IdentityBlocked||skill.id!=AegisRouteData.Identity(RouteId))return false;
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
            if(!IsActive||RaidCombatAuthority.Replica||_player==null||_player.IsDead||!CombatRange.LivingEnemy(hit.Attacker))return;
            double reflected=DamageState.Route.Reflect(RouteId,hit.Kind,absorbed,_player.maxHp);
            if(reflected>0)CombatSecondaryDamage.Apply(transform,hit.Attacker,reflected,AegisRouteData.Mirror+".shield-reflect/"+hit.SkillId,CombatDamageKind.Reflection);
        }
        public bool TryGuard(CombatHitContext hit,float confirmedIncoming)
        {
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
        {if(!RaidCombatAuthority.Replica&&IsActive&&CombatRange.LivingPartyMember(transform)&&DamageState.ShieldAbsorbed(amount))_actor.GainResource(AegisPaladinData.Holy,AegisPaladinData.AbsorbHoly);}
        private void Update()
        {
            if(_gear!=null)_gear.SetActive(IsActive);
            if(!IsActive){if(_guardLine!=null)_guardLine.enabled=false;return;}if(_gear==null)BuildGear();
            if(_player!=null&&_player.normalBodyColor!=PlatinumColor)_player.ApplyCharacterAppearance(PlatinumColor,HolyColor);
            UpdateRouteVisuals();
            if(RaidCombatAuthority.Replica||StageManager.CombatSuspended||_player==null||_player.IsDead)return;
            float due=(float)DamageState.Advance(Time.deltaTime);
            if(due>0){_player.ApplyDeferredDamage(due);_debtTextAmount+=due;}
            _debtTextTimer+=Time.deltaTime;
            if(_debtTextTimer>=1){if(_debtTextAmount>.01f)FloatingTextPopup.Spawn(transform.position+Vector3.up*2,"지연 피해 -"+_debtTextAmount.ToString("0.#"),new Color(1,.65f,.2f),false);_debtTextTimer=_debtTextAmount=0;}
        }
        private bool Execute(CharacterSlot slot,CharacterSkillDefinition skill,Transform target)
        {
            if(!IsActive||RaidCombatAuthority.Replica||_player==null||_player.IsDead||StageManager.CombatSuspended)return false;
            if(slot==CharacterSlot.Job1){DamageState.Activate();FloatingTextPopup.Spawn(transform.position+Vector3.up*2,"불굴의 신념 · 피해 분산",HolyColor,false);return true;}
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
                if(!counter||!counterTarget.TryCounter(transform,skill.range,AegisPaladinData.CounterStagger+(RouteId==AegisRouteData.Mirror?AegisRouteData.CounterExtraStagger:0),opportunity))
                    CombatCounterTarget.Ensure(target).AddStagger(AegisPaladinData.CounterStagger);
                return true;
            }
            if(slot==CharacterSlot.OffGcd1)
            {
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
            Vector3 comboCenter=target.position;
            if(!Hit(target,skill.id,name,power,stagger,1))return false;
            if(Transformed)
            {
                var boss=StageManager.Instance?.boss;
                if(boss!=null&&boss.transform!=target&&InsideBurst(comboCenter,boss.transform,AegisRouteData.SunRadius))Hit(boss.transform,skill.id,name,power,stagger,1);
                foreach(var add in FindObjectsByType<BossAddMinion>(FindObjectsSortMode.None))if(add.transform!=target&&InsideBurst(comboCenter,add.transform,AegisRouteData.SunRadius))Hit(add.transform,skill.id,name,power,stagger,1);
                Pulse(comboCenter,AegisRouteData.SunRadius);
            }
            if(stage==3)
            {
                if(sword){_actor.GainResource(AegisPaladinData.Holy,AegisPaladinData.FinisherHoly);_actor.ReduceCooldowns(AegisPaladinData.ReducedSkills,AegisPaladinData.CooldownReduction);}
                else CombatStatusEffects.Shield(transform,transform,skill.id,_player.maxHp*AegisPaladinData.FinisherShieldRatio,AegisPaladinData.FinisherShieldDuration);
            }
            return true;
        }
        private bool InsideBurst(Vector3 center,Transform enemy,float radius=AegisPaladinData.BurstRadius)
        {return CombatRange.LivingEnemy(enemy)&&CombatRange.CircleTouches(center,enemy,radius);}
        private bool Hit(Transform enemy,string origin,string name,int power,float stagger,float threat)
            =>DealAttack(enemy,origin,name,power,stagger,threat)>0;
        public float DealAttack(Transform enemy,string origin,string name,float power,float stagger,float threat,CombatDamageKind kind=CombatDamageKind.Direct,bool scaleAttack=true)
        {
            if(RaidCombatAuthority.Replica||StageManager.CombatSuspended||!CombatRange.LivingPartyMember(transform)||!CombatRange.LivingEnemy(enemy))return 0;
            int damage=Mathf.RoundToInt(power*_player.combatController.GetPlayerDamageBuffMultiplier());
            float before,after;var boss=enemy.GetComponent<BossController>();
            if(boss!=null){before=boss.currentHp;boss.ReceiveRoleSkillAttack("이지스 팔라딘",name,0,damage,HolyColor,false,source:transform,origin:origin,kind:kind,scaleAttack:scaleAttack);after=boss.currentHp;}
            else {var add=enemy.GetComponent<BossAddMinion>();before=add.currentHp;add.ReceivePlayerAttack("이지스 팔라딘",name,damage,HolyColor,false,source:transform,origin:origin,kind:kind,scaleAttack:scaleAttack);after=add.currentHp;}
            float actual=Mathf.Max(0,before-after);if(actual<=0)return 0;
            if(RouteId==AegisRouteData.Sun&&kind==CombatDamageKind.Direct&&AegisExtensionData.TriggersSun(origin))CombatSecondaryDamage.Apply(transform,enemy,CombatDamageRules.SunDamage(actual),origin,CombatDamageKind.Extra);
            if(boss!=null&&threat>1)boss.AddCombatThreat(transform,actual*(threat-1));
            if(stagger>0)CombatCounterTarget.Ensure(enemy).AddStagger(stagger);
            if(kind==CombatDamageKind.Direct){_player.FaceAttackTarget(enemy);_player.PlaySkillLineVisual(enemy.position,HolyColor);
                if(_player.hasVampiricBlade)_player.TriggerVampiricBladeHeal();}return actual;
        }
        public void ShowPulse(Vector3 center,float radius)=>Pulse(center,radius);
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
            bool guard=RouteId==AegisRouteData.Mirror&&DamageState.Route.Guard>0;
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
        private void OnDestroy(){if(_metal!=null)Destroy(_metal);if(_platinum!=null)Destroy(_platinum);if(_guardMaterial!=null)Destroy(_guardMaterial);}
    }
}
