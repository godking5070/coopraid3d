using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RaidRogue
{
    public partial class BossController
    {
        private AoETelegraph _floraThornGround;
        private GameObject _floraTemporaryPose;
        private readonly List<Transform> _floraPoseParts=new List<Transform>();
        private float _floraThornTick;
        private float FloraHardTempo(float seconds) => FloraHardRules.Tempo(seconds,floraHard.attackTempo);

        private void BeginNextFloraHardPattern()
        {
            // Both new attacks are introduced alone before entering the rotation.
            FloraPattern pattern;
            if (_floraHardIntroduced < 2)
                pattern = _floraHardIntroduced++ == 0 ? FloraPattern.RootReflux : FloraPattern.CrossPruning;
            else
            {
                var cycle = new[] { FloraPattern.Pruning, FloraPattern.ClockScissors, FloraPattern.RisingRoots,
                    FloraPattern.PreservedPetals, FloraPattern.MemorialBloom, FloraPattern.PruningCharge,
                    FloraPattern.ParasiticVines, FloraPattern.RootReflux, FloraPattern.CrossPruning };
                pattern=cycle[_floraHardCycle++ % cycle.Length];
            }
            BeginFlora(pattern);
        }

        private bool TryStartFloraHardSpecial()
        {
            var due=FloraHardRules.Due(_floraTransformed,_floraCoreDone,_floraVinesIntroduced,_floraGardenDone,
                _floraFinalDone,HpRatio,floraHard.transformationThreshold,FloraTuning.gardenThreshold,floraHard.finalGardenThreshold);
            if(due==FloraHardSpecial.Transformation){BeginFloraTransformation();return true;}
            if(due==FloraHardSpecial.RootCores){BeginFloraRootCores();return true;}
            if(due==FloraHardSpecial.FirstGarden)
            { _floraGardenDone=true;BeginFlora(FloraPattern.EternalGarden);return true; }
            if(due==FloraHardSpecial.FinalGarden)
            {
                _floraFinalDone=true;_floraFinalActive=true;
                ClearFloraThorn();ClearFloraAttack();
                BeginFlora(FloraPattern.ParasiticVines);
                SetMechanicAlert("마지막 박제 정원 · 덩굴 절단 후 정화 씨앗",new Color(.7f,.5f,1),FloraTuning.vineSeconds);
                return true;
            }
            return false;
        }

        private void BeginFloraTransformation()
        {
            ClearFloraAttack();ClearFloraThorn();
            _floraStep=FloraStep.Transformation;fsmState=BossFSMState.CastingSkill;
            _floraRemaining=floraHard.transformationSeconds;
            var presentation=floraHard.transformationPresentation;
            if(presentation!=null && PlayFloraTransformationPresentation(0))
                _floraRemaining=RaidStageCutscenePresentation.Duration(presentation.DirectorAsset,floraHard.transformationSeconds);
            _castTotalDuration=_castRemainingTime=_floraRemaining;
            _currentCastSkillName="만개한 박제 정원사 · 변신";
            SetMechanicAlert("플로라 기어 변신 · 만개한 박제 정원사",new Color(.75f,.35f,.85f),_floraRemaining);
            PlayFloraCue(floraHard.transformationCue);
        }

        private bool PlayFloraTransformationPresentation(float elapsed)
        {
            var presentation=floraHard.transformationPresentation;
            if(presentation==null){_floraPresentationUnavailable=true;return false;}
            try
            {
                _floraTransformationInstance=Instantiate(presentation,transform.position,Quaternion.identity,transform);
                if(_floraTransformationInstance.Play(this,StageManager.Instance?.player,elapsed))return true;
            }
            catch(System.Exception e) { Debug.LogWarning("Flora hard transformation fallback: "+e); }
            StopFloraTransformation();_floraPresentationUnavailable=true;return false;
        }

        private void FinishFloraTransformation()
        {
            StopFloraTransformation();
            _floraReplicaTransforming=false;_floraLocalSkipped=false;_floraPresentationUnavailable=false;
            _floraTransformed=true;
            RestoreHardTransformationHealth();
            currentBossTitle="만개한 박제 정원사, 플로라 기어";
            if(_floraModel!=null)_floraModel.SetActive(false);
            else if(_floraHiddenBody==null)
            {
                var body=GetBossBodyTransform();
                if(body!=null){_floraHiddenBody=body.GetComponentsInChildren<Renderer>();foreach(var renderer in _floraHiddenBody)renderer.enabled=false;}
            }
            if(floraHard.transformedVisualPrefab!=null)
            {
                _floraTransformedModel=Instantiate(floraHard.transformedVisualPrefab,transform);
                foreach(var collider in _floraTransformedModel.GetComponentsInChildren<Collider>())collider.enabled=false;
                var animator=_floraTransformedModel.GetComponentInChildren<Animator>();
                if(animator!=null && floraHard.transformedAnimatorController!=null)
                    animator.runtimeAnimatorController=floraHard.transformedAnimatorController;
            }
            else BuildFloraTemporaryTransformedVisual();
            _floraStep=FloraStep.Rest;fsmState=BossFSMState.IdleChasing;
            _floraRest=0;_floraRemaining=0;_currentCastSkillName="";
            _castRemainingTime=_castTotalDuration=0;
        }

        private void ResetFloraReplicaTransformation()
        {
            StopFloraTransformation();
            if(_floraTransformedModel!=null)Destroy(_floraTransformedModel);
            _floraTransformedModel=null;_floraTransformed=false;
            if(_floraModel!=null)_floraModel.SetActive(true);
            if(_floraModel==null && _floraHiddenBody!=null)
            {foreach(var renderer in _floraHiddenBody)if(renderer!=null)renderer.enabled=true;_floraHiddenBody=null;}
            currentBossTitle="제2외곽 수호자 : 플로라 기어";
            SetBossBodyColor(_stagePhase1BodyColor);
        }

        private void BuildFloraTemporaryTransformedVisual()
        {
            _floraTransformedModel=new GameObject("Temporary bloomed Flora Gear · replace in floraHard");
            _floraTransformedModel.transform.SetParent(transform,false);
            CreateFloraTempPart(PrimitiveType.Sphere,"Open thorn core",new Vector3(0,2.2f,0),
                new Vector3(2.1f,2.8f,1.7f),Quaternion.identity,new Color(.53f,.12f,.42f));
            for(int i=0;i<6;i++)
            {
                float angle=i*Mathf.PI/3;
                var direction=new Vector3(Mathf.Cos(angle),.25f,Mathf.Sin(angle));
                CreateFloraTempPart(PrimitiveType.Capsule,"Clockwork thorn petal "+(i+1),
                    new Vector3(0,2.5f,0)+direction*1.4f,new Vector3(.55f,2.2f,.55f),
                    Quaternion.FromToRotation(Vector3.up,direction),new Color(.27f,.06f,.28f));
            }
            for(int i=0;i<4;i++)
            {
                float angle=i*Mathf.PI*.5f;
                var direction=new Vector3(Mathf.Cos(angle),-.35f,Mathf.Sin(angle));
                CreateFloraTempPart(PrimitiveType.Capsule,"Raised clockwork vine "+(i+1),
                    new Vector3(0,1.6f,0)+direction*1.4f,new Vector3(.35f,2.8f,.35f),
                    Quaternion.FromToRotation(Vector3.up,direction),new Color(.52f,.38f,.11f));
            }
        }
        private void CreateFloraTempPart(PrimitiveType type,string partName,Vector3 localPosition,Vector3 scale,Quaternion rotation,Color color)
        {
            var part=RaidPrimitive.Create(type);part.name=partName;
            part.transform.SetParent(_floraTransformedModel.transform,false);
            part.transform.localPosition=localPosition;part.transform.localRotation=rotation;part.transform.localScale=scale;
            part.GetComponent<Collider>().enabled=false;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
            part.GetComponent<Renderer>().SetPropertyBlock(block);
        }

        private void StopFloraTransformation()
        {
            if(_floraTransformationInstance==null)return;
            _floraTransformationInstance.StopAndRestore();
            _floraTransformationInstance.gameObject.SetActive(false);
            Destroy(_floraTransformationInstance.gameObject);
            _floraTransformationInstance=null;
        }

        private void TickFloraReplicaTransformationInput()
        {
            if(!_floraReplicaTransforming || _floraLocalSkipped || LocalGameplayInput.ModalCovered)return;
            if(!FloraSkipPressed())return;
            _floraLocalSkipped=true;StopFloraTransformation();
            LocalGameplayInput.SuppressThisFrame();
        }

        private void BeginFloraRootCores()
        {
            ClearFloraAttack();ClearFloraThorn();
            _floraStep=FloraStep.RootCores;fsmState=BossFSMState.CastingSkill;
            _floraRemaining=floraHard.rootCoreSeconds;
            _castTotalDuration=_castRemainingTime=_floraRemaining;
            _currentCastSkillName="오염핵 개화 · 뿌리핵 0/2";
            var spawner=GetComponent<BossAddSpawner>();
            if(spawner==null)spawner=gameObject.AddComponent<BossAddSpawner>();
            spawner.boss=this;
            Vector3[] points={floraHard.firstCore,floraHard.secondCore};
            for(int i=0;i<2;i++)
            {
                Vector3 point=CombatRange.ClampToArena(points[i],FloraTuning.arenaRadius-3);
                var core=new GameObject("Flora parasitic root core "+(i+1)).AddComponent<BossAddMinion>();
                core.InitializeAdd(AddMinionType.FloraRootCore,this,spawner,point,EncounterTemplate);
                _floraCores.Add(core);spawner.activeAdds.Add(core);
            }
            SetMechanicAlert("오염핵 개화 · 뿌리핵 두 개를 파괴!",new Color(.75f,.3f,.85f),_floraRemaining);
            PlayFloraCue(floraHard.coreCue);
        }

        public void NotifyFloraCoreDestroyed(BossAddMinion core)
        {
            if(!FloraHard || _floraStep!=FloraStep.RootCores || !_floraCores.Contains(core))return;
            int killed=0;foreach(var item in _floraCores)if(item==null||item.isDead)killed++;
            _currentCastSkillName="오염핵 개화 · 뿌리핵 "+killed+"/2";
            CombatMetricsManager.Event("오염핵 개화 뿌리핵 파괴 "+killed+"/2");
        }

        private void TickFloraRootCores()
        {
            int alive=0;foreach(var core in _floraCores)if(core!=null&&!core.isDead)alive++;
            if(alive>0 && _floraRemaining>0)return;
            bool success=alive==0;
            if(!success)
            {
                SetMechanicAlert("오염핵 개화 실패 · 광역 충격파",Color.red,3);
                foreach(var member in GetLivingPartyMemberTransforms())
                    DamagePartyMember(member,floraHard.rootCoreFailureDamage,CombatDamageKind.Area,"flora.hard.core.failure");
                CombatMetricsManager.Event("오염핵 개화 실패 · 충격파");
            }
            else CombatMetricsManager.Event("오염핵 개화 성공 · 그로기");
            if(StageManager.CombatSuspended || currentHp<=0)
            { ClearFloraCores();return; }
            ClearFloraCores();_floraCoreDone=true;
            _floraStep=success?FloraStep.Groggy:FloraStep.Rest;
            fsmState=success?BossFSMState.Groggy:BossFSMState.IdleChasing;
            _floraRemaining=success?floraHard.rootCoreGroggySeconds:0;
            _groggyRemainingTime=_floraRemaining;
            _floraRest=floraHard.specialRecoverySeconds;
            _floraCoreRecoveryPending=success;
            _castRemainingTime=_castTotalDuration=0;_currentCastSkillName="";
        }

        private void ClearFloraCores()
        {
            var spawner=GetComponent<BossAddSpawner>();
            foreach(var core in _floraCores)if(core!=null)
            {
                if(spawner!=null)spawner.activeAdds.Remove(core);
                core.gameObject.SetActive(false);Destroy(core.gameObject);
            }
            _floraCores.Clear();
        }

        private bool StartFloraHardFollowup()
        {
            if(_floraPattern==FloraPattern.PreservedPetals)
            {
                ClearFloraAttack();
                _floraThornPoint=_floraTargetPoint;_floraThornRemaining=floraHard.petalThornSeconds;
                _floraThornTick=.5f;
                _floraThornGround=AoETelegraph.Spawn(AoETelegraphType.Circle_Radius6,_floraThornPoint,Vector3.forward,
                    _floraThornRemaining,0,FloraTuning.petalRadius);
                _floraThornGround.SetThornStyle();
                return false;
            }
            float seconds=0;
            switch(_floraPattern)
            {
                case FloraPattern.Pruning:seconds=floraHard.tankFollowupSeconds;break;
                case FloraPattern.ClockScissors:seconds=floraHard.sweepFollowupSeconds;break;
                case FloraPattern.RisingRoots:case FloraPattern.RootReflux:seconds=floraHard.rootFollowupSeconds;break;
                case FloraPattern.CrossPruning:seconds=floraHard.crossFollowupSeconds;break;
            }
            if(seconds<=0)return false;
            ClearFloraAttack();
            _floraStep=FloraStep.Followup;_floraHardFollowup=1;_floraHardWarningShown=false;
            _floraRemaining=FloraHardTempo(seconds);
            _castTotalDuration=_castRemainingTime=_floraRemaining;
            _currentCastSkillName=_floraPattern==FloraPattern.Pruning?"정원사의 전정 · 2타":
                _floraPattern==FloraPattern.ClockScissors?"시곗바늘 가위질 · 반대쪽":
                _floraPattern==FloraPattern.CrossPruning?"교차 전정 · II":
                _floraPattern==FloraPattern.RootReflux?"뿌리 환류 · 중심":"뿌리 솟구침 · 외곽";
            SetMechanicAlert(_currentCastSkillName+" 준비",Color.red,_floraRemaining);
            if(!PlayFloraHardAnimation(_floraPattern,true))ShowFloraTemporaryPose(_floraPattern);
            PlayFloraCue(floraHard.followupCue);
            return true;
        }

        private void ResolveFloraHardFollowup()
        {
            PlayFloraCue(floraHard.attackImpactCue);
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                bool hit=false;float damage=0;
                switch(_floraPattern)
                {
                    case FloraPattern.Pruning:hit=member==_floraTarget;damage=FloraTuning.tankDamage;break;
                    case FloraPattern.ClockScissors:
                        hit=FloraNormalRules.InHalf(member.position,_floraLockedOrigin,Vector3.Cross(Vector3.up,_floraLockedForward),FloraTuning.arenaRadius,!_floraRight);
                        damage=FloraTuning.sweepDamage;break;
                    case FloraPattern.RisingRoots:hit=FloraHardInDonut(member.position);damage=FloraTuning.rootDamage;break;
                    case FloraPattern.RootReflux:hit=FloraNormalRules.InCircle(member.position,_floraLockedOrigin,FloraTuning.rootRadius);damage=FloraTuning.rootDamage;break;
                    case FloraPattern.CrossPruning:hit=FloraHardInLine(member.position,true);damage=FloraTuning.sweepDamage;break;
                }
                if(hit)DamagePartyMember(member,damage,CombatDamageKind.Area,"flora.hard.followup."+_floraPattern);
            }
            _floraHardFollowup=0;EndFlora();
        }

        private bool FloraHardInDonut(Vector3 point) =>
            FloraNormalRules.InCircle(point,_floraLockedOrigin,FloraTuning.arenaRadius) &&
            !FloraNormalRules.InCircle(point,_floraLockedOrigin,FloraTuning.rootRadius);

        private bool FloraHardInLine(Vector3 point,bool second)
        {
            Vector3 right=Vector3.Cross(Vector3.up,_floraLockedForward);
            Vector3 origin=_floraLockedOrigin+right*(_floraPattern==FloraPattern.CrossPruning?(second?4:-4):0);
            Vector3 offset=point-origin;
            return Mathf.Abs(Vector3.Dot(offset,right))<=FloraTuning.chargeWidth*.5f &&
                Vector3.Dot(offset,_floraLockedForward)>=0 && Vector3.Dot(offset,_floraLockedForward)<=FloraTuning.chargeLength;
        }

        private void TickFloraHardWarning()
        {
            if(_floraHardWarningShown || _floraRemaining>floraHard.impactWarning || _floraStep!=FloraStep.Cast && _floraStep!=FloraStep.Followup)return;
            _floraHardWarningShown=true;
            bool second=_floraStep==FloraStep.Followup;
            switch(_floraPattern)
            {
                case FloraPattern.ClockScissors:
                    Vector3 side=Vector3.Cross(Vector3.up,_floraLockedForward)*((_floraRight != second)?1:-1);
                    AddFloraGround(AoETelegraphType.FrontCone_120Deg,_floraLockedOrigin,side,_floraRemaining,0,FloraTuning.arenaRadius,180);break;
                case FloraPattern.RisingRoots:case FloraPattern.RootReflux:
                    bool donut=_floraPattern==FloraPattern.RootReflux != second;
                    AddFloraGround(donut?AoETelegraphType.Donut_Inner4_Outer12:AoETelegraphType.Circle_Radius6,
                        _floraLockedOrigin,_floraLockedForward,_floraRemaining,donut?FloraTuning.rootRadius:0,
                        donut?FloraTuning.arenaRadius:FloraTuning.rootRadius);break;
                case FloraPattern.PruningCharge:case FloraPattern.CrossPruning:
                    Vector3 origin=_floraLockedOrigin;
                    if(_floraPattern==FloraPattern.CrossPruning)
                        origin+=Vector3.Cross(Vector3.up,_floraLockedForward)*(second?4:-4);
                    AddFloraGround(AoETelegraphType.Rectangle,origin,_floraLockedForward,_floraRemaining,FloraTuning.chargeWidth,FloraTuning.chargeLength);break;
            }
        }

        private void TickFloraHardStep(float dt)
        {
            switch(_floraStep)
            {
                case FloraStep.Followup:
                    TickFloraHardWarning();
                    if(_floraRemaining<=0)ResolveFloraHardFollowup();break;
                case FloraStep.Transformation:
                    if(!LocalGameplayInput.ModalCovered && FloraSkipPressed())
                    { _floraRemaining=0;LocalGameplayInput.SuppressThisFrame(); }
                    if(_floraRemaining<=0)FinishFloraTransformation();break;
                case FloraStep.RootCores:TickFloraRootCores();break;
                case FloraStep.Reposition:
                    if(_floraRemaining<=0)
                    {
                        _floraFinalActive=false;
                        BeginFlora(FloraPattern.EternalGarden);
                    }
                    break;
            }
        }

        private void BeginFloraFinalReposition()
        {
            ClearFloraAttack();
            _floraStep=FloraStep.Reposition;fsmState=BossFSMState.CastingSkill;
            _floraRemaining=floraHard.finalRepositionSeconds;
            _castTotalDuration=_castRemainingTime=_floraRemaining;
            _currentCastSkillName="마지막 박제 정원 · 정렬";
            SetMechanicAlert("덩굴 종료 · 정화 씨앗을 준비하세요",new Color(1,.82f,.2f),_floraRemaining);
        }

        private void TickFloraThorn(float dt)
        {
            if(_floraThornRemaining<=0)return;
            _floraThornRemaining-=dt;_floraThornTick-=dt;
            if(_floraThornTick<=0)
            {
                _floraThornTick=.5f;
                foreach(var member in GetLivingPartyMemberTransforms())
                    if(FloraNormalRules.InCircle(member.position,_floraThornPoint,FloraTuning.petalRadius))
                        DamagePartyMember(member,floraHard.petalThornDamage,CombatDamageKind.Area,"flora.hard.petal.thorns");
            }
            if(_floraThornRemaining<=0)ClearFloraThorn();
        }
        private void ClearFloraThorn()
        {
            _floraThornRemaining=0;
            if(_floraThornGround!=null)Destroy(_floraThornGround.gameObject);
            _floraThornGround=null;
        }
        private void ShowFloraCrossGuides()
        {
            Vector3 right=Vector3.Cross(Vector3.up,_floraLockedForward);
            for(int i=0;i<2;i++)
            {
                Vector3 origin=_floraLockedOrigin+right*(i==0?-4:4);
                var guide=AoETelegraph.Spawn(AoETelegraphType.Rectangle,origin,_floraLockedForward,
                    _floraRemaining+FloraHardTempo(floraHard.crossFollowupSeconds)+.2f,FloraTuning.chargeWidth,FloraTuning.chargeLength);
                guide.SetGuideStyle(true);_floraCrossGuide.Add(guide);
                var label=FloraLabel(i==0?"I":"II",new Color(.95f,.9f,.65f));
                label.transform.position=origin+_floraLockedForward*7+Vector3.up*1.4f;
                _floraCrossOrder.Add(label);
            }
        }
        private void ClearFloraCrossGuides()
        {
            foreach(var guide in _floraCrossGuide)if(guide!=null)Destroy(guide.gameObject);
            _floraCrossGuide.Clear();
            foreach(var label in _floraCrossOrder)if(label!=null)Destroy(label.gameObject);
            _floraCrossOrder.Clear();
        }
        private bool PlayFloraHardAnimation(FloraPattern pattern,bool followup)
        {
            var root=_floraTransformedModel!=null?_floraTransformedModel:_floraModel;
            var animator=root!=null?root.GetComponentInChildren<Animator>():GetComponentInChildren<Animator>();
            if(animator==null)return false;
            string trigger=followup?floraHard.followupTrigger:
                pattern==FloraPattern.Pruning?floraHard.pruningTrigger:
                pattern==FloraPattern.ClockScissors?floraHard.scissorsTrigger:
                pattern==FloraPattern.RisingRoots?floraHard.rootsTrigger:
                pattern==FloraPattern.RootReflux?floraHard.refluxTrigger:
                pattern==FloraPattern.CrossPruning?floraHard.crossTrigger:null;
            if(string.IsNullOrEmpty(trigger))return false;
            foreach(var parameter in animator.parameters)
                if(parameter.name==trigger && parameter.type==AnimatorControllerParameterType.Trigger)
                { animator.SetTrigger(trigger);return true; }
            return false;
        }
        private void ShowFloraTemporaryPose(FloraPattern pattern)
        {
            if(pattern!=FloraPattern.Pruning && pattern!=FloraPattern.ClockScissors &&
                pattern!=FloraPattern.RisingRoots && pattern!=FloraPattern.RootReflux &&
                pattern!=FloraPattern.CrossPruning)return;
            _floraTemporaryPose=new GameObject("Temporary Flora preparation · replace Animator clips");
            _floraTemporaryPose.transform.SetParent(transform,false);
            int count=pattern==FloraPattern.RisingRoots||pattern==FloraPattern.RootReflux?8:2;
            for(int i=0;i<count;i++)
            {
                var part=RaidPrimitive.Create(pattern==FloraPattern.RisingRoots||pattern==FloraPattern.RootReflux
                    ?PrimitiveType.Capsule:PrimitiveType.Cube);
                part.name="Flora pose "+(i+1);part.transform.SetParent(_floraTemporaryPose.transform,false);
                part.GetComponent<Collider>().enabled=false;
                var block=new MaterialPropertyBlock();
                Color color=pattern==FloraPattern.RisingRoots||pattern==FloraPattern.RootReflux
                    ?new Color(.16f,.58f,.44f):new Color(.8f,.62f,.18f);
                block.SetColor("_BaseColor",color);block.SetColor("_Color",color);
                part.GetComponent<Renderer>().SetPropertyBlock(block);
                _floraPoseParts.Add(part.transform);
            }
            TickFloraTemporaryPose();
        }
        private void TickFloraTemporaryPose()
        {
            if(_floraTemporaryPose==null)return;
            float progress=1-Mathf.Clamp01(_floraRemaining/Mathf.Max(.01f,_castTotalDuration));
            bool roots=_floraPattern==FloraPattern.RisingRoots||_floraPattern==FloraPattern.RootReflux;
            for(int i=0;i<_floraPoseParts.Count;i++)
            {
                var part=_floraPoseParts[i];if(part==null)continue;
                if(roots)
                {
                    float angle=i*Mathf.PI*2/_floraPoseParts.Count;
                    float radius=_floraPattern==FloraPattern.RootReflux?Mathf.Lerp(13,2,progress):Mathf.Lerp(2,13,progress);
                    part.localPosition=new Vector3(Mathf.Cos(angle)*radius,.35f,Mathf.Sin(angle)*radius);
                    part.localScale=new Vector3(.25f,.7f,.25f);
                }
                else
                {
                    float side=i==0?-1:1;
                    part.localPosition=new Vector3(side*1.2f,2.5f+Mathf.Sin(progress*Mathf.PI)*.8f,1.5f);
                    part.localRotation=Quaternion.Euler(0,side*25,side*(30-25*progress));
                    part.localScale=new Vector3(.18f,2.2f,.35f);
                }
            }
        }
        private void ClearFloraTemporaryPose()
        {
            if(_floraTemporaryPose!=null)Destroy(_floraTemporaryPose);
            _floraTemporaryPose=null;_floraPoseParts.Clear();
        }
        private bool FloraSkipPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard=Keyboard.current;
            return keyboard!=null && (floraHard.cutsceneSkipKey==KeyCode.Escape
                ? keyboard.escapeKey.wasPressedThisFrame : keyboard.spaceKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(floraHard.cutsceneSkipKey);
#else
            return false;
#endif
        }
    }
}
