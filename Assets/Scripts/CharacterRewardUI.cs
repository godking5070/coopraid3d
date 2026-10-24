using UnityEngine;
using UnityEngine.UI;
namespace RaidRogue
{
    // One automatic mandatory picker after the existing card reward. It never rolls or grants cards.
    [DefaultExecutionOrder(3100)]
    public sealed class CharacterRewardUI : MonoBehaviour
    {
        private static CharacterRewardUI _instance;
        public static bool IsOpen=>_instance!=null&&_instance._root!=null&&_instance._root.gameObject.activeSelf;
        private RectTransform _root,_panel;private Text _header,_status;private CharacterActor _actor;
        private readonly Button[] _buttons=new Button[3];private readonly Text[] _labels=new Text[3];
        private CharacterUnlockDefinition _rule;private string _key="";
        private void Awake(){_instance=this;}
        public static void Close(){if(_instance?._root!=null)_instance._root.gameObject.SetActive(false);}
        private void OnDestroy(){if(_instance==this)_instance=null;if(_root!=null)Destroy(_root.gameObject);}
        private void Update()
        {
            var session=RaidNetworkSession.Instance;var stage=StageManager.Instance;
            var hotbar=FindFirstObjectByType<SkillHotbarUI>();_actor=hotbar?.combatController?.GetComponent<CharacterActor>();
            bool reward=RaidCombatAuthority.Active?session?.BattlePhase==RaidBattlePhase.Reward:stage?.Progress.Phase==RaidBattlePhase.Reward;
            if(!reward||_actor==null||session?.Ending==true||RaidCombatAuthority.Active&&!session.ControlsReady||_actor.PendingChoices.Length==0||hotbar.combatController.playerController.cardManager?.IsSelectionOpen==true)
            {Close();_key="";return;}
            _rule=_actor.PendingChoices[0];string key=_actor.ChoiceKey(_rule.id);
            if(_root==null)Build(hotbar.GetComponentInParent<Canvas>());
            if(_root==null)return;
            if(!IsOpen||_key!=key)
            {
                _key=key;_root.gameObject.SetActive(true);QuarterViewCamera.LocalCamera?.ReleaseOrbit();LocalInputBindings.QuarantineHeld();
            }
            _header.text=$"STAGE {_rule.afterStage} 확정 스킬 보상 · [{LocalInputBindings.Label(CharacterSlots.Binding(_rule.slot))}] {CharacterSlots.Name(_rule.slot)}";
            for(int i=0;i<3;i++)
            {
                _buttons[i].gameObject.SetActive(i<_rule.choices.Length);if(i>=_rule.choices.Length)continue;
                ((RectTransform)_buttons[i].transform).anchoredPosition=new Vector2((i-(_rule.choices.Length-1)*.5f)*380,25);
                var skill=_actor.Definition.Skill(_rule.choices[i]);_labels[i].text=$"[{LocalInputBindings.Label("Choice"+(i+1))}] {skill.name}\n\n{skill.description}\n\n이번 런에서 한 종류만 확정";
            }
            bool covered=LocalCameraSettingsUI.IsOpen||session?.MenuOpen==true||session?.IsRecovering==true||RaidSteamRuntime.Instance?.OverlayOpen==true||RaidSteamRuntime.Instance?.ConfirmInvite==true||CombatMetricsUI.BlocksUnderlyingInput;
            foreach(var b in _buttons)b.interactable=!covered;
            _status.text=$"모든 후보가 제공됩니다. 기존 카드 보상과 별도로 한 스킬을 선택하세요.\n선택 후 교체 불가 · 재도전/재접속 유지 · {stage.SkillRewardSecondsRemaining:0}초 후 첫 후보 자동 선택";
            if(!covered)for(int i=0;i<_rule.choices.Length;i++)if(LocalInputBindings.Pressed("Choice"+(i+1))){Choose(i);break;}
            if(!covered)_root.SetAsLastSibling();FindFirstObjectByType<LocalCameraSettingsUI>()?.RaiseAboveMenus();
            var size=_root.rect.size;_panel.localScale=Vector3.one*Mathf.Max(.1f,Mathf.Min(1,(size.x-20)/1160,(size.y-20)/640));
        }
        private void Choose(int i)
        {if(_actor==null||_rule==null||i>=_rule.choices.Length)return;_actor.RequestChoice(_rule.id,_rule.choices[i]);LocalInputBindings.QuarantineHeld();}
        private void Build(Canvas canvas)
        {
            if(canvas==null)return;
            _root=Rect(canvas.transform,"CharacterReward",Vector2.zero,Vector2.zero);_root.anchorMin=Vector2.zero;_root.anchorMax=Vector2.one;_root.offsetMin=_root.offsetMax=Vector2.zero;
            _root.gameObject.AddComponent<Image>().color=new Color(.025f,.03f,.06f,.98f);
            _panel=Rect(_root,"Choices",Vector2.zero,new Vector2(1160,640));
            _header=Label(_panel,new Vector2(0,265),new Vector2(1130,65),26);
            for(int i=0;i<3;i++)
            {
                int index=i;var r=Rect(_panel,"Choice"+i,new Vector2((i-1)*380,25),new Vector2(365,385));
                var image=r.gameObject.AddComponent<Image>();image.color=new Color(.12f,.2f,.27f);
                var button=r.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>Choose(index));_buttons[i]=button;
                _labels[i]=Label(r,Vector2.zero,new Vector2(330,360),20);_labels[i].resizeTextForBestFit=true;_labels[i].resizeTextMinSize=15;_labels[i].resizeTextMaxSize=20;
            }
            _status=Label(_panel,new Vector2(0,-220),new Vector2(1130,90),18);
            var menu=Rect(_panel,"Menu",new Vector2(0,-290),new Vector2(240,40));var graphic=menu.gameObject.AddComponent<Image>();graphic.color=new Color(.15f,.23f,.3f);
            var buttonMenu=menu.gameObject.AddComponent<Button>();buttonMenu.targetGraphic=graphic;buttonMenu.onClick.AddListener(()=>{if(RaidNetworkSession.Instance!=null)RaidNetworkSession.Instance.MenuOpen=true;});
            Label(menu,Vector2.zero,new Vector2(230,38),18).text="메뉴 / 연결 종료";
        }
        private static RectTransform Rect(Transform parent,string name,Vector2 pos,Vector2 size)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=pos;r.sizeDelta=size;return r;}
        private static Text Label(Transform parent,Vector2 pos,Vector2 size,int font)
        {var r=Rect(parent,"Label",pos,size);var t=r.gameObject.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=font;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
    }
}
