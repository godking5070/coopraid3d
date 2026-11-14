using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // One-time migration of the known legacy HUD; versioned authored layouts are never reset.
    public static class AstellaSplitCombatLayout
    {
        private static readonly Color Gold=new Color(.8f,.69f,.46f);
        public static bool IsApplied(AstellaOrnateHUD hud)
        {
            if(hud==null||hud.playerRoot==null||hud.slots==null)return false;
            var root=hud.playerRoot.transform;
            var left=root.Find("Left - 1234");var right=root.Find("Right - Q F R Space");var center=root.Find("Center - V");
            if(left==null||right==null||center==null||root.Find("Identity gauge")==null||hud.showPlayerWarning)return false;
            CharacterSlot[] leftSlots={CharacterSlot.Gcd1,CharacterSlot.Gcd2,CharacterSlot.OffGcd1,CharacterSlot.Counter};
            CharacterSlot[] rightSlots={CharacterSlot.Job1,CharacterSlot.OffGcd2,CharacterSlot.Identity,CharacterSlot.Movement};
            foreach(var slot in leftSlots)if(hud.slots.FirstOrDefault(s=>!s.sharedResurrection&&s.slot==slot)?.button?.transform.parent!=left)return false;
            foreach(var slot in rightSlots)if(hud.slots.FirstOrDefault(s=>!s.sharedResurrection&&s.slot==slot)?.button?.transform.parent!=right)return false;
            return hud.slots.FirstOrDefault(s=>!s.sharedResurrection&&s.slot==CharacterSlot.Ultimate)?.button?.transform.parent==center;
        }
        public static bool ApplyIfMissing(AstellaOrnateHUD hud,Func<string,Sprite> art=null,Sprite circle=null)
        {
            if(hud==null||hud.playerRoot==null||hud.slots==null)return false;
            if(hud.combatLayoutVersion>=1)return false;
            if(IsApplied(hud)){hud.combatLayoutVersion=1;return false;}
            // Only migrate the known generated layout. Custom prefabs remain authoritative.
            if(hud.playerRoot.transform.Find("Actions")==null||hud.playerRoot.transform.Find("Vitality")==null)return false;
            var required=new[]{CharacterSlot.Gcd1,CharacterSlot.Gcd2,CharacterSlot.OffGcd1,CharacterSlot.Counter,CharacterSlot.Job1,CharacterSlot.OffGcd2,CharacterSlot.Identity,CharacterSlot.Ultimate,CharacterSlot.Movement};
            foreach(var slot in required)
                if(!hud.slots.Any(s=>s!=null&&!s.sharedResurrection&&s.slot==slot&&s.button!=null))
                    throw new InvalidOperationException("Missing combat slot before HUD upgrade: "+slot);
            if(art==null)
            {
                var sprites=new System.Collections.Generic.Dictionary<string,Sprite>(StringComparer.Ordinal);
                foreach(var image in hud.GetComponentsInChildren<Image>(true))if(image.sprite!=null)sprites[image.sprite.name]=image.sprite;
                foreach(var slot in hud.slots)if(slot?.fallbackArt!=null)sprites[slot.fallbackArt.name]=slot.fallbackArt;
                art=name=>sprites.TryGetValue(name,out var sprite)?sprite:null;
                if(circle==null)circle=art("Circle");
            }
                var player=(RectTransform)hud.playerRoot.transform;
                Set(player,new Vector2(.5f,0),new Vector2(0,18),new Vector2(1320,260));player.pivot=new Vector2(.5f,0);
                var oldActions=player.Find("Actions");
                var left=Rect("Left - 1234",player,new Vector2(-345,55),new Vector2(350,78));
                var right=Rect("Right - Q F R Space",player,new Vector2(345,55),new Vector2(410,78));
                var center=Rect("Center - V",player,new Vector2(0,55),new Vector2(78,78));
                CharacterSlot[] leftSlots={CharacterSlot.Gcd1,CharacterSlot.Gcd2,CharacterSlot.OffGcd1,CharacterSlot.Counter};
                CharacterSlot[] rightSlots={CharacterSlot.Job1,CharacterSlot.OffGcd2,CharacterSlot.Identity,CharacterSlot.Movement};
                for(int i=0;i<4;i++)MoveSlot(hud,leftSlots[i],left,new Vector2(-135+i*90,0));
                float[] x={-144,-54,36,158};
                for(int i=0;i<4;i++)MoveSlot(hud,rightSlots[i],right,new Vector2(x[i],0));
                MoveSlot(hud,CharacterSlot.Ultimate,center,Vector2.zero);
                var potion=hud.slots.FirstOrDefault(s=>!s.sharedResurrection&&s.slot==CharacterSlot.Potion);
                if(potion?.button!=null)
                {
                    potion.button.transform.SetParent(player,false);
                    Set((RectTransform)potion.button.transform,new Vector2(.5f,0),new Vector2(-576,56),new Vector2(78,78));
                    potion.button.transform.localScale=Vector3.one*.62f;
                }
                if(oldActions!=null)oldActions.gameObject.SetActive(false);
                hud.showPlayerWarning=false;
                if(hud.warningText!=null&&hud.warningText.transform.parent!=null)hud.warningText.transform.parent.gameObject.SetActive(false);
                // Preserve the original framed HP artwork and move it above the left group.
                if(hud.playerHpFill!=null)
                {
                    var hp=(RectTransform)(hud.playerHpText!=null?hud.playerHpText.transform.parent:hud.playerHpFill.transform.parent.parent);
                    hp.SetParent(player,false);Set(hp,new Vector2(.5f,0),new Vector2(-345,139),new Vector2(620,22));hp.localScale=Vector3.one*.56f;
                    if(hud.playerHpText!=null){hud.playerHpText.fontSize=26;hud.playerHpText.rectTransform.sizeDelta=new Vector2(570,36);}
                }
                var oldVitals=player.Find("Vitality");if(oldVitals!=null)oldVitals.gameObject.SetActive(false);
                CreateIdentity(hud,player,art,circle);
                CreateSecondary(hud,player);
                ConfigureJobEmblems(hud,art);
                PositionSharedRevival(hud);
                hud.combatLayoutVersion=1;
                return true;
        }
        private static void MoveSlot(AstellaOrnateHUD hud,CharacterSlot slot,RectTransform parent,Vector2 position)
        {
            var binding=hud.slots.FirstOrDefault(s=>!s.sharedResurrection&&s.slot==slot);
            if(binding?.button==null)throw new InvalidOperationException("Missing combat slot: "+slot);
            var r=(RectTransform)binding.button.transform;r.SetParent(parent,false);
            Set(r,new Vector2(.5f,.5f),position,new Vector2(78,78));r.localScale=Vector3.one;
            var element=r.GetComponent<LayoutElement>();if(element!=null)element.ignoreLayout=true;
            binding.button.gameObject.SetActive(true);
        }
        private static void CreateIdentity(AstellaOrnateHUD hud,RectTransform player,Func<string,Sprite> art,Sprite circle)
        {
            var gauge=Rect("Identity gauge",player,new Vector2(0,161),new Vector2(140,140));
            ImageAt("Ring background",gauge,Vector2.zero,new Vector2(128,128),new Color(.22f,.18f,.12f),circle);
            hud.resourceFill=ImageAt("Identity fill",gauge,Vector2.zero,new Vector2(126,126),Gold,circle);
            hud.resourceFill.type=Image.Type.Filled;hud.resourceFill.fillMethod=Image.FillMethod.Radial360;hud.resourceFill.fillOrigin=2;hud.resourceFill.fillClockwise=true;
            ImageAt("Inner disc",gauge,Vector2.zero,new Vector2(110,110),new Color(.035f,.035f,.07f),circle);
            hud.identityEmblem=ImageAt("Job emblem",gauge,new Vector2(0,12),new Vector2(60,60),Color.white,art("12_CompassEmblem"));
            hud.resourceText=TextAt("Identity amount",gauge,new Vector2(0,-36),new Vector2(190,26),14);
            hud.resourceText.text="직업 자원";hud.resourceRoot=gauge.gameObject;
            hud.identitySegments=new Image[6];
            for(int i=0;i<6;i++)
            {
                var segment=ImageAt("Resource pip "+(i+1),gauge,new Vector2(-30+i*12,-17),new Vector2(8,8),Gold,circle);
                segment.gameObject.SetActive(false);hud.identitySegments[i]=segment;
            }
        }
        private static void CreateSecondary(AstellaOrnateHUD hud,RectTransform player)
        {
            var r=Rect("Ultimate resource",player,new Vector2(345,139),new Vector2(350,16));
            ImageAt("Track",r,Vector2.zero,new Vector2(350,16),new Color(.04f,.05f,.1f),null);
            var fill=ImageAt("Fill",r,Vector2.zero,new Vector2(348,14),new Color(.34f,.53f,.9f),null);
            fill.sprite=hud.playerHpFill!=null?hud.playerHpFill.sprite:null;fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;
            hud.secondaryResourceFill=fill;hud.secondaryResourceRoot=r.gameObject;
            hud.secondaryResourceText=TextAt("Ultimate amount",r,Vector2.zero,new Vector2(340,24),14);
            hud.secondaryResourceText.text="필살기 게이지";
        }
        private static void ConfigureJobEmblems(AstellaOrnateHUD hud,Func<string,Sprite> loadArt)
        {
            foreach(var visual in hud.jobVisuals??Array.Empty<AstellaOrnateHUD.JobVisual>())
            {
                if(visual==null)continue;
                string id=visual.jobId??"";
                string art=id.Contains("aegis")?"04_ConstellationShield":id.Contains("oracle")?"06_RitualCircle":id.Contains("nightblade")?"03_VioletBlades":id.Contains("ranger")?"05_LavenderMoon":"12_CompassEmblem";
                visual.identityEmblem=loadArt(art);
                visual.identityColor=id.Contains("oracle")?new Color(.4f,.85f,.6f):id.Contains("nightblade")?new Color(.73f,.46f,.95f):id.Contains("ranger")?new Color(.4f,.78f,.94f):Gold;
            }
        }
        private static void PositionSharedRevival(AstellaOrnateHUD hud)
        {
            if(hud.partyRevivalRoot==null)return;
            var dock=(RectTransform)hud.partyRevivalRoot.transform;
            Set(dock,new Vector2(0,1),new Vector2(326,-158),new Vector2(160,62));dock.pivot=new Vector2(0,1);
            var label=dock.Find("Shared revival label") as RectTransform;
            if(label!=null){label.anchoredPosition=new Vector2(20,14);label.sizeDelta=new Vector2(112,22);var text=label.GetComponent<Text>();if(text!=null){text.fontSize=13;text.text="공유 부활";}}
            var binding=hud.slots.FirstOrDefault(s=>s.sharedResurrection);
            if(binding?.button==null)return;
            foreach(Transform child in dock)
                if(child!=label&&child!=binding.button.transform&&child!=binding.cooldown?.transform)child.gameObject.SetActive(false);
            var background=dock.GetComponent<Image>()??dock.gameObject.AddComponent<Image>();background.color=new Color(.035f,.04f,.075f,.9f);background.raycastTarget=false;
            var outline=dock.GetComponent<Outline>()??dock.gameObject.AddComponent<Outline>();outline.effectColor=Gold;outline.effectDistance=new Vector2(1,-1);
            Set((RectTransform)binding.button.transform,new Vector2(0,.5f),new Vector2(24,0),new Vector2(78,78));binding.button.transform.localScale=Vector3.one*.48f;
            if(binding.cooldown!=null){Set(binding.cooldown.rectTransform,new Vector2(.5f,.5f),new Vector2(18,-12),new Vector2(90,24));binding.cooldown.fontSize=17;}
        }
        private static RectTransform Rect(string name,Transform parent,Vector2 position,Vector2 size)
        {var r=parent.Find(name) as RectTransform;if(r==null){r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);}Set(r,new Vector2(.5f,0),position,size);return r;}
        private static void Set(RectTransform r,Vector2 anchor,Vector2 position,Vector2 size)
        {r.anchorMin=r.anchorMax=anchor;r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;}
        private static Image ImageAt(string name,Transform parent,Vector2 position,Vector2 size,Color color,Sprite sprite)
        {var r=Rect(name,parent,position,size);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);var image=r.GetComponent<Image>()??r.gameObject.AddComponent<Image>();image.color=color;image.sprite=sprite;image.raycastTarget=false;return image;}
        private static Text TextAt(string name,Transform parent,Vector2 position,Vector2 size,int fontSize)
        {var r=Rect(name,parent,position,size);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);var text=r.GetComponent<Text>()??r.gameObject.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=fontSize;text.color=Color.white;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;return text;}
    }
}
