using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace RaidRogue
{
    // Extra common slots share the existing HUD canvas/input blockers. Existing 1/2/3/Q + movement/potion stay intact.
    public sealed class CharacterFrameworkUI : MonoBehaviour
    {
        private SkillHotbarUI _hotbar;private RectTransform _root;private Text _resources, _deferred, _identity;
        private readonly Dictionary<CharacterSlot,Text> _labels=new Dictionary<CharacterSlot,Text>();
        private readonly Dictionary<CharacterSlot,Button> _buttons=new Dictionary<CharacterSlot,Button>();
        private readonly Dictionary<string,Image> _gauges=new Dictionary<string,Image>();
        private string _gaugeJob; private readonly Dictionary<string,float> _lastValues=new Dictionary<string,float>(); private float _pulseUntil;
        private float _next;private bool _catalogBuilt;
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
                _buttons[slot]=button;_labels[slot]=Label(r,"",Vector2.zero,new Vector2(106,60),12);
            }
            _resources=Label(_root,"",new Vector2(0,49),new Vector2(850,28),13);
            _deferred=Label(_root,"",new Vector2(0,95),new Vector2(900,24),13);
            _identity=Label(_root,"",new Vector2(0,134),new Vector2(900,54),14);
        }
        private CharacterActor Actor()=>_hotbar?.combatController!=null?CharacterActor.Ensure(_hotbar.combatController.gameObject):null;
        private void Update()
        {
            if(Time.unscaledTime<_next)return;_next=Time.unscaledTime+.25f;
            BuildCatalog();var actor=Actor();if(actor==null)return;
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
            _identity.text="";
            if(aegis!=null&&aegis.IsActive)
            {
                var identity=actor.Selected(CharacterSlot.Identity);
                string effect=AegisRouteData.EffectSummary(actor.RouteId);
                _identity.text=aegis.RouteName+" · ["+LocalInputBindings.Label(CharacterSlots.Binding(CharacterSlot.Identity))+"] "+identity.name+"\n"+effect;
                if(aegis.DamageState.Route.Transformation>0)_deferred.text+=" · 쌍성검 "+aegis.DamageState.Route.Transformation.ToString("0.0")+"초";
                if(aegis.DamageState.Route.Guard>0)_deferred.text+=" · 가드 "+aegis.DamageState.Route.Guard.ToString("0.0")+"초";
            }
            float immunity=actor.GetComponent<CombatStatusEffects>()?.KnockbackImmunity??0;
            if(immunity>0)_deferred.text+=" · 성채 보호 "+immunity.ToString("0.0")+"초";
            foreach(var slot in _labels.Keys)
            {
                var selection=System.Array.Find(state.selections,s=>s.slot==slot);var skill=selection==null?null:actor.Definition.Skill(selection.skillId);
                bool unlocked=skill!=null&&System.Array.IndexOf(state.unlocked,skill.id)>=0;bool ready=unlocked&&skill.implemented;
                var timing=skill==null?null:System.Array.Find(state.skills,s=>s.id==skill.id);
                bool alive=CombatRange.LivingPartyMember(actor.transform);
                bool affordable=skill!=null&&System.Array.TrueForAll(skill.costs,c=>(System.Array.Find(state.resources,r=>r.id==c.resourceId)?.value??0)>=c.amount);
                string status=!alive?"사망":!unlocked?"잠김":!ready?"미구현":timing!=null&&timing.charges==0?timing.recharge.ToString("0.0")+"초":!affordable?"성광 부족":slot==CharacterSlot.Identity&&aegis?.IdentityBlocked==true?"효과 진행 중":timing!=null&&skill.maximumCharges>1?timing.charges+" / "+skill.maximumCharges:"사용 가능";
                _labels[slot].text="["+LocalInputBindings.Label(CharacterSlots.Binding(slot))+"] "+(ready?skill.name:CharacterSlots.Name(slot))+"\n"+status+(slot==CharacterSlot.Identity&&ready&&skill.costs.Length>0?" · 성광 "+skill.costs[0].amount.ToString("0")+" / CD "+skill.cooldown.ToString("0.#")+"초":"");
                _labels[slot].resizeTextForBestFit=true;_labels[slot].resizeTextMinSize=9;_labels[slot].resizeTextMaxSize=12;
                _buttons[slot].interactable=alive&&ready&&affordable&&timing.charges>0&&!(slot==CharacterSlot.Identity&&aegis?.IdentityBlocked==true)&&!LocalGameplayInput.Blocked;
            }
        }
        private void BuildCatalog()
        {
            var role=RoleSelectionManager.Instance;if(_catalogBuilt||role?.roleModalRoot==null)return;_catalogBuilt=true;
            var select=Rect("AegisBasicSelect",role.roleModalRoot.transform,new Vector2(.5f,.5f),new Vector2(0,390),new Vector2(1000,64));
            var selectImage=select.gameObject.AddComponent<Image>();selectImage.color=new Color(.15f,.24f,.36f,.98f);
            var selectButton=select.gameObject.AddComponent<Button>();selectButton.targetGraphic=selectImage;selectButton.onClick.AddListener(role.SelectAegisPaladin);
            Label(select,"이지스 팔라딘 · 기본 스킬 + 기원 3루트 선택 (근접 탱커)  |  R / F / V 잠김",Vector2.zero,new Vector2(980,60),20);
            var panel=Rect("RegisteredClasses",role.roleModalRoot.transform,new Vector2(.5f,.5f),new Vector2(0,-370),new Vector2(1000,126));
            var image=panel.gameObject.AddComponent<Image>();image.color=new Color(.035f,.045f,.065f,.97f);
            var names=new List<string>();foreach(var job in CharacterCatalog.All)if(!job.playable)names.Add(job.name+" (미구현)");
            string rows="등록 직업 · 아래 직업은 아직 전투 선택 불가\n";
            for(int i=0;i<names.Count;i++){rows+=names[i]+((i+1)%4==0?"\n":"   ·   ");}
            Label(panel,rows,Vector2.zero,new Vector2(980,118),15);
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=position;r.sizeDelta=size;return r;}
        private static Text Label(Transform parent,string value,Vector2 position,Vector2 size,int fontSize)
        {var r=Rect("Label",parent,new Vector2(.5f,.5f),position,size);var t=r.gameObject.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=fontSize;t.alignment=TextAnchor.MiddleCenter;t.color=Color.white;t.raycastTarget=false;t.text=value;return t;}
    }
}
