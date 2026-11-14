using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    public sealed partial class CharacterFrameworkUI
    {
        private RaidBuildStationView _stationView;
        private Vector3 _stationDesignScale;
        private readonly RaidBuildStationView.Icon[] _choiceIcons=new RaidBuildStationView.Icon[7];
        private readonly RaidBuildStationView.Icon[,] _partyIcons=new RaidBuildStationView.Icon[4,7];
        private readonly Text[] _counterBadges=new Text[4];
        private RaidBuildStationView.Icon _ownRouteIcon;
        private Text _stationStage, _stationTooltip;
        private string _stationHoverText;
        private AstellaOrnateHUD _stationArt;
        private bool _stationArtResolved;
        private static readonly CharacterSlot[] StationSlots={CharacterSlot.Identity,CharacterSlot.Gcd2,CharacterSlot.OffGcd1,CharacterSlot.OffGcd2,CharacterSlot.Counter};

        private RaidBuildStationView.Icon MakeStationIcon(Transform parent,Vector2 position,float size)
        {
            var r=Rect("BuildIcon",parent,new Vector2(.5f,.5f),position,new Vector2(size,size));
            r.gameObject.AddComponent<Image>().color=new Color(.14f,.19f,.29f);
            var frame=r.gameObject.AddComponent<Outline>();frame.effectColor=new Color(.65f,.54f,.32f);frame.effectDistance=new Vector2(1,-1);
            var icon=new RaidBuildStationView.Icon{root=r};
            var art=Rect("Artwork",r,new Vector2(.5f,.5f),Vector2.zero,new Vector2(size-8,size-8));
            icon.art=art.gameObject.AddComponent<Image>();icon.art.preserveAspect=true;icon.art.raycastTarget=false;
            icon.fallback=Label(r,"◇",Vector2.zero,new Vector2(size,size),Mathf.RoundToInt(size*.5f));
            return icon;
        }
        private void ShowStationIcon(RaidBuildStationView.Icon icon,string id,string symbol,string tooltip,CharacterSlot? slot=null)
        {
            Sprite sprite=icon.overrideSprite!=null?icon.overrideSprite:string.IsNullOrEmpty(id)?null:RaidUIPresentation.Settings?.Icon(id);
            if(sprite==null&&slot.HasValue&&_stationArt!=null)
                sprite=Array.Find(_stationArt.slots,s=>!s.sharedResurrection&&s.slot==slot.Value)?.fallbackArt;
            icon.art.sprite=sprite;icon.art.enabled=sprite!=null;
            icon.fallback.text=symbol;icon.fallback.gameObject.SetActive(sprite==null);
            if(icon.tooltip!=tooltip){if(_stationHoverText==icon.tooltip)_stationHoverText=tooltip;icon.tooltip=tooltip;}
        }
        private void UpdateStationTooltip()
        {
            if(_stationTooltip==null)return;
            _stationTooltip.text=_stationHoverText??"아이콘에 마우스를 올려 상세 정보를 확인하세요. · 선택은 즉시 적용됩니다.";
        }
        public RaidBuildStationView CreateDefaultStationView(Transform parent)
        {
                _loadoutPanel=Rect("ConstellationBuild",parent,new Vector2(.5f,.5f),Vector2.zero,new Vector2(1320,760));
                _loadoutPanel.gameObject.AddComponent<Image>().color=new Color(.035f,.045f,.09f,.98f);
                _loadoutTitle=Label(_loadoutPanel,"✦ 여명의 성소 · 전투 준비 ✦",new Vector2(0,337),new Vector2(1250,42),24);
                _stationPartyTitle=Label(_loadoutPanel,"원정대",new Vector2(-430,281),new Vector2(390,34),20);
                _stationLocalHeading=Label(_loadoutPanel,"나의 전투 구성",new Vector2(38,290),new Vector2(470,34),20);
                _ownRouteIcon=MakeStationIcon(_loadoutPanel,new Vector2(38,246),36);
                Label(_loadoutPanel,"현재 스테이지",new Vector2(480,281),new Vector2(300,34),20);
                for(int i=0;i<4;i++)
                {
                    var row=Rect("PartyMember"+i,_loadoutPanel,new Vector2(.5f,.5f),new Vector2(-430,180-i*132),new Vector2(390,122));
                    row.gameObject.AddComponent<Image>().color=new Color(.075f,.095f,.16f);
                    _partyBuildRows[i]=Label(row,"",new Vector2(34,28),new Vector2(292,58),14);
                    _partyBuildRows[i].alignment=TextAnchor.MiddleLeft;
                    _partyIcons[i,0]=MakeStationIcon(row,new Vector2(-155,27),48);
                    for(int k=1;k<7;k++)_partyIcons[i,k]=MakeStationIcon(row,new Vector2(-155+(k-1)*60,-33),40);
                    _counterBadges[i]=Label(_partyIcons[i,6].root,"",new Vector2(10,-12),new Vector2(34,18),12);
                }
                for(int i=0;i<7;i++)
                {
                    var row=Rect("Choice"+i,_loadoutPanel,new Vector2(.5f,.5f),new Vector2(-100+(i%3)*140,157-(i/3)*146),new Vector2(128,132));
                    var bg=row.gameObject.AddComponent<Image>();bg.color=new Color(.075f,.095f,.16f);
                    var button=row.gameObject.AddComponent<Button>();button.targetGraphic=bg;_loadoutButtons[i]=button;
                    _choiceIcons[i]=MakeStationIcon(row,new Vector2(0,15),74);
                    _loadoutLabels[i]=Label(row,"",new Vector2(0,-46),new Vector2(124,24),13);
                }
                var stageCard=Rect("StageCard",_loadoutPanel,new Vector2(.5f,.5f),new Vector2(480,31),new Vector2(300,440));
                stageCard.gameObject.AddComponent<Image>().color=new Color(.075f,.095f,.16f);
                Label(stageCard,"✧",new Vector2(0,100),new Vector2(230,130),88).color=new Color(.85f,.73f,.43f);
                _stationStage=Label(stageCard,"",new Vector2(0,-48),new Vector2(268,234),20);
                _stationCounterSummary=Label(_loadoutPanel,"",new Vector2(-430,-310),new Vector2(390,46),14);
                _stationTooltip=Label(_loadoutPanel,"",new Vector2(50,-266),new Vector2(490,108),14);
                _stationTooltip.horizontalOverflow=HorizontalWrapMode.Wrap;
                _routeChangeButton=StationButton("루트 변경",30,null);
                _loadoutCloseButton=StationButton("닫기",480,null);

            var view=_loadoutPanel.gameObject.AddComponent<RaidBuildStationView>();
            view.title=_loadoutTitle;view.partyTitle=_stationPartyTitle;view.localHeading=_stationLocalHeading;
            view.counterSummary=_stationCounterSummary;view.stage=_stationStage;view.tooltip=_stationTooltip;
            view.ownRoute=_ownRouteIcon;view.routeChange=_routeChangeButton;view.close=_loadoutCloseButton;
            view.choices=new RaidBuildStationView.Choice[7];view.members=new RaidBuildStationView.Member[4];
            for(int i=0;i<7;i++)view.choices[i]=new RaidBuildStationView.Choice{button=_loadoutButtons[i],label=_loadoutLabels[i],icon=_choiceIcons[i]};
            for(int i=0;i<4;i++)
            {
                var icons=new RaidBuildStationView.Icon[7];for(int k=0;k<7;k++)icons[k]=_partyIcons[i,k];
                view.members[i]=new RaidBuildStationView.Member{label=_partyBuildRows[i],icons=icons,counterBadge=_counterBadges[i]};
            }
            view.ValidateBindings();
            return view;
        }
        private void BindStationHover(RaidBuildStationView.Icon icon)
        {
            var hover=icon.root.GetComponent<CharacterSlotHover>()??icon.root.gameObject.AddComponent<CharacterSlotHover>();
            hover.enter=()=>{_stationHoverText=icon.tooltip;UpdateStationTooltip();};
            hover.leave=()=>{_stationHoverText=null;UpdateStationTooltip();};
        }
        private void BindStationView(RaidBuildStationView view)
        {
            view.ValidateBindings();_stationView=view;_loadoutPanel=(RectTransform)view.transform;
            _stationDesignScale=_loadoutPanel.localScale;
            _loadoutTitle=view.title;_stationPartyTitle=view.partyTitle;_stationLocalHeading=view.localHeading;
            _stationCounterSummary=view.counterSummary;_stationStage=view.stage;_stationTooltip=view.tooltip;
            _ownRouteIcon=view.ownRoute;BindStationHover(_ownRouteIcon);
            _routeChangeButton=view.routeChange;_loadoutCloseButton=view.close;
            _routeChangeButton.onClick.AddListener(()=>{
                if(RaidLobbyRoom.CanUseBuildStation)AegisRouteSelectionUI.OpenCurrentRoutes(Actor(),RaidNetworkSession.Multiplayer);
                LocalGameplayInput.SuppressPointerThisFrame();
            });
            _loadoutCloseButton.onClick.AddListener(CloseStationLoadout);
            for(int i=0;i<7;i++)
            {
                int index=i;_loadoutButtons[i]=view.choices[i].button;_loadoutLabels[i]=view.choices[i].label;
                _choiceIcons[i]=view.choices[i].icon;BindStationHover(_choiceIcons[i]);
                _loadoutButtons[i].onClick.AddListener(()=>CycleLoadout(index));
            }
            for(int i=0;i<4;i++)
            {
                _partyBuildRows[i]=view.members[i].label;_counterBadges[i]=view.members[i].counterBadge;
                for(int k=0;k<7;k++){_partyIcons[i,k]=view.members[i].icons[k];BindStationHover(_partyIcons[i,k]);}
            }
        }
        private void EnsureStationLayout(int rows)
        {
            if(!_stationArtResolved){_stationArt=FindFirstObjectByType<AstellaOrnateHUD>();_stationArtResolved=true;}
            if(_stationView==null)
            {
                var prefab=Resources.Load<RaidBuildStationView>("UI/ConstellationBuild");
                var parent=GetComponentInParent<Canvas>().transform;
                BindStationView(prefab!=null?Instantiate(prefab,parent,false):CreateDefaultStationView(parent));
            }
            if(_stationView.fitToCanvas)
            {
                var available=((RectTransform)_loadoutPanel.parent).rect.size;
                var size=_loadoutPanel.rect.size;
                float factor=Mathf.Max(.1f,Mathf.Min(1,(available.x-32)/Mathf.Max(1,size.x*Mathf.Abs(_stationDesignScale.x)),(available.y-32)/Mathf.Max(1,size.y*Mathf.Abs(_stationDesignScale.y))));
                _loadoutPanel.localScale=_stationDesignScale*factor;
            }
            for(int i=0;i<7;i++)_loadoutButtons[i].gameObject.SetActive(i<rows);
            UpdateStationTooltip();
        }
        private void SetStationChoice(int index,string id,string category,string name,string description,CharacterSlot slot)
        {
            _loadoutLabels[index].text=category;
            ShowStationIcon(_choiceIcons[index],id,slot==0?"✦":"◇",category+" · "+(name??"미선택")+"\n"+description+"\n클릭: 다음 장착 가능 항목",slot==0?(CharacterSlot?)null:slot);
            UpdateStationTooltip();
        }
        private void RefreshStationStage()
        {
            var stage=StageManager.Instance;
            var actor=Actor();
            if(actor!=null)
            {
                var route=Array.Find(actor.Definition.routes,r=>r.id==actor.RouteId);
                _stationLocalHeading.text="나의 전투 구성 · "+actor.Definition.name;
                ShowStationIcon(_ownRouteIcon,route?.id,"✦","기원 · "+(route?.name??"미선택"),CharacterSlot.Identity);
            }
            int number=stage!=null?stage.currentStage:1;
            string difficulty=CampaignRuntime.Instance?.DifficultyId;
            _stationStage.text="STAGE "+number.ToString("00")+" / "+(stage!=null?stage.maxStage:6).ToString("00")+
                "\n\n"+(stage?.boss!=null?stage.boss.currentBossTitle:"스테이지 정보 대기")+
                "\n\n"+(difficulty==CampaignCatalog.Hard?"하드":difficulty==CampaignCatalog.Normal?"노말":"난이도 확인 중");
        }
        private void RefreshPartyBuildOverview(CharacterActor localActor)
        {
            var members=PartyLife.OrderedMembers();
            int count=Mathf.Min(4,members.Count),counters=0,humans=0,ready=0;
            for(int i=0;i<4;i++)
            {
                var text=_partyBuildRows[i];bool occupied=i<count&&members[i]!=null;
                for(int k=0;k<7;k++)_partyIcons[i,k].root.gameObject.SetActive(occupied);
                if(!occupied){text.text="빈자리 · 접속 대기";continue;}
                var member=members[i];var avatar=member.GetComponent<NetworkRaidAvatar>();var bot=member.GetComponent<PartyBotAI>();
                if(bot!=null&&(avatar==null||avatar.IsBot))
                {
                    text.text="AI 역할 동료\n"+CombatMetricsManager.RoleName((int)bot.role+1);
                    ShowStationIcon(_partyIcons[i,0],null,"◇","역할 AI · 개별 직업 빌드 없음");
                    for(int k=1;k<7;k++)_partyIcons[i,k].root.gameObject.SetActive(false);
                    continue;
                }
                var actor=member.GetComponent<CharacterActor>();var state=actor!=null?actor.Snapshot:avatar?.State?.character;
                var definition=actor?.Definition??CharacterCatalog.Find(state?.jobId);var build=RaidPartyBuildSummary.Read(definition,state);
                bool local=avatar!=null?avatar.IsLocal:member==localActor.transform;
                if(avatar?.Automated!=true){humans++;if(avatar?.Ready==true)ready++;}
                if(build.CounterEquipped)counters++;
                string name=RaidRoster.CleanName(avatar!=null?avatar.DisplayName:local?"나":member.name);
                text.text=build.Job+"\n"+name+(local?" · 나":"")+"\n"+(avatar?.Automated==true?"AI 대체":avatar!=null&&!avatar.Loaded?"로딩 중":avatar?.Ready==true?"<color=#7AE1D5>준비 완료</color>":"정비 중");
                string symbol=definition?.role==CharacterRole.Tank?"◇":definition?.role==CharacterRole.Healer?"✚":"⚔";
                ShowStationIcon(_partyIcons[i,0],definition?.id,symbol,build.Job+" · "+build.Role);
                ShowStationIcon(_partyIcons[i,1],state?.routeId,"✦","기원 · "+build.Route+"\n핵심 특성 · "+build.Core,CharacterSlot.Identity);
                for(int k=0;k<StationSlots.Length;k++)
                {
                    var slot=StationSlots[k];var selection=state?.selections?.FirstOrDefault(s=>s.slot==slot);
                    var skill=definition?.Skill(selection?.skillId);
                    ShowStationIcon(_partyIcons[i,k+2],skill?.id,slot==CharacterSlot.Counter?"C":"◇",(skill?.name??"미선택")+"\n"+skill?.description+(slot==CharacterSlot.Counter?"\n"+build.Counter:""),slot);
                    if(slot==CharacterSlot.Counter)
                    {
                        _partyIcons[i,k+2].root.GetComponent<Image>().color=build.CounterEquipped?_stationView.counterEquippedColor:_stationView.counterMissingColor;
                        _counterBadges[i].text=build.CounterEquipped?"<color=#7AE1D5>C✓</color>":"C–";
                    }
                }
            }
            _stationPartyTitle.text="원정대 "+count+" / 4";
            _stationCounterSummary.text="카운터 장착 "+counters+"명"+(RaidNetworkSession.Multiplayer?" · 준비 "+ready+" / "+humans:"");
            UpdateStationTooltip();
        }
    }
}
