using UnityEngine;
using System.Linq;
using UnityEngine.UI;
namespace RaidRogue
{
    // One departure picker, reused by single-player and the authoritative network lobby.
    [DefaultExecutionOrder(3000)]
    public sealed class AegisRouteSelectionUI : MonoBehaviour
    {
        private static AegisRouteSelectionUI _instance;
        public static bool IsOpen=>_instance!=null&&_instance.gameObject.activeSelf;
        private CharacterActor _actor;
        private bool _network;
        private float _opened;
        private RectTransform _panel;
        private Text _status,_finishCaption;
        private Button _finish;
        private readonly Button[] _choices=new Button[12];
        private readonly Button[] _routeChoices=new Button[3];
        private readonly Text[] _choiceLabels=new Text[12],_routeLabels=new Text[3];
        private bool _bound;
        private void Awake(){_instance=this;}
        private bool _jobs=true; private string _pendingJob; private Text _heading;
        private readonly string[] _routes=new string[12];
        public static void PrepareViewForAuthoring(Canvas canvas)
        {
            if(_instance==null)_instance=canvas.GetComponentInChildren<AegisRouteSelectionUI>(true);
            if(_instance==null)
            {
                var root=Rect(canvas.transform,"AegisOriginSelection",Vector2.zero,Vector2.zero);
                root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
                root.gameObject.AddComponent<Image>().color=new Color(.02f,.03f,.05f,.98f);
                _instance=root.gameObject.AddComponent<AegisRouteSelectionUI>();
            }
            if(!_instance._bound)_instance.Build();
            _instance.gameObject.SetActive(false);
        }
        public static void Open(CharacterActor actor,bool network)
        {
            if (CampaignRuntime.Instance?.BuildLocked == true || RaidNetworkSession.Instance?.CampaignBuildLocked == true) return;
            var canvas=FindFirstObjectByType<SkillHotbarUI>()?.GetComponentInParent<Canvas>()??FindFirstObjectByType<Canvas>();
            if(canvas==null)return;
            if(_instance==null) _instance=canvas.GetComponentInChildren<AegisRouteSelectionUI>(true);
            if(_instance==null)
            {
                var root=Rect(canvas.transform,"AegisOriginSelection",Vector2.zero,Vector2.zero);
                root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
                root.gameObject.AddComponent<Image>().color=new Color(.02f,.03f,.05f,.98f);
                _instance=root.gameObject.AddComponent<AegisRouteSelectionUI>();
            }
            if(!_instance._bound)_instance.Build();
            _instance._actor=actor;_instance._network=network;_instance._opened=Time.unscaledTime;_instance._jobs=true;_instance._pendingJob=null;_instance.RefreshChoices();
            _instance.gameObject.SetActive(true);RaidUIOrder.Raise(_instance.transform);
            QuarterViewCamera.LocalCamera?.ReleaseOrbit();LocalInputBindings.QuarantineHeld();LocalGameplayInput.SuppressThisFrame();
        }
        public static void OpenNetwork()
        {
            var session=RaidNetworkSession.Instance;if(session==null||!session.CanEditLobbyBuild||!session.HasLocalAvatar)return;
            foreach(var avatar in session.Avatars.Values)if(avatar.IsLocal)
            {
                var actor=CharacterActor.Ensure(avatar.gameObject);

                Open(actor,true);return;
            }
        }
        public static void Close()
        {
            if(_instance!=null)_instance.gameObject.SetActive(false);
            LocalInputBindings.QuarantineHeld();LocalGameplayInput.SuppressThisFrame();LocalGameplayInput.SuppressPointerThisFrame();
        }
        private void Build()
        {
            _bound=true;
            if(RaidUIView.Attach(this,"origin",transform))
            {
                for(int i=0;i<12;i++){int index=i;RaidUIView.Bind(this,"job."+i,()=>Select(index));}
                for(int i=0;i<3;i++){int index=i;RaidUIView.Bind(this,"route."+i,()=>Select(index));}
                RaidUIView.Bind(this,"back",Back);RaidUIView.Bind(this,"finish",Finish);return;
            }
            var viewParent=RaidUIView.Begin(this,"origin",transform);
            _panel=Rect(viewParent,"Panel",Vector2.zero,new Vector2(960,600));
            _heading=Label(_panel,"12직업 선택",new Vector2(0,255),new Vector2(900,55),28);
            Label(_panel,"출발 전 기원 선택 · Stage 1 R / Stage 2 F / Stage 3 V · 실제 단축키는 설정/핫바 참조",new Vector2(0,207),new Vector2(920,45),17);
            for(int i=0;i<12;i++)
            {
                int index=i;var r=Rect(_panel,"Choice"+i,new Vector2((i%4-1.5f)*230,130-(i/4)*125),new Vector2(218,112));
                var image=r.gameObject.AddComponent<Image>();RaidUIStyle.Color(image, new Color(.12f,.19f,.28f));
                var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;RaidUIView.Bind(this,"job."+i,()=>Select(index),button);_choices[i]=button;
                var description=Label(r,"",Vector2.zero,new Vector2(200,96),18);_choiceLabels[i]=description;description.resizeTextForBestFit=true;description.resizeTextMinSize=12;description.resizeTextMaxSize=18;
            }
            for(int i=0;i<3;i++)
            {
                int index=i;var r=Rect(_panel,"Route"+i,new Vector2((i-1)*310,5),new Vector2(298,340));
                var image=r.gameObject.AddComponent<Image>();RaidUIStyle.Color(image, new Color(.12f,.19f,.28f));
                var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;_routeChoices[i]=button;
                RaidUIView.Bind(this,"route."+i,()=>Select(index),button);
                _routeLabels[i]=Label(r,"",Vector2.zero,new Vector2(280,324),18);
            }
            _status=Label(_panel,"",new Vector2(0,-200),new Vector2(930,50),19);
            ButtonAt(_panel,"돌아가기",new Vector2(-180,-260),Back);
            _finish=ButtonAt(_panel,"선택 확정 / 출발",new Vector2(180,-260),Finish);
            _finishCaption=_finish.GetComponentInChildren<Text>();
        }
        private void Update()
        {
            bool lobby=_network?RaidNetworkSession.Instance?.CanEditLobbyBuild==true:StageManager.Instance?.CanSelectInitialRole==true;
            if(!lobby||_actor==null||RaidNetworkSession.Instance?.Ending==true){Close();return;}
            bool assigned=_pendingJob==null||_actor.Definition.id==_pendingJob;
            if(!_jobs&&assigned&&_routes[0]!=_actor.Definition.routes.FirstOrDefault()?.id)RefreshChoices();
            _status.text=!assigned?"호스트 직업 승인 대기 중…":"현재: "+_actor.Definition.name+" · "+(_actor.Definition.routes.FirstOrDefault(r=>r.id==_actor.RouteId)?.name??"기원 선택 필요");
            bool covered=LocalCameraSettingsUI.IsOpen||RaidSteamRuntime.Instance?.ConfirmInvite==true||RaidSteamRuntime.Instance?.OverlayOpen==true;
            for(int i=0;i<12;i++)
            {
                var button=_jobs?_choices[i]:i<3?_routeChoices[i]:null;if(button==null)continue;
                button.interactable=assigned&&!covered;
                RaidUIStyle.Color(button.targetGraphic, assigned&&_actor.RouteId==_routes[i]?new Color(.24f,.39f,.35f):new Color(.12f,.19f,.28f));
            }
            _finish.interactable=!_jobs&&assigned&&_actor.DepartureReady&&!covered;
            _finishCaption.text=_network?"선택 확인 · 대기실로":"선택 확정 · 출발";
        }
        private void Select(int i)
        {
            if(_jobs)
            {
                _pendingJob=_routes[i];
                if(_network)RaidNetworkSession.Instance.RequestCharacterAction(12,_pendingJob,0);
                else if(!RoleSelectionManager.Instance.SelectRosterJob(_pendingJob))return;
                _jobs=false;RefreshChoices();
            }
            else _actor?.RequestChoice("route",_routes[i]);
        }
        private void RefreshChoices()
        {
            var jobs=CharacterCatalog.All.Where(j=>j.legacyRole==0&&j.playable).ToArray();
            _heading.text=_jobs?"자연과 생명의 시간 · 12직업":(_actor?.Definition.name??"")+" · 기원 3택 1";
            for(int i=0;i<12;i++)
            {
                _choices[i].gameObject.SetActive(_jobs&&i<jobs.Length);
                if(i<3)_routeChoices[i].gameObject.SetActive(!_jobs&&i<(_actor?.Definition.routes.Length??0));
                bool visible=_jobs?i<jobs.Length:i<(_actor?.Definition.routes.Length??0);if(!visible)continue;
                var label=_jobs?_choiceLabels[i]:_routeLabels[i];
                if(_jobs){_routes[i]=jobs[i].id;label.text=jobs[i].name+" · "+(jobs[i].role==CharacterRole.Tank?"탱커":jobs[i].role==CharacterRole.Healer?"힐러":jobs[i].classification==AttackClassification.Melee?"근딜":jobs[i].classification==AttackClassification.RangedPhysical?"원거리":"캐스터")+"\n"+jobs[i].description;}
                else{_routes[i]=_actor.Definition.routes[i].id;label.text=CharacterRosterData.RouteDescription(_actor.Definition,_routes[i]);}
            }
        }
        private void LateUpdate()
        {
            var available=((RectTransform)transform).rect.size;
            if(!RaidUIStyle.Authored(_panel)) _panel.localScale=Vector3.one*Mathf.Max(.1f,Mathf.Min(1,(available.x-20)/960,(available.y-20)/600));
            if(!LocalCameraSettingsUI.IsOpen&&RaidSteamRuntime.Instance?.ConfirmInvite!=true&&RaidNetworkSession.Instance?.IsRecovering!=true)RaidUIOrder.Raise(transform);
            FindFirstObjectByType<LocalCameraSettingsUI>()?.RaiseAboveMenus();
        }
        private void Back(){if(!_jobs){_pendingJob=null;_jobs=true;RefreshChoices();}else Close();}
        private void Finish()
        {if(_actor==null||!_actor.DepartureReady)return;Close();if(!_network)RoleSelectionManager.Instance?.ConfirmAegisDeparture();}
        private void OnDestroy(){if(_instance==this)_instance=null;}
        private static RectTransform Rect(Transform parent,string name,Vector2 pos,Vector2 size)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=pos;r.sizeDelta=size;return r;}
        private static Text Label(Transform parent,string value,Vector2 pos,Vector2 size,int font)
        {var r=Rect(parent,"Label",pos,size);var text=r.gameObject.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=font;RaidUIStyle.Color(text, Color.white);text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.text=value;return text;}
        private Button ButtonAt(Transform parent,string value,Vector2 pos,UnityEngine.Events.UnityAction action)
        {var r=Rect(parent,value,pos,new Vector2(320,50));var image=r.gameObject.AddComponent<Image>();RaidUIStyle.Color(image, new Color(.17f,.29f,.38f));var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;RaidUIView.Bind(this,action== (UnityEngine.Events.UnityAction)Finish ? "finish" : "back",action,button);Label(r,value,Vector2.zero,new Vector2(310,46),20);return button;}
    }
}
