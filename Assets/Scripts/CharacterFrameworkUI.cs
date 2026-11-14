using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace RaidRogue
{
    // Extra common slots share the existing HUD canvas/input blockers. Existing 1/2/3/Q + movement/potion stay intact.
    public sealed partial class CharacterFrameworkUI : MonoBehaviour
    {
        private SkillHotbarUI _hotbar;private RectTransform _root;private Text _resources, _deferred, _identity, _stats;
        private AstellaOrnateHUD _authoredResourceHUD;
        private readonly Dictionary<CharacterSlot,Text> _labels=new Dictionary<CharacterSlot,Text>();
        private readonly Dictionary<CharacterSlot,Button> _buttons=new Dictionary<CharacterSlot,Button>();
        private readonly Dictionary<CharacterSlot,HotbarInputFeedback> _inputFeedback=new Dictionary<CharacterSlot,HotbarInputFeedback>();
        private readonly HashSet<CharacterSlot> _layoutHidden=new HashSet<CharacterSlot>();
        private readonly Dictionary<string,Image> _gauges=new Dictionary<string,Image>();
        private string _gaugeJob; private readonly Dictionary<string,float> _lastValues=new Dictionary<string,float>(); private float _pulseUntil;
        private float _next;
        private Text _tooltip;private CharacterSlot? _hover;
        private RectTransform _loadoutPanel;
        private readonly Button[] _loadoutButtons=new Button[7];
        private readonly Text[] _loadoutLabels=new Text[7];
        private Text _loadoutTitle;
        private bool _stationRequested;
        private Button _routeChangeButton, _loadoutCloseButton;
        private Text _stationPartyTitle, _stationLocalHeading, _stationCounterSummary;
        private readonly Text[] _partyBuildRows = new Text[4];
        public bool OpenLoadoutAtStation()
        {
            var actor = Actor();
            if (!RaidLobbyRoom.CanUseBuildStation || actor == null || !actor.CanChangeLoadout || actor.Definition.loadouts.Length == 0) return false;
            _stationRequested = true; _next = 0;
            RefreshLoadout(actor);
            LocalInputBindings.QuarantineHeld(); LocalGameplayInput.SuppressPointerThisFrame();
            return true;
        }
        private void CloseStationLoadout()
        {
            _stationRequested = false;
            if (_loadoutPanel != null) _loadoutPanel.gameObject.SetActive(false);
            LocalInputBindings.QuarantineHeld(); LocalGameplayInput.SuppressPointerThisFrame();
        }
        private void RefreshLoadout(CharacterActor actor)
        {
            if (!RaidLobbyRoom.CanUseBuildStation) _stationRequested = false;
            bool visible=_stationRequested&&actor.Definition.loadouts.Length>0&&actor.CanChangeLoadout&&!AegisRouteSelectionUI.IsOpen&&!LocalCameraSettingsUI.IsOpen;
            if(_loadoutPanel!=null)_loadoutPanel.gameObject.SetActive(visible);
            if(!visible)return;
            var rules=LoadoutRules(actor);
            var state=actor.Snapshot;
            int rows=rules.Length+2;
            EnsureStationLayout(rows);
            RefreshStationStage();
            for(int i=0;i<rules.Length;i++)
            {
                _loadoutButtons[i].gameObject.SetActive(i<rules.Length);if(i>=rules.Length)continue;
                var rule=rules[i];bool allowed=true;
                string selected=(int)rule.slot==0?state.coreTrait:actor.Selected(rule.slot)?.id;
                var skill=actor.Definition.Skill(selected);
                var blocked=rule.choices.Where(c=>actor.RuntimeState.BuildChoiceFailure(rule.id,c).Length>0).Select(c=>actor.Definition.Skill(c)?.name);
                string unavailable=string.Join(", ",blocked);
                SetStationChoice(i,selected,rule.name,skill?.name,skill?.description+(unavailable.Length>0?"\n장착 불가: "+unavailable:""),rule.slot);
                _loadoutButtons[i].interactable=allowed&&!CombatMetricsUI.BlocksUnderlyingInput&&RaidSteamRuntime.Instance?.OverlayOpen!=true;
            }
            for(int i=0;i<2;i++)
            {
                var selected=state.auxiliaryTraits;
                int row=rules.Length+i;bool present=selected.Length==2;_loadoutButtons[row].gameObject.SetActive(present);if(!present)continue;
                var trait=actor.Definition.auxiliaryTraits.FirstOrDefault(t=>t.id==selected[i]);
                string unavailable=string.Join(", ",actor.Definition.auxiliaryTraits.Where(t=>actor.RuntimeState.BuildChoiceFailure("auxiliary."+(i+1),t.id).Length>0).Select(t=>t.name));
                SetStationChoice(row,trait?.id,"보조 "+(i+1),trait?.name,trait?.description+(unavailable.Length>0?"\n장착 불가: "+unavailable:""),CharacterSlot.Active);
                _loadoutButtons[row].interactable=!CombatMetricsUI.BlocksUnderlyingInput&&RaidSteamRuntime.Instance?.OverlayOpen!=true;
            }
            _routeChangeButton.interactable=AegisRouteSelectionUI.CanEditCurrentRoute(actor,RaidNetworkSession.Multiplayer)&&!CombatMetricsUI.BlocksUnderlyingInput&&RaidSteamRuntime.Instance?.OverlayOpen!=true;
            _routeChangeButton.GetComponentInChildren<Text>().text=_routeChangeButton.interactable?"루트 변경":"루트 · 출발 후 고정";
            _routeChangeButton.gameObject.SetActive(true); _loadoutCloseButton.gameObject.SetActive(true);
            RefreshPartyBuildOverview(actor);
            RaidUIOrder.Raise(_loadoutPanel);
        }
        private Button StationButton(string text, float x, UnityEngine.Events.UnityAction action)
        {
            var rect=Rect("StationAction",_loadoutPanel,new Vector2(.5f,0),new Vector2(x,26),new Vector2(170,36));
            var background=rect.gameObject.AddComponent<Image>(); background.color=new Color(.12f,.19f,.26f);
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=background;if(action!=null)button.onClick.AddListener(action);
            Label(rect,text,Vector2.zero,new Vector2(164,32),14);
            return button;
        }
        private void CycleLoadout(int index)
        {
            var actor=Actor();if(actor==null||!actor.CanChangeLoadout||!_stationRequested||!RaidLobbyRoom.CanUseBuildStation)return;
            var rules=LoadoutRules(actor);
            if(index>=rules.Length)
            {
                int slot=index-rules.Length;var selected=actor.Snapshot.auxiliaryTraits;if(slot>1||selected.Length!=2)return;
                var choices=actor.Definition.auxiliaryTraits.Where(t=>t.id!=selected[1-slot]&&actor.RuntimeState.BuildChoiceFailure("auxiliary."+(slot+1),t.id).Length==0).ToArray();
                if(choices.Length==0)return;
                int nextAux=(System.Array.FindIndex(choices,t=>t.id==selected[slot])+1)%choices.Length;
                actor.RequestChoice("auxiliary."+(slot+1),choices[nextAux].id);LocalGameplayInput.SuppressPointerThisFrame();return;
            }
            if(index>=rules.Length)return;
            var rule=rules[index];string current=(int)rule.slot==0?actor.Snapshot.coreTrait:actor.Selected(rule.slot)?.id;
            var validChoices=rule.choices.Where(c=>actor.RuntimeState.BuildChoiceFailure(rule.id,c).Length==0).ToArray();
            if(validChoices.Length==0)return;
            int next=(System.Array.IndexOf(validChoices,current)+1)%validChoices.Length;
            actor.RequestChoice(rule.id,validChoices[next]);LocalGameplayInput.SuppressPointerThisFrame();
        }
        private static CharacterLoadoutDefinition[] LoadoutRules(CharacterActor actor)=>actor.Definition.loadouts.Where(r=>string.IsNullOrEmpty(r.routeId)||r.routeId==actor.RouteId).ToArray();
        private void OnDestroy(){if(_loadoutPanel!=null)Destroy(_loadoutPanel.gameObject);}
        private void Start()
        {
            _hotbar=GetComponent<SkillHotbarUI>();
            _authoredResourceHUD=GetComponentInParent<Canvas>()?.GetComponentInChildren<AstellaOrnateHUD>(true);
            if (RaidUIView.Attach(this,"character",transform)) { BindSlots(); return; }
            var viewParent=RaidUIView.Begin(this,"character",transform);
            _root=Rect("CharacterSlots",viewParent,new Vector2(.5f,1),new Vector2(0,72),new Vector2(700,70));
            var bg=_root.gameObject.AddComponent<Image>();RaidUIStyle.Color(bg, new Color(.025f,.045f,.07f,.92f));
            var extra=new[]{CharacterSlot.Counter,CharacterSlot.Job2,CharacterSlot.Identity,CharacterSlot.Active,CharacterSlot.OffGcd2,CharacterSlot.Ultimate};
            for(int i=0;i<extra.Length;i++)
            {
                var slot=extra[i];var r=Rect(slot.ToString(),_root,new Vector2(.5f,.5f),new Vector2(-290+i*116,0),new Vector2(110,62));
                var image=r.gameObject.AddComponent<Image>();RaidUIStyle.Color(image, new Color(.12f,.17f,.22f,.95f));
                var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;RaidUIView.Bind(this,slot.ToString(),()=>Actor()?.RequestSlot(slot),button);
                var hover=r.gameObject.AddComponent<CharacterSlotHover>();hover.enter=()=>_hover=slot;hover.leave=()=>_hover=null;
                _buttons[slot]=button;_labels[slot]=Label(r,"",Vector2.zero,new Vector2(106,60),12);
            }
            _tooltip=Label(_root,"",new Vector2(0,260),new Vector2(900,100),16);
            _tooltip.gameObject.AddComponent<CanvasGroup>().blocksRaycasts=false;
            _resources=Label(_root,"",new Vector2(0,49),new Vector2(850,28),13);
            _deferred=Label(_root,"",new Vector2(0,95),new Vector2(900,24),13);
            _identity=Label(_root,"",new Vector2(0,134),new Vector2(900,54),14);
            _stats=Label(_root,"",new Vector2(0,185),new Vector2(1000,44),12);
        }
        private void BindSlots()
        {
            foreach(var pair in _buttons)
            {
                var slot=pair.Key;
                RaidUIView.Bind(this,slot.ToString(),()=>Actor()?.RequestSlot(slot));
                var hover=pair.Value.GetComponent<CharacterSlotHover>() ?? pair.Value.gameObject.AddComponent<CharacterSlotHover>();
                hover.enter=()=>_hover=slot;hover.leave=()=>_hover=null;
            }
        }
        private CharacterActor Actor()=>_hotbar?.combatController!=null?CharacterActor.Ensure(_hotbar.combatController.gameObject):null;
        private void Update()
        {
            // Poll every frame, independently of the slower resource/tooltip refresh.
            var inputActor=Actor();
            foreach(var entry in _buttons)
            {
                if(!_inputFeedback.TryGetValue(entry.Key,out var feedback))
                    _inputFeedback[entry.Key]=feedback=new HotbarInputFeedback();
                feedback.Tick(entry.Value,inputActor,entry.Key);
            }
            if(Time.unscaledTime<_next)return;_next=Time.unscaledTime+.25f;
            var actor=Actor();if(actor==null)return;
            RefreshLoadout(actor);
            var state=actor.Snapshot;if(state==null)return;
            var damage=actor.GetComponent<NineKeyDamageKit>();
            float ShownValue(JobResourceValue resource)=>damage?.Active==true?damage.DisplayResource(resource.id,resource.value):resource.value;
            float ShownMaximum(string id)=>damage?.Active==true?damage.DisplayMaximum(id,actor.ResourceMaximum(id)):actor.ResourceMaximum(id);
            var tankJob=NineKeyTankKit.Supports(actor.Definition)?CharacterRosterData.Find(actor.Definition.id):null;
            var visibleResources=actor.Definition.resources.Where(r=>tankJob?.index!=2||r.id!=NineKeyTankData.Heat(tankJob)||actor.RouteId==actor.Definition.routes[2].id).ToArray();
            string gaugeKey=actor.Definition.id+(tankJob?.index==2?"|"+actor.RouteId:"");
            if(_gaugeJob!=gaugeKey)
            {
                foreach(var fill in _gauges.Values)if(fill!=null)Destroy(fill.transform.parent.gameObject);_gauges.Clear();_lastValues.Clear();_gaugeJob=gaugeKey;
                for(int i=0;i<visibleResources.Length;i++)
                {
                    var d=visibleResources[i];
                    var template=RaidUIRepeatedItem.Create(RaidUIPresentation.Settings?.resourceTemplate,_root);
                    if(template!=null && template.fill!=null)
                    {
                        var layout=template.GetComponent<RectTransform>();layout.anchoredPosition+=new Vector2(0,i*layout.rect.height);
                        if(template.label!=null)template.label.text=d.name;
                        _gauges[d.id]=template.fill;continue;
                    }
                    var bar=Rect("Resource_"+d.id,_root,new Vector2(.5f,1),new Vector2(0,38+i*9),new Vector2(420,5));
                    bar.gameObject.AddComponent<Image>().color=new Color(.12f,.15f,.18f,.9f);
                    var fill=Rect("Fill",bar,Vector2.zero,Vector2.zero,Vector2.zero);fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;
                    var image=fill.gameObject.AddComponent<Image>();RaidUIStyle.Color(image, new Color(.25f,.8f,.75f,.8f));image.raycastTarget=false;_gauges[d.id]=image;
                }
            }
            foreach(var r in state.resources)if(_gauges.TryGetValue(r.id,out var fill))
            {
                // The illustrated HUD owns resource presentation. Keep legacy gauges for
                // the fallback HUD, rather than drawing a second bar under the new layout.
                var legacyGauge=fill.GetComponentInParent<RaidUIRepeatedItem>()?.gameObject??fill.transform.parent.gameObject;
                bool showLegacy=_authoredResourceHUD==null||!_authoredResourceHUD.isActiveAndEnabled;
                if(legacyGauge.activeSelf!=showLegacy)legacyGauge.SetActive(showLegacy);
                var d=System.Array.Find(actor.Definition.resources,x=>x.id==r.id);
                float value=ShownValue(r),maximum=ShownMaximum(r.id);
                if(_lastValues.TryGetValue(r.id,out float previous)&&value>previous)_pulseUntil=Time.unscaledTime+.6f;
                _lastValues[r.id]=value;if(fill.type==Image.Type.Filled)fill.fillAmount=Mathf.Clamp01(value/maximum);else fill.rectTransform.anchorMax=new Vector2(Mathf.Clamp01(value/maximum),1);
                RaidUIStyle.Color(fill, Time.unscaledTime<_pulseUntil?Color.white:value>=maximum?new Color(1,.85f,.2f):new Color(.85f,.73f,.3f));
            }
            var resourceText=new List<string>();
            foreach(var r in state.resources.Where(r=>visibleResources.Any(d=>d.id==r.id))){var d=System.Array.Find(actor.Definition.resources,x=>x.id==r.id);resourceText.Add(d.name+" "+ShownValue(r).ToString(d.integer?"0":"0.#")+" / "+ShownMaximum(r.id).ToString("0.#")+(ShownValue(r)>=ShownMaximum(r.id)?" MAX":""));}
            _resources.text=actor.Definition.name+" · "+(resourceText.Count==0?"직업 전용 자원 없음":string.Join("   ",resourceText));
            var aegis=actor.GetComponent<AegisPaladin>();
            _deferred.text=aegis!=null&&aegis.IsActive?"남은 지연 피해 "+aegis.DamageState.Pending.ToString("0.#")+(aegis.DamageState.FaithRemaining>0?" · 불굴의 신념 "+aegis.DamageState.FaithRemaining.ToString("0.0")+"초":""):"";
            string extensionStatus=actor.GetComponent<AegisExtensions>()?.StatusText()??"";
            if(aegis!=null&&aegis.IsActive&&CharacterSlots.IsNineKey(actor.Definition))
                _deferred.text=aegis.DamageState.FaithRemaining>0?$"불굴의 신념 · 피해 감소 {AegisPaladinData.NineKeyFaithReduction*100:0}% · {aegis.DamageState.FaithRemaining:0.0}초":"";
            if(extensionStatus.Length>0)_deferred.text+=" · "+extensionStatus;
            var statusEffects=actor.GetComponent<CombatStatusEffects>();
            if(statusEffects?.MorningActive==true)_deferred.text+=$" · 아침의 성역 {statusEffects.MorningEnd-RaidCombatAuthority.ServerTime:0.0}초 (공격력 +10% / 피해 -20%)";
            if(statusEffects?.CombatInvulnerable==true)_deferred.text+=$" · 엑스칼리버 무적 {statusEffects.InvulnerabilityEnd-RaidCombatAuthority.ServerTime:0.0}초";
            _identity.text="";
            _stats.text="";
            if(aegis!=null&&aegis.IsActive)
            {
                var identity=actor.Selected(CharacterSlot.Identity);
                string effect=AegisRouteData.EffectSummary(actor.RouteId);
                _identity.text=aegis.RouteName+" · "+AegisRouteData.PassiveSummary(actor.RouteId)+"\n["+LocalInputBindings.Label(CharacterSlots.Binding(actor.Definition,CharacterSlot.Identity))+"] "+identity.name+" · "+effect;
                if(aegis.NewSun)
                {
                    var sun=aegis.DamageState.Sun;
                    string recorded=state.combo2>1&&sun.SecondRecorded?(sun.SecondShield?"방패":"검"):"미시작";
                    _identity.text="태양의 집행관 · "+(sun.Shield?"방패":"검")+" 태세 · ["+LocalInputBindings.Label(CharacterSlots.Binding(actor.Definition,CharacterSlot.Identity))+"] 전환\n2 시작 태세: "+recorded+" · "+(actor.Resource(AegisPaladinData.Holy)>=aegis.SecondCost?"2 강화 가능 ("+aegis.SecondCost+")":"2 기본 효과만 (성광 부족)");
                    _deferred.text+=$" · 협공 {sun.MarkRights}명 / {sun.MarkRemaining:0.0}초";
                }
                if(aegis.NewBulwark)
                {
                    var glyphs=aegis.DamageState.Glyphs.Glyphs;
                    _identity.text="불멸의 성벽 · ["+LocalInputBindings.Label(CharacterSlots.Binding(actor.Definition,CharacterSlot.Identity))+"] 커서 지상 위치 설치\n사용 가능 "+(aegis.GlyphCapacity-glyphs.Count)+" / "+aegis.GlyphCapacity+" · 성광 40 · 20m";
                    for(int i=0;i<glyphs.Count;i++)_identity.text+=" · 문양"+(i+1)+" "+glyphs[i].remaining.ToString("0.0")+"초";
                    if(aegis.Patrol)_identity.text+=" · R 길게: 미사용 문양 회수 (+20)";
                    if(aegis.IdentityBlocked)_deferred.text+=" · 문양 최대 수: 만료/회수 후 설치 가능";
                }
                if(aegis.NewMirror)
                {
                    var mirror=aegis.DamageState.Mirror;
                    _identity.text="응보의 거울방패 · ["+LocalInputBindings.Label(CharacterSlots.Binding(actor.Definition,CharacterSlot.Identity))+"] 자신/선택 동료 수호 · 성광 30\n방어 "+mirror.Remaining.ToString("0.0")+"초 · 정밀 "+mirror.Precise.ToString("0.0")+"초 · 응보 "+mirror.Count+" / "+aegis.ResponseCapacity+" · 1 완주 소비";
                    if(actor.Snapshot.coreTrait==AegisPaladinData.Flash&&mirror.Count>0)_identity.text+=" · 강화 남은 시간 "+Mathf.Max(0,4-mirror.FirstAge).ToString("0.0")+"초";
                }
                if(CharacterSlots.IsNineKey(actor.Definition))_deferred.text+=" · F 인계 보스: "+aegis.ThreatBossLabel+" · V 전용 게이지 100";
                if(!aegis.NewSun&&aegis.DamageState.Route.Transformation>0)_deferred.text+=" · 쌍성검 "+aegis.DamageState.Route.Transformation.ToString("0.0")+"초";
                if(aegis.DamageState.Route.Guard>0)_deferred.text+=" · 가드 "+aegis.DamageState.Route.Guard.ToString("0.0")+"초";
                var player=actor.GetComponent<PlayerController>();
                if(player!=null)
                {
                    var movement=actor.MovementSettings(player.dashSettings);
                    var jobMovement=actor.Selected(CharacterSlot.Movement).movement;
                    _stats.text=$"중갑의 무게: 기본 이동 ×{actor.Definition.moveSpeedMultiplier*100:0}% / 대쉬 거리 ×{jobMovement.commonDistanceMultiplier*100:0}% · 최종 이동 {player.NetworkMoveSpeed:0.##}m/s · [{LocalInputBindings.Label("Dash")}] {movement.distance:0.##}m / {movement.maximumCharges}충전\n"+
                        $"최대 HP {player.maxHp:0} · 물리 방어 {aegis.PhysicalDefense:0} / 마법 방어 {aegis.MagicDefense:0} · 강인함 {aegis.Tenacity:0} · 기본 공격 사거리 {actor.PrimaryRange:0.#}m";
                }
            }
            var kit=actor.GetComponent<CharacterKit>();
            var tank=actor.GetComponent<NineKeyTankKit>();
            var healer=actor.GetComponent<NineKeyHealerKit>();
            if(damage?.Active==true)
            {
                var route=System.Array.Find(actor.Definition.routes,r=>r.id==actor.RouteId);
                var identity=actor.Selected(CharacterSlot.Identity);
                _identity.text=(route?.name??"루트 선택 필요")+" · ["+LocalInputBindings.Label(CharacterSlots.Binding(actor.Definition,CharacterSlot.Identity))+"] "+identity?.name+"\n"+identity?.description;
                _deferred.text=damage.StatusText();
                _stats.text=actor.Definition.description+" · 4: "+actor.Selected(CharacterSlot.Counter)?.name+" · F: "+actor.Selected(CharacterSlot.OffGcd2)?.name;
            }
            if(healer?.Active==true)
            {
                var route=System.Array.Find(actor.Definition.routes,r=>r.id==actor.RouteId);
                var identity=actor.Selected(CharacterSlot.Identity);
                _identity.text=(route?.name??"루트 선택 필요")+" · R "+identity.name+"\n"+identity.description;
                _deferred.text=healer.StatusText();
                _stats.text=actor.Definition.description+" · 2 매 입력 치유 / Q 긴급 회복·정화 / F 광역 회복";
            }
            if(tank?.Active==true)
            {
                var route=System.Array.Find(actor.Definition.routes,r=>r.id==actor.RouteId);
                var identity=actor.Selected(CharacterSlot.Identity);
                _identity.text=(route?.name??"루트 선택 필요")+" · ["+LocalInputBindings.Label(CharacterSlots.Binding(actor.Definition,CharacterSlot.Identity))+"] "+identity.name+"\n"+identity.description;
                _deferred.text=tank.StatusText();
                _stats.text=actor.Definition.description+" · 기본 사거리 "+actor.PrimaryRange.ToString("0.#")+"m · 물리/마법 방어 "+actor.Definition.physicalDefense.ToString("0");
            }
            if(kit?.Active==true)
            {
                var route=System.Array.Find(actor.Definition.routes,r=>r.id==actor.RouteId);var identity=actor.Selected(CharacterSlot.Identity);
                _identity.text=(route?.name??"기원 선택 필요")+" · ["+LocalInputBindings.Label("Identity")+"] "+identity.name+"\n"+(kit.Route>0?kit.Job.passives[kit.Route-1]:"");
                _stats.text=actor.Definition.description+" · 사거리 "+actor.PrimaryRange.ToString("0.#")+"m · 방어 "+kit.Defense.ToString("0")+" · HP "+(kit.GetComponent<PlayerController>()?.maxHp??0).ToString("0")+" · 이동 "+(kit.GetComponent<PlayerController>()?.moveSpeed??0).ToString("0.##")+"m/s";
                _deferred.text=kit.StatusText();if(kit.Job.index==4){string notes="";for(int i=2;i>=0;i--){int note=(kit.State.Data.notes>>(i*2))&3;notes+=note==1?" ♩":note==2?" ♪":note==3?" ♫":" ·";}_resources.text+=notes;}
            }
            float immunity=actor.GetComponent<CombatStatusEffects>()?.KnockbackImmunity??0;
            if(immunity>0)_deferred.text+=" · 성채 보호 "+immunity.ToString("0.0")+"초";
            if((actor.GetComponent<CombatStatusEffects>()?.ControlImmunity??0)>0)_deferred.text+=" · 피격 이상 면역";
            _tooltip.gameObject.SetActive(_hover.HasValue&&!LocalGameplayInput.Blocked);
            foreach(var slot in _labels.Keys)
            {
                bool enabled=CharacterSlots.Enabled(actor.Definition,slot);
                if(!enabled)
                {
                    if(_buttons[slot].gameObject.activeSelf){_layoutHidden.Add(slot);_buttons[slot].gameObject.SetActive(false);}
                    continue;
                }
                if(_layoutHidden.Remove(slot))_buttons[slot].gameObject.SetActive(true);
                var selection=System.Array.Find(state.selections,s=>s.slot==slot);var skill=selection==null?null:actor.Definition.Skill(selection.skillId);
                _buttons[slot].GetComponent<RaidUIIcon>()?.Show(skill?.id);
                bool unlocked=skill!=null&&System.Array.IndexOf(state.unlocked,skill.id)>=0;bool ready=unlocked&&skill.implemented;
                var timing=skill==null?null:System.Array.Find(state.skills,s=>s.id==skill.id);
                bool alive=CombatRange.LivingPartyMember(actor.transform);
                var damageKit=actor.GetComponent<NineKeyDamageKit>();
                float costScale=skill==null?1:damageKit?.Active==true?damageKit.CostScale(skill):kit?.Active==true?kit.CostScale(skill):1;
                bool affordable=skill!=null&&System.Array.TrueForAll(skill.costs,c=>(System.Array.Find(state.resources,r=>r.id==c.resourceId)?.value??0)>=c.amount*costScale);
                string blocked=skill?.handlerId=="aegis.extension"?actor.GetComponent<AegisExtensions>()?.Unavailable(skill,TargetingManager.Instance?.CurrentTarget)??"":"";
                if(kit?.Active==true&&skill!=null)blocked=kit.Unavailable(skill,TargetingManager.Instance?.CurrentTarget);
                if(tank?.Active==true&&skill!=null)blocked=tank.Unavailable(slot);
                if(healer?.Active==true&&skill!=null)blocked=healer.Unavailable(slot);
                if(damageKit?.Active==true&&skill!=null)blocked=DamageSlotUnavailable(actor,skill,slot);
                if(damageKit?.Active!=true&&skill!=null&&(skill.target==SkillTargetRule.LivingEnemy||skill.target==SkillTargetRule.LivingAlly))
                {
                    var target=TargetingManager.Instance?.CurrentTarget;
                    if(healer?.Active==true)
                    {
                        var targeting=TargetingManager.Instance;
                        target=skill.target==SkillTargetRule.LivingEnemy?targeting?.AttackTarget:targeting!=null&&targeting.HasSupportSelection?targeting.SupportTarget:actor.transform;
                    }
                    if(!(skill.target==SkillTargetRule.LivingEnemy?CombatRange.LivingEnemy(target):CombatRange.LivingPartyMember(target)))blocked="유효한 대상 필요";
                    else if(!CombatRange.InRange(actor.transform,target,actor.EffectiveRange(skill)))blocked="사거리 밖";
                }
                string status=!unlocked?actor.SlotUnlockStatus(slot):skill!=null&&!skill.implemented?"미구현":!alive?"사망":timing!=null&&timing.charges==0?timing.recharge.ToString("0.0")+"초":!affordable?"자원 부족":slot==CharacterSlot.Identity&&aegis?.IdentityBlocked==true?"효과 진행 중":blocked.Length>0?blocked:timing!=null&&actor.MaximumCharges(skill.id)>1?timing.charges+" / "+actor.MaximumCharges(skill.id):"사용 가능";
                _labels[slot].text="["+LocalInputBindings.Label(CharacterSlots.Binding(actor.Definition,slot))+"] "+(ready?skill.name:CharacterSlots.Name(slot))+"\n"+status+((slot==CharacterSlot.Identity||slot==CharacterSlot.Ultimate)&&ready&&skill.costs.Length>0?" · 비용 "+skill.costs[0].amount.ToString("0")+" / CD "+skill.cooldown.ToString("0.#")+"초":"");
                if (!RaidUIStyle.Authored(_labels[slot])) { _labels[slot].resizeTextForBestFit=true;_labels[slot].resizeTextMinSize=9;_labels[slot].resizeTextMaxSize=12; }
                _buttons[slot].interactable=alive&&ready&&affordable&&timing!=null&&timing.charges>0&&blocked.Length==0&&!(slot==CharacterSlot.Identity&&aegis?.IdentityBlocked==true)&&!LocalGameplayInput.Blocked;
                if(_hover==slot)
                {
                    string adjustment="";
                    if(aegis!=null&&CharacterSlots.IsNineKey(actor.Definition))
                    {
                        if(slot==CharacterSlot.Job1&&actor.HasAuxiliary(AegisPaladinData.Steadfast))adjustment="\n보조 적용: Q 지속8초 / 재사용30초";
                        if(slot==CharacterSlot.OffGcd2&&actor.HasAuxiliary(AegisPaladinData.FarCall))adjustment="\n보조 적용: 도발·인계 사거리 "+aegis.ThreatRange.ToString("0.#")+"m";
                        if(slot==CharacterSlot.Ultimate&&actor.HasAuxiliary(AegisPaladinData.LastingSanctuary))adjustment="\n보조 적용: V 보호막 지속10초";
                    }
                    _tooltip.text=ready?$"[{LocalInputBindings.Label(CharacterSlots.Binding(actor.Definition,slot))}] {skill.name}\n{skill.description}{adjustment}\n{status}":CharacterSlots.Name(slot)+" · "+status;
                }

            }
        }
        // Both hotbars use the same target routing as RequestSlot. A friendly
        // selection must not hide a remembered attack target, and a lost explicit
        // support target must not silently become self protection.
        public static string DamageSlotUnavailable(CharacterActor actor,CharacterSkillDefinition skill,CharacterSlot slot)
        {
            var damage=actor?.GetComponent<NineKeyDamageKit>();
            if(damage?.Active!=true||skill==null)return "";
            string blocked=damage.Unavailable(slot);if(blocked.Length>0)return blocked;
            if(skill.target!=SkillTargetRule.LivingEnemy&&skill.target!=SkillTargetRule.LivingAlly)return "";
            var targeting=TargetingManager.Instance;Transform target;
            if(targeting!=null)
            {
                if(!targeting.TryResolveSkillTarget(skill.target,actor.transform,StageManager.Instance?.boss,out target,out blocked))return blocked;
            }
            else target=skill.target==SkillTargetRule.LivingAlly?actor.transform:null;
            if(!(skill.target==SkillTargetRule.LivingEnemy?CombatRange.LivingEnemy(target):CombatRange.LivingPartyMember(target)))return "유효한 대상 필요";
            return CombatRange.InRange(actor.transform,target,actor.EffectiveRange(skill))?"":"사거리 밖";
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=position;r.sizeDelta=size;return r;}
        private static Text Label(Transform parent,string value,Vector2 position,Vector2 size,int fontSize)
        {var r=Rect("Label",parent,new Vector2(.5f,.5f),position,size);var t=r.gameObject.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=fontSize;t.alignment=TextAnchor.MiddleCenter;RaidUIStyle.Color(t, Color.white);t.raycastTarget=false;t.text=value;return t;}
    }
}
