using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        [Header("Stage 3 normal: Campanella (initial tuning)")]
        public CampanellaNormalSettings campanella=new CampanellaNormalSettings();
        [Header("Stage 3 hard: Campanella (initial tuning)")]
        public CampanellaHardSettings campanellaHard=new CampanellaHardSettings();
        public bool CampanellaEncounter => currentStageNumber==3;
        public bool CampanellaHard => CampanellaEncounter && CampaignRuntime.Instance?.DifficultyId==CampaignCatalog.Hard;
        public bool CampanellaNormal => CampanellaEncounter && !CampanellaHard;
        private CampanellaNormalSettings CampTuning => CampanellaHard ? campanellaHard.combat : campanella;
        private CampanellaPattern _campPattern;
        private CampanellaStep _campStep;
        private float _campRemaining,_campRest,_campStagger;
        private bool _campGeneralStagger;
        private bool CampanellaRequiredStaggerPending => CampanellaHard &&
            (CampanellaNormalRules.ForceBellDue(_campForceUsed,HpRatio,campanellaHard.transformationThreshold) ||
             CampanellaHardRules.TransformDue(_campHardForcedResolved,_campHardTransformed,currentHp>0) ||
             _campHardTransformed && !_campHardFirstLockDone ||
             CampanellaHardRules.FinalDue(_campHardTransformed,_campHardFirstLockDone,_campHardFinalDone,HpRatio,campanellaHard.finalThreshold));
        public bool CampanellaStaggerLocked => !CampanellaEncounter || currentHp<=0 || StageManager.CombatSuspended ||
            _campStep==CampanellaStep.Groggy || CampanellaRequiredStaggerPending || _campHardFinalActive ||
            _campStep==CampanellaStep.Transformation || _campStep==CampanellaStep.LockPrepare ||
            _campStep==CampanellaStep.LockCounter || _campStep==CampanellaStep.LockHold ||
            _campStep==CampanellaStep.LockOpening || _campStep==CampanellaStep.LockRecovery || _campStep==CampanellaStep.FinalReposition ||
            _campStep!=CampanellaStep.Rest && (_campPattern==CampanellaPattern.SilentTuning || _campPattern==CampanellaPattern.ForcedBell);
        public bool TryCampanellaStaggerGroggy(float seconds)
        {
            if(RaidCombatAuthority.Replica||CampanellaStaggerLocked||float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return false;
            EndCampanella(true,seconds);
            _campGeneralStagger=true;
            _currentCastSkillName="무력화 성공";
            SetMechanicAlert(_currentCastSkillName,Color.yellow,seconds);
            return true;
        }
        private int _campNext=1,_campPlateStep,_campSuccessMask,_campFailureMask,_campMissingMask;
        private bool _campForceUsed,_campStoryUsed,_campCounterOpen,_campWeightLocked,_campWrongNotified,_campSpecialRecoveryPending;
        private Transform _campTarget;
        private Vector3 _campTargetPoint,_campOrigin,_campForward;
        private bool _campRight;
        private readonly Transform[] _campAssignees=new Transform[4];
        private readonly TextMesh[] _campNumberLabels=new TextMesh[4];
        private readonly List<AoETelegraph> _campGround=new List<AoETelegraph>();
        private AttachedPartyMechanicMarker _campMarker;
        private TextMesh _campCounterIcon,_campProgressLabel;
        private GameObject _campModel;
        private GameObject _campPose;
        private Renderer[] _campHiddenBody;
        private AudioSource _campAudio;
        private Vector3[] _campReplicaPlatePoints;
        private bool _campReplicaPoseStarted;
        private bool _campReplicaStoryShown;
        private bool _campReplicaForceAnnounced;
        private bool _campHardRestProtected;
        private float _campBaseAutoAttackInterval;
        private bool _campAutoAttackCaptured;

        private void ResetCampanella()
        {
            ClearCampanella();
            campanella=campanella??new CampanellaNormalSettings();
            campanellaHard=campanellaHard??new CampanellaHardSettings();
            if(campanellaHard.lockGroggySeconds<=0||campanellaHard.lockGroggySeconds==6)campanellaHard.lockGroggySeconds=2;
            campanellaHard.combat=campanellaHard.combat??new CampanellaNormalSettings();
            _campNext=1;_campForceUsed=!CampanellaHard;_campStoryUsed=_campSpecialRecoveryPending=false;
            _campRest=CampanellaHard?CampanellaHardRules.Tempo(CampTuning.patternInterval,campanellaHard.attackTempo):
                CampTuning.patternInterval;
            if(!_campAutoAttackCaptured){_campBaseAutoAttackInterval=autoAttackInterval;_campAutoAttackCaptured=true;}
            autoAttackInterval=CampanellaHard?CampanellaHardRules.Tempo(_campBaseAutoAttackInterval,campanellaHard.attackTempo):_campBaseAutoAttackInterval;
            if(!CampanellaEncounter)return;
            ResetCampanellaHard();
            currentBossTitle=CampanellaHard?"침묵의 종루 · 캄파넬라 · 하드":"제3외곽 수호자 : 캄파넬라";
            maxHp=RaidBossHealthTuning.Resolve(CampanellaHard && campanellaHard.healthOverride>0?campanellaHard.healthOverride:campanella.health,CampanellaHard);currentHp=maxHp;
            arenaBoundaryRadius=Mathf.Max(12,CampTuning.arenaRadius);
            autoAttackDamageToTank=110;
            _stagePhase1BodyColor=new Color(.53f,.39f,.18f);
            SetBossBodyColor(_stagePhase1BodyColor);
            if(CampTuning.bossVisualPrefab!=null)
            {
                _campModel=Instantiate(CampTuning.bossVisualPrefab,transform);
                _campModel.name="Campanella authored visual";
                foreach(var collider in _campModel.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            else BuildTemporaryCampanella();
            var body=GetBossBodyTransform();
            if(body!=null){_campHiddenBody=body.GetComponentsInChildren<Renderer>();foreach(var renderer in _campHiddenBody)renderer.enabled=false;}
            var animator=_campModel.GetComponentInChildren<Animator>();
            if(animator!=null && CampTuning.bossAnimatorController!=null)animator.runtimeAnimatorController=CampTuning.bossAnimatorController;
        }

        private void BuildTemporaryCampanella()
        {
            _campModel=new GameObject("Temporary silenced bronze bell · replace in campanella");
            _campModel.transform.SetParent(transform,false);
            CampPart(PrimitiveType.Cylinder,"Fallen bronze bell",new Vector3(0,1.5f,0),new Vector3(3.2f,1.7f,3.2f),new Color(.54f,.37f,.16f));
            CampPart(PrimitiveType.Sphere,"Muted resonator",new Vector3(0,2.7f,0),new Vector3(2.8f,1.3f,2.8f),new Color(.3f,.2f,.11f));
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI/4;
                var leg=CampPart(PrimitiveType.Capsule,"Clockwork spider leg "+(i+1),
                    new Vector3(Mathf.Cos(angle)*2,1.15f,Mathf.Sin(angle)*2),new Vector3(.22f,2.6f,.22f),new Color(.36f,.29f,.17f));
                leg.transform.localRotation=Quaternion.FromToRotation(Vector3.up,new Vector3(Mathf.Cos(angle),-.4f,Mathf.Sin(angle)));
            }
            CampPart(PrimitiveType.Cube,"Torn blue expedition cloak",new Vector3(0,3.2f,.1f),
                new Vector3(3.4f,.08f,1.1f),new Color(.12f,.3f,.57f));
            CampPart(PrimitiveType.Capsule,"Heavy silencing chain",new Vector3(0,2.8f,1.1f),
                new Vector3(.16f,3.5f,.16f),new Color(.27f,.27f,.28f));
        }
        private GameObject CampPart(PrimitiveType type,string partName,Vector3 at,Vector3 scale,Color color)
        {
            var part=RaidPrimitive.Create(type);part.name=partName;part.transform.SetParent(_campModel.transform,false);
            part.transform.localPosition=at;part.transform.localScale=scale;part.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            part.GetComponent<Renderer>().SetPropertyBlock(block);return part;
        }

        private void ClearCampanella(bool keepModel=false)
        {
            ClearCampanellaHard(keepModel);
            ClearCampanellaAttack();
            for(int i=0;i<4;i++)
            { _campAssignees[i]=null;if(_campNumberLabels[i]!=null)Destroy(_campNumberLabels[i].gameObject);_campNumberLabels[i]=null; }
            if(_campProgressLabel!=null)Destroy(_campProgressLabel.gameObject);_campProgressLabel=null;
            if(!keepModel && _campModel!=null)Destroy(_campModel);
            if(!keepModel)_campModel=null;
            if(!keepModel && _campHiddenBody!=null)
            { foreach(var renderer in _campHiddenBody)if(renderer!=null)renderer.enabled=true;_campHiddenBody=null; }
            _campStep=CampanellaStep.Rest;_campPattern=CampanellaPattern.None;
            _campSpecialRecoveryPending=false;
            _campRemaining=_campStagger=0;_campGeneralStagger=false;_campPlateStep=0;_campSuccessMask=_campFailureMask=_campMissingMask=0;
            _campReplicaPlatePoints=null;
            _campReplicaPoseStarted=false;
            _campReplicaStoryShown=false;
            _campReplicaForceAnnounced=false;
            RaidApproachField.Instance?.SetCampanellaPlateState(-1,0,0);
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
            if(StageManager.Instance?.currentStage==3)FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);
        }
        private void ClearCampanellaAttack()
        {
            if(_campPose!=null)Destroy(_campPose);_campPose=null;
            GetComponent<CombatCounterTarget>()?.CloseWindow();_campCounterOpen=false;
            if(_campCounterIcon!=null)Destroy(_campCounterIcon.gameObject);_campCounterIcon=null;
            if(_campMarker!=null)Destroy(_campMarker.gameObject);_campMarker=null;
            foreach(var ground in _campGround)if(ground!=null)Destroy(ground.gameObject);
            _campGround.Clear();
        }
        private void CampGround(AoETelegraphType type,Vector3 point,Vector3 direction,float duration,float inner,float outer,float angle=120)
        { _campGround.Add(AoETelegraph.Spawn(type,point,direction,duration,inner,outer,angle)); }

        private void TickCampanella()
        {
            float dt=Time.deltaTime;
            if(_campGeneralStagger && CampanellaRequiredStaggerPending){EndCampanella();_campRest=0;}
            TickCampanellaPose();
            if(CampanellaHard && TickCampanellaHard(dt))
            { RefreshCampanellaPlateLabels();UpdateTopBossUI();UpdateCastBanner();UpdateCampanellaCastUI();
              RaidApproachField.Instance?.SetCampanellaPlateState(
                  _campStep==CampanellaStep.TuneActive?_campPlateStep:-1,_campSuccessMask,_campFailureMask,null,_campMissingMask);
              return; }
            if(_campStep==CampanellaStep.Rest)
            {
                fsmState=BossFSMState.IdleChasing;
                if(!CampanellaHard || !_campHardRestProtected)
                { RotateTowardsAggroTargetXZ();UpdateMeleePursuit(dt);UpdateAutoAttack(); }
                _campRest-=dt;
                if(_campRest<=0)
                {
                    if(CampanellaHard && CampanellaNormalRules.ForceBellDue(_campForceUsed,HpRatio,campanellaHard.transformationThreshold))
                    { _campForceUsed=true;_campHardForcedResolved=true; }
                    if(CampanellaHard && TryBeginCampanellaHardSpecial()) { }
                    else if(CampanellaHard) BeginNextCampanellaHardPattern();
                    else {BeginCampanella((CampanellaPattern)_campNext);_campNext=_campNext>=7?1:_campNext+1;}
                }
            }
            else if(_campStep==CampanellaStep.Groggy)
            {
                _groggyRemainingTime=_campRemaining=Mathf.Max(0,_campRemaining-dt);
                if(_campRemaining<=0)EndCampanella();
            }
            else
            {
                _campRemaining=Mathf.Max(0,_campRemaining-dt);_castRemainingTime=_campRemaining;
                switch(_campStep)
                {
                    case CampanellaStep.Cast:TickCampanellaCast();break;
                    case CampanellaStep.TunePrepare:if(_campRemaining<=0)BeginCampanellaPlate();break;
                    case CampanellaStep.TuneActive:TickCampanellaPlate();break;
                    case CampanellaStep.TuneGap:if(_campRemaining<=0)BeginCampanellaPlate();break;
                    case CampanellaStep.ForcedBell:if(_campRemaining<=0)FinishForcedBell(false);break;
                }
            }
            if(!_campStoryUsed && currentHp>0 && HpRatio<=CampTuning.storyThreshold)
            { _campStoryUsed=true;StartCoroutine(CampanellaStory()); }
            RefreshCampanellaPlateLabels();UpdateTopBossUI();UpdateCastBanner();UpdateCampanellaCastUI();
            RaidApproachField.Instance?.SetCampanellaPlateState(
                _campStep==CampanellaStep.TuneActive?_campPlateStep:-1,_campSuccessMask,_campFailureMask,null,_campMissingMask);
        }
        private IEnumerator CampanellaStory()
        {
            if(currentHp<=0)yield break;
            FindFirstObjectByType<RaidLobbyUI>()?.Say("캄파넬라 · 종을 울리지 마라... 시계탑 꼭대기의 ‘그것’이 깨어난다...");
            yield return new WaitForSeconds(3);
            if(currentHp>0)FindFirstObjectByType<RaidLobbyUI>()?.Say("캄파넬라 · 차라리 나를 완전히 부숴라... 소리가 나지 않게...");
        }

        private void BeginCampanella(CampanellaPattern pattern)
        {
            ClearCampanellaAttack();_campPattern=pattern;_campStep=CampanellaStep.Cast;
            _campHardImpactShown=false;
            _campHardRestProtected=false;
            fsmState=BossFSMState.CastingSkill;
            _campRemaining=CampanellaHard?CampanellaHardRules.Tempo(CampTuning.castSeconds,campanellaHard.attackTempo):CampTuning.castSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _campOrigin=transform.position;_campForward=transform.forward;_campRight=Random.value>=.5f;
            _campTarget=ResolveLivingAggroTarget();_campWeightLocked=false;
            if(pattern==CampanellaPattern.SilentTuning){BeginCampanellaTuning();return;}
            if(pattern==CampanellaPattern.ForcedBell){BeginForcedBell();return;}
            if(CampanellaHard)PlayCampanellaCue(campanellaHard.attackPrepCue);
            if(!PlayCampanellaTrigger(pattern))ShowTemporaryCampanellaPose(pattern);
            switch(pattern)
            {
                case CampanellaPattern.ChainCrush:
                    _currentCastSkillName="사슬 압착";
                    if(_campTarget!=null)_campMarker=AttachedPartyMechanicMarker.SpawnTargetMarker(_campTarget,1.5f);
                    SetMechanicAlert("탱커 표식 · 앞다리와 사슬 강타",Color.red,_campRemaining);break;
                case CampanellaPattern.BronzeSweep:
                    _currentCastSkillName="청동다리 휩쓸기";
                    Vector3 side=Vector3.Cross(Vector3.up,_campForward)*(_campRight?1:-1);
                    if(!CampanellaHard)CampGround(AoETelegraphType.FrontCone_120Deg,_campOrigin,side,_campRemaining,0,CampTuning.arenaRadius,180);
                    SetMechanicAlert(_campRight?"오른쪽 앞다리 준비 · 왼쪽으로 회피":"왼쪽 앞다리 준비 · 오른쪽으로 회피",Color.red,_campRemaining);break;
                case CampanellaPattern.SuppressedVibration:
                    _currentCastSkillName="억눌린 진동";
                    if(!CampanellaHard)CampGround(AoETelegraphType.Circle_Radius6,_campOrigin,_campForward,_campRemaining,0,CampTuning.vibrationRadius);
                    SetMechanicAlert("종 아래 공명 확산 · 원 밖으로 이동",Color.red,_campRemaining);break;
                case CampanellaPattern.FallingWeight:
                    _currentCastSkillName="낙하하는 봉인추";
                    var living=GetLivingPartyMemberTransforms();_campTarget=living.Count>0?living[Random.Range(0,living.Count)]:null;
                    if(_campTarget!=null)_campMarker=AttachedPartyMechanicMarker.SpawnTargetMarker(_campTarget,CampTuning.weightRadius);
                    SetMechanicAlert("봉인추 추적 · 산개 후 위치 고정 시 이탈",Color.red,_campRemaining);break;
                case CampanellaPattern.ResonanceShare:
                    _currentCastSkillName="공명 분담";
                    var party=GetLivingPartyMemberTransforms();_campTarget=party.Count>0?party[Random.Range(0,party.Count)]:null;
                    if(_campTarget!=null)_campMarker=AttachedPartyMechanicMarker.SpawnStackMarker(_campTarget,CampTuning.shareRadius);
                    SetMechanicAlert("집합 표식 · 모여서 충격 분담",new Color(1,.65f,.2f),_campRemaining);break;
                case CampanellaPattern.ChainRush:
                    _currentCastSkillName="사슬 폭주";
                    if(!CampanellaHard)CampGround(AoETelegraphType.Rectangle,_campOrigin,_campForward,_campRemaining,CampTuning.chargeWidth,CampTuning.chargeLength);
                    SetMechanicAlert("⚔ 사슬 폭주 · 정면 금색 카운터 또는 직선 회피",new Color(1,.75f,.15f),_campRemaining);break;
                case CampanellaPattern.Pendulum:
                    BeginCampanellaPendulum();break;
            }
        }
        private void TickCampanellaCast()
        {
            if(CampanellaHard)TickCampanellaHardCast();
            if(_campPattern==CampanellaPattern.ChainRush && !_campCounterOpen && _campRemaining<=(CampanellaHard?campanellaHard.counterWindow:2.3f))
            {
                _campCounterOpen=true;CombatCounterTarget.Ensure(transform).OpenWindow(_campRemaining);
                _campCounterIcon=FloraLabel("⚔ COUNTER",new Color(1,.78f,.15f));
                _campCounterIcon.transform.position=transform.position+Vector3.up*4.5f;
                SetMechanicAlert("⚔ 금색 카운터! 정면 기술 적중 또는 직선 회피",new Color(1,.75f,.15f),_campRemaining);
                PlayCampanellaCue(CampTuning.counterCue);
            }
            if(_campPattern==CampanellaPattern.FallingWeight && !_campWeightLocked && _campRemaining<=1f)
            {
                _campWeightLocked=true;_campTargetPoint=_campTarget!=null?_campTarget.position:Vector3.zero;
                if(_campMarker!=null)Destroy(_campMarker.gameObject);_campMarker=null;
                if(!CampanellaHard)CampGround(AoETelegraphType.Circle_Radius6,_campTargetPoint,Vector3.forward,_campRemaining,0,CampTuning.weightRadius);
                SetMechanicAlert("봉인추 위치 고정 · 붉은 원에서 이탈",Color.red,_campRemaining);
            }
            if(_campRemaining<=0)ResolveCampanella();
        }
        private void ResolveCampanella()
        {
            if(currentHp<=0)return;
            if(CampanellaHard)PlayCampanellaCue(campanellaHard.attackImpactCue);
            if(_campPattern==CampanellaPattern.SuppressedVibration)PlayCampanellaCue(CampTuning.suppressedVibrationCue);
            var living=GetLivingPartyMemberTransforms();
            foreach(var member in living)
            {
                bool hit=false;float damage=0;
                switch(_campPattern)
                {
                    case CampanellaPattern.ChainCrush:hit=member==_campTarget;damage=CampTuning.tankDamage;break;
                    case CampanellaPattern.BronzeSweep:
                        hit=FloraNormalRules.InHalf(member.position,_campOrigin,Vector3.Cross(Vector3.up,_campForward),CampTuning.arenaRadius,_campRight);
                        damage=CampTuning.sweepDamage;break;
                    case CampanellaPattern.SuppressedVibration:
                        hit=FloraNormalRules.InCircle(member.position,_campOrigin,CampTuning.vibrationRadius);damage=CampTuning.vibrationDamage;break;
                    case CampanellaPattern.FallingWeight:
                        hit=FloraNormalRules.InCircle(member.position,_campTargetPoint,CampTuning.weightRadius);damage=CampTuning.weightDamage;break;
                    case CampanellaPattern.ChainRush:
                        hit=_campGround.Count>0 && _campGround[0]!=null && _campGround[0].IsPointInsideXZ(member.position);
                        damage=CampTuning.chargeDamage;break;
                    case CampanellaPattern.Pendulum:
                        hit=_campGround.Count>0 && _campGround[0]!=null && _campGround[0].IsPointInsideXZ(member.position);
                        damage=campanellaHard.pendulumDamage;break;
                }
                if(hit)DamagePartyMember(member,damage,CampanellaHard && _campPattern==CampanellaPattern.ChainCrush?
                    CombatDamageKind.Direct:CombatDamageKind.Area,"campanella."+_campPattern);
            }
            if(_campPattern==CampanellaPattern.ResonanceShare && _campTarget!=null && CombatRange.LivingPartyMember(_campTarget))
            {
                int count=0;foreach(var member in living)if(FloraNormalRules.InCircle(member.position,_campTarget.position,CampTuning.shareRadius))count++;
                foreach(var member in living)if(FloraNormalRules.InCircle(member.position,_campTarget.position,CampTuning.shareRadius))
                    DamagePartyMember(member,CampTuning.shareDamage/Mathf.Max(1,count),CombatDamageKind.Area,"campanella.ResonanceShare");
            }
            if(StageManager.CombatSuspended)return;
            if(_campPattern==CampanellaPattern.ChainRush)CombatMetricsManager.Event("캄파넬라 일반 카운터 미성공 · 사슬 폭주");
            if(CampanellaHard && TryBeginCampanellaHardFollowup())return;
            EndCampanella();
        }
        private void EndCampanella(bool groggy=false,float groggySeconds=0,float recovery=0)
        {
            _campGeneralStagger=false;
            NotifyDamageCastEnded();
            ClearCampanellaAttack();
            _campStep=groggy?CampanellaStep.Groggy:CampanellaStep.Rest;
            fsmState=groggy?BossFSMState.Groggy:BossFSMState.IdleChasing;
            _campRemaining=groggy?groggySeconds:0;_groggyRemainingTime=_campRemaining;
            _campRest=(_campSpecialRecoveryPending?CampTuning.forceBellRecoverySeconds:
                recovery>0?recovery:CampanellaHard?CampanellaHardRules.Tempo(CampTuning.recoverySeconds,campanellaHard.attackTempo):CampTuning.recoverySeconds)+
                (CampanellaHard?CampanellaHardRules.Tempo(CampTuning.patternInterval,campanellaHard.attackTempo):CampTuning.patternInterval);
            if(CampanellaHard && _campHardForcedResolved && !_campHardTransformed && !groggy)
                _campRest=_campSpecialRecoveryPending?0:campanellaHard.specialRecoverySeconds;
            if(CampanellaHard)_campHardRestProtected=recovery>0 || _campSpecialRecoveryPending;
            if(!groggy)_campSpecialRecoveryPending=false;
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
        }
        public void CampanellaCounterSucceeded(Transform attacker)
        {
            if(CampanellaHard && CampanellaLockCounterWindow){CampanellaLockCounterSucceeded(attacker);return;}
            if(!CampanellaEncounter || _campStep!=CampanellaStep.Cast || _campPattern!=CampanellaPattern.ChainRush || !_campCounterOpen)return;
            if(CampanellaHard && attacker.GetComponent<PartyLife>()?.IsTank==true)
                _campHardLastCounter=RaidCombatAuthority.ServerTime;
            CombatMetricsManager.Event("캄파넬라 일반 카운터 성공: "+attacker.name);
            SetMechanicAlert("⚔ 사슬 폭주 중단 · 그로기",new Color(1,.8f,.25f),2);
            PlayCampanellaCue(CampTuning.counterCue);
            EndCampanella(true,AegisPaladinData.CounterGroggy);
        }
        public bool CampanellaAiCounterReady(Transform actor) => CampanellaEncounter && _campStep==CampanellaStep.Cast &&
            _campPattern==CampanellaPattern.ChainRush && _campCounterOpen && CombatRange.LivingPartyMember(actor) ||
            CampanellaLockCounterWindow && CampanellaLockCounterAllowed(actor);
        public bool CampanellaTankbusterImminent(Transform actor,float within) => CampanellaEncounter &&
            _campStep==CampanellaStep.Cast && _campPattern==CampanellaPattern.ChainCrush && _campTarget==actor && _campRemaining<=within;

        private void BeginCampanellaTuning()
        {
            _campStep=CampanellaStep.TunePrepare;_campPlateStep=0;
            _campSuccessMask=_campFailureMask=_campMissingMask=0;_campWrongNotified=false;
            _campRemaining=CampTuning.tuningPrepareSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName="침묵의 조율 · 배치";
            var living=GetLivingPartyMemberTransforms();
            for(int i=living.Count-1;i>0;i--){int j=Random.Range(0,i+1);var swap=living[i];living[i]=living[j];living[j]=swap;}
            for(int i=0;i<4;i++)
            {
                _campAssignees[i]=i<living.Count?living[i]:null;
                if(_campAssignees[i]!=null)
                {
                    _campNumberLabels[i]=FloraLabel(CampanellaNumber(i),new Color(1,.88f,.28f));
                    _campNumberLabels[i].transform.position=_campAssignees[i].position+Vector3.up*3;
                }
            }
            SetMechanicAlert("침묵의 조율 · I~IV 각자 같은 번호 공명판으로 이동",new Color(.75f,.66f,.25f),_campRemaining);
            if(!PlayCampanellaTrigger(CampanellaPattern.SilentTuning))ShowTemporaryCampanellaPose(CampanellaPattern.SilentTuning);
            UpdateCampanellaPersonalHint();
        }
        private void BeginCampanellaPlate()
        {
            if(_campPlateStep>=4){FinishCampanellaTuning();return;}
            _campStep=CampanellaStep.TuneActive;_campRemaining=CampTuning.tuningWindowSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _campWrongNotified=false;
            string number=CampanellaNumber(_campPlateStep);
            _currentCastSkillName="침묵의 조율 · "+number+" / 차단 성공 "+CampanellaSuccessCount()+"/4";
            if(_campAssignees[_campPlateStep]==null)
                SetMechanicAlert(number+" 인원 부족으로 미배정 · 건너뜀",new Color(.8f,.7f,.38f),_campRemaining);
            else SetMechanicAlert(number+" 공명판 활성 · 담당자 "+_campAssignees[_campPlateStep].name+
                " · 다음 "+(_campPlateStep<3?CampanellaNumber(_campPlateStep+1):"종료"),new Color(1,.82f,.25f),_campRemaining);
            UpdateCampanellaPersonalHint();
        }
        private void TickCampanellaPlate()
        {
            int index=_campPlateStep;
            var assigned=_campAssignees[index];
            if(assigned==null){ResolveCampanellaPlate(CampanellaPlateResult.Missing);return;}
            bool alive=CombatRange.LivingPartyMember(assigned);
            bool correct=alive && CampanellaNormalRules.InPlate(assigned.position,CampanellaPlatePoint(index),CampTuning.plateRadius);
            bool wrong=false;
            if(!correct)
                foreach(var member in GetLivingPartyMemberTransforms())
                    if(member!=assigned && CampanellaNormalRules.InPlate(member.position,CampanellaPlatePoint(index),CampTuning.plateRadius))
                    { wrong=true;break; }
            var outcome=CampanellaNormalRules.CheckPlate(0,correct?0:wrong?1:-1,alive,_campRemaining<=0);
            if(outcome==CampanellaPlateResult.Success || outcome==CampanellaPlateResult.Failed)
                ResolveCampanellaPlate(outcome);
            else if(outcome==CampanellaPlateResult.WrongActor && !_campWrongNotified)
            { _campWrongNotified=true;SetMechanicAlert(CampanellaNumber(index)+" 오답자 · 담당자만 차단 가능",new Color(1,.55f,.25f),1.5f); }
        }
        private void ResolveCampanellaPlate(CampanellaPlateResult outcome)
        {
            int index=_campPlateStep;if(index>=4)return;
            int bit=1<<index;
            if((_campSuccessMask&bit)!=0 || (_campFailureMask&bit)!=0)return;
            string number=CampanellaNumber(index);
            if(outcome==CampanellaPlateResult.Success)
            {
                _campSuccessMask|=bit;CombatMetricsManager.Event("침묵의 조율 "+number+" 차단 성공");
                SetMechanicAlert(number+" 차단 성공 · "+CampanellaSuccessCount()+"/4",Color.green,CampTuning.tuningGapSeconds);
                PlayCampanellaCue(CampTuning.plateCue);
                if(CampanellaHard){BeginCampanellaEcho(index);return;}
            }
            else
            {
                _campFailureMask|=bit;
                if(outcome==CampanellaPlateResult.Missing)_campMissingMask|=bit;
                CombatMetricsManager.Event("침묵의 조율 "+number+(outcome==CampanellaPlateResult.Missing?" 미배정":" 차단 실패"));
                if(outcome!=CampanellaPlateResult.Missing)
                {
                    PlayCampanellaCue(CampTuning.suppressedVibrationCue);
                    foreach(var member in GetLivingPartyMemberTransforms())
                        DamagePartyMember(member,CampanellaHard?campanellaHard.tuningFailureDamage:CampTuning.tuningFailureDamage,
                            CombatDamageKind.Area,"campanella.tuning."+number);
                }
                if(StageManager.CombatSuspended)return;
                SetMechanicAlert(number+(outcome==CampanellaPlateResult.Missing?" 인원 부족 · 미배정":" 차단 실패 · 음파 충격"),Color.red,CampTuning.tuningGapSeconds);
            }
            _campPlateStep=CampanellaNormalRules.NextStep(index);
            _campStep=CampanellaStep.TuneGap;_campRemaining=CampTuning.tuningGapSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName="침묵의 조율 · 차단 성공 "+CampanellaSuccessCount()+"/4";
            UpdateCampanellaPersonalHint();
        }
        private void FinishCampanellaTuning()
        {
            if(CampanellaHard && FinishCampanellaHardTuning())return;
            FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);
            if(_campProgressLabel!=null)Destroy(_campProgressLabel.gameObject);_campProgressLabel=null;
            for(int i=0;i<4;i++)
            { _campAssignees[i]=null;if(_campNumberLabels[i]!=null)Destroy(_campNumberLabels[i].gameObject);_campNumberLabels[i]=null; }
            EndCampanella(false,0,CampTuning.forceBellRecoverySeconds);
            _campSuccessMask=_campFailureMask=_campMissingMask=0;_campPlateStep=0;
            RaidApproachField.Instance?.SetCampanellaPlateState(-1,0,0);
        }
        private int CampanellaSuccessCount()
        {int n=0;for(int i=0;i<4;i++)if((_campSuccessMask&(1<<i))!=0)n++;return n;}
        private static string CampanellaNumber(int index) => new[]{"I","II","III","IV"}[Mathf.Clamp(index,0,3)];
        private Vector3 CampanellaPlatePoint(int index)
        {
            if(RaidCombatAuthority.Replica && _campReplicaPlatePoints!=null && _campReplicaPlatePoints.Length>index)
                return _campReplicaPlatePoints[index];
            Vector3 point=CampTuning.platePoints!=null && CampTuning.platePoints.Length>index
                ?CampTuning.platePoints[index]:new Vector3(index==0||index==3?-9:9,0,index<2?9:-9);
            return CombatRange.ClampToArena(point,Mathf.Max(3,CampTuning.arenaRadius-CampTuning.plateRadius-1));
        }
        public Vector3 CampanellaPlatePosition(int index) => CampanellaPlatePoint(index);
        private void RefreshCampanellaPlateLabels()
        {
            if(_campStep!=CampanellaStep.TunePrepare && _campStep!=CampanellaStep.TuneActive &&
                _campStep!=CampanellaStep.TuneGap && _campStep!=CampanellaStep.EchoWarning)return;
            for(int i=0;i<4;i++)if(_campNumberLabels[i]!=null && _campAssignees[i]!=null)
            {
                _campNumberLabels[i].transform.position=_campAssignees[i].position+Vector3.up*3;
                if(Camera.main!=null)_campNumberLabels[i].transform.rotation=Camera.main.transform.rotation;
                _campNumberLabels[i].text=CampanellaNumber(i)+" · "+((_campSuccessMask&(1<<i))!=0?"성공":
                    (_campMissingMask&(1<<i))!=0?"미배정":(_campFailureMask&(1<<i))!=0?"실패":i==_campPlateStep?"현재":"대기");
            }
            if(_campProgressLabel==null)_campProgressLabel=FloraLabel("",new Color(1,.85f,.3f));
            _campProgressLabel.text="차단 성공 "+CampanellaSuccessCount()+"/4 · 현재 "+(_campPlateStep<4?CampanellaNumber(_campPlateStep):"종료")+
                " · 다음 "+(_campPlateStep<3?CampanellaNumber(_campPlateStep+1):"종료")+
                " · "+_campRemaining.ToString("0.0")+"초";
            _campProgressLabel.transform.position=Vector3.up*5;
            if(Camera.main!=null)_campProgressLabel.transform.rotation=Camera.main.transform.rotation;
        }
        private void UpdateCampanellaPersonalHint()
        {
            var player=StageManager.Instance?.player;
            if(player==null)return;
            for(int i=0;i<4;i++)if(_campAssignees[i]==player.transform)
            { FindFirstObjectByType<RaidLobbyUI>()?.Hint("내 번호 "+CampanellaNumber(i)+" · 해당 공명판 대기 · 현재 "+
                (_campPlateStep<4?CampanellaNumber(_campPlateStep):"종료"));return; }
            FindFirstObjectByType<RaidLobbyUI>()?.Hint("침묵의 조율 · 미배정");
        }

        private void BeginForcedBell()
        {
            _campForceUsed=true;
            if(CampanellaHard)_campHardForcedResolved=true;
            EndCampanella();
        }
        public void AddCampanellaStagger(float points)
        {
            if(!CampanellaEncounter || RaidCombatAuthority.Replica || StageManager.CombatSuspended || currentHp<=0 ||
                _campStep!=CampanellaStep.ForcedBell)return;
            if(CampanellaNormalRules.ApplyStagger(ref _campStagger,points,CampTuning.forceBellStagger))FinishForcedBell(true);
        }
        private void FinishForcedBell(bool success)
        {
            if(_campStep!=CampanellaStep.ForcedBell)return;
            if(CampanellaHard)_campHardForcedResolved=true;
            if(success)
            {
                CombatMetricsManager.Event("강제 타종 저지 성공 · 무력화");
                SetMechanicAlert("사슬 재봉인 · 강제 타종 저지 · 8초 그로기",new Color(.7f,.35f,1),3);
                PlayCampanellaCue(CampTuning.plateCue);
                _campSpecialRecoveryPending=true;
                EndCampanella(true,CampTuning.forceBellGroggySeconds,CampTuning.forceBellRecoverySeconds);
            }
            else
            {
                CombatMetricsManager.Event("강제 타종 저지 실패 · 불완전한 타종 충격");
                SetMechanicAlert("불완전한 타종 충격 · 회복",Color.red,3);
                PlayCampanellaCue(CampTuning.incompleteBellCue);
                foreach(var member in GetLivingPartyMemberTransforms())
                    DamagePartyMember(member,CampTuning.forceBellFailureDamage,CombatDamageKind.Area,"campanella.forcebell.failure");
                if(!StageManager.CombatSuspended && currentHp>0)EndCampanella(false,0,CampTuning.forceBellRecoverySeconds);
            }
        }
        private void UpdateCampanellaCastUI()
        {
            if(!CampanellaEncounter)return;
            if(bossPhaseText!=null)bossPhaseText.text=CampanellaHard?
                (_campHardTransformed?"봉인이 벗겨진 대종, 캄파넬라 · 하드":"침묵의 종루 · 캄파넬라 · 하드"):
                "침묵의 종루 · 캄파넬라 · 노멀";
            if(_campStep==CampanellaStep.ForcedBell)
            {
                if(bossCastFillImage!=null){bossCastFillImage.fillAmount=Mathf.Clamp01(_campStagger/Mathf.Max(1,CampTuning.forceBellStagger));bossCastFillImage.color=new Color(.7f,.2f,1);}
                if(bossCastText!=null)bossCastText.text="강제 타종 무력화 "+_campStagger.ToString("0")+"/"+
                    CampTuning.forceBellStagger.ToString("0")+" · "+_campRemaining.ToString("0.0")+"초";
            }
            else if(_campStep==CampanellaStep.TunePrepare || _campStep==CampanellaStep.TuneActive || _campStep==CampanellaStep.TuneGap)
            {
                if(bossCastText!=null)bossCastText.text="침묵의 조율 · 차단 성공 "+CampanellaSuccessCount()+"/4 · 현재 "+
                    (_campPlateStep<4?CampanellaNumber(_campPlateStep):"종료")+" → 다음 "+
                    (_campPlateStep<3?CampanellaNumber(_campPlateStep+1):"종료")+" · "+_campRemaining.ToString("0.0")+"초";
            }
        }
        private void PlayCampanellaCue(AudioClip clip)
        {if(clip==null)return;if(_campAudio==null)_campAudio=GetComponent<AudioSource>()??gameObject.AddComponent<AudioSource>();_campAudio.PlayOneShot(clip);}
        private bool PlayCampanellaTrigger(CampanellaPattern pattern)
        {
            var animator=_campHardTransformed && _campHardModel!=null?_campHardModel.GetComponentInChildren<Animator>():
                _campModel!=null?_campModel.GetComponentInChildren<Animator>():null;
            if(animator==null)return false;
            string trigger=pattern==CampanellaPattern.ChainCrush?CampTuning.chainTrigger:
                pattern==CampanellaPattern.BronzeSweep?CampTuning.legTrigger:
                pattern==CampanellaPattern.SuppressedVibration?CampTuning.vibrationTrigger:
                pattern==CampanellaPattern.FallingWeight?CampTuning.weightTrigger:
                pattern==CampanellaPattern.ResonanceShare?CampTuning.shareTrigger:
                pattern==CampanellaPattern.ChainRush?CampTuning.chargeTrigger:
                pattern==CampanellaPattern.SilentTuning?CampTuning.tuneTrigger:
                pattern==CampanellaPattern.Pendulum?campanellaHard.pendulumTrigger:CampTuning.forcedBellTrigger;
            if(string.IsNullOrEmpty(trigger))return false;
            foreach(var parameter in animator.parameters)if(parameter.name==trigger && parameter.type==AnimatorControllerParameterType.Trigger)
            {animator.SetTrigger(trigger);return true;}
            return false;
        }
        private void ShowTemporaryCampanellaPose(CampanellaPattern pattern)
        {
            if(pattern==CampanellaPattern.FallingWeight || pattern==CampanellaPattern.ResonanceShare ||
                pattern==CampanellaPattern.SilentTuning)return; // Persistent markers and plates are the preparation.
            _campPose=RaidPrimitive.Create(PrimitiveType.Capsule);
            _campPose.name="Temporary muted bell/leg preparation · replace Animator trigger";
            _campPose.transform.SetParent(transform,false);
            _campPose.GetComponent<Collider>().enabled=false;
            var color=pattern==CampanellaPattern.ForcedBell?new Color(.56f,.22f,.73f):new Color(.31f,.47f,.57f);
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            _campPose.GetComponent<Renderer>().SetPropertyBlock(block);
            TickCampanellaPose();
        }
        private void TickCampanellaPose()
        {
            if(_campPose==null)return;
            float progress=1-Mathf.Clamp01(_campRemaining/Mathf.Max(.01f,_castTotalDuration));
            if(_campPattern==CampanellaPattern.BronzeSweep)
            {
                bool right=_campStep==CampanellaStep.HardFollowup?!_campRight:_campRight;
                _campPose.transform.localPosition=new Vector3(right?2:-2,1.8f+progress*.6f,1);
                _campPose.transform.localRotation=Quaternion.Euler(0,0,right?-30:30);
                _campPose.transform.localScale=new Vector3(.28f,2.4f,.28f);
            }
            else if(_campPattern==CampanellaPattern.Pendulum)
            {
                bool right=_campStep==CampanellaStep.HardFollowup?!_campRight:_campRight;
                _campPose.transform.localPosition=new Vector3(right?2:-2,2.2f,1.1f);
                _campPose.transform.localRotation=Quaternion.Euler(0,0,(right?-1:1)*(25+progress*45));
                _campPose.transform.localScale=new Vector3(.55f,2.8f,.55f);
            }
            else if(_campPattern==CampanellaPattern.SuppressedVibration)
            {
                _campPose.transform.localPosition=new Vector3(0,.3f,0);
                _campPose.transform.localScale=new Vector3(1+progress*2,.15f,1+progress*2);
            }
            else
            {
                _campPose.transform.localPosition=new Vector3(0,2.7f-progress*.5f,1.3f);
                _campPose.transform.localRotation=Quaternion.Euler(25+progress*35,0,0);
                _campPose.transform.localScale=new Vector3(.35f,2.6f,.35f);
            }
        }

        private int[] CaptureCampanellaAssignees()
        {
            if(!CampanellaEncounter)return null;
            var ids=new int[4];for(int i=0;i<4;i++)ids[i]=RaidCombatAuthority.Id(_campAssignees[i]);
            return ids;
        }
        private float[] CaptureCampanellaPlateAxis(bool xAxis)
        {
            if(!CampanellaEncounter)return null;
            var points=new float[4];for(int i=0;i<4;i++)
            {var point=CampanellaPlatePoint(i);points[i]=xAxis?point.x:point.z;}
            return points;
        }
        private void ApplyCampanellaReplica(RaidCombatEnemy state)
        {
            int priorSuccess=_campSuccessMask,priorFailure=_campFailureMask;
            var priorStep=_campStep;
            if(CampanellaHard)ApplyCampanellaHardReplica(state,priorStep);
            if((CampanellaStep)state.campStep==CampanellaStep.ForcedBell && !_campReplicaForceAnnounced)
            { _campReplicaForceAnnounced=true;FindFirstObjectByType<RaidLobbyUI>()?.Say("캄파넬라 · 안 돼, 사슬이 풀렸다... 내 몸이 부서지기 전에 소리를 삼켜야 해!"); }
            if(state.campStoryUsed && !_campReplicaStoryShown && !state.dead)
            { _campReplicaStoryShown=true;StartCoroutine(CampanellaStory()); }
            if(!state.campStoryUsed)_campReplicaStoryShown=false;
            var previousPattern=_campPattern;
            _campPattern=(CampanellaPattern)state.campPattern;_campStep=(CampanellaStep)state.campStep;
            _campPlateStep=state.campPlateStep;_campSuccessMask=state.campSuccessMask;_campFailureMask=state.campFailureMask;_campMissingMask=state.campMissingMask;
            _campHardEchoPlate=state.campHardEchoPlate;
            if((_campSuccessMask&~priorSuccess)!=0)PlayCampanellaCue(CampTuning.plateCue);
            if((_campFailureMask&~priorFailure)!=0)PlayCampanellaCue(CampTuning.suppressedVibrationCue);
            if(priorStep==CampanellaStep.ForcedBell && _campStep!=CampanellaStep.ForcedBell)
                PlayCampanellaCue(_campStep==CampanellaStep.Groggy?CampTuning.plateCue:CampTuning.incompleteBellCue);
            _campStagger=state.campStagger;_campRemaining=state.castRemaining;
            if(previousPattern!=_campPattern)
            { _campReplicaPoseStarted=false;if(_campPose!=null)Destroy(_campPose);_campPose=null; }
            bool hasPose=_campStep==CampanellaStep.Cast || _campStep==CampanellaStep.ForcedBell;
            if(hasPose && !_campReplicaPoseStarted)
            { _campReplicaPoseStarted=true;if(!PlayCampanellaTrigger(_campPattern))ShowTemporaryCampanellaPose(_campPattern); }
            if(!hasPose && _campReplicaPoseStarted)
            { _campReplicaPoseStarted=false;if(_campPose!=null)Destroy(_campPose);_campPose=null; }
            if(state.campPlateX!=null && state.campPlateZ!=null && state.campPlateX.Length>=4 && state.campPlateZ.Length>=4)
            {
                _campReplicaPlatePoints=new Vector3[4];
                for(int i=0;i<4;i++)_campReplicaPlatePoints[i]=new Vector3(state.campPlateX[i],0,state.campPlateZ[i]);
            }
            bool tuning=_campStep==CampanellaStep.TunePrepare || _campStep==CampanellaStep.TuneActive ||
                _campStep==CampanellaStep.TuneGap || _campStep==CampanellaStep.EchoWarning;
            for(int i=0;i<4;i++)
            {
                _campAssignees[i]=tuning && state.campAssignees!=null && state.campAssignees.Length>i
                    ?RaidCombatAuthority.Resolve(state.campAssignees[i]):null;
                if(tuning && _campAssignees[i]!=null && _campNumberLabels[i]==null)
                    _campNumberLabels[i]=FloraLabel(CampanellaNumber(i),new Color(1,.88f,.28f));
                if((!tuning || _campAssignees[i]==null) && _campNumberLabels[i]!=null)
                {Destroy(_campNumberLabels[i].gameObject);_campNumberLabels[i]=null;}
            }
            if(tuning){RefreshCampanellaPlateLabels();UpdateCampanellaPersonalHint();}
            else
            {
                if(_campProgressLabel!=null)Destroy(_campProgressLabel.gameObject);_campProgressLabel=null;
                FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);
            }
            if(state.campCounterOpen && _campCounterIcon==null)_campCounterIcon=FloraLabel("⚔ COUNTER",new Color(1,.78f,.15f));
            if(!state.campCounterOpen && _campCounterIcon!=null){Destroy(_campCounterIcon.gameObject);_campCounterIcon=null;}
            if(_campCounterIcon!=null)_campCounterIcon.transform.position=new Vector3(state.x,state.y+4.5f,state.z);
            RaidApproachField.Instance?.SetCampanellaPlateState(tuning && _campStep==CampanellaStep.TuneActive?_campPlateStep:-1,
                _campSuccessMask,_campFailureMask,_campReplicaPlatePoints,_campMissingMask);
        }

        public bool CampanellaDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;if(!CampanellaEncounter)return false;
            if(CampanellaHard && CampanellaHardDestination(actor,role,out destination))return true;
            if(_campStep==CampanellaStep.TunePrepare || _campStep==CampanellaStep.TuneActive ||
                _campStep==CampanellaStep.TuneGap || _campStep==CampanellaStep.EchoWarning)
            {
                for(int i=0;i<4;i++)if(_campAssignees[i]==actor)
                {destination=CampanellaPlatePoint(i);destination.y=actor.position.y;return true;}
                return false;
            }
            if(_campStep!=CampanellaStep.Cast)return false;
            if(_campPattern==CampanellaPattern.ResonanceShare && _campTarget!=null)
            {destination=_campTarget.position+new Vector3((role-2.5f)*.5f,0,.5f);destination.y=actor.position.y;return true;}
            if(_campPattern==CampanellaPattern.FallingWeight && _campTarget==actor)
            {destination=CombatRange.ClampToArena(actor.position+(actor.position-transform.position).normalized*5,CampTuning.arenaRadius-1);destination.y=actor.position.y;return true;}
            if(_campPattern==CampanellaPattern.SuppressedVibration)
            {destination=_campOrigin+(actor.position-_campOrigin).normalized*(CampTuning.vibrationRadius+2);destination.y=actor.position.y;return true;}
            if(_campPattern==CampanellaPattern.BronzeSweep)
            {destination=_campOrigin+Vector3.Cross(Vector3.up,_campForward)*(_campRight?-8:8);destination.y=actor.position.y;return true;}
            if(_campPattern==CampanellaPattern.ChainRush && role>=3&&PartyAiTactics.CanTakeCounterPosition(actor))
            {destination=_campOrigin+_campForward*4;destination.y=actor.position.y;return true;}
            return false;
        }
    }
}
