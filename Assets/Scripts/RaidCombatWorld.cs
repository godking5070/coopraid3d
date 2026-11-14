using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        private readonly Dictionary<Transform, float> _combatThreat = new Dictionary<Transform, float>();
        private Vector3 _replicaPosition;
        private float _replicaYaw, _networkCastStarted, _replicaCastEnd;
        public void AddCombatThreat(Transform source, float amount, bool taunt = false)
        {
            if (RaidCombatAuthority.Replica || !CombatRange.LivingPartyMember(source)) return;
            float maximum = 0; if (taunt) foreach (float value in _combatThreat.Values) maximum = Mathf.Max(maximum, value);
            _combatThreat.TryGetValue(source, out float existing);
            float mult = source.GetComponent<PartyLife>()?.IsTank == true ? 5 : 1;
            _combatThreat[source] = taunt ? maximum + 1000 : existing + amount * mult;
        }
        public bool TransferCombatThreat(Transform source,Transform recipient,float fraction)
        {
            if(RaidCombatAuthority.Replica||currentHp<=0||source==recipient||!CombatRange.LivingPartyMember(source)||
                !CombatRange.LivingPartyMember(recipient)||recipient.GetComponent<PartyLife>()?.IsTank!=true||
                !CharacterDefinitionValidation.Finite(fraction)||fraction<=0||fraction>1)return false;
            if(!_combatThreat.TryGetValue(source,out float own)||!CharacterDefinitionValidation.Finite(own)||own<=0)return false;
            _combatThreat.TryGetValue(recipient,out float theirs);
            float moved=own*fraction;
            if(!CharacterDefinitionValidation.Finite(theirs)||theirs<0||!CharacterDefinitionValidation.Finite(theirs+moved))return false;
            _combatThreat[source]=own-moved;_combatThreat[recipient]=theirs+moved;
            Transform best=null;float maximum=-1;
            foreach(var pair in _combatThreat)if(CombatRange.LivingPartyMember(pair.Key)&&pair.Value>maximum){best=pair.Key;maximum=pair.Value;}
            if(best!=null)SetAggroTarget(best); // Existing cast targets and mechanic state are untouched.
            return true;
        }
        public float CombatThreatOf(Transform source)=>source!=null&&_combatThreat.TryGetValue(source,out float value)?value:0;
        public RaidCombatEnemy CaptureNetworkCombat() => new RaidCombatEnemy
        {
            brandEnd=GetComponent<CombatStatusEffects>()?.BrandEnd??0, stagger=GetComponent<CombatCounterTarget>()?.Stagger??0, counterWindowEnd=GetComponent<CombatCounterTarget>()?.WindowEnd??0,
            staggerLocked=StaggerLocked,staggerThreshold=StaggerThreshold,
            guardianPattern=(int)GuardianPatternNow, guardianStep=(int)GuardianStepNow, guardianStarted=_guardianStarted, guardianEnds=_guardianEnds, guardianStagger=_guardianStagger,
            guardianPhase=_hardPhase, guardianClashPhase=_clashPhase, guardianClashTank=RaidCombatAuthority.Id(_clashTank), guardianTransformed=_guardianHard.Transformed,
            guardianClashSucceeded=_clashSucceeded,
            guardianLureTarget=RaidCombatAuthority.Id(_hardLureTarget),
            guardianFollowup=_guardianHardFollowup?1:0, guardianSideSign=Vector3.Dot(_guardianSide,transform.right)<0?-1:1,
            guardianImpactSerial=_guardianImpactSerial, guardianLockSerial=_guardianLockSerial,
            floraCounterOpen=FloraEncounter && _floraCounterOpen, floraTransformed=_floraTransformed,
            floraTransforming=FloraHard && _floraStep==FloraStep.Transformation,
            campPattern=CampanellaEncounter?(int)_campPattern:0,campStep=CampanellaEncounter?(int)_campStep:0,
            campPlateStep=_campPlateStep,campSuccessMask=_campSuccessMask,campFailureMask=_campFailureMask,campMissingMask=_campMissingMask,campStagger=_campStagger,
            campCounterOpen=_campCounterOpen,campStoryUsed=_campStoryUsed,
            campHardTransformed=_campHardTransformed,campHardFirstLockDone=_campHardFirstLockDone,
            campHardFinalDone=_campHardFinalDone,campHardTank=RaidCombatAuthority.Id(_campHardTank),campHardEchoPlate=_campHardEchoPlate,
            campAssignees=CaptureCampanellaAssignees(),campPlateX=CaptureCampanellaPlateAxis(true),campPlateZ=CaptureCampanellaPlateAxis(false),
            marPattern=MarionetteEncounter?(int)_marPattern:0,marStep=MarionetteEncounter?(int)_marStep:0,
            marTarget=MarionetteEncounter?RaidCombatAuthority.Id(_marTarget):0,marStagger=_marStagger,
            marCounterOpen=_marCounterOpen,marClimaxUsed=_marClimaxUsed,marStoryUsed=_marStoryUsed,marFirewall=_marFirewallShown,marRight=_marRight,
            marHardTransformed=_marHardTransformed,marHardClimaxPreparing=_marHardClimaxPreparing,
            marHardMemoryPattern=(int)_marHardMemoryPattern,marHardMemoryRight=_marHardMemoryRight,
            marHardMemoryYaw=Quaternion.LookRotation(_marHardMemoryForward.sqrMagnitude>.01f?_marHardMemoryForward:Vector3.forward).eulerAngles.y,
            marHardDollX=_marHardDollPoint.x,marHardDollZ=_marHardDollPoint.z,
            marHardRecoilX=_marHardRecoilPoint.x,marHardRecoilZ=_marHardRecoilPoint.z,
            marHardStringMask=_marHardStringMask,marHardCrossingX=_marHardCrossingGround!=null?_marHardCrossingGround.transform.position.x:0,
            justPattern=JustitiaEncounter?(int)_justPattern:0,justStep=JustitiaEncounter?(int)_justStep:0,
            justMassIds=JustitiaEncounter?CaptureJustitiaMassIds():null,justMassValues=JustitiaEncounter?_justMass.ToArray():null,
            justMarkX=JustitiaEncounter?_justMarkPoints.ConvertAll(p=>p.x).ToArray():null,
            justMarkZ=JustitiaEncounter?_justMarkPoints.ConvertAll(p=>p.z).ToArray():null,
            justStagger=_justStagger,justVerdictUsed=_justVerdictUsed,justStoryUsed=_justStoryUsed,
            justCounterOpen=_justCounterOpen,justRight=_justRight,
            justHardTransformed=_justHardTransformed,justHardIntroDone=_justHardIntroDone,
            justHardClimaxStarted=_justHardClimaxStarted,justHardWeightActive=_justHardWeightActive,
            justHardImpactShown=_justHardImpactShown,justHardWeightArrived=_justHardWeightArrived,
            justHardBalanceKind=(int)_justHardBalanceKind,justHardRound=_justHardBalanceRound,
            justHardWeightSide=_justHardWeightSide,justHardFollowupPattern=(int)_justHardFollowupPattern,
            justHardNext=(int)_justHardNext,
            justHardNextPulse=float.IsInfinity(_justHardNextPulse)?0:_justHardNextPulse,
            justHardResidueX=_justHardResiduePoints.ConvertAll(p=>p.x).ToArray(),
            justHardResidueZ=_justHardResiduePoints.ConvertAll(p=>p.z).ToArray(),
            janusPattern=JanusEncounter?(int)_janusPattern:0,janusStep=JanusEncounter?(int)_janusStep:0,
            janusPostActors=JanusEncounter?CaptureJanusPostActors():null,janusPostOverlap=_janusPostOverlap,
            janusAlphaCounter=_janusAlphaCounter,janusOmegaCounter=_janusOmegaCounter,
            janusPostUsed=_janusPostUsed,janusCounterUnlocked=_janusCounterUnlocked,
            janusStoryUsed=_janusStoryUsed,janusLineLocked=_janusLineLocked,janusChainBroken=_janusChainBroken,
            janusHardTransformed=_janusHardTransformed,janusHardPostPhase=(int)_janusHardPostPhase,
            janusHardHoldingMask=CaptureJanusHardHoldingMask(),
            id = -1, stage = currentStageNumber, phase = (int)currentPhase, state = (int)fsmState,
            target = RaidCombatAuthority.Id(aggroTarget), name = name, x = transform.position.x, y = transform.position.y, z = transform.position.z,
            yaw = transform.eulerAngles.y, hp = currentHp, maxHp = maxHp, radius = CombatRadius, dead = currentHp <= 0,
            casting = IsCasting, castName = _currentCastSkillName, castRemaining = _castRemainingTime, castTotal = _castTotalDuration, groggy = _groggyRemainingTime,
            alert = mechanicAlertBannerText != null ? mechanicAlertBannerText.text : ""
        };
        public void ApplyNetworkCombat(RaidCombatEnemy state, float sampledTime)
        {
            if (state == null) return;
            if (currentStageNumber != state.stage) SetupBossForStage(state.stage);
            CombatCounterTarget.Ensure(transform).Restore(state.stagger,state.counterWindowEnd);
            _replicaStaggerLocked=state.staggerLocked;
            _replicaStaggerThreshold=CharacterDefinitionValidation.Finite(state.staggerThreshold)&&state.staggerThreshold>0?state.staggerThreshold:1000;
            CombatStatusEffects.Ensure(gameObject).RestoreBrand(state.brandEnd);
            _guardianReplicaPattern=(GuardianPattern)state.guardianPattern; _guardianReplicaStep=(GuardianStep)state.guardianStep;
            if(CampanellaEncounter)ApplyCampanellaReplica(state);
            if(MarionetteEncounter)ApplyMarionetteReplica(state);
            if(JustitiaEncounter)ApplyJustitiaReplica(state);
            if(JanusEncounter)ApplyJanusReplica(state);
            if (FloraEncounter)
            {
                _floraReplicaTransforming=FloraHard && state.floraTransforming;
                if(_floraReplicaTransforming && _floraTransformationInstance==null && !_floraLocalSkipped && !_floraPresentationUnavailable)
                    PlayFloraTransformationPresentation(Mathf.Max(0,state.castTotal-state.castRemaining));
                if(!_floraReplicaTransforming && !state.floraTransformed)
                { StopFloraTransformation();_floraLocalSkipped=false; }
                if(FloraHard && state.floraTransformed && !_floraTransformed) FinishFloraTransformation();
                if(FloraHard && !state.floraTransformed && _floraTransformed) ResetFloraReplicaTransformation();
                if(state.floraCounterOpen && _floraCounterIcon==null) _floraCounterIcon=FloraLabel("⚔ COUNTER",new Color(1,.78f,.15f));
                if(!state.floraCounterOpen && _floraCounterIcon!=null){Destroy(_floraCounterIcon.gameObject);_floraCounterIcon=null;}
                if(_floraCounterIcon!=null)_floraCounterIcon.transform.position=new Vector3(state.x,state.y+4.5f,state.z);
            }
            _guardianStarted=state.guardianStarted; _guardianEnds=state.guardianEnds; _guardianStagger=state.guardianStagger;
            _hardPhase=state.guardianPhase; _guardianHardFollowup=state.guardianFollowup!=0;
            _hardLureTarget=RaidCombatAuthority.Resolve(state.guardianLureTarget);
            _clashPhase=state.guardianClashPhase; _clashTank=RaidCombatAuthority.Resolve(state.guardianClashTank);
            _clashSucceeded=state.guardianClashSucceeded;
            if (GuardianHard && !state.guardianTransformed && _guardianHard.Transformed)
            { ClearHardCorruption(); _guardianHard.Reset(); }
            if (GuardianHard && state.guardianTransformed && !_guardianHard.Transformed)
            { _guardianHard.FinishTransformation(); BuildHardCorruption(); currentBossTitle="STAGE 1 : 거대 가고일 · 하드"; }
            _guardianSide=(Quaternion.Euler(0,state.yaw,0)*Vector3.right)*(state.guardianSideSign<0?-1:1);
            if (_guardianReplicaImpactInitialized && state.guardianImpactSerial > _guardianImpactSerial) PlayHardImpactCue();
            _guardianImpactSerial = state.guardianImpactSerial;
            _guardianReplicaImpactInitialized = true;
            if (_guardianReplicaLockInitialized && state.guardianLockSerial > _guardianLockSerial) PlayGuardianLockCue();
            _guardianLockSerial = state.guardianLockSerial;
            _guardianReplicaLockInitialized = true;
            bool collapse = state.dead && !IsBossDefeatedCollapse;
            currentHp = state.hp; maxHp = state.maxHp; currentPhase = (BossPhase)state.phase; fsmState = (BossFSMState)state.state;
            _replicaPosition = new Vector3(state.x, state.y, state.z); _replicaYaw = state.yaw;
            aggroTarget = RaidCombatAuthority.Resolve(state.target); _currentCastSkillName = state.castName;
            if (state.stage == 1) stage1CombatRadius = state.radius; else if (state.stage == 2) stage2CombatRadius = state.radius; else stage3CombatRadius = state.radius;
            _replicaCastEnd = sampledTime + state.castRemaining;
            _castRemainingTime = state.castRemaining; _castTotalDuration = state.castTotal; _groggyRemainingTime = state.groggy;
            if (mechanicAlertBannerText != null) mechanicAlertBannerText.text = state.alert;
            if (collapse) PlayBossCollapseAnimation();
        }
        private void UpdateReplicaBoss()
        {
            if(FloraHard)TickFloraReplicaTransformationInput();
            if(MarionetteHard)TickMarionetteHardReplicaInput();
            if(JustitiaHard)TickJustitiaHardReplicaInput();
            if(JanusHard)TickJanusHardReplicaInput();
            if (IsCasting) _castRemainingTime = Mathf.Max(0, _replicaCastEnd - RaidCombatAuthority.ServerTime);
            if(CampanellaEncounter)_campRemaining=_castRemainingTime;
            if(MarionetteEncounter)_marRemaining=_castRemainingTime;
            if(JustitiaEncounter)_justRemaining=_castRemainingTime;
            if(JanusEncounter)_janusRemaining=_castRemainingTime;
            if(CampanellaEncounter)TickCampanellaPose();
            if(CampanellaHard)TickCampanellaHardReplicaInput();
            float t = 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, _replicaPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, _replicaYaw, 0), t);
            TickHardReplicaTransformation();
        }
        public void CapturePartyMechanics(List<RaidCombatMechanic> output)
        {
            if(JustitiaEncounter)foreach(var marker in _justMarkers)if(marker!=null && marker.followTarget!=null)
                output.Add(new RaidCombatMechanic{id=marker.GetInstanceID(),kind=_justPattern==JustitiaPattern.SharedGuilt?4:3,
                    target=RaidCombatAuthority.Id(marker.followTarget),outer=marker.radius,targetMarker=marker.isTargetMarker,
                    start=RaidCombatAuthority.ServerTime,end=RaidCombatAuthority.ServerTime+.4f});
            if(MarionetteEncounter && _marMarker!=null && _marMarker.followTarget!=null)
                output.Add(new RaidCombatMechanic{id=_marMarker.GetInstanceID(),kind=_marPattern==MarionettePattern.AudienceApplause?4:3,
                    target=RaidCombatAuthority.Id(_marMarker.followTarget),outer=_marMarker.radius,targetMarker=_marMarker.isTargetMarker,
                    start=RaidCombatAuthority.ServerTime,end=RaidCombatAuthority.ServerTime+.4f});
            if(MarionetteEncounter)foreach(var marker in _marLightMarkers)if(marker!=null && marker.followTarget!=null)
                output.Add(new RaidCombatMechanic{id=marker.GetInstanceID(),kind=3,target=RaidCombatAuthority.Id(marker.followTarget),
                    outer=marker.radius,targetMarker=true,start=RaidCombatAuthority.ServerTime,end=RaidCombatAuthority.ServerTime+.4f});
            if(CampanellaEncounter && _campMarker!=null && _campMarker.followTarget!=null)
                output.Add(new RaidCombatMechanic{id=_campMarker.GetInstanceID(),kind=_campPattern==CampanellaPattern.ResonanceShare?4:3,
                    target=RaidCombatAuthority.Id(_campMarker.followTarget),outer=_campMarker.radius,targetMarker=_campMarker.isTargetMarker,
                    start=RaidCombatAuthority.ServerTime,end=RaidCombatAuthority.ServerTime+.4f});
            if (FloraEncounter)
            {
                if (_floraMarker != null && _floraMarker.followTarget != null)
                    output.Add(new RaidCombatMechanic { id = _floraMarker.GetInstanceID(), kind = _floraPattern == FloraPattern.MemorialBloom ? 4 : 3,
                        target = RaidCombatAuthority.Id(_floraMarker.followTarget), outer = _floraMarker.radius, targetMarker = _floraMarker.isTargetMarker,
                        start = RaidCombatAuthority.ServerTime, end = RaidCombatAuthority.ServerTime + .4f });
                foreach (var pair in _floraPairs) if (!pair.resolved && pair.first != null && pair.second != null)
                    output.Add(new RaidCombatMechanic { id = -100-pair.id, kind = 10, target = RaidCombatAuthority.Id(pair.second),
                        x = pair.first.position.x, z = pair.first.position.z, start = RaidCombatAuthority.ServerTime, end = RaidCombatAuthority.ServerTime + .4f });
                if (_floraStep == FloraStep.Garden && _floraSeedElapsed >= FloraTuning.seedArrivalSeconds)
                    for (int i=0;i<2;i++) if (!_floraSeedOpen[i])
                        output.Add(new RaidCombatMechanic { id = -110-i, kind = 9, x = _floraSeedPoint[i].x, z = _floraSeedPoint[i].z,
                            start = RaidCombatAuthority.ServerTime, end = RaidCombatAuthority.ServerTime + .4f });
                if (_floraStep == FloraStep.Garden && _floraSeedElapsed < FloraTuning.seedArrivalSeconds)
                    for(int i=0;i<2;i++) if(_floraSeedProjectile[i]!=null)
                        output.Add(new RaidCombatMechanic { id = -120-i, kind = 11,
                            x = _floraSeedProjectile[i].transform.position.x, y = _floraSeedProjectile[i].transform.position.y, z = _floraSeedProjectile[i].transform.position.z,
                            start = RaidCombatAuthority.ServerTime, end = RaidCombatAuthority.ServerTime + .4f });
            }
            foreach (var marker in _activePartyMarkers)
            {
                if (marker == null || marker.followTarget == null) continue;
                bool stack = marker == _activeStackMarker;
                output.Add(new RaidCombatMechanic { id = marker.GetInstanceID(), kind = stack ? 4 : 3, target = RaidCombatAuthority.Id(marker.followTarget),
                    x = marker.followTarget.position.x, z = marker.followTarget.position.z, outer = marker.radius, targetMarker = marker.isTargetMarker,
                    start = _networkCastStarted, resolve = _networkCastStarted + _castTotalDuration, end = _networkCastStarted + _castTotalDuration });
            }
            if (_isTetherActive)
            {
                Transform end = _isTetherInterceptedByPlayer ? (_tetherInterceptingBot != null ? _tetherInterceptingBot.transform : _tetherInterceptingPlayer) : _tetherTankTarget;
                if (end != null) output.Add(new RaidCombatMechanic { id = -2, kind = 5, target = RaidCombatAuthority.Id(end), x = transform.position.x, z = transform.position.z,
                    start = _networkCastStarted, resolve = _networkCastStarted + _castTotalDuration, end = _networkCastStarted + _castTotalDuration, intercepted = _isTetherInterceptedByPlayer });
            }
        }
    }
    public partial class BossAddMinion
    {
        private Vector3 _replicaPosition;
        private float _replicaYaw;
        public RaidCombatEnemy CaptureNetworkCombat(int id) => new RaidCombatEnemy { id = id, brandEnd=GetComponent<CombatStatusEffects>()?.BrandEnd??0, stagger=GetComponent<CombatCounterTarget>()?.Stagger??0,counterWindowEnd=GetComponent<CombatCounterTarget>()?.WindowEnd??0,type = (int)addType, name = addDisplayName,
            x = transform.position.x, y = transform.position.y, z = transform.position.z, yaw = transform.eulerAngles.y, hp = currentHp, maxHp = maxHp, radius = combatRadius,
            dead = isDead, casting = _isCastingHeavyStrike, castName = _currentCastSkillName, castRemaining = 1 - _castProgress, castTotal = 1, target = RaidCombatAuthority.Id(aggroTarget) };
        public void ApplyNetworkCombat(RaidCombatEnemy state)
        {
            CombatCounterTarget.Ensure(transform).Restore(state.stagger,state.counterWindowEnd);
            CombatStatusEffects.Ensure(gameObject).RestoreBrand(state.brandEnd);
            currentHp = state.hp; maxHp = state.maxHp; isDead = state.dead; _isCastingHeavyStrike = state.casting; _currentCastSkillName = state.castName;
            _castProgress = 1 - state.castRemaining; aggroTarget = RaidCombatAuthority.Resolve(state.target);
            _replicaPosition = new Vector3(state.x, state.y, state.z); _replicaYaw = state.yaw;
        }
        private void UpdateReplicaAdd()
        {
            float t = 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, _replicaPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, _replicaYaw, 0), t);
            UpdateOverheadHpBar();
            if(addType==AddMinionType.MarionetteString){UpdateMarionetteStringLine();return;}
            if (_tetherOrAttackLine != null)
            {
                _tetherOrAttackLine.enabled = !isDead && aggroTarget != null && (addType == AddMinionType.PowerSupplier_BossTether || IsCastingSkill);
                if (_tetherOrAttackLine.enabled) { _tetherOrAttackLine.SetPosition(0, transform.position); _tetherOrAttackLine.SetPosition(1, aggroTarget.position + Vector3.up); }
            }
        }
    }
    public partial class AoETelegraph
    {
        private float _networkStart;
        private bool _networkReplica;
        public RaidCombatMechanic CaptureNetworkMechanic() => new RaidCombatMechanic { id = GetInstanceID(), kind = (int)telegraphType,
            x = transform.position.x, z = transform.position.z, yaw = transform.eulerAngles.y, inner = innerRadius, outer = outerRadius, angle = coneAngleDeg,
            guideOnly = guideOnly, guideLocked = guideLocked, tankGuide = tankGuide, thornStyle = thornStyle,
            start = _networkStart, resolve = _networkStart + _duration, end = _networkStart + _duration };
        public void ApplyNetworkTiming(RaidCombatMechanic state) { _networkReplica = true; _networkStart = state.start; _duration = Mathf.Max(.01f, state.resolve - state.start); }
    }
    public partial class HealingSanctuaryZone
    {
        private float _networkStart;
        public RaidCombatMechanic CaptureNetworkMechanic() => new RaidCombatMechanic { id = GetInstanceID(), kind = 6,
            x = transform.position.x, z = transform.position.z, outer = radius, start = _networkStart, resolve = _networkStart + duration, end = _networkStart + duration };
        public void CancelIfSource(Transform source) { if (_source == source) { gameObject.SetActive(false); Destroy(gameObject); } }
    }
    public partial class PartyBotAI
    {
        public void CaptureNetworkCombat(RaidCombatActor state)
        {
            state.hp = currentHp; state.maxHp = maxHp; state.shield = currentShield; state.dead = isDead; state.defense = _tankDefensiveTimer;
            state.speed = moveSpeed; state.target = RaidCombatAuthority.Id(CurrentActionTarget);
            // Native bots keep their existing AI cadence; expose its actual timer in common state.
            float interval = role == PartyBotRole.Tank ? tankAttackInterval : role == PartyBotRole.Healer ? healCheckInterval : role == PartyBotRole.MeleeDPS ? meleeAttackInterval : rangedFireInterval;
            state.gcdTotal = interval; state.gcd = Mathf.Max(0, interval - _actionTimer);
            if (role == PartyBotRole.Healer) { state.cd4 = _rescueCooldownTimer; state.cd4Total = rescueCooldown; }
        }
    }
    public sealed partial class BattleResurrection
    {
        public void CancelIfTarget(Transform target) { if (_target != null && _target.transform == target) Cancel(); }
    }
    public sealed partial class RaidNetworkSession
    {
        private readonly Dictionary<int, RaidReplicaProjectile> _replicaProjectiles = new Dictionary<int, RaidReplicaProjectile>();
        private RaidCombatMechanic[] CaptureMechanics()
        {
            var result = new List<RaidCombatMechanic>();
            foreach (var telegraph in FindObjectsByType<AoETelegraph>(FindObjectsSortMode.None)) result.Add(telegraph.CaptureNetworkMechanic());
            foreach (var zone in FindObjectsByType<HealingSanctuaryZone>(FindObjectsSortMode.None)) result.Add(zone.CaptureNetworkMechanic());
            foreach (var projectile in FindObjectsByType<RangedBotProjectile>(FindObjectsSortMode.None)) result.Add(projectile.CaptureNetworkMechanic());
            StageManager.Instance.boss.CapturePartyMechanics(result); return result.ToArray();
        }
        private void ApplyNetworkAdds(RaidCombatEnemy[] states)
        {
            var live = new HashSet<int>(); var spawner = StageManager.Instance.boss.addSpawner;
            foreach (var state in states)
            {
                live.Add(state.id);
                if (!_combatAdds.TryGetValue(state.id, out var add) || add == null)
                {
                    var obj = new GameObject("NetworkAdd_" + state.id); add = obj.AddComponent<BossAddMinion>();
                    add.InitializeAdd((AddMinionType)state.type, StageManager.Instance.boss, spawner, new Vector3(state.x, state.y, state.z), StageManager.Instance.boss.EncounterTemplate);
                    _combatAdds[state.id] = add; spawner.activeAdds.Add(add);
                }
                add.ApplyNetworkCombat(state);
            }
            foreach (int id in new List<int>(_combatAdds.Keys)) if (!live.Contains(id))
            {
                var add = _combatAdds[id]; if (add != null) { TargetingManager.Instance?.ForgetRemovedTarget(add.transform); spawner.activeAdds.Remove(add); add.gameObject.SetActive(false); Destroy(add.gameObject); }
                _combatAdds.Remove(id);
            }
        }
        private void ApplyReplicaMechanics(RaidCombatMechanic[] mechanics)
        {
            var live = new HashSet<int>();
            foreach (var state in mechanics)
            {
                if (ServerTime >= state.end + .15f) continue;
                live.Add(state.id); Transform target = CombatTarget(state.target);
                if (state.kind <= 2 || state.kind == 6 || state.kind == 8)
                {
                    if (!_replicaTelegraphs.TryGetValue(state.id, out var aoe) || aoe == null)
                    {
                        var obj = new GameObject("NetworkTelegraph_" + state.id); obj.transform.position = new Vector3(state.x, 0, state.z); obj.transform.rotation = Quaternion.Euler(0, state.yaw, 0);
                        aoe = obj.AddComponent<AoETelegraph>();
                        if (state.thornStyle) aoe.SetThornStyle();
                        if (state.kind == 6) { aoe.baseFillColor = new Color(.2f, 1, .4f, .15f); aoe.borderColor = Color.green; aoe.progressFillColor = new Color(.2f, 1, .4f, .3f); }
                        aoe.Setup(state.kind == 6 ? AoETelegraphType.Circle_Radius6 : (AoETelegraphType)state.kind, state.end - state.start, state.inner, state.outer, state.angle);
                        if (state.tankGuide) aoe.SetTankGuideStyle();
                        else if (state.guideOnly) aoe.SetGuideStyle(state.guideLocked);
                        _replicaTelegraphs[state.id] = aoe;
                    }
                    aoe.transform.SetPositionAndRotation(new Vector3(state.x, 0, state.z), Quaternion.Euler(0, state.yaw, 0));
                    aoe.ApplyNetworkTiming(state);
                }
                else if (state.kind == 7)
                {
                    if (!_replicaProjectiles.TryGetValue(state.id, out var projectile) || projectile == null)
                        _replicaProjectiles[state.id] = projectile = RaidReplicaProjectile.Create();
                    projectile.Apply(state, target, ServerTime);
                }
                else if ((state.kind == 3 || state.kind == 4) && target != null)
                {
                    if (!_replicaMarkers.TryGetValue(state.id, out var marker) || marker == null)
                        _replicaMarkers[state.id] = state.kind == 4 ? AttachedPartyMechanicMarker.SpawnStackMarker(target, state.outer) :
                            state.targetMarker ? AttachedPartyMechanicMarker.SpawnTargetMarker(target, state.outer) : AttachedPartyMechanicMarker.SpawnSpreadMarker(target, state.outer);
                    _replicaMarkers[state.id].followTarget = target;
                }
                else if (state.kind == 9 || state.kind == 11)
                {
                    if (!_replicaFloraSeeds.TryGetValue(state.id,out var seed) || seed == null)
                    {
                        var go=RaidPrimitive.Create(PrimitiveType.Sphere);go.name="Network purification seed";
                        go.transform.localScale=Vector3.one*.8f;go.GetComponent<Collider>().enabled=false;
                        var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(1,.8f,.1f));block.SetColor("_Color",new Color(1,.8f,.1f));go.GetComponent<Renderer>().SetPropertyBlock(block);
                        if(state.kind==9)
                        {
                            var label=new GameObject("Purification seed icon").AddComponent<TextMesh>();
                            label.text="✿ 정화 씨앗";label.fontSize=40;label.anchor=TextAnchor.MiddleCenter;
                            label.alignment=TextAlignment.Center;label.color=new Color(1,.94f,.55f);
                            label.transform.SetParent(go.transform,false);label.transform.localScale=Vector3.one*.09f;
                            label.transform.localPosition=Vector3.up*1.3f;
                        }
                        _replicaFloraSeeds[state.id]=seed=go.transform;
                    }
                    seed.position=new Vector3(state.x,state.kind==11?state.y:.6f,state.z);
                }
                else if ((state.kind == 5 || state.kind == 10) && target != null)
                {
                    if (!_replicaLines.TryGetValue(state.id, out var line) || line == null)
                    {
                        line = new GameObject("NetworkTether").AddComponent<LineRenderer>(); line.sharedMaterial = new Material(Shader.Find("Sprites/Default")); line.positionCount = 2;
                        line.startWidth = line.endWidth = .16f; _replicaLines[state.id] = line;
                    }
                    line.startColor = line.endColor = state.kind==10 ? new Color(.25f,.85f,1) : state.intercepted ? Color.green : Color.red;
                    line.SetPosition(0, new Vector3(state.x, 1.15f, state.z)); line.SetPosition(1, target.position);
                }
            }
            RemoveReplicaObjects(_replicaTelegraphs, live); RemoveReplicaObjects(_replicaMarkers, live); RemoveReplicaObjects(_replicaLines, live); RemoveReplicaObjects(_replicaProjectiles, live); RemoveReplicaObjects(_replicaFloraSeeds, live);
        }
        private static void RemoveReplicaObjects<T>(Dictionary<int, T> objects, HashSet<int> live) where T : Component
        {
            foreach (int id in new List<int>(objects.Keys)) if (!live.Contains(id)) { if (objects[id] != null) { if (objects[id] is LineRenderer line) Destroy(line.sharedMaterial); Destroy(objects[id].gameObject); } objects.Remove(id); }
        }
        private void ClearReplicaMechanics()
        { var empty = new HashSet<int>(); RemoveReplicaObjects(_replicaTelegraphs, empty); RemoveReplicaObjects(_replicaMarkers, empty); RemoveReplicaObjects(_replicaLines, empty); RemoveReplicaObjects(_replicaProjectiles, empty); RemoveReplicaObjects(_replicaFloraSeeds, empty); }
    }
}
