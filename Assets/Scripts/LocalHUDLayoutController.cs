using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidRogue
{
    [DefaultExecutionOrder(32000)]
    public sealed class LocalHUDLayoutController : MonoBehaviour
    {
        private sealed class Pose
        {
            public Vector3 position,scale;
            public Pose(RectTransform r){position=r.anchoredPosition3D;scale=r.localScale;}
            public void Apply(RectTransform r){r.anchoredPosition3D=position;r.localScale=scale;}
        }
        private sealed class Item
        {
            public string id,name;
            public RectTransform rect,handle;
            public Pose defaults;
            public Image shade;
            public Text caption;
            public Vector2 parentSize;
        }
        public static LocalHUDLayoutController Instance { get; private set; }
        public static bool IsEditing=>Instance!=null&&Instance._editing;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic(){Instance=null;}
        private AstellaOrnateHUD _hud;
        private readonly List<Item> _items=new List<Item>();
        private readonly Dictionary<string,Pose> _before=new Dictionary<string,Pose>();
        private readonly Vector3[] _corners=new Vector3[4];
        private LocalHUDLayoutData _saved=new LocalHUDLayoutData();
        private GameObject _overlay;
        private RectTransform _toolbar;
        private Text _selection,_scaleText,_notice,_snapText;
        private string _selected;
        private bool _editing,_snap=true,_menuBefore,_restoreAllRequested;
        private RaidNetworkSession _session;
        private Action _return;
        private Vector2 _dragStart,_positionStart,_lastCanvasSize;
        private float _nextScan;
        private string _loadNotice="";
        public string FilePath=>Path.Combine(Application.persistentDataPath,"hud-layout.json");
        public static LocalHUDLayoutController Ensure(AstellaOrnateHUD hud)
        {
            if(hud==null)return null;
            var controller=hud.GetComponent<LocalHUDLayoutController>()??hud.gameObject.AddComponent<LocalHUDLayoutController>();
            controller.Initialize(hud);return controller;
        }
        private void Initialize(AstellaOrnateHUD hud)
        {
            Instance=this;
            if(_hud!=null)return;
            _hud=hud;Instance=this;
            string json=LocalSettingsFile.Read(FilePath,ValidJson,out _loadNotice);
            if(json!=null)_saved=JsonUtility.FromJson<LocalHUDLayoutData>(json);
            Discover();
        }
        private static bool ValidJson(string text)
        {
            if(string.IsNullOrWhiteSpace(text)||!text.TrimStart().StartsWith("{")||!text.TrimEnd().EndsWith("}")||text.IndexOf("\"version\"",StringComparison.Ordinal)<0||text.IndexOf("\"entries\"",StringComparison.Ordinal)<0)return false;
            try{return JsonUtility.FromJson<LocalHUDLayoutData>(text)?.Valid()==true;}
            catch(ArgumentException){return false;}
        }
        private void Add(string id,string name,RectTransform rect)
        {
            if(rect==null||rect.parent is not RectTransform||_items.Any(i=>i.id==id)||Mathf.Abs(rect.localScale.x)<.001f)return;
            var item=new Item{id=id,name=name,rect=rect,defaults=new Pose(rect),parentSize=((RectTransform)rect.parent).rect.size};_items.Add(item);
            var entry=Array.Find(_saved.entries,e=>e.id==id);
            if(entry!=null)ApplyEntry(item,entry);
            if(_editing){_before[id]=new Pose(rect);CreateHandle(item);}
        }
        private void Discover()
        {
            if(_hud==null||_hud.playerRoot==null)return;
            var player=_hud.playerRoot.transform;
            Add("skills.left","스킬 1 · 2 · 3 · 4",player.Find("Left - 1234") as RectTransform);
            Add("skills.right","스킬 Q · F · R · Space",player.Find("Right - Q F R Space") as RectTransform);
            Add("identity","아덴 게이지",_hud.resourceRoot?.transform as RectTransform);
            Add("ultimate","V 필살기",player.Find("Center - V") as RectTransform);
            Add("hp","내 체력",_hud.playerHpText?.transform.parent as RectTransform);
            Add("ultimate.resource","필살기 게이지",_hud.secondaryResourceRoot?.transform as RectTransform);
            var potion=Array.Find(_hud.slots,s=>!s.sharedResurrection&&s.slot==CharacterSlot.Potion);
            Add("potion","포션",potion?.button?.transform as RectTransform);
            Add("revival","공유 부활",_hud.partyRevivalRoot?.transform as RectTransform);
            Add("boss","보스 정보",_hud.bossRoot?.transform as RectTransform);
            Add("boss.cast","보스 시전바",_hud.castRoot?.transform as RectTransform);
            var canvas=GetComponentInParent<Canvas>();if(canvas==null)return;
            Add("party","파티 목록",canvas.GetComponentInChildren<PartyListUI>(true)?.panelRoot);
            Add("minimap","미니맵",canvas.GetComponentInChildren<RaidMinimapUI>(true)?.Panel);
            Add("enemies","적 목록",(canvas.GetComponentInChildren<EnemyListUI>(true)??EnemyListUI.Instance)?.Panel);
            Add("metrics","전투 기록",(canvas.GetComponentInChildren<CombatMetricsUI>(true)??FindFirstObjectByType<CombatMetricsUI>())?.Panel);
        }
        private void LateUpdate()
        {
            if(_hud==null)return;
            if(Time.unscaledTime>=_nextScan){_nextScan=Time.unscaledTime+1;Discover();}
            foreach(var item in _items)
            {
                if(item.rect==null||item.rect.parent is not RectTransform parent)continue;
                var size=parent.rect.size;
                if(size!=item.parentSize&&!_editing){var entry=Array.Find(_saved.entries,e=>e.id==item.id);if(entry!=null)ApplyEntry(item,entry);}
                item.parentSize=size;
            }
            var canvas=GetComponentInParent<Canvas>();
            if(canvas!=null)
            {
                var size=((RectTransform)canvas.transform).rect.size;
                if(size!=_lastCanvasSize&&!_editing)
                    foreach(var item in _items){var entry=Array.Find(_saved.entries,e=>e.id==item.id);if(entry!=null)ApplyEntry(item,entry);}
                _lastCanvasSize=size;
            }
            if(!_editing)return;
            if(_session?.Ending==true){End(false,false);return;}
            _toolbar.localScale=Vector3.one*Mathf.Max(.4f,Mathf.Min(1,(Screen.width-24)/1140f));
            foreach(var item in _items)
            {
                if(item.rect==null||item.handle==null)continue;
                var bounds=ScreenBounds(item.rect);
                item.handle.anchoredPosition=bounds.center;
                item.handle.sizeDelta=new Vector2(Mathf.Max(34,bounds.width),Mathf.Max(28,bounds.height));
                item.shade.color=item.id==_selected?new Color(.9f,.72f,.25f,.16f):new Color(.1f,.75f,.88f,.07f);
                item.caption.text=item.name+(item.rect.gameObject.activeInHierarchy?"":" · 미표시");
            }
            var selected=Selected();
            if(selected!=null)
            {
                _selection.text=selected.name;
                _scaleText.text=(ScaleOf(selected)*100).ToString("0")+"%";
                var keyboard=Keyboard.current;
                if(keyboard!=null&&Application.isFocused&&!LocalGameplayInput.IsTyping&&RaidSteamRuntime.Instance?.OverlayOpen!=true)
                {
                    float step=keyboard.shiftKey.isPressed?10:1;Vector2 move=Vector2.zero;
                    if(keyboard.leftArrowKey.wasPressedThisFrame)move.x-=step;if(keyboard.rightArrowKey.wasPressedThisFrame)move.x+=step;
                    if(keyboard.upArrowKey.wasPressedThisFrame)move.y+=step;if(keyboard.downArrowKey.wasPressedThisFrame)move.y-=step;
                    if(move!=Vector2.zero){MoveScreen(selected,move);LocalGameplayInput.SuppressThisFrame();}
                }
            }
        }
        public bool Begin(Action returnToSettings)
        {
            if(_editing||RaidSteamRuntime.Instance?.OverlayOpen==true||RaidNetworkSession.Instance?.Ending==true)return false;
            Discover();if(_items.Count==0)return false;
            _before.Clear();foreach(var item in _items)if(item.rect!=null)_before[item.id]=new Pose(item.rect);
            _return=returnToSettings;_editing=true;_restoreAllRequested=false;Instance=this;
            _session=RaidNetworkSession.Instance;_menuBefore=_session?.MenuOpen==true;if(_session!=null)_session.MenuOpen=false;
            BuildOverlay();foreach(var item in _items)CreateHandle(item);
            _selected=_items[0].id;_notice.text=_loadNotice;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            LocalInputBindings.QuarantineHeld();LocalGameplayInput.SuppressPointerThisFrame();return true;
        }
        public void Select(string id){if(_editing&&_items.Any(i=>i.id==id))_selected=id;}
        private Item Selected()=>_items.Find(i=>i.id==_selected&&i.rect!=null);
        private static Camera CameraFor(RectTransform rect)
        {var c=rect.GetComponentInParent<Canvas>();return c!=null&&c.renderMode!=RenderMode.ScreenSpaceOverlay?c.worldCamera:null;}
        public void BeginDrag(string id,Vector2 point)
        {
            Select(id);var item=Selected();if(item==null)return;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)item.rect.parent,point,CameraFor(item.rect),out _dragStart))
                _positionStart=item.rect.anchoredPosition;
        }
        public void Drag(string id,Vector2 point)
        {
            if(!_editing||id!=_selected||RaidSteamRuntime.Instance?.OverlayOpen==true)return;
            var item=Selected();if(item==null)return;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)item.rect.parent,point,CameraFor(item.rect),out var now))return;
            Vector2 position=_positionStart+now-_dragStart;
            item.rect.anchoredPosition=new Vector2(HUDLayoutRules.Snap(position.x,_snap),HUDLayoutRules.Snap(position.y,_snap));
            Clamp(item);LocalGameplayInput.SuppressPointerThisFrame();
        }
        private void MoveScreen(Item item,Vector2 delta)
        {
            var parent=(RectTransform)item.rect.parent;var center=ScreenBounds(item.rect).center;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,center,CameraFor(item.rect),out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,center+delta,CameraFor(item.rect),out var b);
            item.rect.anchoredPosition+=b-a;Clamp(item);
        }
        private Rect ScreenBounds(RectTransform rect)
        {
            rect.GetWorldCorners(_corners);Vector2 min=new Vector2(float.MaxValue,float.MaxValue),max=new Vector2(float.MinValue,float.MinValue);
            var camera=CameraFor(rect);
            foreach(var corner in _corners){var p=RectTransformUtility.WorldToScreenPoint(camera,corner);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        private void Clamp(Item item)
        {
            var r=ScreenBounds(item.rect);var center=r.center;
            Vector2 delta=new Vector2(HUDLayoutRules.ClampCenter(center.x,r.width,Screen.width),HUDLayoutRules.ClampCenter(center.y,r.height,Screen.height))-center;
            if(delta.sqrMagnitude<.01f)return;
            var parent=(RectTransform)item.rect.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,center,CameraFor(item.rect),out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,center+delta,CameraFor(item.rect),out var b);
            item.rect.anchoredPosition+=b-a;
        }
        private void ApplyEntry(Item item,LocalHUDLayoutEntry entry)
        {
            if(item.rect==null)return;
            var parent=(RectTransform)item.rect.parent;
            item.rect.anchoredPosition=new Vector2(entry.x*parent.rect.width,entry.y*parent.rect.height);
            item.rect.localScale=item.defaults.scale*HUDLayoutRules.Scale(entry.scale);Clamp(item);
        }
        private float ScaleOf(Item item)=>HUDLayoutRules.Scale(item.rect.localScale.x/item.defaults.scale.x);
        private void Resize(float delta)
        {var item=Selected();if(item==null)return;item.rect.localScale=item.defaults.scale*HUDLayoutRules.Scale(ScaleOf(item)+delta);Clamp(item);}
        private void ResetSelected(){var item=Selected();if(item!=null)item.defaults.Apply(item.rect);}
        private void ResetAll(){foreach(var item in _items)if(item.rect!=null)item.defaults.Apply(item.rect);_restoreAllRequested=true;_notice.text="기본 배치를 미리 봅니다. 저장하면 확정됩니다.";}
        private void Save()
        {
            var entries=_restoreAllRequested?new List<LocalHUDLayoutEntry>():new List<LocalHUDLayoutEntry>(_saved.entries.Where(e=>_items.All(i=>i.id!=e.id)).Select(e=>e.Copy()));
            foreach(var item in _items)
            {
                if(item.rect==null)continue;
                float scale=ScaleOf(item);
                if((item.rect.anchoredPosition3D-item.defaults.position).sqrMagnitude<.01f&&Mathf.Abs(scale-1)<.001f)continue;
                var parent=(RectTransform)item.rect.parent;
                entries.Add(new LocalHUDLayoutEntry{id=item.id,x=HUDLayoutRules.Normalize(item.rect.anchoredPosition.x,parent.rect.width),y=HUDLayoutRules.Normalize(item.rect.anchoredPosition.y,parent.rect.height),scale=scale});
            }
            var candidate=new LocalHUDLayoutData{entries=entries.ToArray()};
            if(!LocalSettingsFile.Save(FilePath,JsonUtility.ToJson(candidate,true),ValidJson,out string error)){_notice.text=error;return;}
            _saved=candidate;End(true,true);
        }
        public void CancelEdit()=>End(false,true);
        private void End(bool keep,bool callback)
        {
            if(!_editing)return;
            if(!keep)foreach(var item in _items)if(item.rect!=null&&_before.TryGetValue(item.id,out var pose))pose.Apply(item.rect);
            _editing=false;
            if(_session!=null&&_session==RaidNetworkSession.Instance&&!_session.Ending)_session.MenuOpen=_menuBefore;
            if(_overlay!=null)Destroy(_overlay);_overlay=null;
            foreach(var item in _items)item.handle=null;
            LocalInputBindings.QuarantineHeld();LocalGameplayInput.ConsumeEscape();LocalGameplayInput.SuppressPointerThisFrame();
            var action=_return;_return=null;if(callback)action?.Invoke();
        }
        private void CreateHandle(Item item)
        {
            if(_overlay==null||item.handle!=null)return;
            var rect=MakeRect(item.id,_overlay.transform,Vector2.zero,new Vector2(100,50));rect.anchorMin=rect.anchorMax=Vector2.zero;
            var shade=rect.gameObject.AddComponent<Image>();shade.color=new Color(.1f,.75f,.88f,.07f);item.shade=shade;
            var line=rect.gameObject.AddComponent<Outline>();line.effectColor=new Color(.25f,.85f,1,.95f);line.effectDistance=new Vector2(1,-1);
            var label=MakeText(rect,item.name,new Vector2(0,8),new Vector2(260,24),14);label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(.5f,1);item.caption=label;
            var drag=rect.gameObject.AddComponent<LocalHUDDragHandle>();drag.owner=this;drag.id=item.id;item.handle=rect;
            _toolbar.SetAsLastSibling();_notice.transform.SetAsLastSibling();
        }
        private void BuildOverlay()
        {
            _overlay=new GameObject("HUD Layout Editor",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            var canvas=_overlay.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=30000;
            var background=_overlay.AddComponent<Image>();background.color=new Color(.01f,.015f,.025f,.12f);
            _toolbar=MakeRect("HUD controls",_overlay.transform,new Vector2(0,-12),new Vector2(1140,112));
            _toolbar.anchorMin=_toolbar.anchorMax=new Vector2(.5f,1);_toolbar.pivot=new Vector2(.5f,1);
            _toolbar.gameObject.AddComponent<Image>().color=new Color(.025f,.035f,.06f,.98f);
            MakeText(_toolbar,"HUD 배치 편집",new Vector2(-420,26),new Vector2(260,32),22);
            Button(_toolbar,"◀",new Vector2(-254,26),38,()=>Cycle(-1));
            _selection=Button(_toolbar,"",new Vector2(-90,26),280,()=>Cycle(1)).GetComponentInChildren<Text>();
            Button(_toolbar,"▶",new Vector2(74,26),38,()=>Cycle(1));
            Button(_toolbar,"−",new Vector2(155,26),44,()=>Resize(-.1f));
            _scaleText=MakeText(_toolbar,"100%",new Vector2(230,26),new Vector2(100,32),19);
            Button(_toolbar,"+",new Vector2(305,26),44,()=>Resize(.1f));
            Button(_toolbar,"저장",new Vector2(465,26),140,Save);
            Button(_toolbar,"선택 기본값",new Vector2(-420,-24),160,ResetSelected);
            Button(_toolbar,"전체 기본값",new Vector2(-230,-24),180,ResetAll);
            _snapText=Button(_toolbar,"격자 맞춤 켜짐",new Vector2(-30,-24),180,()=>{_snap=!_snap;_snapText.text=_snap?"격자 맞춤 켜짐":"격자 맞춤 꺼짐";}).GetComponentInChildren<Text>();
            MakeText(_toolbar,"드래그 · 방향키 이동\nShift: 10px · Esc: 취소",new Vector2(220,-24),new Vector2(270,40),13);
            Button(_toolbar,"취소",new Vector2(465,-24),140,CancelEdit);
            _notice=MakeText(_overlay.transform,"",new Vector2(0,24),new Vector2(1000,48),15);
            _notice.rectTransform.anchorMin=_notice.rectTransform.anchorMax=new Vector2(.5f,0);
        }
        private void Cycle(int delta)
        {int at=_items.FindIndex(i=>i.id==_selected);for(int i=0;i<_items.Count;i++){at=(at+delta+_items.Count)%_items.Count;if(_items[at].rect!=null){_selected=_items[at].id;break;}}}
        private static RectTransform MakeRect(string name,Transform parent,Vector2 position,Vector2 size)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;return r;}
        private static Text MakeText(Transform parent,string text,Vector2 position,Vector2 size,int fontSize)
        {var r=MakeRect("Label",parent,position,size);var label=r.gameObject.AddComponent<Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=fontSize;label.color=Color.white;label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;label.text=text;return label;}
        private static Button Button(Transform parent,string title,Vector2 position,float width,UnityEngine.Events.UnityAction callback)
        {var r=MakeRect(title,parent,position,new Vector2(width,36));var image=r.gameObject.AddComponent<Image>();image.color=new Color(.12f,.19f,.27f);var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;b.navigation=new Navigation{mode=Navigation.Mode.None};b.onClick.AddListener(callback);MakeText(r,title,Vector2.zero,new Vector2(width-8,32),16);return b;}
        private void OnDisable(){if(_editing)End(false,true);}
        private void OnDestroy(){End(false,false);if(Instance==this)Instance=null;}
    }
}
