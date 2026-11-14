#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    public static class RaidUIAuthoring
    {
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        private static bool _minimapUpgradePending;

        [InitializeOnLoadMethod]
        private static void ScheduleMissingMinimapUpgrade()
        {
            // Upgrade the existing artist-editable HUD once, after script import. Never
            // touch a prefab while Play Mode, compilation, or Prefab Mode is active.
            if (_minimapUpgradePending) return;
            _minimapUpgradePending = true;
            EditorApplication.update += UpgradeMissingMinimapWhenSafe;
        }

        private static void UpgradeMissingMinimapWhenSafe()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
                return;
            EditorApplication.update -= UpgradeMissingMinimapWhenSafe;
            _minimapUpgradePending = false;
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile == null || profile.hudPrefab == null) return;
            try
            {
                EnsureLobbyLayoutAsset();
                var minimap = profile.hudPrefab.GetComponentInChildren<RaidMinimapUI>(true);
                if (minimap == null || LegacyMinimapPanel(minimap) != null) AddMinimapToActiveHud();
                if (profile.hudPrefab.GetComponentInChildren<RaidLobbyUI>(true) == null) AddLobbyPromptsToActiveHud();
            }
            catch (Exception exception)
            {
                Debug.LogError("Automatic RaidHUD minimap upgrade failed; the existing HUD was left active. " + exception);
            }
        }

        private static void EnsureLobbyLayoutAsset()
        {
            const string path = "Assets/Resources/UI/RaidLobbyLayout.asset";
            if (AssetDatabase.LoadAssetAtPath<RaidLobbyLayout>(path) != null) return;
            Folder("Assets/Resources/UI");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<RaidLobbyLayout>(), path);
            AssetDatabase.SaveAssets();
            Debug.Log("Editable lobby layout created: " + path);
        }

        [MenuItem("Tools/RaidRogue/UI/Add Lobby Prompts to Active RaidHUD")]
        public static void AddLobbyPromptsToActiveHud()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play Mode before editing RaidHUD."); return; }
            EnsureLobbyLayoutAsset();
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            Validate(profile);
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            var openStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (openStage != null && openStage.assetPath == path)
            { Debug.LogWarning("Save and close RaidHUD Prefab Mode before adding lobby prompts."); return; }
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var existing = contents.GetComponentInChildren<RaidLobbyUI>(true);
                var existingRect = existing != null ? existing.GetComponent<RectTransform>() : null;
                bool repairDefaultRoot = existingRect != null && existingRect.name == "Lobby Prompts" &&
                    existingRect.anchorMin == new Vector2(.5f, .5f) && existingRect.anchorMax == new Vector2(.5f, .5f) &&
                    existingRect.sizeDelta == new Vector2(100, 100) && existingRect.anchoredPosition == Vector2.zero;
                if (existing != null && !repairDefaultRoot) return;
                string backupDirectory = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName,
                    "Logs", "UIRepairBackups", "lobby-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                System.IO.Directory.CreateDirectory(backupDirectory);
                System.IO.File.Copy(path, System.IO.Path.Combine(backupDirectory, "RaidHUD.prefab"));
                System.IO.File.Copy(path + ".meta", System.IO.Path.Combine(backupDirectory, "RaidHUD.prefab.meta"));
                if (existing == null) RaidLobbyUI.CreateDefault(contents.transform);
                else
                {
                    existingRect.anchorMin = Vector2.zero; existingRect.anchorMax = Vector2.one;
                    existingRect.offsetMin = existingRect.offsetMax = Vector2.zero;
                }
                ValidateScripts(contents);
                var saved = PrefabUtility.SaveAsPrefabAsset(contents, path);
                if (saved == null || saved.GetComponentInChildren<RaidLobbyUI>(true) == null)
                    throw new InvalidOperationException("Lobby prompts prefab save did not complete.");
                Debug.Log("Editable lobby prompts added to RaidHUD: " + path + ". Backup: " + backupDirectory);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static RectTransform LegacyMinimapPanel(RaidMinimapUI minimap)
        {
            if (minimap == null) return null;
            var panel = new SerializedObject(minimap).FindProperty("panel").objectReferenceValue as RectTransform;
            // Migrate only the untouched generated placement; artist layouts remain authoritative.
            return panel != null && panel.name == "Minimap" &&
                panel.anchorMin == new Vector2(1, 1) && panel.anchorMax == new Vector2(1, 1) &&
                panel.pivot == new Vector2(1, 1) && panel.sizeDelta == new Vector2(216, 244) &&
                panel.anchoredPosition == new Vector2(-20, -154) ? panel : null;
        }

        [MenuItem("Tools/RaidRogue/UI/Add Minimap to Active RaidHUD")]
        public static void AddMinimapToActiveHud()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Stop Play Mode before adding the minimap to the active RaidHUD prefab. No asset was changed.");
                return;
            }
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            Validate(profile);
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            var openStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (openStage != null && openStage.assetPath == path)
            {
                Debug.LogWarning("Save and close the active RaidHUD prefab before adding the minimap. No asset was changed.");
                return;
            }
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var existing = contents.GetComponentInChildren<RaidMinimapUI>(true);
                var legacyPanel = LegacyMinimapPanel(existing);
                if (existing != null && legacyPanel == null)
                {
                    Debug.Log("Active RaidHUD already has an editable minimap. Nothing changed.");
                    return;
                }
                string backupDirectory = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName,
                    "Logs", "UIRepairBackups", "minimap-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                System.IO.Directory.CreateDirectory(backupDirectory);
                System.IO.File.Copy(path, System.IO.Path.Combine(backupDirectory, "RaidHUD.prefab"));
                System.IO.File.Copy(path + ".meta", System.IO.Path.Combine(backupDirectory, "RaidHUD.prefab.meta"));
                if (existing == null) RaidMinimapUI.CreateDefault(contents.transform);
                else legacyPanel.anchoredPosition = new Vector2(-470, -250);
                ValidateScripts(contents);
                var saved = PrefabUtility.SaveAsPrefabAsset(contents, path);
                if (saved == null || saved.GetComponentInChildren<RaidMinimapUI>(true) == null)
                    throw new InvalidOperationException("Minimap prefab save did not complete.");
                Selection.activeObject = saved;
                EditorGUIUtility.PingObject(saved);
                Debug.Log("Editable minimap added to active RaidHUD: " + path + ". Backup: " + backupDirectory);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        [MenuItem("Tools/RaidRogue/UI/Organize Active RaidHUD (Copy)")]
        public static void OrganizeActiveHud()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before organizing the HUD prefab.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            Validate(profile);
            string sourcePath = AssetDatabase.GetAssetPath(profile.hudPrefab);
            var contents = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                var root = contents.GetComponent<RectTransform>();
                if (root == null) throw new InvalidOperationException("Active RaidHUD has no RectTransform.");
                if (root.Find("01 - Combat HUD") != null)
                {
                    Debug.Log("RaidHUD is already organized. No prefab or profile was changed.");
                    return;
                }
                // Only group the original, passive canvas children. Menu/modal panels keep
                // their own root siblings so RaidUIOrder can still raise them independently.
                GroupContiguous(root, "01 - Combat HUD", "MeleeRange_Feedback", "Top_StageHeader_Badge",
                    "Top_BossHP_Frame", "Top_BossCastBar_Frame", "Center_MechanicAlert_Text",
                    "Bottom_SkillHotbar_Panel", "HUD_TopLeft_Panel", "BossClear_CenterBanner");
                ValidateScripts(contents);
                string folder = "Assets/UI/Generated/Organized-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                Folder(folder);
                var saved = PrefabUtility.SaveAsPrefabAsset(contents, folder + "/RaidHUD.prefab");
                if (saved == null) throw new InvalidOperationException("Organized HUD prefab was not saved.");
                foreach (var binding in saved.GetComponent<RaidUIRoot>().presenters)
                foreach (var reference in binding.references)
                {
                    var target = reference.target;
                    var targetTransform = target is GameObject go ? go.transform : target is Component component ? component.transform : null;
                    if (targetTransform != null && targetTransform != saved.transform && !targetTransform.IsChildOf(saved.transform))
                        throw new InvalidOperationException("Organized HUD has a reference outside the copied prefab: " + binding.typeName + "/" + reference.path);
                }
                var previous = profile.hudPrefab;
                try
                {
                    profile.hudPrefab = saved.GetComponent<RaidUIRoot>();
                    Validate(profile);
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssets();
                }
                catch { profile.hudPrefab = previous; throw; }
                Selection.activeObject = saved;
                EditorGUIUtility.PingObject(saved);
                Debug.Log("Organized RaidHUD activated: " + AssetDatabase.GetAssetPath(saved) + ". Previous prefab remains at " + sourcePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static void GroupContiguous(RectTransform root, string label, params string[] names)
        {
            var children = names.Select(name => root.Cast<Transform>().SingleOrDefault(child => child.name == name)).ToArray();
            if (children.Any(child => child == null)) throw new InvalidOperationException("RaidHUD changed; expected combat HUD children were not found. No asset was saved.");
            int first = children[0].GetSiblingIndex();
            for (int i = 0; i < children.Length; i++)
                if (children[i].GetSiblingIndex() != first + i)
                    throw new InvalidOperationException("RaidHUD combat HUD order changed. No asset was saved.");
            var group = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
            group.SetParent(root, false);
            group.anchorMin = Vector2.zero; group.anchorMax = Vector2.one;
            group.offsetMin = group.offsetMax = Vector2.zero;
            group.pivot = root.pivot;
            group.SetSiblingIndex(first);
            foreach (var child in children) child.SetParent(group, false);
        }

        [MenuItem("Tools/RaidRogue/UI/Create Editable Prefabs (Play Mode)")]
        public static void Generate()
        {
            // The normal create command is idempotent. Editing an existing HUD never
            // requires cloning live UI again or replacing an artist's active profile.
            var active = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (active != null && active.hudPrefab != null)
            {
                Validate(active);
                Selection.activeObject = active.hudPrefab.gameObject;
                EditorGUIUtility.PingObject(Selection.activeObject);
                Debug.Log("UI PREFABS ALREADY EXIST: " + AssetDatabase.GetAssetPath(active.hudPrefab) + ". Stop Play Mode and edit this prefab. No assets were regenerated.");
                return;
            }
            if (!Application.isPlaying || RaidNetworkSession.Instance?.Mode != RaidSessionMode.Menu || LocalCameraSettingsUI.IsOpen)
            { Debug.LogError("Enter Play Mode in the ORIGINAL raid project and remain on the main menu before creating UI assets."); return; }
            var canvas = Object.FindFirstObjectByType<SkillHotbarUI>()?.GetComponentInParent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("Raid HUD canvas unavailable.");
            // Build dormant presentations only. No reward/clear/start/ready/gameplay action is invoked.
            Invoke(Object.FindFirstObjectByType<PartyWipeUI>(), "Build");
            Object.FindFirstObjectByType<PartyWipeUI>()?.Hide();
            var reward = Object.FindFirstObjectByType<CharacterRewardUI>();
            if (RaidUIView.For(reward) == null) Invoke(reward, "Build", canvas);
            CharacterRewardUI.Close();
            AegisRouteSelectionUI.PrepareViewForAuthoring(canvas);
            Object.FindFirstObjectByType<LocalCameraSettingsUI>()?.PrepareFormsForAuthoring();
            foreach (var view in canvas.GetComponentsInChildren<RaidUIView>(true)) if (view.owner != null) view.Capture(view.owner);

            var existingRoot = canvas.GetComponent<RaidUIRoot>();
            var liveRoot = existingRoot != null ? existingRoot : canvas.gameObject.AddComponent<RaidUIRoot>();
            GameObject holder = null;
            try
            {
                CapturePresenters(liveRoot);
                holder = new GameObject("UI authoring temporary"); holder.SetActive(false);
                var clone = Object.Instantiate(canvas.gameObject, holder.transform, false);
                clone.name = "RaidHUD";
                var root = clone.GetComponent<RaidUIRoot>();
                // Runtime-generated white sprites are not persistent assets.
                foreach (var image in clone.GetComponentsInChildren<Image>(true))
                    if (image.sprite != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(image.sprite))) image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true))
                {
                    var style = graphic.GetComponent<RaidUIStyle>() ?? graphic.gameObject.AddComponent<RaidUIStyle>();
                    style.graphic = graphic;
                    if (graphic is Text) style.role = RaidUIStyle.ThemeRole.Text;
                    // Preserve the individual design by default; opt in to a theme per element.
                }
                foreach(var outline in clone.GetComponentsInChildren<Outline>(true))
                {
                    // Existing target outlines encode visibility in alpha. Authored outlines use enabled instead.
                    if(outline.effectColor.a==0){var color=outline.effectColor;color.a=1;outline.effectColor=color;outline.enabled=false;}
                }
                ConfigureIcons(clone);
                ConfigureFills(clone);
                ValidateScripts(clone);
                string folder = "Assets/UI/Editable";
                if (AssetDatabase.IsValidFolder(folder)) folder = "Assets/UI/Generated/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                Folder(folder); Folder("Assets/Resources/UI");
                var profile = ScriptableObject.CreateInstance<RaidUIPresentation>();
                profile.icons=CharacterCatalog.All.SelectMany(j=>j.skills.Select(s=>s.id).Concat(new[]{j.id}))
                    .Concat(new[]{"potion","status.stun","status.root","status.morning","status.invulnerable","status.knockback-immunity"})
                    .Distinct().Select(id=>new RaidUIPresentation.IconEntry{id=id}).ToArray();
                var theme = ScriptableObject.CreateInstance<RaidUITheme>(); theme.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                AssetDatabase.CreateAsset(theme, folder + "/Theme.asset"); profile.theme = theme;

                MakeTemplates(root, profile, folder);
                MakeResourceAndStatusTemplates(profile, folder);
                MakeFormTemplates(profile, folder);
                var views = new List<RaidUIPresentation.ViewEntry>();
                // Save nested views deepest first, then remap all serialized references after replacement.
                foreach (var view in clone.GetComponentsInChildren<RaidUIView>(true).OrderByDescending(v => Depth(v.transform)).ToArray())
                {
                    if (view == null || view.viewId == "scene-bindings") continue;
                    string id = view.viewId;
                    var asset = PrefabUtility.SaveAsPrefabAsset(view.gameObject, folder + "/" + id + ".prefab");
                    Replace(root.gameObject, view.gameObject, asset);
                    views.Add(new RaidUIPresentation.ViewEntry { id = id, prefab = asset.GetComponent<RaidUIView>() });
                }
                profile.views = views.ToArray();
                var hud = PrefabUtility.SaveAsPrefabAsset(clone, folder + "/RaidHUD.prefab");
                profile.hudPrefab = hud.GetComponent<RaidUIRoot>();
                // First creation activates the profile. Subsequent generation never overwrites the user's profile/assets.
                string profilePath = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath) == null ? ProfilePath : folder + "/RaidUIPresentation.asset";
                AssetDatabase.CreateAsset(profile, profilePath);
                AssetDatabase.SaveAssets();
                Validate(profile);
                RaidUIBindingChecks.Run();
                Selection.activeObject = hud;
                Debug.Log("UI PREFABS CREATED: " + folder + " | Profile: " + profilePath + ". Stop Play Mode, edit the prefabs, then play again. Existing authored assets were not overwritten.");
            }
            finally
            {
                if (holder != null) Object.DestroyImmediate(holder);
                if (existingRoot == null && liveRoot != null) Object.DestroyImmediate(liveRoot);
            }
        }

        private static void CapturePresenters(RaidUIRoot root)
        {
            var presenters = new List<RaidUIRoot.PresenterBinding>();
            var capture = root.gameObject.AddComponent<RaidUIView>();
            try
            {
                foreach (var owner in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (owner == null || owner.GetType().Namespace != "RaidRogue" || owner.GetType().Name.StartsWith("RaidUI")) continue;
                    capture.Capture(owner);
                    if (!capture.references.Any(x => x.target != null)) continue;
                    bool embedded = owner.transform.IsChildOf(root.transform) || owner.transform == root.transform;
                    presenters.Add(new RaidUIRoot.PresenterBinding
                    {
                        typeName = owner.GetType().AssemblyQualifiedName,
                        embeddedPresenter = embedded ? owner : null,
                        references = new List<RaidUIView.Reference>(capture.references)
                    });
                }
                // Commit only a complete capture; a failure must preserve existing bindings.
                root.presenters.Clear();
                root.presenters.AddRange(presenters);
            }
            finally { Object.DestroyImmediate(capture); }
        }

        private static void ConfigureIcons(GameObject root)
        {
            foreach (var hotbar in root.GetComponentsInChildren<SkillHotbarUI>(true))
                foreach (var slot in hotbar.slots) if (slot?.slotButton != null) Icon(slot.slotButton.gameObject);
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if (view.viewId == "character")
                    foreach (var entry in view.references.Where(x => x.path.StartsWith("_buttons/{")))
                        if (entry.target is Button b) Icon(b.gameObject);
                if (view.viewId == "potion" || view.viewId == "dash")
                    foreach (var entry in view.references.Where(x => x.path == "_slot")) if (entry.target is GameObject go) Icon(go);
            }
        }
        private static RaidUIIcon Icon(GameObject owner)
        {
            var icon = owner.GetComponent<RaidUIIcon>(); if (icon != null) return icon;
            icon = owner.AddComponent<RaidUIIcon>();
            var go = new GameObject("Icon (optional)", typeof(RectTransform), typeof(Image)); go.transform.SetParent(owner.transform, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f); rect.sizeDelta = new Vector2(40,40);
            icon.image = go.GetComponent<Image>(); icon.image.preserveAspect = true; icon.image.raycastTarget = false; icon.image.enabled = false;
            return icon;
        }
        private static void ConfigureFills(GameObject root)
        {
            foreach(var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                string path = view.viewId == "potion" || view.viewId=="dash" ? "_overlay" : view.viewId == "resurrection" ? "_fill" : null;
                if (path == null) continue;
                var target = view.references.Find(x => x.path == path)?.target;
                var image = target as Image ?? (target as Component)?.GetComponent<Image>();
                if (image == null) continue;
                image.type = Image.Type.Filled; image.fillMethod = view.viewId != "resurrection" ? Image.FillMethod.Vertical : Image.FillMethod.Horizontal;
                image.fillOrigin = 0; image.fillAmount = 0;
                image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                image.rectTransform.anchorMin=Vector2.zero;image.rectTransform.anchorMax=Vector2.one;
                image.rectTransform.offsetMin=image.rectTransform.offsetMax=Vector2.zero;
            }
        }
        private static void MakeTemplates(RaidUIRoot root, RaidUIPresentation profile, string folder)
        {
            var character = root.GetComponentsInChildren<RaidUIView>(true).FirstOrDefault(x => x.viewId == "character");
            if (character != null)
            {
                if(character.GetComponentInChildren<RaidUIStatusStrip>(true)==null)
                {
                    var go=new GameObject("Status effects",typeof(RectTransform),typeof(HorizontalLayoutGroup),typeof(RaidUIStatusStrip));
                    go.transform.SetParent(character.transform,false);var rect=(RectTransform)go.transform;
                    rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,290);rect.sizeDelta=new Vector2(420,48);
                    var layout=go.GetComponent<HorizontalLayoutGroup>();layout.childControlWidth=layout.childControlHeight=false;layout.childForceExpandWidth=layout.childForceExpandHeight=false;layout.spacing=6;
                    go.GetComponent<RaidUIStatusStrip>().items=rect;
                }
                var buttons = character.references.Where(x => x.path.StartsWith("_buttons/{")).Select(x => x.target as Button).Where(x => x != null).ToArray();
                foreach(var button in buttons)
                {
                    var item=button.gameObject.AddComponent<RaidUIRepeatedItem>();item.button=button;item.icon=button.GetComponent<RaidUIIcon>();
                    string key=character.references.First(x=>x.target==button).path.Replace("_buttons/","_labels/");
                    item.label=character.references.Find(x=>x.path==key)?.target as Text;
                }
                profile.skillTemplate = Template(root, buttons.Select(x=>x.gameObject).ToArray(), folder + "/SkillSlot.prefab");
            }
            var hotbar=root.GetComponentInChildren<SkillHotbarUI>(true);
            if(hotbar!=null)
            {
                foreach(var slot in hotbar.slots)
                {
                    var item=slot.slotButton.gameObject.AddComponent<RaidUIRepeatedItem>();item.button=slot.slotButton;item.label=slot.skillNameText;item.icon=slot.slotButton.GetComponent<RaidUIIcon>();item.fill=slot.cooldownOverlayImage;
                }
                profile.hotbarTemplate=Template(root,hotbar.slots.Select(x=>x.slotButton.gameObject).ToArray(),folder+"/BaseSkillSlot.prefab");
            }
            var party=root.GetComponentInChildren<PartyListUI>(true);
            if(party!=null)
            {
                foreach(var row in party.memberRows)
                {
                    row.boundTarget=null;var item=row.rowRoot.AddComponent<RaidUIRepeatedItem>();item.party=row;item.button=row.selectButton;item.label=row.memberNameText;item.fill=row.hpFillImage;item.icon=Icon(row.rowRoot);
                }
                profile.partyTemplate=Template(root,party.memberRows.Select(x=>x.rowRoot).ToArray(),folder+"/PartyMember.prefab");
            }
        }
        private static RaidUIRepeatedItem Template(RaidUIRoot root, GameObject[] items, string path)
        {
            if(items.Length==0)return null;
            var prefab=PrefabUtility.SaveAsPrefabAsset(items[0],path);
            foreach(var item in items)Replace(root.gameObject,item,prefab);
            return prefab.GetComponent<RaidUIRepeatedItem>();
        }
        private static void MakeResourceAndStatusTemplates(RaidUIPresentation profile,string folder)
        {
            var go=new GameObject("Resource",typeof(RectTransform),typeof(Image),typeof(RaidUIRepeatedItem));
            try
            {
                var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,38);rect.sizeDelta=new Vector2(420,8);
                go.GetComponent<Image>().color=new Color(.12f,.15f,.18f,.9f);
                var fill=new GameObject("Fill",typeof(RectTransform),typeof(Image));fill.transform.SetParent(rect,false);
                var r=(RectTransform)fill.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
                var image=fill.GetComponent<Image>();image.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");image.type=Image.Type.Filled;image.fillMethod=Image.FillMethod.Horizontal;image.fillOrigin=0;image.color=new Color(.85f,.73f,.3f);
                var style=fill.AddComponent<RaidUIStyle>();style.graphic=image;style.role=RaidUIStyle.ThemeRole.Resource;
                go.GetComponent<RaidUIRepeatedItem>().fill=image;
                profile.resourceTemplate=PrefabUtility.SaveAsPrefabAsset(go,folder+"/ResourceBar.prefab").GetComponent<RaidUIRepeatedItem>();
            }
            finally{Object.DestroyImmediate(go);}
            go=new GameObject("Status",typeof(RectTransform),typeof(RaidUIRepeatedItem));
            try
            {
                var item=go.GetComponent<RaidUIRepeatedItem>();item.icon=Icon(go);((RectTransform)go.transform).sizeDelta=new Vector2(48,48);
                var label=new GameObject("Label",typeof(RectTransform),typeof(Text));label.transform.SetParent(go.transform,false);
                var rect=(RectTransform)label.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                item.label=label.GetComponent<Text>();item.label.font=profile.theme.font;item.label.fontSize=10;item.label.alignment=TextAnchor.LowerCenter;item.label.raycastTarget=false;
                profile.statusTemplate=PrefabUtility.SaveAsPrefabAsset(go,folder+"/StatusEffect.prefab").GetComponent<RaidUIRepeatedItem>();
            }
            finally{Object.DestroyImmediate(go);}
        }
        private static void MakeFormTemplates(RaidUIPresentation profile,string folder)
        {
            foreach(bool button in new[]{false,true})
            {
                var go=new GameObject(button?"Form button":"Form text",typeof(RectTransform),typeof(LayoutElement),typeof(RaidUIRepeatedItem));
                try
                {
                    var item=go.GetComponent<RaidUIRepeatedItem>();var sizing=go.GetComponent<LayoutElement>();sizing.minHeight=38;sizing.flexibleWidth=1;
                    var label=button?new GameObject("Label",typeof(RectTransform),typeof(Text)):go;
                    if(button){label.transform.SetParent(go.transform,false);var r=(RectTransform)label.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(4,2);r.offsetMax=new Vector2(-4,-2);}
                    item.label=label.GetComponent<Text>()??label.AddComponent<Text>();item.label.font=profile.theme.font;item.label.fontSize=15;item.label.color=Color.white;item.label.raycastTarget=false;
                    var style=label.AddComponent<RaidUIStyle>();style.graphic=item.label;style.role=RaidUIStyle.ThemeRole.Text;
                    if(button)
                    {
                        go.AddComponent<Image>().color=new Color(.12f,.25f,.32f);item.button=go.AddComponent<Button>();item.button.targetGraphic=go.GetComponent<Image>();
                        item.label.alignment=TextAnchor.MiddleCenter;profile.formButtonTemplate=PrefabUtility.SaveAsPrefabAsset(go,folder+"/FormButton.prefab").GetComponent<RaidUIRepeatedItem>();
                    }
                    else{item.label.alignment=TextAnchor.MiddleLeft;profile.formTextTemplate=PrefabUtility.SaveAsPrefabAsset(go,folder+"/FormText.prefab").GetComponent<RaidUIRepeatedItem>();}
                }
                finally{Object.DestroyImmediate(go);}
            }
        }
        internal static void Replace(GameObject root,GameObject old,GameObject prefab)
        {
            ValidateScripts(prefab);
            var replacement=(GameObject)PrefabUtility.InstantiatePrefab(prefab,old.transform.parent);
            replacement.name=old.name;replacement.SetActive(old.activeSelf);replacement.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
            RaidUILayout.Copy((RectTransform)replacement.transform,(RectTransform)old.transform);
            var map=new Dictionary<Object,Object>();Map(old.transform,replacement.transform,map);
            RemapScriptReferences(root,map);
            Object.DestroyImmediate(old);
        }
        internal static void RemapScriptReferences(GameObject root,Dictionary<Object,Object> map)
        {
            // Never enumerate native Component/Transform metadata. Rewriting m_GameObject,
            // m_Father or m_Children corrupts Unity's ownership graph and can crash the editor.
            foreach(var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(component==null)continue;
                using(var serialized=new SerializedObject(component))
                {
                    bool changed=false;
                    var fields=new HashSet<string>();
                    for(var type=component.GetType();type!=null&&type!=typeof(MonoBehaviour);type=type.BaseType)
                    foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    {
                        if(!fields.Add(field.Name))continue;
                        var property=serialized.FindProperty(field.Name);
                        if(property==null)continue; // Non-serialized state, delegates and caches.
                        var end=property.GetEndProperty();
                        do
                        {
                            if(property.propertyType==SerializedPropertyType.ObjectReference&&property.objectReferenceValue!=null&&map.TryGetValue(property.objectReferenceValue,out var target))
                            {property.objectReferenceValue=target;changed=true;}
                        }while(property.Next(true)&&!SerializedProperty.EqualContents(property,end));
                    }
                    if(changed)serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
        internal static void ValidateScripts(GameObject root)
        {
            foreach(var node in root.GetComponentsInChildren<Transform>(true))
            foreach(var script in node.GetComponents<MonoBehaviour>())
            {
                string location=root.name+"/"+AnimationUtility.CalculateTransformPath(node,root.transform);
                if(script==null)throw new InvalidOperationException("Missing script in UI hierarchy: "+location+". Use Repair Legacy Shortcut Scripts for the old generated HUD.");
                var asset=MonoScript.FromMonoBehaviour(script);
                using(var serialized=new SerializedObject(script))
                {
                    var savedScript=serialized.FindProperty("m_Script")?.objectReferenceValue as MonoScript;
                    if(asset==null||savedScript==null||!AssetDatabase.Contains(savedScript)||savedScript.GetClass()!=script.GetType())
                        throw new InvalidOperationException("UI component needs a persistent matching script asset: "+script.GetType().FullName+" at "+location);
                }
            }
        }
        private static void Map(Transform old,Transform replacement,Dictionary<Object,Object> map)
        {
            map[old.gameObject]=replacement.gameObject;
            foreach(var component in old.GetComponents<Component>())
            {
                var target=replacement.GetComponent(component.GetType());if(target!=null)map[component]=target;
            }
            for(int i=0;i<Math.Min(old.childCount,replacement.childCount);i++)Map(old.GetChild(i),replacement.GetChild(i),map);
        }
        private static int Depth(Transform t){int depth=0;while(t.parent!=null){depth++;t=t.parent;}return depth;}
        private static void Invoke(object owner,string method,params object[] args)
        {if(owner!=null)owner.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(owner,args);}
        private static void Folder(string path)
        {
            string[] parts=path.Split('/');string current=parts[0];
            for(int i=1;i<parts.Length;i++){string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);current=next;}
        }

        [MenuItem("Tools/RaidRogue/UI/Validate Active Presentation")]
        public static void ValidateActive(){Validate(AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath));}
        public static void Validate(RaidUIPresentation profile)
        {
            if(profile==null||profile.hudPrefab==null)throw new InvalidOperationException("UI Presentation / HUD prefab missing.");
            ValidateScripts(profile.hudPrefab.gameObject);
            int count=0;
            foreach(var binding in profile.hudPrefab.presenters)
                foreach(var reference in binding.references)
                {
                    if(reference.count<0&&reference.target==null)throw new InvalidOperationException("Missing UI reference: "+binding.typeName+"/"+reference.path);
                    count++;
                }
            foreach(var view in profile.hudPrefab.GetComponentsInChildren<RaidUIView>(true))
            {
                if(view.actions.GroupBy(x=>x.id).Any(x=>x.Count()>1))throw new InvalidOperationException("Duplicate UI action IDs: "+view.viewId);
                foreach(var action in view.actions)if(action.button==null)throw new InvalidOperationException("Missing UI button: "+view.viewId+"/"+action.id);
            }
            if(profile.icons.GroupBy(x=>x.id).Any(x=>x.Count()>1))throw new InvalidOperationException("Duplicate icon IDs.");
            Debug.Log("UI VALIDATION PASS: "+count+" references; nested prefab actions and icon IDs checked. This is not a gameplay/network test.");
        }
    }
}
#endif
