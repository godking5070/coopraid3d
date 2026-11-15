using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        [Header("Stage 5 normal: Justitia (initial tuning)")]
        public JustitiaNormalSettings justitia=new JustitiaNormalSettings();
        [Header("Stage 5 hard: separate initial tuning")]
        public JustitiaHardSettings justitiaHard=new JustitiaHardSettings();
        public bool JustitiaHard => currentStageNumber==5 && !CharacterTrainingSession.Active &&
            CampaignRuntime.Instance?.DifficultyId==CampaignCatalog.Hard;
        public bool JustitiaEncounter => currentStageNumber==5 && !CharacterTrainingSession.Active &&
            (CampaignRuntime.Instance==null || CampaignRuntime.Instance.DifficultyId==CampaignCatalog.Normal || JustitiaHard);
        private JustitiaNormalSettings JustTuning => JustitiaHard ? justitiaHard : justitia;
        public float JustitiaHalfWidth => JustTuning.halfWidth;
        public float JustitiaHalfDepth => JustTuning.halfDepth;
        public string JustitiaMassLabel(Transform member)
        {
            if(!JustitiaEncounter||_justStep!=JustitiaStep.Balance)return "";
            int index=_justParticipants.IndexOf(member);
            return index>=0&&index<_justMass.Count?" <color=#F5C854>[⚖"+_justMass[index]+"]</color>":"";
        }
        private JustitiaPattern _justPattern;
        private JustitiaStep _justStep;
        private bool _justDamageStaggerGroggy;
        private float _justRemaining,_justRest,_justStagger;
        private float _justBaseAutoAttackInterval;
        private bool _justAutoAttackCaptured;
        private int _justNext=1,_justBalanceRetry,_justAiMask=-1,_justDialogueBeat=-1;
        private bool _justVerdictUsed,_justStoryUsed,_justCounterOpen,_justMarkLocked,_justAiReplanned;
        private Transform _justTarget;
        private Vector3 _justOrigin,_justForward;
        private bool _justRight;
        private readonly List<Transform> _justParticipants=new List<Transform>();
        private readonly List<int> _justMass=new List<int>();
        private readonly List<TextMesh> _justMassLabels=new List<TextMesh>();
        private readonly List<AoETelegraph> _justGround=new List<AoETelegraph>();
        private readonly List<AttachedPartyMechanicMarker> _justMarkers=new List<AttachedPartyMechanicMarker>();
        private readonly List<Vector3> _justMarkPoints=new List<Vector3>();
        private GameObject _justModel,_justPose;
        private Renderer[] _justHiddenBody;
        private TextMesh _justCounterIcon;
        private AudioSource _justAudio;
        private Coroutine _justStory;

        public bool JustitiaAiCounterReady(Transform actor)=>JustitiaEncounter && _justStep==JustitiaStep.Cast &&
            _justPattern==JustitiaPattern.Charge && _justCounterOpen && CombatRange.LivingPartyMember(actor);
        public bool JustitiaTankbusterImminent(Transform actor,float within)=>JustitiaEncounter &&
            ((_justStep==JustitiaStep.Cast && _justPattern==JustitiaPattern.Execution)||
             (JustitiaHard&&_justStep==JustitiaStep.HardFollowup&&_justHardFollowupPattern==JustitiaPattern.Execution)) &&
            _justTarget==actor && _justRemaining<=within;

        private void ResetJustitia()
        {
            ClearJustitia();justitia=justitia??new JustitiaNormalSettings();
            justitiaHard=justitiaHard??new JustitiaHardSettings();ResetJustitiaHard();
            _justNext=1;_justVerdictUsed=true;_justStoryUsed=false;_justRest=JustitiaHard?JustitiaHardRules.Tempo(JustTuning.patternInterval,justitiaHard.attackTempo):JustTuning.patternInterval;
            if(!JustitiaEncounter)return;
            currentBossTitle="성채 재판소 : 죄업을 재는 천칭, 유스티티아";
            maxHp=RaidBossHealthTuning.Resolve(JustTuning.health,JustitiaHard);currentHp=maxHp;
            if(!_justAutoAttackCaptured){_justBaseAutoAttackInterval=autoAttackInterval;_justAutoAttackCaptured=true;}
            autoAttackInterval=JustitiaHard?JustitiaHardRules.Tempo(_justBaseAutoAttackInterval,justitiaHard.attackTempo):_justBaseAutoAttackInterval;
            arenaBoundaryRadius=Mathf.Sqrt(JustTuning.halfWidth*JustTuning.halfWidth+JustTuning.halfDepth*JustTuning.halfDepth);
            autoAttackDamageToTank=120;
            _stagePhase1BodyColor=new Color(.78f,.64f,.25f);SetBossBodyColor(_stagePhase1BodyColor);
            var phaseOneVisual=JustTuning.bossVisualPrefab??(JustitiaHard?justitia.bossVisualPrefab:null);
            if(phaseOneVisual!=null)
            {
                _justModel=Instantiate(phaseOneVisual,transform);_justModel.name="Justitia authored visual";
                foreach(var collider in _justModel.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            else
            {
                _justModel=new GameObject("Temporary golden clockwork angel · replace in justitia settings");
                _justModel.transform.SetParent(transform,false);
                JustitiaPart(PrimitiveType.Capsule,"Golden angel body",new Vector3(0,1.8f,0),new Vector3(1.8f,3,1.2f),new Color(.72f,.57f,.2f));
                JustitiaPart(PrimitiveType.Sphere,"Blindfolded face",new Vector3(0,3.6f,0),new Vector3(1.3f,1,.8f),new Color(.8f,.7f,.4f));
                JustitiaPart(PrimitiveType.Cube,"Judgment greatsword",new Vector3(1.7f,1.8f,.4f),new Vector3(.25f,3.8f,.65f),new Color(.9f,.76f,.3f));
                JustitiaPart(PrimitiveType.Cube,"Back balance beam",new Vector3(0,3,-.7f),new Vector3(5,.12f,.15f),new Color(.95f,.78f,.25f));
            }
            var body=GetBossBodyTransform();
            if(body!=null){_justHiddenBody=body.GetComponentsInChildren<Renderer>();foreach(var renderer in _justHiddenBody)renderer.enabled=false;}
            var animator=_justModel.GetComponentInChildren<Animator>();
            var phaseOneAnimator=JustTuning.bossAnimatorController??(JustitiaHard?justitia.bossAnimatorController:null);
            if(animator!=null&&phaseOneAnimator!=null)animator.runtimeAnimatorController=phaseOneAnimator;
        }
        private void JustitiaPart(PrimitiveType primitive,string label,Vector3 point,Vector3 scale,Color color)
        {
            var obj=RaidPrimitive.Create(primitive);obj.name=label;obj.transform.SetParent(_justModel.transform,false);
            obj.transform.localPosition=point;obj.transform.localScale=scale;obj.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            obj.GetComponent<Renderer>().SetPropertyBlock(block);
        }
        private void ClearJustitia(bool keepModel=false)
        {
            ClearJustitiaHard(keepModel);
            ClearJustitiaAttack();ClearJustitiaMass();
            if(_justStory!=null){StopCoroutine(_justStory);_justStory=null;}
            if(!keepModel&&_justModel!=null)Destroy(_justModel);
            if(!keepModel)_justModel=null;
            if(!keepModel&&_justHiddenBody!=null)
            {foreach(var renderer in _justHiddenBody)if(renderer!=null)renderer.enabled=true;_justHiddenBody=null;}
            _justPattern=JustitiaPattern.None;_justStep=JustitiaStep.Rest;_justTarget=null;
            _justRemaining=_justStagger=0;_justCounterOpen=false;_justDamageStaggerGroggy=false;_currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
            if(StageManager.Instance?.currentStage==5)FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);
        }
        private void ClearJustitiaAttack()
        {
            ClearJustitiaHardGuides();
            GetComponent<CombatCounterTarget>()?.CloseWindow();_justCounterOpen=false;
            if(_justCounterIcon!=null)Destroy(_justCounterIcon.gameObject);_justCounterIcon=null;
            if(_justPose!=null)Destroy(_justPose);_justPose=null;
            foreach(var ground in _justGround)if(ground!=null)Destroy(ground.gameObject);_justGround.Clear();
            foreach(var marker in _justMarkers)if(marker!=null)Destroy(marker.gameObject);_justMarkers.Clear();
            _justMarkPoints.Clear();
        }
        private void ClearJustitiaMass()
        {
            foreach(var label in _justMassLabels)if(label!=null)Destroy(label.gameObject);
            _justMassLabels.Clear();_justParticipants.Clear();_justMass.Clear();_justAiMask=-1;
        }
        private void JustitiaGround(AoETelegraphType type,Vector3 point,Vector3 direction,float seconds,float inner,float outer,float angle=120)
        {_justGround.Add(AoETelegraph.Spawn(type,point,direction,seconds,inner,outer,angle));}
        private void PlayJustitiaCue(AudioClip clip)
        {if(clip==null)return;if(_justAudio==null)_justAudio=GetComponent<AudioSource>()??gameObject.AddComponent<AudioSource>();_justAudio.PlayOneShot(clip);}
        private void PlayJustitiaTrigger()
        {
            string key=_justPattern==JustitiaPattern.Execution?JustTuning.tankTrigger:_justPattern==JustitiaPattern.HalfSlash?JustTuning.halfTrigger:
                _justPattern==JustitiaPattern.JudgmentMarks?JustTuning.markTrigger:_justPattern==JustitiaPattern.ScaleVibration?JustTuning.centerTrigger:
                _justPattern==JustitiaPattern.SharedGuilt?JustTuning.stackTrigger:_justPattern==JustitiaPattern.Charge?JustTuning.chargeTrigger:
                _justPattern==JustitiaPattern.Balance?JustTuning.balanceTrigger:
                _justPattern==JustitiaPattern.DoubleEdged?justitiaHard.doubleEdgeTrigger:
                _justPattern==JustitiaPattern.EvidenceErasure?justitiaHard.evidenceTrigger:
                _justPattern==JustitiaPattern.UnfairWeight?justitiaHard.weightTrigger:JustTuning.verdictTrigger;
            var animator=(JustitiaHard&&_justHardTransformed?_justHardModel:_justModel)?.GetComponentInChildren<Animator>();
            if(animator!=null&&!string.IsNullOrEmpty(key))foreach(var p in animator.parameters)
                if(p.name==key&&p.type==AnimatorControllerParameterType.Trigger){animator.SetTrigger(key);return;}
            if(_justPose!=null)Destroy(_justPose);
            _justPose=RaidPrimitive.Create(PrimitiveType.Cube);_justPose.name="Temporary judgment preparation";
            _justPose.transform.SetParent(transform,false);_justPose.transform.localPosition=new Vector3(0,3,1);
            _justPose.transform.localScale=new Vector3(.3f,2,.3f);_justPose.GetComponent<Collider>().enabled=false;
        }
        public bool JustitiaStaggerLocked => !JustitiaEncounter || currentHp<=0 ||
            (_justStep!=JustitiaStep.Rest && _justStep!=JustitiaStep.Cast &&
             _justStep!=JustitiaStep.HardFollowup && _justStep!=JustitiaStep.HardResidue) ||
            (_justStep==JustitiaStep.Cast && (_justPattern==JustitiaPattern.Balance ||
             _justPattern==JustitiaPattern.UnfairWeight || _justPattern==JustitiaPattern.Verdict)) ||
            JustitiaStaggerPriorityDue;
        private bool JustitiaStaggerPriorityDue => JustitiaHard ? JustitiaHardRules.Due(_justHardTransformed,_justHardIntroDone,_justHardClimaxStarted,
                HpRatio,justitiaHard.transformationThreshold,justitiaHard.climaxThreshold)!=JustitiaHardSpecial.None :
                !_justVerdictUsed && HpRatio<=JustTuning.verdictThreshold;

        public bool TryJustitiaStaggerGroggy(float seconds)
        {
            if(RaidCombatAuthority.Replica || StageManager.CombatSuspended || JustitiaStaggerLocked ||
                float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds<=0)return false;
            EndJustitia(true,seconds);
            _justDamageStaggerGroggy=true;
            return true;
        }

        private void TickJustitia()
        {
            float dt=Time.deltaTime;
            if(JustitiaHard && TickJustitiaHard(dt)) { }
            else if(_justStep==JustitiaStep.Rest)
            {
                fsmState=BossFSMState.IdleChasing;
                RotateTowardsAggroTargetXZ();UpdateMeleePursuit(dt);UpdateAutoAttack();
                _justRest-=dt;
                if(_justRest<=0)
                {
                    if(JustitiaHard && TryBeginJustitiaHardSpecial()) { }
                    else if(!JustitiaHard&&!_justVerdictUsed&&HpRatio<=JustTuning.verdictThreshold)BeginJustitia(JustitiaPattern.Verdict);
                    else
                    {
                        var next=JustitiaHard?NextJustitiaHardPattern():(JustitiaPattern)_justNext;
                        if(!JustitiaHard)_justNext=_justNext>=7?1:_justNext+1;
                        if(next==JustitiaPattern.Balance&&GetLivingPartyMemberTransforms().Count<2)
                        {SetMechanicAlert("천칭 보류 · 생존자 2명 미만",Color.yellow,2);_justRest=JustTuning.recoverySeconds;}
                        else BeginJustitia(next);
                    }
                }
            }
            else if(_justStep==JustitiaStep.Groggy)
            {
                if(_justDamageStaggerGroggy && JustitiaStaggerPriorityDue){EndJustitia();_justRest=0;}
                else {_groggyRemainingTime=_justRemaining=Mathf.Max(0,_justRemaining-dt);if(_justRemaining<=0)EndJustitia();}
            }
            else
            {
                _justRemaining=Mathf.Max(0,_justRemaining-dt);_castRemainingTime=_justRemaining;
                if(_justStep==JustitiaStep.Cast)TickJustitiaCast();
                else if(_justStep==JustitiaStep.Balance)TickJustitiaBalance();
                else if(_justStep==JustitiaStep.Verdict)
                {TickJustitiaDialogue();if(JustitiaHard)TickJustitiaHardVerdict();if(_justRemaining<=0)FinishJustitiaVerdict(false);}
            }
            if(!_justStoryUsed&&currentHp>0&&HpRatio<=JustTuning.storyThreshold)
            {_justStoryUsed=true;_justStory=StartCoroutine(JustitiaStory());}
            UpdateTopBossUI();UpdateCastBanner();UpdateJustitiaCastUI();
        }
        private void UpdateJustitiaCastUI()
        {
            if(!JustitiaEncounter)return;
            if(bossPhaseText!=null)bossPhaseText.text=JustitiaHard?(_justHardTransformed?"눈뜬 절대심판 · 하드 2페이즈":"성채 재판소 · 유스티티아 · 하드"):"성채 재판소 · 유스티티아 · 노멀";
            UpdateJustitiaLabels();
            if(_justStep!=JustitiaStep.Verdict)return;
            if(bossCastFillImage!=null){bossCastFillImage.fillAmount=Mathf.Clamp01(_justStagger/Mathf.Max(1,JustTuning.verdictStagger));bossCastFillImage.color=new Color(.7f,.2f,1);}
            if(bossCastText!=null)bossCastText.text="보라색 무력화 "+_justStagger.ToString("0")+"/"+JustTuning.verdictStagger.ToString("0")+
                " · "+_justRemaining.ToString("0.0")+"초"+
                (JustitiaHard&&_justHardNextPulse<float.PositiveInfinity?" · 다음 맥동 "+Mathf.Max(0,_justHardNextPulse).ToString("0.0")+"초":"");
        }
        private IEnumerator JustitiaStory()
        {
            while(currentHp>0&&_justStep!=JustitiaStep.Rest&&_justStep!=JustitiaStep.Groggy)yield return null;
            if(currentHp<=0)yield break;
            FindFirstObjectByType<RaidLobbyUI>()?.Say("유스티티아 · 무지한 자들이여... 그 죄책감을 어찌 감당하려 하는가...");
            yield return new WaitForSeconds(3);
            if(currentHp>0)FindFirstObjectByType<RaidLobbyUI>()?.Say("유스티티아 · 이제 마지막 열두 시의 관마저 열리는구나...");
            _justStory=null;
        }
        private void BeginJustitia(JustitiaPattern pattern)
        {
            ClearJustitiaAttack();ClearJustitiaMass();_justPattern=pattern;
            _justStep=pattern==JustitiaPattern.Balance||pattern==JustitiaPattern.UnfairWeight?JustitiaStep.Balance:pattern==JustitiaPattern.Verdict?JustitiaStep.Verdict:JustitiaStep.Cast;
            fsmState=BossFSMState.CastingSkill;
            _justRemaining=_justStep==JustitiaStep.Balance?(JustitiaHard&&pattern==JustitiaPattern.UnfairWeight?justitiaHard.unfairWeightSeconds:JustitiaHard?justitiaHard.remeasureSeconds:JustTuning.balanceSeconds):_justStep==JustitiaStep.Verdict?JustTuning.verdictSeconds:JustitiaHard?JustitiaHardRules.Tempo(JustTuning.castSeconds,justitiaHard.attackTempo):JustTuning.castSeconds;
            _castTotalDuration=_castRemainingTime=_justRemaining;
            _justOrigin=transform.position;_justForward=transform.forward;_justRight=Random.value>=.5f;
            _justTarget=ResolveLivingAggroTarget();_justMarkLocked=false;_justBalanceRetry=0;_justAiReplanned=false;_justDialogueBeat=-1;
            PlayJustitiaTrigger();
            if(JustitiaHard&&_justStep==JustitiaStep.Cast)StartJustitiaHardCast(pattern);
            switch(pattern)
            {
                case JustitiaPattern.Execution:
                    _currentCastSkillName=JustitiaHard?"집행자의 일격 · 2연타":"집행자의 일격";
                    if(_justTarget!=null)_justMarkers.Add(AttachedPartyMechanicMarker.SpawnTargetMarker(_justTarget,1.5f));
                    SetMechanicAlert("집행자의 일격 · 탱커 방어와 회복",Color.red,_justRemaining);break;
                case JustitiaPattern.HalfSlash:
                    _currentCastSkillName=JustitiaHard?"편향된 참격 · "+(_justRight?"오른쪽 → 왼쪽":"왼쪽 → 오른쪽"):"편향된 참격";
                    if(!JustitiaHard)JustitiaGround(AoETelegraphType.FrontCone_120Deg,_justOrigin,Vector3.Cross(Vector3.up,_justForward)*(_justRight?1:-1),_justRemaining,0,JustTuning.halfWidth,180);
                    SetMechanicAlert(JustitiaHard?_currentCastSkillName+" · 첫 공격 후 이미 맞은 쪽으로 이동":"편향된 참격 · 대검 반대편으로 회피",Color.red,_justRemaining);break;
                case JustitiaPattern.JudgmentMarks:
                    _currentCastSkillName="판결의 낙인";
                    foreach(var member in GetLivingPartyMemberTransforms())_justMarkers.Add(AttachedPartyMechanicMarker.SpawnTargetMarker(member,JustTuning.markRadius));
                    SetMechanicAlert("판결의 낙인 · 산개, 위치 고정 후 이탈",Color.red,_justRemaining);break;
                case JustitiaPattern.ScaleVibration:
                    _currentCastSkillName=JustitiaHard?"천칭의 진동 · 중심 → 외곽":"천칭의 진동";
                    if(!JustitiaHard)JustitiaGround(AoETelegraphType.Circle_Radius6,_justOrigin,_justForward,_justRemaining,0,JustTuning.centerRadius);
                    SetMechanicAlert("천칭의 진동 · 원형 밖으로 이동",Color.red,_justRemaining);break;
                case JustitiaPattern.SharedGuilt:
                    _currentCastSkillName="연대 책임";
                    var party=GetLivingPartyMemberTransforms();_justTarget=party.Count>0?party[Random.Range(0,party.Count)]:null;
                    if(_justTarget!=null)_justMarkers.Add(AttachedPartyMechanicMarker.SpawnStackMarker(_justTarget,JustTuning.stackRadius));
                    SetMechanicAlert("연대 책임 · 집합해 피해 분담",new Color(1,.65f,.2f),_justRemaining);break;
                case JustitiaPattern.Charge:
                    _currentCastSkillName="집행 돌진";
                    if(!JustitiaHard)JustitiaGround(AoETelegraphType.Rectangle,_justOrigin,_justForward,_justRemaining,JustTuning.chargeWidth,JustTuning.chargeLength);
                    SetMechanicAlert("⚔ 금색 일반 카운터 · 정면 기술 또는 직선 회피",new Color(1,.76f,.15f),_justRemaining);break;
                case JustitiaPattern.DoubleEdged:
                    _currentCastSkillName="양날 판결 · 정면 → 후방";
                    SetMechanicAlert("I 정면, II 후방 · 첫 타격 후 정면으로 복귀",Color.red,_justRemaining);break;
                case JustitiaPattern.EvidenceErasure:
                    _currentCastSkillName="증거 말소 · I "+(_justRight?"북쪽":"남쪽")+" → II 반대쪽";
                    SetMechanicAlert(_currentCastSkillName+" · 첫 공격 구역으로 이동",Color.red,_justRemaining);break;
                case JustitiaPattern.Balance:BeginJustitiaBalance();break;
                case JustitiaPattern.UnfairWeight:BeginJustitiaBalance();break;
                case JustitiaPattern.Verdict:BeginJustitiaVerdict();break;
            }
        }
        private void TickJustitiaCast()
        {
            if(JustitiaHard)TickJustitiaHardCast();
            if(_justPattern==JustitiaPattern.Charge&&!_justCounterOpen&&_justRemaining<=JustTuning.counterWindow)
            {
                _justCounterOpen=true;CombatCounterTarget.Ensure(transform).OpenWindow(_justRemaining);
                _justCounterIcon=FloraLabel("⚔ COUNTER",new Color(1,.78f,.15f));
                _justCounterIcon.transform.position=transform.position+Vector3.up*4.5f;
                PlayJustitiaCue(JustTuning.counterCue);
            }
            if(_justPattern==JustitiaPattern.JudgmentMarks&&!_justMarkLocked&&_justRemaining<=JustTuning.markLockSeconds)
            {
                _justMarkLocked=true;
                foreach(var marker in _justMarkers)if(marker!=null&&CombatRange.LivingPartyMember(marker.followTarget))
                {var point=marker.followTarget.position;_justMarkPoints.Add(point);
                 if(JustitiaHard)ShowJustitiaHardMarkGuide(point);
                 else JustitiaGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,_justRemaining,0,JustTuning.markRadius);}
                foreach(var marker in _justMarkers)if(marker!=null)Destroy(marker.gameObject);_justMarkers.Clear();
                SetMechanicAlert("판결의 낙인 고정 · 표시 범위에서 이탈",Color.red,_justRemaining);
            }
            if(_justRemaining<=0)ResolveJustitiaCast();
        }
        private void ResolveJustitiaCast()
        {
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                bool hit=false;float damage=0;
                switch(_justPattern)
                {
                    case JustitiaPattern.Execution:hit=member==_justTarget;damage=JustTuning.tankDamage;break;
                    case JustitiaPattern.HalfSlash:hit=FloraNormalRules.InHalf(member.position,_justOrigin,Vector3.Cross(Vector3.up,_justForward),JustTuning.halfWidth,_justRight);damage=JustTuning.halfDamage;break;
                    case JustitiaPattern.JudgmentMarks:
                        foreach(var point in _justMarkPoints)if(FloraNormalRules.InCircle(member.position,point,JustTuning.markRadius)){hit=true;break;}
                        damage=JustTuning.markDamage;break;
                    case JustitiaPattern.ScaleVibration:hit=FloraNormalRules.InCircle(member.position,_justOrigin,JustTuning.centerRadius);damage=JustTuning.centerDamage;break;
                    case JustitiaPattern.Charge:hit=JustitiaHard?JustitiaHardChargeHit(member.position):
                        _justGround.Count>0&&_justGround[0]!=null&&_justGround[0].IsPointInsideXZ(member.position);
                        damage=JustTuning.chargeDamage;break;
                    case JustitiaPattern.DoubleEdged:
                    case JustitiaPattern.EvidenceErasure:hit=JustitiaHardFirstHit(member.position);damage=JustTuning.halfDamage;break;
                }
                if(hit)DamagePartyMember(member,damage,_justPattern==JustitiaPattern.Execution?CombatDamageKind.Direct:CombatDamageKind.Area,"JustTuning."+_justPattern);
            }
            if(_justPattern==JustitiaPattern.SharedGuilt&&_justTarget!=null&&CombatRange.LivingPartyMember(_justTarget))
            {
                int count=0;var living=GetLivingPartyMemberTransforms();
                foreach(var member in living)if(FloraNormalRules.InCircle(member.position,_justTarget.position,JustTuning.stackRadius))count++;
                foreach(var member in living)if(FloraNormalRules.InCircle(member.position,_justTarget.position,JustTuning.stackRadius))
                    DamagePartyMember(member,JustTuning.stackDamage/Mathf.Max(1,count),CombatDamageKind.Area,"JustTuning.shared");
            }
            if(_justPattern==JustitiaPattern.Charge)CombatMetricsManager.Event("유스티티아 일반 카운터 미성공 · 집행 돌진");
            if(!StageManager.CombatSuspended)
            {
                if(JustitiaHard && BeginJustitiaHardFollowup())return;
                EndJustitia(false,0,JustTuning.recoverySeconds);
            }
        }
        private void EndJustitia(bool groggy=false,float groggySeconds=0,float recovery=0)
        {
            _justDamageStaggerGroggy=false;
            NotifyDamageCastEnded();
            ClearJustitiaAttack();ClearJustitiaMass();
            _justStep=groggy?JustitiaStep.Groggy:JustitiaStep.Rest;
            fsmState=groggy?BossFSMState.Groggy:BossFSMState.IdleChasing;
            _justRemaining=groggy?groggySeconds:0;_groggyRemainingTime=_justRemaining;
            _justRest=(JustitiaHard?JustitiaHardRules.Tempo(JustTuning.patternInterval,justitiaHard.attackTempo):JustTuning.patternInterval)+
                (recovery>0?recovery:JustTuning.recoverySeconds);
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
        }
        public void JustitiaCounterSucceeded(Transform attacker)
        {
            if(!JustitiaAiCounterReady(attacker))return;
            CombatMetricsManager.Event("유스티티아 일반 카운터 성공: "+attacker.name);
            SetMechanicAlert("⚔ 집행 돌진 중단 · 그로기",new Color(1,.8f,.25f),2);
            PlayJustitiaCue(JustTuning.counterCue);EndJustitia(true,AegisPaladinData.CounterGroggy);
        }
        private void BeginJustitiaBalance()
        {
            ClearJustitiaMass();var living=GetLivingPartyMemberTransforms();
            if(living.Count<2){CombatMetricsManager.Event("천칭 보류 · 생존자 부족");EndJustitia();return;}
            var values=JustitiaHard&&_justHardWeightActive?ChooseJustitiaHardWeightMass(living.Count):
                JustitiaNormalRules.Candidate(living.Count,Random.Range(0,12));
            for(int i=values.Length-1;i>0;i--){int j=Random.Range(0,i+1);int temp=values[i];values[i]=values[j];values[j]=temp;}
            if(JustitiaHard&&_justHardBalanceKind==JustitiaHardBalanceKind.Remeasure&&_justHardBalanceRound==2)
                EnsureJustitiaHardNewMass(values,living);
            for(int i=0;i<living.Count;i++){_justParticipants.Add(living[i]);_justMass.Add(values[i]);
                var label=FloraLabel("",new Color(1,.85f,.35f));_justMassLabels.Add(label);}
            _justStep=JustitiaStep.Balance;_justRemaining=JustitiaHard?
                (_justHardWeightActive?justitiaHard.unfairWeightSeconds:justitiaHard.remeasureSeconds):JustTuning.balanceSeconds;
            _castTotalDuration=_castRemainingTime=_justRemaining;
            _currentCastSkillName=JustitiaHard?(_justHardWeightActive?"부당한 추 · 수평 심판":
                "질량 재측정 · "+_justHardBalanceRound+"/2"):"천칭 작동: 수평 심판";
            _justAiMask=PlanJustitiaAi();_justAiReplanned=false;
            PlayJustitiaCue(JustTuning.balanceCue);
            SetMechanicAlert(JustitiaHard?(_justHardWeightActive?"⚖ 부당한 추 "+_justHardWeightSide+" 질량 "+justitiaHard.unfairWeightMass+" · 마지막 위치 판정":
                "⚖ 질량 재측정 "+_justHardBalanceRound+"/2 · 마지막 위치 판정"):
                "⚖ 질량을 좌우로 나눠 합계 일치 · 마지막 위치 판정",new Color(1,.78f,.25f),_justRemaining);
            CombatMetricsManager.Event("천칭 작동 시작 · 질량 "+string.Join(",",_justMass));
        }
        private int PlanJustitiaAi()
        {
            int n=_justMass.Count;var human=new JustitiaSide[n];var current=new JustitiaSide[n];var ai=new bool[n];
            for(int i=0;i<n;i++)
            {var member=_justParticipants[i];current[i]=JustitiaNormalRules.Side(member.position.x,JustTuning.centerDeadZone);
             ai[i]=member.GetComponent<PartyBotAI>()!=null||member.GetComponent<NetworkRaidAvatar>()?.Automated==true;
             human[i]=ai[i]?JustitiaSide.Center:current[i];}
            return JustitiaHard&&_justHardWeightActive?
                JustitiaHardRules.BestMask(_justMass.ToArray(),human,current,ai,_justHardWeightSide,justitiaHard.unfairWeightMass):
                JustitiaNormalRules.BestMask(_justMass.ToArray(),human,current,ai);
        }
        private void TickJustitiaBalance()
        {
            if(JustitiaHard&&_justHardWeightActive)TickJustitiaHardWeightVisual(Time.deltaTime);
            foreach(var member in _justParticipants)if(!CombatRange.LivingPartyMember(member))
            {
                if(_justBalanceRetry==0&&GetLivingPartyMemberTransforms().Count>=2)
                {_justBalanceRetry++;CombatMetricsManager.Event("천칭 참가자 사망 · 새 예고 1회");BeginJustitiaBalance();_justBalanceRetry=1;}
                else {CombatMetricsManager.Event("천칭 중단 · 인원 변화");SetMechanicAlert("천칭 중단 · 인원 변화, 전투 재개",Color.yellow,3);EndJustitia();}
                return;
            }
            if(!_justAiReplanned&&_justRemaining>3.5f&&_justRemaining<JustTuning.balanceSeconds-1)
            {
                int candidate=PlanJustitiaAi();
                if(candidate>=0&&candidate!=_justAiMask){_justAiMask=candidate;_justAiReplanned=true;}
            }
            if(_justRemaining>0)return;
            var sides=new JustitiaSide[_justMass.Count];
            for(int i=0;i<sides.Length;i++)sides[i]=JustitiaNormalRules.Side(_justParticipants[i].position.x,JustTuning.centerDeadZone);
            bool success=JustitiaHard&&_justHardWeightActive?
                JustitiaHardRules.BalancedWeight(_justMass.ToArray(),sides,_justHardWeightSide,justitiaHard.unfairWeightMass,out int left,out int right):
                JustitiaNormalRules.Balanced(_justMass.ToArray(),sides,out left,out right);
            CombatMetricsManager.Event("천칭 "+(success?"성공":"실패")+" · 좌 "+left+" / 우 "+right);
            SetMechanicAlert(success?"⚖ 수평 심판 성공 · 재집결":"⚖ 수평 심판 실패 · 파티 충격과 회복",success?Color.green:Color.red,3);
            if(!success)foreach(var member in GetLivingPartyMemberTransforms())
                DamagePartyMember(member,JustTuning.balanceFailureDamage,CombatDamageKind.Area,"JustTuning.balance.failure");
            if(!StageManager.CombatSuspended&&currentHp>0)
            {if(JustitiaHard&&FinishJustitiaHardBalance(success))return;
             EndJustitia(false,0,JustTuning.balanceRecoverySeconds);}
        }
        private void UpdateJustitiaLabels()
        {
            if(_justStep!=JustitiaStep.Balance)return;
            int left=0,right=0;
            for(int i=0;i<_justMass.Count;i++)
            {
                var member=_justParticipants[i];if(member==null||i>=_justMassLabels.Count)continue;
                var side=JustitiaNormalRules.Side(member.position.x,JustTuning.centerDeadZone);
                if(side==JustitiaSide.Left)left+=_justMass[i];else if(side==JustitiaSide.Right)right+=_justMass[i];
                var label=_justMassLabels[i];if(label==null)continue;
                label.text="⚖ "+_justMass[i]+" · "+(side==JustitiaSide.Left?"좌":side==JustitiaSide.Right?"우":"중앙 미배정");
                label.transform.position=member.position+Vector3.up*3.1f;
                if(Camera.main!=null)label.transform.rotation=Camera.main.transform.rotation;
            }
            if(bossCastText!=null)bossCastText.text=JustitiaHard&&_justHardWeightActive?
                "⚖ 좌 "+left+" + 추 "+(_justHardWeightSide<0?justitiaHard.unfairWeightMass:0)+
                " / 우 "+right+" + 추 "+(_justHardWeightSide>0?justitiaHard.unfairWeightMass:0)+
                " · "+_justRemaining.ToString("0.0")+"초":
                "⚖ 좌 "+left+" / 우 "+right+" · "+(JustitiaHard?_justHardBalanceRound+"/2 · ":"")+"최종 판정 "+_justRemaining.ToString("0.0")+"초";
        }
        public bool JustitiaDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;if(!JustitiaEncounter)return false;
            if(JustitiaHard&&JustitiaHardDestination(actor,role,out destination))return true;
            if(_justStep==JustitiaStep.Balance)
            {
                int index=_justParticipants.IndexOf(actor);if(index<0||_justAiMask<0)return false;
                bool left=(_justAiMask&(1<<index))!=0;
                destination=new Vector3(left?-7:7,actor.position.y,(role-2.5f)*1.8f);
                return true;
            }
            if(_justStep!=JustitiaStep.Cast)return false;
            if(_justPattern==JustitiaPattern.SharedGuilt&&_justTarget!=null)
            {destination=_justTarget.position+new Vector3((role-2.5f)*.45f,0,.5f);destination.y=actor.position.y;return true;}
            if(_justPattern==JustitiaPattern.JudgmentMarks)
            {
                var corner=new Vector3(role==1||role==3?-10:10,actor.position.y,role<=2?7:-7);
                if(!_justMarkLocked){destination=corner;return true;}
                var away=actor.position-corner;away.y=0;if(away.sqrMagnitude<.01f)away=Vector3.forward;
                destination=JustitiaNormalRules.Clamp(corner+away.normalized*5,JustTuning.halfWidth,JustTuning.halfDepth);
                destination.y=actor.position.y;return true;
            }
            if(_justPattern==JustitiaPattern.ScaleVibration)
            {var away=actor.position-_justOrigin;away.y=0;if(away.sqrMagnitude<.01f)away=Vector3.forward;
             destination=JustitiaNormalRules.Clamp(_justOrigin+away.normalized*(JustTuning.centerRadius+2),JustTuning.halfWidth,JustTuning.halfDepth);destination.y=actor.position.y;return true;}
            if(_justPattern==JustitiaPattern.HalfSlash)
            {destination=_justOrigin+Vector3.Cross(Vector3.up,_justForward)*(_justRight?-8:8);destination=JustitiaNormalRules.Clamp(destination,JustTuning.halfWidth,JustTuning.halfDepth);destination.y=actor.position.y;return true;}
            if(_justPattern==JustitiaPattern.Charge&&role>=3&&PartyAiTactics.CanTakeCounterPosition(actor))
            {destination=JustitiaNormalRules.Clamp(_justOrigin+_justForward*(role==3?2.3f:4),JustTuning.halfWidth,JustTuning.halfDepth);destination.y=actor.position.y;return true;}
            return false;
        }
        private void BeginJustitiaVerdict()
        {
            _justVerdictUsed=true;
            EndJustitia();
        }
        private void TickJustitiaDialogue()
        {
            if(_justStep!=JustitiaStep.Verdict)return;
            float elapsed=JustTuning.verdictSeconds-_justRemaining;
            int beat=elapsed<2.8f?0:elapsed<5.8f?1:2;
            if(beat==_justDialogueBeat)return;_justDialogueBeat=beat;
            FindFirstObjectByType<RaidLobbyUI>()?.Say(beat==0?
                "유스티티아 · 너희는 성소에 잠든 네 명을 깨우기 위해,":beat==1?
                "유스티티아 · 시계탑 안에서 시간을 붙잡고 있는 ‘선발대 동료 네 명’의 심장 피를 뽑아 마셨다!!":
                "이리스 · 거짓말이에요! 저 말에 귀 기울이지 마세요!");
        }
        public void AddJustitiaStagger(float points)
        {
            if(!JustitiaEncounter||RaidCombatAuthority.Replica||StageManager.CombatSuspended||currentHp<=0||_justStep!=JustitiaStep.Verdict)return;
            if(MarionetteNormalRules.ApplyStagger(ref _justStagger,points,JustTuning.verdictStagger))FinishJustitiaVerdict(true);
        }
        private void FinishJustitiaVerdict(bool success)
        {
            if(_justStep!=JustitiaStep.Verdict)return;
            _justHardNextPulse=float.PositiveInfinity;
            CombatMetricsManager.Event("판결문 낭독 무력화 "+(success?"성공":"실패"));
            SetMechanicAlert(success?"보라색 무력화 성공 · 8초 공격 기회":"유죄 선고 실패 · 파티 충격과 회복",success?Color.green:Color.red,3);
            if(!success)foreach(var member in GetLivingPartyMemberTransforms())
                DamagePartyMember(member,JustTuning.verdictFailureDamage,CombatDamageKind.Area,"JustTuning.verdict.failure");
            if(!StageManager.CombatSuspended&&currentHp>0)EndJustitia(success,success?JustTuning.verdictGroggySeconds:0,JustTuning.verdictRecoverySeconds);
        }
        private void ApplyJustitiaReplica(RaidCombatEnemy state)
        {
            var previous=_justStep;var previousPattern=_justPattern;
            bool newStory=state.justStoryUsed&&!_justStoryUsed;
            _justPattern=(JustitiaPattern)state.justPattern;_justStep=(JustitiaStep)state.justStep;
            _justRemaining=state.castRemaining;_justStagger=state.justStagger;_justVerdictUsed=state.justVerdictUsed;_justStoryUsed=state.justStoryUsed;
            _justRight=state.justRight;
            _justOrigin=new Vector3(state.x,state.y,state.z);
            _justForward=Quaternion.Euler(0,state.yaw,0)*Vector3.forward;
            if(previous!=_justStep||previousPattern!=_justPattern)
            {
                ClearJustitiaAttack();
                if(_justStep==JustitiaStep.Cast&&!JustitiaHard)
                {
                    if(_justPattern==JustitiaPattern.HalfSlash)
                        JustitiaGround(AoETelegraphType.FrontCone_120Deg,_justOrigin,Vector3.Cross(Vector3.up,_justForward)*(_justRight?1:-1),_justRemaining,0,JustTuning.halfWidth,180);
                    else if(_justPattern==JustitiaPattern.ScaleVibration)
                        JustitiaGround(AoETelegraphType.Circle_Radius6,_justOrigin,_justForward,_justRemaining,0,JustTuning.centerRadius);
                    else if(_justPattern==JustitiaPattern.Charge)
                        JustitiaGround(AoETelegraphType.Rectangle,_justOrigin,_justForward,_justRemaining,JustTuning.chargeWidth,JustTuning.chargeLength);
                }
                if(_justStep==JustitiaStep.Cast||_justStep==JustitiaStep.Balance||_justStep==JustitiaStep.Verdict)PlayJustitiaTrigger();
            }
            _justCounterOpen=state.justCounterOpen;
            if(_justStep==JustitiaStep.Cast&&_justPattern==JustitiaPattern.JudgmentMarks&&state.justMarkX!=null&&state.justMarkZ!=null&&_justMarkPoints.Count==0)
                for(int i=0;i<state.justMarkX.Length&&i<state.justMarkZ.Length;i++)
                {
                    var point=new Vector3(state.justMarkX[i],state.y,state.justMarkZ[i]);_justMarkPoints.Add(point);
                    if(JustitiaHard)ShowJustitiaHardMarkGuide(point);
                    else JustitiaGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,_justRemaining,0,JustTuning.markRadius);
                }
            if(_justCounterOpen&&_justCounterIcon==null)
                _justCounterIcon=FloraLabel("⚔ COUNTER",new Color(1,.78f,.15f));
            if(!_justCounterOpen&&_justCounterIcon!=null){Destroy(_justCounterIcon.gameObject);_justCounterIcon=null;}
            if(_justCounterIcon!=null)_justCounterIcon.transform.position=_justOrigin+Vector3.up*4.5f;
            if(_justStep!=JustitiaStep.Balance)ClearJustitiaMass();
            else if(state.justMassIds!=null&&state.justMassValues!=null&&
                (_justMass.Count!=state.justMassValues.Length||!SameJustitiaMass(state)))
            {
                ClearJustitiaMass();
                for(int i=0;i<state.justMassIds.Length&&i<state.justMassValues.Length;i++)
                {var member=RaidCombatAuthority.Resolve(state.justMassIds[i]);if(member==null)continue;
                 _justParticipants.Add(member);_justMass.Add(state.justMassValues[i]);_justMassLabels.Add(FloraLabel("",new Color(1,.85f,.35f)));}
            }
            if(_justStep==JustitiaStep.Verdict)TickJustitiaDialogue();else _justDialogueBeat=-1;
            if(newStory&&_justStory==null)_justStory=StartCoroutine(JustitiaStory());
            if(_justStep==JustitiaStep.Balance)UpdateJustitiaLabels();
            if(JustitiaHard)ApplyJustitiaHardReplica(state,previous,previousPattern);
            if(_justStep==JustitiaStep.Rest||_justStep==JustitiaStep.Groggy)
            {if(_justPose!=null)Destroy(_justPose);_justPose=null;}
        }
        private bool SameJustitiaMass(RaidCombatEnemy state)
        {
            if(state.justMassIds.Length!=_justParticipants.Count)return false;
            for(int i=0;i<_justParticipants.Count;i++)
                if(RaidCombatAuthority.Id(_justParticipants[i])!=state.justMassIds[i]||_justMass[i]!=state.justMassValues[i])return false;
            return true;
        }
        private int[] CaptureJustitiaMassIds()
        {var values=new int[_justParticipants.Count];for(int i=0;i<values.Length;i++)values[i]=RaidCombatAuthority.Id(_justParticipants[i]);return values;}
    }
}
