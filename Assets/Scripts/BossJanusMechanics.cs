using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        private Vector3 JanusPostPoint(int index)
        {
            var points=JanusTuning.postPositions;
            return points!=null&&points.Length==4?points[index]:new Vector3(index<2?-10:10,0,index%2==0?-8:-14);
        }
        private static bool JanusAutomated(Transform member)
        {
            var avatar=member?.GetComponent<NetworkRaidAvatar>();
            return member!=null&&(member.GetComponent<PartyBotAI>()!=null||avatar!=null&&(avatar.IsBot||avatar.Automated));
        }
        private void BeginJanusPosts()
        {
            ClearJanusAttack();ClearJanusPosts();_janusPostUsed=true;_janusPattern=JanusPattern.ChainPosts;
            if(JanusHard)BeginJanusHardPosts();
            _janusStep=JanusStep.Posts;fsmState=BossFSMState.CastingSkill;
            _janusRemaining=JanusTuning.postSeconds;_castTotalDuration=_castRemainingTime=_janusRemaining;
            _currentCastSkillName=JanusHard?"정문 봉인 재강화: 장력 조율":"정문 봉인 재강화";_janusPostOverlap=0;
            ShowJanusPosts();
            if(JanusTuning.chainVisualPrefab!=null)
            {_janusChainVisual=Instantiate(JanusTuning.chainVisualPrefab,new Vector3(0,5,0),Quaternion.identity);
             foreach(var collider in _janusChainVisual.GetComponentsInChildren<Collider>())collider.enabled=false;}
            JanusCue(JanusTuning.postCue);JanusTrigger(JanusTuning.postTrigger,false);
            SetMechanicAlert((JanusHard?"정문 봉인 재강화: 장력 조율 · 당김 준비":"정문 봉인 재강화 · 서로 다른 말뚝 4개")+
                " · "+LocalInputBindings.Label("Interact")+" 상호작용",Color.red,_janusRemaining);
            CombatMetricsManager.Event("야누스 60% 말뚝 시작 · 제한 "+JanusTuning.postSeconds.ToString("0.#")+"초");
        }
        private void ShowJanusPosts()
        {
            for(int i=0;i<4;i++)
            {
                if(_janusPostVisuals[i]!=null)continue;
                var point=JanusPostPoint(i);
                _janusPostVisuals[i]=JanusTuning.postVisualPrefab!=null?Instantiate(JanusTuning.postVisualPrefab,point,Quaternion.identity):
                    RaidPrimitive.Create(PrimitiveType.Cylinder);
                _janusPostVisuals[i].name="사슬 말뚝 "+(i+1);
                _janusPostVisuals[i].transform.position=point;
                if(JanusTuning.postVisualPrefab==null)_janusPostVisuals[i].transform.localScale=new Vector3(1.4f,.35f,1.4f);
                foreach(var collider in _janusPostVisuals[i].GetComponentsInChildren<Collider>())collider.enabled=false;
                _janusPostLabels[i]=FloraLabel("말뚝 "+(i+1)+" · 대기",new Color(.9f,.7f,.25f));
                _janusPostLabels[i].transform.SetParent(_janusPostVisuals[i].transform,true);
                _janusPostLabels[i].transform.position=point+Vector3.up*2;
            }
        }
        private void ClearJanusPosts()
        {
            for(int i=0;i<4;i++)
            {if(_janusPostVisuals[i]!=null)Destroy(_janusPostVisuals[i]);_janusPostVisuals[i]=null;
             _janusPostLabels[i]=null;_janusPostActors[i]=null;}
            _janusPostOverlap=0;
            for(int i=0;i<4;i++)_janusHardHolding[i]=false;
            if(_janusChainVisual!=null)Destroy(_janusChainVisual);_janusChainVisual=null;
        }
        public bool RequestJanusPost(Transform actor)
        {
            if(RaidCombatAuthority.Replica||!JanusPostActive||!CombatRange.LivingPartyMember(actor)||JanusAutomated(actor))return false;
            int nearest=-1;float best=JanusTuning.postInteractRange*JanusTuning.postInteractRange;
            for(int i=0;i<4;i++)
            {var delta=actor.position-JanusPostPoint(i);delta.y=0;float d=delta.sqrMagnitude;
             if(d<=best){best=d;nearest=i;}}
            if(nearest<0)return false;
            for(int i=0;i<4;i++)if(_janusPostActors[i]==actor)
            {
                if(JanusHard){_janusHardHolding[i]=JanusHardRules.IsPull(_janusHardPostPhase)&&!_janusHardHolding[i];return true;}
                _janusPostActors[i]=null;
            }
            if(_janusPostActors[nearest]!=null&&!JanusAutomated(_janusPostActors[nearest]))return false;
            _janusPostActors[nearest]=actor;
            if(JanusHard)_janusHardHolding[nearest]=JanusHardRules.IsPull(_janusHardPostPhase);
            CombatMetricsManager.Event("야누스 말뚝 "+(nearest+1)+" 당기기 · "+actor.name);
            return true;
        }
        private void AssignJanusAiPosts()
        {
            foreach(var actor in GetLivingPartyMemberTransforms())
            {
                if(!JanusAutomated(actor))continue;
                bool assigned=false;for(int i=0;i<4;i++)if(_janusPostActors[i]==actor)assigned=true;
                if(assigned)continue;
                int chosen=-1;float best=float.PositiveInfinity;
                for(int i=0;i<4;i++)if(_janusPostActors[i]==null)
                {float d=(actor.position-JanusPostPoint(i)).sqrMagnitude;
                 if(d<best){chosen=i;best=d;}}
                if(chosen>=0)_janusPostActors[chosen]=actor;
            }
        }
        private void TickJanusPosts(float dt)
        {
            _janusRemaining=Mathf.Max(0,_janusRemaining-dt);_castRemainingTime=_janusRemaining;
            if(!RaidNetworkSession.Multiplayer&&!LocalGameplayInput.Blocked&&LocalInputBindings.Pressed("Interact"))
                RequestJanusPost(StageManager.Instance?.player?.transform);
            for(int i=0;i<4;i++)
            {
                var actor=_janusPostActors[i];
                if(actor==null)continue;
                if(!CombatRange.LivingPartyMember(actor)||!JanusNormalRules.Inside(actor.position,JanusTuning.arenaRadius))
                {_janusPostActors[i]=null;_janusHardHolding[i]=false;}
                else if(!JanusAutomated(actor)&&Vector3.Distance(new Vector3(actor.position.x,0,actor.position.z),JanusPostPoint(i))>JanusTuning.postInteractRange)
                    {_janusPostActors[i]=null;_janusHardHolding[i]=false;}
            }
            AssignJanusAiPosts();
            if(JanusHard){TickJanusHardPosts(dt);return;}
            var ordered=PartyLife.OrderedMembers();int ready=0;
            var assigned=new int[4];var inRange=new bool[4];var alive=new bool[4];
            for(int i=0;i<4;i++)
            {assigned[i]=ordered.IndexOf(_janusPostActors[i]);
             if(i<ordered.Count)alive[i]=CombatRange.LivingPartyMember(ordered[i]);
             var actor=_janusPostActors[i];
             inRange[i]=actor!=null&&CombatRange.LivingPartyMember(actor)&&
                 Vector3.Distance(new Vector3(actor.position.x,0,actor.position.z),JanusPostPoint(i))<=JanusTuning.postInteractRange;
             if(inRange[i])ready++;}
            _janusPostOverlap=ordered.Count==4&&JanusNormalRules.AllPosts(assigned,inRange,alive)?_janusPostOverlap+dt:0;
            UpdateJanusPostLabels();
            SetMechanicAlert("정문 봉인 재강화 · 준비 "+ready+"/4 · 동시 유지 "+
                _janusPostOverlap.ToString("0.0")+"/"+JanusTuning.postOverlapSeconds.ToString("0.0")+"초",Color.yellow,_janusRemaining);
            if(_janusPostOverlap>=JanusTuning.postOverlapSeconds)
            {
                CombatMetricsManager.Event("야누스 정문 봉인 사슬 절단 성공");
                _janusChainBroken=true;RaidApproachField.Instance?.SetJanusChainBroken(true);
                JanusCue(JanusTuning.chainBreakCue);ClearJanusPosts();
                _janusCounterUnlocked=true;_janusStep=JanusStep.Groggy;fsmState=BossFSMState.Groggy;
                _janusRemaining=JanusTuning.counterGroggySeconds;_groggyRemainingTime=_janusRemaining;
                _castRemainingTime=_castTotalDuration=0;_currentCastSkillName="사슬 절단 · 공격 기회";
                return;
            }
            if(_janusRemaining>0)return;
            CombatMetricsManager.Event("야누스 정문 봉인 말뚝 시간 초과 · 준비 "+ready+"/4");
            ClearJanusPosts();_janusCounterUnlocked=true;
            foreach(var member in GetLivingPartyMemberTransforms())
                DamagePartyMember(member,JanusTuning.postFailureDamage,CombatDamageKind.Area,"janus.posts.failure");
            if(!StageManager.CombatSuspended)EndJanus(JanusTuning.postRecoverySeconds);
        }
        private void UpdateJanusPostLabels()
        {
            for(int i=0;i<4;i++)if(_janusPostLabels[i]!=null)
            {var actor=_janusPostActors[i];bool holding=actor!=null&&CombatRange.LivingPartyMember(actor)&&
                Vector3.Distance(new Vector3(actor.position.x,0,actor.position.z),JanusPostPoint(i))<=JanusTuning.postInteractRange;
             bool pulling=holding&&(!JanusHard||_janusHardHolding[i]);
             _janusPostLabels[i].text="말뚝 "+(i+1)+(pulling?" · 당기는 중":holding?" · 해제":" · 대기");
             _janusPostLabels[i].color=pulling?Color.green:new Color(.9f,.7f,.25f);}
        }
        private void BeginJanusCounter()
        {
            ClearJanusAttack();_janusCounterSerial++;
            if(JanusHard)_janusHardQueueCounter=false;
            _janusPattern=JanusPattern.TwinCounter;_janusStep=JanusStep.CounterPrepare;
            fsmState=BossFSMState.CastingSkill;_janusRemaining=JanusTuning.counterPreparation;
            _castTotalDuration=_castRemainingTime=_janusRemaining;
            _janusAlphaCounter=_janusOmegaCounter=-1;_janusAlphaCounterActor=_janusOmegaCounterActor=null;
            bool alternate=JanusHard&&_janusHardCounterAlternate;
            transform.position=alternate?janusHard.alphaCounterAlternate:JanusTuning.alphaPosition;
            transform.rotation=Quaternion.LookRotation(Vector3.back);
            if(_janusOmega!=null){_janusOmega.transform.position=alternate?janusHard.omegaCounterAlternate:JanusTuning.omegaPosition;
                _janusOmega.transform.rotation=Quaternion.LookRotation(Vector3.back);}
            if(JanusHard)_janusHardCounterAlternate=!_janusHardCounterAlternate;
            _currentCastSkillName=JanusHard?"교대하는 양면 협공":"양면 협공 돌진 · 두 명의 정면 카운터";
            JanusGround(AoETelegraphType.Rectangle,transform.position,transform.forward,_janusRemaining+JanusTuning.counterWindow,JanusTuning.lineWidth,JanusTuning.lineLength);
            if(_janusOmega!=null)JanusGround(AoETelegraphType.Rectangle,_janusOmega.transform.position,_janusOmega.transform.forward,
                _janusRemaining+JanusTuning.counterWindow,JanusTuning.lineWidth,JanusTuning.lineLength);
            JanusTrigger(JanusTuning.counterTrigger,false);JanusTrigger(JanusTuning.counterTrigger,true);
            SetMechanicAlert("⚔ 쌍동 카운터 / 전 직업 가능 · 알파·오메가 각각 다른 사람 · 차단 0/2",new Color(.2f,.65f,1),_janusRemaining);
        }
        private void OpenJanusCounter()
        {
            _janusStep=JanusStep.CounterWindow;_janusRemaining=JanusTuning.counterWindow;
            _castTotalDuration=_castRemainingTime=_janusRemaining;
            CombatCounterTarget.Ensure(transform).OpenWindow(_janusRemaining);
            if(_janusOmega!=null)CombatCounterTarget.Ensure(_janusOmega.transform).OpenWindow(_janusRemaining);
            _janusAlphaIcon=FloraLabel("⚔ 알파 · 전 직업",new Color(.2f,.65f,1));
            _janusAlphaIcon.transform.position=transform.position+Vector3.up*4.5f;
            if(_janusOmega!=null){_janusOmegaIcon=FloraLabel("⚔ 오메가 · 전 직업",new Color(.2f,.65f,1));
                _janusOmegaIcon.transform.position=_janusOmega.transform.position+Vector3.up*4.5f;}
            JanusCue(JanusTuning.counterCue);
            SetMechanicAlert("⚔ 쌍동 카운터 / 전 직업 가능 · 차단 0/2 · 지금!",new Color(.25f,.8f,1),_janusRemaining);
        }
        private void FinishJanusCounter()
        {
            if(_janusStep!=JanusStep.CounterWindow)return;
            _janusLastCounterEnd=RaidCombatAuthority.ServerTime;
            bool alpha=_janusAlphaCounterActor!=null,omega=_janusOmegaCounterActor!=null;
            CombatMetricsManager.Event("야누스 쌍동 카운터 · 알파 "+(alpha?"성공":"미성공")+" / 오메가 "+(omega?"성공":"미성공"));
            foreach(var member in GetLivingPartyMemberTransforms())
            {
                if(!alpha&&JanusChargeHit(member.position,transform.position,transform.forward))
                    DamagePartyMember(member,JanusTuning.counterChargeDamage,CombatDamageKind.Area,"janus.alpha.charge");
                if(!omega&&_janusOmega!=null&&JanusChargeHit(member.position,_janusOmega.transform.position,_janusOmega.transform.forward))
                    DamagePartyMember(member,JanusTuning.counterChargeDamage,CombatDamageKind.Area,"janus.omega.charge");
            }
            if(StageManager.CombatSuspended)return;
            if(JanusNormalRules.CounterSuccess(alpha,omega))
            {
                ClearJanusAttack();_janusStep=JanusStep.Groggy;fsmState=BossFSMState.Groggy;
                _janusRemaining=AegisPaladinData.CounterGroggy;_groggyRemainingTime=_janusRemaining;
                _currentCastSkillName="쌍동 카운터 성공 · 2초 그로기";
                SetMechanicAlert("⚔ 2/2 완전 차단 · 공격 기회",Color.green,_janusRemaining);
            }
            else {SetMechanicAlert("⚔ "+JanusCounterCount()+"/2 차단 · 실패한 돌진만 실행",Color.red,3);
                EndJanus(JanusTuning.counterRecoverySeconds);}
        }
        private bool JanusChargeHit(Vector3 point,Vector3 origin,Vector3 forward)
        {var delta=point-origin;return GuardianNormalState.InRectangle(Vector3.Dot(delta,Vector3.Cross(Vector3.up,forward)),
            Vector3.Dot(delta,forward),JanusTuning.lineWidth,JanusTuning.lineLength);}
        private bool TryJanusCounterDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;
            if((_janusStep!=JanusStep.CounterPrepare&&_janusStep!=JanusStep.CounterWindow)||!PartyAiTactics.CanTakeCounterPosition(actor))return false;
            var target=JanusAiCounterTarget(actor);
            if(target==null&&_janusStep==JanusStep.CounterPrepare)
                target=role==4&&_janusOmega!=null?_janusOmega.transform:transform;
            if(target==null)return false;
            // The final approach turns network AI toward the actual counter target through normal movement.
            float distance=_janusStep==JanusStep.CounterPrepare?3.8f:2.2f;
            destination=target.position+target.forward*distance;
            destination.y=actor.position.y;
            return true;
        }
        public bool JanusDestination(Transform actor,int role,out Vector3 destination)
        {
            destination=actor.position;if(!JanusEncounter)return false;
            if(_janusStep==JanusStep.Posts)
            {for(int i=0;i<4;i++)if(_janusPostActors[i]==actor)
             {destination=JanusPostPoint(i);destination.y=actor.position.y;return true;}
             return false;}
            if(_janusStep==JanusStep.CounterPrepare||_janusStep==JanusStep.CounterWindow)
                return TryJanusCounterDestination(actor,role,out destination);
            if(JanusHard&&_janusStep==JanusStep.HardFollowup)
            {
                if(_janusHardFollowupPattern==JanusPattern.AlphaSweep)
                {destination=_janusOrigin+Vector3.Cross(Vector3.up,_janusForward)*(_janusRight?8:-8);return true;}
                if(_janusHardFollowupPattern==JanusPattern.OmegaCircle)
                {destination=_janusOrigin+new Vector3((role-2.5f)*.8f,0,-2);return true;}
                if(_janusHardFollowupPattern==JanusPattern.RedChainWave)
                {destination=new Vector3(Mathf.Clamp(actor.position.x,-12,12),actor.position.y,-8);return true;}
                if(_janusHardFollowupPattern==JanusPattern.OmegaLine||_janusHardFollowupPattern==JanusPattern.ShieldSpearCross)
                {destination=actor.position+Vector3.Cross(Vector3.up,_janusForward)*(role%2==0?4:-4);return true;}
            }
            if(_janusStep!=JanusStep.Cast)return false;
            if(JanusHard&&_janusPattern==JanusPattern.ShieldSpearCross)
            {destination=_janusOrigin+Vector3.Cross(Vector3.up,_janusForward)*(_janusRight?-8:8);return true;}
            if(JanusHard&&_janusPattern==JanusPattern.RedChainWave)
            {destination=new Vector3(Mathf.Clamp(actor.position.x,-12,12),actor.position.y,-12);return true;}
            if(_janusPattern==JanusPattern.TwinOath&&_janusTarget!=null)
            {destination=_janusTarget.position+new Vector3((role-2.5f)*.45f,0,.3f);return true;}
            if(_janusPattern==JanusPattern.OmegaCircle)
            {var away=actor.position-_janusOrigin;away.y=0;if(away.sqrMagnitude<.01f)away=Vector3.back;
             destination=_janusOrigin+away.normalized*(JanusTuning.circleRadius+2);return true;}
            if(_janusPattern==JanusPattern.AlphaSweep)
            {destination=_janusOrigin+Vector3.Cross(Vector3.up,_janusForward)*(_janusRight?-8:8);return true;}
            if(_janusPattern==JanusPattern.OmegaLine&&_janusLineLocked)
            {destination=actor.position+Vector3.Cross(Vector3.up,_janusForward)*(role%2==0?4:-4);return true;}
            if(_janusPattern==JanusPattern.SealMark)
            {destination=new Vector3(role%2==0?9:-9,actor.position.y,role<=2?-7:-13);return true;}
            return false;
        }
    }
}
