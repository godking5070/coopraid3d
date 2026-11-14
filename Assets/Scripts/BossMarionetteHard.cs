using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public partial class BossController
    {
        private bool _marHardTransformed,_marHardClimaxPreparing,_marHardClimaxStarted,
            _marHardCrossingIntroduced,_marHardImpactShown,_marHardRestProtected,
            _marHardMemoryPending,_marHardReenactIntroduced,_marHardReplicaSkipped,_marHardResidueApplied;
        private MarionettePattern _marHardMemoryPattern,_marHardLastPattern;
        private Vector3 _marHardDollPoint,_marHardMemoryForward,_marHardRecoilPoint;
        private bool _marHardMemoryRight;
        private int _marHardDollIndex,_marHardStringMask;
        private readonly HashSet<Transform> _marHardCrossingHit=new HashSet<Transform>();
        private float _marHardCrossingElapsed,_marHardNextPulse,_marHardPullCommitAt;
        private readonly List<MarionettePattern> _marHardBag=new List<MarionettePattern>();
        private readonly BossAddMinion[] _marHardStrings=new BossAddMinion[2];
        private GameObject _marHardModel,_marHardDoll;
        private readonly GameObject[] _marHardCrossGuides=new GameObject[2];
        private TextMesh _marHardDollLabel,_marHardCrossingLabel,_marHardStringLabel;
        private AoETelegraph _marHardCrossingGround,_marHardRecoilGround;
        private RaidStageCutscenePresentation _marHardPresentation;

        public bool MarionetteHardTransformationActive => MarionetteHard && _marStep==MarionetteStep.Transformation;
        private void ResetMarionetteHard()
        {
            _marHardTransformed=_marHardClimaxPreparing=_marHardClimaxStarted=false;
            _marHardCrossingIntroduced=_marHardImpactShown=_marHardRestProtected=false;
            _marHardMemoryPending=_marHardReenactIntroduced=_marHardReplicaSkipped=false;
            _marHardMemoryPattern=_marHardLastPattern=MarionettePattern.None;
            _marHardDollIndex=_marHardStringMask=0;
            _marHardResiduePoints.Clear();_marHardCrossingHit.Clear();
            _marHardBag.Clear();_marHardNextPulse=0;
        }
        private void ClearMarionetteHard(bool keepModel)
        {
            StopMarionetteHardPresentation();ClearMarionetteHardStrings();ClearMarionetteHardDoll();
            ClearMarionetteHardCrossing();ClearMarionetteHardRecoil();
            if(_marHardModel!=null&&!keepModel){Destroy(_marHardModel);_marHardModel=null;}
            if(!keepModel)_marHardTransformed=false;
            _marHardClimaxPreparing=_marHardClimaxStarted=false;
            _marHardMemoryPending=false;_marHardBag.Clear();
            _marHardResiduePoints.Clear();_marHardCrossingHit.Clear();
            _marHardRestProtected=false;_marHardReplicaSkipped=false;_marHardNextPulse=0;
        }
        private bool TryBeginMarionetteHardSpecial()
        {
            if(MarionetteHardRules.TransformDue(_marHardTransformed,HpRatio,marionetteHard.transformationThreshold))
            {BeginMarionetteHardTransformation();return true;}
            if(MarionetteHardRules.ClimaxDue(_marHardTransformed,_marHardClimaxStarted,HpRatio,marionetteHard.climaxThreshold))
            {
                _marHardClimaxStarted=_marHardClimaxPreparing=true;
                if(GetLivingPartyMemberTransforms().Count<2)
                {CombatMetricsManager.Event("배신자의 클라이맥스 · 구출 인원 부족, 준비 단계 취소");BeginMarionetteHardRegroup();}
                else {BeginMarionette(MarionettePattern.TraitorRole);_currentCastSkillName="배신자의 클라이맥스 · 이중 실 구출";}
                return true;
            }
            if(_marHardMemoryPending){BeginMarionette(MarionettePattern.Reenact);return true;}
            return false;
        }
        private void BeginNextMarionetteHardPattern()
        {
            if(!_marHardCrossingIntroduced)
            { _marHardCrossingIntroduced=true;_marHardLastPattern=MarionettePattern.GoldenCrossing;
              BeginMarionette(MarionettePattern.GoldenCrossing);return; }
            MarionettePattern[] cycle={MarionettePattern.LeadExecution,MarionettePattern.HalfCurtain,
                MarionettePattern.TragicCenter,MarionettePattern.TrackingLight,MarionettePattern.AudienceApplause,
                MarionettePattern.StageCharge,MarionettePattern.TraitorRole,MarionettePattern.GoldenCrossing};
            if(_marHardBag.Count==0)
            {
                for(int i=0;i<cycle.Length;i++)
                {
                    int weight=marionetteHard.patternWeights!=null&&i<marionetteHard.patternWeights.Length?
                        Mathf.Clamp(marionetteHard.patternWeights[i],1,4):1;
                    for(int draw=0;draw<weight;draw++)_marHardBag.Add(cycle[i]);
                }
                for(int i=_marHardBag.Count-1;i>0;i--)
                {int j=Random.Range(0,i+1);var temp=_marHardBag[i];_marHardBag[i]=_marHardBag[j];_marHardBag[j]=temp;}
            }
            int selected=_marHardBag.FindIndex(p=>p!=_marHardLastPattern &&
                (p!=MarionettePattern.TraitorRole||GetLivingPartyMemberTransforms().Count>=2));
            if(selected<0){_marHardBag.Clear();_marRest=MarTuning.recoverySeconds;return;}
            var pattern=_marHardBag[selected];_marHardBag.RemoveAt(selected);
            _marHardLastPattern=pattern;BeginMarionette(pattern);
        }
        private bool TickMarionetteHard(float dt)
        {
            switch(_marStep)
            {
                case MarionetteStep.HardFollowup:
                    _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                    if(!_marHardImpactShown && _marRemaining<=marionetteHard.impactWarning)ShowMarionetteHardFollowupGround();
                    if(_marRemaining<=0)ResolveMarionetteHardFollowup();return true;
                case MarionetteStep.HardResidue:
                    _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                    if(!_marHardResidueApplied && _marRemaining<=marionetteHard.lightResidueSeconds*.5f)
                    {
                        _marHardResidueApplied=true;
                        foreach(var member in GetLivingPartyMemberTransforms())
                            foreach(var point in _marHardResiduePoints)
                                if(FloraNormalRules.InCircle(member.position,point,marionetteHard.lightResidueRadius))
                                {DamagePartyMember(member,marionetteHard.lightResidueDamage,CombatDamageKind.Area,"marionette.hard.light.residue");break;}
                    }
                    if(_marRemaining<=0){ClearMarionetteAttack();EndMarionette();}return true;
                case MarionetteStep.ReenactDelay:
                    _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                    UpdateMarionetteHardDoll();
                    if(_marRemaining<=0)StartMarionetteHardReenactCast();return true;
                case MarionetteStep.ReenactCast:
                    _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                    UpdateMarionetteHardDoll();
                    if(!_marHardImpactShown && _marRemaining<=marionetteHard.impactWarning)ShowMarionetteHardReenactGround();
                    if(_marRemaining<=0)ResolveMarionetteHardReenact();return true;
                case MarionetteStep.CrossingFirst:case MarionetteStep.CrossingSecond:
                    TickMarionetteHardCrossing(dt);return true;
                case MarionetteStep.CrossingGap:
                    _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                    if(_marRemaining<=0)StartMarionetteHardCrossingPass(_marHardCrossingElapsed<0);return true;
                case MarionetteStep.Recoil:
                    _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                    if(_marRemaining<=0)ResolveMarionetteHardRecoil();return true;
                case MarionetteStep.Transformation:
                    _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                    if(MarionetteHardSkipPressed()&&!LocalGameplayInput.ModalCovered)
                    {_marRemaining=0;LocalGameplayInput.SuppressThisFrame();}
                    if(_marRemaining<=0)FinishMarionetteHardTransformation();return true;
                case MarionetteStep.ClimaxRegroup:
                    _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                    if(_marRemaining<=0)
                    { _marHardClimaxPreparing=false;BeginMarionette(MarionettePattern.Climax); }
                    return true;
            }
            return false;
        }
        private void TickMarionetteHardCast()
        {
            if(_marHardImpactShown||_marRemaining>marionetteHard.impactWarning)return;
            _marHardImpactShown=true;
            if(_marPattern==MarionettePattern.HalfCurtain)
                MarionetteGround(AoETelegraphType.FrontCone_120Deg,_marOrigin,
                    Vector3.Cross(Vector3.up,_marForward)*(_marRight?1:-1),_marRemaining,0,MarTuning.stageHalfSize,180);
            else if(_marPattern==MarionettePattern.TragicCenter)
                MarionetteGround(AoETelegraphType.Circle_Radius6,_marOrigin,_marForward,_marRemaining,0,MarTuning.centerRadius);
            else if(_marPattern==MarionettePattern.StageCharge)
                MarionetteGround(AoETelegraphType.Rectangle,_marOrigin,_marForward,_marRemaining,MarTuning.chargeWidth,MarTuning.chargeLength);
            else if(_marPattern==MarionettePattern.TrackingLight)
                foreach(var point in _marLightPoints)
                    MarionetteGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,_marRemaining,0,MarTuning.lightRadius);
        }
        private bool MarionetteHardChargeHit(Vector3 point)
        {
            Vector3 delta=point-_marOrigin;delta.y=0;
            float longitudinal=Vector3.Dot(delta,_marForward);
            float lateral=Vector3.Dot(delta,Vector3.Cross(Vector3.up,_marForward));
            return longitudinal>=0&&longitudinal<=MarTuning.chargeLength&&Mathf.Abs(lateral)<=MarTuning.chargeWidth*.5f;
        }
        private bool TryBeginMarionetteHardAfterImpact()
        {
            if(_marPattern==MarionettePattern.HalfCurtain||_marPattern==MarionettePattern.TragicCenter)
                RecordMarionetteHardMemory();
            if(_marPattern==MarionettePattern.LeadExecution||_marPattern==MarionettePattern.HalfCurtain||
                _marPattern==MarionettePattern.TragicCenter)
            {
                ClearMarionetteAttack();_marStep=MarionetteStep.HardFollowup;
                _marRemaining=_marPattern==MarionettePattern.LeadExecution?
                    marionetteHard.secondTankInterval:marionetteHard.followupSeconds;
                _castTotalDuration=_castRemainingTime=_marRemaining;_marHardImpactShown=false;
                _currentCastSkillName=_marPattern==MarionettePattern.LeadExecution?"주연 처형 · 두 번째 타격":
                    _marPattern==MarionettePattern.HalfCurtain?"반쪽의 막 · 반대쪽":"비극의 중심 · 외곽 도넛";
                PlayMarionetteHardTrigger(marionetteHard.followupTrigger);
                PlayMarionetteCue(marionetteHard.followupCue);
                SetMechanicAlert(_currentCastSkillName,Color.red,_marRemaining);
                return true;
            }
            if(_marPattern==MarionettePattern.TrackingLight && _marLightPoints.Count>0)
            {
                _marHardResiduePoints.Clear();_marHardResiduePoints.AddRange(_marLightPoints);
                ClearMarionetteAttack();_marStep=MarionetteStep.HardResidue;
                _marHardResidueApplied=false;
                _marRemaining=marionetteHard.lightResidueSeconds;
                _castTotalDuration=_castRemainingTime=_marRemaining;
                _currentCastSkillName="추적 조명 · 잔류 위험 구역";
                foreach(var point in _marHardResiduePoints)
                    MarionetteGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,_marRemaining,0,marionetteHard.lightResidueRadius);
                SetMechanicAlert("잔류 조명 · 종료까지 범위 이탈",Color.red,_marRemaining);
                return true;
            }
            return false;
        }
        private readonly List<Vector3> _marHardResiduePoints=new List<Vector3>();
        private void RecordMarionetteHardMemory()
        {
            if(_marHardMemoryPending||!MarionetteHardRules.Recordable(_marPattern))return;
            _marHardMemoryPattern=_marPattern;_marHardMemoryForward=_marForward;
            _marHardMemoryRight=_marRight;_marHardMemoryPending=true;
            var points=marionetteHard.reenactPoints;
            if(points!=null&&points.Length>0)
            {
                var candidate=points[_marHardDollIndex++%points.Length];
                _marHardDollPoint=MarionetteNormalRules.ClampToStage(candidate,MarTuning.stageHalfSize,7);
            }
            else _marHardDollPoint=new Vector3(-7,0,6);
            CombatMetricsManager.Event("전투용 인형에 기록: "+_marHardMemoryPattern);
        }
        private void ShowMarionetteHardFollowupGround()
        {
            _marHardImpactShown=true;
            if(_marPattern==MarionettePattern.HalfCurtain)
                MarionetteGround(AoETelegraphType.FrontCone_120Deg,_marOrigin,
                    Vector3.Cross(Vector3.up,_marForward)*(_marRight?-1:1),_marRemaining,0,MarTuning.stageHalfSize,180);
            else if(_marPattern==MarionettePattern.TragicCenter)
                MarionetteGround(AoETelegraphType.Donut_Inner4_Outer12,_marOrigin,_marForward,_marRemaining,
                    MarTuning.centerRadius+1,MarTuning.stageHalfSize);
        }
        private void ResolveMarionetteHardFollowup()
        {
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                bool hit=_marPattern==MarionettePattern.LeadExecution?member==_marTarget:
                    _marPattern==MarionettePattern.HalfCurtain?
                        FloraNormalRules.InHalf(member.position,_marOrigin,Vector3.Cross(Vector3.up,_marForward),
                            MarTuning.stageHalfSize,!_marRight):
                        !FloraNormalRules.InCircle(member.position,_marOrigin,MarTuning.centerRadius+1);
                if(hit)DamagePartyMember(member,_marPattern==MarionettePattern.LeadExecution?MarTuning.tankDamage:
                    _marPattern==MarionettePattern.HalfCurtain?MarTuning.halfDamage:MarTuning.centerDamage,
                    _marPattern==MarionettePattern.LeadExecution?CombatDamageKind.Direct:CombatDamageKind.Area,
                    "marionette.hard.followup");
            }
            if(!StageManager.CombatSuspended)EndMarionette(false,0,
                _marPattern==MarionettePattern.LeadExecution?MarTuning.recoverySeconds:0);
        }
        private void BeginMarionetteReenact()
        {
            if(!_marHardMemoryPending){EndMarionette();return;}
            ClearMarionetteAttack();_marStep=MarionetteStep.ReenactDelay;
            _marRemaining=marionetteHard.reenactDelay;
            _castTotalDuration=_castRemainingTime=_marRemaining;
            _currentCastSkillName="기억의 재연 · 전투용 인형 준비";
            SpawnMarionetteHardDoll();
            PlayMarionetteHardTrigger(marionetteHard.reenactTrigger);
            SetMechanicAlert("기억의 재연 · 인형의 기록과 방향 확인",new Color(1,.7f,.3f),_marRemaining);
        }
        private void SpawnMarionetteHardDoll()
        {
            ClearMarionetteHardDoll();
            _marHardDoll=RaidPrimitive.Create(PrimitiveType.Capsule);
            _marHardDoll.name="전투용 기억 재연 인형 · 비타기팅";
            _marHardDoll.transform.position=_marHardDollPoint+Vector3.up;
            _marHardDoll.transform.rotation=Quaternion.LookRotation(_marHardMemoryForward);
            _marHardDoll.transform.localScale=new Vector3(.7f,1.8f,.7f);
            _marHardDoll.GetComponent<Collider>().enabled=false;
            _marHardDollLabel=FloraLabel("",new Color(1,.7f,.27f));
            UpdateMarionetteHardDoll();
        }
        private void UpdateMarionetteHardDoll()
        {
            if(_marHardDollLabel==null)return;
            _marHardDollLabel.text="재연: "+(_marHardMemoryPattern==MarionettePattern.HalfCurtain?
                (_marHardMemoryRight?"오른쪽 반면":"왼쪽 반면"):"중심 원형")+" · "+_marRemaining.ToString("0.0")+"초";
            _marHardDollLabel.transform.position=_marHardDollPoint+Vector3.up*3;
            if(Camera.main!=null)_marHardDollLabel.transform.rotation=Camera.main.transform.rotation;
        }
        private void StartMarionetteHardReenactCast()
        {
            _marStep=MarionetteStep.ReenactCast;_marRemaining=marionetteHard.reenactCastSeconds;
            _castTotalDuration=_castRemainingTime=_marRemaining;_marHardImpactShown=false;
            _currentCastSkillName="기억의 재연 · 인형 공격";
            PlayMarionetteCue(marionetteHard.reenactCue);
        }
        private void ShowMarionetteHardReenactGround()
        {
            _marHardImpactShown=true;
            if(_marHardMemoryPattern==MarionettePattern.HalfCurtain)
                MarionetteGround(AoETelegraphType.FrontCone_120Deg,_marHardDollPoint,
                    Vector3.Cross(Vector3.up,_marHardMemoryForward)*(_marHardMemoryRight?1:-1),
                    _marRemaining,0,MarTuning.stageHalfSize,180);
            else MarionetteGround(AoETelegraphType.Circle_Radius6,_marHardDollPoint,_marHardMemoryForward,
                _marRemaining,0,MarTuning.centerRadius);
        }
        private void ResolveMarionetteHardReenact()
        {
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                bool hit=_marHardMemoryPattern==MarionettePattern.HalfCurtain?
                    FloraNormalRules.InHalf(member.position,_marHardDollPoint,
                        Vector3.Cross(Vector3.up,_marHardMemoryForward),MarTuning.stageHalfSize,_marHardMemoryRight):
                    FloraNormalRules.InCircle(member.position,_marHardDollPoint,MarTuning.centerRadius);
                if(hit)DamagePartyMember(member,marionetteHard.reenactDamage,CombatDamageKind.Area,"marionette.hard.reenact");
            }
            CombatMetricsManager.Event("기억의 재연 완료: "+_marHardMemoryPattern);
            _marHardMemoryPending=false;ClearMarionetteHardDoll();
            if(!StageManager.CombatSuspended)EndMarionette();
        }
        private void ClearMarionetteHardDoll()
        {
            if(_marHardDoll!=null)Destroy(_marHardDoll);_marHardDoll=null;
            if(_marHardDollLabel!=null)Destroy(_marHardDollLabel.gameObject);_marHardDollLabel=null;
        }
        private void BeginMarionetteCrossing()
        {
            ClearMarionetteAttack();_marStep=MarionetteStep.CrossingGap;
            _marHardCrossingElapsed=-1;
            _marRemaining=MarionetteHardRules.Tempo(MarTuning.castSeconds,marionetteHard.attackTempo);
            _castTotalDuration=_castRemainingTime=_marRemaining;
            _currentCastSkillName="황금 실의 횡단 · I → II";
            _marHardCrossingLabel=FloraLabel("I: 중심→오른쪽 / II: 중심→왼쪽",new Color(1,.72f,.2f));
            _marHardCrossingLabel.transform.position=Vector3.up*.6f;
            ShowMarionetteHardCrossingGuides();
            PlayMarionetteHardTrigger(marionetteHard.crossingTrigger);
            SetMechanicAlert("I 중심→오른쪽, II 중심→왼쪽 · 두 경로를 순서대로 회피",new Color(1,.72f,.2f),_marRemaining);
            PlayMarionetteCue(marionetteHard.crossingCue);
        }
        private void StartMarionetteHardCrossingPass(bool first)
        {
            _marStep=first?MarionetteStep.CrossingFirst:MarionetteStep.CrossingSecond;
            _marHardCrossingElapsed=0;
            _marHardCrossingHit.Clear();
            _marRemaining=marionetteHard.crossingTravelSeconds;
            _castTotalDuration=_castRemainingTime=_marRemaining;
            _currentCastSkillName=first?"황금 실의 횡단 I · 중심→오른쪽":"황금 실의 횡단 II · 중심→왼쪽";
            if(_marHardCrossingLabel!=null)_marHardCrossingLabel.text=_currentCastSkillName;
            _marHardCrossingGround=AoETelegraph.Spawn(AoETelegraphType.Rectangle,new Vector3(0,0,-MarTuning.stageHalfSize),
                Vector3.forward,_marRemaining,marionetteHard.crossingWidth,MarTuning.stageHalfSize*2);
            SetMechanicAlert(_currentCastSkillName,Color.red,_marRemaining);
        }
        private void TickMarionetteHardCrossing(float dt)
        {
            bool reverse=_marStep==MarionetteStep.CrossingSecond;
            _marHardCrossingElapsed+=dt;_marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
            float x=MarionetteHardRules.CrossingX(_marHardCrossingElapsed,marionetteHard.crossingTravelSeconds,MarTuning.stageHalfSize,reverse);
            if(_marHardCrossingGround!=null)_marHardCrossingGround.transform.position=new Vector3(x,0,-MarTuning.stageHalfSize);
            var living=GetLivingPartyMemberTransforms();
            for(int i=0;i<living.Count;i++)
            {
                var member=living[i];
                if(_marHardCrossingHit.Contains(member) || Mathf.Abs(member.position.x-x)>marionetteHard.crossingWidth*.5f+CombatRange.Radius(member))continue;
                _marHardCrossingHit.Add(member);
                DamagePartyMember(member,marionetteHard.crossingDamage,CombatDamageKind.Area,
                    reverse?"marionette.hard.crossing.two":"marionette.hard.crossing.one");
            }
            if(_marRemaining>0 || StageManager.CombatSuspended)return;
            if(_marHardCrossingGround!=null)Destroy(_marHardCrossingGround.gameObject);_marHardCrossingGround=null;
            if(!reverse)
            { _marStep=MarionetteStep.CrossingGap;_marRemaining=marionetteHard.crossingGapSeconds;
              _castTotalDuration=_castRemainingTime=_marRemaining;
              _currentCastSkillName="황금 실의 횡단 · II 준비";
              SetMechanicAlert("II 중심→왼쪽 · 이미 지난 오른쪽으로 재배치",new Color(1,.72f,.2f),_marRemaining); }
            else {ClearMarionetteHardCrossing();EndMarionette();}
        }
        private void ClearMarionetteHardCrossing()
        {
            if(_marHardCrossingGround!=null)Destroy(_marHardCrossingGround.gameObject);_marHardCrossingGround=null;
            if(_marHardCrossingLabel!=null)Destroy(_marHardCrossingLabel.gameObject);_marHardCrossingLabel=null;
            for(int i=0;i<2;i++)if(_marHardCrossGuides[i]!=null)
            {Destroy(_marHardCrossGuides[i]);_marHardCrossGuides[i]=null;}
            _marHardCrossingHit.Clear();
        }
        private void ShowMarionetteHardCrossingGuides()
        {
            float length=MarTuning.stageHalfSize-2;
            for(int i=0;i<2;i++)
            {
                if(_marHardCrossGuides[i]!=null)continue;
                var guide=RaidPrimitive.Create(PrimitiveType.Cube);
                guide.name="황금 실 횡단 경로 "+(i==0?"I":"II")+" · 순서 안내";
                guide.transform.position=new Vector3((i==0?1:-1)*length,.05f,0);
                guide.transform.localScale=new Vector3(.12f,.025f,MarTuning.stageHalfSize*2-2);
                guide.GetComponent<Collider>().enabled=false;
                var block=new MaterialPropertyBlock();
                block.SetColor("_BaseColor",new Color(1,.72f,.15f,.18f));
                block.SetColor("_Color",new Color(1,.72f,.15f,.18f));
                guide.GetComponent<Renderer>().SetPropertyBlock(block);
                _marHardCrossGuides[i]=guide;
            }
        }
        private void SpawnMarionetteHardStrings(BossAddSpawner spawner,Vector3 back)
        {
            _marHardStringMask=0;
            for(int i=0;i<2;i++)
            {
                Vector3 side=Vector3.Cross(Vector3.up,_marTarget.forward)*(i==0?-1:1);
                Vector3 point=MarionetteNormalRules.ClampToStage(back+side*1.3f,MarTuning.stageHalfSize,1.4f);
                var add=new GameObject("인형의 실 "+(i+1)+"/2 · 구출 대상").AddComponent<BossAddMinion>();
                add.InitializeAdd(AddMinionType.MarionetteString,this,spawner,point,EncounterTemplate);
                add.aggroTarget=_marTarget;spawner.activeAdds.Add(add);_marHardStrings[i]=add;
            }
            _marHardStringLabel=FloraLabel("황금 실 0/2 절단",new Color(1,.8f,.3f));
        }
        private void NotifyMarionetteHardStringDestroyed(BossAddMinion add)
        {
            for(int i=0;i<2;i++)if(_marHardStrings[i]==add && (_marHardStringMask&(1<<i))==0)
            {
                _marHardStringMask|=1<<i;
                CombatMetricsManager.Event("마리오네트 이중 실 "+(i+1)+"/2 절단");
                if(_marHardStringMask==3)FinishMarionetteControl(true,false);
                return;
            }
        }
        private void ClearMarionetteHardStrings()
        {
            for(int i=0;i<2;i++)
            {var add=_marHardStrings[i];_marHardStrings[i]=null;if(add!=null&&!add.isDead)add.KillAddMinion(true);}
            if(_marHardStringLabel!=null)Destroy(_marHardStringLabel.gameObject);_marHardStringLabel=null;
            _marHardStringMask=0;
        }
        private void TickMarionetteHardPull(float dt)
        {
            if(_marTarget==null)return;
            Vector3 toward=transform.position-_marTarget.position;toward.y=0;
            if(toward.sqrMagnitude>9)
            {
                Vector3 next=_marTarget.position+toward.normalized*marionetteHard.pullSpeed*dt;
                _marTarget.position=MarionetteNormalRules.ClampToStage(next,MarTuning.stageHalfSize,1.4f);
                if(RaidCombatAuthority.Host && RaidCombatAuthority.ServerTime>=_marHardPullCommitAt)
                {RaidNetworkSession.Instance?.CommitSkillRelocation(_marTarget);_marHardPullCommitAt=RaidCombatAuthority.ServerTime+.2f;}
            }
            for(int i=0;i<2;i++)if(_marHardStrings[i]!=null&&!_marHardStrings[i].isDead)
            {
                Vector3 side=Vector3.Cross(Vector3.up,_marTarget.forward)*(i==0?-1:1);
                _marHardStrings[i].transform.position=MarionetteNormalRules.ClampToStage(
                    _marTarget.position-_marTarget.forward*1.8f+side*1.3f,MarTuning.stageHalfSize,1.4f);
            }
            if(_marHardStringLabel!=null)
            { _marHardStringLabel.text="황금 실 "+(((_marHardStringMask&1)!=0?1:0)+((_marHardStringMask&2)!=0?1:0))+"/2 절단";
              _marHardStringLabel.transform.position=_marTarget.position+Vector3.up*3.8f;
              if(Camera.main!=null)_marHardStringLabel.transform.rotation=Camera.main.transform.rotation; }
        }
        private void BeginMarionetteHardRecoil(Transform rescued)
        {
            ClearMarionetteAttack();
            _marStep=MarionetteStep.Recoil;
            _marHardRecoilPoint=rescued!=null?rescued.position:Vector3.zero;
            _marRemaining=marionetteHard.recoilWarningSeconds;
            _castTotalDuration=_castRemainingTime=_marRemaining;
            _currentCastSkillName="끊어진 실의 반동 · 이탈";
            _marHardRecoilGround=AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6,
                _marHardRecoilPoint,Vector3.forward,_marRemaining,0,marionetteHard.recoilRadius);
            SetMechanicAlert("끊어진 실의 반동 · 대상 주변에서 이탈",Color.red,_marRemaining);
            PlayMarionetteCue(marionetteHard.recoilCue);
        }
        private void ResolveMarionetteHardRecoil()
        {
            foreach(var member in GetLivingPartyMemberTransforms())
                if(FloraNormalRules.InCircle(member.position,_marHardRecoilPoint,marionetteHard.recoilRadius))
                    DamagePartyMember(member,marionetteHard.recoilDamage,CombatDamageKind.Area,"marionette.hard.recoil");
            ClearMarionetteHardRecoil();
            if(StageManager.CombatSuspended)return;
            if(_marHardClimaxPreparing)BeginMarionetteHardRegroup();
            else EndMarionette(false,0,MarTuning.recoverySeconds);
        }
        private void ClearMarionetteHardRecoil()
        {if(_marHardRecoilGround!=null)Destroy(_marHardRecoilGround.gameObject);_marHardRecoilGround=null;}
        private void BeginMarionetteHardRegroup()
        {
            ClearMarionetteAttack();ClearMarionetteHardRecoil();
            BeginMarionetteClimax(); // Rescue/recoil finishes without a regroup or stagger phase.
        }
        private void BeginMarionetteHardPulse()
        { _marHardNextPulse=MarionetteClimaxSeconds-marionetteHard.climaxPulseInterval; }
        private void TickMarionetteHardPulse()
        {
            if(_marStep!=MarionetteStep.Climax||_marHardNextPulse<=0)return;
            float until=_marRemaining-_marHardNextPulse;
            if(until>0&&until<=marionetteHard.climaxPulseWarning)
                SetMechanicAlert("클라이맥스 맥동 "+until.ToString("0.0")+"초 후 · 파티 회복",new Color(.75f,.35f,1),until);
            if(!MarionetteHardRules.PulseDue(_marRemaining,_marHardNextPulse))return;
            _marHardNextPulse-=marionetteHard.climaxPulseInterval;
            foreach(var member in GetLivingPartyMemberTransforms())
                DamagePartyMember(member,marionetteHard.climaxPulseDamage,CombatDamageKind.Area,"marionette.hard.climax.pulse");
            CombatMetricsManager.Event("클라이맥스 맥동 피해");
        }
        private void StopMarionetteHardPulse(){_marHardNextPulse=0;}
        private void BeginMarionetteHardTransformation()
        {
            ClearMarionetteAttack();ClearMarionetteHardDoll();ClearMarionetteHardCrossing();ClearMarionetteHardRecoil();
            _marHardMemoryPending=false;
            _marStep=MarionetteStep.Transformation;fsmState=BossFSMState.CastingSkill;
            _marRemaining=marionetteHard.transformationSeconds;
            var template=marionetteHard.transformationPresentation;
            if(template!=null)
            {
                try
                { _marHardPresentation=Instantiate(template,transform.position,Quaternion.identity,transform);
                  if(_marHardPresentation.Play(this,StageManager.Instance?.player,0))
                      _marRemaining=RaidStageCutscenePresentation.Duration(_marHardPresentation.DirectorAsset,_marRemaining);
                  else StopMarionetteHardPresentation(); }
                catch(System.Exception e){Debug.LogWarning("Marionette hard transformation fallback: "+e);StopMarionetteHardPresentation();}
            }
            _castTotalDuration=_castRemainingTime=_marRemaining;
            _currentCastSkillName="실에 매달린 비극 · 변신";
            if(_marHardPresentation==null)ShowTemporaryMarionettePose(MarionettePattern.Climax);
            SetMechanicAlert("보스도 황금 실에 매달립니다 · 변신 연출",new Color(.75f,.4f,.8f),_marRemaining);
            PlayMarionetteCue(marionetteHard.transformationCue);
        }
        private void FinishMarionetteHardTransformation()
        {
            StopMarionetteHardPresentation();ClearMarionetteAttack();
            _marHardTransformed=true;_marHardReplicaSkipped=false;
            RestoreHardTransformationHealth();
            ShowMarionetteHardModel();
            _marStep=MarionetteStep.Rest;fsmState=BossFSMState.IdleChasing;
            _marRemaining=0;_marRest=marionetteHard.transformationRecoverySeconds;
            _marHardRestProtected=true;
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
        }
        private void ShowMarionetteHardModel()
        {
            currentBossTitle="실에 매달린 비극, 마리오네트";
            if(_marModel!=null)_marModel.SetActive(false);
            if(_marHardModel!=null)Destroy(_marHardModel);
            if(marionetteHard.transformedVisualPrefab!=null)
            {
                _marHardModel=Instantiate(marionetteHard.transformedVisualPrefab,transform);
                foreach(var collider in _marHardModel.GetComponentsInChildren<Collider>())collider.enabled=false;
                var animator=_marHardModel.GetComponentInChildren<Animator>();
                if(animator!=null&&marionetteHard.transformedAnimatorController!=null)
                    animator.runtimeAnimatorController=marionetteHard.transformedAnimatorController;
            }
            else
            {
                _marHardModel=new GameObject("Temporary split-mask suspended puppeteer · replace in marionetteHard");
                _marHardModel.transform.SetParent(transform,false);
                HardMarionettePart(PrimitiveType.Sphere,"Smiling mask half",new Vector3(-.65f,3,0),new Vector3(.7f,1.3f,.6f));
                HardMarionettePart(PrimitiveType.Sphere,"Crying mask half",new Vector3(.65f,3,0),new Vector3(.7f,1.3f,.6f));
                HardMarionettePart(PrimitiveType.Cylinder,"Exposed golden drive core",new Vector3(0,2.1f,0),new Vector3(1.5f,1.4f,1.5f));
                HardMarionettePart(PrimitiveType.Capsule,"Reachable lower body",new Vector3(0,.8f,0),new Vector3(1.3f,1.6f,1.3f));
                for(int i=0;i<4;i++)HardMarionettePart(PrimitiveType.Capsule,"Puppet arm "+i,
                    new Vector3(i<2?-1.8f:1.8f,i%2==0?2.5f:1.5f,.2f),new Vector3(.25f,2.1f,.25f));
            }
        }
        private void HardMarionettePart(PrimitiveType type,string name,Vector3 at,Vector3 scale)
        {
            var obj=RaidPrimitive.Create(type);obj.name=name;obj.transform.SetParent(_marHardModel.transform,false);
            obj.transform.localPosition=at;obj.transform.localScale=scale;obj.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(.72f,.53f,.25f));
            block.SetColor("_Color",new Color(.72f,.53f,.25f));obj.GetComponent<Renderer>().SetPropertyBlock(block);
        }
        private void PlayMarionetteHardTrigger(string trigger)
        {
            var visual=_marHardTransformed&&_marHardModel!=null?_marHardModel:_marModel;
            var animator=visual!=null?visual.GetComponentInChildren<Animator>():null;
            if(animator!=null&&!string.IsNullOrEmpty(trigger))
                foreach(var parameter in animator.parameters)
                    if(parameter.name==trigger&&parameter.type==AnimatorControllerParameterType.Trigger)
                    {animator.SetTrigger(trigger);return;}
            if(_marPattern!=MarionettePattern.Reenact)ShowTemporaryMarionettePose(_marPattern);
        }
        private void StopMarionetteHardPresentation()
        {
            if(_marHardPresentation==null)return;
            _marHardPresentation.StopAndRestore();_marHardPresentation.gameObject.SetActive(false);
            Destroy(_marHardPresentation.gameObject);_marHardPresentation=null;
        }
        private bool MarionetteHardSkipPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;
            return keyboard!=null&&(marionetteHard.cutsceneSkipKey==KeyCode.Escape?
                keyboard.escapeKey.wasPressedThisFrame:keyboard.spaceKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(marionetteHard.cutsceneSkipKey);
#else
            return false;
#endif
        }
        private void TickMarionetteHardReplicaInput()
        {
            if(_marStep!=MarionetteStep.Transformation||_marHardReplicaSkipped||LocalGameplayInput.ModalCovered)return;
            if(!MarionetteHardSkipPressed())return;
            _marHardReplicaSkipped=true;StopMarionetteHardPresentation();LocalGameplayInput.SuppressThisFrame();
        }
        private void ApplyMarionetteHardReplica(RaidCombatEnemy state,MarionetteStep previous)
        {
            if(_marStep==MarionetteStep.Transformation&&previous!=MarionetteStep.Transformation&&
                !_marHardReplicaSkipped&&marionetteHard.transformationPresentation!=null)
            {
                try
                { _marHardPresentation=Instantiate(marionetteHard.transformationPresentation,transform.position,Quaternion.identity,transform);
                  if(!_marHardPresentation.Play(this,StageManager.Instance?.player,
                      Mathf.Max(0,state.castTotal-state.castRemaining)))StopMarionetteHardPresentation(); }
                catch(System.Exception){StopMarionetteHardPresentation();}
            }
            if(_marStep!=MarionetteStep.Transformation&&previous==MarionetteStep.Transformation)
            {StopMarionetteHardPresentation();_marHardReplicaSkipped=false;}
            if(state.marHardTransformed&&!_marHardTransformed)
            {_marHardTransformed=true;ShowMarionetteHardModel();}
            if(!state.marHardTransformed&&_marHardTransformed)
            {_marHardTransformed=false;if(_marHardModel!=null)Destroy(_marHardModel);_marHardModel=null;
             if(_marModel!=null)_marModel.SetActive(true);}
            _marHardClimaxPreparing=state.marHardClimaxPreparing;
            _marHardMemoryPattern=(MarionettePattern)state.marHardMemoryPattern;
            _marHardMemoryRight=state.marHardMemoryRight;
            _marHardMemoryForward=Quaternion.Euler(0,state.marHardMemoryYaw,0)*Vector3.forward;
            _marHardDollPoint=new Vector3(state.marHardDollX,0,state.marHardDollZ);
            _marHardRecoilPoint=new Vector3(state.marHardRecoilX,0,state.marHardRecoilZ);
            _marHardStringMask=state.marHardStringMask;
            if(_marStep==MarionetteStep.Control&&_marTarget!=null)
            {
                if(_marHardStringLabel==null)_marHardStringLabel=FloraLabel("",new Color(1,.8f,.3f));
                _marHardStringLabel.text="황금 실 "+(((_marHardStringMask&1)!=0?1:0)+((_marHardStringMask&2)!=0?1:0))+"/2 절단";
                _marHardStringLabel.transform.position=_marTarget.position+Vector3.up*3.8f;
                if(Camera.main!=null)_marHardStringLabel.transform.rotation=Camera.main.transform.rotation;
            }
            else if(_marHardStringLabel!=null){Destroy(_marHardStringLabel.gameObject);_marHardStringLabel=null;}
            if((_marStep==MarionetteStep.ReenactDelay||_marStep==MarionetteStep.ReenactCast)&&_marHardDoll==null)
                SpawnMarionetteHardDoll();
            if(_marStep!=MarionetteStep.ReenactDelay&&_marStep!=MarionetteStep.ReenactCast)ClearMarionetteHardDoll();
            if(_marStep==MarionetteStep.CrossingFirst||_marStep==MarionetteStep.CrossingSecond)
            {
                ShowMarionetteHardCrossingGuides();
            }
            else if(_marStep==MarionetteStep.CrossingGap)ShowMarionetteHardCrossingGuides();
            else ClearMarionetteHardCrossing();
        }
        private bool MarionetteHardDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;
            if(_marStep==MarionetteStep.HardResidue)
            {
                foreach(var point in _marHardResiduePoints)
                    if(FloraNormalRules.InCircle(actor.position,point,marionetteHard.lightResidueRadius+1))
                    {Vector3 away=actor.position-point;away.y=0;
                     if(away.sqrMagnitude<.01f)away=role%2==0?Vector3.right:Vector3.left;
                     destination=MarionetteNormalRules.ClampToStage(point+away.normalized*(marionetteHard.lightResidueRadius+2),MarTuning.stageHalfSize);
                     destination.y=actor.position.y;return true;}
                return false;
            }
            if(_marStep==MarionetteStep.Recoil)
            {Vector3 away=actor.position-_marHardRecoilPoint;away.y=0;
             if(away.sqrMagnitude<.01f)away=role%2==0?Vector3.right:Vector3.left;
             destination=MarionetteNormalRules.ClampToStage(_marHardRecoilPoint+away.normalized*(marionetteHard.recoilRadius+2),MarTuning.stageHalfSize);
             destination.y=actor.position.y;return true;}
            if(_marStep==MarionetteStep.ReenactDelay||_marStep==MarionetteStep.ReenactCast)
            {Vector3 side=Vector3.Cross(Vector3.up,_marHardMemoryForward);
             destination=_marHardMemoryPattern==MarionettePattern.HalfCurtain?
                 _marHardDollPoint+side*(_marHardMemoryRight?-8:8):
                 _marHardDollPoint+(actor.position-_marHardDollPoint).normalized*(MarTuning.centerRadius+2);
             destination=MarionetteNormalRules.ClampToStage(destination,MarTuning.stageHalfSize);destination.y=actor.position.y;return true;}
            if(_marStep==MarionetteStep.HardFollowup)
            {if(_marPattern==MarionettePattern.TragicCenter)
                 destination=_marOrigin+(actor.position-_marOrigin).normalized*3;
             else if(_marPattern==MarionettePattern.HalfCurtain)
                 destination=_marOrigin+Vector3.Cross(Vector3.up,_marForward)*(_marRight?8:-8);
             else return false;
             destination=MarionetteNormalRules.ClampToStage(destination,MarTuning.stageHalfSize);destination.y=actor.position.y;return true;}
            if(_marStep==MarionetteStep.CrossingFirst||_marStep==MarionetteStep.CrossingSecond||
                _marStep==MarionetteStep.CrossingGap)
            {destination=new Vector3(_marStep==MarionetteStep.CrossingFirst||
                 _marStep==MarionetteStep.CrossingGap&&_marHardCrossingElapsed<0?-6:6,actor.position.y,
                 role<=2?6:-6);return true;}
            if(_marStep==MarionetteStep.ClimaxRegroup)
            {destination=new Vector3((role-2.5f)*1.5f,actor.position.y,-5);return true;}
            return false;
        }
    }
}
