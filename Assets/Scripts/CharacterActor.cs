using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    // One actor-owned state; never attached to a shared ScriptableObject or a connection ID.
    [DefaultExecutionOrder(500)]
    public sealed partial class CharacterActor : MonoBehaviour
    {
        private CharacterRuntimeState _state;
        internal CharacterRuntimeState RuntimeState=>_state;
        public bool ClaimDamageDirect(CharacterSkillDefinition skill)=>!RaidCombatAuthority.Replica&&NineKeyDamageKit.Supports(Definition)&&_executingSkill==skill&&_actionExecution?.TryResolveDirectGrowth("damage.route",true)==true;
        public bool OwnsHealerInput=>!RaidCombatAuthority.Replica&&NineKeyHealerKit.Supports(Definition)&&_actionExecution?.Active==true&&_executingSkill?.handlerId==NineKeyHealerData.Handler;
        public bool ClaimHealerDirect(CharacterSkillDefinition skill)=>!RaidCombatAuthority.Replica&&NineKeyHealerKit.Supports(Definition)&&_executingSkill==skill&&_actionExecution?.TryResolveDirectGrowth("healer.route",true)==true;
        public bool ClaimHealerFinisher(CharacterSkillDefinition skill)=>!RaidCombatAuthority.Replica&&NineKeyHealerKit.Supports(Definition)&&_executingSkill==skill&&_actionExecution?.TryResolveFinisherGauge(true)==true;
        public bool ClaimTankFinisher(CharacterSkillDefinition skill)=>!RaidCombatAuthority.Replica&&NineKeyTankKit.Supports(Definition)&&_executingSkill==skill&&_actionExecution?.TryResolveFinisherGauge(true)==true;
        private CombatActionExecution _actionExecution;
        private CharacterSkillDefinition _executingSkill;
        public Transform ActionSupportTarget {get;private set;}
        public Vector3? ActionGroundPoint {get;private set;}
        public DamagePrismCommand ActionPrism {get;private set;}
        public bool ActionRecall {get;private set;}
        private readonly AegisRecallInput _recallInput=new AegisRecallInput();
        public bool OwnsDirectSkillHit(string origin)=>GetComponent<NineKeyTankKit>()?.OwnsDeferredDirectHit(origin)==true||GetComponent<NineKeyDamageKit>()?.OwnsDeferredDirectHit(origin)==true||_actionExecution?.Active==true&&_executingSkill!=null&&_executingSkill.id==origin&&
            (_executingSkill.target==SkillTargetRule.LivingEnemy||Definition?.id==AegisPaladinData.JobId&&CharacterSlots.IsNineKey(Definition)&&
            (_executingSkill.id==AegisExtensionData.Morning||_executingSkill.id==AegisExtensionData.Excalibur)||
            NineKeyTankKit.Supports(Definition)&&(_executingSkill.id==NineKeyTankData.Id(CharacterRosterData.Find(Definition.id),"v")||Definition.routes.Any(r=>r.identitySkillId==_executingSkill.id))||
            NineKeyHealerKit.Supports(Definition)&&_executingSkill.handlerId==NineKeyHealerData.Handler&&
            (_executingSkill.id==NineKeyHealerData.Id(CharacterRosterData.Find(Definition.id),"v")||Definition.routes.Any(r=>r.identitySkillId==_executingSkill.id))||
            NineKeyDamageKit.Supports(Definition)&&_executingSkill.handlerId==NineKeyDamageData.Handler&&Definition.routes.Any(r=>r.identitySkillId==_executingSkill.id));
        private PlayerController _player;
        private PartyBotAI _bot;
        private bool _identityHeld;
        private int _role; private int _preservedDashFrame = -1; private Transform _singleCastTarget;
        private Vector3 _previous;
        private readonly Dictionary<string, Func<CharacterSkillDefinition, Transform, bool>> _handlers = new Dictionary<string, Func<CharacterSkillDefinition, Transform, bool>>();
        public float PrimaryRange => EffectiveRange(_state?.Selected(CharacterSlot.Gcd1));
        public float EffectiveRange(CharacterSkillDefinition skill)=>GetComponent<NineKeyDamageKit>()?.Active==true?GetComponent<NineKeyDamageKit>().EffectiveRange(skill):GetComponent<NineKeyHealerKit>()?.Active==true?GetComponent<NineKeyHealerKit>().EffectiveRange(skill):GetComponent<NineKeyTankKit>()?.Active==true?GetComponent<NineKeyTankKit>().EffectiveRange(skill):GetComponent<CharacterKit>()?.Active==true?GetComponent<CharacterKit>().EffectiveRange(skill):GetComponent<AegisPaladin>()?.EffectiveRange(skill)??skill?.range??0;
        public CharacterSkillDefinition Selected(CharacterSlot slot)=>_state?.Selected(slot);
        public string RouteId=>_state?.RouteId??"";
        public CharacterUnlockDefinition[] PendingChoices=>_state?.PendingChoices()??Array.Empty<CharacterUnlockDefinition>();
        public string ChoiceKey(string rule)=>Array.Find(Definition.unlocks,r=>r.id==rule) is CharacterUnlockDefinition value?_state.RewardKey(value):"";
        public string SlotUnlockStatus(CharacterSlot slot)
        {
            var rule=Array.Find(Definition.unlocks,r=>r.slot==slot);if(rule==null)return "잠김";
            return Array.Exists(PendingChoices,r=>r.id==rule.id)?"선택 대기":"Stage "+rule.afterStage+" 클리어 후 선택";
        }
        public bool DepartureReady=>Definition!=null&&(Definition.routes.Length==0||!string.IsNullOrEmpty(RouteId));
        public CharacterDefinition Definition => _state?.Definition;
        public CharacterStateSnapshot Snapshot { get { SyncLegacy(); return _state?.Snapshot; } }
        public static CharacterActor Ensure(GameObject actor)
        {
            var result=actor.GetComponent<CharacterActor>() ?? actor.AddComponent<CharacterActor>(); result.BindLegacy(); return result;
        }
        private void Awake() { CharacterPartyStats.Ensure(gameObject); _player=GetComponent<PlayerController>(); _bot=GetComponent<PartyBotAI>(); _previous=transform.position; RegisterHandler("legacy.counter",ExecuteLegacyCounter); }
        public void BindLegacy()
        {
            int role=_player!=null?(int)_player.currentRole:_bot!=null?(int)_bot.role+1:0;
            if(role<1||role>4||_state!=null&&(_role==role||Definition.legacyRole==0))return;
            _role=role; _state=new CharacterRuntimeState(CharacterCatalog.Legacy(role));
        }
        public bool Available(CharacterSlot slot,out string reason)
        {
            BindLegacy();reason="";
            if(_state==null||!Definition.playable)reason="미구현 직업은 전투에서 선택할 수 없습니다";
            else if(!CharacterSlots.Enabled(Definition,slot))reason="현재 키 구성에서 사용하지 않는 슬롯입니다";
            else if(_state.Selected(slot)==null)reason="잠긴 슬롯입니다";
            else if(!_state.Unlocked(slot))reason=SlotUnlockStatus(slot);
            else if(!_state.Selected(slot).implemented)reason="아직 구현되지 않은 스킬입니다";
            return reason.Length==0;
        }
#if UNITY_EDITOR
        public bool PreviewExtension(string skillId,out string reason)
        {
            if(!CanEditSkillTest(out reason))return false;
            if(!_state.PreviewSkillChoice(skillId)){reason="시전 중이거나 제공되지 않은 후보입니다";return false;}
            CampaignRuntime.Instance?.MarkDevelopmentUse();
            GetComponent<AegisPaladin>()?.ClearTransient();
            reason="테스트 장착: "+Definition.Skill(skillId).name+" (실제 클리어/카드/선택 완료 기록은 변경하지 않음)";
            return true;
        }
        public bool CanEditSkillTest(out string reason)
        {
            reason="";
            var avatar=GetComponent<NetworkRaidAvatar>();
            if(!Application.isPlaying||_state==null||_player==null)reason="Play 모드에서 자신의 캐릭터를 선택하세요.";
            else if(RaidNetworkSession.Instance?.Ending==true)reason="세션 종료 중에는 사용할 수 없습니다.";
            else if(RaidCombatAuthority.Active&&(!RaidCombatAuthority.Host||avatar==null||!avatar.IsLocal))reason="멀티플레이에서는 호스트가 자신의 캐릭터에만 적용할 수 있습니다.";
            else if(!Definition.playable)reason="미구현 직업은 테스트 해금할 수 없습니다.";
            else if(_player.IsDead)reason="사망 중에는 사용할 수 없습니다. 부활 또는 재도전 후 사용하세요.";
            return reason.Length==0;
        }
        public bool ApplyEditorSkillTest(SkillTestOperation operation,out string message)
        {
            if(!CanEditSkillTest(out message))return false;
            if(!_state.ApplySkillTest(operation,out int affected))
            {message="시전 중이거나 기존 테스트 직업에서는 직업 스킬 쿨타임을 초기화할 수 없습니다.";return false;}
            if(operation==SkillTestOperation.FillResources)GetComponent<NineKeyTankKit>()?.SynchronizeDevelopmentResources();
            message=operation==SkillTestOperation.UnlockImplemented
                ? $"구현된 현재 슬롯 {affected}개 해금. 미구현 스킬은 제외하며 Z는 출발 전 루트 선택이 필요합니다."
                : operation==SkillTestOperation.FillResources?"직업 자원을 최대치로 채웠습니다."
                : "직업 스킬 쿨타임과 GCD를 초기화했습니다. 대쉬·포션·진행 중 효과는 유지됩니다.";
            Debug.Log($"[Skill Test] actor={RaidCombatAuthority.Id(transform)} job={Definition.id} operation={operation} affected={affected}",this);
            CampaignRuntime.Instance?.MarkDevelopmentUse();
            return true;
        }
#endif
        // Register a small effect adapter; the common state handles resources, casting, charges and combos.
        // A handler must validate before mutation and return false without side effects on failure.
        public void RegisterHandler(string id,Func<CharacterSkillDefinition,Transform,bool> handler) { if(!string.IsNullOrEmpty(id)&&handler!=null)_handlers[id]=handler; }
        public bool AssignDefinition(CharacterDefinition definition)
        {
            if (CampaignRuntime.Instance?.BuildLocked == true || RaidNetworkSession.Instance?.CampaignBuildLocked == true) return false;
            if(RaidCombatAuthority.Replica||definition==null||CharacterCatalog.Find(definition.id)!=definition||!definition.playable)return false;
            if(RaidCombatAuthority.Active?RaidNetworkSession.Instance.BattlePhase!=RaidBattlePhase.Lobby:StageManager.Instance==null||!StageManager.Instance.CanSelectInitialRole)return false;
            if(definition.id==AegisPaladinData.JobId) AegisPaladin.Ensure(gameObject);
            if(CharacterRosterData.Find(definition.id)!=null)CharacterKit.Ensure(gameObject);
            if(NineKeyTankKit.Supports(definition))NineKeyTankKit.Ensure(gameObject);
            if(NineKeyHealerKit.Supports(definition))NineKeyHealerKit.Ensure(gameObject);
            if(NineKeyDamageKit.Supports(definition))NineKeyDamageKit.Ensure(gameObject);
            if(!HasRequiredSkillHandlers(definition))return false;
            GetComponent<NineKeyHealerKit>()?.Clear();GetComponent<NineKeyDamageKit>()?.Clear();
            GetComponent<CharacterPartyStats>()?.ResetFactors();
            GetComponent<CharacterKit>()?.Clear();
            GetComponent<AegisPaladin>()?.ReleaseRouteEffects();
            _actionExecution?.Reset();_executingSkill=null;
            _state=new CharacterRuntimeState(definition); ObserveResources(); _role=definition.legacyRole;
            if(_player!=null){_player.maxHp=_player.currentHp=definition.baseHp;_player.combatRadius=definition.combatRadius;_player.moveSpeed=definition.moveSpeed;_player.playerRoleDisplayName=definition.name;_player.SetNetworkDisplayName(definition.name,NetworkRaidAvatar.RoleColor(CharacterRosterData.Role(definition)));}
            GetComponent<AegisPaladin>()?.RecalculateStats();GetComponent<CharacterKit>()?.Recalculate();
            return true;
        }
        private bool HasRequiredSkillHandlers(CharacterDefinition definition)
        {
            foreach(var skill in definition.skills)
            {
                if(!skill.implemented||skill.legacyAction!=0||skill.handlerId=="legacy.dash"||
                    !string.IsNullOrEmpty(skill.handlerId)&&_handlers.ContainsKey(skill.handlerId))continue;
                // Core choices live in the skill catalog for save/load, but slot zero never executes.
                // Still require a handler if the same skill is also exposed as an action anywhere.
                bool passiveOnly=definition.loadouts.Any(l=>(int)l.slot==0&&l.choices.Contains(skill.id))&&
                    !definition.slots.Any(s=>s.skillId==skill.id)&&
                    !definition.routes.Any(r=>r.identitySkillId==skill.id||r.unlockSkills.Contains(skill.id))&&
                    !definition.unlocks.Any(u=>u.choices.Contains(skill.id))&&
                    !definition.loadouts.Any(l=>(int)l.slot!=0&&l.choices.Contains(skill.id));
                if(!passiveOnly)return false;
            }
            return true;
        }
        public void UseLegacyRole()
        {
            if(RaidCombatAuthority.Replica)return;
            _actionExecution?.Reset();_executingSkill=null;
            GetComponent<CharacterKit>()?.Clear();
            GetComponent<AegisPaladin>()?.ReleaseRouteEffects();
            if(Definition?.legacyRole==0&&_player!=null)_player.ApplyRoleStatsAndVisuals(_player.currentRole,true);
            GetComponent<NineKeyHealerKit>()?.Clear();GetComponent<NineKeyDamageKit>()?.Clear();_state=null;_role=0;BindLegacy();
            if(_player!=null&&Definition!=null)_player.moveSpeed=Definition.moveSpeed*Mathf.Pow(1.3f,_player.ghostStepStacks);
        }
        private void ObserveResources()
        {
            _state.ResourceChanged += (id, delta) => {
                GetComponent<CharacterKit>()?.OnResourceChanged(id, delta);
            };
        }
        public void RefreshSkillCooldowns(System.Collections.Generic.IEnumerable<string> ids)
        {
            if (RaidCombatAuthority.Replica || _state == null || ids == null) return;
            var chosen=ids.ToArray(); _state.RefreshSkillCooldowns(chosen);
            if(chosen.Contains(Selected(CharacterSlot.Movement)?.id)) RefreshMovementCharges();
        }
        public bool CompletingCast=>_state?.CompletingCast==true;
        public bool HasAuxiliary(string id)=>_state?.HasAuxiliary(id)==true;
        public float CommittingCostScale=>_state?.CommittingCostScale??1;
        public float ResourceMaximum(string id)=>_state?.Maximum(id)??0;
        public int MaximumCharges(string id)=>_state?.MaximumCharges(id)??0;
        public float Resource(string id)=>_state?.Resource(id)??0;
        public bool SpendResource(string id,float amount)=>!RaidCombatAuthority.Replica&&_state!=null&&_state.Spend(id,amount);
        public void RestoreCharge(string id,int count=1){if(!RaidCombatAuthority.Replica)_state?.RestoreCharge(id,count);}
        public void ReduceCooldowns(System.Collections.Generic.IEnumerable<string> ids,float seconds)
        {
            if(RaidCombatAuthority.Replica||_state==null||ids==null||seconds<=0)return;
            var selectedIds=ids.ToArray();_state.ReduceCooldowns(selectedIds,seconds);
            // The authoritative dash motor owns recharge; the actor only mirrors it for the hotbar.
            if(selectedIds.Contains(Selected(CharacterSlot.Movement)?.id))_player?.ReduceMovementRecharge(seconds);
        }
        public void OnDeath()
        {if(RaidCombatAuthority.Replica)return;_actionExecution?.Reset();_executingSkill=null;_state?.ClearOnDeath();GetComponent<NineKeyTankKit>()?.Clear(true);GetComponent<NineKeyHealerKit>()?.Clear();GetComponent<NineKeyDamageKit>()?.Clear();GetComponent<AegisPaladin>()?.ClearTransient();GetComponent<CharacterKit>()?.Clear();GetComponent<CombatStatusEffects>()?.Clear();}
        public bool GainResource(string id,float amount) => !RaidCombatAuthority.Replica&&_state!=null&&_state.Gain(id,amount);
        // Only a successful direct effect inside this skill's host handler can
        // request its configured combo reward. No client-supplied receipt/amount.
        public bool ResolveComboResource(CharacterSkillDefinition skill,CombatComboResourcePolicy policy,bool resolved)
        {
            if(RaidCombatAuthority.Replica||_state==null||_executingSkill!=skill||policy==null||_actionExecution==null)return false;
            var resource=Array.Find(Definition.resources,r=>r.id==policy.ResourceId);
            if(resource==null||resource.integer&&!policy.SupportsIntegerResource)return false;
            if(!_actionExecution.TryResolveResource(policy,resolved,out float amount))return false;
            return _state.Gain(policy.ResourceId,amount);
        }
        public bool HasBuildEffect(string id)=>_state!=null&&_state.HasBuildEffect(id);
        public bool ResolvePaladinUltimateGauge(CharacterSkillDefinition skill)
        {
            if(RaidCombatAuthority.Replica||Definition?.id!=AegisPaladinData.JobId||_executingSkill!=skill||
                !Definition.resources.Any(r=>r.id==AegisPaladinData.UltimateGauge)||_actionExecution?.TryResolveFinisherGauge(true)!=true)return false;
            return _state.Gain(AegisPaladinData.UltimateGauge,AegisPaladinData.UltimateGain);
        }
        public void RecordBuildEffect(string id,int stacks,float magnitude=0) { if(!RaidCombatAuthority.Replica)_state?.SetBuildEffect(id,stacks,magnitude); }
        public void ClearedStage(int stage) { if(!RaidCombatAuthority.Replica&&StageManager.Instance!=null)_state?.ClearedStage(StageManager.Instance.RunId,StageManager.Instance.ChapterId,stage); }
        internal bool ChooseAiSpectatorPreset(string rule,string choice)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(RaidNetworkSession.Instance?.AiSpectatorActive!=true ||
                RaidNetworkSession.Instance.AiSpectatorStartingStage<=1 || CampaignRuntime.Instance?.IsolatedTestProfile!=true ||
                _state==null || RaidCombatAuthority.Replica)return false;
            return _state.ChooseSkill(rule,choice,ChoiceKey(rule),true);
#else
            return false;
#endif
        }
        public void ResetEncounter(CharacterReset reason)
        {
            BindLegacy();if(RaidCombatAuthority.Replica||_state==null)return;
            _actionExecution?.Reset();_executingSkill=null;
            string departureRoute=_state.Snapshot.routeId;
            _state.Reset(reason,CharacterSlots.IsNineKey(Definition));GetComponent<NineKeyTankKit>()?.Clear();GetComponent<NineKeyHealerKit>()?.Clear();
            GetComponent<AegisPaladin>()?.ClearTransient();GetComponent<CharacterKit>()?.Clear();GetComponent<CombatStatusEffects>()?.Clear();
            if(reason==CharacterReset.NewRun&&departureRoute.Length>0)_state.ChooseRoute(departureRoute,true);
            // DPS route state (for example the season period) depends on the restored core trait.
            GetComponent<NineKeyDamageKit>()?.Clear();
            if(reason==CharacterReset.NewRun&&StageManager.Instance!=null)_state.BeginRun(StageManager.Instance.RunId,StageManager.Instance.ChapterId);
            _player?.ClearResolvedCombatHits();_bot?.ClearResolvedCombatHits();GetComponent<AegisPaladin>()?.RecalculateStats();GetComponent<CharacterKit>()?.Recalculate();
        }
        public void InterruptCast() { if(!RaidCombatAuthority.Replica){_state?.CancelCast();GetComponent<NineKeyTankKit>()?.Interrupt();GetComponent<NineKeyHealerKit>()?.Interrupt();GetComponent<NineKeyDamageKit>()?.Interrupt();GetComponent<CharacterKit>()?.CancelCasting();_player?.CancelSkillRush();GetComponent<AegisExtensions>()?.CancelChannel();GetComponent<CharacterKit>()?.CancelChannel();} }
        public bool Choose(string rule,string choice,out string reason,string rewardKey=null,bool automatic=false)
        {
            reason="루트/스킬 선택 조건을 충족하지 않습니다";
            if(Definition.loadouts.Any(r=>r.id==rule)||CharacterRuntimeState.AuxiliaryIndex(rule)>=0)
            {
                if(RaidCombatAuthority.Replica||!CanChangeLoadout)return false;
                string compatibility=_state.BuildChoiceFailure(rule,choice);
                if(compatibility.Length>0){reason=compatibility;return false;}
                bool changed=CharacterRuntimeState.AuxiliaryIndex(rule)>=0?_state.ChangeAuxiliary(rule,choice,true):_state.ChangeLoadout(rule,choice,true);
                if(changed){GetComponent<NineKeyTankKit>()?.Clear(true);GetComponent<NineKeyHealerKit>()?.Clear();GetComponent<NineKeyDamageKit>()?.Clear();GetComponent<AegisPaladin>()?.ClearTransient();reason="";}
                return changed;
            }
            if (rule == "route" && (CampaignRuntime.Instance?.BuildLocked == true || RaidNetworkSession.Instance?.CampaignBuildLocked == true)) return false;
            if(RaidCombatAuthority.Replica||_state==null)return false;
            bool lobby=RaidCombatAuthority.Active?RaidNetworkSession.Instance.BattlePhase==RaidBattlePhase.Lobby:StageManager.Instance!=null&&StageManager.Instance.CanSelectInitialRole;
            bool reward=RaidCombatAuthority.Active?RaidNetworkSession.Instance.BattlePhase==RaidBattlePhase.Reward:StageManager.Instance?.Progress.Phase==RaidBattlePhase.Reward;
            string previousRoute=RouteId;
            bool ok=rule=="route"?_state.ChooseRoute(choice,lobby):reward&&_state.ChooseSkill(rule,choice,rewardKey,automatic);
            if(ok){reason="";if(rule=="route"&&previousRoute!=RouteId){GetComponent<AegisPaladin>()?.ApplyRouteChoice();GetComponent<CharacterKit>()?.Clear();GetComponent<NineKeyTankKit>()?.Clear(true);GetComponent<NineKeyHealerKit>()?.Clear();GetComponent<NineKeyDamageKit>()?.Clear();}}return ok;
        }
        public void RequestChoice(string rule,string choice)
        {
            if(!RaidCombatAuthority.Local(this)||LocalCameraSettingsUI.IsOpen)return;
            if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(11,Definition.id,0,0,rule,choice);
            else if(!Choose(rule,choice,out string reason))Report(reason);
        }
        public bool CanChangeLoadout=>_state!=null&&(RaidCombatAuthority.Active?
            RaidNetworkSession.Instance.BattlePhase==RaidBattlePhase.Lobby&&RaidNetworkSession.Instance.ControlsReady:
            StageManager.Instance?.Progress.Phase==RaidBattlePhase.Lobby);
        public void RequestSlot(CharacterSlot slot)
        {
            if(RaidCombatAuthority.InputBlocked(this))return;
            var skill=_state?.Selected(slot);
            if(slot==CharacterSlot.Identity&&GetComponent<NineKeyHealerKit>()?.Active==true){GetComponent<NineKeyHealerKit>().RequestIdentity();return;}
            if(slot==CharacterSlot.Identity&&GetComponent<NineKeyDamageKit>()?.Active==true){GetComponent<NineKeyDamageKit>().RequestIdentity();return;}
            if(skill==null)return;
            var glyphPaladin=GetComponent<AegisPaladin>();
            if(slot==CharacterSlot.Identity&&glyphPaladin?.NewBulwark==true)
            {
                if(!glyphPaladin.TryPointerGlyph(out Vector3 point)){Report("문양을 설치할 유효한 지상 위치를 가리키세요 (20m)");return;}
                if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(10,Definition.id,(int)slot,0,"ground",point.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+point.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                else if(!Execute(slot,transform,out string glyphFailure,null,point))Report(glyphFailure);
                return;
            }
            Transform target;
            var targeting=TargetingManager.Instance;
            var tank=GetComponent<NineKeyTankKit>();
            if(tank?.Active==true&&tank.Blood&&tank.Route==2&&(slot==CharacterSlot.Identity||slot==CharacterSlot.Gcd2)&&tank.State?.charging==true&&!LocalInputBindings.Held(CharacterSlots.Binding(Definition,slot)))
            {tank.RequestRelease(slot);return;}
            if(NineKeyHealerKit.Supports(Definition)&&(slot==CharacterSlot.Identity||slot==CharacterSlot.Ultimate)) target=targeting?.AttackTarget;
            else if(tank?.Active==true&&(slot==CharacterSlot.Identity||slot==CharacterSlot.Ultimate||slot==CharacterSlot.OffGcd2))
            {
                target=slot==CharacterSlot.OffGcd2||slot==CharacterSlot.Identity&&tank.Blood&&tank.Route==1?targeting?.CurrentTarget:targeting?.AttackTarget;
                if(slot==CharacterSlot.OffGcd2&&target==null){Report("도발할 적 또는 인계할 다른 탱커를 선택하세요");return;}
            }
            else if(CharacterSlots.IsNineKey(Definition)&&Definition.id==AegisPaladinData.JobId&&slot==CharacterSlot.Ultimate)
                target=targeting?.AttackTarget;
            else if(CharacterSlots.IsNineKey(Definition)&&Definition.id==AegisPaladinData.JobId&&slot==CharacterSlot.OffGcd2)
            {
                target=targeting?.CurrentTarget;
                if(target==null){Report("도발할 적 또는 인계할 다른 탱커를 선택하세요");return;}
            }
            else if(targeting!=null)
            {
                if(!targeting.TryResolveSkillTarget(skill.target,transform,StageManager.Instance?.boss,out target,out string failure))
                {Report(failure);return;}
            }
            else target=skill.target==SkillTargetRule.Self||skill.target==SkillTargetRule.LivingAlly?transform:null;
            bool support=NineKeyDamageKit.Supports(Definition)||NineKeyHealerKit.Supports(Definition)||tank?.Active==true||Definition.id==AegisPaladinData.JobId&&CharacterSlots.IsNineKey(Definition)&&(slot==CharacterSlot.Gcd2||slot==CharacterSlot.Identity&&GetComponent<AegisPaladin>()?.NewMirror==true);
            Transform ally=support?(targeting!=null&&targeting.HasSupportSelection?targeting.SupportTarget:transform):null;
            if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(10,Definition.id,(int)slot,RaidCombatAuthority.Id(target),support?"support":"",support?RaidCombatAuthority.Id(ally).ToString(System.Globalization.CultureInfo.InvariantCulture):"");
            else if(!Execute(slot,target,out string reason,ally))Report(reason);
        }
        // Read-only runtime validation lets AI reject unavailable casts before requesting them.
        public bool CanExecute(CharacterSlot slot,Transform target,out string reason)
        {
            if(StageManager.Instance?.boss?.GuardianLured(transform)==true){reason="유혹 상태 · 힐러 구출 대기";return false;}
            if(!Available(slot,out reason))return false;
            return _state.Validate(slot,Context(_state.Selected(slot),target),out reason);
        }
        public bool Execute(CharacterSlot slot,Transform target,out string reason,Transform supportTarget=null,Vector3? groundPoint=null,bool recall=false,DamagePrismCommand prism=null)
        {
            reason="호스트만 전투를 확정할 수 있습니다";
            if(RaidCombatAuthority.Replica||!Available(slot,out reason))return false;
            if(prism!=null&&(slot!=CharacterSlot.Identity||recall||GetComponent<NineKeyDamageKit>()?.CanUsePrism(prism)!=true)){reason="프리즘 대상·위치·방향이 유효하지 않습니다";return false;}
            if(prism!=null)groundPoint=new Vector3(prism.point.x,transform.position.y,prism.point.z);
            if(recall&&(slot!=CharacterSlot.Identity||GetComponent<AegisPaladin>()?.Patrol!=true&&GetComponent<NineKeyHealerKit>()?.AllowsLongInput!=true&&GetComponent<NineKeyDamageKit>()?.UsesLongInput!=true)){reason="현재 루트에서 지원하지 않는 긴 입력입니다";return false;}
            if(StageManager.Instance?.boss?.GuardianLured(transform)==true){reason="유혹 상태 · 힐러 구출 대기";return false;}
            GetComponent<NineKeyHealerKit>()?.BeforeInput(slot);
            if(GetComponent<NineKeyHealerKit>()?.Active==true)
            {reason=GetComponent<NineKeyHealerKit>().Unavailable(slot);if(reason.Length>0)return false;}
            var skill=_state.Selected(slot);
            var damageKit=GetComponent<NineKeyDamageKit>();
            if(damageKit?.Active==true){reason=damageKit.Unavailable(slot);if(reason.Length>0)return false;}
            var encounter=StageManager.Instance?.boss;
            if(encounter?.MarionetteControlled(transform)==true)
            {reason="조종당함 · 동료가 등 뒤의 인형의 실을 끊어야 합니다";return false;}
            if(encounter!=null && (encounter.GuardianClashHolding(transform) || encounter.CampanellaLockHolding(transform)) && slot!=CharacterSlot.Potion &&
                (slot==CharacterSlot.Movement || skill.target!=SkillTargetRule.Self))
            { reason="힘겨루기 중에는 포션과 자기 대상 방어기만 사용할 수 있습니다"; return false; }
            var kit=GetComponent<CharacterKit>();
            var tank=GetComponent<NineKeyTankKit>();
            if(tank?.Active==true)
            {
                tank.BeforeInput(slot);
                reason=tank.Unavailable(slot);if(reason.Length>0)return false;
                if(tank.NeedsSecondCharge(slot))
                {
                    if(!_state.Validate(slot,Context(skill,target),out reason))return false;
                    if(!tank.BeginSecondCharge(target,supportTarget)){reason="완주 차징을 시작할 수 없습니다";return false;}
                    reason="";return true;
                }
            }
            if(GetComponent<AegisExtensions>()?.Channeling==true&&CharacterSlots.InterruptsCast(Definition,slot))
            {
                if(!_state.Validate(slot,Context(skill,target),out reason))return false;
                if(slot==CharacterSlot.OffGcd2&&GetComponent<AegisPaladin>()?.CanUseThreatCall(target)!=true){reason="도발/인계 대상 또는 보스 참조가 유효하지 않습니다";return false;}
                InterruptCast();
            }
            if(kit?.Active==true){if(kit.TryReturn(slot))return true;reason=kit.Unavailable(skill,target);if(reason.Length>0)return false;}
            if(skill.handlerId=="aegis.extension")
            {reason=GetComponent<AegisExtensions>()?.Unavailable(skill,target)??"스킬 실행기가 아직 연결되지 않았습니다";if(reason.Length>0)return false;}
            if(GetComponent<AegisExtensions>()?.Channeling==true&&slot!=CharacterSlot.Movement&&slot!=CharacterSlot.Potion){reason="결계의 날개 수호 중입니다. 대쉬로 종료할 수 있습니다";return false;}
            if(!recall&&slot==CharacterSlot.Identity&&GetComponent<AegisPaladin>()?.IdentityBlocked==true){reason=GetComponent<AegisPaladin>().NewBulwark?"수호 문양 최대 수 · 만료 또는 미사용 문양 회수 후 설치하세요":"아이덴티티 효과가 이미 진행 중입니다";return false;}
            if(skill.legacyAction>0)
            {
                if(_player==null){reason="이 캐릭터는 기존 AI 전투 처리기를 사용합니다";return false;}
                var combat=_player.combatController;
                if(skill.legacyAction==6){bool used=_player.Potion.TryUse();reason=used?"":"포션을 사용할 수 없습니다";return used;}
                if(!combat.ValidateCombatSkill(skill.legacyAction,out reason))return false;
                switch(skill.legacyAction){case 1:combat.TryUseSkill1_FlankCombo();break;case 2:combat.TryUseSkill2_RearCombo();break;case 3:combat.TryUseSkill3_PersonalDefensive_oGCD();break;case 4:combat.TryUseSkill4_GaleThrust_oGCD();break;}
                SyncLegacy();return true;
            }
            if(!_handlers.ContainsKey(skill.handlerId)){reason="스킬 실행기가 아직 연결되지 않았습니다";return false;}
            Vector3 rushEnd=transform.position;
            bool rushing=slot==CharacterSlot.Counter&&Definition.id==AegisPaladinData.JobId&&!CharacterSlots.IsNineKey(Definition);
            if(rushing)
            {
                if(!_state.Validate(slot,Context(skill,target),out reason))return false;
                if(_player==null||!_player.PlanSkillRush(target,skill.movement.distance,out rushEnd)){reason="전방 돌진 경로 또는 이동 상태가 유효하지 않습니다";return false;}
            }
            bool accepted;
            ActionSupportTarget=supportTarget;ActionGroundPoint=groundPoint;ActionRecall=recall;ActionPrism=prism;
            try
            {
                var context=Context(skill,target);
                if(damageKit?.Active==true)
                {if(!_state.Validate(slot,context,out reason))return false;damageKit.BeforeInput(slot);}
                accepted=_state.TryUse(slot,context,RaidCombatAuthority.Id(target),s=>Apply(s,target),out reason);
            }
            finally{ActionSupportTarget=null;ActionGroundPoint=null;ActionRecall=false;ActionPrism=null;}
            if(accepted&&slot==CharacterSlot.Job1&&CharacterSlots.IsNineKey(Definition)&&CharacterRosterData.Find(Definition.id)?.Role!=2)NineKeyHealerKit.OnPersonalDefense(transform);
            if(accepted&&_state.Snapshot.castSkillId.Length>0){kit?.BeganCast(skill);_singleCastTarget=target;if(rushing)_player.BeginSkillRush(rushEnd,skill.castTime);}return accepted;
        }
        private bool Apply(CharacterSkillDefinition skill,Transform target)
        {
            if(RaidCombatAuthority.Replica||!_handlers.TryGetValue(skill.handlerId,out var handler))return false;
            if(_actionExecution==null)_actionExecution=new CombatActionExecution(GetInstanceID());
            var purpose=skill.target==SkillTargetRule.LivingEnemy?CombatActionPurpose.Attack:
                skill.target==SkillTargetRule.LivingAlly?CombatActionPurpose.Heal:CombatActionPurpose.Unknown;
            int step=skill.comboChain==1?_state.Snapshot.combo1:skill.comboChain==2?_state.Snapshot.combo2:0;
            bool combat=RaidLobbyRoom.PracticeActive||(RaidCombatAuthority.Active?
                RaidNetworkSession.Instance.CombatRunning:StageManager.Instance?.Progress.Phase==RaidBattlePhase.Combat);
            bool targetValid=purpose==CombatActionPurpose.Attack?CombatRange.LivingEnemy(target):
                purpose==CombatActionPurpose.Heal&&CombatRange.LivingPartyMember(target);
            if(!_actionExecution.Begin(purpose,skill.comboChain,step,combat,targetValid))return false;
            _executingSkill=skill;
            try
            {
                if(!handler(skill,target))return false;
                GetComponent<CommonPotion>()?.EndRevivalProtection();return true;
            }
            finally{_executingSkill=null;_actionExecution.End();}
        }
        private bool ExecuteLegacyCounter(CharacterSkillDefinition skill,Transform target)
        {
            if(RaidCombatAuthority.Replica||Definition?.legacyRole<=0||!CombatRange.LivingPartyMember(transform)||!CombatRange.LivingEnemy(target)||!CombatRange.InRange(transform,target,skill.range))return false;
            var counter=CombatCounterTarget.Ensure(target);
            int opportunity=counter.OpportunityId;
            bool canCounter=counter.CanCounter(transform,skill.range);
            int damage=Definition.legacyRole==1?160:Definition.legacyRole==2?140:180;
            float stagger=40;
            var boss=target.GetComponent<BossController>();
            var add=boss==null?target.GetComponent<BossAddMinion>():null;
            float before=boss!=null?boss.currentHp:add?.currentHp??0;
            if(boss!=null)boss.ReceiveRoleSkillAttack(Definition.name,skill.name,0,damage,Color.cyan,false,source:transform,origin:skill.id);
            else add?.ReceivePlayerAttack(Definition.name,skill.name,damage,Color.cyan,false,source:transform,origin:skill.id);
            float after=boss!=null?boss.currentHp:add?.currentHp??0;
            if(after>=before)return false;
            if(!canCounter||!counter.TryCounter(transform,skill.range,stagger,opportunity))counter.AddStagger(stagger);
            _player?.FaceAttackTarget(target);
            return true;
        }
        private CharacterUseContext Context(CharacterSkillDefinition skill,Transform target)
        {
            bool alive=_player!=null?!_player.IsDead:_bot!=null&&!_bot.isDead;
            bool can=!StageManager.PlayerSkillsSuspended&&(_player==null||!_player.IsStunned&&!_player.IsBeingRescued&&!_player.IsBeingKnockedBack);
            bool valid=skill.target==SkillTargetRule.None||skill.target==SkillTargetRule.Self;
            if(skill.target==SkillTargetRule.LivingEnemy)valid=CombatRange.LivingEnemy(target);
            if(skill.target==SkillTargetRule.LivingAlly)valid=CombatRange.LivingPartyMember(target);
            if(skill.target==SkillTargetRule.DeadAlly)valid=target!=null&&PartyLife.OrderedMembers().Contains(target)&&target.GetComponent<PartyLife>()?.IsDead==true;
            if(GetComponent<CharacterKit>()?.CanAirAttack(skill,target)==true)valid=true;
            float distance=valid&&target!=null&&skill.target!=SkillTargetRule.Self&&skill.target!=SkillTargetRule.None?CombatRange.BoundaryDistance(transform,target):0;
            var kit=GetComponent<CharacterKit>();if(kit?.CanAirAttack(skill,target)==true)distance=0;
            return new CharacterUseContext(alive,can,valid,distance,_player!=null&&_player.IsMovingXZ&&!PreservesCastWhileDashing,EffectiveRange(skill),GetComponent<NineKeyDamageKit>()?.Active==true?GetComponent<NineKeyDamageKit>().GcdScale(skill):kit?.Active==true?kit.GcdScale(skill):NineKeyTankKit.Supports(Definition)?1:GetComponent<AegisPaladin>()?.GcdScale(skill)??1,GetComponent<NineKeyDamageKit>()?.Active==true?GetComponent<NineKeyDamageKit>().CastTime(skill):kit?.Active==true?kit.CastTime(skill):-1,ActionRecall?0:GetComponent<NineKeyDamageKit>()?.Active==true?GetComponent<NineKeyDamageKit>().CostScale(skill):kit?.Active==true?kit.CostScale(skill):1,kit?.Active==true&&kit.AllowsMovingCast(skill),GetComponent<NineKeyDamageKit>()?.Active==true?GetComponent<NineKeyDamageKit>().CooldownScale(skill):GetComponent<NineKeyHealerKit>()?.Active==true?GetComponent<NineKeyHealerKit>().CooldownScale(skill):kit?.Active==true?kit.CooldownScale(skill):GetComponent<NineKeyTankKit>()?.Active==true?GetComponent<NineKeyTankKit>().CooldownScale(skill):GetComponent<AegisPaladin>()?.CooldownScale(skill)??1,preserveCombo:GetComponent<NineKeyDamageKit>()?.IsFourth(skill)==true);
        }
        private void UpdateRecallInput()
        {
            bool active=_player!=null&&RaidCombatAuthority.Local(this)&&GetComponent<AegisPaladin>()?.Patrol==true;
            string binding=CharacterSlots.Binding(Definition,CharacterSlot.Identity);
            bool blocked=!active||RaidCombatAuthority.InputBlocked(this);
            foreach(var slot in new[]{CharacterSlot.Job1,CharacterSlot.Counter,CharacterSlot.Movement,CharacterSlot.OffGcd2})
                if(LocalInputBindings.Pressed(CharacterSlots.Binding(Definition,slot)))blocked=true;
            int gesture=_recallInput.Step(LocalInputBindings.Pressed(binding),LocalInputBindings.Held(binding),Time.unscaledTime,blocked);
            if(gesture==1)RequestSlot(CharacterSlot.Identity);
            if(gesture==2)
            {
                if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(10,Definition.id,(int)CharacterSlot.Identity,0,"recall");
                else if(!Execute(CharacterSlot.Identity,transform,out string failure,recall:true))Report(failure);
            }
        }
        private void Update()
        {
            BindLegacy();if(_state==null)return;
            GetComponent<NineKeyTankKit>()?.UpdateInput();GetComponent<NineKeyHealerKit>()?.UpdateInput();GetComponent<NineKeyDamageKit>()?.UpdateInput();
            UpdateRecallInput();
            if(_player!=null&&RaidCombatAuthority.Local(this)&&!RaidCombatAuthority.InputBlocked(this))
                foreach(var slot in CharacterSlots.All)
                    if(slot==CharacterSlot.Counter||slot==CharacterSlot.Job2||slot>=CharacterSlot.Identity&&slot<=CharacterSlot.Ultimate)
                        if(!(slot==CharacterSlot.Identity&&(GetComponent<AegisPaladin>()?.Patrol==true||GetComponent<NineKeyHealerKit>()?.UsesLongInput==true||GetComponent<NineKeyDamageKit>()?.UsesLongInput==true||GetComponent<NineKeyDamageKit>()?.UsesPrismInput==true))&&CharacterSlots.Enabled(Definition,slot)&&LocalInputBindings.Pressed(CharacterSlots.Binding(Definition,slot)))RequestSlot(slot);
            if(_player!=null&&RaidCombatAuthority.Local(this))
            {
                bool held=LocalInputBindings.Held(CharacterSlots.Binding(Definition,CharacterSlot.Identity));
                if(!CharacterSlots.IsNineKey(Definition)&&_identityHeld&&!held&&Definition.id=="job.berserker"&&RouteId.EndsWith(".3"))
                {if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(13,Definition.id,0);else GetComponent<CharacterKit>()?.CancelChannel();}
                _identityHeld=held;
            }
            if(RaidCombatAuthority.Replica)return;
            bool moved=(transform.position-_previous).sqrMagnitude>.000001f && !PreservesCastWhileDashing;_previous=transform.position;
            if(moved)GetComponent<NineKeyDamageKit>()?.Moved();
            if(Definition.legacyRole>0){_state.Tick(Time.deltaTime,false,_player!=null?!_player.IsDead:_bot!=null&&!_bot.isDead);SyncLegacy();return;}
            if(StageManager.PlayerSkillsSuspended)
            {
                if(RaidLobbyRoom.Active || RaidApproachField.Instance?.Walking==true)
                    _state.Tick(Time.deltaTime,moved,_player!=null?!_player.IsDead:_bot!=null&&!_bot.isDead);
                return;
            }
            float cooldownBonus=CharacterKit.Bonus(transform,"cooldownRate");
            if(cooldownBonus>0)_player?.ReduceMovementRecharge(Time.deltaTime*cooldownBonus);
            if(cooldownBonus>0)_state.ReduceCooldowns(Definition.skills.Where(s=>s.id!=Selected(CharacterSlot.Ultimate)?.id&&s.id!=Selected(CharacterSlot.Identity)?.id).Select(s=>s.id),Time.deltaTime*cooldownBonus);
            // Reuse the existing party attack-speed buff without accelerating oGCD/debt/combo timers.
            float gcdRate=_player?.cardManager?.HasPartyAttackSpeedBuff==true?1.35f:1;
            _state.Tick(Time.deltaTime,moved,_player!=null?!_player.IsDead:_bot!=null&&!_bot.isDead,gcdRate);
            string castSkillId=_state.CastingSkillId;
            float castRemaining=_state.CastingRemaining;
            int castTarget=_state.CastingTarget;
            if(castSkillId.Length==0&&GetComponent<NineKeyDamageKit>()?.GroundAssaultActive!=true)_player?.CancelSkillRush();
            // Network movement advances at fixed server steps, while casts use the frame clock.
            // Never resolve a lunge hit until its swept movement has actually completed.
            if(castSkillId.Length>0&&castRemaining<=0&&_player?.IsSkillRushing!=true)
            {
                Transform target=RaidCombatAuthority.Active?RaidNetworkSession.Instance.CombatTarget(castTarget):_singleCastTarget;
                var skill=Definition.Skill(castSkillId);
                if(!_state.FinishCast(Context(skill,target),s=>Apply(s,target),out string reason))Report(reason);
            }
        }
        public bool StartMovement(DashMotion motion,DashSettings fallback,bool allowed,float x,float z,float facingX,float facingZ,int? supportId=null)
        {
            if(!Available(CharacterSlot.Movement,out _)||!allowed)return false;
            var kit=GetComponent<CharacterKit>();if(kit?.Active==true&&kit.DashBlocked)return false;
            if(kit?.TryReturn(CharacterSlot.Movement)==true)return false;
            // Space follows movement input; target selection must never redirect it.
            var settings=MovementSettings(fallback);
            if(Definition.legacyRole>0||RaidCombatAuthority.Replica)return motion.TryStart(settings,true,x,z,facingX,facingZ);
            GetComponent<NineKeyTankKit>()?.BeforeInput(CharacterSlot.Movement);GetComponent<NineKeyHealerKit>()?.BeforeInput(CharacterSlot.Movement);
            var skill=_state.Selected(CharacterSlot.Movement);
            _state.MirrorCharges(CharacterSlot.Movement,motion.MaximumCharges!=settings.maximumCharges&&motion.RechargeRemaining<=0?settings.maximumCharges:motion.Charges,(float)motion.RechargeRemaining,(float)motion.CooldownDuration);
            var context=new CharacterUseContext(_player!=null?!_player.IsDead:_bot!=null&&!_bot.isDead,allowed,true,0);
            Vector3 before=transform.position;bool started=_state.TryUse(CharacterSlot.Movement,context,0,s=>
            {bool success=motion.TryStart(settings,true,x,z,facingX,facingZ);if(success)GetComponent<NineKeyDamageKit>()?.BeforeInput(CharacterSlot.Movement);return success;},out _);if(started){GetComponent<NineKeyDamageKit>()?.MovementStarted();kit?.MovementStarted(before);GetComponent<NineKeyHealerKit>()?.MovementStarted(supportId);}return started;
        }
        public bool HandleLegacyInput(int action)
        {
            BindLegacy();if(Definition==null||Definition.legacyRole>0)return false;
            if(RaidCombatAuthority.Active&&RaidCombatAuthority.ExecutingActor!=0)Execute(CharacterSlots.LegacySlot(action),GetComponent<NetworkRaidAvatar>()?.CombatTarget,out _);
            else RequestSlot(CharacterSlots.LegacySlot(action));return true;
        }
        public void MarkDashMovement(bool preserveCast) { if(preserveCast)_preservedDashFrame=Time.frameCount; }
        public bool PreservesCastWhileDashing => _preservedDashFrame==Time.frameCount || _player!=null&&_player.IsDashing&&MovementSettings(_player.dashSettings).preserveCast;
        public DashSettings MovementSettings(DashSettings fallback)
        {
            if(RaidCombatAuthority.Active&&RaidNetworkSession.Instance.CommonDashSettings!=null)fallback=RaidNetworkSession.Instance.CommonDashSettings;
            if(Definition==null||Definition.legacyRole>0)return fallback;
            var skill=_state.Selected(CharacterSlot.Movement);var movement=skill?.movement;
            if(NineKeyDamageKit.Supports(Definition)&&movement!=null)
                return new DashSettings{distance=movement.distance,duration=movement.duration,cooldown=skill.cooldown*(HasAuxiliary(NineKeyDamageData.Mobile)?1.25f:1),maximumCharges=_state.MaximumCharges(skill.id),preserveCast=movement.preserveCast};
            if(NineKeyTankKit.Supports(Definition)&&movement!=null)
            {
                bool measured=HasAuxiliary(NineKeyTankData.MeasuredStep);
                return new DashSettings{distance=movement.distance+(measured?1:0),duration=movement.duration,cooldown=skill.cooldown+(measured?2:0),maximumCharges=skill.maximumCharges,preserveCast=false};
            }
            return movement==null?fallback:new DashSettings{distance=movement.ResolveDistance(fallback.distance),duration=GetComponent<CharacterKit>()?.Active==true&&GetComponent<CharacterKit>().Job.index==10?.01f:movement.duration,cooldown=GetComponent<CharacterKit>()?.Active==true&&GetComponent<CharacterKit>().Job.index==10&&GetComponent<CharacterKit>().Overcome?8:skill.cooldown,maximumCharges=skill.maximumCharges,preserveCast=movement.preserveCast,backwardFromFacing=Definition.id==AegisPaladinData.JobId&&CharacterSlots.IsNineKey(Definition)};
        }
        public void RefreshMovementCharges()
        {
            if(RaidCombatAuthority.Replica||!CombatRange.LivingPartyMember(transform))return;
            if(_player!=null)_player.RefillMovementCharges();
            else {var skill=Selected(CharacterSlot.Movement);if(skill!=null)_state.MirrorCharges(CharacterSlot.Movement,skill.maximumCharges,0,skill.cooldown);}
        }
        private void Report(string reason) { if(RaidCombatAuthority.Local(this))_player?.combatController?.ReportNetworkFailure(reason); }
        private void SyncLegacy()
        {
            if(_state==null||RaidCombatAuthority.Replica)return;
            if(Definition.legacyRole>0)
            {
            var data=new RaidCombatActor();
            if(_player!=null)_player.CaptureNetworkCombat(data);else _bot?.CaptureNetworkCombat(data);
            _state.MirrorLegacy(data.gcd,data.gcdTotal,Mathf.Clamp(data.combo1,1,3),Mathf.Clamp(data.combo2,1,3),data.comboTime1,data.comboTime2,data.castName,data.castRemaining,data.castTotal);
            _state.MirrorCooldown(CharacterSlot.OffGcd1,data.cd3,data.cd3Total);_state.MirrorCooldown(CharacterSlot.Job1,data.cd4,data.cd4Total);
            }
            var potion=GetComponent<CommonPotion>(); if(potion!=null)_state.MirrorCooldown(CharacterSlot.Potion,potion.CooldownRemaining,potion.Settings.cooldown);
            if(_player!=null) { var dash=_player.CaptureDash(); if(dash.MaximumCharges==_state.MaximumCharges(_state.Selected(CharacterSlot.Movement).id)) _state.MirrorCharges(CharacterSlot.Movement,dash.Charges,dash.Recharge,dash.CooldownTotal); }
        }
        public bool Restore(CharacterStateSnapshot snapshot)
        {
            if(!RaidCombatAuthority.Replica||snapshot==null)return false;
            var definition=CharacterCatalog.Find(snapshot.jobId);if(definition==null||!definition.playable)return false;
            if(definition.id==AegisPaladinData.JobId) AegisPaladin.Ensure(gameObject);
            if(CharacterRosterData.Find(definition.id)!=null)CharacterKit.Ensure(gameObject);
            if(NineKeyTankKit.Supports(definition))NineKeyTankKit.Ensure(gameObject);
            if(NineKeyHealerKit.Supports(definition))NineKeyHealerKit.Ensure(gameObject);
            if(NineKeyDamageKit.Supports(definition))NineKeyDamageKit.Ensure(gameObject);
            if(_state==null||Definition.id!=definition.id){var candidate=new CharacterRuntimeState(definition);if(!candidate.Restore(snapshot))return false;_state=candidate;_role=definition.legacyRole;return true;}
            return _state.Restore(snapshot);
        }
    }
}
