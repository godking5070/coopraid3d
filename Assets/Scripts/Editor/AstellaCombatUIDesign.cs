#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    public static class AstellaCombatUIDesign
    {
        private static readonly Color Ink = new Color32(12, 20, 34, 244);
        private static readonly Color Gold = new Color32(229, 194, 124, 255);
        private static readonly Color Mint = new Color32(103, 223, 204, 255);
        private static readonly Color Purple = new Color32(167, 129, 232, 255);

        [MenuItem("Tools/RaidRogue/UI/Apply Astella Combat HUD &F9")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open Astella SampleScene first.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            if (profile?.hudPrefab == null || !AssetDatabase.GetAssetPath(profile.hudPrefab).StartsWith("Assets/UI/AstellaAstral-", StringComparison.Ordinal))
                throw new InvalidOperationException("Apply Astella Astral Design first.");
            string backup = "Logs/UIRepairBackups/astella-combat-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            string hudPath = AssetDatabase.GetAssetPath(profile.hudPrefab);
            foreach (string path in new[] { hudPath, scene.path, AssetDatabase.GetAssetPath(profile.theme) })
            {
                File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
                File.Copy(path + ".meta", Path.Combine(backup, Path.GetFileName(path) + ".meta"));
            }
            AstellaAstralUIDesign.RepairActive();
            var root = PrefabUtility.LoadPrefabContents(hudPath);
            try
            {
                Layout(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, hudPath) == null) throw new IOException(hudPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            profile.theme.compactLoadout = true;
            EditorUtility.SetDirty(profile.theme);
            AssetDatabase.SaveAssets();

            // Place the actual authored HUD in the main scene so edit view and Play use the same design.
            var instance = scene.GetRootGameObjects().FirstOrDefault(o => o.GetComponent<RaidUIRoot>() != null && o.activeSelf);
            foreach (var old in scene.GetRootGameObjects())
                if (old != instance && (old.name == "RaidRogue_HUD_Canvas" || old.GetComponent<RaidUIRoot>() != null))
                { Undo.RecordObject(old, "Preserve previous HUD"); old.SetActive(false); }
            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(profile.hudPrefab.gameObject, scene);
                Undo.RegisterCreatedObjectUndo(instance, "Install Astella HUD");
            }
            instance.name = "Astella_RaidHUD";
            var authored = instance.GetComponent<RaidUIRoot>();
            // Explicitly restore scene presenters without running Start, gameplay or runtime Connect in edit mode.
            var bridge = instance.AddComponent<RaidUIView>();
            foreach (var binding in authored.presenters)
            {
                var type = Type.GetType(binding.typeName);
                var owner = binding.embeddedPresenter != null ? binding.embeddedPresenter : type == null ? null : Object.FindFirstObjectByType(type) as MonoBehaviour;
                if (owner == null) continue;
                bridge.references = binding.references; bridge.Restore(owner);
                EditorUtility.SetDirty(owner);
            }
            Object.DestroyImmediate(bridge);
            foreach (var view in instance.GetComponentsInChildren<RaidUIView>(true).Where(v => v.viewId == "connection"))
            {
                var overlay = view.references.FirstOrDefault(r => r.path == "_overlay")?.target as GameObject;
                if (overlay != null) overlay.SetActive(false); // Editor preview; the session opens it at runtime.
            }
            foreach (var role in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<RoleSelectionManager>(true)))
                if (role.roleModalRoot != null) role.roleModalRoot.SetActive(false);
            foreach (var party in instance.GetComponentsInChildren<PartyListUI>(true)) party.RefreshPartyReferences();
            foreach (var hotbar in instance.GetComponentsInChildren<SkillHotbarUI>(true)) hotbar.combatController = Object.FindFirstObjectByType<PlayerCombatController>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            RaidUIAuthoring.Validate(profile);
            Debug.Log("ASTELLA COMBAT HUD SAVED IN MAIN SCENE. Backup: " + backup);
        }

        private static void Layout(GameObject root)
        {
            RectTransform Find(string name) => root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == name);
            void Move(string name, Vector2 anchor, Vector2 position, Vector2 size)
            { var rt = Find(name); if (rt != null) Set(rt, anchor, position, size); }
            void Colorize(string name, Color color)
            { var rt = Find(name); if (rt != null && rt.TryGetComponent<Image>(out var image)) image.color = color; }
            Move("Top_StageHeader_Badge", new Vector2(.5f, 1), new Vector2(0, -20), new Vector2(760, 30));
            Move("Top_BossHP_Frame", new Vector2(.5f, 1), new Vector2(0, -56), new Vector2(760, 88));
            Move("Top_BossCastBar_Frame", new Vector2(.5f, 1), new Vector2(0, -153), new Vector2(620, 30));
            Move("TargetInfoHUD_FFXIV", new Vector2(.5f, 1), new Vector2(0, -194), new Vector2(660, 64));
            Move("Center_MechanicAlert_Text", new Vector2(.5f, 1), new Vector2(0, -276), new Vector2(1000, 50));
            Colorize("Top_BossHP_Frame", Ink); Colorize("Boss_HP_Fill", Purple);
            Colorize("Top_StageHeader_Badge", new Color32(20, 47, 52, 244));
            Colorize("Boss_Cast_Fill", Gold);
            Sprite flat = WhiteSprite();
            foreach (var image in root.GetComponentsInChildren<Image>(true))
                if (image.type == Image.Type.Filled) image.sprite = flat;
            foreach (var text in root.GetComponentsInChildren<Text>(true))
                if (new[] { "Boss_HP_Text", "Boss_PhaseText", "Stage_Header_Text" }.Contains(text.name))
                { text.resizeTextForBestFit = true; text.resizeTextMinSize = 12; text.resizeTextMaxSize = 18; }
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true).Where(r => r.name == "HUD_TopLeft_Panel")) rt.gameObject.SetActive(false);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var party = root.GetComponentInChildren<PartyListUI>(true);
            if (party != null)
            {
                Set(party.panelRoot, new Vector2(0, 1), new Vector2(28, -140), new Vector2(310, 320));
                party.panelRoot.localScale = Vector3.one;
                if (party.panelRoot.TryGetComponent<Image>(out var background)) background.color = Ink;
                Caption(party.panelRoot, "ASTELLA / RAID PARTY", new Vector2(0, 1), new Vector2(16, -13), new Vector2(280, 25), 16, Mint, font);
                var header = party.panelRoot.Find("HeaderTitle");
                if (header != null) { Set((RectTransform)header, new Vector2(0, 1), new Vector2(16, -42), new Vector2(280, 20)); header.GetComponent<Text>().fontSize = 12; }
                for (int i = 0; i < party.memberRows.Length; i++)
                {
                    var row = party.memberRows[i];
                    if (row?.rowRoot == null) continue;
                    Set((RectTransform)row.rowRoot.transform, new Vector2(.5f, 1), new Vector2(0, -72 - i * 58), new Vector2(286, 52));
                    if (row.memberNameText != null) row.memberNameText.fontSize = 16;
                    if (row.hpValueText != null) row.hpValueText.fontSize = 13;
                    if (row.hpFillImage != null) row.hpFillImage.color = Mint;
                }
            }
            Move("EnemyList_LeftHUD", new Vector2(0, 1), new Vector2(28, -486), new Vector2(310, 220));
            Move("TopRight_ViewModeToggle_Button", Vector2.one, new Vector2(-28, -24), new Vector2(280, 50));
            Move("RoleSwitchButton_TopRight", Vector2.one, new Vector2(-28, -82), new Vector2(280, 42));
            Move("Settings", Vector2.one, new Vector2(-28, -132), new Vector2(280, 38));
            Move("Minimap", Vector2.one, new Vector2(-28, -184), new Vector2(216, 244));
            var bar = root.GetComponentInChildren<SkillHotbarUI>(true);
            if (bar != null)
            {
                Set((RectTransform)bar.transform, new Vector2(.5f, 0), new Vector2(0, 24), new Vector2(760, 160));
                bar.transform.localScale = Vector3.one;
                if (bar.TryGetComponent<Image>(out var bg)) bg.color = Ink;
                for (int i = 0; i < bar.slots.Length; i++)
                {
                    var slot = bar.slots[i]; if (slot?.slotButton == null) continue;
                    Set(slot.slotButton.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(-258 + i * 172, 16), new Vector2(160, 124));
                    if (slot.iconBackgroundImage != null) slot.iconBackgroundImage.color = new Color32(26, 40, 59, 255);
                    if (slot.keybindBadgeText != null)
                    {
                        Set(slot.keybindBadgeText.rectTransform, new Vector2(.5f, 1), new Vector2(0, -4), new Vector2(144, 23));
                        slot.keybindBadgeText.fontSize = 15; slot.keybindBadgeText.alignment = TextAnchor.MiddleLeft;
                    }
                    if (slot.stageBadgeText != null) { Set(slot.stageBadgeText.rectTransform, new Vector2(.5f, 1), new Vector2(0, -27), new Vector2(146, 19)); slot.stageBadgeText.fontSize = 12; }
                    if (slot.skillNameText != null)
                    {
                        Set(slot.skillNameText.rectTransform, new Vector2(.5f, 0), new Vector2(0, 25), new Vector2(146, 48));
                        slot.skillNameText.fontSize = 17; slot.skillNameText.resizeTextForBestFit = true;
                        slot.skillNameText.resizeTextMinSize = 13; slot.skillNameText.resizeTextMaxSize = 17;
                    }
                    if (slot.cooldownNumberText != null) { Set(slot.cooldownNumberText.rectTransform, new Vector2(.5f, 0), new Vector2(0, 4), new Vector2(148, 22)); slot.cooldownNumberText.fontSize = 13; }
                    if (slot.comboTimerBarRoot != null)
                    {
                        slot.comboTimerBarRoot.transform.SetParent(slot.slotButton.transform, false);
                        Set(slot.comboTimerBarRoot.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(0, 1), new Vector2(148, 3));
                        if (slot.comboTimerText != null) slot.comboTimerText.gameObject.SetActive(false);
                    }
                }
                if (bar.gcdSummaryText != null) bar.gcdSummaryText.gameObject.SetActive(false);
            }
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                var slots = view.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "CharacterSlots");
                if (view.viewId == "character" && slots != null)
                {
                    Set(slots, new Vector2(.5f, 1), new Vector2(0, 78), new Vector2(760, 76)); slots.localScale = Vector3.one;
                    var buttons = slots.GetComponentsInChildren<Button>(true).Where(b => b.GetComponent<RaidUIFoldout>() == null).ToArray();
                    int count = 0;
                    foreach (var button in buttons)
                    {
                        if (!new[] { "Counter", "Job2", "Identity", "Active", "OffGcd2", "Ultimate" }.Contains(button.name)) continue;
                        Set(button.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(-310 + count++ * 124, 0), new Vector2(114, 64));
                        foreach (var text in button.GetComponentsInChildren<Text>(true)) text.fontSize = 14;
                    }
                }
                if (view.viewId == "connection")
                {
                    var flow = view.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "RunFlow_Status");
                    if (flow != null) Set(flow, new Vector2(.5f, 1), new Vector2(0, -305), new Vector2(850, 38));
                }
            }
            Move("CommonPotion_Slot", new Vector2(.5f, 0), new Vector2(-480, 38), new Vector2(110, 112));
            Move("CommonDash_Slot", new Vector2(.5f, 0), new Vector2(480, 38), new Vector2(110, 112));
            foreach (string name in new[] { "TargetInfoHUD_FFXIV", "EnemyList_LeftHUD", "CommonPotion_Slot", "CommonDash_Slot" })
            { var rt = Find(name); if (rt != null) rt.localScale = Vector3.one; }
        }

        private static void Set(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        { rt.anchorMin = rt.anchorMax = rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = size; }
        private static void Caption(Transform parent, string value, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, Color color, Font font)
        {
            if (parent.Find("Astella_Title") != null) return;
            var go = new GameObject("Astella_Title", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            Set((RectTransform)go.transform, anchor, position, size);
            var text = go.GetComponent<Text>(); text.font = font; text.text = value; text.fontSize = fontSize;
            text.color = color; text.raycastTarget = false;
        }

        private static Sprite WhiteSprite()
        {
            const string path = "Assets/UI/Astella_FlatFill.png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(2, 2);
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
#endif
