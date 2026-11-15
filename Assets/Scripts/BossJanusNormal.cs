using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        [Header("Stage 6 normal · Janus initial tuning")]
        public JanusNormalSettings janus=new JanusNormalSettings();
        public JanusHardSettings janusHard=new JanusHardSettings();
        public bool JanusHard => currentStageNumber==6&&!CharacterTrainingSession.Active&&
            CampaignRuntime.Instance?.DifficultyId==CampaignCatalog.Hard;
        public bool JanusEncounter => currentStageNumber==6&&!CharacterTrainingSession.Active&&
            (CampaignRuntime.Instance==null||CampaignRuntime.Instance.DifficultyId==CampaignCatalog.Normal||JanusHard);
        private JanusNormalSettings JanusTuning => JanusHard ? (JanusNormalSettings)janusHard : janus;
        public JanusNormalSettings JanusSettings => JanusTuning;
        private JanusPattern _janusPattern;
        private JanusStep _janusStep;
        private bool _janusDamageStaggerGroggy;
        private float _janusRemaining,_janusRest,_janusPostOverlap,_janusLastCounterEnd;
        private int _janusNext,_janusCounterSerial,_janusAreaFrame=-1,_janusAlphaCounter=-1,_janusOmegaCounter=-1;
        private bool _janusStarted,_janusPostUsed,_janusCounterUnlocked,_janusStoryUsed,_janusLineLocked,_janusChainBroken;
        private Transform _janusTarget;
        private Transform _janusAlphaCounterActor,_janusOmegaCounterActor;
        private Vector3 _janusOrigin,_janusForward;
        private bool _janusRight;
        private struct JanusAreaBucket
        {public Transform source;public string origin;public int alpha,omega;}
        private readonly List<JanusAreaBucket> _janusAreaBuckets=new List<JanusAreaBucket>();
        private readonly Transform[] _janusPostActors=new Transform[4];
        private readonly GameObject[] _janusPostVisuals=new GameObject[4];
        private readonly TextMesh[] _janusPostLabels=new TextMesh[4];
        private readonly List<AoETelegraph> _janusGround=new List<AoETelegraph>();
        private readonly List<AttachedPartyMechanicMarker> _janusMarkers=new List<AttachedPartyMechanicMarker>();
        private readonly List<Vector3> _janusMarkPoints=new List<Vector3>();
        private BossAddMinion _janusOmega;
        public Transform JanusOmegaTransform => JanusEncounter && _janusOmega != null ? _janusOmega.transform : null;
        private GameObject _janusAlphaVisual,_janusChainVisual;
        private TextMesh _janusAlphaIcon,_janusOmegaIcon;
        private Coroutine _janusStory;
        private AudioSource _janusAudio;
        public bool JanusPostActive => JanusEncounter&&_janusStep==JanusStep.Posts;
        public bool JanusAiCounterReady(Transform actor) => JanusEncounter&&_janusStep==JanusStep.CounterWindow&&
            actor!=null&&JanusAiCounterTarget(actor)!=null;
        public Transform JanusAiCounterTarget(Transform actor)
        {
            if(!JanusEncounter||_janusStep!=JanusStep.CounterWindow||!CombatRange.LivingPartyMember(actor)||!PartyAiTactics.CanTakeCounterPosition(actor))return null;
            if(actor==_janusAlphaCounterActor||actor==_janusOmegaCounterActor)return null;
            int role=actor.GetComponent<NetworkRaidAvatar>()?.Role??
                (actor.GetComponent<PartyBotAI>()!=null?(int)actor.GetComponent<PartyBotAI>().role+1:
                actor.GetComponent<PartyLife>()?.IsTank==true?1:actor.GetComponent<PartyLife>()?.IsHealer==true?2:3);
            bool omegaFirst=role>=4||role==2&&_janusAlphaCounter>=0;
            if(omegaFirst&&_janusOmegaCounterActor==null&&_janusOmega!=null)return _janusOmega.transform;
            if(_janusAlphaCounterActor==null)return transform;
            return _janusOmegaCounterActor==null&&_janusOmega!=null?_janusOmega.transform:null;
        }
        public bool JanusCounterAllowed(Transform attacker,bool omega)
        {
            if(!JanusEncounter||_janusStep!=JanusStep.CounterWindow||!CombatRange.LivingPartyMember(attacker))return false;
            return attacker!=_janusAlphaCounterActor&&attacker!=_janusOmegaCounterActor&&
                (omega?_janusOmegaCounterActor:_janusAlphaCounterActor)==null;
        }
        public void JanusCounterSucceeded(Transform attacker,bool omega)
        {
            if(!JanusCounterAllowed(attacker,omega))return;
            int id=RaidCombatAuthority.Id(attacker);
            if(omega){_janusOmegaCounter=id;_janusOmegaCounterActor=attacker;_janusOmega?.GetComponent<CombatCounterTarget>()?.CloseWindow();}
            else {_janusAlphaCounter=id;_janusAlphaCounterActor=attacker;GetComponent<CombatCounterTarget>()?.CloseWindow();}
            CombatMetricsManager.Event("야누스 "+(omega?"오메가":"알파")+" 카운터 성공 · "+attacker.name);
            SetMechanicAlert("⚔ 쌍동 카운터 / 전 직업 가능 · 차단 "+JanusCounterCount()+"/2",new Color(.2f,.65f,1),_janusRemaining);
            if(_janusAlphaCounterActor!=null&&_janusOmegaCounterActor!=null)FinishJanusCounter();
        }
        private int JanusCounterCount()=>(_janusAlphaCounterActor!=null?1:0)+(_janusOmegaCounterActor!=null?1:0);
        public bool JanusTankbusterImminent(Transform actor,float within)=>JanusEncounter&&_janusStep==JanusStep.Cast&&
            _janusPattern==JanusPattern.AlphaStrike&&_janusTarget==actor&&_janusRemaining<=within;
        private void ResetJanus()
        {
            ClearJanus(false);janus=janus??new JanusNormalSettings();janusHard=janusHard??new JanusHardSettings();ResetJanusHard();
            _janusPattern=JanusPattern.None;_janusStep=JanusStep.Rest;_janusRemaining=0;
            _janusRest=JanusTuning.patternInterval;_janusNext=0;_janusCounterSerial=0;
            _janusStarted=_janusPostUsed=_janusCounterUnlocked=_janusStoryUsed=_janusChainBroken=false;
            RaidApproachField.Instance?.SetJanusChainBroken(false);
            _janusAlphaCounter=_janusOmegaCounter=-1;_janusAreaFrame=-1;
            _janusLastCounterEnd=float.NegativeInfinity;
            _janusAreaBuckets.Clear();
            _janusAlphaCounterActor=_janusOmegaCounterActor=null;
            if(!JanusEncounter)return;
            BossAddSpawner.Instance?.ResetWaveFlagsAndClearAdds();
            currentBossTitle="시계탑 정문 : 성문의 쌍둥이 문지기, 야누스";
            maxHp=RaidBossHealthTuning.Resolve(JanusTuning.health,JanusHard);currentHp=maxHp;
            arenaBoundaryRadius=JanusTuning.arenaRadius;
            _stagePhase1BodyColor=new Color(.75f,.76f,.84f);SetBossBodyColor(_stagePhase1BodyColor);
            _janusAlphaVisual=new GameObject("Janus Alpha visual · authored replacement or temporary shield");
            _janusAlphaVisual.transform.SetParent(transform,false);
            if(JanusTuning.alphaVisualPrefab!=null)
            {var authored=Instantiate(JanusTuning.alphaVisualPrefab,_janusAlphaVisual.transform);
             foreach(var collider in authored.GetComponentsInChildren<Collider>())collider.enabled=false;}
            else
            {var shield=RaidPrimitive.Create(PrimitiveType.Cube);shield.name="Temporary platinum shield";
             shield.transform.SetParent(_janusAlphaVisual.transform,false);shield.transform.localPosition=new Vector3(-1.35f,1.6f,.7f);
             shield.transform.localScale=new Vector3(.25f,2.8f,1.6f);shield.GetComponent<Collider>().enabled=false;}
        }
        private void StartJanus()
        {
            if(_janusStarted||!JanusEncounter)return;
            _janusStarted=true;transform.position=JanusTuning.alphaPosition;transform.rotation=Quaternion.LookRotation(Vector3.back);
            var spawner=BossAddSpawner.Instance;
            if(spawner!=null)
            {
                var go=new GameObject("Janus Omega · shared HP target");
                _janusOmega=go.AddComponent<BossAddMinion>();
                _janusOmega.InitializeAdd(AddMinionType.JanusOmega,this,spawner,JanusTuning.omegaPosition,6);
                _janusOmega.transform.rotation=Quaternion.LookRotation(Vector3.back);
                spawner.activeAdds.Add(_janusOmega);
            }
            SetMechanicAlert("야누스 알파 · 주 어그로 / 오메가 · 예고 기믹",new Color(.7f,.85f,1),4);
        }
        public void PrepareJanusArrival()
        {if(!JanusEncounter)return;StartJanus();SetJanusOmegaVisible(false);}
        private void ClearJanus(bool keepModels)
        {
            _janusDamageStaggerGroggy=false;
            ClearJanusAttack();ClearJanusPosts();
            if(_janusStory!=null){StopCoroutine(_janusStory);_janusStory=null;}
            if(!keepModels&&_janusAlphaVisual!=null)Destroy(_janusAlphaVisual);
            if(!keepModels)_janusAlphaVisual=null;
            ClearJanusHard(keepModels);
            if(_janusOmega!=null)
            {
                if(!keepModels)
                {BossAddSpawner.Instance?.activeAdds.Remove(_janusOmega);Destroy(_janusOmega.gameObject);}
                else {_janusOmega.isDead=true;_janusOmega.currentHp=0;}
                if(!keepModels)_janusOmega=null;
            }
            if(_janusChainVisual!=null)Destroy(_janusChainVisual);_janusChainVisual=null;
            _janusStarted=false;_janusLineLocked=false;
            _janusAlphaCounter=_janusOmegaCounter=-1;
            _janusAlphaCounterActor=_janusOmegaCounterActor=null;
        }
        public void SetJanusOmegaVisible(bool visible)
        {
            if(_janusOmega!=null)_janusOmega.gameObject.SetActive(visible);
        }
        private int[] CaptureJanusPostActors()
        {var ids=new int[4];for(int i=0;i<4;i++)ids[i]=RaidCombatAuthority.Id(_janusPostActors[i]);return ids;}
        private void ApplyJanusReplica(RaidCombatEnemy state)
        {
            var previous=_janusStep;
            _janusPattern=(JanusPattern)state.janusPattern;_janusStep=(JanusStep)state.janusStep;
            _janusRemaining=state.castRemaining;_janusPostOverlap=state.janusPostOverlap;
            _janusPostUsed=state.janusPostUsed;_janusCounterUnlocked=state.janusCounterUnlocked;
            _janusStoryUsed=state.janusStoryUsed;_janusLineLocked=state.janusLineLocked;
            if(_janusChainBroken!=state.janusChainBroken)
            {_janusChainBroken=state.janusChainBroken;RaidApproachField.Instance?.SetJanusChainBroken(_janusChainBroken);}
            _janusAlphaCounter=state.janusAlphaCounter;_janusOmegaCounter=state.janusOmegaCounter;
            _janusAlphaCounterActor=RaidCombatAuthority.Resolve(_janusAlphaCounter);
            _janusOmegaCounterActor=RaidCombatAuthority.Resolve(_janusOmegaCounter);
            if(JanusHard)ApplyJanusHardReplica(state,previous);
            if(_janusStep==JanusStep.Posts)
            {if(previous!=JanusStep.Posts)ShowJanusPosts();
             if(state.janusPostActors!=null)for(int i=0;i<4&&i<state.janusPostActors.Length;i++)
                _janusPostActors[i]=RaidCombatAuthority.Resolve(state.janusPostActors[i]);
             UpdateJanusPostLabels();}
            else if(previous==JanusStep.Posts)ClearJanusPosts();
            if(_janusStep==JanusStep.CounterWindow&&previous!=JanusStep.CounterWindow)
            {_janusAlphaIcon=FloraLabel("⚔ 알파 · 전 직업",new Color(.2f,.65f,1));
             _janusAlphaIcon.transform.position=transform.position+Vector3.up*4.5f;
             _janusOmegaIcon=FloraLabel("⚔ 오메가 · 전 직업",new Color(.2f,.65f,1));
             _janusOmegaIcon.transform.position=(_janusOmega!=null?_janusOmega.transform.position:JanusTuning.omegaPosition)+Vector3.up*4.5f;}
            if(_janusAlphaIcon!=null)_janusAlphaIcon.transform.position=new Vector3(state.x,state.y+4.5f,state.z);
            if(_janusOmegaIcon!=null&&_janusOmega!=null)_janusOmegaIcon.transform.position=_janusOmega.transform.position+Vector3.up*4.5f;
            if(previous==JanusStep.CounterWindow&&_janusStep!=JanusStep.CounterWindow)ClearJanusAttack();
        }
        private void ClearJanusAttack()
        {
            ClearJanusHardGuides();
            GetComponent<CombatCounterTarget>()?.CloseWindow();
            _janusOmega?.GetComponent<CombatCounterTarget>()?.CloseWindow();
            foreach(var ground in _janusGround)if(ground!=null)Destroy(ground.gameObject);_janusGround.Clear();
            foreach(var marker in _janusMarkers)if(marker!=null)Destroy(marker.gameObject);_janusMarkers.Clear();
            _janusMarkPoints.Clear();
            if(_janusAlphaIcon!=null)Destroy(_janusAlphaIcon.gameObject);_janusAlphaIcon=null;
            if(_janusOmegaIcon!=null)Destroy(_janusOmegaIcon.gameObject);_janusOmegaIcon=null;
        }
        private void JanusGround(AoETelegraphType type,Vector3 at,Vector3 forward,float seconds,float inner,float outer,float angle=120)
            =>_janusGround.Add(AoETelegraph.Spawn(type,at,forward,seconds,inner,outer,angle));
        private void JanusCue(AudioClip cue)
        {if(cue==null)return;if(_janusAudio==null)_janusAudio=GetComponent<AudioSource>()??gameObject.AddComponent<AudioSource>();_janusAudio.PlayOneShot(cue);}
        private void JanusTrigger(string key,bool omega)
        {
            var model=JanusHard&&_janusHardTransformed?
                (omega?_janusHardOmegaModel:_janusHardAlphaModel):
                (omega?_janusOmega?.JanusAuthoredVisual:_janusAlphaVisual);
            var animator=model?.GetComponentInChildren<Animator>();
            if(animator==null||string.IsNullOrEmpty(key))return;
            foreach(var parameter in animator.parameters)if(parameter.name==key&&parameter.type==AnimatorControllerParameterType.Trigger)
            {animator.SetTrigger(key);break;}
        }
        private void TickJanus()
        {
            if(!_janusStarted)StartJanus();
            float dt=Time.deltaTime;
            if(JanusHard&&TickJanusHard(dt)){UpdateTopBossUI();UpdateCastBanner();return;}
            if(_janusStep==JanusStep.Rest)
            {
                fsmState=BossFSMState.IdleChasing;RotateTowardsAggroTargetXZ();
                if(!JanusHard)UpdateAutoAttack();
                _janusRest-=dt;
                if(_janusRest<=0)
                {
                    if(JanusNormalRules.PostDue(_janusPostUsed,currentHp>0,HpRatio,JanusTuning.postThreshold))BeginJanusPosts();
                    else if(JanusHard&&JanusHardRules.DueTransformation(_janusHardTransformed,HpRatio,janusHard.transformationThreshold))BeginJanusHardTransformation();
                    else if(JanusHard&&_janusHardQueueCounter&&_janusCounterUnlocked&&
                        RaidCombatAuthority.ServerTime-_janusLastCounterEnd>=janusHard.counterRepeatGap)
                    {_janusHardQueueCounter=false;BeginJanusCounter();}
                    else if(_janusCounterUnlocked&&_janusCounterSerial==0)BeginJanusCounter();
                    else if(JanusHard&&_janusCounterUnlocked&&_janusNext>=8&&_janusNext%7==0&&
                        _janusHardQueued==JanusPattern.None&&
                        RaidCombatAuthority.ServerTime-_janusLastCounterEnd>=janusHard.counterRepeatGap)
                        BeginJanusCounter();
                    else
                    {
                        JanusPattern[] intro={JanusPattern.AlphaStrike,JanusPattern.AlphaSweep,JanusPattern.OmegaLine,
                            JanusPattern.OmegaCircle,JanusPattern.SealMark,JanusPattern.TwinOath};
                        bool queued=JanusHard&&_janusHardQueued!=JanusPattern.None;
                        var next=JanusHard?NextJanusHardPattern():intro[_janusNext%intro.Length];_janusNext++;
                        if(!JanusHard&&_janusCounterUnlocked&&_janusNext%7==0&&
                            !queued&&RaidCombatAuthority.ServerTime-_janusLastCounterEnd>=JanusTuning.counterRepeatGap)BeginJanusCounter();
                        else BeginJanusCast(next);
                    }
                }
            }
            else if(_janusStep==JanusStep.Cast)
            {
                _janusRemaining=Mathf.Max(0,_janusRemaining-dt);_castRemainingTime=_janusRemaining;
                if(_janusPattern==JanusPattern.OmegaLine&&!_janusLineLocked&&_janusRemaining<=_castTotalDuration*.5f)LockJanusLine();
                if(_janusPattern==JanusPattern.SealMark&&!_janusLineLocked&&_janusRemaining<=_castTotalDuration*.5f)LockJanusMarks();
                if(JanusHard)TickJanusHardCastTelegraph();
                if(_janusRemaining<=0)ResolveJanusCast();
            }
            else if(_janusStep==JanusStep.Posts)TickJanusPosts(dt);
            else if(_janusStep==JanusStep.CounterPrepare)
            {_janusRemaining=Mathf.Max(0,_janusRemaining-dt);_castRemainingTime=_janusRemaining;
             if(_janusRemaining<=0)OpenJanusCounter();}
            else if(_janusStep==JanusStep.CounterWindow)
            {_janusRemaining=Mathf.Max(0,_janusRemaining-dt);_castRemainingTime=_janusRemaining;
             if(_janusRemaining<=0)FinishJanusCounter();}
            else if(_janusStep==JanusStep.Groggy)
            {
                if(_janusDamageStaggerGroggy && JanusStaggerPriorityDue){EndJanus(JanusTuning.recoverySeconds);_janusRest=0;}
                else {_janusRemaining=Mathf.Max(0,_janusRemaining-dt);_groggyRemainingTime=_janusRemaining;
                 if(_janusRemaining<=0)EndJanus(JanusTuning.recoverySeconds);}
            }
            if(!_janusStoryUsed&&currentHp>0&&HpRatio<=JanusTuning.storyThreshold)
            {_janusStoryUsed=true;_janusStory=StartCoroutine(JanusStory());}
            UpdateTopBossUI();UpdateCastBanner();
        }
        private void BeginJanusCast(JanusPattern pattern)
        {
            ClearJanusAttack();_janusStep=JanusStep.Cast;_janusPattern=pattern;fsmState=BossFSMState.CastingSkill;
            _janusRemaining=JanusHard?JanusHardRules.Tempo(janusHard.castSeconds,janusHard.attackTempo):JanusTuning.castSeconds;
            _castTotalDuration=_castRemainingTime=_janusRemaining;
            bool omega=pattern==JanusPattern.OmegaLine||pattern==JanusPattern.OmegaCircle;
            _janusOrigin=omega&&_janusOmega!=null?_janusOmega.transform.position:transform.position;
            _janusForward=omega&&_janusOmega!=null?_janusOmega.transform.forward:transform.forward;
            _janusRight=Random.value>=.5f;_janusLineLocked=false;_janusHardFollowupShown=false;
            var party=GetLivingPartyMemberTransforms();
            _janusTarget=pattern==JanusPattern.AlphaStrike?ResolveLivingAggroTarget():
                party.Count>0?party[Random.Range(0,party.Count)]:null;
            string name="";
            switch(pattern)
            {
                case JanusPattern.AlphaStrike:name="알파의 성문 강타";
                    if(_janusTarget!=null)_janusMarkers.Add(AttachedPartyMechanicMarker.SpawnTargetMarker(_janusTarget,1.5f));
                    JanusTrigger(JanusTuning.alphaTankTrigger,false);break;
                case JanusPattern.AlphaSweep:name="알파의 방벽 휩쓸기 · "+(_janusRight?"오른쪽":"왼쪽");
                    if(!JanusHard)JanusGround(AoETelegraphType.FrontCone_120Deg,_janusOrigin,Vector3.Cross(Vector3.up,_janusForward)*(_janusRight?1:-1),_janusRemaining,0,JanusTuning.halfRadius,180);
                    JanusTrigger(JanusTuning.alphaHalfTrigger,false);break;
                case JanusPattern.OmegaLine:name="오메가의 관통 지시 · 추적 후 고정";
                    if(_janusTarget!=null)_janusMarkers.Add(AttachedPartyMechanicMarker.SpawnTargetMarker(_janusTarget,JanusTuning.lineWidth));
                    JanusTrigger(JanusTuning.omegaLineTrigger,true);break;
                case JanusPattern.OmegaCircle:name="오메가의 처형 원무";
                    if(!JanusHard)JanusGround(AoETelegraphType.Circle_Radius6,_janusOrigin,_janusForward,_janusRemaining,0,JanusTuning.circleRadius);
                    JanusTrigger(JanusTuning.omegaCircleTrigger,true);break;
                case JanusPattern.SealMark:name="봉인의 낙인 · 추적 후 고정";
                    foreach(var member in party)_janusMarkers.Add(AttachedPartyMechanicMarker.SpawnTargetMarker(member,JanusTuning.markRadius));
                    JanusTrigger(JanusTuning.markTrigger,false);break;
                case JanusPattern.TwinOath:name="쌍둥이의 맹약 · 집합";
                    if(_janusTarget!=null)_janusMarkers.Add(AttachedPartyMechanicMarker.SpawnStackMarker(_janusTarget,JanusTuning.stackRadius));
                    JanusTrigger(JanusTuning.stackTrigger,false);break;
                case JanusPattern.ShieldSpearCross:name="방패와 창의 교차 · 알파 반면 → 오메가 직선";
                    JanusTrigger(janusHard.crossTrigger,false);break;
                case JanusPattern.RedChainWave:name="붉은 사슬 파동 · I → II";
                    JanusHardGuide("I · 첫 파동",new Vector3(0,0,-8));
                    JanusHardGuide("II · 다음 파동",new Vector3(0,0,-12));
                    JanusTrigger(janusHard.chainWaveTrigger,false);break;
            }
            _currentCastSkillName=(omega?"오메가 · ":"알파 · ")+name;
            SetMechanicAlert(_currentCastSkillName,pattern==JanusPattern.TwinOath?Color.yellow:Color.red,_janusRemaining);
        }
        private void LockJanusLine()
        {
            _janusLineLocked=true;
            if(_janusTarget!=null){var delta=_janusTarget.position-_janusOrigin;delta.y=0;if(delta.sqrMagnitude>.01f)_janusForward=delta.normalized;}
            foreach(var marker in _janusMarkers)if(marker!=null)Destroy(marker.gameObject);_janusMarkers.Clear();
            if(!JanusHard)JanusGround(AoETelegraphType.Rectangle,_janusOrigin,_janusForward,_janusRemaining,JanusTuning.lineWidth,JanusTuning.lineLength);
            SetMechanicAlert("오메가 · 관통 방향 고정 · 옆으로 이탈",Color.red,_janusRemaining);
        }
        private void LockJanusMarks()
        {
            _janusLineLocked=true;
            foreach(var marker in _janusMarkers)if(marker!=null&&CombatRange.LivingPartyMember(marker.followTarget))
            {_janusMarkPoints.Add(marker.followTarget.position);
             if(JanusHard)JanusHardGuide("낙인 고정",marker.followTarget.position);
             else JanusGround(AoETelegraphType.Circle_Radius6,marker.followTarget.position,Vector3.forward,_janusRemaining,0,JanusTuning.markRadius);}
            foreach(var marker in _janusMarkers)if(marker!=null)Destroy(marker.gameObject);_janusMarkers.Clear();
            SetMechanicAlert("봉인의 낙인 고정 · 범위 이탈",Color.red,_janusRemaining);
        }
        private void ResolveJanusCast()
        {
            var living=GetLivingPartyMemberTransforms();
            foreach(var member in living)
            {
                bool hit=false;float damage=0;
                switch(_janusPattern)
                {
                    case JanusPattern.AlphaStrike:hit=member==_janusTarget;damage=JanusTuning.tankDamage;break;
                    case JanusPattern.AlphaSweep:hit=FloraNormalRules.InHalf(member.position,_janusOrigin,Vector3.Cross(Vector3.up,_janusForward),JanusTuning.halfRadius,_janusRight);damage=JanusTuning.halfDamage;break;
                    case JanusPattern.OmegaLine:
                        var delta=member.position-_janusOrigin;var right=Vector3.Cross(Vector3.up,_janusForward);
                        hit=GuardianNormalState.InRectangle(Vector3.Dot(delta,right),Vector3.Dot(delta,_janusForward),JanusTuning.lineWidth,JanusTuning.lineLength);damage=JanusTuning.lineDamage;break;
                    case JanusPattern.OmegaCircle:hit=FloraNormalRules.InCircle(member.position,_janusOrigin,JanusTuning.circleRadius);damage=JanusTuning.circleDamage;break;
                    case JanusPattern.SealMark:foreach(var point in _janusMarkPoints)if(FloraNormalRules.InCircle(member.position,point,JanusTuning.markRadius)){hit=true;break;}damage=JanusTuning.markDamage;break;
                }
                if(hit)DamagePartyMember(member,damage,_janusPattern==JanusPattern.AlphaStrike?CombatDamageKind.Direct:CombatDamageKind.Area,"janus."+_janusPattern);
            }
            if(_janusPattern==JanusPattern.TwinOath&&_janusTarget!=null&&CombatRange.LivingPartyMember(_janusTarget))
            {int count=0;foreach(var member in living)if(FloraNormalRules.InCircle(member.position,_janusTarget.position,JanusTuning.stackRadius))count++;
             foreach(var member in living)if(FloraNormalRules.InCircle(member.position,_janusTarget.position,JanusTuning.stackRadius))
                 DamagePartyMember(member,JanusTuning.stackDamage/Mathf.Max(1,count),CombatDamageKind.Area,"janus.stack");}
            if(JanusHard&&_janusHardTransformed&&_janusPattern==JanusPattern.TwinOath)
                _janusHardQueued=JanusPattern.ShieldSpearCross;
            if(!StageManager.CombatSuspended&&JanusHard&&BeginJanusHardFollowup())return;
            if(!StageManager.CombatSuspended)EndJanus(JanusTuning.recoverySeconds);
        }
        public bool JanusStaggerLocked => !JanusEncounter || currentHp<=0 ||
            (_janusStep!=JanusStep.Rest && _janusStep!=JanusStep.Cast && _janusStep!=JanusStep.HardFollowup) ||
            JanusStaggerPriorityDue;
        private bool JanusStaggerPriorityDue => JanusNormalRules.PostDue(_janusPostUsed,currentHp>0,HpRatio,JanusTuning.postThreshold) ||
            (JanusHard && JanusHardRules.DueTransformation(_janusHardTransformed,HpRatio,janusHard.transformationThreshold));

        public bool TryJanusStaggerGroggy(float seconds)
        {
            if(RaidCombatAuthority.Replica || StageManager.CombatSuspended || JanusStaggerLocked ||
                float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds<=0)return false;
            EndJanus(JanusTuning.recoverySeconds);
            _janusStep=JanusStep.Groggy;fsmState=BossFSMState.Groggy;
            _janusRemaining=_groggyRemainingTime=seconds;
            _janusDamageStaggerGroggy=true;
            return true;
        }

        private void EndJanus(float recovery)
        {
            _janusDamageStaggerGroggy=false;
            NotifyDamageCastEnded();
            ClearJanusAttack();_janusStep=JanusStep.Rest;fsmState=BossFSMState.IdleChasing;
            _janusRest=JanusHard?JanusHardRules.Tempo(JanusTuning.patternInterval,janusHard.attackTempo)+recovery:
                JanusTuning.patternInterval+recovery;_janusRemaining=0;
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=_groggyRemainingTime=0;
        }
        private IEnumerator JanusStory()
        {
            while(currentHp>0&&_janusStep!=JanusStep.Rest&&_janusStep!=JanusStep.Groggy)yield return null;
            if(currentHp<=0)yield break;
            string speaker=JanusTuning.storySpeaker==1?"오메가":"알파";
            FindFirstObjectByType<RaidLobbyUI>()?.Say(speaker+" · 단장님... 죄송합니다... 그들을 막지 못했습니다...");
            yield return new WaitForSeconds(3);
            if(currentHp>0)FindFirstObjectByType<RaidLobbyUI>()?.Say(speaker+" · 부디... 저 문 너머의 진실 앞에서 부서지지 말기를...");
            _janusStory=null;
        }
        private bool JanusAcceptAreaHit(Transform source,string origin,CombatDamageKind kind,float amount,bool viaOmega)
        {
            if(kind!=CombatDamageKind.Area)return true;
            int frame=Time.frameCount;
            if(frame!=_janusAreaFrame){_janusAreaFrame=frame;_janusAreaBuckets.Clear();}
            int index=_janusAreaBuckets.FindIndex(b=>b.source==source&&b.origin==origin);
            var bucket=index>=0?_janusAreaBuckets[index]:new JanusAreaBucket{source=source,origin=origin};
            if(viaOmega)bucket.omega++;else bucket.alpha++;
            bool accept=JanusNormalRules.AcceptPairedAreaHit(viaOmega?bucket.omega:bucket.alpha,
                viaOmega?bucket.alpha:bucket.omega);
            if(index>=0)_janusAreaBuckets[index]=bucket;else _janusAreaBuckets.Add(bucket);
            return accept;
        }
        public float ReceiveJanusOmegaDamage(float amount,Transform source,string origin,CombatDamageKind kind,bool alreadyScaled=false)
        {
            if(!JanusEncounter)return 0;
            float before=currentHp;ApplyBossHpDamage(amount,source,origin,kind,alreadyScaled,true);
            return Mathf.Max(0,before-currentHp);
        }
    }
}
