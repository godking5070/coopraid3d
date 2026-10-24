using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    // One actor-owned state; never attached to a shared ScriptableObject or a connection ID.
    [DefaultExecutionOrder(500)]
    public sealed class CharacterActor : MonoBehaviour
    {
        private CharacterRuntimeState _state;
        private PlayerController _player;
        private PartyBotAI _bot;
        private bool _identityHeld;
        private int _role; private int _preservedDashFrame = -1; private Transform _singleCastTarget;
        private Vector3 _previous;
        private readonly Dictionary<string, Func<CharacterSkillDefinition, Transform, bool>> _handlers = new Dictionary<string, Func<CharacterSkillDefinition, Transform, bool>>();
        public float PrimaryRange => EffectiveRange(_state?.Selected(CharacterSlot.Gcd1));
        public float EffectiveRange(CharacterSkillDefinition skill)=>GetComponent<CharacterKit>()?.Active==true?GetComponent<CharacterKit>().EffectiveRange(skill):GetComponent<AegisPaladin>()?.EffectiveRange(skill)??skill?.range??0;
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
        private void Awake() { CharacterPartyStats.Ensure(gameObject); _player=GetComponent<PlayerController>(); _bot=GetComponent<PartyBotAI>(); _previous=transform.position; }
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
            message=operation==SkillTestOperation.UnlockImplemented
                ? $"구현된 현재 슬롯 {affected}개 해금. 미구현 스킬은 제외하며 Z는 출발 전 루트 선택이 필요합니다."
                : operation==SkillTestOperation.FillResources?"직업 자원을 최대치로 채웠습니다."
                : "직업 스킬 쿨타임과 GCD를 초기화했습니다. 대쉬·포션·진행 중 효과는 유지됩니다.";
            Debug.Log($"[Skill Test] actor={RaidCombatAuthority.Id(transform)} job={Definition.id} operation={operation} affected={affected}",this);
            return true;
        }
