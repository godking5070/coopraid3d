using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public partial class BossController
    {
        private bool _campHardForcedResolved, _campHardTransformed, _campHardFirstLockDone, _campHardPendulumIntroduced,
            _campHardFinalDone, _campHardFinalActive, _campHardImpactShown,
            _campHardReplicaSkipped, _campHardPresentationUnavailable, _campHardShockwaveWarned,
            _campHardCoreDestroyed;
        private int _campHardEchoPlate;
        private readonly List<CampanellaPattern> _campHardPatternBag=new List<CampanellaPattern>();
        private CampanellaPattern _campHardLastPattern;
        private float _campHardNextPulse, _campHardLastCounter, _campHardHoldBegan, _campHardReleaseUntil,
            _campHardWeightNextPulse;
        private bool _campHardHoldClipStarted;
        private Transform _campHardTank, _campHardWeapon, _campHardVisualRoot;
        private Vector3 _campHardVisualPosition;
        private Quaternion _campHardVisualRotation, _campHardWeaponRotation;
        private bool _campHardWeaponActive;
        private GuardianClashJobVisual _campHardJob;
        private readonly GuardianClashClipPlayer _campHardActorClip=new GuardianClashClipPlayer();
        private readonly GuardianClashClipPlayer _campHardBossClip=new GuardianClashClipPlayer();
        private RaidStageCutscenePresentation _campHardPresentation;
        private GameObject _campHardModel, _campHardContactVisual;
        private AoETelegraph _campHardCircle;
        private TextMesh _campHardShield, _campHardEchoLabel;
        private BossAddMinion _campHardCore;

        public bool CampanellaHardTransformationActive => CampanellaHard && _campStep==CampanellaStep.Transformation;
        public bool CampanellaLockCounterWindow => CampanellaHard && _campStep==CampanellaStep.LockCounter;
        public bool CampanellaLockHolding(Transform actor) => CampanellaHard && _campStep==CampanellaStep.LockHold && actor==_campHardTank;
        public bool CampanellaLockPulseImminent(Transform actor) => CampanellaLockHolding(actor) && _campHardNextPulse-RaidCombatAuthority.ServerTime<=1.5f;
        public bool CampanellaLockCounterAllowed(Transform actor) => actor!=null &&
            CampanellaHardRules.CounterEligible(CampanellaLockCounterWindow,CombatRange.LivingPartyMember(actor),
                actor.GetComponent<PartyLife>()?.IsTank==true,
                BattleResurrection.FlatDistance(actor.position,transform.position),campanellaHard.tankCircleRadius);

        private void ResetCampanellaHard()
        {
            campanellaHard=campanellaHard??new CampanellaHardSettings();
            _campHardForcedResolved=_campHardTransformed=_campHardFirstLockDone=_campHardFinalDone=_campHardFinalActive=false;
            _campHardPatternBag.Clear();_campHardLastPattern=CampanellaPattern.None;
            _campHardLastCounter=-1000;_campHardReplicaSkipped=false;
            _campHardPendulumIntroduced=false;_campHardRestProtected=false;
            _campHardPresentationUnavailable=false;_campHardCoreDestroyed=false;
        }
        private void ClearCampanellaHard(bool keepModel)
        {
            StopCampanellaHardPresentation();ClearCampanellaLockVisuals();ClearCampanellaCore();
            if(_campHardModel!=null && !keepModel){Destroy(_campHardModel);_campHardModel=null;}
            _campHardTank=null;if(!keepModel)_campHardTransformed=false;_campHardFinalActive=false;
            _campHardForcedResolved=false;_campHardFirstLockDone=_campHardFinalDone=false;
            _campHardImpactShown=false;
            _campHardRestProtected=false;
            _campHardPresentationUnavailable=false;_campHardReplicaSkipped=false;
            _campHardPatternBag.Clear();_campHardLastPattern=CampanellaPattern.None;
            if(_campHardEchoLabel!=null){Destroy(_campHardEchoLabel.gameObject);_campHardEchoLabel=null;}
        }
        private bool TryBeginCampanellaHardSpecial()
        {
            if(CampanellaHardRules.TransformDue(_campHardForcedResolved,_campHardTransformed,currentHp>0))
            {BeginCampanellaTransformation();return true;}
            if(_campHardTransformed && !_campHardFirstLockDone)
            {BeginCampanellaLock();return true;}
            if(CampanellaHardRules.FinalDue(_campHardTransformed,_campHardFirstLockDone,_campHardFinalDone,
                HpRatio,campanellaHard.finalThreshold))
            {
                float cooldown=campanellaHard.counterCooldownMargin-(RaidCombatAuthority.ServerTime-_campHardLastCounter);
                if(cooldown>0 && GetLivingPartyMemberTransforms().Exists(t=>t.GetComponent<PartyLife>()?.IsTank==true))
                { _campRest=Mathf.Max(.1f,cooldown);SetMechanicAlert("탱커 카운터 준비 · 마지막 조율 "+cooldown.ToString("0.0")+"초 후",Color.yellow,cooldown);return true; }
                _campHardFinalDone=_campHardFinalActive=true;
                ClearCampanellaAttack();BeginCampanella(CampanellaPattern.SilentTuning);
                SetMechanicAlert("마지막 침묵의 조율 · 잔향 후 재집결, 종치 봉쇄",new Color(.75f,.65f,.25f),CampTuning.tuningPrepareSeconds);
                return true;
            }
            return false;
        }
        private void BeginNextCampanellaHardPattern()
        {
            // Introduce the new phase-two motion by itself before adding it to the cycle.
            if(_campHardTransformed && !_campHardPendulumIntroduced)
            { _campHardPendulumIntroduced=true;_campHardPatternBag.Clear();
              _campHardLastPattern=CampanellaPattern.Pendulum;
              BeginCampanella(CampanellaPattern.Pendulum);return; }
            CampanellaPattern[] cycle={CampanellaPattern.ChainCrush,CampanellaPattern.BronzeSweep,
                CampanellaPattern.SuppressedVibration,CampanellaPattern.FallingWeight,
                CampanellaPattern.ResonanceShare,CampanellaPattern.ChainRush,
                CampanellaPattern.SilentTuning,CampanellaPattern.Pendulum};
            if(_campHardPatternBag.Count==0)
            {
                int count=_campHardTransformed?cycle.Length:cycle.Length-1;
                for(int i=0;i<count;i++)
                {
                    int weight=campanellaHard.patternWeights!=null && i<campanellaHard.patternWeights.Length?
                        Mathf.Clamp(campanellaHard.patternWeights[i],1,4):1;
                    for(int draw=0;draw<weight;draw++)_campHardPatternBag.Add(cycle[i]);
                }
                for(int i=_campHardPatternBag.Count-1;i>0;i--)
                { int j=Random.Range(0,i+1);var swap=_campHardPatternBag[i];
                  _campHardPatternBag[i]=_campHardPatternBag[j];_campHardPatternBag[j]=swap; }
            }
            int selected=_campHardPatternBag.FindIndex(p=>p!=_campHardLastPattern);
            if(selected<0){_campHardPatternBag.Clear();BeginNextCampanellaHardPattern();return;}
            var pattern=_campHardPatternBag[selected];_campHardPatternBag.RemoveAt(selected);
            _campHardLastPattern=pattern;BeginCampanella(pattern);
        }
        private bool TickCampanellaHard(float dt)
        {
            if(_campHardReleaseUntil>0 && RaidCombatAuthority.ServerTime>=_campHardReleaseUntil)
            { _campHardReleaseUntil=0;ClearCampanellaLockVisuals(); }
            if(_campStep==CampanellaStep.Rest || _campStep==CampanellaStep.Cast ||
                _campStep==CampanellaStep.TunePrepare || _campStep==CampanellaStep.TuneActive ||
                _campStep==CampanellaStep.TuneGap || _campStep==CampanellaStep.ForcedBell ||
                _campStep==CampanellaStep.Groggy)return false;
            _campRemaining=Mathf.Max(0,_campRemaining-dt);_castRemainingTime=_campRemaining;
            switch(_campStep)
            {
                case CampanellaStep.HardFollowup:TickCampanellaFollowup();break;
                case CampanellaStep.EchoWarning:TickCampanellaEcho();break;
                case CampanellaStep.Transformation:
                    if(!_campHardReplicaSkipped && !LocalGameplayInput.ModalCovered && CampanellaHardSkipPressed())
                    {StopCampanellaHardPresentation();_campHardReplicaSkipped=true;_campRemaining=0;
                     LocalGameplayInput.SuppressThisFrame();}
                    if(_campRemaining<=0)FinishCampanellaTransformation();break;
                case CampanellaStep.LockPrepare:
                    if(_campRemaining<=0)OpenCampanellaLockCounter();break;
                case CampanellaStep.LockCounter:
                    if(_campRemaining<=0)FinishCampanellaLock(false,"탱커 카운터 미성공");break;
                case CampanellaStep.LockHold:TickCampanellaLockHold();break;
                case CampanellaStep.LockOpening:
                    _groggyRemainingTime=_campRemaining;
                    if(_campRemaining<=0)BeginCampanellaLockRecovery();break;
                case CampanellaStep.LockRecovery:
                    if(_campRemaining<=0)
                    { EndCampanella();_campRest=CampanellaHardRules.Tempo(CampTuning.patternInterval,campanellaHard.attackTempo);
                      _campHardRestProtected=true; }
                    break;
                case CampanellaStep.FinalReposition:
                    if(_campRemaining<=0)BeginCampanellaLock();break;
            }
            if(!_campStoryUsed && currentHp>0 && HpRatio<=CampTuning.storyThreshold)
            {_campStoryUsed=true;StartCoroutine(CampanellaStory());}
            return true;
        }
        private void TickCampanellaHardCast()
        {
            if(_campHardImpactShown || _campRemaining>campanellaHard.impactWarning)return;
            _campHardImpactShown=true;
            if(_campPattern==CampanellaPattern.BronzeSweep)
                CampGround(AoETelegraphType.FrontCone_120Deg,_campOrigin,
                    Vector3.Cross(Vector3.up,_campForward)*(_campRight?1:-1),_campRemaining,0,CampTuning.arenaRadius,180);
            else if(_campPattern==CampanellaPattern.SuppressedVibration)
                CampGround(AoETelegraphType.Circle_Radius6,_campOrigin,_campForward,_campRemaining,0,CampTuning.vibrationRadius);
            else if(_campPattern==CampanellaPattern.ChainRush)
                CampGround(AoETelegraphType.Rectangle,_campOrigin,_campForward,_campRemaining,CampTuning.chargeWidth,CampTuning.chargeLength);
            else if(_campPattern==CampanellaPattern.FallingWeight && _campWeightLocked)
                CampGround(AoETelegraphType.Circle_Radius6,_campTargetPoint,Vector3.forward,_campRemaining,0,CampTuning.weightRadius);
            else if(_campPattern==CampanellaPattern.Pendulum)CampanellaPendulumGround(false,_campRemaining);
        }
        private void BeginCampanellaPendulum()
        {
            _currentCastSkillName="흔들리는 종치 · 첫 경로 → 반대 경로";
            SetMechanicAlert("종치 진자 준비 · 첫 직선 뒤 이미 지나간 쪽으로 이동",Color.red,_campRemaining);
        }
        private void CampanellaPendulumGround(bool opposite,float seconds)
        {
            Vector3 side=Vector3.Cross(Vector3.up,_campForward)*(_campRight?1:-1)*(opposite?-1:1);
            CampGround(AoETelegraphType.Rectangle,_campOrigin+side*4,_campForward,seconds,
                campanellaHard.pendulumWidth,campanellaHard.pendulumLength);
        }
        private bool TryBeginCampanellaHardFollowup()
        {
            if(_campPattern!=CampanellaPattern.ChainCrush && _campPattern!=CampanellaPattern.BronzeSweep &&
                _campPattern!=CampanellaPattern.SuppressedVibration && _campPattern!=CampanellaPattern.FallingWeight &&
                _campPattern!=CampanellaPattern.Pendulum)return false;
            bool weight=_campPattern==CampanellaPattern.FallingWeight;
            ClearCampanellaAttack();
            _campStep=CampanellaStep.HardFollowup;fsmState=BossFSMState.CastingSkill;
            _campRemaining=weight?campanellaHard.lingeringWeightSeconds:
                CampanellaHardRules.Tempo(_campPattern==CampanellaPattern.Pendulum?
                    campanellaHard.pendulumFollowupSeconds:campanellaHard.followupSeconds,campanellaHard.attackTempo);
            _castTotalDuration=_castRemainingTime=_campRemaining;_campHardImpactShown=false;
            _currentCastSkillName=_campPattern==CampanellaPattern.ChainCrush?"사슬 압착 · 두 번째 강타":
                _campPattern==CampanellaPattern.BronzeSweep?"청동다리 · 반대쪽":
                _campPattern==CampanellaPattern.SuppressedVibration?"억눌린 진동 · 외곽":
                _campPattern==CampanellaPattern.Pendulum?"흔들리는 종치 · 복귀": "봉인추 잔향 · 종료 전 이탈";
            SetMechanicAlert(_currentCastSkillName,Color.red,_campRemaining);
            PlayCampanellaCue(campanellaHard.followupCue);
            if(!PlayCampanellaHardFollowupTrigger())ShowTemporaryCampanellaPose(_campPattern);
            if(weight)_campHardWeightNextPulse=RaidCombatAuthority.ServerTime+campanellaHard.lingeringWeightTickSeconds;
            if(_campPattern==CampanellaPattern.ChainCrush && _campTarget!=null)
                _campMarker=AttachedPartyMechanicMarker.SpawnTargetMarker(_campTarget,1.5f);
            if(weight)CampGround(AoETelegraphType.Circle_Radius6,_campTargetPoint,Vector3.forward,_campRemaining,0,CampTuning.weightRadius);
            return true;
        }
        private void TickCampanellaFollowup()
        {
            if(_campPattern==CampanellaPattern.FallingWeight &&
                RaidCombatAuthority.ServerTime>=_campHardWeightNextPulse)
            {
                foreach(var member in GetLivingPartyMemberTransforms())
                    if(FloraNormalRules.InCircle(member.position,_campTargetPoint,CampTuning.weightRadius))
                        DamagePartyMember(member,campanellaHard.lingeringWeightDamage,CombatDamageKind.Area,
                            "campanella.hard.weight.lingering");
                _campHardWeightNextPulse=RaidCombatAuthority.ServerTime+campanellaHard.lingeringWeightTickSeconds;
                if(StageManager.CombatSuspended)return;
            }
            if(!_campHardImpactShown && _campRemaining<=campanellaHard.impactWarning)
            {
                _campHardImpactShown=true;
                if(_campPattern==CampanellaPattern.BronzeSweep)
                    CampGround(AoETelegraphType.FrontCone_120Deg,_campOrigin,
                        Vector3.Cross(Vector3.up,_campForward)*(_campRight?-1:1),_campRemaining,0,CampTuning.arenaRadius,180);
                else if(_campPattern==CampanellaPattern.SuppressedVibration)
                    CampGround(AoETelegraphType.Donut_Inner4_Outer12,_campOrigin,_campForward,_campRemaining,
                        CampTuning.vibrationRadius,CampTuning.arenaRadius);
                else if(_campPattern==CampanellaPattern.Pendulum)CampanellaPendulumGround(true,_campRemaining);
            }
            if(_campRemaining>0)return;
            if(_campPattern==CampanellaPattern.FallingWeight){EndCampanella();return;}
            PlayCampanellaCue(campanellaHard.attackImpactCue);
            foreach(var actor in GetLivingPartyMemberTransforms())
            {
                bool hit=_campPattern==CampanellaPattern.ChainCrush?actor==_campTarget:
                    _campPattern==CampanellaPattern.BronzeSweep?FloraNormalRules.InHalf(actor.position,_campOrigin,
                        Vector3.Cross(Vector3.up,_campForward),CampTuning.arenaRadius,!_campRight):
                    _campPattern==CampanellaPattern.SuppressedVibration?
                        BattleResurrection.FlatDistance(actor.position,_campOrigin)>=CampTuning.vibrationRadius &&
                        BattleResurrection.FlatDistance(actor.position,_campOrigin)<=CampTuning.arenaRadius:
                    _campPattern==CampanellaPattern.FallingWeight?FloraNormalRules.InCircle(actor.position,_campTargetPoint,CampTuning.weightRadius):
                    _campGround.Count>0 && _campGround[0]!=null && _campGround[0].IsPointInsideXZ(actor.position);
                if(hit)DamagePartyMember(actor,_campPattern==CampanellaPattern.ChainCrush?CampTuning.tankDamage:
                    _campPattern==CampanellaPattern.BronzeSweep?CampTuning.sweepDamage:
                    _campPattern==CampanellaPattern.SuppressedVibration?CampTuning.vibrationDamage:
                    _campPattern==CampanellaPattern.FallingWeight?campanellaHard.lingeringWeightDamage:
                    campanellaHard.pendulumDamage,_campPattern==CampanellaPattern.ChainCrush?
                    CombatDamageKind.Direct:CombatDamageKind.Area,"campanella.hard.followup");
            }
            if(!StageManager.CombatSuspended)EndCampanella();
        }
        private bool PlayCampanellaHardFollowupTrigger()
        {
            var animator=_campHardModel!=null?_campHardModel.GetComponentInChildren<Animator>():
                _campModel!=null?_campModel.GetComponentInChildren<Animator>():null;
            if(animator==null || string.IsNullOrEmpty(campanellaHard.followupTrigger))return false;
            foreach(var parameter in animator.parameters)
                if(parameter.name==campanellaHard.followupTrigger && parameter.type==AnimatorControllerParameterType.Trigger)
                { animator.SetTrigger(campanellaHard.followupTrigger);return true; }
            return false;
        }
        private void BeginCampanellaEcho(int index)
        {
            _campHardEchoPlate=index;
            _campStep=CampanellaStep.EchoWarning;_campRemaining=campanellaHard.echoWarningSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName="침묵의 조율 · "+CampanellaNumber(index)+" 잔향 이탈";
            _campHardEchoLabel=FloraLabel(CampanellaNumber(index)+" ⚠ 잔향",new Color(1,.35f,.26f));
            _campHardEchoLabel.transform.position=CampanellaPlatePoint(index)+Vector3.up*.8f;
            CampGround(AoETelegraphType.Circle_Radius6,CampanellaPlatePoint(index),Vector3.forward,
                _campRemaining,0,campanellaHard.echoRadius);
            SetMechanicAlert(CampanellaNumber(index)+" 차단 성공 · 잔향 "+_campRemaining.ToString("0.0")+
                "초 후 폭발 / 다음 "+(index<3?CampanellaNumber(index+1):"종료"),Color.red,_campRemaining);
            PlayCampanellaCue(campanellaHard.echoCue);
        }
        private void TickCampanellaEcho()
        {
            if(_campHardEchoLabel!=null)
            { _campHardEchoLabel.text=CampanellaNumber(_campHardEchoPlate)+" ⚠ 잔향 "+_campRemaining.ToString("0.0")+"초";
              if(Camera.main!=null)_campHardEchoLabel.transform.rotation=Camera.main.transform.rotation; }
            if(_campRemaining>0)return;
            foreach(var actor in GetLivingPartyMemberTransforms())
                if(CampanellaNormalRules.InPlate(actor.position,CampanellaPlatePoint(_campHardEchoPlate),campanellaHard.echoRadius))
                    DamagePartyMember(actor,campanellaHard.echoDamage,CombatDamageKind.Area,"campanella.hard.echo");
            if(StageManager.CombatSuspended)return;
            ClearCampanellaAttack();
            if(_campHardEchoLabel!=null){Destroy(_campHardEchoLabel.gameObject);_campHardEchoLabel=null;}
            _campPlateStep=CampanellaNormalRules.NextStep(_campHardEchoPlate);
            _campStep=CampanellaStep.TuneGap;_campRemaining=CampTuning.tuningGapSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName="침묵의 조율 · 다음 "+(_campPlateStep<4?CampanellaNumber(_campPlateStep):"종료");
        }
        private bool FinishCampanellaHardTuning()
        {
            if(!_campHardFinalActive)return false;
            _campHardFinalActive=false;
            ClearCampanellaAttack();
            for(int i=0;i<4;i++)
            { _campAssignees[i]=null;if(_campNumberLabels[i]!=null)Destroy(_campNumberLabels[i].gameObject);_campNumberLabels[i]=null; }
            if(_campProgressLabel!=null)Destroy(_campProgressLabel.gameObject);_campProgressLabel=null;
            RaidApproachField.Instance?.SetCampanellaPlateState(-1,0,0);
            _campSuccessMask=_campFailureMask=_campMissingMask=_campPlateStep=0;
            _campStep=CampanellaStep.FinalReposition;_campRemaining=campanellaHard.finalRepositionSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName="마지막 침묵의 조율 · 재집결과 회복";
            SetMechanicAlert("잔향 종료 · 파티 회복 후 파란 원 종치 봉쇄",Color.green,_campRemaining);
            FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);
            return true;
        }
        private void BeginCampanellaTransformation()
        {
            ClearCampanellaAttack();
            _campStep=CampanellaStep.Transformation;fsmState=BossFSMState.CastingSkill;
            _campRemaining=campanellaHard.transformationSeconds;
            if(PlayCampanellaHardPresentation(0))
                _campRemaining=RaidStageCutscenePresentation.Duration(
                    campanellaHard.transformationPresentation.DirectorAsset,_campRemaining);
            _castTotalDuration=_castRemainingTime=_campRemaining;
            if(_campHardPresentation==null)ShowTemporaryCampanellaPose(CampanellaPattern.ForcedBell);
            _currentCastSkillName="봉인이 벗겨진 대종 · 변신";
            SetMechanicAlert("억제 장치 파손 · 봉인이 벗겨진 대종, 캄파넬라",new Color(.7f,.38f,.8f),_campRemaining);
            PlayCampanellaCue(campanellaHard.transformationCue);
        }
        private bool PlayCampanellaHardPresentation(float elapsed)
        {
            var template=campanellaHard.transformationPresentation;
            if(template==null){_campHardPresentationUnavailable=true;return false;}
            try
            {
                _campHardPresentation=Instantiate(template,transform.position,Quaternion.identity,transform);
                if(_campHardPresentation.Play(this,StageManager.Instance?.player,elapsed))return true;
            }
            catch(System.Exception e){Debug.LogWarning("Campanella hard transformation fallback: "+e);}
            StopCampanellaHardPresentation();_campHardPresentationUnavailable=true;return false;
        }
        private void StopCampanellaHardPresentation()
        {
            if(_campHardPresentation==null)return;
            _campHardPresentation.StopAndRestore();
            _campHardPresentation.gameObject.SetActive(false);Destroy(_campHardPresentation.gameObject);
            _campHardPresentation=null;
        }
        private bool CampanellaHardSkipPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;
            return keyboard!=null && (campanellaHard.cutsceneSkipKey==KeyCode.Escape?
                keyboard.escapeKey.wasPressedThisFrame:keyboard.spaceKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(campanellaHard.cutsceneSkipKey);
#else
            return false;
#endif
        }
        private void TickCampanellaHardReplicaInput()
        {
            if(_campHardReleaseUntil>0 && RaidCombatAuthority.ServerTime>=_campHardReleaseUntil)
            { _campHardReleaseUntil=0;ClearCampanellaLockVisuals(); }
            if(_campStep!=CampanellaStep.Transformation || _campHardReplicaSkipped || LocalGameplayInput.ModalCovered)return;
            if(!CampanellaHardSkipPressed())return;
            _campHardReplicaSkipped=true;StopCampanellaHardPresentation();LocalGameplayInput.SuppressThisFrame();
        }
        private void FinishCampanellaTransformation()
        {
            StopCampanellaHardPresentation();
            ClearCampanellaAttack();
            _campHardTransformed=true;_campHardReplicaSkipped=false;_campHardPresentationUnavailable=false;
            RestoreHardTransformationHealth();
            currentBossTitle="봉인이 벗겨진 대종, 캄파넬라";
            if(_campModel!=null)_campModel.SetActive(false);
            if(_campHardModel!=null)Destroy(_campHardModel);
            if(campanellaHard.transformedVisualPrefab!=null)
            {
                _campHardModel=Instantiate(campanellaHard.transformedVisualPrefab,transform);
                foreach(var collider in _campHardModel.GetComponentsInChildren<Collider>())collider.enabled=false;
                var animator=_campHardModel.GetComponentInChildren<Animator>();
                if(animator!=null && campanellaHard.transformedAnimatorController!=null)
                    animator.runtimeAnimatorController=campanellaHard.transformedAnimatorController;
            }
            else BuildTemporaryUnsealedBell();
            _campStep=CampanellaStep.Rest;fsmState=BossFSMState.IdleChasing;
            _campRemaining=0;_campRest=0;
            _campHardRestProtected=true;
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
        }
        private void BuildTemporaryUnsealedBell()
        {
            _campHardModel=new GameObject("Temporary unsealed bell · replace in campanellaHard");
            _campHardModel.transform.SetParent(transform,false);
            HardBellPart(PrimitiveType.Cylinder,"Split bronze shell left",new Vector3(-1.1f,2.2f,0),
                new Vector3(1.7f,2.5f,2.4f),new Color(.5f,.31f,.15f));
            HardBellPart(PrimitiveType.Cylinder,"Split bronze shell right",new Vector3(1.1f,2.2f,0),
                new Vector3(1.7f,2.5f,2.4f),new Color(.5f,.31f,.15f));
            HardBellPart(PrimitiveType.Capsule,"Exposed bell clapper",campanellaHard.bellContact,
                new Vector3(.6f,2.3f,.6f),new Color(.67f,.49f,.19f));
            HardBellPart(PrimitiveType.Cylinder,"Corrupted drive shaft",new Vector3(0,2.1f,-1.2f),
                new Vector3(1.3f,2.3f,1.3f),new Color(.4f,.15f,.47f));
            HardBellPart(PrimitiveType.Cube,"Remaining blue cloak",new Vector3(0,3.4f,.5f),
                new Vector3(2.1f,.08f,.8f),new Color(.12f,.3f,.57f));
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI/4;
                var leg=RaidPrimitive.Create(PrimitiveType.Capsule);
                leg.name="Raised clockwork spider leg "+(i+1);
                leg.transform.SetParent(_campHardModel.transform,false);
                leg.transform.localPosition=new Vector3(Mathf.Cos(angle)*2.1f,1.5f,Mathf.Sin(angle)*2.1f);
                leg.transform.localRotation=Quaternion.FromToRotation(Vector3.up,
                    new Vector3(Mathf.Cos(angle),-.35f,Mathf.Sin(angle)));
                leg.transform.localScale=new Vector3(.2f,3,.2f);
                leg.GetComponent<Collider>().enabled=false;
            }
        }
        private void HardBellPart(PrimitiveType type,string name,Vector3 local,Vector3 scale,Color color)
        {
            var obj=RaidPrimitive.Create(type);obj.name=name;obj.transform.SetParent(_campHardModel.transform,false);
            obj.transform.localPosition=local;obj.transform.localScale=scale;obj.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            obj.GetComponent<Renderer>().SetPropertyBlock(block);
        }
        private void BeginCampanellaLock()
        {
            ClearCampanellaAttack();ClearCampanellaLockVisuals();ClearCampanellaCore();
            _campStep=CampanellaStep.LockPrepare;fsmState=BossFSMState.CastingSkill;
            _campHardCoreDestroyed=false;
            _campRemaining=campanellaHard.lockPrepareSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName="🛡 탱커 집결 · 종치 봉쇄 준비";
            bool movedForLock=new Vector2(transform.position.x,transform.position.z).sqrMagnitude>.0001f;
            transform.position=new Vector3(0,transform.position.y,0);
            if(movedForLock)NotifyDamagePatternMoveEnded();
            _campOrigin=transform.position;
            _campHardCircle=AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6,_campOrigin,Vector3.forward,
                campanellaHard.lockPrepareSeconds+campanellaHard.lockCounterSeconds,0,campanellaHard.tankCircleRadius);
            _campHardCircle.SetTankGuideStyle();
            _campHardShield=FloraLabel("🛡 탱커",new Color(.3f,.7f,1));
            _campHardShield.transform.position=_campOrigin+Vector3.up*.25f;
            SetMechanicAlert("🛡 탱커 집결 · 파란 원에서 정면 ["+LocalInputBindings.Label("Counter")+"] 준비",
                new Color(.3f,.72f,1),_campRemaining);
        }
        private void OpenCampanellaLockCounter()
        {
            _campStep=CampanellaStep.LockCounter;_campRemaining=campanellaHard.lockCounterSeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            var tanks=GetLivingPartyMemberTransforms().FindAll(t=>t.GetComponent<PartyLife>()?.IsTank==true);
            if(tanks.Count>0)
            {
                Vector3 direction=tanks[0].position-transform.position;direction.y=0;
                if(direction.sqrMagnitude>.01f)transform.rotation=Quaternion.LookRotation(direction);
            }
            CombatCounterTarget.Ensure(transform).OpenWindow(_campRemaining);
            _currentCastSkillName="🛡 카운터! · 탱커 전용 종치 봉쇄";
            SetMechanicAlert(_currentCastSkillName,new Color(.4f,.85f,1),_campRemaining);
            PlayCampanellaCue(campanellaHard.counterCue);
            FloatingTextPopup.Spawn(transform.position+Vector3.up*3.8f,"🛡 카운터!",new Color(.55f,.88f,1),true);
            _campHardBossClip.Play(CampanellaHardAnimator(),campanellaHard.bellPressClip);
            UpdateCampanellaContactVisual();
        }
        private Animator CampanellaHardAnimator() => (_campHardModel!=null?_campHardModel.transform:transform).GetComponentInChildren<Animator>();
        private Transform CampanellaBellContact() => _campHardModel!=null && !string.IsNullOrEmpty(campanellaHard.bellContactPath)
            ?_campHardModel.transform.Find(campanellaHard.bellContactPath):null;
        private void UpdateCampanellaContactVisual()
        {
            if(CampanellaBellContact()!=null)return;
            if(_campHardContactVisual==null)
            {
                _campHardContactVisual=RaidPrimitive.Create(PrimitiveType.Sphere);
                _campHardContactVisual.name="Temporary clapper contact · no damage collider";
                _campHardContactVisual.GetComponent<Collider>().enabled=false;
                _campHardContactVisual.transform.localScale=Vector3.one*.65f;
            }
            _campHardContactVisual.transform.position=transform.TransformPoint(campanellaHard.bellContact);
        }
        private GuardianClashJobVisual CampanellaLockJob(Transform actor)
        {
            string id=actor!=null?actor.GetComponent<CharacterActor>()?.Definition?.id:null;
            return id==AegisPaladinData.JobId?campanellaHard.aegis:
                id=="job.berserker"?campanellaHard.berserker:
                id=="job.dragon-breaker"?campanellaHard.dragon:null;
        }
        private void CampanellaLockCounterSucceeded(Transform actor)
        {
            if(!CampanellaLockCounterAllowed(actor))return;
            CombatCounterTarget.Ensure(transform).CloseWindow();
            _campHardLastCounter=RaidCombatAuthority.ServerTime;
            _campHardTank=actor;_campStep=CampanellaStep.LockHold;
            _campRemaining=Mathf.Min(campanellaHard.lockHoldSeconds,campanellaHard.lockCoreSeconds);
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _campHardNextPulse=RaidCombatAuthority.ServerTime+campanellaHard.lockPulseInterval;
            _campHardHoldBegan=RaidCombatAuthority.ServerTime;_campHardHoldClipStarted=false;
            _campHardShockwaveWarned=false;
            Vector3 facing=transform.position-actor.position;facing.y=0;
            if(facing.sqrMagnitude>.01f)actor.rotation=Quaternion.LookRotation(facing);
            _campHardJob=CampanellaLockJob(actor);
            if(_campHardJob!=null)
            {
                _campHardWeapon=string.IsNullOrEmpty(_campHardJob.weaponContactPath)?null:actor.Find(_campHardJob.weaponContactPath);
                _campHardVisualRoot=string.IsNullOrEmpty(_campHardJob.visualRootPath)?null:actor.Find(_campHardJob.visualRootPath);
                if(_campHardVisualRoot==null)_campHardVisualRoot=_campHardWeapon?.parent;
                if(_campHardVisualRoot!=null)
                { _campHardVisualPosition=_campHardVisualRoot.localPosition;_campHardVisualRotation=_campHardVisualRoot.localRotation; }
                if(_campHardWeapon!=null)
                { _campHardWeaponRotation=_campHardWeapon.localRotation;_campHardWeaponActive=_campHardWeapon.gameObject.activeSelf;
                  if(_campHardJob.requireWeaponVisible)_campHardWeapon.gameObject.SetActive(true); }
                _campHardActorClip.Play(actor.GetComponentInChildren<Animator>(),_campHardJob.catchClip);
                if(_campHardVisualRoot!=null)
                {
                    _campHardVisualRoot.localPosition=_campHardVisualPosition+_campHardJob.visualOffset;
                    _campHardVisualRoot.localRotation=_campHardVisualRotation*Quaternion.Euler(_campHardJob.visualEuler);
                }
                if(_campHardWeapon!=null && _campHardJob.catchClip==null)
                    _campHardWeapon.localRotation=_campHardWeaponRotation*Quaternion.Euler(_campHardJob.fallbackWeaponEuler);
                AlignCampanellaLockContact();
                PlayCampanellaJobCue(_campHardJob.catchSound,_campHardJob.catchVfx);
            }
            _campHardBossClip.Play(CampanellaHardAnimator(),campanellaHard.bellHoldClip);
            ClearCampanellaLockGuide();SpawnCampanellaCore();
            _currentCastSkillName="🛡 종치 버티기 · 구동부 파괴 / 다음 맥동 "+campanellaHard.lockPulseInterval.ToString("0.0")+"초";
            SetMechanicAlert(_currentCastSkillName,new Color(.4f,.78f,1),_campRemaining);
            CombatMetricsManager.Event("종치 봉쇄 탱커 카운터 성공: "+actor.name);
        }
        private void AlignCampanellaLockContact()
        {
            if(_campHardTank==null || _campHardJob==null || _campHardVisualRoot==null)return;
            Vector3 anchor=CampanellaBellContact()!=null?CampanellaBellContact().position:
                transform.TransformPoint(campanellaHard.bellContact);
            Vector3 contact=_campHardWeapon!=null?_campHardWeapon.TransformPoint(_campHardJob.contactLocalOffset):
                _campHardTank.position+_campHardTank.forward*.6f+Vector3.up*1.5f;
            Vector3 delta=Vector3.ClampMagnitude(anchor-contact,_campHardJob.maxContactCorrection);
            _campHardVisualRoot.localPosition+=_campHardVisualRoot.parent.InverseTransformVector(delta);
            if(_campHardContactVisual!=null)_campHardContactVisual.transform.position=anchor;
        }
        private void SpawnCampanellaCore()
        {
            var spawner=GetComponent<BossAddSpawner>()??gameObject.AddComponent<BossAddSpawner>();spawner.boss=this;
            Vector3 point=CombatRange.ClampToArena(transform.TransformPoint(campanellaHard.coreOffset),CampTuning.arenaRadius-3);
            _campHardCore=new GameObject("Campanella exposed drive core").AddComponent<BossAddMinion>();
            _campHardCore.InitializeAdd(AddMinionType.CampanellaGearCore,this,spawner,point,EncounterTemplate);
            spawner.activeAdds.Add(_campHardCore);
        }
        public void NotifyCampanellaCoreDestroyed(BossAddMinion core)
        {
            if(!CampanellaHard || _campStep!=CampanellaStep.LockHold || core!=_campHardCore || _campHardCoreDestroyed)return;
            _campHardCoreDestroyed=true;
            CombatMetricsManager.Event("종치 봉쇄 구동부 파괴");
            _campRemaining=1.5f;_castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName="종치 봉쇄 성공 · 해제 충격파 1.5초";
            SetMechanicAlert("구동부 파괴 · 해제 충격파 1.5초 후 / 파티 체력 준비",Color.yellow,1.5f);
        }
        private void TickCampanellaLockHold()
        {
            if(!CombatRange.LivingPartyMember(_campHardTank))
            {FinishCampanellaLock(false,"종치 봉쇄 중 탱커 사망");return;}
            if(_campHardCoreDestroyed)
            { if(_campRemaining<=0)FinishCampanellaLock(true,"구동부 파괴 · 종치 봉쇄 성공");return; }
            if(!_campHardHoldClipStarted && RaidCombatAuthority.ServerTime-_campHardHoldBegan>=
                Mathf.Max(.3f,_campHardJob?.catchClip?.length??.3f))
            { _campHardHoldClipStarted=true;_campHardActorClip.Play(_campHardTank.GetComponentInChildren<Animator>(),_campHardJob?.holdClip); }
            if(_campHardHoldClipStarted)_campHardActorClip.Loop(_campHardJob?.holdClip);
            _campHardBossClip.Loop(campanellaHard.bellHoldClip);
            if(RaidCombatAuthority.ServerTime>=_campHardNextPulse)
            {
                DamagePartyMember(_campHardTank,campanellaHard.lockPulseDamage,CombatDamageKind.Direct,"campanella.hard.lock.pulse");
                CombatMetricsManager.Event("종치 봉쇄 맥동 피격: "+_campHardTank.name);
                PlayCampanellaCue(campanellaHard.pulseCue);
                _campHardNextPulse=RaidCombatAuthority.ServerTime+campanellaHard.lockPulseInterval;
                if(!CombatRange.LivingPartyMember(_campHardTank))
                {FinishCampanellaLock(false,"종치 봉쇄 중 탱커 사망");return;}
            }
            if(!_campHardShockwaveWarned && _campRemaining<=1.5f)
            { _campHardShockwaveWarned=true;SetMechanicAlert("종치 해제 충격파 임박 · 파티 체력 준비",Color.yellow,1.5f); }
            _currentCastSkillName="🛡 종치 버티기 "+_campRemaining.ToString("0.0")+"초 · 다음 맥동 "+
                Mathf.Max(0,_campHardNextPulse-RaidCombatAuthority.ServerTime).ToString("0.0")+"초 · 구동부 "+
                (_campHardCore!=null?_campHardCore.currentHp.ToString("0"):"0")+" HP";
            if(_campRemaining<=0)FinishCampanellaLock(false,"종치 봉쇄 제한시간 초과");
        }
        private void FinishCampanellaLock(bool success,string reason)
        {
            if(_campStep!=CampanellaStep.LockCounter && _campStep!=CampanellaStep.LockHold)return;
            CombatCounterTarget.Ensure(transform).CloseWindow();
            ClearCampanellaLockGuide();ClearCampanellaCore();
            CombatMetricsManager.Event(reason);
            PlayCampanellaCue(success?campanellaHard.releaseCue:campanellaHard.failureCue);
            // Exactly one host-authoritative release/failure shockwave.
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                DamagePartyMember(member,success?campanellaHard.lockSuccessShockwave:
                    campanellaHard.lockFailureShockwave,CombatDamageKind.Area,
                    success?"campanella.hard.lock.release":"campanella.hard.lock.failure");
                CombatMetricsManager.Event("종치 충격파 피격: "+member.name);
                if(!success)
                {
                    var player=member.GetComponent<PlayerController>();
                    if(player!=null)player.StartKnockbackXZ(transform.position,3);
                    else member.GetComponent<PartyBotAI>()?.StartKnockbackXZ(transform.position,3);
                }
            }
            _campHardActorClip.Play(_campHardTank!=null?_campHardTank.GetComponentInChildren<Animator>():null,
                success?_campHardJob?.releaseClip:_campHardJob?.interruptClip);
            _campHardBossClip.Play(CampanellaHardAnimator(),success?campanellaHard.bellReleaseClip:campanellaHard.bellInterruptClip);
            if(success && _campHardJob!=null)PlayCampanellaJobCue(_campHardJob.releaseSound,_campHardJob.releaseVfx);
            _campHardReleaseUntil=RaidCombatAuthority.ServerTime+.55f;
            _campHardFirstLockDone=true;
            if(StageManager.CombatSuspended || currentHp<=0)return;
            _campStep=success?CampanellaStep.LockOpening:CampanellaStep.LockRecovery;
            fsmState=success?BossFSMState.Groggy:BossFSMState.CastingSkill;
            _campRemaining=success?campanellaHard.lockGroggySeconds:campanellaHard.lockRecoverySeconds;
            _groggyRemainingTime=success?_campRemaining:0;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName=success?"종치 재봉쇄 · 공격 기회":"종치 봉쇄 실패 · 회복과 재정렬";
            SetMechanicAlert(_currentCastSkillName,success?Color.yellow:Color.red,_campRemaining);
        }
        private void BeginCampanellaLockRecovery()
        {
            _campStep=CampanellaStep.LockRecovery;fsmState=BossFSMState.CastingSkill;
            _campRemaining=campanellaHard.lockRecoverySeconds;
            _castTotalDuration=_castRemainingTime=_campRemaining;
            _currentCastSkillName="종치 해제 충격파 후 회복";
            SetMechanicAlert(_currentCastSkillName,Color.green,_campRemaining);
        }
        private void ClearCampanellaCore()
        {
            if(_campHardCore==null)return;
            var spawner=GetComponent<BossAddSpawner>();if(spawner!=null)spawner.activeAdds.Remove(_campHardCore);
            _campHardCore.gameObject.SetActive(false);Destroy(_campHardCore.gameObject);_campHardCore=null;
        }
        private void ClearCampanellaLockGuide()
        {
            if(_campHardCircle!=null){_campHardCircle.gameObject.SetActive(false);Destroy(_campHardCircle.gameObject);_campHardCircle=null;}
            if(_campHardShield!=null){Destroy(_campHardShield.gameObject);_campHardShield=null;}
            if(_campHardContactVisual!=null){Destroy(_campHardContactVisual);_campHardContactVisual=null;}
        }
        private void ClearCampanellaLockVisuals()
        {
            _campHardReleaseUntil=0;
            ClearCampanellaLockGuide();_campHardActorClip.Stop();_campHardBossClip.Stop();
            if(_campHardVisualRoot!=null)
            { _campHardVisualRoot.localPosition=_campHardVisualPosition;_campHardVisualRoot.localRotation=_campHardVisualRotation; }
            if(_campHardWeapon!=null)
            { _campHardWeapon.localRotation=_campHardWeaponRotation;_campHardWeapon.gameObject.SetActive(_campHardWeaponActive); }
            _campHardVisualRoot=_campHardWeapon=_campHardTank=null;_campHardJob=null;
        }
        private void PlayCampanellaJobCue(AudioClip sound,GameObject effect)
        {
            PlayCampanellaCue(sound);
            if(effect==null || _campHardTank==null)return;
            Vector3 point=_campHardWeapon!=null?_campHardWeapon.TransformPoint(_campHardJob.contactLocalOffset):
                _campHardTank.position+Vector3.up*1.5f;
            Destroy(Instantiate(effect,point,Quaternion.identity),2);
        }
        private bool CampanellaHardDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;
            if(_campStep==CampanellaStep.EchoWarning)
            {
                if(_campAssignees[_campHardEchoPlate]!=actor)return false;
                Vector3 delta=actor.position-CampanellaPlatePoint(_campHardEchoPlate);delta.y=0;
                if(delta.sqrMagnitude<.01f)delta=CampanellaPlatePoint(_campHardEchoPlate)-transform.position;
                destination=CombatRange.ClampToArena(CampanellaPlatePoint(_campHardEchoPlate)+
                    delta.normalized*(campanellaHard.echoRadius+1.5f),CampTuning.arenaRadius-1);
                destination.y=actor.position.y;return true;
            }
            if(_campStep==CampanellaStep.LockPrepare || _campStep==CampanellaStep.LockCounter)
            {
                if(role!=1)return false;
                destination=transform.position+transform.forward*2.2f;destination.y=actor.position.y;return true;
            }
            if(_campStep==CampanellaStep.LockHold)
            {
                if(role==1)return false;
                if(_campHardCore!=null){destination=CombatRange.AttackPosition(actor,_campHardCore.transform,
                    Vector3.forward,role==4?8:2.5f);destination.y=actor.position.y;return true;}
            }
            if(_campStep==CampanellaStep.HardFollowup)
            {
                if(_campPattern==CampanellaPattern.SuppressedVibration)
                {destination=_campOrigin+(actor.position-_campOrigin).normalized*3;destination.y=actor.position.y;return true;}
                if(_campPattern==CampanellaPattern.BronzeSweep)
                {destination=_campOrigin+Vector3.Cross(Vector3.up,_campForward)*(_campRight?8:-8);destination.y=actor.position.y;return true;}
                if(_campPattern==CampanellaPattern.Pendulum)
                {destination=_campOrigin+Vector3.Cross(Vector3.up,_campForward)*(_campRight?4:-4);destination.y=actor.position.y;return true;}
            }
            if(_campStep==CampanellaStep.Cast && _campPattern==CampanellaPattern.Pendulum)
            {destination=_campOrigin+Vector3.Cross(Vector3.up,_campForward)*(_campRight?-4:4);destination.y=actor.position.y;return true;}
            return false;
        }
        private void ApplyCampanellaHardReplica(RaidCombatEnemy state,CampanellaStep previous)
        {
            var next=(CampanellaStep)state.campStep;
            if(next==CampanellaStep.Transformation && previous!=CampanellaStep.Transformation &&
                !_campHardReplicaSkipped && !_campHardPresentationUnavailable)
            { if(!PlayCampanellaHardPresentation(Mathf.Max(0,state.castTotal-state.castRemaining)))
                  ShowTemporaryCampanellaPose(CampanellaPattern.ForcedBell); }
            if(next!=CampanellaStep.Transformation && previous==CampanellaStep.Transformation)
            {StopCampanellaHardPresentation();_campHardReplicaSkipped=false;}
            if(state.campHardTransformed && !_campHardTransformed)FinishCampanellaTransformation();
            if(!state.campHardTransformed && _campHardTransformed)
            {
                StopCampanellaHardPresentation();
                if(_campHardModel!=null)Destroy(_campHardModel);_campHardModel=null;
                if(_campModel!=null)_campModel.SetActive(true);
                _campHardTransformed=false;
            }
            _campHardFirstLockDone=state.campHardFirstLockDone;
            _campHardFinalDone=state.campHardFinalDone;
            Transform tank=RaidCombatAuthority.Resolve(state.campHardTank);
            if(next==CampanellaStep.EchoWarning)
            {
                if(_campHardEchoLabel==null)_campHardEchoLabel=FloraLabel("",new Color(1,.35f,.26f));
                _campHardEchoLabel.text=CampanellaNumber(state.campHardEchoPlate)+" ⚠ 잔향 "+state.castRemaining.ToString("0.0")+"초";
                if(state.campPlateX!=null && state.campPlateZ!=null && state.campPlateX.Length>state.campHardEchoPlate &&
                    state.campPlateZ.Length>state.campHardEchoPlate)
                    _campHardEchoLabel.transform.position=new Vector3(state.campPlateX[state.campHardEchoPlate],.8f,
                        state.campPlateZ[state.campHardEchoPlate]);
                if(Camera.main!=null)_campHardEchoLabel.transform.rotation=Camera.main.transform.rotation;
            }
            else if(_campHardEchoLabel!=null){Destroy(_campHardEchoLabel.gameObject);_campHardEchoLabel=null;}
            if((next==CampanellaStep.LockPrepare || next==CampanellaStep.LockCounter) && _campHardShield==null)
                _campHardShield=FloraLabel("🛡 탱커 집결",new Color(.3f,.7f,1));
            if(_campHardShield!=null && (next==CampanellaStep.LockPrepare || next==CampanellaStep.LockCounter))
            { _campHardShield.text=next==CampanellaStep.LockCounter?"🛡 카운터!":"🛡 탱커 집결";
              _campHardShield.transform.position=new Vector3(state.x,state.y+.25f,state.z); }
            if(next!=CampanellaStep.LockPrepare && next!=CampanellaStep.LockCounter && _campHardShield!=null)
            {Destroy(_campHardShield.gameObject);_campHardShield=null;}
            if(next==CampanellaStep.LockCounter && previous!=CampanellaStep.LockCounter)
            {
                PlayCampanellaCue(campanellaHard.counterCue);
                _campHardBossClip.Play(CampanellaHardAnimator(),campanellaHard.bellPressClip);
                UpdateCampanellaContactVisual();
            }
            if(next==CampanellaStep.LockHold && (previous!=CampanellaStep.LockHold || tank!=_campHardTank))
            {
                ClearCampanellaLockVisuals();_campHardTank=tank;
                PlayCampanellaHoldVisual(tank);
                _campHardBossClip.Play(CampanellaHardAnimator(),campanellaHard.bellHoldClip);
                _campHardHoldClipStarted=false;
            }
            if(next==CampanellaStep.LockHold)
            {
                if(!_campHardHoldClipStarted && state.castTotal-state.castRemaining>=
                    Mathf.Max(.3f,_campHardJob?.catchClip?.length??.3f))
                { _campHardHoldClipStarted=true;_campHardActorClip.Play(tank!=null?tank.GetComponentInChildren<Animator>():null,_campHardJob?.holdClip); }
                if(_campHardHoldClipStarted)_campHardActorClip.Loop(_campHardJob?.holdClip);
                _campHardBossClip.Loop(campanellaHard.bellHoldClip);
            }
            if(previous==CampanellaStep.LockHold && next!=CampanellaStep.LockHold)
            {
                bool success=next==CampanellaStep.LockOpening;
                _campHardActorClip.Play(_campHardTank!=null?_campHardTank.GetComponentInChildren<Animator>():null,
                    success?_campHardJob?.releaseClip:_campHardJob?.interruptClip);
                _campHardBossClip.Play(CampanellaHardAnimator(),success?campanellaHard.bellReleaseClip:campanellaHard.bellInterruptClip);
                _campHardReleaseUntil=RaidCombatAuthority.ServerTime+.55f;
                PlayCampanellaCue(success?campanellaHard.releaseCue:campanellaHard.failureCue);
            }
            if(next!=CampanellaStep.LockCounter && next!=CampanellaStep.LockHold &&
                _campHardContactVisual!=null){Destroy(_campHardContactVisual);_campHardContactVisual=null;}
        }
        private void PlayCampanellaHoldVisual(Transform actor)
        {
            if(actor==null)return;
            _campHardJob=CampanellaLockJob(actor);
            if(_campHardJob==null)return;
            _campHardWeapon=string.IsNullOrEmpty(_campHardJob.weaponContactPath)?null:actor.Find(_campHardJob.weaponContactPath);
            _campHardVisualRoot=string.IsNullOrEmpty(_campHardJob.visualRootPath)?null:actor.Find(_campHardJob.visualRootPath);
            if(_campHardVisualRoot==null)_campHardVisualRoot=_campHardWeapon?.parent;
            if(_campHardVisualRoot!=null)
            { _campHardVisualPosition=_campHardVisualRoot.localPosition;_campHardVisualRotation=_campHardVisualRoot.localRotation; }
            if(_campHardWeapon!=null)
            { _campHardWeaponRotation=_campHardWeapon.localRotation;_campHardWeaponActive=_campHardWeapon.gameObject.activeSelf;
              if(_campHardJob.requireWeaponVisible)_campHardWeapon.gameObject.SetActive(true); }
            _campHardActorClip.Play(actor.GetComponentInChildren<Animator>(),_campHardJob.catchClip);
            if(_campHardVisualRoot!=null)
            { _campHardVisualRoot.localPosition=_campHardVisualPosition+_campHardJob.visualOffset;
              _campHardVisualRoot.localRotation=_campHardVisualRotation*Quaternion.Euler(_campHardJob.visualEuler); }
            if(_campHardWeapon!=null && _campHardJob.catchClip==null)
                _campHardWeapon.localRotation=_campHardWeaponRotation*Quaternion.Euler(_campHardJob.fallbackWeaponEuler);
            AlignCampanellaLockContact();
            PlayCampanellaJobCue(_campHardJob.catchSound,_campHardJob.catchVfx);
        }
    }
}
