using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    // Development presentation and controls. The combat still runs in RaidNetworkSession.
    public sealed class AiSpectatorDevelopment : MonoBehaviour
    {
        public Rect menuRect=new Rect(24,90,430,480);
        public Rect statusRect=new Rect(12,12,410,540);
        private readonly List<CharacterDefinition>[] _jobs={new List<CharacterDefinition>(),new List<CharacterDefinition>(),
            new List<CharacterDefinition>(),new List<CharacterDefinition>()};
        private readonly int[] _choice=new int[4];
        private int _stage=1,_cameraMember;
        private bool _hard,_continuous=true,_open,_autoRetry;
        private bool _statusMinimized;
        private Vector2 _statusScroll;
        private int _maxRetries=2;
        private string _message="";
        private string _damageInput="100";
        private RaidNetworkSession _session;
        private float _lastProgressAt,_lastBossHp;
        private int _lastPhase=-1;

        private void Awake(){_session=GetComponent<RaidNetworkSession>();}
        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            foreach(var job in CharacterRosterData.Jobs)
            {
                var definition=CharacterCatalog.Find(job.Id);
                if(definition!=null&&definition.playable)_jobs[CharacterRosterData.Role(definition)-1].Add(definition);
            }
            var aegis=CharacterCatalog.Find(AegisPaladinData.JobId);
            if(aegis!=null&&aegis.playable&&!_jobs[0].Contains(aegis))_jobs[0].Insert(0,aegis);
#endif
        }
        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(_session==null||!_session.AiSpectatorActive)return;
            var camera=QuarterViewCamera.LocalCamera;
            if(camera!=null&&!LocalGameplayInput.ModalCovered)
            {
                if(LocalInputBindings.Pressed("SpectatePrevious"))Cycle(-1);
                if(LocalInputBindings.Pressed("SpectateNext"))Cycle(1);
                if(LocalInputBindings.Pressed("SpectateBoss"))camera.SetDevelopmentView(AiSpectatorView.Boss);
                if(LocalInputBindings.Pressed("SpectateArena"))camera.SetDevelopmentView(AiSpectatorView.Arena);
                if(LocalInputBindings.Pressed("SpectateFree"))camera.SetDevelopmentView(AiSpectatorView.Free);
            }
            if(camera!=null&&camera.DevelopmentView==AiSpectatorView.Party&&
                !LocalGameplayInput.DevelopmentReportCovered&&!CombatRange.LivingPartyMember(camera.SpectatorTarget))Cycle(1);
            var boss=StageManager.Instance?.boss;
            int phase=(int)_session.BattlePhase;
            float hp=boss!=null?boss.currentHp:0;
            if(phase!=_lastPhase||Mathf.Abs(hp-_lastBossHp)>1)
            {_lastPhase=phase;_lastBossHp=hp;_lastProgressAt=Time.unscaledTime;}
            if(_session.BattlePhase==RaidBattlePhase.Combat&&_session.AiSpectatorDamagePercent>0&&Time.unscaledTime-_lastProgressAt>90)
            {_message="진행 정체: 90초 동안 보스 HP/전투 단계 변화 없음. 로그와 타깃·경로를 확인하세요.";
             Debug.LogWarning("[AI Spectator] "+_message);_lastProgressAt=Time.unscaledTime;}
            if(_session.BattlePhase==RaidBattlePhase.Reward&&!_session.AiSpectatorStopOnClear&&Time.unscaledTime-_lastProgressAt>30)
            {
                var leader=_session.AiSpectatorLeader;
                var pc=leader!=null?leader.GetComponent<PlayerController>():null;
                Debug.LogWarning("[AI Spectator] reward portal wait: position="+leader?.position.ToString("F3")+
                    " stage="+StageManager.Instance?.currentStage+" phase="+_session.BattlePhase+
                    " stopOnClear="+_session.AiSpectatorStopOnClear+
                    " saveFailure="+CampaignRuntime.Instance?.HasSaveFailure+
                    " settlement="+(CampaignRuntime.Instance?.Settlement!=null)+
                    " portal="+RaidApproachField.Instance?.PortalActive+" near="+RaidApproachField.Instance?.NearPortal(leader)+
                    " dead="+pc?.IsDead+" stun="+pc?.IsStunned+" root="+pc?.IsMovementLocked+
                    " rescue="+pc?.IsBeingRescued+" knockback="+pc?.IsBeingKnockedBack+" rush="+pc?.IsSkillRushing+
                    " motionLock="+leader?.GetComponent<NetworkRaidAvatar>()?.MovementLocked);
                _lastProgressAt=Time.unscaledTime;
            }
