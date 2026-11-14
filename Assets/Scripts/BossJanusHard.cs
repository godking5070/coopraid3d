using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public partial class BossController
    {
        private readonly bool[] _janusHardHolding=new bool[4];
        private JanusHardPostPhase _janusHardPostPhase;
        private bool _janusHardTransformed,_janusHardCounterAlternate,_janusHardFollowupShown,
            _janusHardReplicaSkipped,_janusHardChainIntroDone,_janusHardCrossIntroDone,_janusHardQueueCounter;
        private JanusPattern _janusHardQueued,_janusHardFollowupPattern,_janusHardLastPattern;
        private float _janusHardPhaseTime,_janusHardReleaseStable;
        private GameObject _janusHardAlphaModel,_janusHardOmegaModel,_janusHardFallback;
        private readonly List<TextMesh> _janusHardGuides=new List<TextMesh>();
        private RaidStageCutscenePresentation _janusHardPresentation;
        public bool JanusHardTransformationActive => JanusHard&&_janusStep==JanusStep.HardTransformation;
        private int CaptureJanusHardHoldingMask()
        {int mask=0;for(int i=0;i<4;i++)if(_janusHardHolding[i])mask|=1<<i;return mask;}

        private void ResetJanusHard()
        {
            _janusHardTransformed=_janusHardCounterAlternate=_janusHardFollowupShown=false;
            _janusHardReplicaSkipped=_janusHardChainIntroDone=_janusHardCrossIntroDone=_janusHardQueueCounter=false;
            _janusHardPostPhase=JanusHardPostPhase.None;_janusHardQueued=JanusPattern.None;
            _janusHardFollowupPattern=_janusHardLastPattern=JanusPattern.None;_janusHardPhaseTime=_janusHardReleaseStable=0;
            for(int i=0;i<4;i++)_janusHardHolding[i]=false;
        }
        private void ClearJanusHard(bool keepModels)
        {
            StopJanusHardPresentation();
            ClearJanusHardGuides();
            if(_janusHardFallback!=null)Destroy(_janusHardFallback);_janusHardFallback=null;
            if(!keepModels)
            {
                if(_janusHardAlphaModel!=null)Destroy(_janusHardAlphaModel);
                if(_janusHardOmegaModel!=null)Destroy(_janusHardOmegaModel);
                _janusHardAlphaModel=_janusHardOmegaModel=null;
                if(_janusAlphaVisual!=null)_janusAlphaVisual.SetActive(true);
                if(_janusOmega?.JanusAuthoredVisual!=null)_janusOmega.JanusAuthoredVisual.SetActive(true);
            }
            _janusHardPostPhase=JanusHardPostPhase.None;
            for(int i=0;i<4;i++)_janusHardHolding[i]=false;
        }
        private JanusPattern NextJanusHardPattern()
        {
            if(_janusHardQueued!=JanusPattern.None){var queued=_janusHardQueued;_janusHardQueued=JanusPattern.None;
                _janusHardLastPattern=queued;return queued;}
            JanusPattern[] basics={JanusPattern.AlphaStrike,JanusPattern.AlphaSweep,JanusPattern.OmegaLine,
                JanusPattern.OmegaCircle,JanusPattern.SealMark,JanusPattern.TwinOath};
            if(_janusNext<basics.Length){_janusHardLastPattern=basics[_janusNext];return _janusHardLastPattern;}
            if(!_janusHardCrossIntroDone){_janusHardCrossIntroDone=true;_janusHardLastPattern=JanusPattern.ShieldSpearCross;return _janusHardLastPattern;}
            if(!_janusHardChainIntroDone){_janusHardChainIntroDone=true;_janusHardLastPattern=JanusPattern.RedChainWave;return _janusHardLastPattern;}
            JanusPattern[] pool={JanusPattern.AlphaStrike,JanusPattern.AlphaSweep,JanusPattern.OmegaLine,
                JanusPattern.OmegaCircle,JanusPattern.SealMark,JanusPattern.TwinOath,
                JanusPattern.ShieldSpearCross,JanusPattern.RedChainWave};
            int total=0;for(int i=0;i<pool.Length;i++)if(pool[i]!=_janusHardLastPattern)
                total+=Mathf.Max(1,janusHard.patternWeights!=null&&i<janusHard.patternWeights.Length?janusHard.patternWeights[i]:1);
            int roll=Random.Range(0,total);
            for(int i=0;i<pool.Length;i++)
            {if(pool[i]==_janusHardLastPattern)continue;
             roll-=Mathf.Max(1,janusHard.patternWeights!=null&&i<janusHard.patternWeights.Length?janusHard.patternWeights[i]:1);
             if(roll<0){_janusHardLastPattern=pool[i];return pool[i];}}
            return JanusPattern.AlphaStrike;
        }
        private bool TickJanusHard(float dt)
        {
            if(_janusStep==JanusStep.HardTransformation)
            {
                _janusRemaining=Mathf.Max(0,_janusRemaining-dt);_castRemainingTime=_janusRemaining;
                if(_janusHardFallback!=null)_janusHardFallback.transform.Rotate(0,90*dt,0);
                if(JanusHardSkipPressed()&&!LocalGameplayInput.ModalCovered)
                {_janusRemaining=0;LocalGameplayInput.SuppressThisFrame();}
                if(_janusRemaining<=0)FinishJanusHardTransformation();return true;
            }
            if(_janusStep==JanusStep.HardRegroup)
            {
                _janusRemaining=Mathf.Max(0,_janusRemaining-dt);_castRemainingTime=_janusRemaining;
                if(_janusRemaining<=0)EndJanus(0);return true;
            }
            if(_janusStep==JanusStep.HardFollowup)
            {
                _janusRemaining=Mathf.Max(0,_janusRemaining-dt);_castRemainingTime=_janusRemaining;
                if(!_janusHardFollowupShown&&_janusRemaining<=janusHard.impactWarning)
                {_janusHardFollowupShown=true;ShowJanusHardFollowupGround();}
                if(_janusRemaining<=0)ResolveJanusHardFollowup();return true;
            }
            return false;
        }
        private void BeginJanusHardPosts()
        {
            _janusHardPostPhase=JanusHardPostPhase.FirstPull;
            _janusHardPhaseTime=_janusHardReleaseStable=0;
            for(int i=0;i<4;i++)_janusHardHolding[i]=false;
        }
        private void TickJanusHardPosts(float dt)
        {
            _janusHardPhaseTime+=dt;
            var ordered=PartyLife.OrderedMembers();var ids=new int[4];var alive=new bool[4];var range=new bool[4];var holding=new bool[4];
            int ready=0,pulling=0;
            for(int i=0;i<4;i++)
            {
                var actor=_janusPostActors[i];ids[i]=ordered.IndexOf(actor);
                if(i<ordered.Count)alive[i]=CombatRange.LivingPartyMember(ordered[i]);
                range[i]=actor!=null&&CombatRange.LivingPartyMember(actor)&&
                    Vector3.Distance(new Vector3(actor.position.x,0,actor.position.z),JanusPostPoint(i))<=JanusTuning.postInteractRange;
                if(JanusAutomated(actor))_janusHardHolding[i]=JanusHardRules.IsPull(_janusHardPostPhase)&&range[i];
                holding[i]=range[i]&&_janusHardHolding[i];
                if(range[i])ready++;if(holding[i])pulling++;
            }
            bool four=ordered.Count==4&&JanusNormalRules.AllPosts(ids,range,alive);
            if(_janusHardPostPhase==JanusHardPostPhase.Release)
            {
                if(four&&JanusHardRules.ReleaseComplete(ids,holding))_janusHardReleaseStable+=dt;
                else _janusHardReleaseStable=0;
                if(_janusHardReleaseStable>=.7f)
                {_janusHardPostPhase=JanusHardPostPhase.FinalPull;_janusHardPhaseTime=0;_janusPostOverlap=0;
                 JanusCue(janusHard.finalPullCue);SetMechanicAlert("사슬 안정 · 최종 당김! 다시 상호작용",Color.yellow,4);}
                else if(_janusHardPhaseTime>=janusHard.postReleaseSeconds){FailJanusHardPosts("이완 시간 초과");return;}
            }
            else
            {
                _janusPostOverlap=JanusHardRules.AllPulling(ids,range,alive,holding)?_janusPostOverlap+dt:0;
                float need=_janusHardPostPhase==JanusHardPostPhase.FirstPull?janusHard.postFirstHold:janusHard.postFinalHold;
                if(_janusPostOverlap>=need)
                {
                    if(_janusHardPostPhase==JanusHardPostPhase.FirstPull)
                    {_janusHardPostPhase=JanusHardPostPhase.Release;_janusHardPhaseTime=0;_janusHardReleaseStable=0;
                     JanusCue(janusHard.releaseCue);SetMechanicAlert("붉은 사슬 · 이완! 상호작용을 다시 눌러 놓기",Color.red,janusHard.postReleaseSeconds);}
                    else {FinishJanusHardPosts();return;}
                }
            }
            UpdateJanusPostLabels();
            string stage=_janusHardPostPhase==JanusHardPostPhase.FirstPull?"당김 · 유지":
                _janusHardPostPhase==JanusHardPostPhase.Release?"이완 · 상호작용 다시 눌러 해제":"최종 당김 · 다시 눌러 유지";
            SetMechanicAlert("⛓ 사슬 장력 조율 · "+stage+" · 참여 "+ready+"/4 · 당김 "+pulling+"/4 · 장력 "+
                _janusPostOverlap.ToString("0.0")+" · 남은 "+_janusRemaining.ToString("0.0")+"초",Color.yellow,_janusRemaining);
            if(_janusRemaining<=0)FailJanusHardPosts("전체 제한시간 초과");
        }
        private void FinishJanusHardPosts()
        {
            CombatMetricsManager.Event("야누스 하드 사슬 장력 조율 성공");
            _janusChainBroken=true;RaidApproachField.Instance?.SetJanusChainBroken(true);
            JanusCue(JanusTuning.chainBreakCue);ClearJanusPosts();_janusHardPostPhase=JanusHardPostPhase.None;
            _janusCounterUnlocked=true;_janusStep=JanusStep.Groggy;fsmState=BossFSMState.Groggy;
            _janusRemaining=JanusTuning.counterGroggySeconds;_groggyRemainingTime=_janusRemaining;
            _castRemainingTime=_castTotalDuration=0;_currentCastSkillName="장력 조율 성공 · 사슬 절단";
        }
        private void FailJanusHardPosts(string reason)
        {
            CombatMetricsManager.Event("야누스 하드 사슬 장력 조율 실패 · "+reason);
            ClearJanusPosts();_janusHardPostPhase=JanusHardPostPhase.None;_janusCounterUnlocked=true;
            foreach(var member in GetLivingPartyMemberTransforms())
                DamagePartyMember(member,JanusTuning.postFailureDamage,CombatDamageKind.Area,"janus.hard.posts.failure");
            if(!StageManager.CombatSuspended)
            {EndJanus(janusHard.postRegroupSeconds);
             SetMechanicAlert("사슬 장력 조율 실패 · "+reason+" · 회복 후 전투",Color.red,janusHard.postRegroupSeconds);}
        }
        private void BeginJanusHardTransformation()
        {
            ClearJanusAttack();ClearJanusPosts();
            _janusStep=JanusStep.HardTransformation;fsmState=BossFSMState.CastingSkill;
            _janusRemaining=janusHard.transformationSeconds;
            if(janusHard.transformationPresentation!=null)
            {
                try
                {_janusHardPresentation=Instantiate(janusHard.transformationPresentation,transform.position,Quaternion.identity,transform);
                 if(_janusHardPresentation.Play(this,StageManager.Instance?.player,0))
                     _janusRemaining=RaidStageCutscenePresentation.Duration(_janusHardPresentation.DirectorAsset,_janusRemaining);
                 else StopJanusHardPresentation();}
                catch(System.Exception error){Debug.LogWarning("Janus hard transformation fallback: "+error);StopJanusHardPresentation();}
            }
            if(_janusHardPresentation==null)ShowJanusHardFallback();
            _castTotalDuration=_castRemainingTime=_janusRemaining;
            _currentCastSkillName="무너진 성벽 · 해방된 처형자";
            SetMechanicAlert("야누스 알파·오메가 변신 · 두 문지기의 동력 사슬 해방",new Color(1,.3f,.2f),_janusRemaining);
            JanusCue(janusHard.transformationCue);
            CombatMetricsManager.Event("야누스 하드 40% 변신 시작 · 공유 HP "+currentHp.ToString("0"));
        }
        private void FinishJanusHardTransformation()
        {
            StopJanusHardPresentation();
            if(_janusHardFallback!=null)Destroy(_janusHardFallback);_janusHardFallback=null;
            _janusHardTransformed=true;_janusHardReplicaSkipped=false;
            RestoreHardTransformationHealth();
            ShowJanusHardModels();
            _janusStep=JanusStep.HardRegroup;fsmState=BossFSMState.CastingSkill;
            _janusRemaining=janusHard.postRegroupSeconds;_castTotalDuration=_castRemainingTime=_janusRemaining;
            _currentCastSkillName="변신 후 재정렬";
            CombatMetricsManager.Event("야누스 하드 변신 완료 · 공유 HP "+currentHp.ToString("0"));
        }
        private void ShowJanusHardModels()
        {
            currentBossTitle="무너진 성벽, 알파 & 해방된 처형자, 오메가";
            if(_janusAlphaVisual!=null)_janusAlphaVisual.SetActive(false);
            if(_janusOmega?.JanusAuthoredVisual!=null)_janusOmega.JanusAuthoredVisual.SetActive(false);
            if(_janusHardAlphaModel==null)
            {
                _janusHardAlphaModel=janusHard.transformedAlphaPrefab!=null?
                    Instantiate(janusHard.transformedAlphaPrefab,transform):RaidPrimitive.Create(PrimitiveType.Cube);
                if(janusHard.transformedAlphaPrefab==null)
                {_janusHardAlphaModel.name="Temporary Alpha layered gate shield";
                 _janusHardAlphaModel.transform.SetParent(transform,false);
                 _janusHardAlphaModel.transform.localPosition=new Vector3(-1.4f,2,.5f);
                 _janusHardAlphaModel.transform.localScale=new Vector3(.5f,3,2.4f);}
                foreach(var collider in _janusHardAlphaModel.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            if(_janusOmega!=null&&_janusHardOmegaModel==null)
            {
                _janusHardOmegaModel=janusHard.transformedOmegaPrefab!=null?
                    Instantiate(janusHard.transformedOmegaPrefab,_janusOmega.transform):RaidPrimitive.Create(PrimitiveType.Cube);
                if(janusHard.transformedOmegaPrefab==null)
                {_janusHardOmegaModel.name="Temporary Omega double halberd";
                 _janusHardOmegaModel.transform.SetParent(_janusOmega.transform,false);
                 _janusHardOmegaModel.transform.localPosition=new Vector3(1.4f,2,.5f);
                 _janusHardOmegaModel.transform.localScale=new Vector3(.45f,3.8f,1.5f);}
                foreach(var collider in _janusHardOmegaModel.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
        }
        private void ShowJanusHardFallback()
        {
            if(_janusHardFallback!=null)return;
            _janusHardFallback=RaidPrimitive.Create(PrimitiveType.Sphere);
            _janusHardFallback.name="Temporary Janus dual transformation cue";
            _janusHardFallback.transform.SetParent(transform,false);
            _janusHardFallback.transform.localPosition=new Vector3(0,4,0);
            _janusHardFallback.transform.localScale=Vector3.one*1.5f;
            _janusHardFallback.GetComponent<Collider>().enabled=false;
        }
        private void StopJanusHardPresentation()
        {
            if(_janusHardPresentation==null)return;
            _janusHardPresentation.StopAndRestore();
            Destroy(_janusHardPresentation.gameObject);_janusHardPresentation=null;
        }
        private static bool JanusHardSkipPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;return keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }
        private void ApplyJanusHardReplica(RaidCombatEnemy state,JanusStep previous)
        {
            bool wasTransformed=_janusHardTransformed;
            _janusHardPostPhase=(JanusHardPostPhase)state.janusHardPostPhase;
            for(int i=0;i<4;i++)_janusHardHolding[i]=(state.janusHardHoldingMask&(1<<i))!=0;
            if(_janusStep==JanusStep.HardTransformation&&previous!=JanusStep.HardTransformation&&
                !_janusHardReplicaSkipped&&janusHard.transformationPresentation!=null)
            {
                try
                {_janusHardPresentation=Instantiate(janusHard.transformationPresentation,transform.position,Quaternion.identity,transform);
                 if(!_janusHardPresentation.Play(this,StageManager.Instance?.player,
                     Mathf.Max(0,state.castTotal-state.castRemaining)))StopJanusHardPresentation();}
                catch(System.Exception){StopJanusHardPresentation();}
            }
            if(_janusStep==JanusStep.HardTransformation&&_janusHardPresentation==null)ShowJanusHardFallback();
            if(previous==JanusStep.HardTransformation&&_janusStep!=JanusStep.HardTransformation)
            {StopJanusHardPresentation();if(_janusHardFallback!=null)Destroy(_janusHardFallback);
             _janusHardFallback=null;_janusHardReplicaSkipped=false;}
            _janusHardTransformed=state.janusHardTransformed;
            if(_janusHardTransformed&&!wasTransformed)ShowJanusHardModels();
            if(_janusHardTransformed&&_janusOmega!=null&&_janusHardOmegaModel==null)ShowJanusHardModels();
            if(!_janusHardTransformed&&wasTransformed)
            {if(_janusHardAlphaModel!=null)Destroy(_janusHardAlphaModel);
             if(_janusHardOmegaModel!=null)Destroy(_janusHardOmegaModel);
             _janusHardAlphaModel=_janusHardOmegaModel=null;
             if(_janusAlphaVisual!=null)_janusAlphaVisual.SetActive(true);
             if(_janusOmega?.JanusAuthoredVisual!=null)_janusOmega.JanusAuthoredVisual.SetActive(true);}
        }
        private void TickJanusHardReplicaInput()
        {
            if(_janusStep!=JanusStep.HardTransformation||_janusHardReplicaSkipped||LocalGameplayInput.ModalCovered)return;
            if(!JanusHardSkipPressed())return;
            _janusHardReplicaSkipped=true;StopJanusHardPresentation();LocalGameplayInput.SuppressThisFrame();
        }
        private void TickJanusHardCastTelegraph()
        {
            if(_janusRemaining>janusHard.impactWarning||_janusHardFollowupShown)return;
            if(_janusPattern==JanusPattern.ShieldSpearCross)
                JanusGround(AoETelegraphType.FrontCone_120Deg,_janusOrigin,Vector3.Cross(Vector3.up,_janusForward)*(_janusRight?1:-1),
                    _janusRemaining,0,JanusTuning.halfRadius,180);
            else if(_janusPattern==JanusPattern.AlphaSweep)
                JanusGround(AoETelegraphType.FrontCone_120Deg,_janusOrigin,Vector3.Cross(Vector3.up,_janusForward)*(_janusRight?1:-1),
                    _janusRemaining,0,JanusTuning.halfRadius,180);
            else if(_janusPattern==JanusPattern.OmegaCircle)
                JanusGround(AoETelegraphType.Circle_Radius6,_janusOrigin,_janusForward,_janusRemaining,0,JanusTuning.circleRadius);
            else if(_janusPattern==JanusPattern.OmegaLine)
                JanusGround(AoETelegraphType.Rectangle,_janusOrigin,_janusForward,_janusRemaining,
                    JanusTuning.lineWidth,JanusTuning.lineLength);
            else if(_janusPattern==JanusPattern.SealMark)
                foreach(var point in _janusMarkPoints)
                    JanusGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,_janusRemaining,0,JanusTuning.markRadius);
            else if(_janusPattern==JanusPattern.RedChainWave)
                JanusGround(AoETelegraphType.Rectangle,new Vector3(-14,0,-8),Vector3.right,
                    _janusRemaining,2.2f,28);
            else return;
            _janusHardFollowupShown=true;
        }
        private void JanusHardGuide(string label,Vector3 point)
        {
            var text=FloraLabel(label,Color.yellow);text.transform.position=point+Vector3.up*.8f;
            _janusHardGuides.Add(text);
        }
        private void ClearJanusHardGuides()
        {foreach(var label in _janusHardGuides)if(label!=null)Destroy(label.gameObject);_janusHardGuides.Clear();}
        private bool BeginJanusHardFollowup()
        {
            JanusPattern pattern=_janusPattern;
            if(pattern==JanusPattern.ShieldSpearCross)
            {
                var side=Vector3.Cross(Vector3.up,_janusForward);
                foreach(var member in GetLivingPartyMemberTransforms())
                    if(FloraNormalRules.InHalf(member.position,_janusOrigin,side,JanusTuning.halfRadius,_janusRight))
                        DamagePartyMember(member,JanusTuning.halfDamage,CombatDamageKind.Area,"janus.hard.cross.alpha");
                if(StageManager.CombatSuspended)return true;
            }
            else if(pattern==JanusPattern.RedChainWave)
            {
                JanusHardLineDamage(-8,"janus.hard.chain.I");if(StageManager.CombatSuspended)return true;
            }
            if(pattern!=JanusPattern.AlphaStrike&&pattern!=JanusPattern.AlphaSweep&&pattern!=JanusPattern.OmegaLine&&
               pattern!=JanusPattern.OmegaCircle&&pattern!=JanusPattern.ShieldSpearCross&&pattern!=JanusPattern.RedChainWave)
                return false;
            ClearJanusAttack();_janusHardFollowupShown=false;_janusHardFollowupPattern=pattern;
            if(pattern==JanusPattern.RedChainWave)JanusHardGuide("II · 다음 파동",new Vector3(0,0,-12));
            _janusStep=JanusStep.HardFollowup;fsmState=BossFSMState.CastingSkill;
            _janusRemaining=pattern==JanusPattern.AlphaStrike?janusHard.tankSecondGap:
                pattern==JanusPattern.AlphaSweep?janusHard.halfSecondGap:
                pattern==JanusPattern.OmegaCircle?janusHard.ringSecondGap:
                pattern==JanusPattern.ShieldSpearCross?janusHard.crossSecondGap:
                pattern==JanusPattern.RedChainWave?janusHard.chainSecondGap:janusHard.residueSeconds;
            _castTotalDuration=_castRemainingTime=_janusRemaining;
            if(pattern==JanusPattern.ShieldSpearCross)
            {
                _janusOrigin=_janusOmega!=null?_janusOmega.transform.position:JanusTuning.omegaPosition;
                var delta=_janusTarget!=null?_janusTarget.position-_janusOrigin:Vector3.back;
                delta.y=0;_janusForward=delta.sqrMagnitude>.01f?delta.normalized:Vector3.back;
            }
            _currentCastSkillName=pattern==JanusPattern.AlphaStrike?"알파 · 두 번째 성문 강타":
                pattern==JanusPattern.AlphaSweep?"알파 · 반대쪽 방벽 휩쓸기":
                pattern==JanusPattern.OmegaCircle?"오메가 · 외곽 원무":
                pattern==JanusPattern.OmegaLine?"오메가 · 직선 잔류 위험":
                pattern==JanusPattern.ShieldSpearCross?"오메가 · 고정 직선 찌르기":"붉은 사슬 파동 · II";
            SetMechanicAlert(_currentCastSkillName,Color.red,_janusRemaining);
            return true;
        }
        private void ShowJanusHardFollowupGround()
        {
            if(_janusHardFollowupPattern==JanusPattern.AlphaSweep)
                JanusGround(AoETelegraphType.FrontCone_120Deg,_janusOrigin,
                    Vector3.Cross(Vector3.up,_janusForward)*(_janusRight?-1:1),_janusRemaining,0,JanusTuning.halfRadius,180);
            else if(_janusHardFollowupPattern==JanusPattern.OmegaCircle)
                JanusGround(AoETelegraphType.Donut_Inner4_Outer12,_janusOrigin,_janusForward,_janusRemaining,
                    JanusTuning.circleRadius,14);
            else if(_janusHardFollowupPattern==JanusPattern.OmegaLine||_janusHardFollowupPattern==JanusPattern.ShieldSpearCross)
                JanusGround(AoETelegraphType.Rectangle,_janusOrigin,_janusForward,_janusRemaining,
                    JanusTuning.lineWidth,JanusTuning.lineLength);
            else if(_janusHardFollowupPattern==JanusPattern.RedChainWave)
                JanusGround(AoETelegraphType.Rectangle,new Vector3(-14,0,-12),Vector3.right,
                    _janusRemaining,2.2f,28);
        }
        private void JanusHardLineDamage(float z,string origin)
        {
            foreach(var member in GetLivingPartyMemberTransforms())
            {var delta=member.position-new Vector3(-14,0,z);
             if(GuardianNormalState.InRectangle(delta.z,delta.x,2.2f,28))
                DamagePartyMember(member,JanusTuning.lineDamage,CombatDamageKind.Area,origin);}
        }
        private void ResolveJanusHardFollowup()
        {
            var living=GetLivingPartyMemberTransforms();
            foreach(var member in living)
            {
                bool hit=false;float damage=0;
                switch(_janusHardFollowupPattern)
                {
                    case JanusPattern.AlphaStrike:hit=member==_janusTarget;damage=JanusTuning.tankDamage;break;
                    case JanusPattern.AlphaSweep:hit=FloraNormalRules.InHalf(member.position,_janusOrigin,
                        Vector3.Cross(Vector3.up,_janusForward),JanusTuning.halfRadius,!_janusRight);damage=JanusTuning.halfDamage;break;
                    case JanusPattern.OmegaCircle:
                        var offset=member.position-_janusOrigin;offset.y=0;
                        hit=offset.magnitude>=JanusTuning.circleRadius&&offset.magnitude<=14;damage=JanusTuning.circleDamage;break;
                    case JanusPattern.OmegaLine:case JanusPattern.ShieldSpearCross:
                        var delta=member.position-_janusOrigin;
                        hit=GuardianNormalState.InRectangle(Vector3.Dot(delta,Vector3.Cross(Vector3.up,_janusForward)),
                            Vector3.Dot(delta,_janusForward),JanusTuning.lineWidth,JanusTuning.lineLength);
                        damage=_janusHardFollowupPattern==JanusPattern.OmegaLine?janusHard.residueDamage:JanusTuning.lineDamage;break;
                }
                if(hit)DamagePartyMember(member,damage,_janusHardFollowupPattern==JanusPattern.AlphaStrike?
                    CombatDamageKind.Direct:CombatDamageKind.Area,"janus.hard.followup."+_janusHardFollowupPattern);
            }
            if(_janusHardFollowupPattern==JanusPattern.RedChainWave)JanusHardLineDamage(-12,"janus.hard.chain.II");
            if(StageManager.CombatSuspended)return;
            if(_janusHardTransformed&&_janusHardFollowupPattern==JanusPattern.OmegaCircle)
                _janusHardQueued=JanusPattern.TwinOath;
            if(_janusHardTransformed&&_janusHardFollowupPattern==JanusPattern.RedChainWave)
                _janusHardQueueCounter=true;
            EndJanus(JanusTuning.recoverySeconds);
        }
    }
}
