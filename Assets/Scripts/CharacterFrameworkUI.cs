using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace RaidRogue
{
    // Extra common slots share the existing HUD canvas/input blockers. Existing 1/2/3/Q + movement/potion stay intact.
    public sealed class CharacterSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action enter,leave;
        public void OnPointerEnter(PointerEventData e)=>enter?.Invoke();
        public void OnPointerExit(PointerEventData e)=>leave?.Invoke();
        private void OnDisable()=>leave?.Invoke();
    }
    public sealed class CharacterFrameworkUI : MonoBehaviour
    {
        private SkillHotbarUI _hotbar;private RectTransform _root;private Text _resources, _deferred, _identity, _stats;
        private readonly Dictionary<CharacterSlot,Text> _labels=new Dictionary<CharacterSlot,Text>();
        private readonly Dictionary<CharacterSlot,Button> _buttons=new Dictionary<CharacterSlot,Button>();
        private readonly Dictionary<string,Image> _gauges=new Dictionary<string,Image>();
        private string _gaugeJob; private readonly Dictionary<string,float> _lastValues=new Dictionary<string,float>(); private float _pulseUntil;
        private float _next;
        private Text _tooltip;private CharacterSlot? _hover;
        private void Start()
        {
            _hotbar=GetComponent<SkillHotbarUI>();
            _root=Rect("CharacterSlots",transform,new Vector2(.5f,1),new Vector2(0,72),new Vector2(700,70));
            var bg=_root.gameObject.AddComponent<Image>();bg.color=new Color(.025f,.045f,.07f,.92f);
            var extra=new[]{CharacterSlot.Counter,CharacterSlot.Job2,CharacterSlot.Identity,CharacterSlot.Active,CharacterSlot.OffGcd2,CharacterSlot.Ultimate};
            for(int i=0;i<extra.Length;i++)
            {
                var slot=extra[i];var r=Rect(slot.ToString(),_root,new Vector2(.5f,.5f),new Vector2(-290+i*116,0),new Vector2(110,62));
                var image=r.gameObject.AddComponent<Image>();image.color=new Color(.12f,.17f,.22f,.95f);
                var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>Actor()?.RequestSlot(slot));
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
        private CharacterActor Actor()=>_hotbar?.combatController!=null?CharacterActor.Ensure(_hotbar.combatController.gameObject):null;
        private void Update()
        {
            if(Time.unscaledTime<_next)return;_next=Time.unscaledTime+.25f;
            var actor=Actor();if(actor==null)return;
            var state=actor.Snapshot;if(state==null)return;
            if(_gaugeJob!=actor.Definition.id)
            {
                foreach(var fill in _gauges.Values)if(fill!=null)Destroy(fill.transform.parent.gameObject);_gauges.Clear();_lastValues.Clear();_gaugeJob=actor.Definition.id;
                for(int i=0;i<actor.Definition.resources.Length;i++)
                {
                    var d=actor.Definition.resources[i];var bar=Rect("Resource_"+d.id,_root,new Vector2(.5f,1),new Vector2(0,38+i*9),new Vector2(420,5));
                    bar.gameObject.AddComponent<Image>().color=new Color(.12f,.15f,.18f,.9f);
                    var fill=Rect("Fill",bar,Vector2.zero,Vector2.zero,Vector2.zero);fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;
                    var image=fill.gameObject.AddComponent<Image>();image.color=new Color(.25f,.8f,.75f,.8f);image.raycastTarget=false;_gauges[d.id]=image;
                }
            }
            foreach(var r in state.resources)if(_gauges.TryGetValue(r.id,out var fill))
            {
                var d=System.Array.Find(actor.Definition.resources,x=>x.id==r.id);
                if(_lastValues.TryGetValue(r.id,out float previous)&&r.value>previous)_pulseUntil=Time.unscaledTime+.6f;
                _lastValues[r.id]=r.value;fill.rectTransform.anchorMax=new Vector2(Mathf.Clamp01(r.value/d.maximum),1);
                fill.color=Time.unscaledTime<_pulseUntil?Color.white:r.value>=d.maximum?new Color(1,.85f,.2f):new Color(.85f,.73f,.3f);
            }
            var resourceText=new List<string>();
            foreach(var r in state.resources){var d=System.Array.Find(actor.Definition.resources,x=>x.id==r.id);resourceText.Add(d.name+" "+r.value.ToString(d.integer?"0":"0.#")+" / "+d.maximum.ToString("0.#")+(r.value>=d.maximum?" MAX":""));}
            _resources.text=actor.Definition.name+" · "+(resourceText.Count==0?"직업 전용 자원 없음":string.Join("   ",resourceText));
            var aegis=actor.GetComponent<AegisPaladin>();
            _deferred.text=aegis!=null&&aegis.IsActive?"남은 지연 피해 "+aegis.DamageState.Pending.ToString("0.#")+(aegis.DamageState.FaithRemaining>0?" · 불굴의 신념 "+aegis.DamageState.FaithRemaining.ToString("0.0")+"초":""):"";
            string extensionStatus=actor.GetComponent<AegisExtensions>()?.StatusText()??"";
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
                _identity.text=aegis.RouteName+" · "+AegisRouteData.PassiveSummary(actor.RouteId)+"\n["+LocalInputBindings.Label(CharacterSlots.Binding(CharacterSlot.Identity))+"] "+identity.name+" · "+effect;
                if(aegis.DamageState.Route.Transformation>0)_deferred.text+=" · 쌍성검 "+aegis.DamageState.Route.Transformation.ToString("0.0")+"초";
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
            float immunity=actor.GetComponent<CombatStatusEffects>()?.KnockbackImmunity??0;
            if(immunity>0)_deferred.text+=" · 성채 보호 "+immunity.ToString("0.0")+"초";
            if((actor.GetComponent<CombatStatusEffects>()?.ControlImmunity??0)>0)_deferred.text+=" · 피격 이상 면역";
            _tooltip.gameObject.SetActive(_hover.HasValue&&!LocalGameplayInput.Blocked);
            foreach(var slot in _labels.Keys)
            {
                var selection=System.Array.Find(state.selections,s=>s.slot==slot);var skill=selection==null?null:actor.Definition.Skill(selection.skillId);
                bool unlocked=skill!=null&&System.Array.IndexOf(state.unlocked,skill.id)>=0;bool ready=unlocked&&skill.implemented;
                var timing=skill==null?null:System.Array.Find(state.skills,s=>s.id==skill.id);
                bool alive=CombatRange.LivingPartyMember(actor.transform);
                bool affordable=skill!=null&&System.Array.TrueForAll(skill.costs,c=>(System.Array.Find(state.resources,r=>r.id==c.resourceId)?.value??0)>=c.amount);
                string blocked=skill?.handlerId=="aegis.extension"?actor.GetComponent<AegisExtensions>()?.Unavailable(skill,TargetingManager.Instance?.CurrentTarget)??"":"";
                if(skill!=null&&(skill.target==SkillTargetRule.LivingEnemy||skill.target==SkillTargetRule.LivingAlly))
                {
                    var target=TargetingManager.Instance?.CurrentTarget;
                    if(!(skill.target==SkillTargetRule.LivingEnemy?CombatRange.LivingEnemy(target):CombatRange.LivingPartyMember(target)))blocked="유효한 대상 필요";
                    else if(!CombatRange.InRange(actor.transform,target,actor.EffectiveRange(skill)))blocked="사거리 밖";
                }
                string status=!unlocked?actor.SlotUnlockStatus(slot):skill!=null&&!skill.implemented?"미구현":!alive?"사망":timing!=null&&timing.charges==0?timing.recharge.ToString("0.0")+"초":!affordable?"성광 부족":slot==CharacterSlot.Identity&&aegis?.IdentityBlocked==true?"효과 진행 중":blocked.Length>0?blocked:timing!=null&&skill.maximumCharges>1?timing.charges+" / "+skill.maximumCharges:"사용 가능";
                _labels[slot].text="["+LocalInputBindings.Label(CharacterSlots.Binding(slot))+"] "+(ready?skill.name:CharacterSlots.Name(slot))+"\n"+status+(slot==CharacterSlot.Identity&&ready&&skill.costs.Length>0?" · 성광 "+skill.costs[0].amount.ToString("0")+" / CD "+skill.cooldown.ToString("0.#")+"초":"");
                _labels[slot].resizeTextForBestFit=true;_labels[slot].resizeTextMinSize=9;_labels[slot].resizeTextMaxSize=12;
                _buttons[slot].interactable=alive&&ready&&affordable&&timing!=null&&timing.charges>0&&blocked.Length==0&&!(slot==CharacterSlot.Identity&&aegis?.IdentityBlocked==true)&&!LocalGameplayInput.Blocked;
                if(_hover==slot)_tooltip.text=ready?$"[{LocalInputBindings.Label(CharacterSlots.Binding(slot))}] {skill.name}\n{skill.description}\n{status}":CharacterSlots.Name(slot)+" · "+status;

            }
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=position;r.sizeDelta=size;return r;}
        private static Text Label(Transform parent,string value,Vector2 position,Vector2 size,int fontSize)
        {var r=Rect("Label",parent,new Vector2(.5f,.5f),position,size);var t=r.gameObject.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=fontSize;t.alignment=TextAnchor.MiddleCenter;t.color=Color.white;t.raycastTarget=false;t.text=value;return t;}
    }
}
