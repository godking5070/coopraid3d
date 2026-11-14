using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    [Serializable] public sealed class NineKeyTankCombatSnapshot
    {
        public NineKeyTankSnapshot route;
        public CharacterKitSnapshot effects;
        public AegisSunSnapshot cooperation;
        public int bossId,chargeTarget,chargeSupport,staggerTarget;
        public float staggerRemaining;
    }
    // Host-only effect adapter for the new tanks. Legacy CharacterKit is visual-only
    // for these definitions; none of its old passives or resource callbacks apply.
    [DefaultExecutionOrder(450)]
    public sealed class NineKeyTankKit : MonoBehaviour
    {
        private CharacterActor _actor;
        private PlayerController _player;
        private NineKeyTankState _route;
        private CharacterRuntimeState _runtime;
        private readonly CharacterEffectState _effects=new CharacterEffectState();
        private readonly AegisSunState _cooperation=new AegisSunState();
        private int _bossId,_chargeTarget,_chargeSupport,_staggerTarget;
        private float _staggerRemaining;
        private bool _release;
        private string _deferredDirectSkill;
        public bool OwnsDeferredDirectHit(string origin)
        {
            return Active&&!RaidCombatAuthority.Replica&&!string.IsNullOrEmpty(origin)&&_deferredDirectSkill==origin;
        }
        private bool _heldR,_heldSecond;
        public RosterJob Job=>CharacterRosterData.Find(_actor?.Definition?.id);
        public static bool Supports(CharacterDefinition definition)=>CharacterSlots.IsNineKey(definition)&&NineKeyTankData.Supports(CharacterRosterData.Find(definition?.id));
        public bool Active=>Supports(_actor?.Definition);
        public bool Blood=>Job?.index==1;
        public int Route=>!Active?0:Array.FindIndex(_actor.Definition.routes,r=>r.id==_actor.RouteId)+1;
        private bool Core(int route,int variant)=>_actor.RuntimeState.CoreTrait==NineKeyTankData.Core(Job,route,variant);
        private float Now=>RaidCombatAuthority.ServerTime;
        private Color Tint=>Blood?new Color(.9f,.2f,.18f):new Color(1,.5f,.15f);
        public NineKeyTankSnapshot State{get{Bind();return _route?.Snapshot;}}
        public static NineKeyTankKit Ensure(GameObject go)=>go.GetComponent<NineKeyTankKit>()??go.AddComponent<NineKeyTankKit>();
        private void Awake(){_actor=GetComponent<CharacterActor>();_player=GetComponent<PlayerController>();_actor.RegisterHandler(NineKeyTankData.Handler,Execute);}
        private void Bind()
        {
            if(!Active||_actor.RuntimeState==null)return;
            if(_runtime!=_actor.RuntimeState){_runtime=_actor.RuntimeState;_route=new NineKeyTankState(_runtime,Job);_effects.Clear();_cooperation.Clear();_bossId=_chargeTarget=_chargeSupport=_staggerTarget=0;_staggerRemaining=0;}
        }
        private static int Id(Transform target)=>CharacterKit.Id(target);
        private static Transform Resolve(int id)=>CharacterKit.Resolve(id);
        private float MaxHp=>_player!=null?_player.maxHp:GetComponent<PartyBotAI>()?.maxHp??1;
        private IEnumerable<Transform> Party(float range=30)=>PartyLife.OrderedMembers().Where(t=>CombatRange.LivingPartyMember(t)&&CombatRange.InRange(transform,t,range));
        public float EffectiveRange(CharacterSkillDefinition skill)=>skill?.range+(skill?.id==NineKeyTankData.Id(Job,"f")&&_actor.HasAuxiliary(AegisPaladinData.FarCall)?5:0)??0;
        public float CooldownScale(CharacterSkillDefinition skill)=>skill?.id==NineKeyTankData.Id(Job,"q")&&_actor.HasAuxiliary(AegisPaladinData.Steadfast)?1.25f:1;
        public void Clear(bool refund=false)
        {
            _deferredDirectSkill=null;
            Bind();if(!RaidCombatAuthority.Replica)_route?.Clear(refund);
            _effects.Clear();_cooperation.Clear();_bossId=_chargeTarget=_chargeSupport=_staggerTarget=0;_staggerRemaining=0;
        }
        public void Interrupt(){Bind();if(RaidCombatAuthority.Replica)return;_route?.CancelCharge();_route?.StopWhirl();foreach(var effect in _effects.Data.effects.Where(e=>e.kind=="barrage").ToArray())_effects.Remove(effect.id);}
        public void SynchronizeDevelopmentResources(){if(!Active||RaidCombatAuthority.Replica)return;Interrupt();_route.SynchronizeExternalResources();}
        public string Unavailable(CharacterSlot slot)
        {
            if(!Active)return "";
            Bind();if(_route==null)return "";
            bool response=slot==CharacterSlot.Job1||slot==CharacterSlot.Counter||slot==CharacterSlot.Movement||slot==CharacterSlot.OffGcd2;
            if(!response&&_effects.Data.effects.Any(e=>e.kind=="barrage"&&e.end>Now))return "전탄 발사 중 · 방어/카운터/이동/인계로 취소 가능";
            float resource=_runtime.Resource(Job.Resource);var state=_route.Snapshot;
            if(slot==CharacterSlot.OffGcd1&&!Blood&&_actor.Selected(slot)?.id==NineKeyTankData.Id(Job,"a0")&&resource>=4)return "탄창 가득 참";
            if(slot!=CharacterSlot.Identity)return "";
            if(Blood)
            {
                if(Route==1&&resource<30)return "혈기30 필요";
                if(Route==2&&state.reserved==0&&!state.charging&&resource<60)return "혈기60 예약 필요";
                if(Route==3&&!state.whirling&&resource<3)return "첫 공격 틱 혈기3 필요";
            }
            else
            {
                int rounds=Core(1,0)?1:Core(1,1)?4:2;
                if(Route==1&&resource<rounds)return "용염 탄"+rounds+"발 필요";
                if(Route==2&&state.danceTime<=0&&resource<2)return "연무 시작 탄2발 필요";
                if(Route==2&&state.danceTime>0&&state.danceReady<=0)return "1·2 직접 적중 후 격발";
                if(Route==3&&_runtime.Resource(NineKeyTankData.Heat(Job))<40)return "노심 온도40 필요";
            }
            return "";
        }
        public string Capture()
        {
            Bind();return JsonUtility.ToJson(new NineKeyTankCombatSnapshot{route=_route.Snapshot,effects=_effects.Data.Copy(),cooperation=_cooperation.Capture(),bossId=_bossId,chargeTarget=_chargeTarget,chargeSupport=_chargeSupport,staggerTarget=_staggerTarget,staggerRemaining=_staggerRemaining});
        }
        public bool Restore(string json)
        {
            Bind();if(_route==null||string.IsNullOrEmpty(json))return false;
            NineKeyTankCombatSnapshot data;
            try{data=JsonUtility.FromJson<NineKeyTankCombatSnapshot>(json);}catch(ArgumentException){return false;}
            var candidate=new NineKeyTankState(_runtime,Job);var effects=new CharacterEffectState();var mark=new AegisSunState();
            if(data==null||!candidate.Restore(data.route)||!effects.Restore(data.effects)||!ValidEffectSnapshot(data.effects)||!mark.Restore(data.cooperation)||
                !CharacterDefinitionValidation.Finite(data.staggerRemaining)||data.staggerRemaining<0||data.staggerRemaining>6)return false;
            _route=candidate;_effects.Restore(data.effects);_cooperation.Restore(data.cooperation);
            _bossId=data.bossId;_chargeTarget=data.chargeTarget;_chargeSupport=data.chargeSupport;_staggerTarget=data.staggerTarget;_staggerRemaining=data.staggerRemaining;return true;
        }
        private bool ValidEffectSnapshot(CharacterKitSnapshot snapshot)
        {
            if(snapshot==null||snapshot.effects==null)return false;
            foreach(var effect in snapshot.effects)
            {
                if(effect==null||effect.target==0||effect.value<0||effect.aux<0||effect.end<0||effect.next<0)return false;
                if(effect.kind=="barrage")
                {
                    if(Blood||Route!=1||!Core(1,1)||effect.id!="barrage"||effect.skill!=NineKeyTankData.Id(Job,"r1")||
                        effect.ticks!=0||effect.interval!=.8f||effect.next!=effect.end||effect.radius!=0&&effect.radius!=1)return false;
                    continue;
                }
                if(effect.id!=effect.skill+"@"+effect.target||effect.aux!=0||effect.radius!=0||effect.interval!=1)return false;
                if(effect.kind=="reduction")
                {
                    if(effect.value>1||!(effect.skill=="guard"||Blood&&Route==3&&effect.skill=="storm.wave"))return false;
                }
                else if(effect.kind=="heal")
                {if(!Blood||effect.skill!="encourage"&&effect.skill!="ultimate.hot")return false;}
                else if(effect.kind=="damage")
                {if(!Blood||effect.skill!="combo.bleed")return false;}
                else return false;
            }
            return true;
        }
        private void Effect(string key,string kind,Transform target,float value,float duration,float interval=1)
        {
            if(target==null)return;
            _effects.Put(new CharacterTimedEffect{id=key+"@"+Id(target),kind=kind,skill=key,target=Id(target),value=value,end=Now+duration,next=Now+interval,interval=interval});
        }
        private float Heal(Transform target,float amount)
        {
            if(!CombatRange.LivingPartyMember(target))return 0;
            var player=target.GetComponent<PlayerController>();return player!=null?player.ReceiveHeal(amount,transform):target.GetComponent<PartyBotAI>().ReceiveHeal(amount,transform);
        }
        private void Shield(Transform target,string key,float amount,float duration=6)
            =>CombatStatusEffects.Shield(target,transform,Job.Id+".nine."+key,amount,duration,true);
        private bool ValidSupport(Transform target)=>CombatRange.LivingPartyMember(target)&&CombatRange.InRange(transform,target,20);
        private float Attack(Transform target,CharacterSkillDefinition skill,float power,float stagger=0,CombatDamageKind kind=CombatDamageKind.Direct)
        {
            if(StageManager.PlayerSkillsSuspended||!CombatRange.LivingEnemy(target)||!CombatRange.InRange(transform,target,EffectiveRange(skill)))return 0;
            int damage=Mathf.RoundToInt(power*(_player?.combatController?.GetPlayerDamageBuffMultiplier()??1));
            float before,after;var boss=target.GetComponent<BossController>();
            // Spectator zero-HP-damage mode still cannot turn an immune boss
            // into a successful hit for resource, stagger or cooperation rewards.
            if(boss!=null&&boss.HpDamageBlocked)return 0;
            if(boss!=null){before=boss.currentHp;boss.ReceiveRoleSkillAttack(Job.name,skill.name,0,damage,Tint,false,source:transform,origin:skill.id,kind:kind);after=boss.currentHp;}
            else{var add=target.GetComponent<BossAddMinion>();if(add==null)return 0;before=add.currentHp;add.ReceivePlayerAttack(Job.name,skill.name,damage,Tint,false,source:transform,origin:skill.id,kind:kind);after=add.currentHp;}
            float actual=Mathf.Max(0,before-after);
            bool landed=actual>0||AiSpectatorDamage.ZeroHpDamageMode(transform);
            if(landed&&stagger>0)CombatCounterTarget.Ensure(target).AddStagger(stagger);
            if(landed&&kind==CombatDamageKind.Direct)
            {
                if(boss!=null)_bossId=Id(target);
                _player?.FaceAttackTarget(target);_player?.PlaySkillLineVisual(target.position,Tint);
            }
            return landed?Mathf.Max(.001f,actual):0;
        }
        private void Mark(Transform target,float scale=1)
        {
            if(!CombatRange.LivingEnemy(target))return;var party=PartyLife.OrderedMembers();
            _cooperation.SetMark(Id(target),CombatMetricsManager.SourceId(transform),party.Where(CombatRange.LivingPartyMember).Select(CombatMetricsManager.SourceId),party.Count,scale);
        }
        public bool CanThreat(Transform target)
        {
            var skill=_actor.Selected(CharacterSlot.OffGcd2);
            if(target==null||!CombatRange.InRange(transform,target,EffectiveRange(skill)))return false;
            if(CombatRange.LivingEnemy(target))return true;
            var boss=Resolve(_bossId)?.GetComponent<BossController>();
            return target!=transform&&CombatRange.LivingPartyMember(target)&&target.GetComponent<PartyLife>()?.IsTank==true&&boss!=null&&CombatRange.LivingEnemy(boss.transform)&&boss.CombatThreatOf(transform)>0;
        }
        private bool Threat(Transform target)
        {
            if(!CanThreat(target))return false;
            if(CombatRange.LivingEnemy(target))
            {
                var boss=target.GetComponent<BossController>();
                if(boss!=null){boss.AddCombatThreat(transform,0,true);boss.SetAggroTarget(transform);_bossId=Id(target);}
                else target.GetComponent<BossAddMinion>().TauntBy(transform,Job.name);
                return true;
            }
            return Resolve(_bossId).GetComponent<BossController>().TransferCombatThreat(transform,target,.8f);
        }
        private bool Execute(CharacterSkillDefinition skill,Transform target)
        {
            Bind();if(!Active||_route==null||RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||!CombatRange.LivingPartyMember(transform))return false;
            string key=CharacterRosterData.Key(skill.id);var support=_actor.ActionSupportTarget;
            if(key=="q"){Effect("guard","reduction",transform,.3f,_actor.HasAuxiliary(AegisPaladinData.Steadfast)?8:6);return true;}
            if(key=="f")return Threat(target);
            if(key=="c")
            {
                if(Attack(target,skill,160)<=0)return false;
                var counter=CombatCounterTarget.Ensure(target);if(!counter.TryCounter(transform,skill.range,80))counter.AddStagger(80);return true;
            }
            if(skill.comboChain>0)return Combo(skill,target,support);
            if(key.StartsWith("a",StringComparison.Ordinal))return AuxiliarySkill(key,skill,target);
            if(key=="v")
            {
                Attack(target,skill,1200,Blood?0:250);
                if(Blood)foreach(var ally in Party())Effect("ultimate.hot","heal",ally,MaxHp*.025f,6);
                return true;
            }
            if(key.StartsWith("r",StringComparison.Ordinal))return Identity(skill,target,support);
            return false;
        }
        private bool Combo(CharacterSkillDefinition skill,Transform target,Transform support)
        {
            int chain=skill.comboChain,step=chain==1?_actor.Snapshot.combo1:_actor.Snapshot.combo2;
            int variant=chain==1?-1:skill.id.EndsWith("g21",StringComparison.Ordinal)?1:skill.id.EndsWith("g22",StringComparison.Ordinal)?2:0;
            int chargeStage=0;
            if(_release&&Blood&&Route==2&&step==3&&chain==2&&!_route.ReleaseCharge(out chargeStage,out _))return false;
            bool heated=!Blood&&Route==3&&chain==2&&step==3&&_route.FireFurnaceSecond();
            float power=(step==3?240:step==2?150:100)*(variant==1||variant==2?.8f:1);
            if(chargeStage>0)power*=1+chargeStage*.2f;
            if(!Blood&&Route==3&&chain==2&&step==3)power*=heated?1.4f:.6f;
            bool hit=Attack(target,skill,power,variant==(Blood?1:2)?60:20)>0;
            if(!Blood&&chain==2&&variant==0)
                foreach(var enemy in CharacterKit.Enemies())
                    if(enemy!=target&&CombatRange.InRange(transform,enemy,skill.range))
                        hit=Attack(enemy,skill,power,20)>0||hit;
            if(!hit)return true; // An emitted miss still consumes its GCD and furnace round.
            if(!Blood&&Route==2)_route.PrepareDance(true);
            if(step==3)
            {
                if(_actor.ClaimTankFinisher(skill))_route.FinishCombo(chain);
                if(Blood&&chain==2&&variant==0)Heal(transform,MaxHp*.05f);
                if(Blood&&chain==2&&variant==2)Effect("combo.bleed","damage",target,30,6);
                if(!Blood&&Route==3&&chain==1&&_route.InComfortBand)
                {
                    float shield=MaxHp*(Core(3,1)?.015f:.03f);Shield(transform,"warmth",shield);
                    if(support!=transform&&ValidSupport(support))Shield(support,"warmth",shield);
                }
            }
            return true;
        }
        private bool AuxiliarySkill(string key,CharacterSkillDefinition skill,Transform target)
        {
            if(key=="a2")
            {
                if(!ValidSupport(target))return false;
                if(Blood)Effect("encourage","heal",target,MaxHp*.02f,5);
                else{Shield(transform,"scales",MaxHp*.08f);if(target!=transform)Shield(target,"scales",MaxHp*.08f);}
                return true;
            }
            if(!Blood&&key=="a0")return _route.EmergencyReload();
            if(Attack(target,skill,key=="a0"?180:140,key=="a1"?100:20)<=0)return true;
            if(Blood&&key=="a0")Heal(transform,MaxHp*.07f);return true;
        }
        private bool Identity(CharacterSkillDefinition skill,Transform target,Transform support)
        {
            if(Blood&&Route==1)
            {
                var patient=CombatRange.LivingPartyMember(target)?target:transform;
                if(target!=null&&target!=transform&&!CombatRange.LivingEnemy(target)&&!ValidSupport(target)||!ValidSupport(patient)||!_runtime.Spend(Job.Resource,30))return false;
                float amount=MaxHp*(patient==transform?.12f:.1f);
                if(Core(1,1)&&patient==transform&&support!=transform&&ValidSupport(support)){Heal(support,amount*.3f);amount*=.7f;}
                float actual=Heal(patient,amount);
                if(Core(1,0)&&patient==transform)Shield(transform,"clotted",Mathf.Min(MaxHp*.06f,Mathf.Max(0,amount-actual)*.5f));
                return true;
            }
            if(Blood&&Route==2)
            {
                if(!_release){_chargeTarget=Id(target);_chargeSupport=Id(support);return _route.StartCharge(0);}
                var prepared=_route.Snapshot;bool full=prepared.chargeTime+prepared.chargeDiscount>=prepared.chargeLimit;
                if(!_route.ReleaseCharge(out int stage,out _))return false;
                float power=(Core(2,0)?300:600)*(1+stage*.25f);
                if(Attack(target,skill,power,full?120:60)>0&&full){_staggerTarget=Id(target);_staggerRemaining=6;}
                return true;
            }
            if(Blood&&Route==3){if(_route.Snapshot.whirling){_route.StopWhirl();return true;}return _route.StartWhirl();}
            if(!Blood&&Route==1)
            {
                if(!CombatRange.LivingEnemy(target)||!CombatRange.InRange(transform,target,skill.range)||!_route.Barrage(out var shots))return false;
                bool marked=false;
                for(int i=0;i<shots.Length;i++)
                {
                    var shot=shots[i];
                    // Prototype pair budgets: pierce/pierce direct damage,
                    // crush/pierce empowered damage+cooperation, pierce/crush
                    // boosted second stagger, crush/crush stagger concentration.
                    float power=shot.First==0?200:shot.First==1&&shot.Second==1?600:shot.Cooperation?550:shot.First==1?450:400;
                    float stagger=shot.First==0?(shot.Second==2?60:20):shot.First==2&&shot.Second==2?240:shot.Stagger?180:40;
                    if(Core(1,0)&&shot.First!=0){power-=200;stagger-=shot.First==2?60:20;}
                    if(Core(1,1))power*=1.15f;
                    if(i==0)
                    {if(Attack(target,skill,power,stagger)>0&&shot.Cooperation){Mark(target);marked=true;}}
                    else _effects.Put(new CharacterTimedEffect{id="barrage",kind="barrage",skill=skill.id,target=Id(target),value=power,aux=stagger,radius=shot.Cooperation&&!marked?1:0,end=Now+.8f,next=Now+.8f,interval=.8f});
                }
                return true;
            }
            if(!Blood&&Route==2)
            {
                if(_route.Snapshot.danceTime<=0)return _route.StartDance();
                if(!CombatRange.LivingEnemy(target)||!CombatRange.InRange(transform,target,skill.range)||!_route.FireDance(out bool last))return false;
                if(Attack(target,skill,last?(Core(2,0)?260:Core(2,1)?380:320):180)>0&&last)Mark(target);return true;
            }
            if(!Blood&&Route==3)
            {
                if(!_route.Vent(out bool hot))return false;
                float scale=hot?(Core(3,1)?1.7f:Core(3,0)?1.2f:1.4f):1;
                Attack(target,skill,400*scale,80);
                Shield(transform,"vent",MaxHp*.06f*scale);if(support!=transform&&ValidSupport(support))Shield(support,"vent",MaxHp*.06f*scale);return true;
            }
            return false;
        }
        public bool BeginSecondCharge(Transform target,Transform support)
        {
            Bind();if(!Active||!Blood||Route!=2||RaidCombatAuthority.Replica||!CombatRange.LivingEnemy(target))return false;
            if(!_route.StartCharge(2))return false;_chargeTarget=Id(target);_chargeSupport=Id(support);return true;
        }
        public bool Release(CharacterSlot slot,Transform target,Transform support,out string reason)
        {
            Bind();reason="차징 중이 아닙니다";
            if(RaidCombatAuthority.Replica||_route==null||!_route.Snapshot.charging)return false;
            if((_route.Snapshot.chargeChain==2?CharacterSlot.Gcd2:CharacterSlot.Identity)!=slot)return false;
            var skill=_actor.Selected(slot);
            if(!CombatRange.LivingEnemy(target)||!CombatRange.InRange(transform,target,EffectiveRange(skill)))
            {Interrupt();reason="방출 대상이 유효하지 않아 차징을 취소했습니다";return false;}
            _release=true;try{return _actor.Execute(slot,target,out reason,support);}finally{_release=false;}
        }
        public bool Releasing=>_release;
        public void RequestRelease(CharacterSlot slot)
        {
            var targeting=TargetingManager.Instance;var target=targeting?.AttackTarget;
            var support=targeting?.HasSupportSelection==true?targeting.SupportTarget:transform;
            if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(10,_actor.Definition.id,(int)slot,RaidCombatAuthority.Id(target),"tank.release",RaidCombatAuthority.Id(support).ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if(!Release(slot,target,support,out string reason))FloatingTextPopup.Spawn(transform.position+Vector3.up*2,reason,Tint,false);
        }
        public void UpdateInput()
        {
            if(!Active||!Blood||Route!=2||!RaidCombatAuthority.Local(_actor)){_heldR=_heldSecond=false;return;}
            bool heldR=LocalInputBindings.Held(CharacterSlots.Binding(_actor.Definition,CharacterSlot.Identity));
            bool heldSecond=LocalInputBindings.Held(CharacterSlots.Binding(_actor.Definition,CharacterSlot.Gcd2));
            bool blocked=RaidCombatAuthority.InputBlocked(_actor)||LocalGameplayInput.Blocked;
            foreach(var slot in new[]{CharacterSlot.Job1,CharacterSlot.Counter,CharacterSlot.Movement,CharacterSlot.OffGcd2})
                blocked|=LocalInputBindings.Pressed(CharacterSlots.Binding(_actor.Definition,slot));
            if(blocked)
            {
                if(_heldR||_heldSecond)
                {if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(13,_actor.Definition.id,0);else Interrupt();}
                _heldR=_heldSecond=false;return;
            }
            if(_heldR&&!heldR)RequestRelease(CharacterSlot.Identity);
            if(_heldSecond&&!heldSecond&&(_route?.Snapshot.chargeChain==2||_actor.Snapshot.combo2==3))RequestRelease(CharacterSlot.Gcd2);
            _heldR=heldR;_heldSecond=heldSecond;
        }
        public bool NeedsSecondCharge(CharacterSlot slot)=>Active&&Blood&&Route==2&&slot==CharacterSlot.Gcd2&&_actor.Snapshot.combo2==3&&!_release;
        public void BeforeInput(CharacterSlot slot)
        {
            Bind();if(_route==null||RaidCombatAuthority.Replica||_release)return;
            if(slot==CharacterSlot.Job1||slot==CharacterSlot.Counter||slot==CharacterSlot.Movement||slot==CharacterSlot.OffGcd2)
            {Interrupt();return;}
            if(slot==CharacterSlot.Gcd1||slot==CharacterSlot.Gcd2||slot==CharacterSlot.OffGcd1)
            {
                _route.StopWhirl();
                if(_route.Snapshot.charging)_route.CancelCharge();
            }
        }
        private void ResolveBarrage(CharacterTimedEffect effect)
        {
            // Only a host-accepted manual R creates this one pending pair. Remove
            // it before callbacks, and expose provenance only during this hit.
            if(RaidCombatAuthority.Replica||Blood||Route!=1||effect==null||effect.kind!="barrage"||
                effect.skill!=NineKeyTankData.Id(Job,"r1")||!_effects.Data.effects.Contains(effect)||
                CharacterEffectState.TakeTicks(effect,Now)==0)return;
            _effects.Remove(effect.id);
            var skill=_actor.Definition.Skill(effect.skill);var target=Resolve(effect.target);
            if(skill==null)return;
            _deferredDirectSkill=skill.id;
            try { if(Attack(target,skill,effect.value,effect.aux)>0&&effect.radius>0)Mark(target); }
            finally { _deferredDirectSkill=null; }
        }
        private void Update()
        {
            Bind();if(!Active||_route==null||RaidCombatAuthority.Replica)return;
            if(!CombatRange.LivingPartyMember(transform)){Clear(true);return;}
            if(StageManager.PlayerSkillsSuspended)return;
            if(_player!=null&&(_player.IsStunned||_player.IsBeingKnockedBack||_player.IsBeingRescued))Interrupt();
            var before=_route.Snapshot;_route.Advance(Time.deltaTime,out int ticks,out int waves);
            _cooperation.Advance(Time.deltaTime);_staggerRemaining=Mathf.Max(0,_staggerRemaining-Time.deltaTime);
            if(ticks>0)
            {
                var skill=_actor.Selected(CharacterSlot.Identity);
                foreach(var enemy in CharacterKit.Enemies())if(CombatRange.InRange(transform,enemy,4))Attack(enemy,skill,ticks*30,0,CombatDamageKind.Periodic);
            }
            if(waves>0)
            {
                float reduction=Core(3,0)?.06f:Core(3,1)?(before.whirlPulses+waves>=2?.14f:.04f):.1f;
                foreach(var ally in Party())Effect("storm.wave","reduction",ally,reduction,2);
            }
            foreach(var effect in _effects.Data.effects.ToArray())
            {
                if(effect.kind=="barrage")ResolveBarrage(effect);
                if(effect.kind=="heal"||effect.kind=="damage")
                {
                    int due=CharacterEffectState.TakeTicks(effect,Now);var target=Resolve(effect.target);
                    if(due>0&&effect.kind=="heal")Heal(target,effect.value*due);
                    if(due>0&&effect.kind=="damage")Attack(target,new CharacterSkillDefinition{id=Job.Id+".nine.periodic",name="혈흔",range=30},effect.value*due,0,CombatDamageKind.Periodic);
                }
                if(effect.end<=Now)_effects.Remove(effect.id);
            }
        }
        public static float Incoming(Transform recipient,float damage,CombatHitContext hit)
        {
            if(hit.Defense==CombatDefenseType.Bypass||hit.Kind==CombatDamageKind.System||hit.Kind==CombatDamageKind.Transfer||hit.Kind==CombatDamageKind.Deferred)return damage;
            var guards=new Dictionary<string,float>();
            foreach(var owner in PartyLife.OrderedMembers())
            {
                var kit=owner.GetComponent<NineKeyTankKit>();if(kit?.Active!=true||!CombatRange.LivingPartyMember(owner))continue;
                foreach(var effect in kit._effects.Data.effects)
                    if(effect.kind=="reduction"&&effect.target==Id(recipient)&&effect.end>kit.Now)
                    {guards.TryGetValue(effect.skill,out float prior);guards[effect.skill]=Mathf.Max(prior,effect.value);}
            }
            foreach(float reduction in guards.Values)damage*=1-Mathf.Clamp01(reduction);return damage;
        }
        public static void AfterDirect(Transform source,Transform target,string origin,CombatDamageKind kind)
        {
            if(RaidCombatAuthority.Replica||StageManager.PlayerSkillsSuspended||kind!=CombatDamageKind.Direct||!CombatRange.LivingPartyMember(source)||!CombatRange.LivingEnemy(target))return;
            var actor=source.GetComponent<CharacterActor>();
            if(actor?.OwnsDirectSkillHit(origin)!=true&&!(actor?.Definition?.legacyRole>0&&origin=="input.legacy.skill"))return;
            foreach(var member in PartyLife.OrderedMembers())
            {
                var kit=member.GetComponent<NineKeyTankKit>();if(kit?.Active!=true||member==source||!CombatRange.LivingPartyMember(member))continue;
                if(kit._cooperation.MarkTarget==Id(target))
                {
                    float power=kit._cooperation.Consume(CombatMetricsManager.SourceId(source),true);
                    if(power>0)CombatSecondaryDamage.Apply(member,target,power,"tank.nine.cooperation",CombatDamageKind.Extra);
                }
                if(kit._staggerRemaining>0&&kit._staggerTarget==Id(target))
                {kit._staggerRemaining=0;CombatCounterTarget.Ensure(target).AddStagger(100);}
            }
        }
        public void Recalculate()
        {
            if(!Active||RaidCombatAuthority.Replica)return;
            if(_player!=null)
            {
                float hp=_actor.Definition.baseHp*(1+CharacterKit.Bonus(transform,"maxHp"));float ratio=_player.HpRatio;
                _player.maxHp=hp;_player.currentHp=hp*ratio;
                _player.moveSpeed=_actor.Definition.moveSpeed*_actor.Definition.moveSpeedMultiplier*Mathf.Pow(1.3f,_player.ghostStepStacks)*(1+CharacterKit.Bonus(transform,"speed"));
            }
        }
        public string StatusText()
        {
            Bind();if(_route==null)return "";var data=_route.Snapshot;var boss=Resolve(_bossId);
            string text="F 인계 보스: "+(CombatRange.LivingEnemy(boss)?boss.name:"없음");
            if(Blood&&Route==2)text+=" · 예약혈기 "+data.reserved+" · 차징 "+data.chargeTime.ToString("0.0")+" / "+data.chargeLimit+"초";
            if(Blood&&Route==3)text+=" · 회전 "+(data.whirling?data.whirlTime.ToString("0.0")+" / 6초":"대기");
            if(!Blood&&Route==1)text+=" · 발사순서 "+string.Join(" → ",data.ammo.Select(a=>a==1?"관통":"파쇄"))+" · 연결 "+data.linkTime.ToString("0.0")+"초";
            if(!Blood&&Route==2)text+=" · 연무 "+data.danceTime.ToString("0.0")+"초 / 격발 "+data.danceShots+"회 · 준비 "+data.danceReady.ToString("0.0")+"초";
            if(!Blood&&Route==3)text+=" · 온도 "+_actor.Resource(NineKeyTankData.Heat(Job))+" · "+(_route.InComfortBand?"잔열 보호 구간":"가열/냉각 선택");
            return text;
        }
    }
}
