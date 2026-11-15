#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    // Explicit editor operation, never an import callback or a runtime layout reset.
    public static class RaidUICleanDesign
    {
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        private static readonly Color Ink = new Color(.035f,.055f,.085f,.94f);
        private static readonly Color Surface = new Color(.075f,.105f,.15f,.96f);
        private static readonly Color Line = new Color(.24f,.32f,.4f,.72f);
        private static readonly Color Gold = new Color(.83f,.7f,.44f);
        private static readonly Color TextColor = new Color(.91f,.94f,.97f);
        private static readonly Color Muted = new Color(.62f,.71f,.79f);
        private static readonly Color Teal = new Color(.3f,.78f,.8f);

        [MenuItem("Tools/RaidRogue/UI/Improve Combat Readability Only")]
        public static void ImproveReadability()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode first.");
            string project = Directory.GetParent(Application.dataPath).FullName;
            if (File.Exists(Path.Combine(project,".git"))) throw new InvalidOperationException("Use the original Unity project.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile?.hudPrefab==null || profile.theme==null) throw new InvalidOperationException("Active UI profile missing.");
            var paths = new[]{AssetDatabase.GetAssetPath(profile.hudPrefab), AssetDatabase.GetAssetPath(profile.View("metrics")), AssetDatabase.GetAssetPath(profile.theme)}.Distinct().ToArray();
            foreach (var path in paths)
                if (!path.StartsWith("Assets/UI/CleanNavy-",StringComparison.Ordinal)) throw new InvalidOperationException("Only active CleanNavy copies may be edited: "+path);
            string backup = Path.Combine(project,"Logs","UIRepairBackups","readability-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(backup);
            foreach (var path in paths)
            {
                File.Copy(path,Path.Combine(backup,Path.GetFileName(path)));
                File.Copy(path+".meta",Path.Combine(backup,Path.GetFileName(path)+".meta"));
            }
            int edited = 0;
            foreach (var path in paths.Where(x=>x.EndsWith(".prefab")))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var references = root.GetComponentsInChildren<RaidUIView>(true).SelectMany(v=>v.references).ToList();
                    foreach (var hud in root.GetComponentsInChildren<RaidUIRoot>(true))
                        references.AddRange(hud.presenters.Where(p=>p.typeName.Contains("CombatMetricsUI")).SelectMany(p=>p.references));
                    foreach (var entry in references.Where(r=>r.path=="_results" || r.path=="_resultBox"))
                    {
                        var go = entry.target as GameObject ?? (entry.target as Component)?.gameObject;
                        var image = go != null ? go.GetComponent<Image>() : null;
                        if (image==null) continue;
                        image.color = entry.path=="_results" ? new Color(.02f,.03f,.05f,.98f) : new Color(.045f,.065f,.1f,1f);
                        var style=image.GetComponent<RaidUIStyle>(); if(style!=null) style.useTheme=false;
                        edited++;
                    }
                    if (PrefabUtility.SaveAsPrefabAsset(root,path)==null) throw new IOException(path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            profile.theme.compactDamageText=true;
            EditorUtility.SetDirty(profile.theme); AssetDatabase.SaveAssets();
            RaidUIAuthoring.Validate(profile);
            Debug.Log("COMBAT READABILITY: "+edited+" result backgrounds updated; compact damage enabled. Backup: "+backup);
        }

        [MenuItem("Tools/RaidRogue/UI/Refine Active Clean Navy Design")]
        public static void Refine()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Stop Play Mode and close Prefab Mode first.");
            var profile=AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if(profile?.hudPrefab==null||!AssetDatabase.GetAssetPath(profile.hudPrefab).StartsWith("Assets/UI/CleanNavy-",StringComparison.Ordinal))
                throw new InvalidOperationException("Refine only edits the new CleanNavy copy.");
            var assets=new List<Object>{profile.hudPrefab,profile.skillTemplate,profile.hotbarTemplate,profile.partyTemplate,
                profile.resourceTemplate,profile.statusTemplate,profile.formButtonTemplate,profile.formTextTemplate};
            assets.AddRange(profile.views.Select(x=>(Object)x.prefab));
            foreach(var path in assets.Where(x=>x!=null).Select(AssetDatabase.GetAssetPath).Distinct())
            {
                if(!path.StartsWith("Assets/UI/CleanNavy-",StringComparison.Ordinal))throw new InvalidOperationException("External prefab: "+path);
                var root=PrefabUtility.LoadPrefabContents(path);
                try{Style(root,profile.theme);Layout(root);if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new IOException(path);}
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            if(profile.theme!=null){profile.theme.combatTextScale=.62f;EditorUtility.SetDirty(profile.theme);}
            RaidUIAuthoring.Validate(profile);AssetDatabase.SaveAssets();
            Debug.Log("CLEAN NAVY UI REFINED: active copied prefabs only.");
        }

        [MenuItem("Tools/RaidRogue/UI/Apply Clean Navy Design (Copy Assets)")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play Mode and close Prefab Mode before applying the UI design.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile == null || profile.hudPrefab == null) throw new InvalidOperationException("Active UI profile is missing.");
            string project = Directory.GetParent(Application.dataPath).FullName;
            if (File.Exists(Path.Combine(project,".git"))) throw new InvalidOperationException("Use the original Unity project.");
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backup = Path.Combine(project,"Logs","UIRepairBackups","clean-design-"+stamp);
            Directory.CreateDirectory(backup);
            File.Copy(ProfilePath,Path.Combine(backup,"RaidUIPresentation.asset"));
            File.Copy(ProfilePath+".meta",Path.Combine(backup,"RaidUIPresentation.asset.meta"));
            string folder = "Assets/UI/CleanNavy-"+stamp;
            AssetDatabase.CreateFolder("Assets/UI","CleanNavy-"+stamp);
            var copies = new Dictionary<string,string>();
            string Copy(Object asset)
            {
                if (asset == null) return null;
                string source = AssetDatabase.GetAssetPath(asset);
                if (copies.TryGetValue(source,out var known)) return known;
                string target = folder+"/"+Path.GetFileName(source);
                if (!AssetDatabase.CopyAsset(source,target)) throw new IOException("Could not copy "+source);
                copies.Add(source,target); return target;
            }
            var oldJson = EditorJsonUtility.ToJson(profile);
            try
            {
                string hudPath = Copy(profile.hudPrefab);
                foreach (var entry in profile.views) Copy(entry.prefab);
                var templates = new[]{profile.skillTemplate,profile.hotbarTemplate,profile.partyTemplate,profile.resourceTemplate,
                    profile.statusTemplate,profile.formButtonTemplate,profile.formTextTemplate};
                foreach (var template in templates) Copy(template);
                var theme = profile.theme != null ? Object.Instantiate(profile.theme) : ScriptableObject.CreateInstance<RaidUITheme>();
                theme.name="Clean Navy Theme"; theme.panel=Ink;theme.text=TextColor;theme.accent=Gold;
                theme.health=new Color(.32f,.74f,.59f);theme.resource=Teal;
                theme.combatTextScale=.62f;
                theme.ready=TextColor;theme.selected=Teal;theme.unavailable=Muted;theme.locked=new Color(.34f,.4f,.47f);
                AssetDatabase.CreateAsset(theme,folder+"/CleanNavyTheme.asset");
                foreach (string path in copies.Values.Where(x=>x.EndsWith(".prefab")))
                {
                    var root=PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        // The copy owns its nested design; the old prefabs remain untouched.
                        foreach (var nested in root.GetComponentsInChildren<Transform>(true).Select(t=>t.gameObject).ToArray())
                            if (nested != root && PrefabUtility.IsAnyPrefabInstanceRoot(nested))
                                PrefabUtility.UnpackPrefabInstance(nested,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                        Style(root,theme);
                        Layout(root);
                        if (root.GetComponentsInChildren<Transform>(true).Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))
                            throw new InvalidOperationException("Missing scripts in "+path);
                        if (PrefabUtility.SaveAsPrefabAsset(root,path)==null) throw new IOException("Could not save "+path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                profile.hudPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(hudPath).GetComponent<RaidUIRoot>();
                foreach (var entry in profile.views)
                    if(entry.prefab!=null)entry.prefab=AssetDatabase.LoadAssetAtPath<GameObject>(copies[AssetDatabase.GetAssetPath(entry.prefab)]).GetComponent<RaidUIView>();
                RaidUIRepeatedItem Remap(RaidUIRepeatedItem item)=>item==null?null:
                    AssetDatabase.LoadAssetAtPath<GameObject>(copies[AssetDatabase.GetAssetPath(item)]).GetComponent<RaidUIRepeatedItem>();
                profile.skillTemplate=Remap(profile.skillTemplate);profile.hotbarTemplate=Remap(profile.hotbarTemplate);
                profile.partyTemplate=Remap(profile.partyTemplate);profile.resourceTemplate=Remap(profile.resourceTemplate);
                profile.statusTemplate=Remap(profile.statusTemplate);profile.formButtonTemplate=Remap(profile.formButtonTemplate);
                profile.formTextTemplate=Remap(profile.formTextTemplate);profile.theme=theme;
                RaidUIAuthoring.Validate(profile);
                EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
                Selection.activeObject=profile.hudPrefab;
                Debug.Log("CLEAN NAVY UI APPLIED: "+folder+" | Previous profile backup: "+backup);
            }
            catch
            {
                EditorJsonUtility.FromJsonOverwrite(oldJson,profile);EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
                throw;
            }
        }

        private static void Style(GameObject root,RaidUITheme theme)
        {
            foreach(var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                var style=graphic.GetComponent<RaidUIStyle>()??graphic.gameObject.AddComponent<RaidUIStyle>();
                style.graphic=graphic;style.useTheme=false;style.themeOverride=theme;
                style.ready=TextColor;style.unavailable=Muted;style.selected=Teal;style.locked=new Color(.34f,.4f,.47f);
                if(graphic is Text text)
                {
                    text.color=TextColor;text.lineSpacing=1.12f;
                    text.fontSize=Mathf.Clamp(text.fontSize,12,32);
                    text.fontStyle=FontStyle.Normal;
                    // No enlarged best-fit text when a dynamic label becomes short.
                    if(text.resizeTextForBestFit){text.resizeTextMaxSize=text.fontSize;text.resizeTextMinSize=Mathf.Min(12,text.fontSize);}
                    text.raycastTarget=false;
                }
                if(graphic is Image image)
                {
                    string name=image.name.ToLowerInvariant();
                    if(name=="raid_mainmenu")image.color=new Color(.025f,.04f,.065f,1);
                    else if(name=="icon (optional)") image.color=Color.white;
                    else if(image.GetComponent<Button>()!=null) image.color=Surface;
                    else if(name.Contains("overlay")||name.Contains("modalroot")) image.color=new Color(.012f,.02f,.035f,.91f);
                    else if(name.Contains("cooldown")) image.color=new Color(.01f,.025f,.04f,.78f);
                    else if(name.Contains("shield"))image.color=Gold;
                    else if(name.Contains("aggro"))image.color=new Color(.9f,.3f,.36f,image.color.a);
                    else if(name.Contains("fill"))
                        image.color=name.Contains("cast")?Gold:
                            name.Contains("boss")||image.GetComponentsInParent<RectTransform>(true).Any(t=>t.name.StartsWith("EnemyRow_",StringComparison.Ordinal))
                                ?new Color(.73f,.29f,.35f):new Color(.32f,.74f,.59f);
                    else if(name.Contains("border")||name.Contains("highlight"))image.color=Line;
                    else if(image.color.a>.15f && !name.Contains("marker") && name!="boss" && name!="player" && name!="arena (north up)")image.color=Ink;
                }
            }
            foreach(var outline in root.GetComponentsInChildren<Outline>(true))
            {
                var color=outline.effectColor;
                // Selection outlines retain their visibility and distinct state color.
                if(!outline.name.Contains("Row")&&!outline.name.Contains("Target"))outline.effectColor=new Color(Line.r,Line.g,Line.b,color.a*.65f);
                outline.effectDistance=new Vector2(1,-1);
            }
            foreach(var button in root.GetComponentsInChildren<Button>(true))
            {
                var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.25f,1.3f,1.35f);
                colors.pressedColor=new Color(.68f,.85f,.87f);colors.selectedColor=Color.white;
                colors.disabledColor=new Color(.48f,.52f,.58f,.8f);colors.fadeDuration=.1f;button.colors=colors;
            }
        }

        private static RectTransform Find(GameObject root,string name)=>root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r=>r.name==name);
        private static void Place(RectTransform r,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
        {if(r==null)return;r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=position;r.sizeDelta=size;}
        private static void Scale(GameObject root,string name,float value)
        {var r=Find(root,name);if(r!=null)r.localScale=Vector3.one*value;}
        private static T Reference<T>(RaidUIView view,string key) where T:Object => view.references.FirstOrDefault(x=>x.path==key)?.target as T;
        private static void Layout(GameObject root)
        {
            Place(Find(root,"Top_StageHeader_Badge"),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-18),new Vector2(700,27));
            Place(Find(root,"Top_BossHP_Frame"),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-51),new Vector2(700,65));
            Place(Find(root,"Top_BossCastBar_Frame"),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-122),new Vector2(610,27));
            Place(Find(root,"TargetInfoHUD_FFXIV"),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-159),new Vector2(680,62));
            Scale(root,"TargetInfoHUD_FFXIV",.86f);
            Place(Find(root,"Center_MechanicAlert_Text"),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-238),new Vector2(880,38));
            var debug=Find(root,"HUD_TopLeft_Panel");if(debug!=null)debug.gameObject.SetActive(false);
            Place(Find(root,"PartyList_LeftHUD"),new Vector2(0,1),new Vector2(0,1),new Vector2(24,-220),new Vector2(335,316));
            Scale(root,"PartyList_LeftHUD",.84f);
            var party = root.GetComponentInChildren<PartyListUI>(true);
            if (party != null)
                for (int i=0;i<party.memberRows.Length;i++)
                    if (party.memberRows[i]?.roleBadgeBgImage != null)
                    {
                        var badge=party.memberRows[i].roleBadgeBgImage;
                        var state=badge.GetComponent<RaidUIStyle>()??badge.gameObject.AddComponent<RaidUIStyle>();
                        state.graphic=badge;state.useStateColors=true;
                    }
            foreach(var r in root.GetComponentsInChildren<RectTransform>(true).Where(r=>r.name=="EnemyList_LeftHUD"))
            {Place(r,new Vector2(0,1),new Vector2(0,1),new Vector2(24,-508),r.sizeDelta);r.localScale=Vector3.one*.84f;}
            var hud = root.GetComponent<RaidUIRoot>();
            var enemyBinding = hud?.presenters.FirstOrDefault(b=>b.typeName.StartsWith("RaidRogue.EnemyListUI,",StringComparison.Ordinal));
            var boundEnemy = enemyBinding?.references.FirstOrDefault(r=>r.path=="_panelRoot")?.target as GameObject;
            if(boundEnemy!=null)
                foreach(var r in root.GetComponentsInChildren<RectTransform>(true).Where(r=>r.name=="EnemyList_LeftHUD"))
                    if(r.gameObject!=boundEnemy)r.gameObject.SetActive(false);
            Place(Find(root,"Minimap"),Vector2.one,Vector2.one,new Vector2(-24,-158),new Vector2(216,244));Scale(root,"Minimap",.72f);
            Place(Find(root,"TopRight_ViewModeToggle_Button"),Vector2.one,Vector2.one,new Vector2(-24,-22),new Vector2(250,48));
            Place(Find(root,"RoleSwitchButton_TopRight"),Vector2.one,Vector2.one,new Vector2(-24,-78),new Vector2(250,36));
            Place(Find(root,"Settings"),Vector2.one,Vector2.one,new Vector2(-24,-120),new Vector2(250,30));
            Place(Find(root,"Bottom_SkillHotbar_Panel"),new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,16),new Vector2(640,142));
            var hotbar=root.GetComponentInChildren<SkillHotbarUI>(true);
            if(hotbar!=null)
            {
                for(int i=0;i<hotbar.slots.Length;i++)
                {
                    var slot=hotbar.slots[i];if(slot?.slotButton==null)continue;
                    Place(slot.slotButton.GetComponent<RectTransform>(),new Vector2(.5f,0),new Vector2(.5f,.5f),new Vector2(-201+i*134,68),new Vector2(122,96));
                    if(slot.skillNameText!=null)slot.skillNameText.fontSize=16;
                    if(slot.keybindBadgeText!=null)slot.keybindBadgeText.fontSize=14;
                    if(slot.stageBadgeText!=null)slot.stageBadgeText.fontSize=12;
                }
                if(hotbar.gcdSummaryText!=null)hotbar.gcdSummaryText.gameObject.SetActive(false);
                if(hotbar.playerCastBarRoot!=null)Place(hotbar.playerCastBarRoot.GetComponent<RectTransform>(),new Vector2(.5f,1),new Vector2(.5f,0),new Vector2(0,90),new Vector2(480,24));
            }
            foreach(var name in new[]{"CommonPotion_Slot","CommonDash_Slot"})Scale(root,name,.78f);
            var meter=Find(root,"CombatMetrics_RightHUD");
            if(meter!=null)
            {
                Place(meter,new Vector2(1,0),new Vector2(1,0),new Vector2(-24,260),new Vector2(380,440));
                meter.localScale=Vector3.one*.82f;
                // Fold grows downward from this top edge, so keep the pivot at the top.
                meter.pivot=Vector2.one;meter.anchoredPosition=new Vector2(-24,440);
                var body=meter.Find("Body");if(body!=null)body.gameObject.SetActive(false);
                var caption=meter.Find("Fold")?.GetComponentInChildren<Text>();if(caption!=null)caption.text="펼치기";
            }
            foreach(var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if(view.viewId=="character")CharacterDetails(view);
                if(view.viewId=="connection")
                {
                    Place(Find(view.gameObject,"RunFlow_Status"),new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-285),new Vector2(780,38));
                    var lan=Find(view.gameObject,"LAN_Status");if(lan!=null)lan.gameObject.SetActive(false);
                }
            }
        }

        private static void CharacterDetails(RaidUIView view)
        {
            var slots=Find(view.gameObject,"CharacterSlots");if(slots==null)return;
            slots.anchoredPosition=new Vector2(0,40);slots.localScale=Vector3.one*.88f;
            var resources=Reference<Text>(view,"_resources");if(resources!=null){resources.fontSize=14;resources.color=Gold;}
            var deferred=Reference<Text>(view,"_deferred");if(deferred!=null){deferred.fontSize=12;deferred.rectTransform.anchoredPosition=new Vector2(0,80);}
            var tooltip=Reference<Text>(view,"_tooltip");if(tooltip!=null){tooltip.fontSize=16;tooltip.rectTransform.anchoredPosition=new Vector2(0,160);}
            var status=Find(view.gameObject,"Status effects");if(status!=null)status.anchoredPosition=new Vector2(0,162);
            var existingToggle=Find(view.gameObject,"Character details toggle");
            if(existingToggle!=null)Place(existingToggle,new Vector2(0,1),new Vector2(1,0),new Vector2(-15,8),new Vector2(122,28));
            if(Find(view.gameObject,"Character details")!=null)return;
            var stats=Reference<Text>(view,"_stats");var identity=Reference<Text>(view,"_identity");
            if(stats==null||identity==null)return;
            var panel=new GameObject("Character details",typeof(RectTransform),typeof(Image));panel.transform.SetParent(slots,false);
            var rect=panel.GetComponent<RectTransform>();Place(rect,new Vector2(.5f,.5f),new Vector2(.5f,0),new Vector2(0,110),new Vector2(760,150));
            panel.GetComponent<Image>().color=Ink;
            identity.transform.SetParent(rect,false);stats.transform.SetParent(rect,false);
            Place(identity.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,35),new Vector2(720,62));
            Place(stats.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-35),new Vector2(720,62));
            identity.fontSize=14;stats.fontSize=13;stats.color=Muted;
            var go=new GameObject("Character details toggle",typeof(RectTransform),typeof(Image),typeof(Button),typeof(RaidUIFoldout));go.transform.SetParent(slots,false);
            Place(go.GetComponent<RectTransform>(),new Vector2(0,1),new Vector2(1,0),new Vector2(-15,8),new Vector2(122,28));go.GetComponent<Image>().color=Surface;
            var textGo=new GameObject("Caption",typeof(RectTransform),typeof(Text));textGo.transform.SetParent(go.transform,false);
            var label=textGo.GetComponent<Text>();label.font=stats.font;label.fontSize=13;label.color=Muted;label.alignment=TextAnchor.MiddleCenter;label.text="직업 정보 +";label.raycastTarget=false;
            var tr=label.rectTransform;tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;
            var fold=go.GetComponent<RaidUIFoldout>();fold.button=go.GetComponent<Button>();fold.button.targetGraphic=go.GetComponent<Image>();fold.panel=panel;fold.caption=label;
            panel.SetActive(false);
        }
    }
}
#endif
