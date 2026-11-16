#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Astella.PrismV4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Explicit, one-time authoring into a unique copy. Never regenerates an edited HUD.</summary>
    public static class AstellaPrismHUDInstaller
    {
        const string Output = "Assets/UI/AstellaPrismV4/AppliedMain";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        static Font font;
        static readonly CharacterSlot[] MainSlots = { CharacterSlot.Gcd1, CharacterSlot.Gcd2, CharacterSlot.OffGcd1,
            CharacterSlot.Counter, CharacterSlot.Job1, CharacterSlot.OffGcd2, CharacterSlot.Identity, CharacterSlot.Movement };

        [MenuItem("Tools/Astella/Prism V4/Apply to Main HUD (Preserved Copy)")]
        public static void ApplyToMain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play Mode and close Prefab Mode before applying Prism.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/','\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Apply only in the original Unity project.");
            var profile = RaidUIPresentation.Settings;
            if (profile?.hudPrefab == null) throw new InvalidOperationException("Active HUD missing.");
            if (profile.hudPrefab.GetComponentInChildren<AstellaPrismHUDBridge>(true) != null)
            { Debug.Log("Prism already active. Existing authored copy was preserved."); return; }
            string source = AssetDatabase.GetAssetPath(profile.hudPrefab);
            byte[] original = File.ReadAllBytes(source);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backup = "Logs/PrismV4/Backup-" + stamp;
            Directory.CreateDirectory(backup);
            File.Copy(source, backup + "/OriginalRaidHUD.prefab");
            File.Copy(source + ".meta", backup + "/OriginalRaidHUD.prefab.meta");
            File.Copy(AssetDatabase.GetAssetPath(profile), backup + "/RaidUIPresentation.asset");
            File.Copy(ScenePath, backup + "/SampleScene.unity");
            Folder(Output); string folder = Output + "/Applied-" + stamp; Folder(folder);
            string path = folder + "/RaidHUD.prefab";
            if (!AssetDatabase.CopyAsset(source, path)) throw new IOException("HUD copy failed.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Build(root);
                RaidUIAuthoring.ValidateScripts(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException("HUD save failed.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (!original.SequenceEqual(File.ReadAllBytes(source))) throw new IOException("Original HUD changed during authoring; activation stopped.");
            AstellaPrismHUDChecks.ValidatePrefab(path);
            Activate(path, source, backup);
        }

        [MenuItem("Tools/Astella/Prism V4/Activate Validated Copy")]
        public static void ActivateValidatedCopy()
        {
            var report=JsonUtility.FromJson<ValidationRecord>(File.ReadAllText("Logs/PrismV4/active-hud-validation.json"));
            if(report==null||!report.passed||!report.prefabPath.StartsWith(Output+"/",StringComparison.Ordinal))
                throw new InvalidOperationException("No validated Prism copy available.");
            AstellaPrismHUDChecks.ValidatePrefab(report.prefabPath);
            string stamp=Path.GetFileName(Path.GetDirectoryName(report.prefabPath)).Replace("Applied-","");
            Activate(report.prefabPath,report.sourcePrefabPath,"Logs/PrismV4/Backup-"+stamp);
        }
        [Serializable] sealed class ValidationRecord { public bool passed; public string prefabPath,sourcePrefabPath; }

        static void Activate(string path,string source,string backup)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before activating a HUD.");
            if(PrefabStageUtility.GetCurrentPrefabStage()!=null||!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/','\\'),@"C:\Astella\Astella\Assets",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Activate from the original project, outside Prefab Mode.");
            // The scene's existing HUD overrides the profile at runtime. Preserve that instance,
            // including its authored overrides, and activate exactly one new prefab instance.
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var old in scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<RaidUIRoot>(true)))
                if (old.gameObject.activeInHierarchy) old.gameObject.SetActive(false);
            // OpenScene can unload unused prefab assets. Resolve the new asset afterwards.
            var saved = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(saved.gameObject, scene);
            instance.name = "Astella_PrismV4HUD";
            instance.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene activation save failed.");
            var profile=RaidUIPresentation.Settings;
            profile.hudPrefab = saved;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/PrismV4/installation.json", JsonUtility.ToJson(new Installation {
                source = source, applied = path, scene = ScenePath, backup = backup, generatedUtc = DateTime.UtcNow.ToString("O") }, true));
            Debug.Log("PRISM MAIN APPLIED: " + path + " | Original preserved: " + source);
        }

        [Serializable] sealed class Installation { public string source, applied, scene, backup, generatedUtc; }

        static void Build(GameObject root)
        {
            var hud = root.GetComponentInChildren<AstellaOrnateHUD>(true);
            font = hud.playerHpText.font;
            var bridge = hud.gameObject.AddComponent<AstellaPrismHUDBridge>();
            bridge.hud = hud;
            bridge.jobSource = root.GetComponentInChildren<AstellaClassJobHUD>(true);
            bridge.ultimateSource = root.GetComponentInChildren<AstellaClassUltimateHUD>(true);
            bridge.readinessSource = root.GetComponentInChildren<AstellaPlayerHUDV3>(true);
            bridge.partySource = root.GetComponentInChildren<AstellaPartyHUDV4>(true);
            var display = Rect("Prism V4", root.transform, 0, 0, 1672, 941);
            display.anchorMin = display.anchorMax = display.pivot = new Vector2(.5f,.5f);
            display.anchoredPosition = Vector2.zero;
            display.localScale = Vector3.one * (1080f/941f);
            display.gameObject.AddComponent<AstellaPrismCanvasFit>();
            var player = Rect("Player", display, 0, 0, 1672, 941);
            bridge.playerView = player.gameObject.AddComponent<PrismV4Bindings>();
            bridge.playerView.module = PrismV4Bindings.Module.Player;
            var hp = Rect("Health", player, 64, 618, 620, 58);
            Picture("Track", hp, "Player/gauge-track", 0, 0, 620, 58);
            bridge.playerView.healthFill = Gauge(hp, "Player/hp-fill",620,58,new Rect(29f/1024,18f/96,966f/1024,60f/96),36f/1024);
            Picture("Frame", hp, "Player/gauge-frame",0,0,620,58);
            Label("Caption",hp,"HP",45,0,70,58,22);
            bridge.playerView.valueText = Label("Live HP",hp,"",120,0,395,58,25);
            var resource = Rect("Primary resource",player,986,618,620,58);
            Picture("Track",resource,"Player/gauge-track",0,0,620,58);
            bridge.playerView.jobResourceFill = Gauge(resource,"Player/resource-fill",620,58,new Rect(29f/1024,18f/96,966f/1024,60f/96),36f/1024);
            Picture("Frame",resource,"Player/gauge-frame",0,0,620,58);
            bridge.playerView.resourceText = Label("Live primary value",resource,"",100,0,420,58,25);
            bridge.playerView.nameText = Label("Live primary name",resource,"",0,-29,620,28,20);
            var left = Rect("Left skills",player,82,710,598,136);
            var right = Rect("Right skills",player,1000,710,598,136);
            bridge.slots = new AstellaPrismHUDBridge.SlotMirror[8];
            for (int i=0;i<8;i++)
            {
                int index = Array.FindIndex(hud.slots,s=>s!=null&&!s.sharedResurrection&&s.slot==MainSlots[i]);
                if(index<0) throw new InvalidOperationException("Missing skill " + MainSlots[i]);
                var slot = hud.slots[index];
                var button = (RectTransform)slot.button.transform;
                HideChildren(button);
                Place(button,i<4?left:right,(i%4)*154,0,136,136);
                button.gameObject.SetActive(true);
                if(button.TryGetComponent<Graphic>(out var oldHit)) { oldHit.enabled=false; oldHit.raycastTarget=false; }
                var viewRoot = Rect("Prism slot",button,0,0,136,136);
                var view = viewRoot.gameObject.AddComponent<PrismV4Bindings>();
                view.module=PrismV4Bindings.Module.SkillSlot; view.button=slot.button;
                Picture("Background",viewRoot,"Skills/slot-background",0,0,136,136);
                var mask=Rect("Clipped contents",viewRoot,0,0,136,136);
                var maskImage=mask.gameObject.AddComponent<Image>();maskImage.sprite=Sprite("Skills/slot-inner-mask");maskImage.raycastTarget=false;
                mask.gameObject.AddComponent<Mask>().showMaskGraphic=false;
                view.artwork=Picture("Existing icon",mask,null,0,0,136,136);view.artwork.preserveAspect=true;
                view.cooldownShade=Picture("Cooldown",mask,"States/cooldown-overlay",0,0,136,136);
                view.cooldownShade.type=Image.Type.Filled;view.cooldownShade.fillMethod=Image.FillMethod.Radial360;view.cooldownShade.fillOrigin=2;view.cooldownShade.fillClockwise=true;view.cooldownShade.fillAmount=0;
                view.nameText=Label("Existing skill name",mask,"",22,28,92,82,17);
                view.frame=Picture("Frame",viewRoot,"Skills/slot-frame-normal",0,0,136,136);view.frame.raycastTarget=true;
                view.disabledOverlay=Picture("Unavailable",viewRoot,"States/disabled-overlay",0,0,136,136);
                view.cooldownText=Label("Cooldown number",viewRoot,"",0,0,136,136,31);
                bool wide=slot.slot==CharacterSlot.Movement;float keyWidth=wide?80:51;
                Picture("Binding badge",viewRoot,wide?"Skills/key-badge-wide":"Skills/key-badge",(136-keyWidth)/2,-15,keyWidth,34);
                view.keyText=Label("Live key",viewRoot,"",(136-keyWidth)/2,-15,keyWidth,34,20);
                slot.button.targetGraphic=view.frame;slot.button.transition=Selectable.Transition.SpriteSwap;
                slot.button.spriteState=new SpriteState { highlightedSprite=Sprite("Skills/slot-frame-hover"),pressedSprite=Sprite("Skills/slot-frame-pressed"),selectedSprite=Sprite("Skills/slot-frame-focused"),disabledSprite=Sprite("Skills/slot-frame-disabled") };
                var skin=button.GetComponent<AstellaSkillSlotSkin>();
                if(skin!=null)
                {
                    skin.frame=view.frame;skin.normalFrame=view.frame.sprite;
                    skin.hoverFrame=Sprite("Skills/slot-frame-hover");skin.pressedFrame=Sprite("Skills/slot-frame-pressed");
                    skin.focusedFrame=Sprite("Skills/slot-frame-focused");skin.disabledFrame=Sprite("Skills/slot-frame-disabled");
                    slot.button.transition=Selectable.Transition.None;
                }
                bridge.slots[i]=new AstellaPrismHUDBridge.SlotMirror { sourceIndex=index,view=view };
            }
            bridge.playerView.skillSlots=bridge.slots.Select(s=>s.view).ToArray();
            var ultimate=hud.slots.First(s=>!s.sharedResurrection&&s.slot==CharacterSlot.Ultimate);
            var center=(RectTransform)ultimate.button.transform;HideChildren(center);Place(center,display,700,610,272,272);
            if(center.TryGetComponent<Graphic>(out var centerHit)){centerHit.enabled=false;centerHit.raycastTarget=false;}
            var centerVisual=Rect("Prism ultimate",center,0,0,272,272);
            var central=bridge.centerView=centerVisual.gameObject.AddComponent<PrismV4Bindings>();central.module=PrismV4Bindings.Module.Center;central.button=ultimate.button;
            Picture("Background",centerVisual,"Hub/hub-background",0,0,272,272);
            central.ultimateChargeFill=Gauge(centerVisual,"Hub/hub-crystal",272,272,new Rect(0,0,1,1),0);central.ultimateChargeFill.direction=PrismV4Gauge.Direction.BottomToTop;
            central.readyGlow=Picture("Ready",centerVisual,"Hub/ready-glow",0,0,272,272).gameObject;
            central.unavailableOverlay=Picture("Unavailable",centerVisual,"Hub/disabled-overlay",0,0,272,272).gameObject;
            central.frame=Picture("Fixed frame",centerVisual,"Hub/hub-frame",0,0,272,272);central.frame.raycastTarget=true;
            ultimate.button.targetGraphic=central.frame;ultimate.button.transition=Selectable.Transition.None;
            Picture("V badge",centerVisual,"Hub/ultimate-badge",90.7f,-17,90.7f,56.7f);
            central.keyText=Label("Live ultimate key",centerVisual,"V",90.7f,-17,90.7f,56.7f,28);
            central.valueText=Label("Live charge",centerVisual,"",62,219,148,31,23);
            central.cooldownText=Label("Cooldown",centerVisual,"",60,103,152,64,32);
            center.gameObject.SetActive(true);
            // Utility actions and tooltip remain existing objects, outside the hidden source composition.
            var potion=hud.slots.FirstOrDefault(s=>!s.sharedResurrection&&s.slot==CharacterSlot.Potion);
            if(potion?.button!=null)Place((RectTransform)potion.button.transform,player,30,850,72,64);
            if(hud.tooltipRoot!=null)Place((RectTransform)hud.tooltipRoot.transform,player,450,410,772,180);
            if(hud.partyRevivalRoot!=null)Place((RectTransform)hud.partyRevivalRoot.transform,display,44,400,200,62);
            Hide(hud.playerRoot);

            var boss=Rect("Boss",display,348,35,984,200);
            var bossView=bridge.bossView=boss.gameObject.AddComponent<PrismV4Bindings>();bossView.module=PrismV4Bindings.Module.Boss;
            bossView.nameText=Label("Live boss title",boss,"",24,0,960,34,24);
            var bossHp=Rect("Health",boss,24,35,960,72);
            Picture("Track",bossHp,"Boss/boss-track",0,0,960,72);
            bossView.bossFill=Gauge(bossHp,"Boss/boss-fill",960,72,new Rect(27f/1280,29f/96,1226f/1280,38f/96),22f/1280);
            Picture("Frame",bossHp,"Boss/boss-frame",0,0,960,72);
            Picture("Left gem",bossHp,"Boss/boss-gem-left",-12,0,72,72);Picture("Right gem",bossHp,"Boss/boss-gem-right",900,0,72,72);
            bossView.valueText=Label("Live HP percent",bossHp,"",0,0,960,72,25);
            bossView.resourceText=Label("Live stage and difficulty",boss,"",24,-25,960,25,17);
            var cast=Rect("Cast",boss,227,98,512,48);
            Picture("Track",cast,"Boss/cast-track",0,0,512,48);
            bossView.castFill=Gauge(cast,"Boss/cast-fill",512,48,new Rect(15f/512,20f/48,482f/512,8f/48),7f/512);
            Picture("Frame",cast,"Boss/cast-frame",0,0,512,48);
            bossView.castNameText=Label("Live cast name",cast,"",0,31,350,28,20);bossView.castNameText.alignment=TextAnchor.MiddleLeft;
            bossView.castTimeText=Label("Live cast time",cast,"",360,31,152,28,20);bossView.castTimeText.alignment=TextAnchor.MiddleRight;
            var target=root.GetComponentInChildren<AstellaBossHUDV4>(true);
            if(target?.targetRoot!=null){Place((RectTransform)target.targetRoot.transform,boss,227,171,512,30);target.repositionTargetWhenIdle=false;target.scaleTargetWithComposition=false;}
            Hide(hud.bossRoot);

            var party=Rect("Party",display,44,82,340,300);
            var rows=new List<AstellaPrismHUDBridge.PartyMirror>();
            foreach(var row in bridge.partySource.rows)
            {
                var button=(RectTransform)row.button.transform;HideChildren(button);Place(button,party,0,row.sourceIndex*74,340,68);
                if(button.TryGetComponent<Graphic>(out var oldHit)){oldHit.enabled=false;oldHit.raycastTarget=false;}
                var visual=Rect("Prism member",button,0,0,340,68);
                var view=visual.gameObject.AddComponent<PrismV4Bindings>();view.module=PrismV4Bindings.Module.PartyRow;view.button=row.button;
                var rowHit=Picture("Row hit surface",visual,null,0,0,340,68);rowHit.color=Color.clear;rowHit.raycastTarget=true;
                Picture("Role background",visual,"Party/role-background",0,0,68,68);
                view.artwork=Picture("Existing role icon",visual,null,11,11,46,46);view.artwork.preserveAspect=true;
                view.frame=Picture("Role frame",visual,"Party/role-frame",0,0,68,68);view.frame.raycastTarget=true;
                view.nameText=Label("Live name",visual,"",82,0,232,26,18);view.nameText.alignment=TextAnchor.MiddleLeft;
                var bar=Rect("Health",visual,76,27,174,22);
                Picture("Track",bar,"Party/bar-track",0,0,174,22);view.healthFill=Gauge(bar,"Party/bar-fill",174,22,new Rect(14f/320,12f/40,292f/320,16f/40),10f/320);
                Picture("Frame",bar,"Party/bar-frame",0,0,174,22);view.criticalHealthSprite=Sprite("Party/bar-fill-critical");
                if(row.shieldGauge==null)
                    row.shieldGauge=Rect("Shield data",button.Find("Legacy data sources"),0,0,1,1).gameObject.AddComponent<AstellaPointedGauge>();
                var shield=Gauge(bar,"Party/bar-fill",174,10,new Rect(14f/320,12f/40,292f/320,16f/40),10f/320);
                shield.name="Live shield";((RectTransform)shield.transform).anchoredPosition=new Vector2(0,3);shield.color=new Color(.68f,.87f,1,.9f);
                view.selectedOverlay=Picture("Selected",bar,"Party/selected-overlay",0,0,174,22);
                view.deadOverlay=Picture("Defeated",bar,"Party/dead-overlay",0,0,174,22);
                view.valueText=Label("Live health",visual,"",250,27,58,22,15);
                view.keyText=Label("Live party key",visual,"",0,48,68,19,13);
                var aggro=Label("Aggro",visual,"대상",260,3,65,23,15);aggro.color=new Color(1,.68f,.36f);
                int n=0;
                foreach(var status in row.statuses??Array.Empty<AstellaPartyHUDV4.StatusView>())
                    if(status?.root!=null)Place((RectTransform)status.root.transform,visual,80+(n++)*24,49,23,19);
                row.button.targetGraphic=view.frame;row.button.transition=Selectable.Transition.None;
                rows.Add(new AstellaPrismHUDBridge.PartyMirror{sourceIndex=row.sourceIndex,view=view,shieldFill=shield,aggroOverlay=aggro.gameObject});
            }
            bridge.partyRows=rows.ToArray();
            Hide(bridge.partySource.panelRoot);
            hud.combatLayoutVersion=Mathf.Max(1,hud.combatLayoutVersion);
            var layout=hud.gameObject.AddComponent<AstellaPrismHUDLayout>();
            layout.entries=new[]{Entry("skills.left","스킬 1–4",left),Entry("skills.right","스킬 Q F R Space",right),
                Entry("hp","체력",hp),Entry("identity","직업 자원",resource),Entry("ultimate","궁극기",center),
                Entry("ultimate.resource","궁극기 충전 수치",(RectTransform)central.valueText.transform),
                Entry("boss","보스 체력",boss),Entry("boss.cast","보스 시전",cast),Entry("party","파티",party)};
            InitializePreviewState(bridge);
            ArrangeAuxiliaryPanels(root);
        }

        [MenuItem("Tools/Astella/Prism V4/Prepare Neutral Preview %#F9")]
        public static void PrepareNeutralPreview()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before preparing a prefab preview.");
            string path=AssetDatabase.GetAssetPath(RaidUIPresentation.Settings.hudPrefab);
            var root=PrefabUtility.LoadPrefabContents(path);
            try{var bridge=root.GetComponentInChildren<AstellaPrismHUDBridge>(true);if(bridge==null)throw new InvalidOperationException("Active HUD is not Prism.");InitializePreviewState(bridge);PrefabUtility.SaveAsPrefabAsset(root,path);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
            Debug.Log("PRISM NEUTRAL PREVIEW PREPARED: state overlays off; icon availability remains runtime-owned.");
        }
        static void InitializePreviewState(AstellaPrismHUDBridge bridge)
        {
            bridge.centerView.readyGlow.SetActive(false);bridge.centerView.unavailableOverlay.SetActive(false);
            foreach(var slot in bridge.slots){slot.view.artwork.enabled=false;slot.view.disabledOverlay.enabled=false;slot.view.cooldownShade.enabled=false;}
            foreach(var row in bridge.partyRows){row.view.artwork.enabled=false;row.view.selectedOverlay.enabled=false;row.view.deadOverlay.enabled=false;if(row.aggroOverlay!=null)row.aggroOverlay.SetActive(false);}
        }

        [MenuItem("Tools/Astella/Prism V4/Refine Main Layout %#F7")]
        public static void InstallResponsiveFit()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before adding responsive composition fit.");
            string path=AssetDatabase.GetAssetPath(RaidUIPresentation.Settings.hudPrefab);
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var bridge=root.GetComponentInChildren<AstellaPrismHUDBridge>(true);
                if(bridge==null)throw new InvalidOperationException("Active HUD is not Prism.");
                var display=root.transform.Find("Prism V4");
                if(display.GetComponent<AstellaPrismCanvasFit>()==null)display.gameObject.AddComponent<AstellaPrismCanvasFit>();
                var potion=bridge.hud.slots.FirstOrDefault(s=>!s.sharedResurrection&&s.slot==CharacterSlot.Potion);
                if(potion?.button!=null)Place((RectTransform)potion.button.transform,bridge.playerView.transform,30,850,72,64);
                foreach(var slot in bridge.slots)
                    if(slot.view?.cooldownShade!=null)slot.view.cooldownShade.sprite=Sprite("States/cooldown-overlay");
                ArrangeAuxiliaryPanels(root);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            Debug.Log("PRISM MAIN LAYOUT REFINED: composition fit and utility bounds; connection and enemy controls clear the party rows.");
        }
        static void ArrangeAuxiliaryPanels(GameObject root)
        {
            foreach(var rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if(rect.name=="LAN_Status")
                {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(250,-12);}
                else if(rect.name=="EnemyList_LeftHUD")
                {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,1);rect.anchoredPosition=new Vector2(-250,-370);}
            }
        }

        static AstellaPrismHUDLayout.Entry Entry(string id,string name,RectTransform rect)=>new AstellaPrismHUDLayout.Entry{id=id,name=name,rect=rect};
        static void Hide(GameObject go){if(go==null)return;var group=go.GetComponent<CanvasGroup>();if(group==null)group=go.AddComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=false;}
        static void HideChildren(RectTransform parent)
        {
            var children=parent.Cast<Transform>().ToArray();
            var hidden=Rect("Legacy data sources",parent,0,0,parent.rect.width,parent.rect.height);
            foreach(var child in children)child.SetParent(hidden,false);
            Hide(hidden.gameObject);
        }
        static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();Place(rect,parent,x,y,w,h);return rect;}
        static void Place(RectTransform rect,Transform parent,float x,float y,float w,float h)
        {rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;var le=rect.GetComponent<LayoutElement>();if(le!=null)le.ignoreLayout=true;}
        static Image Picture(string name,Transform parent,string sprite,float x,float y,float w,float h)
        {var image=Rect(name,parent,x,y,w,h).gameObject.AddComponent<Image>();image.sprite=sprite!=null?Sprite(sprite):null;image.raycastTarget=false;return image;}
        static Text Label(string name,Transform parent,string value,float x,float y,float w,float h,int size)
        {var text=Rect(name,parent,x,y,w,h).gameObject.AddComponent<Text>();text.font=font;text.text=value;text.fontSize=size;text.color=new Color(.93f,.92f,1);text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;return text;}
        static PrismV4Gauge Gauge(Transform parent,string sprite,float w,float h,Rect inner,float tip)
        {var gauge=Rect("Live fill",parent,0,0,w,h).gameObject.AddComponent<PrismV4Gauge>();gauge.sprite=Sprite(sprite);gauge.innerBounds=inner;gauge.pointWidth=tip;gauge.raycastTarget=false;gauge.SetValue(1);return gauge;}
        static Sprite Sprite(string name)
        {return AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/AstellaPrismV4/Sprites/"+name+".png")??throw new InvalidOperationException("Missing Prism sprite "+name);}
        static void Folder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;string parent=path.Substring(0,path.LastIndexOf('/'));Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
    }
}
#endif