#endif
        }
        private void Cycle(int direction)
        {
            var members=PartyLife.OrderedMembers();
            if(members.Count==0){QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Arena);return;}
            for(int offset=1;offset<=members.Count;offset++)
            {
                int next=(_cameraMember+direction*offset+members.Count*4)%members.Count;
                if(!CombatRange.LivingPartyMember(members[next]))continue;
                _cameraMember=next;QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Party,members[next]);return;
            }
            QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Arena);
        }
        private void Begin()
        {
            if(_session==null)return;
            var jobs=new string[4];for(int i=0;i<4;i++)
            {if(_jobs[i].Count==0){_message="선택할 수 있는 직업 데이터가 없습니다.";return;}
             jobs[i]=_jobs[i][_choice[i]%_jobs[i].Count].id;}
#if UNITY_EDITOR
            string profile="ai-spectator-"+DateTime.UtcNow.ToString("MMddHHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
            UnityEditor.SessionState.SetString("GuardianNormal.TestProfile",profile);
            CampaignRuntime.Instance?.ReloadAccount();
#endif
            if(_session.BeginAiSpectator(_stage,_hard?CampaignCatalog.Hard:CampaignCatalog.Normal,jobs,_continuous))
            {_session.AiSpectatorAutoRetry=_autoRetry;_session.AiSpectatorMaxRetries=_maxRetries;
             _open=false;_message="";Cycle(1);}
            else _message=_session.AiSpectatorError;
        }
        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(LocalGameplayInput.DevelopmentReportCovered)return;
            if(!Debug.isDebugBuild||_session==null)return;
            if(_session.Mode==RaidSessionMode.Menu)
            {
                if(!_open){if(GUI.Button(new Rect(20,Screen.height-62,210,42),"AI 4인 관전 테스트"))_open=true;return;}
                menuRect=GUI.Window(9843,menuRect,DrawMenu,"개발 테스트 / AI 4인 관전");return;
            }
            if(_session.AiSpectatorActive)
            {
                var window=statusRect;
                if(_statusMinimized)window.height=90;
                window=GUI.Window(9844,window,DrawStatus,"개발 테스트 / AI 4인 관전");
                // Preserve the expanded size and keep dragging available in both modes.
                if(_statusMinimized)statusRect.position=window.position;
                else statusRect=window;
            }
#endif
        }
        private void DrawMenu(int id)
        {
            GUILayout.Label("테스트 프로필에서만 시작됩니다. 관전자는 파티 인원이 아닙니다.");
            GUILayout.BeginHorizontal();if(GUILayout.Button("◀ Stage"))_stage=Mathf.Max(1,_stage-1);
            GUILayout.Label(_stage.ToString(),GUILayout.Width(35));if(GUILayout.Button("Stage ▶"))_stage=Mathf.Min(6,_stage+1);GUILayout.EndHorizontal();
            if(GUILayout.Button("난이도: "+(_hard?"하드":"노멀")))_hard=!_hard;
            for(int role=0;role<4;role++)
            {int r=role;if(_jobs[r].Count==0)continue;
             if(GUILayout.Button((r==0?"탱커":r==1?"힐러":r==2?"딜러 1":"딜러 2")+": "+_jobs[r][_choice[r]%_jobs[r].Count].name))
                 _choice[r]=(_choice[r]+1)%_jobs[r].Count;}
            _continuous=GUILayout.Toggle(_continuous,"연속 진행 (끔: 현재 스테이지 클리어 시 멈춤)");
            _autoRetry=GUILayout.Toggle(_autoRetry,"전멸 시 자동 재도전 (기본 꺼짐)");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("재도전 최대 -"))_maxRetries=Mathf.Max(0,_maxRetries-1);
            GUILayout.Label(_maxRetries.ToString(),GUILayout.Width(30));
            if(GUILayout.Button("재도전 최대 +"))_maxRetries=Mathf.Min(20,_maxRetries+1);
            GUILayout.EndHorizontal();
              GUILayout.Label(_stage>5?"Stage 6 노멀·하드는 야누스 전용 전투입니다. 후반 강제 진입은 개발 프리셋입니다.":"후반 강제 진입은 개발 프리셋이며 정상 런 검증이 아닙니다.");
              if(GUILayout.Button("관전 시작"))
              Begin();
            if(GUILayout.Button("닫기"))_open=false;
            if(_message.Length>0)GUILayout.Label(_message);
            GUI.DragWindow(new Rect(0,0,menuRect.width,24));
        }
        private void DrawStatus(int id)
        {
            if(GUI.Button(new Rect(statusRect.width-76,2,70,20),_statusMinimized?"최대화":"최소화"))
            {
                _statusMinimized=!_statusMinimized;
                GUIUtility.ExitGUI(); // Rebuild the window at its new size on the next GUI event.
            }
            var stage=StageManager.Instance;var boss=stage?.boss;
            GUILayout.Label("Stage "+stage?.currentStage+" / "+CampaignRuntime.Instance?.DifficultyId+
                " · "+_session.BattlePhase+" · "+(CampaignRuntime.Instance?.IsolatedTestProfile==true?"테스트 저장":"저장 경계 오류"));
            if(_statusMinimized)
            {
                GUILayout.Label("보스 HP "+(boss!=null?Mathf.CeilToInt(boss.currentHp)+"/"+Mathf.CeilToInt(boss.maxHp):"—"));
                GUI.DragWindow(new Rect(0,0,statusRect.width-80,24));
                return;
            }
            _statusScroll=GUILayout.BeginScrollView(_statusScroll);
            foreach(var member in PartyLife.OrderedMembers())
            {var pc=member!=null?member.GetComponent<PlayerController>():null;
             if(pc!=null)GUILayout.Label((CharacterActor.Ensure(member.gameObject).Definition?.name??member.name)+
                 " HP "+Mathf.CeilToInt(pc.currentHp)+"/"+Mathf.CeilToInt(pc.maxHp)+(pc.IsDead?" · 사망":""));}
            GUILayout.Label("보스 HP "+(boss!=null?Mathf.CeilToInt(boss.currentHp)+"/"+Mathf.CeilToInt(boss.maxHp):"—")+
                " · "+(boss?.CurrentCastSkillName??""));
            GUILayout.Label("공용 부활 "+(stage?.IrisCoins??0)+"/4 · 재도전 "+_session.AiSpectatorRetries+"/"+_session.AiSpectatorMaxRetries);
            int damage=_session.AiSpectatorDamagePercent;
            GUILayout.Label("AI 공격 피해 ×"+(damage/100f).ToString("F2")+" / "+damage+"%"+
                (damage==100?"":" · 기본 밸런스 결과 아님"));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("-10%p"))ChangeDamage(AiSpectatorDamageRules.Step(damage,-10));
            if(GUILayout.Button("+10%p"))ChangeDamage(AiSpectatorDamageRules.Step(damage,10));
            if(GUILayout.Button("100%로 초기화"))ChangeDamage(100);
            GUILayout.EndHorizontal();
            int slider=Mathf.RoundToInt(GUILayout.HorizontalSlider(_session.AiSpectatorDamagePercent,0,500));
            if(slider!=_session.AiSpectatorDamagePercent)ChangeDamage(slider);
            GUILayout.BeginHorizontal();
            _damageInput=GUILayout.TextField(_damageInput,GUILayout.Width(70));
            if(GUILayout.Button("% 적용"))
            {if(AiSpectatorDamageRules.TryParse(_damageInput,out int parsed))ChangeDamage(parsed);
             else _message="AI 공격 피해는 0~500 사이의 정수 %로 입력하세요.";}
            GUILayout.EndHorizontal();
            GUILayout.Label("기믹 오브젝트 파괴 속도도 바뀝니다. 0%는 공격 판정만 유지합니다.");
            GUILayout.Label("도전 시작 "+_session.AiSpectatorAttemptStartPercent+"% · 변경 "+
                (_session.AiSpectatorDamageChangedDuringAttempt?"있음":"없음")+" · 종료 "+_session.AiSpectatorAttemptEndPercent+"%");
            GUILayout.BeginHorizontal();if(GUILayout.Button("◀"))Cycle(-1);if(GUILayout.Button("다음 AI"))Cycle(1);
            if(GUILayout.Button("보스"))QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Boss);
            if(GUILayout.Button("전장"))QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Arena);
            if(GUILayout.Button("자유"))QuarterViewCamera.LocalCamera?.SetDevelopmentView(AiSpectatorView.Free);GUILayout.EndHorizontal();
            _session.AiSpectatorAutoRetry=GUILayout.Toggle(_session.AiSpectatorAutoRetry,"전멸 시 자동 재도전");
            GUILayout.BeginHorizontal();if(GUILayout.Button("최대 -"))_session.AiSpectatorMaxRetries=Mathf.Max(0,_session.AiSpectatorMaxRetries-1);
            if(GUILayout.Button("최대 +"))_session.AiSpectatorMaxRetries=Mathf.Min(20,_session.AiSpectatorMaxRetries+1);GUILayout.EndHorizontal();
            _session.AiSpectatorStopOnClear=GUILayout.Toggle(_session.AiSpectatorStopOnClear,"클리어 시 멈춤");
            bool controlsEnabled=GUI.enabled;
            GUI.enabled=controlsEnabled && _session.CanTestAiSpectatorWipe;
            if(GUILayout.Button("파티 전멸 테스트 (코인 부활 건너뜀)"))
                _message=_session.RequestAiSpectatorWipeTest()?"전멸 테스트 요청 · 기존 전멸·재도전 흐름을 확인하세요.":"전투 중인 개발 AI 관전에서만 사용할 수 있습니다.";
            GUI.enabled=controlsEnabled;
            if(GUILayout.Button("관전 테스트 종료"))_session.DisconnectConfirmed();
            if(_message.Length>0)GUILayout.Label(_message);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0,0,statusRect.width-80,24));
        }
        private void ChangeDamage(int percent)
        {
            if(_session.SetAiSpectatorDamagePercent(percent)){_damageInput=percent.ToString();_message="";}
            else _message="개발 AI 관전 세션에서 0~500%만 적용할 수 있습니다.";
        }
    }
}
