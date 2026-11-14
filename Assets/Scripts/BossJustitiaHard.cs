using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public partial class BossController
    {
        private bool _justHardTransformed,_justHardIntroDone,_justHardClimaxStarted,_justHardImpactShown,
            _justHardResidueApplied,_justHardWeightActive,_justHardWeightArrived,_justHardReplicaSkipped;
        private JustitiaHardBalanceKind _justHardBalanceKind;
        private JustitiaHardNext _justHardNext;
        private JustitiaPattern _justHardLastPattern,_justHardFollowupPattern;
        private readonly List<JustitiaPattern> _justHardBag=new List<JustitiaPattern>();
        private readonly List<Vector3> _justHardResiduePoints=new List<Vector3>();
        private readonly List<GameObject> _justHardMarkGuides=new List<GameObject>();
        private readonly Dictionary<Transform,int> _justHardPreviousMass=new Dictionary<Transform,int>();
        private int _justHardIntroduced,_justHardBalanceRound,_justHardWeightSide;
        private float _justHardNextPulse,_justHardWeightElapsed;
        private GameObject _justHardModel,_justHardWeightVisual,_justHardTransitionVisual;
        private TextMesh _justHardWeightLabel;
        private RaidStageCutscenePresentation _justHardPresentation;
        public bool JustitiaHardTransformationActive => JustitiaHard&&_justStep==JustitiaStep.HardTransformation;

        private void ResetJustitiaHard()
        {
            _justHardTransformed=_justHardIntroDone=_justHardClimaxStarted=false;
            _justHardImpactShown=_justHardResidueApplied=_justHardWeightActive=_justHardWeightArrived=false;
            _justHardReplicaSkipped=false;_justHardBalanceKind=JustitiaHardBalanceKind.None;
            _justHardNext=JustitiaHardNext.None;_justHardLastPattern=JustitiaPattern.None;
            _justHardIntroduced=_justHardBalanceRound=_justHardWeightSide=0;
            _justHardBag.Clear();_justHardResiduePoints.Clear();_justHardPreviousMass.Clear();_justHardNextPulse=float.PositiveInfinity;
        }
        private void ClearJustitiaHard(bool keepModel)
        {
            StopJustitiaHardPresentation();ClearJustitiaHardWeight();_justHardResiduePoints.Clear();
            if(_justHardTransitionVisual!=null)Destroy(_justHardTransitionVisual);_justHardTransitionVisual=null;
            if(_justHardModel!=null&&!keepModel){Destroy(_justHardModel);_justHardModel=null;}
            if(!keepModel)_justHardTransformed=false;
            _justHardBag.Clear();_justHardPreviousMass.Clear();_justHardWeightActive=false;_justHardNext=JustitiaHardNext.None;
            _justHardNextPulse=float.PositiveInfinity;_justHardReplicaSkipped=false;
        }
        private JustitiaPattern NextJustitiaHardPattern()
        {
            if(_justHardIntroduced<2)
            {var intro=_justHardIntroduced++==0?JustitiaPattern.DoubleEdged:JustitiaPattern.EvidenceErasure;
             _justHardLastPattern=intro;return intro;}
            JustitiaPattern[] pool={JustitiaPattern.Execution,JustitiaPattern.HalfSlash,JustitiaPattern.JudgmentMarks,
                JustitiaPattern.ScaleVibration,JustitiaPattern.SharedGuilt,JustitiaPattern.Charge,
                JustitiaPattern.Balance,JustitiaPattern.DoubleEdged,JustitiaPattern.EvidenceErasure};
            if(_justHardBag.Count==0)
            {
                for(int i=0;i<pool.Length;i++)
                {int count=justitiaHard.patternWeights!=null&&i<justitiaHard.patternWeights.Length?
                    Mathf.Clamp(justitiaHard.patternWeights[i],1,4):1;
                 for(int n=0;n<count;n++)_justHardBag.Add(pool[i]);}
                for(int i=_justHardBag.Count-1;i>0;i--)
                {int j=Random.Range(0,i+1);var temp=_justHardBag[i];_justHardBag[i]=_justHardBag[j];_justHardBag[j]=temp;}
            }
            int selected=_justHardBag.FindIndex(p=>p!=_justHardLastPattern&&
                (p!=JustitiaPattern.Balance||GetLivingPartyMemberTransforms().Count>=2));
            if(selected<0){_justHardBag.Clear();return JustitiaPattern.Execution;}
            var result=_justHardBag[selected];_justHardBag.RemoveAt(selected);
            _justHardLastPattern=result;
            if(result==JustitiaPattern.Balance)
            {_justHardBalanceKind=JustitiaHardBalanceKind.Remeasure;_justHardBalanceRound=1;_justHardWeightActive=false;}
            return result;
        }
        private bool TryBeginJustitiaHardSpecial()
        {
            var due=JustitiaHardRules.Due(_justHardTransformed,_justHardIntroDone,_justHardClimaxStarted,
                HpRatio,justitiaHard.transformationThreshold,justitiaHard.climaxThreshold);
            if(due==JustitiaHardSpecial.Transformation)
            {BeginJustitiaHardTransformation();return true;}
            if(due==JustitiaHardSpecial.WeightIntro)
            {StartJustitiaHardWeight(JustitiaHardBalanceKind.WeightIntro);return true;}
            if(due==JustitiaHardSpecial.ClimaxWeight)
            {_justHardClimaxStarted=true;StartJustitiaHardWeight(JustitiaHardBalanceKind.ClimaxWeight);return true;}
            return false;
        }
        private bool TickJustitiaHard(float dt)
        {
            if(_justStep==JustitiaStep.HardTransformation)
            {
                _justRemaining=Mathf.Max(0,_justRemaining-dt);_castRemainingTime=_justRemaining;
                if(_justHardTransitionVisual!=null)
                {_justHardTransitionVisual.transform.Rotate(0,90*dt,0);
                 _justHardTransitionVisual.transform.localScale=Vector3.one*(1.5f+Mathf.PingPong(Time.time,1));}
                if(JustitiaHardSkipPressed()&&!LocalGameplayInput.ModalCovered)
                {_justRemaining=0;LocalGameplayInput.SuppressThisFrame();}
                if(_justRemaining<=0)FinishJustitiaHardTransformation();
                return true;
            }
            if(_justStep==JustitiaStep.HardRegroup)
            {
                _justRemaining=Mathf.Max(0,_justRemaining-dt);_castRemainingTime=_justRemaining;
                if(_justRemaining<=0)
                {
                    var next=_justHardNext;_justHardNext=JustitiaHardNext.None;
                    if(next==JustitiaHardNext.SecondBalance)
                    {_justHardBalanceRound=2;_justPattern=JustitiaPattern.Balance;BeginJustitiaBalance();}
                    else if(next==JustitiaHardNext.Verdict)
                    {_justHardWeightActive=false;ClearJustitiaHardWeight();BeginJustitia(JustitiaPattern.Verdict);}
                    else EndJustitia(false,0,justitiaHard.balanceRecoverySeconds);
                }
                return true;
            }
            if(_justStep==JustitiaStep.HardFollowup)
            {
                _justRemaining=Mathf.Max(0,_justRemaining-dt);_castRemainingTime=_justRemaining;
                if(!_justHardImpactShown&&_justRemaining<=justitiaHard.impactWarning)
                {_justHardImpactShown=true;ShowJustitiaHardFollowupGround();}
                if(_justRemaining<=0)ResolveJustitiaHardFollowup();
                return true;
            }
            if(_justStep==JustitiaStep.HardResidue)
            {
                _justRemaining=Mathf.Max(0,_justRemaining-dt);_castRemainingTime=_justRemaining;
                if(!_justHardResidueApplied&&_justRemaining<=justitiaHard.residueSeconds*.5f)
                {
                    _justHardResidueApplied=true;
                    foreach(var member in GetLivingPartyMemberTransforms())foreach(var point in _justHardResiduePoints)
                        if(FloraNormalRules.InCircle(member.position,point,justitiaHard.residueRadius))
                        {DamagePartyMember(member,justitiaHard.residueDamage,CombatDamageKind.Area,"justitia.hard.mark.residue");break;}
                }
                if(_justRemaining<=0){_justHardResiduePoints.Clear();EndJustitia(false,0,justitiaHard.recoverySeconds);}
                return true;
            }
            return false;
        }
        private void StartJustitiaHardCast(JustitiaPattern pattern)
        {
            _justHardImpactShown=false;
        }
        private void TickJustitiaHardCast()
        {
            if(_justHardImpactShown||_justRemaining>justitiaHard.impactWarning)return;
            _justHardImpactShown=true;
            switch(_justPattern)
            {
                case JustitiaPattern.HalfSlash:
                    JustitiaGround(AoETelegraphType.FrontCone_120Deg,_justOrigin,
                        Vector3.Cross(Vector3.up,_justForward)*(_justRight?1:-1),_justRemaining,0,JustTuning.halfWidth,180);break;
                case JustitiaPattern.ScaleVibration:
                    JustitiaGround(AoETelegraphType.Circle_Radius6,_justOrigin,_justForward,_justRemaining,0,JustTuning.centerRadius);break;
                case JustitiaPattern.Charge:
                    JustitiaGround(AoETelegraphType.Rectangle,_justOrigin,_justForward,_justRemaining,JustTuning.chargeWidth,JustTuning.chargeLength);break;
                case JustitiaPattern.DoubleEdged:
                    JustitiaGround(AoETelegraphType.FrontCone_120Deg,_justOrigin,_justForward,_justRemaining,0,justitiaHard.doubleEdgeRadius,180);break;
                case JustitiaPattern.EvidenceErasure:ShowJustitiaHardEvidenceGround(_justRight,_justRemaining);break;
                case JustitiaPattern.JudgmentMarks:
                    foreach(var point in _justMarkPoints)JustitiaGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,
                        _justRemaining,0,JustTuning.markRadius);break;
            }
        }
        private void ShowJustitiaHardMarkGuide(Vector3 point)
        {
            var guide=RaidPrimitive.Create(PrimitiveType.Cylinder);
            guide.name="낙인 고정 위치 · 금색 안내";
            guide.transform.position=new Vector3(point.x,.04f,point.z);
            guide.transform.localScale=new Vector3(JustTuning.markRadius*2,.012f,JustTuning.markRadius*2);
            guide.GetComponent<Collider>().enabled=false;
            var color=new MaterialPropertyBlock();color.SetColor("_BaseColor",new Color(1,.75f,.2f,.3f));
            color.SetColor("_Color",new Color(1,.75f,.2f,.3f));guide.GetComponent<Renderer>().SetPropertyBlock(color);
            _justHardMarkGuides.Add(guide);
        }
        private void ClearJustitiaHardGuides()
        {foreach(var guide in _justHardMarkGuides)if(guide!=null)Destroy(guide);_justHardMarkGuides.Clear();}
        private void ShowJustitiaHardEvidenceGround(bool positiveZ,float duration)
        {
            for(int i=0;i<2;i++)JustitiaGround(AoETelegraphType.Rectangle,
                new Vector3((i==0?-1:1)*JustTuning.halfWidth*.5f,0,0),
                positiveZ?Vector3.forward:Vector3.back,duration,JustTuning.halfWidth,JustTuning.halfDepth);
        }
        private bool JustitiaHardFirstHit(Vector3 point)
        {
            if(_justPattern==JustitiaPattern.DoubleEdged)
                return FloraNormalRules.InHalf(point,_justOrigin,_justForward,justitiaHard.doubleEdgeRadius,true);
            if(_justPattern==JustitiaPattern.EvidenceErasure)
                return Mathf.Abs(point.x)<=JustTuning.halfWidth&&Mathf.Abs(point.z)<=JustTuning.halfDepth&&
                    (point.z>0)==_justRight;
            return false;
        }
        private bool JustitiaHardChargeHit(Vector3 point)
        {
            Vector3 delta=point-_justOrigin;
            Vector3 forward=new Vector3(_justForward.x,0,_justForward.z).normalized;
            Vector3 right=Vector3.Cross(Vector3.up,forward);
            return GuardianNormalState.InRectangle(Vector3.Dot(delta,right),Vector3.Dot(delta,forward),
                JustTuning.chargeWidth,JustTuning.chargeLength);
        }
        private bool BeginJustitiaHardFollowup()
        {
            var pattern=_justPattern;
            if(pattern==JustitiaPattern.JudgmentMarks)
            {
                _justHardResiduePoints.Clear();_justHardResiduePoints.AddRange(_justMarkPoints);
                ClearJustitiaAttack();
                foreach(var point in _justHardResiduePoints)
                    JustitiaGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,justitiaHard.residueSeconds,0,justitiaHard.residueRadius);
                _justStep=JustitiaStep.HardResidue;_justRemaining=justitiaHard.residueSeconds;
                _castTotalDuration=_castRemainingTime=_justRemaining;_justHardResidueApplied=false;
                _currentCastSkillName="판결의 낙인 · 잔류 위험";
                SetMechanicAlert("낙인 잔류 위험 · 표시 범위 이탈",Color.red,_justRemaining);
                return true;
            }
            if(pattern!=JustitiaPattern.Execution&&pattern!=JustitiaPattern.HalfSlash&&
                pattern!=JustitiaPattern.ScaleVibration&&pattern!=JustitiaPattern.DoubleEdged&&
                pattern!=JustitiaPattern.EvidenceErasure)return false;
            ClearJustitiaAttack();_justHardFollowupPattern=pattern;
            _justStep=JustitiaStep.HardFollowup;fsmState=BossFSMState.CastingSkill;
            _justRemaining=pattern==JustitiaPattern.Execution?justitiaHard.doubleHitGap:
                pattern==JustitiaPattern.HalfSlash?justitiaHard.halfGap:
                pattern==JustitiaPattern.ScaleVibration?justitiaHard.ringGap:
                pattern==JustitiaPattern.DoubleEdged?justitiaHard.doubleEdgeGap:justitiaHard.evidenceGap;
            _castTotalDuration=_castRemainingTime=_justRemaining;_justHardImpactShown=false;
            _currentCastSkillName=pattern==JustitiaPattern.Execution?"집행자의 일격 · 2/2":
                pattern==JustitiaPattern.HalfSlash?"편향된 참격 · 반대쪽":
                pattern==JustitiaPattern.ScaleVibration?"천칭의 진동 · 외곽 도넛":
                pattern==JustitiaPattern.DoubleEdged?"양날 판결 · 후방": "증거 말소 · II";
            SetMechanicAlert(_currentCastSkillName+" · 후속 타격 준비",Color.red,_justRemaining);
            return true;
        }
        private void ShowJustitiaHardFollowupGround()
        {
            switch(_justHardFollowupPattern)
            {
                case JustitiaPattern.HalfSlash:
                    JustitiaGround(AoETelegraphType.FrontCone_120Deg,_justOrigin,
                        Vector3.Cross(Vector3.up,_justForward)*(_justRight?-1:1),_justRemaining,0,JustTuning.halfWidth,180);break;
                case JustitiaPattern.ScaleVibration:
                    JustitiaGround(AoETelegraphType.Donut_Inner4_Outer12,_justOrigin,_justForward,_justRemaining,
                        JustTuning.centerRadius,justitiaHard.ringOuterRadius);break;
                case JustitiaPattern.DoubleEdged:
                    JustitiaGround(AoETelegraphType.FrontCone_120Deg,_justOrigin,-_justForward,_justRemaining,0,justitiaHard.doubleEdgeRadius,180);break;
                case JustitiaPattern.EvidenceErasure:ShowJustitiaHardEvidenceGround(!_justRight,_justRemaining);break;
            }
        }
        private void ResolveJustitiaHardFollowup()
        {
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                bool hit=_justHardFollowupPattern==JustitiaPattern.Execution?member==_justTarget:
                    _justHardFollowupPattern==JustitiaPattern.HalfSlash?
                        FloraNormalRules.InHalf(member.position,_justOrigin,Vector3.Cross(Vector3.up,_justForward),JustTuning.halfWidth,!_justRight):
                    _justHardFollowupPattern==JustitiaPattern.ScaleVibration?
                        !FloraNormalRules.InCircle(member.position,_justOrigin,JustTuning.centerRadius)&&
                        FloraNormalRules.InCircle(member.position,_justOrigin,justitiaHard.ringOuterRadius):
                    _justHardFollowupPattern==JustitiaPattern.DoubleEdged?
                        FloraNormalRules.InHalf(member.position,_justOrigin,_justForward,justitiaHard.doubleEdgeRadius,false):
                        Mathf.Abs(member.position.x)<=JustTuning.halfWidth&&Mathf.Abs(member.position.z)<=JustTuning.halfDepth&&
                        (member.position.z>0)!=_justRight;
                if(hit)DamagePartyMember(member,_justHardFollowupPattern==JustitiaPattern.Execution?JustTuning.tankDamage:
                    _justHardFollowupPattern==JustitiaPattern.ScaleVibration?JustTuning.centerDamage:JustTuning.halfDamage,
                    _justHardFollowupPattern==JustitiaPattern.Execution?CombatDamageKind.Direct:CombatDamageKind.Area,
                    "justitia.hard.followup."+_justHardFollowupPattern);
            }
            if(!StageManager.CombatSuspended)EndJustitia(false,0,justitiaHard.recoverySeconds);
        }
        private bool JustitiaHardDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;
            if(_justStep==JustitiaStep.HardFollowup)
            {
                if(_justHardFollowupPattern==JustitiaPattern.Execution)return false;
                if(_justHardFollowupPattern==JustitiaPattern.HalfSlash)
                    destination=_justOrigin+Vector3.Cross(Vector3.up,_justForward)*(_justRight?8:-8);
                else if(_justHardFollowupPattern==JustitiaPattern.ScaleVibration)
                    destination=_justOrigin+Vector3.ClampMagnitude(actor.position-_justOrigin,2);
                else if(_justHardFollowupPattern==JustitiaPattern.DoubleEdged)
                    destination=_justOrigin+_justForward*7;
                else destination=new Vector3(actor.position.x,actor.position.y,_justRight?JustTuning.halfDepth*.5f:-JustTuning.halfDepth*.5f);
                destination=JustitiaNormalRules.Clamp(destination,JustTuning.halfWidth,JustTuning.halfDepth);destination.y=actor.position.y;
                return true;
            }
            if(_justStep==JustitiaStep.Cast&&_justPattern==JustitiaPattern.DoubleEdged)
            {destination=JustitiaNormalRules.Clamp(_justOrigin-_justForward*7,JustTuning.halfWidth,JustTuning.halfDepth);
             destination.y=actor.position.y;return true;}
            if(_justStep==JustitiaStep.Cast&&_justPattern==JustitiaPattern.EvidenceErasure)
            {destination=new Vector3(actor.position.x,actor.position.y,_justRight?-JustTuning.halfDepth*.5f:JustTuning.halfDepth*.5f);return true;}
            if(_justStep==JustitiaStep.HardRegroup)
            {destination=_justHardNext==JustitiaHardNext.Verdict?new Vector3((role-2.5f)*1.3f,actor.position.y,-4):actor.position;return true;}
            return false;
        }
        private void BeginJustitiaHardTransformation()
        {
            ClearJustitiaAttack();ClearJustitiaMass();_justHardResiduePoints.Clear();ClearJustitiaHardWeight();
            _justStep=JustitiaStep.HardTransformation;fsmState=BossFSMState.CastingSkill;
            _justRemaining=justitiaHard.transformationSeconds;
            if(justitiaHard.transformationPresentation!=null)
            {
                try
                {_justHardPresentation=Instantiate(justitiaHard.transformationPresentation,transform.position,Quaternion.identity,transform);
                 if(_justHardPresentation.Play(this,StageManager.Instance?.player,0))
                     _justRemaining=RaidStageCutscenePresentation.Duration(_justHardPresentation.DirectorAsset,_justRemaining);
                 else StopJustitiaHardPresentation();}
                catch(System.Exception error){Debug.LogWarning("Justitia hard transformation fallback: "+error);StopJustitiaHardPresentation();}
            }
            _castTotalDuration=_castRemainingTime=_justRemaining;_currentCastSkillName="눈뜬 절대심판 · 변신";
            if(_justHardPresentation==null)ShowJustitiaHardTransitionFallback();
            SetMechanicAlert("유스티티아 변신 · 눈뜬 절대심판",new Color(1,.65f,.2f),_justRemaining);
            PlayJustitiaCue(justitiaHard.transformationCue);
            CombatMetricsManager.Event("유스티티아 하드 40% 변신 시작 · HP "+currentHp.ToString("0"));
        }
        private void FinishJustitiaHardTransformation()
        {
            StopJustitiaHardPresentation();ClearJustitiaAttack();
            if(_justHardTransitionVisual!=null)Destroy(_justHardTransitionVisual);_justHardTransitionVisual=null;
            _justHardTransformed=true;_justHardReplicaSkipped=false;
            RestoreHardTransformationHealth();
            ShowJustitiaHardModel();
            _justStep=JustitiaStep.Rest;fsmState=BossFSMState.IdleChasing;
            _justRemaining=0;_justRest=justitiaHard.climaxRegroupSeconds;
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
            CombatMetricsManager.Event("유스티티아 하드 변신 종료 · 남은 HP "+currentHp.ToString("0"));
        }
        private void ShowJustitiaHardModel()
        {
            currentBossTitle="눈뜬 절대심판, 유스티티아";
            if(_justModel!=null)_justModel.SetActive(false);
            if(_justHardModel!=null)Destroy(_justHardModel);
            if(justitiaHard.transformedVisualPrefab!=null)
            {
                _justHardModel=Instantiate(justitiaHard.transformedVisualPrefab,transform);
                foreach(var collider in _justHardModel.GetComponentsInChildren<Collider>())collider.enabled=false;
                var animator=_justHardModel.GetComponentInChildren<Animator>();
                if(animator!=null&&justitiaHard.transformedAnimatorController!=null)
                    animator.runtimeAnimatorController=justitiaHard.transformedAnimatorController;
            }
            else
            {
                _justHardModel=new GameObject("Temporary unveiled golden judge · replace in justitiaHard");
                _justHardModel.transform.SetParent(transform,false);
                JustitiaHardPart(PrimitiveType.Capsule,"Cracked clockwork body",new Vector3(0,1.7f,0),new Vector3(2,3,1.4f));
                JustitiaHardPart(PrimitiveType.Sphere,"Uncovered golden eyes",new Vector3(0,3.5f,.45f),new Vector3(1.2f,.8f,.6f));
                JustitiaHardPart(PrimitiveType.Cube,"Exposed drive axle",new Vector3(0,2.2f,-.9f),new Vector3(.9f,2.2f,.4f));
                JustitiaHardPart(PrimitiveType.Cube,"Expanded balance wings",new Vector3(0,3,-.6f),new Vector3(7,.25f,.5f));
                JustitiaHardPart(PrimitiveType.Cube,"Cracked verdict sword",new Vector3(1.7f,1.7f,.5f),new Vector3(.3f,3.8f,.7f));
            }
        }
        private void JustitiaHardPart(PrimitiveType type,string label,Vector3 at,Vector3 scale)
        {
            var part=RaidPrimitive.Create(type);part.name=label;part.transform.SetParent(_justHardModel.transform,false);
            part.transform.localPosition=at;part.transform.localScale=scale;part.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(.88f,.64f,.22f));
            block.SetColor("_Color",new Color(.88f,.64f,.22f));part.GetComponent<Renderer>().SetPropertyBlock(block);
        }
        private void StopJustitiaHardPresentation()
        {
            if(_justHardPresentation==null)return;
            _justHardPresentation.StopAndRestore();_justHardPresentation.gameObject.SetActive(false);
            Destroy(_justHardPresentation.gameObject);_justHardPresentation=null;
        }
        private void ShowJustitiaHardTransitionFallback()
        {
            if(_justHardTransitionVisual!=null)return;
            _justHardTransitionVisual=RaidPrimitive.Create(PrimitiveType.Sphere);
            _justHardTransitionVisual.name="Temporary golden judgment opening · replace with Timeline";
            _justHardTransitionVisual.transform.SetParent(transform,false);
            _justHardTransitionVisual.transform.localPosition=new Vector3(0,3.4f,.8f);
            _justHardTransitionVisual.GetComponent<Collider>().enabled=false;
            var glow=new MaterialPropertyBlock();glow.SetColor("_BaseColor",new Color(1,.78f,.12f));
            glow.SetColor("_Color",new Color(1,.78f,.12f));_justHardTransitionVisual.GetComponent<Renderer>().SetPropertyBlock(glow);
        }
        private bool JustitiaHardSkipPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;return keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }
        private void TickJustitiaHardReplicaInput()
        {
            if(_justHardTransitionVisual!=null)
            {_justHardTransitionVisual.transform.Rotate(0,90*Time.unscaledDeltaTime,0);
             _justHardTransitionVisual.transform.localScale=Vector3.one*(1.5f+Mathf.PingPong(Time.unscaledTime,1));}
            if(_justStep!=JustitiaStep.HardTransformation||_justHardReplicaSkipped||LocalGameplayInput.ModalCovered)return;
            if(!JustitiaHardSkipPressed())return;
            _justHardReplicaSkipped=true;StopJustitiaHardPresentation();LocalGameplayInput.SuppressThisFrame();
        }
        private void ApplyJustitiaHardReplica(RaidCombatEnemy state,JustitiaStep previous,JustitiaPattern previousPattern)
        {
            bool wasImpact=_justHardImpactShown;
            bool wasTransformed=_justHardTransformed;
            _justHardFollowupPattern=(JustitiaPattern)state.justHardFollowupPattern;
            _justHardBalanceKind=(JustitiaHardBalanceKind)state.justHardBalanceKind;
            _justHardBalanceRound=state.justHardRound;_justHardWeightSide=state.justHardWeightSide;
            _justHardNext=(JustitiaHardNext)state.justHardNext;
            _justHardIntroDone=state.justHardIntroDone;_justHardClimaxStarted=state.justHardClimaxStarted;
            _justHardNextPulse=state.justHardNextPulse;
            if(_justStep==JustitiaStep.HardTransformation&&previous!=JustitiaStep.HardTransformation&&
                !_justHardReplicaSkipped&&justitiaHard.transformationPresentation!=null)
            {
                try
                {_justHardPresentation=Instantiate(justitiaHard.transformationPresentation,transform.position,Quaternion.identity,transform);
                 if(!_justHardPresentation.Play(this,StageManager.Instance?.player,
                     Mathf.Max(0,state.castTotal-state.castRemaining)))StopJustitiaHardPresentation();}
                catch(System.Exception){StopJustitiaHardPresentation();}
            }
            if(_justStep==JustitiaStep.HardTransformation&&_justHardPresentation==null)
                ShowJustitiaHardTransitionFallback();
            if(_justStep!=JustitiaStep.HardTransformation&&previous==JustitiaStep.HardTransformation)
            {StopJustitiaHardPresentation();
             if(_justHardTransitionVisual!=null)Destroy(_justHardTransitionVisual);_justHardTransitionVisual=null;
             _justHardReplicaSkipped=false;}
            _justHardTransformed=state.justHardTransformed;
            if(_justHardTransformed&&!wasTransformed)ShowJustitiaHardModel();
            if(!_justHardTransformed&&wasTransformed)
            {
                if(_justHardModel!=null)Destroy(_justHardModel);_justHardModel=null;
                if(_justModel!=null)_justModel.SetActive(true);
            }
            _justHardWeightActive=state.justHardWeightActive;
            if(_justHardWeightActive&&_justHardWeightVisual==null)SpawnJustitiaHardWeight();
            if(!_justHardWeightActive&&_justHardWeightVisual!=null)ClearJustitiaHardWeight();
            if(_justHardWeightVisual!=null)
            {
                _justHardWeightArrived=state.justHardWeightArrived;
                _justHardWeightVisual.transform.position=new Vector3(_justHardWeightSide*justitiaHard.weightOffset,
                    _justHardWeightArrived?.35f:Mathf.Lerp(5,.35f,
                        Mathf.Clamp01((state.castTotal-state.castRemaining)/justitiaHard.weightArrivalSeconds)),0);
                if(_justHardWeightLabel!=null)_justHardWeightLabel.transform.position=new Vector3(_justHardWeightSide*justitiaHard.weightOffset,2,0);
            }
            if(previous!=_justStep||previousPattern!=_justPattern)_justHardImpactShown=false;
            if(state.justHardImpactShown&&!_justHardImpactShown)
            {
                if(_justStep==JustitiaStep.Cast)TickJustitiaHardCast();
                else if(_justStep==JustitiaStep.HardFollowup)ShowJustitiaHardFollowupGround();
            }
            _justHardImpactShown=state.justHardImpactShown;
            if(_justStep==JustitiaStep.HardResidue&&(previous!=_justStep||_justHardResiduePoints.Count==0)&&
                state.justHardResidueX!=null&&state.justHardResidueZ!=null)
            {
                _justHardResiduePoints.Clear();
                for(int i=0;i<state.justHardResidueX.Length&&i<state.justHardResidueZ.Length;i++)
                {var point=new Vector3(state.justHardResidueX[i],state.y,state.justHardResidueZ[i]);
                 _justHardResiduePoints.Add(point);
                 JustitiaGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,state.castRemaining,0,justitiaHard.residueRadius);}
            }
            if(_justStep!=JustitiaStep.HardResidue)_justHardResiduePoints.Clear();
        }
        private int[] ChooseJustitiaHardWeightMass(int living)
        {
            int[][] pool=living==4?JustitiaNormalRules.Four:living==3?JustitiaNormalRules.Three:
                living==2?new[]{new[]{3,1}}:new int[0][];
            var valid=new List<int[]>();
            foreach(var candidate in pool)
                if(JustitiaHardRules.ValidWeight(candidate,_justHardWeightSide,justitiaHard.unfairWeightMass))
                    valid.Add(candidate);
            if(valid.Count>0)return (int[])valid[Random.Range(0,valid.Count)].Clone();
            var fallback=new int[living];fallback[0]=justitiaHard.unfairWeightMass+living-1;
            for(int i=1;i<living;i++)fallback[i]=1;
            CombatMetricsManager.Event("부당한 추 후보 검증 실패 · 안전한 기본 후보로 대체");
            return fallback;
        }
        private void StartJustitiaHardWeight(JustitiaHardBalanceKind kind)
        {
            _justHardBalanceKind=kind;_justHardBalanceRound=1;_justHardWeightActive=true;
            _justHardWeightSide=Random.value>=.5f?1:-1;
            if(GetLivingPartyMemberTransforms().Count<2)
            {
                CombatMetricsManager.Event("부당한 추 보류 · 생존자 2명 미만");
                _justHardWeightActive=false;_justHardBalanceKind=JustitiaHardBalanceKind.None;
                if(kind==JustitiaHardBalanceKind.WeightIntro)_justHardIntroDone=true;
                if(kind==JustitiaHardBalanceKind.ClimaxWeight)BeginJustitiaHardRegroup(JustitiaHardNext.Verdict,justitiaHard.climaxRegroupSeconds);
                else EndJustitia(false,0,justitiaHard.balanceRecoverySeconds);
                return;
            }
            BeginJustitia(JustitiaPattern.UnfairWeight);
            SpawnJustitiaHardWeight();
        }
        private void SpawnJustitiaHardWeight()
        {
            ClearJustitiaHardWeight();_justHardWeightArrived=false;_justHardWeightElapsed=0;
            _justHardWeightVisual=justitiaHard.weightVisualPrefab!=null
                ?Instantiate(justitiaHard.weightVisualPrefab)
                :RaidPrimitive.Create(PrimitiveType.Cylinder);
            _justHardWeightVisual.name="부당한 추 · 환경 질량 "+justitiaHard.unfairWeightMass;
            _justHardWeightVisual.transform.position=new Vector3(_justHardWeightSide*justitiaHard.weightOffset,5,0);
            _justHardWeightVisual.transform.localScale=new Vector3(2,.5f,2);
            foreach(var hitbox in _justHardWeightVisual.GetComponentsInChildren<Collider>())hitbox.enabled=false;
            _justHardWeightLabel=FloraLabel("⚖ 추 "+justitiaHard.unfairWeightMass+" · "+(_justHardWeightSide<0?"좌":"우"),new Color(1,.8f,.2f));
            PlayJustitiaCue(justitiaHard.weightCue);
        }
        private void TickJustitiaHardWeightVisual(float dt)
        {
            if(_justHardWeightVisual==null)return;
            _justHardWeightElapsed+=dt;
            float progress=Mathf.Clamp01(_justHardWeightElapsed/Mathf.Max(.1f,justitiaHard.weightArrivalSeconds));
            _justHardWeightVisual.transform.position=new Vector3(_justHardWeightSide*justitiaHard.weightOffset,Mathf.Lerp(5,.35f,progress),0);
            if(_justHardWeightLabel!=null)_justHardWeightLabel.transform.position=new Vector3(_justHardWeightSide*justitiaHard.weightOffset,2,0);
            _justHardWeightArrived=progress>=1;
        }
        private void ClearJustitiaHardWeight()
        {
            if(_justHardWeightVisual!=null)Destroy(_justHardWeightVisual);_justHardWeightVisual=null;
            if(_justHardWeightLabel!=null)Destroy(_justHardWeightLabel.gameObject);_justHardWeightLabel=null;
            _justHardWeightArrived=false;
        }
        private bool FinishJustitiaHardBalance(bool success)
        {
            if(_justHardBalanceKind==JustitiaHardBalanceKind.Remeasure)
            {
                if(_justHardBalanceRound==1)
                {
                    _justHardPreviousMass.Clear();
                    for(int i=0;i<_justParticipants.Count;i++)_justHardPreviousMass[_justParticipants[i]]=_justMass[i];
                    BeginJustitiaHardRegroup(JustitiaHardNext.SecondBalance,justitiaHard.remeasureRecovery);return true;
                }
                _justHardBalanceKind=JustitiaHardBalanceKind.None;_justHardBalanceRound=0;
                EndJustitia(false,0,justitiaHard.balanceRecoverySeconds);return true;
            }
            if(_justHardBalanceKind==JustitiaHardBalanceKind.WeightIntro)
            {
                _justHardIntroDone=true;_justHardBalanceKind=JustitiaHardBalanceKind.None;
                _justHardWeightActive=false;ClearJustitiaHardWeight();
                EndJustitia(false,0,justitiaHard.balanceRecoverySeconds);return true;
            }
            if(_justHardBalanceKind==JustitiaHardBalanceKind.ClimaxWeight)
            {BeginJustitiaHardRegroup(JustitiaHardNext.Verdict,justitiaHard.climaxRegroupSeconds);return true;}
            return false;
        }
        private void EnsureJustitiaHardNewMass(int[] values,List<Transform> living)
        {
            bool same=values.Length==living.Count;
            for(int i=0;i<values.Length&&same;i++)
                if(!_justHardPreviousMass.TryGetValue(living[i],out int old)||old!=values[i])same=false;
            if(!same)return;
            if(values.Length==2&&values[0]==values[1])
            {values[0]=values[1]=values[0]==1?2:1;return;}
            int first=values[0];for(int i=0;i<values.Length-1;i++)values[i]=values[i+1];values[values.Length-1]=first;
            CombatMetricsManager.Event("질량 재측정 · 이전 배정과 다른 공개 조합 적용");
        }
        private void BeginJustitiaHardRegroup(JustitiaHardNext next,float seconds)
        {
            ClearJustitiaAttack();ClearJustitiaMass();ClearJustitiaHardWeight();
            if(next==JustitiaHardNext.Verdict)
            {
                _justHardWeightActive=false;_justHardNext=JustitiaHardNext.None;
                _justVerdictUsed=true;
                EndJustitia(); // Keep the weight mechanic, then resume without a verdict check.
                return;
            }
            _justHardWeightActive=false;_justHardNext=next;_justStep=JustitiaStep.HardRegroup;
            fsmState=BossFSMState.CastingSkill;_justRemaining=seconds;_castTotalDuration=_castRemainingTime=seconds;
            _currentCastSkillName=next==JustitiaHardNext.Verdict?"유죄 선고 전 재집결·회복":"질량 재측정 전 회복·재배치";
            SetMechanicAlert(_currentCastSkillName,Color.yellow,seconds);
        }
        private void TickJustitiaHardVerdict()
        {
            if(_justRemaining<=0||_justStagger>=justitiaHard.verdictStagger)return;
            _justHardNextPulse-=Time.deltaTime;
            if(_justHardNextPulse>0)return;
            _justHardNextPulse=justitiaHard.pulseInterval;
            PlayJustitiaCue(justitiaHard.pulseCue);
            foreach(var member in GetLivingPartyMemberTransforms())
                DamagePartyMember(member,justitiaHard.pulseDamage,CombatDamageKind.Area,"justitia.hard.verdict.pulse");
            CombatMetricsManager.Event("유죄 선고 · 파티 맥동 피해");
        }
    }
}
