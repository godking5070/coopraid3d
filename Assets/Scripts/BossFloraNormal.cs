using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        [Header("Stage 2 normal: Flora Gear (initial tuning)")]
        public FloraNormalSettings flora = new FloraNormalSettings();
        [Header("Stage 2 hard: Flora Gear (initial tuning)")]
        public FloraHardSettings floraHard = new FloraHardSettings();
        public bool FloraEncounter => currentStageNumber == 2;
        public bool FloraHard => FloraEncounter && CampaignRuntime.Instance?.DifficultyId == CampaignCatalog.Hard;
        public bool FloraNormal => FloraEncounter && !FloraHard;
        public bool FloraHardTransformationActive => FloraHard && (_floraStep==FloraStep.Transformation || _floraReplicaTransforming);
        private FloraNormalSettings FloraTuning => FloraHard ? floraHard.combat : flora;
        public FloraPattern FloraPatternNow => _floraPattern;
        public FloraStep FloraStepNow => _floraStep;
        private FloraPattern _floraPattern;
        private FloraStep _floraStep;
        private float _floraRemaining, _floraRest, _floraSeedElapsed;
        private bool _floraGeneralStagger;
        private bool FloraRequiredStaggerPending => FloraHard?
            FloraHardRules.Due(_floraTransformed,_floraCoreDone,_floraVinesIntroduced,_floraGardenDone,_floraFinalDone,
                HpRatio,floraHard.transformationThreshold,FloraTuning.gardenThreshold,floraHard.finalGardenThreshold)!=FloraHardSpecial.None:
            FloraNormalRules.GardenDue(_floraVinesIntroduced,_floraGardenDone,HpRatio,FloraTuning.gardenThreshold);
        public bool FloraStaggerLocked => !FloraEncounter || currentHp<=0 || StageManager.CombatSuspended ||
            _floraStep==FloraStep.Groggy || FloraRequiredStaggerPending || _floraFinalActive ||
            _floraStep==FloraStep.Transformation || _floraStep==FloraStep.RootCores || _floraStep==FloraStep.Reposition ||
            _floraStep!=FloraStep.Rest && (_floraPattern==FloraPattern.ParasiticVines || _floraPattern==FloraPattern.EternalGarden);
        public bool TryFloraStaggerGroggy(float seconds)
        {
            if(RaidCombatAuthority.Replica||FloraStaggerLocked||float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return false;
            EndFlora(true);
            _floraRemaining=_groggyRemainingTime=seconds;
            _floraGeneralStagger=true;
            _currentCastSkillName="무력화 성공";
            SetMechanicAlert(_currentCastSkillName,Color.yellow,seconds);
            return true;
        }
        private int _floraNext = 1;
        private bool _floraGardenDone, _floraVinesIntroduced, _floraStoryDone, _floraCounterOpen;
        private Transform _floraTarget;
        private Vector3 _floraTargetPoint, _floraLockedOrigin, _floraLockedForward;
        private bool _floraRight;
        private bool _floraPetalLocked;
        private AttachedPartyMechanicMarker _floraMarker;
        private TextMesh _floraCounterIcon;
        private readonly List<AoETelegraph> _floraGround = new List<AoETelegraph>();
        private readonly List<AoETelegraph> _floraCrossGuide = new List<AoETelegraph>();
        private readonly List<TextMesh> _floraCrossOrder = new List<TextMesh>();
        private readonly List<FloraVinePair> _floraPairs = new List<FloraVinePair>();
        private readonly bool[] _floraSeedOpen = new bool[2];
        private readonly GameObject[] _floraSeedVisual = new GameObject[2];
        private readonly GameObject[] _floraSeedProjectile = new GameObject[2];
        private readonly Vector3[] _floraSeedPoint = new Vector3[2];
        private GameObject _floraModel;
        private Renderer[] _floraHiddenBody;
        private AudioSource _floraAudio;
        private float _floraBaseAutoAttackInterval;
        private bool _floraAutoAttackCaptured;
        private bool _floraTransformed, _floraCoreDone, _floraFinalDone, _floraFinalActive, _floraHardWarningShown, _floraCoreRecoveryPending, _floraReplicaTransforming, _floraLocalSkipped, _floraPresentationUnavailable;
        private int _floraHardCycle, _floraHardFollowup, _floraHardIntroduced;
        private float _floraThornRemaining;
        private Vector3 _floraThornPoint;
        private RaidStageCutscenePresentation _floraTransformationInstance;
        private GameObject _floraTransformedModel;
        private readonly List<BossAddMinion> _floraCores = new List<BossAddMinion>();

        private sealed class FloraVinePair
        {
            public Transform first, second;
            public LineRenderer line;
            public TextMesh firstLabel, secondLabel;
            public bool resolved;
            public int id;
            public Vector3 firstGoal, secondGoal;
        }

        private void ResetFlora()
        {
            ClearFlora();
            flora = flora ?? new FloraNormalSettings();
            floraHard = floraHard ?? new FloraHardSettings();
            floraHard.combat = floraHard.combat ?? new FloraNormalSettings();
            if(!_floraAutoAttackCaptured){_floraBaseAutoAttackInterval=autoAttackInterval;_floraAutoAttackCaptured=true;}
            autoAttackInterval=_floraBaseAutoAttackInterval;
            _floraNext = 1; _floraGardenDone = _floraVinesIntroduced = _floraStoryDone = false;
            _floraHardCycle = _floraHardFollowup = _floraHardIntroduced = 0;
            _floraTransformed = _floraCoreDone = _floraFinalDone = _floraFinalActive = _floraCoreRecoveryPending = _floraReplicaTransforming = _floraLocalSkipped = _floraPresentationUnavailable = false;
            _floraRest = FloraHard ? FloraHardTempo(FloraTuning.patternInterval) : FloraTuning.patternInterval;
            if (!FloraEncounter) return;
            if(FloraHard)autoAttackInterval=FloraHardTempo(_floraBaseAutoAttackInterval);
            currentBossTitle = "제2외곽 수호자 : 플로라 기어";
            maxHp = RaidBossHealthTuning.Resolve(FloraHard && floraHard.healthOverride > 0 ? floraHard.healthOverride : flora.health, FloraHard); currentHp = maxHp;
            arenaBoundaryRadius = Mathf.Max(12,FloraTuning.arenaRadius);
            autoAttackDamageToTank = 100;
            _stagePhase1BodyColor = new Color(.24f,.49f,.43f);
            SetBossBodyColor(_stagePhase1BodyColor);
            var initialVisual = FloraTuning.bossVisualPrefab != null ? FloraTuning.bossVisualPrefab : flora.bossVisualPrefab;
            if (initialVisual != null)
            {
                _floraModel = Instantiate(initialVisual,transform);
                _floraModel.name = "Flora Gear authored visual";
                foreach (var collider in _floraModel.GetComponentsInChildren<Collider>()) collider.enabled = false;
                var body = GetBossBodyTransform();
                if (body != null) { _floraHiddenBody=body.GetComponentsInChildren<Renderer>(); foreach (var renderer in _floraHiddenBody) renderer.enabled = false; }
            }
            var animator = _floraModel != null ? _floraModel.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
            var initialController=FloraTuning.bossAnimatorController!=null?FloraTuning.bossAnimatorController:flora.bossAnimatorController;
            if (animator != null && initialController != null) animator.runtimeAnimatorController = initialController;
        }

        private void ClearFlora(bool keepModel=false)
        {
            StopFloraTransformation();
            ClearFloraCores();
            ClearFloraThorn();
            if(!keepModel && _floraTransformedModel!=null)Destroy(_floraTransformedModel);
            if(!keepModel)_floraTransformedModel=null;
            ClearFloraAttack();
            ClearFloraCrossGuides();
            GetComponent<CombatCounterTarget>()?.CloseWindow();
            foreach (var ground in _floraGround) if (ground != null) Destroy(ground.gameObject);
            _floraGround.Clear();
            foreach (var pair in _floraPairs) DestroyFloraPairVisual(pair);
            _floraPairs.Clear();
            for (int i=0;i<2;i++) { if (_floraSeedVisual[i]!=null) Destroy(_floraSeedVisual[i]); if(_floraSeedProjectile[i]!=null)Destroy(_floraSeedProjectile[i]); _floraSeedProjectile[i]=null; _floraSeedVisual[i]=null; _floraSeedOpen[i]=false; }
            if (!keepModel && _floraModel != null) Destroy(_floraModel);
            if (!keepModel) _floraModel = null;
            if (!keepModel && _floraHiddenBody != null)
            { foreach(var renderer in _floraHiddenBody)if(renderer!=null)renderer.enabled=true; _floraHiddenBody=null; }
            _floraStep = FloraStep.Rest; _floraPattern = FloraPattern.None;
            _floraGeneralStagger=false;
            _floraCounterOpen = false; _floraRemaining = _floraSeedElapsed = 0;
            _floraThornRemaining=0;_floraHardFollowup=0;_floraFinalActive=false;
            _floraReplicaTransforming=false;_floraLocalSkipped=false;_floraPresentationUnavailable=false;
            foreach (var member in PartyLife.OrderedMembers())
                if(member!=null) CombatStatusEffects.Ensure(member.gameObject).ClearFloraAttack();
            _currentCastSkillName = ""; _castRemainingTime = _castTotalDuration = 0;
        }

        private void TickFlora()
        {
            float dt = Time.deltaTime;
            if(_floraGeneralStagger && FloraRequiredStaggerPending){EndFlora();_floraRest=0;}
            TickFloraTemporaryPose();
            TickFloraThorn(dt);
            if (_floraStep == FloraStep.Rest)
            {
                fsmState = BossFSMState.IdleChasing;
                RotateTowardsAggroTargetXZ(); UpdateMeleePursuit(dt); UpdateAutoAttack();
                _floraRest -= dt;
                if (_floraRest <= 0)
                {
                    if(FloraHard && TryStartFloraHardSpecial()) { }
                    else if (FloraNormal && FloraNormalRules.GardenDue(_floraVinesIntroduced,_floraGardenDone,HpRatio,FloraTuning.gardenThreshold))
                    { _floraGardenDone = true; BeginFlora(FloraPattern.EternalGarden); }
                    else if(FloraHard) BeginNextFloraHardPattern();
                    else { BeginFlora((FloraPattern)_floraNext); _floraNext = _floraNext >= 7 ? 1 : _floraNext+1; }
                }
            }
            else if (_floraStep == FloraStep.Groggy)
            {
                _groggyRemainingTime = _floraRemaining = Mathf.Max(0,_floraRemaining-dt);
                if (_floraRemaining <= 0) EndFlora();
            }
            else
            {
                _floraRemaining = Mathf.Max(0,_floraRemaining-dt);
                _castRemainingTime = _floraRemaining;
                if (_floraStep == FloraStep.Cast)
                {
                    if (_floraPattern == FloraPattern.PruningCharge && !_floraCounterOpen && _floraRemaining <= (FloraHard?floraHard.counterWindow:2.3f))
                    {
                        _floraCounterOpen = true;
                        CombatCounterTarget.Ensure(transform).OpenWindow(_floraRemaining);
                        SetMechanicAlert("⚔ 금색 카운터! 정면에서 카운터 기술, 또는 직선 회피",new Color(1,.75f,.15f),_floraRemaining);
                        PlayFloraCue(FloraTuning.counterCue!=null?FloraTuning.counterCue:flora.counterCue);
                        _floraCounterIcon=FloraLabel("⚔ COUNTER",new Color(1,.78f,.15f));
                        _floraCounterIcon.transform.position=transform.position+Vector3.up*4.5f;
                    }
                    if (_floraPattern==FloraPattern.PreservedPetals && !_floraPetalLocked && _floraRemaining<=(FloraHard?floraHard.impactWarning:1f))
                    {
                        _floraPetalLocked=true;
                        _floraTargetPoint=_floraTarget!=null?_floraTarget.position:Vector3.zero;
                        if(_floraMarker!=null)Destroy(_floraMarker.gameObject);
                        _floraMarker=null;
                        AddFloraGround(AoETelegraphType.Circle_Radius6,_floraTargetPoint,Vector3.forward,_floraRemaining,0,FloraTuning.petalRadius);
                        SetMechanicAlert("꽃잎 위치 고정 · 붉은 원에서 이탈",Color.red,_floraRemaining);
                    }
                    if(FloraHard) TickFloraHardWarning();
                    if (_floraRemaining <= 0) ResolveFlora();
                }
                else if (_floraStep == FloraStep.Vine) TickFloraVines();
                else if (_floraStep == FloraStep.Garden) TickFloraGarden(dt);
                else if(FloraHard) TickFloraHardStep(dt);
            }
            if (!_floraStoryDone && HpRatio <= FloraTuning.storyThreshold && currentHp > 0)
            {
                _floraStoryDone = true;
                StartCoroutine(FloraStoryLines());
            }
            UpdateTopBossUI(); UpdateCastBanner();
        }

        private System.Collections.IEnumerator FloraStoryLines()
        {
            if (currentHp <= 0) yield break;
            FindFirstObjectByType<RaidLobbyUI>()?.Say("플로라 기어 · 이 꽃들이 시들면... 성소의 네 사람은 누가 기억해 주지...?");
            yield return new WaitForSeconds(3);
            if (currentHp > 0)
                FindFirstObjectByType<RaidLobbyUI>()?.Say("플로라 기어 · 제발... 온실의 동력을 끄지 마...");
        }

        private void BeginFlora(FloraPattern pattern)
        {
            ClearFloraAttack();
            _floraPattern = pattern; _floraStep = FloraStep.Cast; fsmState = BossFSMState.CastingSkill;
            _floraHardWarningShown=false;
            _floraRemaining = pattern == FloraPattern.ParasiticVines ? FloraTuning.castSeconds :
                pattern == FloraPattern.EternalGarden ? FloraTuning.gardenSeconds : FloraTuning.castSeconds;
            if(FloraHard && pattern!=FloraPattern.ParasiticVines && pattern!=FloraPattern.EternalGarden)
                _floraRemaining=FloraHardTempo(_floraRemaining);
            _castTotalDuration = _floraRemaining; _castRemainingTime = _floraRemaining;
            _floraLockedOrigin = transform.position; _floraLockedForward = transform.forward;
            _floraTarget = ResolveLivingAggroTarget();
            _floraRight = Random.value >= .5f;
            _floraPetalLocked=false;
            if(FloraHard && pattern!=FloraPattern.ParasiticVines && pattern!=FloraPattern.EternalGarden)
            { if(!PlayFloraHardAnimation(pattern,false))ShowFloraTemporaryPose(pattern);PlayFloraCue(floraHard.attackPrepCue); }
            switch (pattern)
            {
                case FloraPattern.Pruning: _currentCastSkillName="정원사의 전정"; SetMechanicAlert("탱커 표식 · 가위 강타! 방어와 회복",Color.red,_floraRemaining); break;
                case FloraPattern.ClockScissors:
                    _currentCastSkillName="시곗바늘 가위질";
                    Vector3 side=Vector3.Cross(Vector3.up,_floraLockedForward)*(_floraRight?1:-1);
                    if(!FloraHard)AddFloraGround(AoETelegraphType.FrontCone_120Deg,_floraLockedOrigin,side,_floraRemaining,0,FloraTuning.arenaRadius,180);
                    SetMechanicAlert(_floraRight?"오른쪽 가위 준비 · 왼쪽으로 회피":"왼쪽 가위 준비 · 오른쪽으로 회피",Color.red,_floraRemaining);
                    break;
                case FloraPattern.RisingRoots:
                    _currentCastSkillName="뿌리 솟구침";
                    if(!FloraHard)AddFloraGround(AoETelegraphType.Circle_Radius6,_floraLockedOrigin,_floraLockedForward,_floraRemaining,0,FloraTuning.rootRadius);
                    break;
                case FloraPattern.PreservedPetals:
                    _currentCastSkillName="박제의 꽃잎";
                    var living=GetLivingPartyMemberTransforms();
                    _floraTarget=living.Count>0?living[Random.Range(0,living.Count)]:null;
                    _floraTargetPoint=_floraTarget!=null?_floraTarget.position:Vector3.zero;
                    if(_floraTarget!=null)_floraMarker=AttachedPartyMechanicMarker.SpawnTargetMarker(_floraTarget,FloraTuning.petalRadius);
                    SetMechanicAlert("꽃잎 추적 표식 · 산개 후 고정 위치에서 이탈",Color.red,_floraRemaining);
                    break;
                case FloraPattern.MemorialBloom:
                    _currentCastSkillName="추모의 개화";
                    var party=GetLivingPartyMemberTransforms(); _floraTarget=party.Count>0?party[Random.Range(0,party.Count)]:null;
                    if(_floraTarget!=null)_floraMarker=AttachedPartyMechanicMarker.SpawnStackMarker(_floraTarget,FloraTuning.stackRadius);
                    SetMechanicAlert("집합 표식 · 대상에게 모여 피해 분담",new Color(1,.65f,.2f),_floraRemaining);
                    break;
                case FloraPattern.PruningCharge:
                    _currentCastSkillName="가지치기 돌진";
                    if(!FloraHard)AddFloraGround(AoETelegraphType.Rectangle,_floraLockedOrigin,_floraLockedForward,_floraRemaining,FloraTuning.chargeWidth,FloraTuning.chargeLength);
                    SetMechanicAlert("⚔ 가지치기 돌진 준비 · 금색 카운터 또는 직선 회피",new Color(1,.75f,.15f),_floraRemaining);
                    break;
                case FloraPattern.ParasiticVines:
                    _floraVinesIntroduced=true;
                    _currentCastSkillName="기생하는 태엽 덩굴";
                    BeginFloraVines(); break;
                case FloraPattern.EternalGarden:
                    _currentCastSkillName="영원한 박제 정원 (전멸기)";
                    BeginFloraGarden(); break;
                case FloraPattern.RootReflux:
                    _currentCastSkillName="뿌리 환류 · 외곽 → 중심";
                    SetMechanicAlert("뿌리 환류 I 외곽 · 안쪽 대기 후 밖으로 이동",Color.red,_floraRemaining);
                    break;
                case FloraPattern.CrossPruning:
                    _currentCastSkillName="교차 전정 · I → II";
                    SetMechanicAlert("교차 전정 I·II · 고정된 두 직선의 측면으로 회피",Color.red,_floraRemaining);
                    ShowFloraCrossGuides();
                    break;
            }
        }

        private void ResolveFlora()
        {
            if (currentHp <= 0) return;
            if(FloraHard)PlayFloraCue(floraHard.attackImpactCue);
            var living=GetLivingPartyMemberTransforms();
            foreach (var member in living)
            {
                bool hit=false; float damage=0;
                switch (_floraPattern)
                {
                    case FloraPattern.Pruning: hit=member==_floraTarget; damage=FloraTuning.tankDamage; break;
                    case FloraPattern.ClockScissors:
                        hit=FloraNormalRules.InHalf(member.position,_floraLockedOrigin,Vector3.Cross(Vector3.up,_floraLockedForward),FloraTuning.arenaRadius,_floraRight);
                        damage=FloraTuning.sweepDamage; break;
                    case FloraPattern.RisingRoots: hit=FloraNormalRules.InCircle(member.position,_floraLockedOrigin,FloraTuning.rootRadius); damage=FloraTuning.rootDamage; break;
                    case FloraPattern.PreservedPetals: hit=FloraNormalRules.InCircle(member.position,_floraTargetPoint,FloraTuning.petalRadius); damage=FloraTuning.petalDamage; break;
                    case FloraPattern.PruningCharge:
                        hit=FloraHard?FloraHardInLine(member.position,false):_floraGround.Count>0 && _floraGround[0]!=null && _floraGround[0].IsPointInsideXZ(member.position);
                        damage=FloraTuning.chargeDamage; break;
                    case FloraPattern.RootReflux: hit=FloraHardInDonut(member.position);damage=FloraTuning.rootDamage;break;
                    case FloraPattern.CrossPruning: hit=FloraHardInLine(member.position,false);damage=FloraTuning.sweepDamage;break;
                }
                if (hit) DamagePartyMember(member,damage,CombatDamageKind.Area,"flora."+_floraPattern);
            }
            if (_floraPattern==FloraPattern.MemorialBloom && _floraTarget!=null && CombatRange.LivingPartyMember(_floraTarget))
            {
                int count=0;
                foreach (var member in living) if (FloraNormalRules.InCircle(member.position,_floraTarget.position,FloraTuning.stackRadius)) count++;
                foreach (var member in living) if (FloraNormalRules.InCircle(member.position,_floraTarget.position,FloraTuning.stackRadius))
                    DamagePartyMember(member,FloraTuning.stackDamage/Mathf.Max(1,count),CombatDamageKind.Area,"flora.MemorialBloom");
            }
            if(_floraPattern==FloraPattern.PruningCharge)CombatMetricsManager.Event("플로라 기어 일반 카운터 미성공 · 가지치기 돌진");
            if(FloraHard && StartFloraHardFollowup())return;
            EndFlora();
        }

        private void EndFlora(bool groggy=false)
        {
            _floraGeneralStagger=false;
            NotifyDamageCastEnded();
            ClearFloraAttack();
            ClearFloraCrossGuides();
            _floraStep=groggy?FloraStep.Groggy:FloraStep.Rest;
            fsmState=groggy?BossFSMState.Groggy:BossFSMState.IdleChasing;
            _floraRemaining=groggy?AegisPaladinData.CounterGroggy:0;
            _groggyRemainingTime=_floraRemaining;
            float recovery=FloraHard && _floraPattern!=FloraPattern.Pruning && _floraPattern!=FloraPattern.MemorialBloom
                ? FloraHardTempo(FloraTuning.recoverySeconds):FloraTuning.recoverySeconds;
            _floraRest=recovery+(FloraHard?FloraHardTempo(FloraTuning.patternInterval):FloraTuning.patternInterval);
            if(FloraHard && (_floraPattern==FloraPattern.EternalGarden || _floraCoreRecoveryPending))
                _floraRest=Mathf.Max(_floraRest,floraHard.specialRecoverySeconds);
            _floraCoreRecoveryPending=false;
            if(FloraHard && _floraFinalActive && _floraStep==FloraStep.Rest)
            { BeginFloraFinalReposition();return; }
            _currentCastSkillName=""; _castRemainingTime=_castTotalDuration=0;
        }

        private void ClearFloraAttack()
        {
            ClearFloraTemporaryPose();
            GetComponent<CombatCounterTarget>()?.CloseWindow(); _floraCounterOpen=false;
            if(_floraCounterIcon!=null)Destroy(_floraCounterIcon.gameObject);_floraCounterIcon=null;
            if(_floraMarker!=null)Destroy(_floraMarker.gameObject);_floraMarker=null;
            foreach (var ground in _floraGround) if (ground!=null) Destroy(ground.gameObject);
            _floraGround.Clear();
            foreach (var pair in _floraPairs) DestroyFloraPairVisual(pair);
            _floraPairs.Clear();
            for(int i=0;i<2;i++) { if(_floraSeedVisual[i]!=null)Destroy(_floraSeedVisual[i]); if(_floraSeedProjectile[i]!=null)Destroy(_floraSeedProjectile[i]);_floraSeedProjectile[i]=null; _floraSeedVisual[i]=null;_floraSeedOpen[i]=false; }
        }

        private void AddFloraGround(AoETelegraphType type,Vector3 position,Vector3 direction,float duration,float inner,float outer,float cone=120)
        {
            var ground=AoETelegraph.Spawn(type,position,direction,duration,inner,outer,cone);
            _floraGround.Add(ground);
        }

        public void FloraCounterSucceeded(Transform attacker)
        {
            if (!FloraEncounter || _floraPattern!=FloraPattern.PruningCharge || _floraStep!=FloraStep.Cast || !_floraCounterOpen) return;
            CombatMetricsManager.Event("플로라 기어 일반 카운터 성공: "+attacker.name);
            SetMechanicAlert("⚔ 카운터 성공 · 가위 공격 중단",new Color(1,.8f,.25f),2);
            EndFlora(true);
        }
        public bool FloraAiCounterReady(Transform actor) => FloraEncounter && _floraPattern==FloraPattern.PruningCharge &&
            _floraStep==FloraStep.Cast && _floraCounterOpen && CombatRange.LivingPartyMember(actor);
        public bool FloraTankbusterImminent(Transform actor,float within) => FloraEncounter &&
            _floraPattern==FloraPattern.Pruning && (_floraStep==FloraStep.Cast || _floraStep==FloraStep.Followup) && _floraTarget==actor && _floraRemaining<=within;

        private void BeginFloraVines()
        {
            var living=GetLivingPartyMemberTransforms();
            // Stable slot order and disjoint pairs; an odd survivor is simply unpaired.
            for(int i=0;i<FloraNormalRules.PairCount(living.Count)*2;i+=2)
            {
                var a=living[i];var b=living[i+1];
                var pair=new FloraVinePair{first=a,second=b,id=i/2+1};
                Vector3 axis=a.position-b.position;axis.y=0;
                if(axis.sqrMagnitude<.01f)axis=i==0?Vector3.right:Vector3.forward;
                axis.Normalize();
                pair.firstGoal=CombatRange.ClampToArena(a.position+axis*FloraTuning.vineBreakDistance,FloraTuning.arenaRadius-1);
                pair.secondGoal=CombatRange.ClampToArena(b.position-axis*FloraTuning.vineBreakDistance,FloraTuning.arenaRadius-1);
                var go=new GameObject("Flora vine pair "+pair.id);pair.line=go.AddComponent<LineRenderer>();
                pair.line.positionCount=2;pair.line.startWidth=pair.line.endWidth=.12f;
                pair.line.material=AoETelegraph.CreateTransparentTelegraphMaterial(new Color(.25f,.85f,1,.9f));
                pair.firstLabel=FloraLabel("덩굴 "+pair.id,new Color(.35f,.9f,1));
                pair.secondLabel=FloraLabel("덩굴 "+pair.id,new Color(.35f,.9f,1));
                _floraPairs.Add(pair);
            }
            _floraStep=FloraStep.Vine;_floraRemaining=FloraTuning.vineSeconds;_castTotalDuration=_castRemainingTime=_floraRemaining;
            SetMechanicAlert("태엽 덩굴 · 같은 번호의 상대와 "+FloraTuning.vineBreakDistance.ToString("0.#")+"m 이상 멀어져 끊기",new Color(.2f,.9f,1),_floraRemaining);
        }
        private void TickFloraVines()
        {
            bool unresolved=false;
            foreach(var pair in _floraPairs)
            {
                if(pair.resolved)continue;
                bool firstAlive=CombatRange.LivingPartyMember(pair.first), secondAlive=CombatRange.LivingPartyMember(pair.second);
                if(!firstAlive || !secondAlive)
                { pair.resolved=true; DestroyFloraPairVisual(pair); CombatMetricsManager.Event("태엽 덩굴 "+pair.id+" 대상 사망 · 취소"); continue; }
                pair.line.SetPosition(0,pair.first.position+Vector3.up*1.4f);
                pair.line.SetPosition(1,pair.second.position+Vector3.up*1.4f);
                float distance=Vector2.Distance(new Vector2(pair.first.position.x,pair.first.position.z),new Vector2(pair.second.position.x,pair.second.position.z));
                pair.firstLabel.text=pair.secondLabel.text="덩굴 "+pair.id+" · "+distance.ToString("0.0")+"/"+FloraTuning.vineBreakDistance.ToString("0.0")+"m";
                pair.firstLabel.transform.position=pair.first.position+Vector3.up*2.8f;
                pair.secondLabel.transform.position=pair.second.position+Vector3.up*2.8f;
                if(Camera.main!=null){pair.firstLabel.transform.rotation=Camera.main.transform.rotation;pair.secondLabel.transform.rotation=Camera.main.transform.rotation;}
                FloraVineOutcome outcome=FloraNormalRules.ResolveVine(firstAlive,secondAlive,
                    FloraNormalRules.VineBroken(pair.first.position,pair.second.position,FloraTuning.vineBreakDistance),_floraRemaining<=0);
                if(outcome==FloraVineOutcome.Broken)
                { pair.resolved=true;DestroyFloraPairVisual(pair);PlayFloraCue(FloraTuning.vineBreakCue!=null?FloraTuning.vineBreakCue:flora.vineBreakCue);CombatMetricsManager.Event("태엽 덩굴 "+pair.id+" 절단 성공");SetMechanicAlert("덩굴 "+pair.id+" 절단 성공",Color.cyan,1.5f); }
                else if(outcome==FloraVineOutcome.TimedOut)
                {
                    pair.resolved=true;DestroyFloraPairVisual(pair);
                    CombatMetricsManager.Event("태엽 덩굴 "+pair.id+" 시간 초과");
                    DamagePartyMember(pair.first,FloraTuning.vineFailureDamage,CombatDamageKind.Area,"flora.vine.timeout");
                    DamagePartyMember(pair.second,FloraTuning.vineFailureDamage,CombatDamageKind.Area,"flora.vine.timeout");
                }
                else unresolved=true;
            }
            if(!unresolved || _floraRemaining<=0)EndFlora();
        }

        private void BeginFloraGarden()
        {
            _floraStep=FloraStep.Garden;_floraSeedElapsed=0;
            _floraSeedPoint[0]=ClampFloraSeed(FloraTuning.seedOne);_floraSeedPoint[1]=ClampFloraSeed(FloraTuning.seedTwo);
            if(Vector3.Distance(_floraSeedPoint[0],_floraSeedPoint[1])<4)
                _floraSeedPoint[1]=-_floraSeedPoint[0];
            RaidApproachField.Instance?.PlayFloraIrisThrow(FloraTuning.irisThrowTrigger);
            for(int i=0;i<2;i++)
            {
                var projectile=RaidPrimitive.Create(PrimitiveType.Sphere);
                projectile.name="Iris thrown purification seed "+(i+1);
                projectile.transform.localScale=Vector3.one*.55f;
                projectile.transform.position=new Vector3(19,4.2f,2);
                projectile.GetComponent<Collider>().enabled=false;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(1,.82f,.15f));block.SetColor("_Color",new Color(1,.82f,.15f));projectile.GetComponent<Renderer>().SetPropertyBlock(block);
                _floraSeedProjectile[i]=projectile;
            }
            SetMechanicAlert("이리스: 제가 던진 황금 씨앗을 밟으세요! 가시를 태워버릴게요!",new Color(1,.82f,.2f),_floraRemaining);
            AddFloraGround(AoETelegraphType.Circle_Radius6,Vector3.zero,Vector3.forward,_floraRemaining,0,FloraTuning.arenaRadius);
            _floraGround[_floraGround.Count-1].SetThornStyle();
        }
        private Vector3 ClampFloraSeed(Vector3 point)
        {
            var planar=new Vector3(point.x,0,point.z);
            if(planar.magnitude>FloraTuning.arenaRadius-FloraTuning.safeRadius-1)planar=planar.normalized*(FloraTuning.arenaRadius-FloraTuning.safeRadius-1);
            if(planar.magnitude<5)planar=planar.sqrMagnitude<.01f?Vector3.right*6:planar.normalized*6;
            return planar;
        }
        private void TickFloraGarden(float dt)
        {
            _floraSeedElapsed+=dt;
            if(_floraSeedElapsed<FloraTuning.seedArrivalSeconds)
            {
                float t=Mathf.Clamp01(_floraSeedElapsed/Mathf.Max(.01f,FloraTuning.seedArrivalSeconds));
                for(int i=0;i<2;i++)if(_floraSeedProjectile[i]!=null)
                    _floraSeedProjectile[i].transform.position=Vector3.Lerp(new Vector3(19,4.2f,2),_floraSeedPoint[i]+Vector3.up*.6f,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*3;
            }
            if(_floraSeedElapsed>=FloraTuning.seedArrivalSeconds)
            {
                for(int i=0;i<2;i++)
                {
                    if(_floraSeedProjectile[i]!=null){Destroy(_floraSeedProjectile[i]);_floraSeedProjectile[i]=null;}
                    if(_floraSeedVisual[i]==null && !_floraSeedOpen[i])
                    {
                        var seed=RaidPrimitive.Create(PrimitiveType.Sphere);
                        seed.name="정화의 씨앗 "+(i+1);seed.transform.position=_floraSeedPoint[i]+Vector3.up*.6f;
                        seed.transform.localScale=Vector3.one*.8f;seed.GetComponent<Collider>().enabled=false;
                        var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(1,.8f,.1f));block.SetColor("_Color",new Color(1,.8f,.1f));seed.GetComponent<Renderer>().SetPropertyBlock(block);
                        var icon=FloraLabel("✿ 정화 씨앗",new Color(1,.94f,.55f));
                        icon.transform.SetParent(seed.transform,false);icon.transform.localPosition=Vector3.up*1.3f;
                        _floraSeedVisual[i]=seed;PlayFloraCue(FloraTuning.seedCue!=null?FloraTuning.seedCue:flora.seedCue);
                    }
                    if(_floraSeedOpen[i])continue;
                    foreach(var member in GetLivingPartyMemberTransforms())
                        if(FloraNormalRules.InCircle(member.position,_floraSeedPoint[i],FloraTuning.seedTriggerRadius))
                        {
                            if(!FloraNormalRules.ActivateOnce(ref _floraSeedOpen[i]))break;
                            Destroy(_floraSeedVisual[i]);_floraSeedVisual[i]=null;
                            CombatMetricsManager.Event("정화의 씨앗 "+(i+1)+" 활성화: "+member.name);
                            var safe=AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6,_floraSeedPoint[i],Vector3.forward,_floraRemaining,0,FloraTuning.safeRadius);
                            safe.transform.position+=Vector3.up*.03f;
                            safe.SetGuideStyle(true);_floraGround.Add(safe);
                            SetMechanicAlert("정화 안전지대 "+(i+1)+" 활성화 · 안으로 이동",new Color(1,.85f,.2f),2);
                            break;
                        }
                }
            }
            if(_floraRemaining>0)return;
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                bool safe=FloraNormalRules.Safe(member.position,_floraSeedPoint[0],_floraSeedOpen[0],_floraSeedPoint[1],_floraSeedOpen[1],FloraTuning.safeRadius);
                if(!safe) { DamagePartyMember(member,FloraTuning.gardenDamage,CombatDamageKind.Area,"FloraTuning.garden.thorns"); continue; }
                CombatMetricsManager.Event("박제 정원 안전지대 보호: "+member.name);
                CombatStatusEffects.Ensure(member.gameObject).GrantFloraAttack(FloraTuning.gardenAttackBonus,FloraTuning.gardenBuffSeconds);
            }
            EndFlora();
        }
        private void PlayFloraCue(AudioClip clip)
        {
            if(clip==null)return;
            if(_floraAudio==null)_floraAudio=GetComponent<AudioSource>()??gameObject.AddComponent<AudioSource>();
            _floraAudio.PlayOneShot(clip);
        }
        private static TextMesh FloraLabel(string message,Color color)
        {
            var go=new GameObject(message);var label=go.AddComponent<TextMesh>();
            label.text=message;label.fontSize=40;label.anchor=TextAnchor.MiddleCenter;
            label.alignment=TextAlignment.Center;label.color=color;
            go.transform.localScale=Vector3.one*.09f;return label;
        }
        private static void DestroyFloraPairVisual(FloraVinePair pair)
        {
            if(pair.line!=null){if(pair.line.sharedMaterial!=null)Destroy(pair.line.sharedMaterial);Destroy(pair.line.gameObject);pair.line=null;}
            if(pair.firstLabel!=null){Destroy(pair.firstLabel.gameObject);pair.firstLabel=null;}
            if(pair.secondLabel!=null){Destroy(pair.secondLabel.gameObject);pair.secondLabel=null;}
        }
        public bool FloraDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;
            if(!FloraEncounter)return false;
            if(FloraHard && _floraStep==FloraStep.RootCores && role==1)
            {
                foreach(var core in _floraCores)if(core!=null&&!core.isDead)
                { destination=CombatRange.AttackPosition(actor,core.transform,Vector3.back,2.5f);destination.y=actor.position.y;return true; }
            }
            if(_floraStep==FloraStep.Vine)
            {
                foreach(var pair in _floraPairs)if(!pair.resolved)
                {
                    if(pair.first==actor){destination=pair.firstGoal;destination.y=actor.position.y;return true;}
                    if(pair.second==actor){destination=pair.secondGoal;destination.y=actor.position.y;return true;}
                }
            }
            if(_floraStep==FloraStep.Garden)
            {
                int slot=role<=2?0:1;
                if(!_floraSeedOpen[slot])destination=_floraSeedPoint[slot];
                else if(!_floraSeedOpen[1-slot] && role==4)destination=_floraSeedPoint[1-slot];
                else destination=_floraSeedPoint[slot];
                destination.y=actor.position.y;return true;
            }
            if(FloraHard && (_floraStep==FloraStep.Cast || _floraStep==FloraStep.Followup))
            {
                bool second=_floraStep==FloraStep.Followup;
                if(_floraPattern==FloraPattern.RisingRoots || _floraPattern==FloraPattern.RootReflux)
                {
                    bool donut=(_floraPattern==FloraPattern.RootReflux)!=second;
                    Vector3 radial=actor.position-_floraLockedOrigin;radial.y=0;
                    if(radial.sqrMagnitude<.01f)radial=Vector3.forward;
                    destination=_floraLockedOrigin+radial.normalized*(donut?Mathf.Max(1,FloraTuning.rootRadius-2):FloraTuning.rootRadius+2);
                    destination.y=actor.position.y;return true;
                }
                if(_floraPattern==FloraPattern.ClockScissors)
                { destination=_floraLockedOrigin+_floraLockedForward*7+Vector3.Cross(Vector3.up,_floraLockedForward)*((_floraRight!=second)?-1.8f:1.8f);destination.y=actor.position.y;return true; }
                if(_floraPattern==FloraPattern.CrossPruning)
                { destination=_floraLockedOrigin+_floraLockedForward*7;destination.y=actor.position.y;return true; }
            }
            if(_floraStep!=FloraStep.Cast)return false;
            if(_floraPattern==FloraPattern.MemorialBloom && _floraTarget!=null)
            { destination=_floraTarget.position+new Vector3((role-2.5f)*.5f,0,.5f);destination.y=actor.position.y;return true; }
            if(_floraPattern==FloraPattern.PreservedPetals && _floraTarget==actor)
            { destination=CombatRange.ClampToArena(actor.position+(actor.position-transform.position).normalized*5,FloraTuning.arenaRadius-1);destination.y=actor.position.y;return true; }
            if(_floraPattern==FloraPattern.RisingRoots)
            { destination=_floraLockedOrigin+(actor.position-_floraLockedOrigin).normalized*(FloraTuning.rootRadius+2);destination.y=actor.position.y;return true; }
            if(_floraPattern==FloraPattern.ClockScissors)
            { destination=_floraLockedOrigin+Vector3.Cross(Vector3.up,_floraLockedForward)*(_floraRight?-8:8);destination.y=actor.position.y;return true; }
            if(_floraPattern==FloraPattern.PruningCharge && role>=3&&PartyAiTactics.CanTakeCounterPosition(actor))
            { destination=_floraLockedOrigin+_floraLockedForward*4;destination.y=actor.position.y;return true; }
            return false;
        }
    }
}