#endif
        // Register a small effect adapter; the common state handles resources, casting, charges and combos.
        // A handler must validate before mutation and return false without side effects on failure.
        public void RegisterHandler(string id,Func<CharacterSkillDefinition,Transform,bool> handler) { if(!string.IsNullOrEmpty(id)&&handler!=null)_handlers[id]=handler; }
        public bool AssignDefinition(CharacterDefinition definition)
        {
            GetComponent<CharacterPartyStats>()?.ResetFactors();
            if(RaidCombatAuthority.Replica||definition==null||CharacterCatalog.Find(definition.id)!=definition||!definition.playable)return false;
            if(RaidCombatAuthority.Active?RaidNetworkSession.Instance.BattlePhase!=RaidBattlePhase.Lobby:StageManager.Instance==null||!StageManager.Instance.CanSelectInitialRole)return false;
            if(definition.id==AegisPaladinData.JobId) AegisPaladin.Ensure(gameObject);
            if(CharacterRosterData.Find(definition.id)!=null)CharacterKit.Ensure(gameObject);
            foreach(var skill in definition.skills) if(skill.implemented&&skill.legacyAction==0&&skill.handlerId!="legacy.dash"&&!_handlers.ContainsKey(skill.handlerId))return false;
            GetComponent<CharacterKit>()?.Clear();
            GetComponent<AegisPaladin>()?.ReleaseRouteEffects();
            _state=new CharacterRuntimeState(definition); _role=definition.legacyRole;
            if(_player!=null){_player.maxHp=_player.currentHp=definition.baseHp;_player.combatRadius=definition.combatRadius;_player.moveSpeed=definition.moveSpeed;_player.playerRoleDisplayName=definition.name;_player.SetNetworkDisplayName(definition.name,NetworkRaidAvatar.RoleColor(CharacterRosterData.Role(definition)));}
            GetComponent<AegisPaladin>()?.RecalculateStats();GetComponent<CharacterKit>()?.Recalculate();
            return true;
        }
        public void UseLegacyRole()
        {
            if(RaidCombatAuthority.Replica)return;
            GetComponent<CharacterKit>()?.Clear();
            GetComponent<AegisPaladin>()?.ReleaseRouteEffects();
            if(Definition?.legacyRole==0&&_player!=null)_player.ApplyRoleStatsAndVisuals(_player.currentRole,true);
            _state=null;_role=0;BindLegacy();
            if(_player!=null&&Definition!=null)_player.moveSpeed=Definition.moveSpeed*Mathf.Pow(1.3f,_player.ghostStepStacks);
        }
        public bool CompletingCast=>_state?.CompletingCast==true;
        public float CommittingCostScale=>_state?.CommittingCostScale??1;
        public float ResourceMaximum(string id)=>_state?.Maximum(id)??0;
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
        {if(RaidCombatAuthority.Replica)return;_state?.ClearOnDeath();GetComponent<AegisPaladin>()?.ClearTransient();GetComponent<CharacterKit>()?.Clear();GetComponent<CombatStatusEffects>()?.Clear();}
        public bool GainResource(string id,float amount) => !RaidCombatAuthority.Replica&&_state!=null&&_state.Gain(id,amount);
        public bool HasBuildEffect(string id)=>_state!=null&&_state.HasBuildEffect(id);
        public void RecordBuildEffect(string id,int stacks,float magnitude=0) { if(!RaidCombatAuthority.Replica)_state?.SetBuildEffect(id,stacks,magnitude); }
        public void ClearedStage(int stage) { if(!RaidCombatAuthority.Replica&&StageManager.Instance!=null)_state?.ClearedStage(StageManager.Instance.RunId,StageManager.Instance.ChapterId,stage); }
        public void ResetEncounter(CharacterReset reason)
        {
            BindLegacy();if(RaidCombatAuthority.Replica||_state==null)return;
            string departureRoute=_state.Snapshot.routeId;_state.Reset(reason);
            GetComponent<AegisPaladin>()?.ClearTransient();GetComponent<CharacterKit>()?.Clear();GetComponent<CombatStatusEffects>()?.Clear();
            if(reason==CharacterReset.NewRun&&departureRoute.Length>0)_state.ChooseRoute(departureRoute,true);
            if(reason==CharacterReset.NewRun&&StageManager.Instance!=null)_state.BeginRun(StageManager.Instance.RunId,StageManager.Instance.ChapterId);
            _player?.ClearResolvedCombatHits();_bot?.ClearResolvedCombatHits();GetComponent<AegisPaladin>()?.RecalculateStats();GetComponent<CharacterKit>()?.Recalculate();
        }
        public void InterruptCast() { if(!RaidCombatAuthority.Replica){_state?.CancelCast();GetComponent<CharacterKit>()?.CancelCasting();_player?.CancelSkillRush();GetComponent<AegisExtensions>()?.CancelChannel();GetComponent<CharacterKit>()?.CancelChannel();} }
        public bool Choose(string rule,string choice,out string reason,string rewardKey=null,bool automatic=false)
        {
            reason="루트/스킬 선택 조건을 충족하지 않습니다";
            if(RaidCombatAuthority.Replica||_state==null)return false;
            bool lobby=RaidCombatAuthority.Active?RaidNetworkSession.Instance.BattlePhase==RaidBattlePhase.Lobby:StageManager.Instance!=null&&StageManager.Instance.CanSelectInitialRole;
            bool reward=RaidCombatAuthority.Active?RaidNetworkSession.Instance.BattlePhase==RaidBattlePhase.Reward:StageManager.Instance?.Progress.Phase==RaidBattlePhase.Reward;
            string previousRoute=RouteId;
            bool ok=rule=="route"?_state.ChooseRoute(choice,lobby):reward&&_state.ChooseSkill(rule,choice,rewardKey,automatic);
            if(ok){reason="";if(rule=="route"&&previousRoute!=RouteId){GetComponent<AegisPaladin>()?.ApplyRouteChoice();GetComponent<CharacterKit>()?.Clear();}}return ok;
        }
        public void RequestChoice(string rule,string choice)
        {
            if(!RaidCombatAuthority.Local(this)||LocalCameraSettingsUI.IsOpen)return;
            if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(11,Definition.id,0,0,rule,choice);
            else if(!Choose(rule,choice,out string reason))Report(reason);
        }
        public void RequestSlot(CharacterSlot slot)
        {
            if(RaidCombatAuthority.InputBlocked(this))return;
            Transform target=TargetingManager.Instance?.CurrentTarget;
            if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(10,Definition.id,(int)slot,RaidCombatAuthority.Id(target));
            else if(!Execute(slot,target,out string reason))Report(reason);
        }
        public bool Execute(CharacterSlot slot,Transform target,out string reason)
        {
            reason="호스트만 전투를 확정할 수 있습니다";
            if(RaidCombatAuthority.Replica||!Available(slot,out reason))return false;
            var skill=_state.Selected(slot);
            var kit=GetComponent<CharacterKit>();
            if(kit?.Active==true){if(kit.TryReturn(slot))return true;reason=kit.Unavailable(skill,target);if(reason.Length>0)return false;}
            if(skill.handlerId=="aegis.extension")
            {reason=GetComponent<AegisExtensions>()?.Unavailable(skill,target)??"스킬 실행기가 아직 연결되지 않았습니다";if(reason.Length>0)return false;}
            if(GetComponent<AegisExtensions>()?.Channeling==true&&slot!=CharacterSlot.Movement&&slot!=CharacterSlot.Potion){reason="결계의 날개 수호 중입니다. 대쉬로 종료할 수 있습니다";return false;}
            if(slot==CharacterSlot.Identity&&GetComponent<AegisPaladin>()?.IdentityBlocked==true){reason="아이덴티티 효과가 이미 진행 중입니다";return false;}
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
            bool rushing=slot==CharacterSlot.Counter&&Definition.id==AegisPaladinData.JobId;
            if(rushing)
            {
                if(!_state.Validate(slot,Context(skill,target),out reason))return false;
                if(_player==null||!_player.PlanSkillRush(target,skill.movement.distance,out rushEnd)){reason="전방 돌진 경로 또는 이동 상태가 유효하지 않습니다";return false;}
            }
            bool accepted=_state.TryUse(slot,Context(skill,target),RaidCombatAuthority.Id(target),s=>Apply(s,target),out reason);
            if(accepted&&_state.Snapshot.castSkillId.Length>0){kit?.BeganCast(skill);_singleCastTarget=target;if(rushing)_player.BeginSkillRush(rushEnd,skill.castTime);}return accepted;
        }
        private bool Apply(CharacterSkillDefinition skill,Transform target)
        {
            if(!_handlers.TryGetValue(skill.handlerId,out var handler)||!handler(skill,target))return false;
            GetComponent<CommonPotion>()?.EndRevivalProtection();return true;
        }
        private CharacterUseContext Context(CharacterSkillDefinition skill,Transform target)
        {
            bool alive=_player!=null?!_player.IsDead:_bot!=null&&!_bot.isDead;
            bool can=!StageManager.CombatSuspended&&(_player==null||!_player.IsStunned&&!_player.IsBeingRescued&&!_player.IsBeingKnockedBack);
            bool valid=skill.target==SkillTargetRule.None||skill.target==SkillTargetRule.Self;
            if(skill.target==SkillTargetRule.LivingEnemy)valid=CombatRange.LivingEnemy(target);
            if(skill.target==SkillTargetRule.LivingAlly)valid=CombatRange.LivingPartyMember(target);
            if(skill.target==SkillTargetRule.DeadAlly)valid=target!=null&&PartyLife.OrderedMembers().Contains(target)&&target.GetComponent<PartyLife>()?.IsDead==true;
            if(GetComponent<CharacterKit>()?.CanAirAttack(skill,target)==true)valid=true;
            float distance=valid&&target!=null&&skill.target!=SkillTargetRule.Self&&skill.target!=SkillTargetRule.None?CombatRange.BoundaryDistance(transform,target):0;
            var kit=GetComponent<CharacterKit>();if(kit?.CanAirAttack(skill,target)==true)distance=0;
            return new CharacterUseContext(alive,can,valid,distance,_player!=null&&_player.IsMovingXZ&&!PreservesCastWhileDashing,EffectiveRange(skill),kit?.Active==true?kit.GcdScale(skill):GetComponent<AegisPaladin>()?.GcdScale(skill)??1,kit?.Active==true?kit.CastTime(skill):-1,kit?.Active==true?kit.CostScale(skill):1,kit?.Active==true&&kit.AllowsMovingCast(skill),kit?.Active==true?kit.CooldownScale(skill):1);
        }
        private void Update()
        {
            BindLegacy();if(_state==null)return;
            if(_player!=null&&RaidCombatAuthority.Local(this)&&!RaidCombatAuthority.InputBlocked(this))
                foreach(var slot in CharacterSlots.All)
                    if(slot==CharacterSlot.Counter||slot==CharacterSlot.Job2||slot>=CharacterSlot.Identity&&slot<=CharacterSlot.Ultimate)
                        if(LocalInputBindings.Pressed(CharacterSlots.Binding(slot)))RequestSlot(slot);
            if(_player!=null&&RaidCombatAuthority.Local(this))
            {
                bool held=LocalInputBindings.Held("Identity");
                if(_identityHeld&&!held&&Definition.id=="job.berserker"&&RouteId.EndsWith(".3"))
                {if(RaidCombatAuthority.Active)RaidNetworkSession.Instance.RequestCharacterAction(13,Definition.id,0);else GetComponent<CharacterKit>()?.CancelChannel();}
                _identityHeld=held;
            }
            if(RaidCombatAuthority.Replica)return;
            bool moved=(transform.position-_previous).sqrMagnitude>.000001f && !PreservesCastWhileDashing;_previous=transform.position;
            if(Definition.legacyRole>0){SyncLegacy();return;}
            if(StageManager.CombatSuspended)return;
            float cooldownBonus=CharacterKit.Bonus(transform,"cooldownRate");
            if(cooldownBonus>0)_player?.ReduceMovementRecharge(Time.deltaTime*cooldownBonus);
            if(cooldownBonus>0)_state.ReduceCooldowns(Definition.skills.Where(s=>s.id!=Selected(CharacterSlot.Ultimate)?.id&&s.id!=Selected(CharacterSlot.Identity)?.id).Select(s=>s.id),Time.deltaTime*cooldownBonus);
            // Reuse the existing party attack-speed buff without accelerating oGCD/debt/combo timers.
            float gcdRate=_player?.cardManager?.HasPartyAttackSpeedBuff==true?1.35f:1;
            _state.Tick(Time.deltaTime,moved,_player!=null?!_player.IsDead:_bot!=null&&!_bot.isDead,gcdRate);
            var snapshot=_state.Snapshot;
            if(snapshot.castSkillId.Length==0)_player?.CancelSkillRush();
            // Network movement advances at fixed server steps, while casts use the frame clock.
            // Never resolve a lunge hit until its swept movement has actually completed.
            if(snapshot.castSkillId.Length>0&&snapshot.castRemaining<=0&&_player?.IsSkillRushing!=true)
            {
                Transform target=RaidCombatAuthority.Active?RaidNetworkSession.Instance.CombatTarget(snapshot.castTarget):_singleCastTarget;
                var skill=Definition.Skill(snapshot.castSkillId);
                if(!_state.FinishCast(Context(skill,target),s=>Apply(s,target),out string reason))Report(reason);
            }
        }
        public bool StartMovement(DashMotion motion,DashSettings fallback,bool allowed,float x,float z,float facingX,float facingZ)
        {
            if(!Available(CharacterSlot.Movement,out _)||!allowed)return false;
            var kit=GetComponent<CharacterKit>();if(kit?.Active==true&&kit.DashBlocked)return false;
            if(kit?.TryReturn(CharacterSlot.Movement)==true)return false;
            var settings=MovementSettings(fallback);kit?.PlanMovement(settings,ref x,ref z);
            if(Definition.legacyRole>0||RaidCombatAuthority.Replica)return motion.TryStart(settings,true,x,z,facingX,facingZ);
            var skill=_state.Selected(CharacterSlot.Movement);
            _state.MirrorCharges(CharacterSlot.Movement,motion.MaximumCharges!=settings.maximumCharges&&motion.RechargeRemaining<=0?settings.maximumCharges:motion.Charges,(float)motion.RechargeRemaining,(float)motion.CooldownDuration);
            var context=new CharacterUseContext(_player!=null?!_player.IsDead:_bot!=null&&!_bot.isDead,allowed,true,0);
            Vector3 before=transform.position;bool started=_state.TryUse(CharacterSlot.Movement,context,0,s=>motion.TryStart(settings,true,x,z,facingX,facingZ),out _);if(started)kit?.MovementStarted(before);return started;
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
            return movement==null?fallback:new DashSettings{distance=movement.ResolveDistance(fallback.distance),duration=GetComponent<CharacterKit>()?.Active==true&&GetComponent<CharacterKit>().Job.index==10?.01f:movement.duration,cooldown=GetComponent<CharacterKit>()?.Active==true&&GetComponent<CharacterKit>().Job.index==10&&GetComponent<CharacterKit>().Overcome?8:skill.cooldown,maximumCharges=skill.maximumCharges,preserveCast=movement.preserveCast};
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
            if(_player!=null) { var dash=_player.CaptureDash(); if(dash.MaximumCharges==_state.Selected(CharacterSlot.Movement)?.maximumCharges) _state.MirrorCharges(CharacterSlot.Movement,dash.Charges,dash.Recharge,dash.CooldownTotal); }
        }
        public bool Restore(CharacterStateSnapshot snapshot)
        {
            if(!RaidCombatAuthority.Replica||snapshot==null)return false;
            var definition=CharacterCatalog.Find(snapshot.jobId);if(definition==null||!definition.playable)return false;
            if(definition.id==AegisPaladinData.JobId) AegisPaladin.Ensure(gameObject);
            if(CharacterRosterData.Find(definition.id)!=null)CharacterKit.Ensure(gameObject);
            if(_state==null||Definition.id!=definition.id){var candidate=new CharacterRuntimeState(definition);if(!candidate.Restore(snapshot))return false;_state=candidate;_role=definition.legacyRole;return true;}
            return _state.Restore(snapshot);
        }
    }
}
