using UnityEngine;
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
        private Text _status;
        private Button _finish;
        private readonly Button[] _choices=new Button[3];
        private readonly string[] _routes={AegisRouteData.Bulwark,AegisRouteData.Sun,AegisRouteData.Mirror};
        public static void Open(CharacterActor actor,bool network)
        {
            var canvas=FindFirstObjectByType<SkillHotbarUI>()?.GetComponentInParent<Canvas>()??FindFirstObjectByType<Canvas>();
            if(canvas==null)return;
            if(_instance==null)
            {
                var root=Rect(canvas.transform,"AegisOriginSelection",Vector2.zero,Vector2.zero);
                root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
                root.gameObject.AddComponent<Image>().color=new Color(.02f,.03f,.05f,.98f);
                _instance=root.gameObject.AddComponent<AegisRouteSelectionUI>();_instance.Build();
            }
            _instance._actor=actor;_instance._network=network;_instance._opened=Time.unscaledTime;
            _instance.gameObject.SetActive(true);_instance.transform.SetAsLastSibling();
            QuarterViewCamera.LocalCamera?.ReleaseOrbit();LocalInputBindings.QuarantineHeld();LocalGameplayInput.SuppressThisFrame();
        }
        public static void OpenNetwork()
        {
            var session=RaidNetworkSession.Instance;if(session==null||!session.CanChooseRole||!session.HasLocalAvatar)return;
            foreach(var avatar in session.Avatars.Values)if(avatar.IsLocal)
            {
                var actor=CharacterActor.Ensure(avatar.gameObject);
                if(actor.Definition.id!=AegisPaladinData.JobId)session.RequestCharacterAction(12,AegisPaladinData.JobId,0);
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
            _panel=Rect(transform,"Panel",Vector2.zero,new Vector2(960,600));
            Label(_panel,"이지스 팔라딘 · 기원 선택",new Vector2(0,255),new Vector2(900,55),28);
            Label(_panel,"출발 전 변경 가능 · 재도전/재접속에는 유지 · R/F/V는 아직 잠김",new Vector2(0,207),new Vector2(920,45),17);
            for(int i=0;i<3;i++)
            {
                string route=_routes[i];var r=Rect(_panel,route,new Vector2((i-1)*310,5),new Vector2(298,340));
                var image=r.gameObject.AddComponent<Image>();image.color=new Color(.12f,.19f,.28f);
                var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>_actor?.RequestChoice("route",route));_choices[i]=button;
                var description=Label(r,AegisRouteData.Description(route),Vector2.zero,new Vector2(270,320),18);
                description.resizeTextForBestFit=true;description.resizeTextMinSize=14;description.resizeTextMaxSize=18;
            }
            _status=Label(_panel,"",new Vector2(0,-200),new Vector2(930,50),19);
            ButtonAt(_panel,"돌아가기",new Vector2(-180,-260),Close);
            _finish=ButtonAt(_panel,"선택 확정 / 출발",new Vector2(180,-260),Finish);
        }
        private void Update()
        {
            bool lobby=_network?RaidNetworkSession.Instance?.CanChooseRole==true:StageManager.Instance?.CanSelectInitialRole==true;
            if(!lobby||_actor==null||RaidNetworkSession.Instance?.Ending==true){Close();return;}
            bool assigned=_actor.Definition.id==AegisPaladinData.JobId;
            _status.text=!assigned?(Time.unscaledTime-_opened>5?"직업 선택이 승인되지 않았습니다. 탱커 자리/연결 상태를 확인하고 돌아가서 다시 선택하세요.":"호스트의 직업 선택 승인 대기 중…"):
                "호스트 확정: "+(_actor.GetComponent<AegisPaladin>()?.RouteName??"기원 선택 필요");
            bool covered=LocalCameraSettingsUI.IsOpen||RaidSteamRuntime.Instance?.ConfirmInvite==true||RaidSteamRuntime.Instance?.OverlayOpen==true;
            for(int i=0;i<3;i++)
            {
                _choices[i].interactable=assigned&&!covered;
                _choices[i].targetGraphic.color=assigned&&_actor.RouteId==_routes[i]?new Color(.24f,.39f,.35f):new Color(.12f,.19f,.28f);
            }
            _finish.interactable=assigned&&_actor.DepartureReady&&!covered;
            _finish.GetComponentInChildren<Text>().text=_network?"선택 확인 · 대기실로":"선택 확정 · 출발";
        }
        private void LateUpdate()
        {
            var available=((RectTransform)transform).rect.size;
            _panel.localScale=Vector3.one*Mathf.Max(.1f,Mathf.Min(1,(available.x-20)/960,(available.y-20)/600));
            if(!LocalCameraSettingsUI.IsOpen&&RaidSteamRuntime.Instance?.ConfirmInvite!=true&&RaidNetworkSession.Instance?.IsRecovering!=true)transform.SetAsLastSibling();
            FindFirstObjectByType<LocalCameraSettingsUI>()?.RaiseAboveMenus();
        }
        private void Finish()
        {if(_actor==null||!_actor.DepartureReady)return;Close();if(!_network)RoleSelectionManager.Instance?.ConfirmAegisDeparture();}
        private void OnDestroy(){if(_instance==this)_instance=null;}
        private static RectTransform Rect(Transform parent,string name,Vector2 pos,Vector2 size)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=pos;r.sizeDelta=size;return r;}
        private static Text Label(Transform parent,string value,Vector2 pos,Vector2 size,int font)
        {var r=Rect(parent,"Label",pos,size);var text=r.gameObject.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=font;text.color=Color.white;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.text=value;return text;}
        private static Button ButtonAt(Transform parent,string value,Vector2 pos,UnityEngine.Events.UnityAction action)
        {var r=Rect(parent,value,pos,new Vector2(320,50));var image=r.gameObject.AddComponent<Image>();image.color=new Color(.17f,.29f,.38f);var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(action);Label(r,value,Vector2.zero,new Vector2(310,46),20);return button;}
    }
}
