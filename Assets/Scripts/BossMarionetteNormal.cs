using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        [Header("Stage 4 normal: Marionette (initial tuning)")]
        public MarionetteNormalSettings marionette=new MarionetteNormalSettings();
        [Header("Stage 4 hard: Marionette (initial tuning)")]
        public MarionetteHardSettings marionetteHard=new MarionetteHardSettings();
        public bool MarionetteEncounter => currentStageNumber==4 &&
            (CampaignRuntime.Instance==null || CampaignRuntime.Instance.DifficultyId==CampaignCatalog.Normal ||
             CampaignRuntime.Instance.DifficultyId==CampaignCatalog.Hard);
        public bool MarionetteHard => MarionetteEncounter && CampaignRuntime.Instance?.DifficultyId==CampaignCatalog.Hard;
        public bool MarionetteNormal => MarionetteEncounter && !MarionetteHard;
        private MarionetteNormalSettings MarTuning => MarionetteHard ? marionetteHard.combat : marionette;
        public float MarionetteStageHalfSize => MarTuning.stageHalfSize;
        private float MarionetteClimaxSeconds => MarionetteHard?12:MarTuning.climaxSeconds;
        private MarionettePattern _marPattern;
        private MarionetteStep _marStep;
        private bool _marDamageStaggerGroggy;
        private float _marRemaining,_marRest,_marStagger;
        private int _marNext=1;
        private bool _marClimaxUsed,_marStoryUsed,_marCounterOpen,_marLightLocked,_marFirewallShown;
        private Transform _marTarget;
        private Vector3 _marOrigin,_marForward;
        private bool _marRight;
        private BossAddMinion _marString;
        private readonly List<AoETelegraph> _marGround=new List<AoETelegraph>();
        private readonly List<Transform> _marLightTargets=new List<Transform>();
        private readonly List<Vector3> _marLightPoints=new List<Vector3>();
        private readonly List<AttachedPartyMechanicMarker> _marLightMarkers=new List<AttachedPartyMechanicMarker>();
        private AttachedPartyMechanicMarker _marMarker;
        private TextMesh _marCounterLabel;
        private TextMesh _marControlledLabel;
        private GameObject _marModel,_marPuppetStage,_marFirewall;
        private GameObject _marPose;
        private TextMesh _marPlayCaption;
        private int _marPlayBeat=-1;
        private Renderer[] _marHiddenBody;
        private AudioSource _marAudio;
        private Coroutine _marStoryCoroutine;

        public bool MarionetteControlled(Transform actor) => MarionetteEncounter && actor!=null &&
            _marStep==MarionetteStep.Control && actor==_marTarget;
        public bool MarionetteAiCounterReady(Transform actor) => MarionetteEncounter &&
            _marStep==MarionetteStep.Cast && _marPattern==MarionettePattern.StageCharge &&
            _marCounterOpen && CombatRange.LivingPartyMember(actor);
        public bool MarionetteTankbusterImminent(Transform actor,float within) => MarionetteEncounter &&
            _marStep==MarionetteStep.Cast && _marPattern==MarionettePattern.LeadExecution &&
            _marTarget==actor && _marRemaining<=within;

        private void ResetMarionette()
        {
            ClearMarionette();
            marionette=marionette??new MarionetteNormalSettings();
            marionetteHard=marionetteHard??new MarionetteHardSettings();
            marionetteHard.combat=marionetteHard.combat??new MarionetteNormalSettings();
            ResetMarionetteHard();
            if(MarionetteHard)autoAttackInterval=MarionetteHardRules.Tempo(autoAttackInterval,marionetteHard.attackTempo);
            _marNext=1;_marClimaxUsed=true;_marStoryUsed=false;
            _marRest=MarionetteHard?MarionetteHardRules.Tempo(MarTuning.patternInterval,marionetteHard.attackTempo):MarTuning.patternInterval;
            if(!MarionetteEncounter)return;
            currentBossTitle=MarionetteHard?"버려진 오페라 극장 : 기억의 인형사, 마리오네트 · 하드":"버려진 오페라 극장 : 기억의 인형사, 마리오네트";
            maxHp=RaidBossHealthTuning.Resolve(MarionetteHard && marionetteHard.healthOverride>0?marionetteHard.healthOverride:marionette.health,MarionetteHard);currentHp=maxHp;
            arenaBoundaryRadius=MarTuning.stageHalfSize*Mathf.Sqrt(2);
            autoAttackDamageToTank=110;
            _stagePhase1BodyColor=new Color(.55f,.3f,.48f);
            SetBossBodyColor(_stagePhase1BodyColor);
            if(MarTuning.bossVisualPrefab!=null)
            {
                _marModel=Instantiate(MarTuning.bossVisualPrefab,transform);
                _marModel.name="Marionette authored visual";
                foreach(var collider in _marModel.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            else BuildTemporaryMarionette();
            var body=GetBossBodyTransform();
            if(body!=null){_marHiddenBody=body.GetComponentsInChildren<Renderer>();foreach(var renderer in _marHiddenBody)renderer.enabled=false;}
            var animator=_marModel.GetComponentInChildren<Animator>();
            if(animator!=null && MarTuning.bossAnimatorController!=null)animator.runtimeAnimatorController=MarTuning.bossAnimatorController;
        }
        private void BuildTemporaryMarionette()
        {
            _marModel=new GameObject("Temporary four armed clockwork puppeteer · replace in marionette settings");
            _marModel.transform.SetParent(transform,false);
            MarionettePart(PrimitiveType.Capsule,"Clockwork body",new Vector3(0,1.5f,0),new Vector3(1.8f,2.4f,1.1f),new Color(.4f,.28f,.36f));
            MarionettePart(PrimitiveType.Sphere,"Half smiling, half crying porcelain mask",new Vector3(0,3,0),
                new Vector3(1.3f,1.4f,.6f),new Color(.82f,.77f,.7f));
            for(int i=0;i<4;i++)MarionettePart(PrimitiveType.Capsule,"Clockwork arm "+(i+1),
                new Vector3(i<2?-1.25f:1.25f,i%2==0?2.4f:1.5f,.2f),new Vector3(.24f,2,.24f),new Color(.65f,.48f,.18f));
        }
        private GameObject MarionettePart(PrimitiveType type,string name,Vector3 at,Vector3 scale,Color color)
        {
            var obj=RaidPrimitive.Create(type);obj.name=name;obj.transform.SetParent(_marModel.transform,false);
            obj.transform.localPosition=at;obj.transform.localScale=scale;obj.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            obj.GetComponent<Renderer>().SetPropertyBlock(block);return obj;
        }
        private void ClearMarionette(bool keepModel=false)
        {
            ClearMarionetteHard(keepModel);
            ClearMarionetteAttack();ClearMarionetteString();StopMarionettePlay();ClearMarionetteFirewall();
            if(_marStoryCoroutine!=null){StopCoroutine(_marStoryCoroutine);_marStoryCoroutine=null;}
            if(!keepModel && _marModel!=null)Destroy(_marModel);
            if(!keepModel)_marModel=null;
            if(!keepModel && _marHiddenBody!=null)
            {foreach(var renderer in _marHiddenBody)if(renderer!=null)renderer.enabled=true;_marHiddenBody=null;}
            _marStep=MarionetteStep.Rest;_marPattern=MarionettePattern.None;_marTarget=null;
            _marRemaining=_marStagger=0;_marCounterOpen=false;_marDamageStaggerGroggy=false;
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
            if(StageManager.Instance?.currentStage==4)FindFirstObjectByType<RaidLobbyUI>()?.Hint(null);
        }
        private void ClearMarionetteString()
        {
            var stringAdd=_marString;_marString=null;
            if(stringAdd!=null && !stringAdd.isDead)stringAdd.KillAddMinion(true);
            ClearMarionetteHardStrings();
        }
        private void ClearMarionetteAttack()
        {
            GetComponent<CombatCounterTarget>()?.CloseWindow();_marCounterOpen=false;
            if(_marCounterLabel!=null)Destroy(_marCounterLabel.gameObject);_marCounterLabel=null;
            if(_marPose!=null)Destroy(_marPose);_marPose=null;
            if(_marControlledLabel!=null)Destroy(_marControlledLabel.gameObject);_marControlledLabel=null;
            if(_marMarker!=null)Destroy(_marMarker.gameObject);_marMarker=null;
            foreach(var marker in _marLightMarkers)if(marker!=null)Destroy(marker.gameObject);
            _marLightMarkers.Clear();_marLightTargets.Clear();_marLightPoints.Clear();
            foreach(var ground in _marGround)if(ground!=null)Destroy(ground.gameObject);
            _marGround.Clear();
        }
        private void MarionetteGround(AoETelegraphType type,Vector3 point,Vector3 direction,float duration,float inner,float outer,float angle=120)
        { _marGround.Add(AoETelegraph.Spawn(type,point,direction,duration,inner,outer,angle)); }

        public bool MarionetteStaggerLocked => !MarionetteEncounter || currentHp<=0 ||
            (_marStep!=MarionetteStep.Rest && _marStep!=MarionetteStep.Cast &&
             _marStep!=MarionetteStep.HardFollowup && _marStep!=MarionetteStep.HardResidue) ||
            (_marStep==MarionetteStep.Cast && (_marPattern==MarionettePattern.TraitorRole ||
             _marPattern==MarionettePattern.Climax || _marPattern==MarionettePattern.Reenact ||
             _marPattern==MarionettePattern.GoldenCrossing)) ||
            MarionetteStaggerPriorityDue;
        private bool MarionetteStaggerPriorityDue => MarionetteHard ?
             MarionetteHardRules.TransformDue(_marHardTransformed,HpRatio,marionetteHard.transformationThreshold) ||
             MarionetteHardRules.ClimaxDue(_marHardTransformed,_marHardClimaxStarted,HpRatio,marionetteHard.climaxThreshold) ||
             _marHardClimaxPreparing || _marHardMemoryPending :
             MarionetteNormalRules.ClimaxDue(_marClimaxUsed,HpRatio,MarTuning.climaxThreshold);

        public bool TryMarionetteStaggerGroggy(float seconds)
        {
            if(RaidCombatAuthority.Replica || StageManager.CombatSuspended || MarionetteStaggerLocked ||
                float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds<=0)return false;
            EndMarionette(true,seconds);
            _marDamageStaggerGroggy=true;
            return true;
        }

        private void TickMarionette()
        {
            float dt=Time.deltaTime;
            TickMarionettePose();
            if(MarionetteHard && TickMarionetteHard(dt)) { }
            else if(_marStep==MarionetteStep.Rest)
            {
                fsmState=BossFSMState.IdleChasing;
                if(!MarionetteHard || !_marHardRestProtected)
                {RotateTowardsAggroTargetXZ();UpdateMeleePursuit(dt);UpdateAutoAttack();}
                _marRest-=dt;
                if(_marRest<=0)
                {
                    if(MarionetteHard && TryBeginMarionetteHardSpecial()) { }
                    else if(!MarionetteHard && MarionetteNormalRules.ClimaxDue(_marClimaxUsed,HpRatio,MarTuning.climaxThreshold))BeginMarionette(MarionettePattern.Climax);
                    else if(MarionetteHard)BeginNextMarionetteHardPattern();
                    else
                    {
                        var next=(MarionettePattern)_marNext;_marNext=_marNext>=7?1:_marNext+1;
                        if(next!=MarionettePattern.TraitorRole || MarionetteNormalRules.RescueAllowed(GetLivingPartyMemberTransforms().Count))BeginMarionette(next);
                        else _marRest=MarTuning.recoverySeconds;
                    }
                }
            }
            else if(_marStep==MarionetteStep.Groggy)
            {
                if(_marDamageStaggerGroggy && MarionetteStaggerPriorityDue)
                {EndMarionette();_marRest=0;}
                else
                {
                _groggyRemainingTime=_marRemaining=Mathf.Max(0,_marRemaining-dt);
                if(_marRemaining<=0)EndMarionette();
                }
            }
            else
            {
                _marRemaining=Mathf.Max(0,_marRemaining-dt);_castRemainingTime=_marRemaining;
                if(_marStep==MarionetteStep.Cast)TickMarionetteCast();
                else if(_marStep==MarionetteStep.Control)
                {
                    if(_marTarget==null || !CombatRange.LivingPartyMember(_marTarget))FinishMarionetteControl(false,false);
                    else {if(MarionetteHard)TickMarionetteHardPull(dt);if(_marRemaining<=0)FinishMarionetteControl(false,true);}
                }
                else if(_marStep==MarionetteStep.Climax)
                { TickMarionettePlay();if(MarionetteHard)TickMarionetteHardPulse();if(_marRemaining<=0)FinishMarionetteClimax(false); }
            }
            if(!_marStoryUsed && currentHp>0 && HpRatio<=MarTuning.storyThreshold)
            { _marStoryUsed=true;_marStoryCoroutine=StartCoroutine(MarionetteStory()); }
            UpdateTopBossUI();UpdateCastBanner();UpdateMarionetteCastUI();
            UpdateMarionetteControlLabel();
        }
        private IEnumerator MarionetteStory()
        {
            while(currentHp>0 && _marStep!=MarionetteStep.Rest && _marStep!=MarionetteStep.Groggy)yield return null;
            if(currentHp<=0)yield break;
            FindFirstObjectByType<RaidLobbyUI>()?.Say("마리오네트 · 연극을 멈춰도... 결말은 바뀌지 않아...");
            yield return new WaitForSeconds(3);
            if(currentHp>0)FindFirstObjectByType<RaidLobbyUI>()?.Say("마리오네트 · 불쌍한 성녀님... 저들을 위해 몇 번이나 더 부서지실 건가요...");
            _marStoryCoroutine=null;
        }
        private void BeginMarionette(MarionettePattern pattern)
        {
            ClearMarionetteAttack();_marPattern=pattern;_marStep=MarionetteStep.Cast;
            _marHardImpactShown=false;
            fsmState=BossFSMState.CastingSkill;
            _marRemaining=MarionetteHard?MarionetteHardRules.Tempo(MarTuning.castSeconds,marionetteHard.attackTempo):MarTuning.castSeconds;
            _castTotalDuration=_castRemainingTime=_marRemaining;
            _marOrigin=transform.position;_marForward=transform.forward;_marRight=Random.value>=.5f;
            _marTarget=ResolveLivingAggroTarget();_marLightLocked=false;
            if(pattern==MarionettePattern.TraitorRole){BeginMarionetteControl();return;}
            if(pattern==MarionettePattern.Climax){BeginMarionetteClimax();return;}
            if(pattern==MarionettePattern.Reenact){BeginMarionetteReenact();return;}
            if(pattern==MarionettePattern.GoldenCrossing){BeginMarionetteCrossing();return;}
            PlayMarionetteTrigger(pattern);
            switch(pattern)
            {
                case MarionettePattern.LeadExecution:
                    _currentCastSkillName="주연 처형";
                    if(_marTarget!=null)_marMarker=AttachedPartyMechanicMarker.SpawnTargetMarker(_marTarget,1.5f);
                    SetMechanicAlert("주연 처형 · 탱커 방어와 회복",Color.red,_marRemaining);break;
                case MarionettePattern.HalfCurtain:
                    _currentCastSkillName="반쪽의 막";
                    if(!MarionetteHard)MarionetteGround(AoETelegraphType.FrontCone_120Deg,_marOrigin,Vector3.Cross(Vector3.up,_marForward)*(_marRight?1:-1),_marRemaining,0,MarTuning.stageHalfSize,180);
                    SetMechanicAlert("반쪽의 막 · 팔과 커튼 반대쪽으로 이동",Color.red,_marRemaining);break;
                case MarionettePattern.TragicCenter:
                    _currentCastSkillName="비극의 중심";
                    if(!MarionetteHard)MarionetteGround(AoETelegraphType.Circle_Radius6,_marOrigin,_marForward,_marRemaining,0,MarTuning.centerRadius);
                    SetMechanicAlert("비극의 중심 · 원형 바깥으로 이동",Color.red,_marRemaining);break;
                case MarionettePattern.TrackingLight:
                    _currentCastSkillName="추적 조명";
                    foreach(var member in GetLivingPartyMemberTransforms())
                    { _marLightTargets.Add(member);_marLightMarkers.Add(AttachedPartyMechanicMarker.SpawnTargetMarker(member,MarTuning.lightRadius)); }
                    SetMechanicAlert("추적 조명 · 산개, 고정 후 이탈",Color.red,_marRemaining);break;
                case MarionettePattern.AudienceApplause:
                    _currentCastSkillName="관객의 박수";
                    var party=GetLivingPartyMemberTransforms();_marTarget=party.Count>0?party[Random.Range(0,party.Count)]:null;
                    if(_marTarget!=null)_marMarker=AttachedPartyMechanicMarker.SpawnStackMarker(_marTarget,MarTuning.stackRadius);
                    SetMechanicAlert("관객의 박수 · 집합해 분담",new Color(1,.65f,.2f),_marRemaining);break;
                case MarionettePattern.StageCharge:
                    _currentCastSkillName="무대 돌진";
                    if(!MarionetteHard)MarionetteGround(AoETelegraphType.Rectangle,_marOrigin,_marForward,_marRemaining,MarTuning.chargeWidth,MarTuning.chargeLength);
                    SetMechanicAlert("⚔ 금색 일반 카운터 · 정면 기술 적중 또는 직선 회피",new Color(1,.75f,.15f),_marRemaining);break;
            }
        }
        private void TickMarionetteCast()
        {
            if(MarionetteHard)TickMarionetteHardCast();
            if(_marPattern==MarionettePattern.StageCharge && !_marCounterOpen && _marRemaining<=(MarionetteHard?marionetteHard.counterWindow:MarTuning.counterWindow))
            {
                _marCounterOpen=true;CombatCounterTarget.Ensure(transform).OpenWindow(_marRemaining);
                _marCounterLabel=FloraLabel("⚔ COUNTER",new Color(1,.78f,.15f));
                _marCounterLabel.transform.position=transform.position+Vector3.up*4.5f;
                PlayMarionetteCue(MarTuning.counterCue);
            }
            if(_marPattern==MarionettePattern.TrackingLight && !_marLightLocked && _marRemaining<=1)
            {
                _marLightLocked=true;
                foreach(var target in _marLightTargets)
                {
                    if(!CombatRange.LivingPartyMember(target))continue;
                    Vector3 point=target.position;
                    _marLightPoints.Add(point);
                    if(!MarionetteHard || _marRemaining<=marionetteHard.impactWarning)
                        MarionetteGround(AoETelegraphType.Circle_Radius6,point,Vector3.forward,_marRemaining,0,MarTuning.lightRadius);
                }
                foreach(var marker in _marLightMarkers)if(marker!=null)Destroy(marker.gameObject);
                _marLightMarkers.Clear();
                SetMechanicAlert("추적 조명 위치 고정 · 원 밖으로 이탈",Color.red,_marRemaining);
            }
            if(_marRemaining<=0)ResolveMarionetteAttack();
        }
        private void ResolveMarionetteAttack()
        {
            if(currentHp<=0)return;
            var living=GetLivingPartyMemberTransforms();
            foreach(var member in living)
            {
                bool hit=false;float damage=0;
                switch(_marPattern)
                {
                    case MarionettePattern.LeadExecution:hit=member==_marTarget;damage=MarTuning.tankDamage;break;
                    case MarionettePattern.HalfCurtain:
                        hit=FloraNormalRules.InHalf(member.position,_marOrigin,Vector3.Cross(Vector3.up,_marForward),MarTuning.stageHalfSize,_marRight);
                        damage=MarTuning.halfDamage;break;
                    case MarionettePattern.TragicCenter:
                        hit=FloraNormalRules.InCircle(member.position,_marOrigin,MarTuning.centerRadius);damage=MarTuning.centerDamage;break;
                    case MarionettePattern.TrackingLight:
                        foreach(var point in _marLightPoints)if(FloraNormalRules.InCircle(member.position,point,MarTuning.lightRadius)){hit=true;break;}
                        damage=MarTuning.lightDamage;break;
                    case MarionettePattern.StageCharge:
                        hit=MarionetteHard?MarionetteHardChargeHit(member.position):_marGround.Count>0 && _marGround[0]!=null && _marGround[0].IsPointInsideXZ(member.position);
                        damage=MarTuning.chargeDamage;break;
                }
                if(hit)DamagePartyMember(member,damage,_marPattern==MarionettePattern.LeadExecution?
                    CombatDamageKind.Direct:CombatDamageKind.Area,"MarTuning."+_marPattern);
            }
            if(_marPattern==MarionettePattern.AudienceApplause && _marTarget!=null && CombatRange.LivingPartyMember(_marTarget))
            {
                int count=0;foreach(var member in living)if(FloraNormalRules.InCircle(member.position,_marTarget.position,MarTuning.stackRadius))count++;
                foreach(var member in living)if(FloraNormalRules.InCircle(member.position,_marTarget.position,MarTuning.stackRadius))
                    DamagePartyMember(member,MarTuning.stackDamage/Mathf.Max(1,count),CombatDamageKind.Area,"MarTuning.AudienceApplause");
            }
            if(_marPattern==MarionettePattern.StageCharge)CombatMetricsManager.Event("마리오네트 일반 카운터 미성공 · 무대 돌진");
            if(StageManager.CombatSuspended)return;
            if(MarionetteHard && TryBeginMarionetteHardAfterImpact())return;
            if(!StageManager.CombatSuspended)EndMarionette();
        }
        private void EndMarionette(bool groggy=false,float groggySeconds=0,float recovery=0)
        {
            _marDamageStaggerGroggy=false;
            NotifyDamageCastEnded();
            ClearMarionetteAttack();
            _marStep=groggy?MarionetteStep.Groggy:MarionetteStep.Rest;
            fsmState=groggy?BossFSMState.Groggy:BossFSMState.IdleChasing;
            _marRemaining=groggy?groggySeconds:0;_groggyRemainingTime=_marRemaining;
            _marRest=(recovery>0?recovery:MarTuning.recoverySeconds)+
                (MarionetteHard?MarionetteHardRules.Tempo(MarTuning.patternInterval,marionetteHard.attackTempo):MarTuning.patternInterval);
            if(MarionetteHard)_marHardRestProtected=recovery>0;
            _currentCastSkillName="";_castRemainingTime=_castTotalDuration=0;
        }
        public void MarionetteCounterSucceeded(Transform attacker)
        {
            if(!MarionetteAiCounterReady(attacker))return;
            CombatMetricsManager.Event("마리오네트 일반 카운터 성공: "+attacker.name);
            SetMechanicAlert("⚔ 무대 돌진 중단 · 그로기",new Color(1,.8f,.25f),2);
            PlayMarionetteCue(MarTuning.counterCue);
            EndMarionette(true,AegisPaladinData.CounterGroggy);
        }
        private void BeginMarionetteControl()
        {
            var living=GetLivingPartyMemberTransforms();
            if(!MarionetteNormalRules.RescueAllowed(living.Count)){EndMarionette();return;}
            _marTarget=living[Random.Range(0,living.Count)];
            _marTarget.rotation=Quaternion.Euler(0,_marTarget.eulerAngles.y+25,0);
            if(RaidCombatAuthority.Host)RaidNetworkSession.Instance?.CommitSkillRelocation(_marTarget);
            _marStep=MarionetteStep.Control;_marRemaining=MarionetteHard?marionetteHard.rescueSeconds:MarTuning.rescueSeconds;
            _castTotalDuration=_castRemainingTime=_marRemaining;_currentCastSkillName="배역 지정: 배신자";
            PlayMarionetteTrigger(MarionettePattern.TraitorRole);PlayMarionetteCue(MarTuning.controlCue);
            Vector3 back=MarionetteNormalRules.ClampToStage(_marTarget.position-_marTarget.forward*1.8f,MarTuning.stageHalfSize,1.4f);
            Vector3 separation=back-_marTarget.position;separation.y=0;
            if(separation.sqrMagnitude<2.56f)
            {
                Vector3 inward=new Vector3(-_marTarget.position.x,0,-_marTarget.position.z);
                if(inward.sqrMagnitude<.01f)inward=Vector3.right;
                back=MarionetteNormalRules.ClampToStage(_marTarget.position+inward.normalized*1.8f,MarTuning.stageHalfSize,1.4f);
            }
            var spawner=GetComponent<BossAddSpawner>()??gameObject.AddComponent<BossAddSpawner>();spawner.boss=this;
            if(MarionetteHard)SpawnMarionetteHardStrings(spawner,back);
            else
            {
                _marString=new GameObject("인형의 실 · 구출 대상").AddComponent<BossAddMinion>();
                _marString.InitializeAdd(AddMinionType.MarionetteString,this,spawner,back,EncounterTemplate);
                _marString.aggroTarget=_marTarget;spawner.activeAdds.Add(_marString);
            }
            UpdateMarionetteControlLabel();
            SetMechanicAlert("황금 실 · 조종당한 "+_marTarget.name+" 등 뒤의 인형의 실을 공격!",new Color(1,.8f,.25f),_marRemaining);
            CombatMetricsManager.Event("마리오네트 배역 지정 · 조종 대상: "+_marTarget.name);
        }
        public void NotifyMarionetteStringDestroyed(BossAddMinion stringAdd)
        {
            if(!MarionetteEncounter || _marStep!=MarionetteStep.Control)return;
            if(MarionetteHard){NotifyMarionetteHardStringDestroyed(stringAdd);return;}
            if(stringAdd!=_marString)return;
            FinishMarionetteControl(true,false);
        }
        public void MarionetteControlOwnerDisconnected(Transform actor)
        {
            if(MarionetteEncounter && _marStep==MarionetteStep.Control && actor==_marTarget)
                FinishMarionetteControl(false,false);
        }
        private void FinishMarionetteControl(bool rescued,bool timeout)
        {
            if(_marStep!=MarionetteStep.Control)return;
            var target=_marTarget;
            ClearMarionetteString();_marTarget=null;
            if(rescued)
            {
                PlayMarionetteCue(MarTuning.rescueCue);
                CombatMetricsManager.Event("마리오네트 인형의 실 절단 · 동료 구출");
                SetMechanicAlert("인형의 실 절단 · 조종 해제",Color.green,2);
            }
            else if(timeout)
            {
                CombatMetricsManager.Event("마리오네트 인형의 실 시간 초과 · 조종 해제");
                SetMechanicAlert("배역 지정 실패 · 조종 해제와 회복",Color.red,3);
                foreach(var member in GetLivingPartyMemberTransforms())DamagePartyMember(member,MarTuning.rescueFailureDamage,
                    CombatDamageKind.Area,"MarTuning.rescue.failure");
            }
            else CombatMetricsManager.Event("마리오네트 배역 지정 중단 · 대상 이탈");
            if(StageManager.CombatSuspended || currentHp<=0)return;
            if(MarionetteHard && MarionetteHardRules.RecoilDue(rescued,true))
            {BeginMarionetteHardRecoil(target);return;}
            if(MarionetteHard && _marHardClimaxPreparing)
            {BeginMarionetteHardRegroup();return;}
            EndMarionette(false,0,MarTuning.recoverySeconds);
        }
        private void BeginMarionetteClimax()
        {
            _marClimaxUsed=true;_marHardClimaxPreparing=false;
            StopMarionetteHardPulse();StopMarionettePlay();
            ShowMarionetteFirewall();
            EndMarionette();
        }
        public void AddMarionetteStagger(float points)
        {
            if(!MarionetteEncounter || RaidCombatAuthority.Replica || StageManager.CombatSuspended || currentHp<=0 ||
                _marStep!=MarionetteStep.Climax)return;
            if(MarionetteNormalRules.ApplyStagger(ref _marStagger,points,MarTuning.climaxStagger))FinishMarionetteClimax(true);
        }
        private void FinishMarionetteClimax(bool success)
        {
            if(_marStep!=MarionetteStep.Climax)return;
            if(MarionetteHard)StopMarionetteHardPulse();
            StopMarionettePlay();
            if(success)
            {
                ShowMarionetteFirewall();
                CombatMetricsManager.Event("클라이맥스 무력화 성공 · 시스템 개입: 오염된 시각 데이터 차단");
                SetMechanicAlert("시스템 개입: 오염된 시각 데이터 차단",new Color(.65f,.75f,1),3);
                PlayMarionetteCue(MarTuning.firewallCue);
                EndMarionette(true,MarTuning.climaxGroggySeconds,MarTuning.climaxRecoverySeconds);
            }
            else
            {
                CombatMetricsManager.Event("클라이맥스 무력화 실패 · 불완전한 무대 충격");
                SetMechanicAlert("클라이맥스 상연 실패 · 파티 충격과 회복",Color.red,3);
                foreach(var member in GetLivingPartyMemberTransforms())DamagePartyMember(member,MarTuning.climaxFailureDamage,
                    CombatDamageKind.Area,"MarTuning.climax.failure");
                if(!StageManager.CombatSuspended && currentHp>0)EndMarionette(false,0,MarTuning.climaxRecoverySeconds);
            }
        }
        private void StartMarionettePlay()
        {
            StopMarionettePlay();
            _marPlayBeat=-1;
            if(MarTuning.rearPuppetStagePrefab!=null)
            {
                _marPuppetStage=Instantiate(MarTuning.rearPuppetStagePrefab,new Vector3(0,0,MarTuning.stageHalfSize+5),Quaternion.identity);
                _marPuppetStage.name="Rear puppet play · visual only";
                foreach(var collider in _marPuppetStage.GetComponentsInChildren<Collider>())collider.enabled=false;
                foreach(var camera in _marPuppetStage.GetComponentsInChildren<Camera>())camera.enabled=false;
                foreach(var director in _marPuppetStage.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>())
                { if(MarionettePlayIsLocal(director)){director.time=0;director.Play();} }
            }
            else
            {
                _marPuppetStage=RaidPrimitive.Create(PrimitiveType.Cube);
                _marPuppetStage.name="Temporary rear puppet play · authored prefab missing";
                _marPuppetStage.transform.position=new Vector3(0,2,MarTuning.stageHalfSize+5);
                _marPuppetStage.transform.localScale=new Vector3(8,4,.3f);
                _marPuppetStage.GetComponent<Collider>().enabled=false;
            }
            _marPlayCaption=new GameObject("Rear play story caption · non target").AddComponent<TextMesh>();
            _marPlayCaption.transform.SetParent(_marPuppetStage.transform,false);
            _marPlayCaption.transform.position=new Vector3(0,4,MarTuning.stageHalfSize+2);
            _marPlayCaption.transform.localScale=Vector3.one*.17f;
            _marPlayCaption.anchor=TextAnchor.MiddleCenter;_marPlayCaption.alignment=TextAlignment.Center;
            _marPlayCaption.fontSize=36;_marPlayCaption.color=new Color(.9f,.78f,.44f);
            TickMarionettePlay();
        }
        private bool MarionettePlayIsLocal(UnityEngine.Playables.PlayableDirector director)
        {
            if(director.playableAsset==null)return false;
            foreach(var output in director.playableAsset.outputs)
            {
                var bound=director.GetGenericBinding(output.sourceObject);
                var component=bound as Component;
                var go=bound as GameObject;
                var root=component!=null?component.transform:go!=null?go.transform:null;
                if(root!=null && !root.IsChildOf(_marPuppetStage.transform))return false;
            }
            return true;
        }
        private void TickMarionettePlay()
        {
            if(_marPlayCaption==null)return;
            float elapsed=Mathf.Max(0,MarionetteClimaxSeconds-_marRemaining);
            int beat=elapsed<MarionetteClimaxSeconds/3?0:elapsed<MarionetteClimaxSeconds*2/3?1:2;
            if(beat!=_marPlayBeat)
            {
                _marPlayBeat=beat;
                _marPlayCaption.text=beat==0?"과거 인형극 I · 검은 태양의 신이 12인 원정대를 몰살":
                    beat==1?"과거 인형극 II · 단장이 심장을 시계바늘에 박다":
                    "과거 인형극 III · 시간이 멈추다";
            }
            if(Camera.main!=null)_marPlayCaption.transform.rotation=Camera.main.transform.rotation;
        }
        private void StopMarionettePlay()
        {
            if(_marPuppetStage==null)return;
            foreach(var director in _marPuppetStage.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>())director.Stop();
            Destroy(_marPuppetStage);_marPuppetStage=null;_marPlayCaption=null;_marPlayBeat=-1;
        }
        private void ShowMarionetteFirewall()
        {
            if(_marFirewallShown)return;_marFirewallShown=true;
            _marFirewall=MarTuning.firewallPrefab!=null?
                Instantiate(MarTuning.firewallPrefab,new Vector3(0,2,MarTuning.stageHalfSize+3),Quaternion.identity):
                RaidPrimitive.Create(PrimitiveType.Cube);
            _marFirewall.name="Administrator firewall · rear stage visual only";
            if(MarTuning.firewallPrefab==null)
            { _marFirewall.transform.position=new Vector3(0,2,MarTuning.stageHalfSize+3);_marFirewall.transform.localScale=new Vector3(9,4,.3f); }
            foreach(var collider in _marFirewall.GetComponentsInChildren<Collider>())collider.enabled=false;
        }
        private void ClearMarionetteFirewall()
        {if(_marFirewall!=null)Destroy(_marFirewall);_marFirewall=null;_marFirewallShown=false;}
        private void UpdateMarionetteCastUI()
        {
            if(!MarionetteEncounter)return;
            if(bossPhaseText!=null)bossPhaseText.text=MarionetteHard?
                (_marHardTransformed?"실에 매달린 비극 · 마리오네트 · 하드":"버려진 오페라 극장 · 마리오네트 · 하드"):
                "버려진 오페라 극장 · 마리오네트 · 노멀";
            if(_marStep==MarionetteStep.Climax)
            {
                if(bossCastFillImage!=null){bossCastFillImage.fillAmount=Mathf.Clamp01(_marStagger/Mathf.Max(1,MarTuning.climaxStagger));bossCastFillImage.color=new Color(.7f,.2f,1);}
                if(bossCastText!=null)bossCastText.text="클라이맥스 무력화 "+_marStagger.ToString("0")+"/"+
                    MarTuning.climaxStagger.ToString("0")+" · "+_marRemaining.ToString("0.0")+"초";
            }
            else if(_marStep==MarionetteStep.Control && bossCastText!=null)
                bossCastText.text="배역 지정: 배신자 · 인형의 실 절단 "+_marRemaining.ToString("0.0")+"초";
        }
        private void UpdateMarionetteControlLabel()
        {
            if(_marStep!=MarionetteStep.Control || _marTarget==null)
            {if(_marControlledLabel!=null)Destroy(_marControlledLabel.gameObject);_marControlledLabel=null;return;}
            if(_marControlledLabel==null)_marControlledLabel=FloraLabel("✂ 조종당함 · 등 뒤의 황금 실을 끊어주세요",new Color(1,.82f,.3f));
            _marControlledLabel.transform.position=_marTarget.position+Vector3.up*3.2f;
            if(Camera.main!=null)_marControlledLabel.transform.rotation=Camera.main.transform.rotation;
        }
        private void PlayMarionetteCue(AudioClip clip)
        {if(clip==null)return;if(_marAudio==null)_marAudio=GetComponent<AudioSource>()??gameObject.AddComponent<AudioSource>();_marAudio.PlayOneShot(clip);}
        private void PlayMarionetteTrigger(MarionettePattern pattern)
        {
            var visual=_marHardTransformed&&_marHardModel!=null?_marHardModel:_marModel;
            var animator=visual!=null?visual.GetComponentInChildren<Animator>():null;
            if(animator==null){ShowTemporaryMarionettePose(pattern);return;}
            string trigger=pattern==MarionettePattern.LeadExecution?MarTuning.tankTrigger:
                pattern==MarionettePattern.HalfCurtain?MarTuning.halfTrigger:
                pattern==MarionettePattern.TragicCenter?MarTuning.centerTrigger:
                pattern==MarionettePattern.TrackingLight?MarTuning.lightTrigger:
                pattern==MarionettePattern.AudienceApplause?MarTuning.stackTrigger:
                pattern==MarionettePattern.StageCharge?MarTuning.chargeTrigger:
                pattern==MarionettePattern.TraitorRole?MarTuning.controlTrigger:MarTuning.climaxTrigger;
            if(string.IsNullOrEmpty(trigger)){ShowTemporaryMarionettePose(pattern);return;}
            foreach(var parameter in animator.parameters)if(parameter.name==trigger && parameter.type==AnimatorControllerParameterType.Trigger)
            {animator.SetTrigger(trigger);return;}
            ShowTemporaryMarionettePose(pattern);
        }
        private void ShowTemporaryMarionettePose(MarionettePattern pattern)
        {
            if(_marPose!=null)Destroy(_marPose);
            _marPose=RaidPrimitive.Create(PrimitiveType.Capsule);
            _marPose.name="Temporary puppeteer preparation · replace Animator trigger";
            _marPose.transform.SetParent(transform,false);
            _marPose.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();
            Color color=pattern==MarionettePattern.Climax?new Color(.65f,.2f,.8f):
                pattern==MarionettePattern.TraitorRole?new Color(1,.72f,.2f):new Color(.78f,.25f,.35f);
            block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            _marPose.GetComponent<Renderer>().SetPropertyBlock(block);
            TickMarionettePose();
        }
        private void TickMarionettePose()
        {
            if(_marPose==null)return;
            float t=1-Mathf.Clamp01(_marRemaining/Mathf.Max(.01f,_castTotalDuration));
            if(_marPattern==MarionettePattern.HalfCurtain)
            {
                _marPose.transform.localPosition=new Vector3(_marRight?2:-2,2+t*.8f,1);
                _marPose.transform.localRotation=Quaternion.Euler(0,0,_marRight?-35:35);
                _marPose.transform.localScale=new Vector3(.25f,2.4f,.25f);
            }
            else if(_marPattern==MarionettePattern.TragicCenter)
            {
                _marPose.transform.localPosition=new Vector3(0,.3f,0);
                _marPose.transform.localScale=new Vector3(1+t*2,.15f,1+t*2);
            }
            else
            {
                _marPose.transform.localPosition=new Vector3(0,2.8f-t*.6f,1.2f);
                _marPose.transform.localRotation=Quaternion.Euler(25+t*35,0,0);
                _marPose.transform.localScale=new Vector3(.3f,2.5f,.3f);
            }
        }
        private void ApplyMarionetteReplica(RaidCombatEnemy state)
        {
            bool wasClimax=_marStep==MarionetteStep.Climax;
            var previousPattern=_marPattern;var previousStep=_marStep;
            _marPattern=(MarionettePattern)state.marPattern;_marStep=(MarionetteStep)state.marStep;
            _marTarget=RaidCombatAuthority.Resolve(state.marTarget);
            _marStagger=state.marStagger;_marRemaining=state.castRemaining;
            _marClimaxUsed=state.marClimaxUsed;_marStoryUsed=state.marStoryUsed;
            _marCounterOpen=state.marCounterOpen;
            _marRight=state.marRight;
            if((_marPattern!=previousPattern || _marStep!=previousStep) &&
                (_marStep==MarionetteStep.Cast || _marStep==MarionetteStep.Control || _marStep==MarionetteStep.Climax))
                PlayMarionetteTrigger(_marPattern);
            if(_marStep==MarionetteStep.Rest || _marStep==MarionetteStep.Groggy)
            {if(_marPose!=null)Destroy(_marPose);_marPose=null;}
            TickMarionettePose();
            UpdateMarionetteControlLabel();
            if(_marStep==MarionetteStep.Climax && !wasClimax)StartMarionettePlay();
            if(_marStep==MarionetteStep.Climax)TickMarionettePlay();
            if(_marStep!=MarionetteStep.Climax && wasClimax)StopMarionettePlay();
            if(state.marFirewall)ShowMarionetteFirewall();else ClearMarionetteFirewall();
            if(MarionetteHard)ApplyMarionetteHardReplica(state,previousStep);
        }
        public bool MarionetteDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;
            if(MarionetteHard && MarionetteHardDestination(actor,role,out destination))return true;
            if(!MarionetteEncounter || _marStep!=MarionetteStep.Cast)return false;
            if(_marPattern==MarionettePattern.AudienceApplause && _marTarget!=null)
            {destination=_marTarget.position+new Vector3((role-2.5f)*.45f,0,.5f);destination.y=actor.position.y;return true;}
            if(_marPattern==MarionettePattern.TrackingLight)
            {
                Vector3 corner=new Vector3(role==1||role==3?-9:9,actor.position.y,role<=2?9:-9);
                if(!_marLightLocked){destination=corner;return true;}
                Vector3 away=actor.position-corner;away.y=0;
                if(away.sqrMagnitude<.01f)away=role<=2?Vector3.back:Vector3.forward;
                destination=MarionetteNormalRules.ClampToStage(corner+away.normalized*5,MarTuning.stageHalfSize);
                destination.y=actor.position.y;return true;
            }
            if(_marPattern==MarionettePattern.TragicCenter)
            {destination=_marOrigin+(actor.position-_marOrigin).normalized*(MarTuning.centerRadius+2);destination=MarionetteNormalRules.ClampToStage(destination,MarTuning.stageHalfSize);destination.y=actor.position.y;return true;}
            if(_marPattern==MarionettePattern.HalfCurtain)
            {destination=_marOrigin+Vector3.Cross(Vector3.up,_marForward)*(_marRight?-8:8);destination=MarionetteNormalRules.ClampToStage(destination,MarTuning.stageHalfSize);destination.y=actor.position.y;return true;}
            if(_marPattern==MarionettePattern.StageCharge && role>=3&&PartyAiTactics.CanTakeCounterPosition(actor))
            {destination=_marOrigin+_marForward*(role==3?2.3f:4);destination=MarionetteNormalRules.ClampToStage(destination,MarTuning.stageHalfSize);destination.y=actor.position.y;return true;}
            return false;
        }
    }
}
