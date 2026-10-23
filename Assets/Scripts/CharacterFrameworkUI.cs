using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace RaidRogue
{
    // Extra common slots share the existing HUD canvas/input blockers. Existing 1/2/3/Q + movement/potion stay intact.
    public sealed class CharacterFrameworkUI : MonoBehaviour
    {
        private SkillHotbarUI _hotbar;private RectTransform _root;private Text _resources;
        private readonly Dictionary<CharacterSlot,Text> _labels=new Dictionary<CharacterSlot,Text>();
        private readonly Dictionary<CharacterSlot,Button> _buttons=new Dictionary<CharacterSlot,Button>();
        private readonly Dictionary<string,Image> _gauges=new Dictionary<string,Image>();
        private string _gaugeJob;
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
        }
        private CharacterActor Actor()=>_hotbar?.combatController!=null?CharacterActor.Ensure(_hotbar.combatController.gameObject):null;
        private void Update()
        {
            if(Time.unscaledTime<_next)return;_next=Time.unscaledTime+.25f;
            BuildCatalog();var actor=Actor();if(actor==null)return;
            var state=actor.Snapshot;if(state==null)return;
            if(_gaugeJob!=actor.Definition.id)
            {
                foreach(var fill in _gauges.Values)if(fill!=null)Destroy(fill.transform.parent.gameObject);_gauges.Clear();_gaugeJob=actor.Definition.id;
                for(int i=0;i<actor.Definition.resources.Length;i++)
                {
                    var d=actor.Definition.resources[i];var bar=Rect("Resource_"+d.id,_root,new Vector2(.5f,1),new Vector2(0,38+i*9),new Vector2(420,5));
                    bar.gameObject.AddComponent<Image>().color=new Color(.12f,.15f,.18f,.9f);
                    var fill=Rect("Fill",bar,Vector2.zero,Vector2.zero,Vector2.zero);fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;
                    var image=fill.gameObject.AddComponent<Image>();image.color=new Color(.25f,.8f,.75f,.8f);image.raycastTarget=false;_gauges[d.id]=image;
                }
            }
            foreach(var r in state.resources)if(_gauges.TryGetValue(r.id,out var fill)) { var d=System.Array.Find(actor.Definition.resources,x=>x.id==r.id);fill.rectTransform.anchorMax=new Vector2(Mathf.Clamp01(r.value/d.maximum),1); }
            var resourceText=new List<string>();
            foreach(var r in state.resources){var d=System.Array.Find(actor.Definition.resources,x=>x.id==r.id);resourceText.Add(d.name+" "+r.value.ToString(d.integer?"0":"0.#")+" / "+d.maximum.ToString("0.#"));}
            _resources.text=actor.Definition.name+" · "+(resourceText.Count==0?"직업 전용 자원 없음":string.Join("   ",resourceText));
            foreach(var slot in _labels.Keys)
            {
                var selection=System.Array.Find(state.selections,s=>s.slot==slot);var skill=selection==null?null:actor.Definition.Skill(selection.skillId);
                bool unlocked=skill!=null&&System.Array.IndexOf(state.unlocked,skill.id)>=0;bool ready=unlocked&&skill.implemented;
                var timing=skill==null?null:System.Array.Find(state.skills,s=>s.id==skill.id);
                string status=!unlocked?"잠김":!ready?"미구현":timing!=null&&timing.charges==0?timing.recharge.ToString("0.0")+"초":timing!=null&&skill.maximumCharges>1?timing.charges+" / "+skill.maximumCharges:"사용 가능";
                _labels[slot].text="["+LocalInputBindings.Label(CharacterSlots.Binding(slot))+"] "+CharacterSlots.Name(slot)+"\n"+status;
                _buttons[slot].interactable=ready&&!LocalGameplayInput.Blocked;
            }
        }
        private void BuildCatalog()
        {
            var role=RoleSelectionManager.Instance;if(_catalogBuilt||role?.roleModalRoot==null)return;_catalogBuilt=true;
            var panel=Rect("RegisteredClasses",role.roleModalRoot.transform,new Vector2(.5f,.5f),new Vector2(0,-370),new Vector2(1000,126));
            var image=panel.gameObject.AddComponent<Image>();image.color=new Color(.035f,.045f,.065f,.97f);
            var names=new List<string>();foreach(var job in CharacterCatalog.All)if(!job.playable)names.Add(job.name+" (미구현)");
            string rows="12직업 식별 데이터 등록 · 아래 직업은 아직 전투 선택 불가\n";
            for(int i=0;i<names.Count;i++){rows+=names[i]+((i+1)%4==0?"\n":"   ·   ");}
            Label(panel,rows,Vector2.zero,new Vector2(980,118),15);
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size)
        {var go=new GameObject(name,typeof(RectTransform));var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=position;r.sizeDelta=size;return r;}
        private static Text Label(Transform parent,string value,Vector2 position,Vector2 size,int fontSize)
        {var r=Rect("Label",parent,new Vector2(.5f,.5f),position,size);var t=r.gameObject.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=fontSize;t.alignment=TextAnchor.MiddleCenter;t.color=Color.white;t.raycastTarget=false;t.text=value;return t;}
    }
}
