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
    /// <summary>Builds an editable illustrated HUD from the current bound HUD; scene and old artwork are preserved.</summary>
    public static class AstellaOrnateUIInstaller
    {
        private const string Folder = "Assets/UI/AstellaOrnate";
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        private static readonly Color Gold = new Color32(203, 175, 118, 255);
        private static readonly Color LightGold = new Color32(245, 226, 179, 255);
        private static readonly Color ShadowGold = new Color32(73, 56, 43, 255);
        private static readonly Color Ink = new Color32(20, 18, 36, 232);
        private static Sprite white, circle, gradient;
        private static Font serif, sans;

        [MenuItem("Tools/RaidRogue/UI/Polish Active Celestial HUD &F7")]
        public static void Refine()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            if (!path.StartsWith(Folder + "/Applied-", StringComparison.Ordinal)) throw new InvalidOperationException("Apply the celestial HUD first.");
            string backup = "Logs/UIRepairBackups/palette-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup); File.Copy(path, backup + "/RaidHUD.prefab");
            sans = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            serif = AssetDatabase.LoadAssetAtPath<Font>(Folder + "/Fonts/NanumMyeongjo-Regular.ttf") ?? sans;
            white = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/Shapes/White.png");
            gradient = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/Shapes/BottomFade.png");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var hud = root.GetComponentInChildren<AstellaOrnateHUD>(true);
                foreach (var slot in hud.slots)
                {
                    if (slot.keycap != null) { slot.keycap.resizeTextForBestFit = true; slot.keycap.resizeTextMinSize = 9; slot.keycap.resizeTextMaxSize = 17; }
                    if (slot.slot == CharacterSlot.OffGcd1) slot.fallbackArt = Art("04_ConstellationShield");
                    if (slot.slot == CharacterSlot.Job1) slot.fallbackArt = Art("03_VioletBlades");
                    if (slot.artwork != null) slot.artwork.sprite = slot.fallbackArt;
                }
                var boss = (RectTransform)hud.bossRoot.transform;
                if (boss.Find("Top vignette") == null)
                {
                    foreach (Transform child in boss)
                        if (child.name == "Gold filigree") ((RectTransform)child).anchoredPosition += new Vector2(0, boss.sizeDelta.y * .5f);
                    var fade = Image("Top vignette", boss, new Vector2(.5f, 1), new Vector2(0, -90), new Vector2(1150, 240), new Color(.025f, .02f, .06f, .75f));
                    fade.sprite = gradient; fade.rectTransform.localRotation = Quaternion.Euler(0, 0, 180); fade.transform.SetAsFirstSibling();
                }
                RefineJobPaletteAndUtilities(root, hud);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path == "Assets/Scenes/SampleScene.unity")
            {
                var liveRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Astella_CelestialHUD" && g.activeSelf);
                if (liveRoot != null)
                {
                    File.Copy(scene.path, backup + "/SampleScene.unity");
                    RefineJobPaletteAndUtilities(liveRoot, liveRoot.GetComponentInChildren<AstellaOrnateHUD>(true));
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
            }
            AssetDatabase.SaveAssets(); Debug.Log("CELESTIAL HUD POLISHED: job palettes, compact potion, party resurrection coin.");
        }

        private static Material Palette(string name, string hex)
        {
            string folder = Folder + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Folder, "Materials");
            string path = folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material; // Later art-direction edits remain authored.
            var shader = Shader.Find("Astella/UI/JobPalette");
            if (shader == null) throw new InvalidOperationException("JobPalette shader has not imported yet.");
            ColorUtility.TryParseHtmlString("#" + hex, out var mid);
            material = new Material(shader) { name = name };
            material.SetColor("_ShadowColor", Color.Lerp(new Color(.018f, .015f, .03f), mid, .045f));
            material.SetColor("_MidColor", mid);
            material.SetColor("_HighlightColor", Color.Lerp(mid, Color.white, .82f));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void RefineJobPaletteAndUtilities(GameObject root, AstellaOrnateHUD hud)
        {
            string[] jobs = { "job.aegis-paladin", "job.berserker", "job.dragon-breaker", "job.sylvan-oracle", "job.seraphic-bard", "job.blood-apothecary", "job.nightblade", "job.tempest-lancer", "job.gunslinger", "job.spirit-ranger", "job.astromancer", "job.mirror-phantom", "test.guardian", "test.oracle", "test.blade", "test.archmage" };
            string[] hues = { "D5CCA9", "F35D45", "EC933E", "66C995", "F1D374", "CB617B", "9972DA", "59BDF2", "9BBFDA", "69C7AB", "A785F0", "C6A4EA", "71B4E2", "77C89B", "E5BD64", "A785F0" };
            hud.jobVisuals = jobs.Select((id, i) => new AstellaOrnateHUD.JobVisual { jobId = id, skillMaterial = Palette(id, hues[i]) }).ToArray();
            hud.defaultSkillMaterial = Palette("CelestialDefault", "C4A1EF");
            var player = (RectTransform)hud.playerRoot.transform;
            var track = (RectTransform)player.Find("Actions");
            var potion = hud.slots.FirstOrDefault(v => v.slot == CharacterSlot.Potion);
            if (potion != null)
            {
                var dock = track.Find("Consumable dock") as RectTransform;
                if (dock == null)
                {
                    dock = Rect("Consumable dock", track, new Vector2(.5f, .5f), Vector2.zero, new Vector2(102, 82));
                    var layout = dock.gameObject.AddComponent<LayoutElement>(); layout.preferredWidth = layout.minWidth = 102; layout.preferredHeight = 82;
                    Image("Utility divider", dock, new Vector2(.5f, .5f), new Vector2(-41, 0), new Vector2(1, 62), new Color(Gold.r, Gold.g, Gold.b, .55f));
                    var label = Text("Consumable label", dock, new Vector2(15, 45), new Vector2(86, 18), 12, Gold, false); label.text = "\uC18C\uBAA8\uD488";
                }
                dock.Find("Consumable label").GetComponent<Text>().text = "\uC18C\uBAA8\uD488";
                var rect = potion.button.GetComponent<RectTransform>(); rect.SetParent(dock, false);
                Set(rect, new Vector2(.5f, .5f), new Vector2(15, -2), new Vector2(78, 78)); rect.localScale = Vector3.one * .7f;
                potion.artwork.material = null;
            }
            var resurrection = hud.slots.FirstOrDefault(v => v.sharedResurrection);
            if (resurrection != null)
            {
                var presentation = hud.transform;
                var dock = presentation.Find("Party revival") as RectTransform;
                if (dock == null)
                {
                    dock = Rect("Party revival", presentation, new Vector2(0, 1), new Vector2(24, -448), new Vector2(278, 58)); dock.pivot = new Vector2(0, 1);
                    Panel(dock, 278, 58, 8);
                    var label = Text("Shared revival label", dock, new Vector2(35, 11), new Vector2(180, 22), 17, LightGold, true); label.text = "\uACF5\uC720 \uBD80\uD65C \uCF54\uC778";
                }
                dock.Find("Shared revival label").GetComponent<Text>().text = "\uACF5\uC720 \uBD80\uD65C \uCF54\uC778";
                hud.partyRevivalRoot = dock.gameObject;
                var rect = resurrection.button.GetComponent<RectTransform>(); rect.SetParent(dock, false);
                Set(rect, new Vector2(0, .5f), new Vector2(32, 3), new Vector2(78, 78)); rect.localScale = Vector3.one * .52f;
                resurrection.artwork.material = Palette("SharedRevivalCoin", "E6BD62");
                var count = resurrection.cooldown.rectTransform; count.SetParent(dock, false);
                Set(count, new Vector2(.5f, .5f), new Vector2(35, -13), new Vector2(150, 24));
                resurrection.cooldown.fontSize = 19; resurrection.cooldown.color = LightGold;
                var badge = resurrection.keycap.transform.parent as RectTransform;
                Set(badge, new Vector2(1, .5f), new Vector2(32, 0), new Vector2(46, 27));
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(track);
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                if (rect.name == "EnemyList_LeftHUD") rect.anchoredPosition = new Vector2(24, -524);
        }

        [MenuItem("Tools/RaidRogue/UI/Capture Celestial HUD (Play Mode) &F6")]
        public static void Capture()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run the main scene first.");
            Directory.CreateDirectory("docs/part1");
            ScreenCapture.CaptureScreenshot("docs/part1/ASTELLA_CELESTIAL_HUD.png");
            Debug.Log("Celestial HUD capture requested: docs/part1/ASTELLA_CELESTIAL_HUD.png");
        }

        [MenuItem("Tools/RaidRogue/UI/Apply Celestial Illustrated HUD &F8")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open the Astella main SampleScene.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile?.hudPrefab == null) throw new InvalidOperationException("Active authored HUD missing.");
            PrepareAssets();
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backup = "Logs/UIRepairBackups/celestial-" + stamp;
            Directory.CreateDirectory(backup);
            foreach (string path in new[] { ProfilePath, scene.path })
            { File.Copy(path, Path.Combine(backup, Path.GetFileName(path))); File.Copy(path + ".meta", Path.Combine(backup, Path.GetFileName(path) + ".meta")); }
            string target = Folder + "/Applied-" + stamp;
            AssetDatabase.CreateFolder(Folder, "Applied-" + stamp);
            string hudPath = target + "/RaidHUD.prefab";
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(profile.hudPrefab), hudPath)) throw new IOException(hudPath);
            var theme = profile.theme != null ? Object.Instantiate(profile.theme) : ScriptableObject.CreateInstance<RaidUITheme>();
            theme.name = "Celestial Illustrated"; theme.panel = Ink; theme.accent = Gold;
            theme.text = LightGold; theme.compactLoadout = true;
            AssetDatabase.CreateAsset(theme, target + "/CelestialTheme.asset");
            string oldJson = EditorJsonUtility.ToJson(profile);
            try
            {
                var contents = PrefabUtility.LoadPrefabContents(hudPath);
                try
                {
                    foreach (var child in contents.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).ToArray())
                        if (child != contents && PrefabUtility.IsAnyPrefabInstanceRoot(child))
                            PrefabUtility.UnpackPrefabInstance(child, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    Build(contents);
                    if (PrefabUtility.SaveAsPrefabAsset(contents, hudPath) == null) throw new IOException(hudPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                profile.hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath).GetComponent<RaidUIRoot>();
                profile.theme = theme;
                RaidUIAuthoring.Validate(profile);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();

                foreach (var old in scene.GetRootGameObjects())
                    if (old.GetComponent<RaidUIRoot>() != null || old.name == "RaidRogue_HUD_Canvas") old.SetActive(false);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(profile.hudPrefab.gameObject, scene);
                instance.name = "Astella_CelestialHUD";
                Undo.RegisterCreatedObjectUndo(instance, "Apply celestial HUD");
                RebindScene(instance);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                File.WriteAllText(backup + "/applied.txt", "New HUD: " + hudPath + "\nPrevious scene/profile preserved here.\n");
                Debug.Log("CELESTIAL HUD APPLIED / 12 illustrated assets / bindings validated / main scene saved. Backup: " + backup);
            }
            catch
            { EditorJsonUtility.FromJsonOverwrite(oldJson, profile); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets(); throw; }
        }

        private static void PrepareAssets()
        {
            sans = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            serif = AssetDatabase.LoadAssetAtPath<Font>(Folder + "/Fonts/NanumMyeongjo-Regular.ttf") ?? sans;
            foreach (string file in Directory.GetFiles(Folder + "/Art", "*.png"))
            {
                if (Path.GetFileName(file) == "AstellaSkillAtlas.png") continue;
                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.maxTextureSize = 512; importer.mipmapEnabled = false; importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            }
            Directory.CreateDirectory(Folder + "/Shapes");
            white = Shape("White", (x, y) => Color.white);
            circle = Shape("Circle", (x, y) => new Color(1, 1, 1, Mathf.Clamp01((.5f - Vector2.Distance(new Vector2(x, y), new Vector2(.5f, .5f))) * 128)));
            gradient = Shape("BottomFade", (x, y) => new Color(1, 1, 1, (1 - y) * (1 - y)));
        }
        private static Sprite Shape(string name, Func<float, float, Color> pixel)
        {
            string path = Folder + "/Shapes/" + name + ".png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) texture.SetPixel(x, y, pixel((x + .5f) / 64, (y + .5f) / 64));
                texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        private static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/Art/" + name + ".png");

        private static void Build(GameObject root)
        {
            var oldPresentation = root.transform.Find("CelestialPresentation");
            if (oldPresentation != null) throw new InvalidOperationException("Celestial design already applied. Edit its prefab directly; previous copy remains preserved.");
            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null) { scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; }
            var presentation = Rect("CelestialPresentation", root.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1920, 1080));
            Stretch(presentation);
            var hud = presentation.gameObject.AddComponent<AstellaOrnateHUD>();
            hud.hotbarSource = root.GetComponentInChildren<SkillHotbarUI>(true);
            var bossBinding = root.GetComponent<RaidUIRoot>().presenters.FirstOrDefault(p => p.typeName.StartsWith("RaidRogue.BossController,"));
            hud.mechanicSource = bossBinding?.references.FirstOrDefault(r => r.path == "mechanicAlertBannerText")?.target as Text;
            if (hud.mechanicSource != null) hud.mechanicSource.enabled = false;
            foreach (string name in new[] { "Top_StageHeader_Badge", "Top_BossHP_Frame", "Top_BossCastBar_Frame", "TargetInfoHUD_FFXIV", "HUD_TopLeft_Panel" })
                foreach (var old in root.GetComponentsInChildren<RectTransform>(true).Where(r => r.name == name)) old.gameObject.SetActive(false);
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
            {
                if (view.viewId == "character")
                    foreach (var reference in view.references.Where(r => new[] { "_resources", "_deferred", "_identity", "_stats", "_tooltip" }.Contains(r.path)))
                        if (reference.target is Text text) text.enabled = false;
            }
            BuildBoss(presentation, hud);
            var player = Rect("PlayerHUD", presentation, new Vector2(.5f, 0), new Vector2(0, 22), new Vector2(1440, 258));
            player.pivot = new Vector2(.5f, 0);
            hud.playerRoot = player.gameObject;
            var fade = Image("Vignette", player, new Vector2(.5f, 0), new Vector2(0, -22), new Vector2(1920, 300), new Color(.035f, .025f, .08f, .82f));
            fade.sprite = gradient;
            fade.rectTransform.pivot = new Vector2(.5f, 0);
            BuildVitals(player, hud);
            var track = Rect("Actions", player, new Vector2(.5f, 0), new Vector2(0, 62), new Vector2(1320, 82));
            var layout = track.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter; layout.spacing = 14;
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var bindings = new List<AstellaOrnateHUD.SlotBinding>();
            var hotbar = hud.hotbarSource;
            if (hotbar == null) throw new InvalidOperationException("Bound skill bar missing.");
            if (hotbar.TryGetComponent<Image>(out var background)) background.color = Color.clear;
            var primary = new[] { CharacterSlot.Gcd1, CharacterSlot.Gcd2, CharacterSlot.OffGcd1, CharacterSlot.Job1 };
            string[] arts = { "01_GoldenSlash", "02_Sunburst", "03_VioletBlades", "04_ConstellationShield" };
            for (int i = 0; i < hotbar.slots.Length && i < primary.Length; i++)
            {
                var old = hotbar.slots[i];
                var slot = Slot(old.slotButton, track, primary[i], Art(arts[i]), old.skillNameText, old.cooldownNumberText, old.keybindBadgeText);
                HideText(old.stageBadgeText);
                if (old.cooldownOverlayImage != null) old.cooldownOverlayImage.enabled = false;
                if (old.comboTimerBarRoot != null) old.comboTimerBarRoot.SetActive(false);
                bindings.Add(slot);
            }
            var character = root.GetComponentsInChildren<RaidUIView>(true).FirstOrDefault(v => v.viewId == "character");
            var extra = new[] { CharacterSlot.Counter, CharacterSlot.Job2, CharacterSlot.Identity, CharacterSlot.Active, CharacterSlot.OffGcd2, CharacterSlot.Ultimate };
            string[] extraArt = { "05_LavenderMoon", "11_VioletLightning", "06_RitualCircle", "02_Sunburst", "05_LavenderMoon", "07_Party" };
            for (int i = 0; i < extra.Length; i++)
            {
                string key = "{" + extra[i] + "}";
                var button = character?.references.FirstOrDefault(r => r.path == "_buttons/" + key)?.target as Button;
                if (button == null) continue;
                var label = character.references.FirstOrDefault(r => r.path == "_labels/" + key)?.target as Text;
                bindings.Add(Slot(button, track, extra[i], Art(extraArt[i]), label, null, null));
            }
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true).OrderBy(v => v.viewId == "dash" ? 0 : v.viewId == "potion" ? 1 : 2))
            {
                if (view.viewId == "potion" || view.viewId == "dash")
                {
                    var go = Reference<GameObject>(view, "_slot"); if (go == null) continue;
                    var button = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                    button.targetGraphic = go.GetComponent<Image>();
                    bool potion = view.viewId == "potion";
                    var status = Reference<Text>(view, potion ? "_status" : "_state");
                    var key = Reference<Text>(view, "_key");
                    var slot = Slot(button, track, potion ? CharacterSlot.Potion : CharacterSlot.Movement,
                        Art(potion ? "09_RedPotion" : "08_CyanDash"), key, status, key);
                    slot.bindAction = true;
                    HideText(Reference<Text>(view, potion ? "_description" : "_details"));
                    var overlay = Reference<RectTransform>(view, "_overlay");
                    if (overlay != null && overlay.TryGetComponent<Image>(out var shade)) shade.enabled = false;
                    bindings.Add(slot);
                }
                if (view.viewId == "resurrection")
                {
                    var button = Reference<Button>(view, "_rescue"); if (button == null) continue;
                    var slot = Slot(button, track, (CharacterSlot)0, Art("10_ResurrectionStar"), Reference<Text>(view, "_label"), null, null);
                    slot.sharedResurrection = true; slot.bindingOverride = "Skill5";
                    var fill = Reference<Image>(view, "_fill"); if (fill != null) fill.enabled = false;
                    bindings.Add(slot);
                }
            }
            hud.slots = bindings.ToArray();
            for (int i = 0; i < hud.slots.Length; i++)
            {
                var hover = hud.slots[i].button.gameObject.AddComponent<AstellaOrnateSlotHover>(); hover.hud = hud; hover.slotIndex = i;
                hover.invokeAction = hud.slots[i].bindAction;
            }
            // Prevent the empty old extra-slot background from remaining as a dark second row.
            if (character != null)
                foreach (var rect in character.GetComponentsInChildren<RectTransform>(true))
                    if (rect.name == "CharacterSlots") rect.gameObject.SetActive(false);
            if (hotbar.playerCastBarRoot != null)
            {
                hotbar.playerCastBarRoot.transform.SetParent(player, false);
                Set(hotbar.playerCastBarRoot.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(0, 126), new Vector2(520, 22));
            }
            var tooltip = Rect("Tooltip", player, new Vector2(.5f, 0), new Vector2(0, 285), new Vector2(680, 116));
            Panel(tooltip, 680, 116, 10); hud.tooltipRoot = tooltip.gameObject;
            hud.tooltipText = Text("Skill description", tooltip, Vector2.zero, new Vector2(638, 92), 17, LightGold, false);
            tooltip.gameObject.SetActive(false);
            MinimizeLegacy(root);
            presentation.SetAsLastSibling();
            // Full-screen menus/results must remain above the new presentation; the presenter also hides in menus.
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true).Where(v => v.viewId == "connection" || v.viewId == "metrics" || v.viewId == "settings"))
                view.transform.SetAsLastSibling();
        }

        private static void BuildBoss(Transform parent, AstellaOrnateHUD hud)
        {
            var boss = Rect("BossHUD", parent, new Vector2(.5f, 1), new Vector2(0, -18), new Vector2(1000, 210));
            boss.pivot = new Vector2(.5f, 1);
            hud.bossRoot = boss.gameObject;
            var badge = Rect("Stage badge", boss, new Vector2(.5f, 1), new Vector2(0, -5), new Vector2(350, 34));
            Panel(badge, 350, 34, 16);
            hud.stageText = Text("Stage", badge, Vector2.zero, new Vector2(320, 30), 19, LightGold, true); hud.stageText.text = "STAGE 01 · NORMAL";
            hud.bossTitleText = Text("Boss title", boss, new Vector2(0, -68), new Vector2(700, 48), 34, LightGold, true, new Vector2(.5f, 1));
            hud.bossTitleText.text = "마을 수호 석상";
            Star(boss, new Vector2(-230, -68), 10); Star(boss, new Vector2(230, -68), 10);
            var hp = Rect("Boss health", boss, new Vector2(.5f, 1), new Vector2(0, -114), new Vector2(790, 22));
            hud.bossHpFill = Bar(hp, 790, 22, new Color32(203, 44, 78, 255));
            Star(hp, new Vector2(-413, 0), 20);
            hud.bossPercentText = Text("Boss percent", hp, new Vector2(452, 0), new Vector2(100, 34), 25, LightGold, false); hud.bossPercentText.text = "100%";
            var cast = Rect("Cast", boss, new Vector2(.5f, 1), new Vector2(0, -163), new Vector2(530, 38));
            hud.castRoot = cast.gameObject; Panel(cast, 530, 38, 16);
            var castBar = Rect("Cast progress", cast, new Vector2(.5f, .5f), new Vector2(10, -11), new Vector2(470, 5));
            hud.castFill = Fill(castBar, new Color32(161, 114, 226, 255));
            hud.castNameText = Text("Cast name", cast, new Vector2(-40, 5), new Vector2(350, 22), 18, LightGold, true);
            hud.castTimeText = Text("Cast time", cast, new Vector2(213, 5), new Vector2(74, 22), 18, LightGold, false);
            hud.castNameText.text = "공격 준비"; hud.castTimeText.text = "2.4s";
            Star(cast, new Vector2(-246, 0), 10);
        }
        private static void BuildVitals(RectTransform player, AstellaOrnateHUD hud)
        {
            var warning = Rect("Mechanic warning", player, new Vector2(.5f, 0), new Vector2(0, 247), new Vector2(920, 38));
            Stroke(warning, new Vector2(-440, -21), new Vector2(440, -21), new Color(Gold.r, Gold.g, Gold.b, .65f), 1);
            Star(warning, new Vector2(-400, 0), 8); Star(warning, new Vector2(400, 0), 8);
            hud.warningText = Text("Live warning", warning, Vector2.zero, new Vector2(760, 36), 23, LightGold, true);
            var vital = Rect("Vitality", player, new Vector2(.5f, 0), new Vector2(0, 183), new Vector2(720, 70));
            var hp = Rect("Player health", vital, new Vector2(.5f, .5f), new Vector2(26, 13), new Vector2(620, 22));
            hud.playerHpFill = Bar(hp, 620, 22, new Color32(84, 184, 114, 255));
            hud.playerHpText = Text("HP amount", hp, Vector2.zero, new Vector2(570, 22), 19, Color.white, false); hud.playerHpText.text = "1,000 / 1,000";
            var resource = Rect("Job resource", vital, new Vector2(.5f, .5f), new Vector2(26, -15), new Vector2(620, 18));
            hud.resourceRoot = resource.gameObject;
            hud.resourceFill = Bar(resource, 620, 18, new Color32(123, 94, 209, 255));
            hud.resourceText = Text("Resource amount", resource, Vector2.zero, new Vector2(570, 21), 17, Color.white, false); hud.resourceText.text = "직업 자원";
            var emblem = Rect("Compass emblem", vital, new Vector2(.5f, .5f), new Vector2(-326, 0), new Vector2(92, 92));
            var disc = Image("Disc", emblem, new Vector2(.5f, .5f), Vector2.zero, new Vector2(78, 78), Color.white);
            disc.sprite = circle; disc.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var art = Image("Compass artwork", disc.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(82, 82), Color.white); art.sprite = Art("12_CompassEmblem");
            Ring(emblem, 43, Gold, 2); Ring(emblem, 39, LightGold, 1); Ring(emblem, 46, ShadowGold, 1);
            Stroke(emblem, new Vector2(-42, -34), new Vector2(-50, -26), Gold, 2);
        }

        private static AstellaOrnateHUD.SlotBinding Slot(Button button, RectTransform track, CharacterSlot slot, Sprite artwork, Text sourceLabel, Text sourceStatus, Text sourceKey)
        {
            var rect = button.GetComponent<RectTransform>(); rect.SetParent(track, false); rect.localScale = Vector3.one;
            Set(rect, new Vector2(.5f, .5f), Vector2.zero, new Vector2(78, 78));
            var element = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = 78; element.minHeight = element.preferredHeight = 78;
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            if (button.targetGraphic != null) { button.targetGraphic.enabled = true; button.targetGraphic.color = new Color(.03f, .025f, .06f, .85f); }
            var imageRoot = Rect("Illustration", rect, new Vector2(.5f, .5f), Vector2.zero, new Vector2(74, 74));
            var state = imageRoot.gameObject.AddComponent<CanvasGroup>(); state.blocksRaycasts = false;
            var art = Image("Skill art", imageRoot, new Vector2(.5f, .5f), Vector2.zero, new Vector2(74, 74), Color.white); art.sprite = artwork;
            var shade = Image("Cooldown veil", imageRoot, new Vector2(.5f, .5f), Vector2.zero, new Vector2(74, 74), new Color(0, 0, .025f, .72f));
            shade.sprite = white; shade.type = UnityEngine.UI.Image.Type.Filled; shade.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360; shade.fillOrigin = 2; shade.fillAmount = 0;
            Border(imageRoot, 78, 78, 6);
            var cooldown = Text("Cooldown number", rect, Vector2.zero, new Vector2(72, 60), 32, Color.white, false);
            var shadow = cooldown.gameObject.AddComponent<Shadow>(); shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(1, -2);
            var cap = Rect("Key badge", rect, new Vector2(.5f, 0), new Vector2(0, -11), new Vector2(46, 27));
            Panel(cap, 46, 27, 4);
            var key = Text("Key", cap, Vector2.zero, new Vector2(44, 25), 17, LightGold, false); key.text = "—";
            var result = new AstellaOrnateHUD.SlotBinding { button = button, slot = slot, sourceLabel = sourceLabel,
                sourceStatus = sourceStatus, sourceKey = sourceKey, keycap = key, cooldown = cooldown,
                cooldownShade = shade, artwork = art, fallbackArt = artwork, stateGroup = state, unavailableAlpha = .43f };
            return result;
        }
        private static void MinimizeLegacy(GameObject root)
        {
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name == "RunFlow_Status")
                {
                    Set(rect, new Vector2(1, 1), new Vector2(-24, -156), new Vector2(220, 30));
                    foreach (var text in rect.GetComponentsInChildren<Text>(true)) if (text.transform.parent == rect) text.enabled = false;
                    foreach (var button in rect.GetComponentsInChildren<Button>(true)) Set(button.GetComponent<RectTransform>(), new Vector2(.5f, .5f), Vector2.zero, new Vector2(220, 30));
                }
                if (rect.name == "PartyList_LeftHUD")
                { Set(rect, new Vector2(0, 1), new Vector2(24, -150), new Vector2(310, 320)); rect.localScale = Vector3.one * .9f; }
                if (rect.name == "EnemyList_LeftHUD")
                { Set(rect, new Vector2(0, 1), new Vector2(24, -480), rect.sizeDelta); rect.localScale = Vector3.one * .85f; }
                if (rect.name == "TopRight_ViewModeToggle_Button") Set(rect, Vector2.one, new Vector2(-24, -24), new Vector2(220, 40));
                if (rect.name == "RoleSwitchButton_TopRight") Set(rect, Vector2.one, new Vector2(-24, -72), new Vector2(220, 32));
                if (rect.name == "Settings") Set(rect, Vector2.one, new Vector2(-24, -112), new Vector2(220, 32));
                if (rect.name == "Minimap") { Set(rect, Vector2.one, new Vector2(-24, -246), rect.sizeDelta); rect.localScale = Vector3.one * .8f; }
                if (new[] { "RunFlow_Status", "PartyList_LeftHUD", "EnemyList_LeftHUD", "TopRight_ViewModeToggle_Button", "RoleSwitchButton_TopRight", "Settings", "Minimap" }.Contains(rect.name)) rect.pivot = rect.anchorMin;
            }
        }
        private static void RebindScene(GameObject instance)
        {
            var authored = instance.GetComponent<RaidUIRoot>();
            var bridge = instance.AddComponent<RaidUIView>();
            foreach (var binding in authored.presenters)
            {
                var type = Type.GetType(binding.typeName);
                var owner = binding.embeddedPresenter != null ? binding.embeddedPresenter : type == null ? null : Object.FindFirstObjectByType(type) as MonoBehaviour;
                if (owner == null) continue; bridge.references = binding.references; bridge.Restore(owner); EditorUtility.SetDirty(owner);
            }
            Object.DestroyImmediate(bridge);
            foreach (var hotbar in instance.GetComponentsInChildren<SkillHotbarUI>(true)) hotbar.combatController = Object.FindFirstObjectByType<PlayerCombatController>();
            foreach (var view in instance.GetComponentsInChildren<RaidUIView>(true).Where(v => v.viewId == "connection"))
            { var overlay = Reference<GameObject>(view, "_overlay"); if (overlay != null) overlay.SetActive(false); }
            foreach (var role in Object.FindObjectsByType<RoleSelectionManager>(FindObjectsSortMode.None))
                if (role.roleModalRoot != null) role.roleModalRoot.SetActive(false);
        }

        private static T Reference<T>(RaidUIView view, string path) where T : Object => view.references.FirstOrDefault(r => r.path == path)?.target as T;
        private static void HideText(Text text) { if (text != null) text.enabled = false; }
        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rt = (RectTransform)go.transform; Set(rt, anchor, pos, size); return rt; }
        private static void Set(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        { rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = pos; rt.sizeDelta = size; }
        private static void Stretch(RectTransform rt)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        private static Image Image(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        { var rt = Rect(name, parent, anchor, pos, size); var image = rt.gameObject.AddComponent<Image>(); image.sprite = white; image.color = color; image.raycastTarget = false; return image; }
        private static Text Text(string name, Transform parent, Vector2 pos, Vector2 size, int fontSize, Color color, bool useSerif, Vector2? anchor = null)
        {
            var rt = Rect(name, parent, anchor ?? new Vector2(.5f, .5f), pos, size); var text = rt.gameObject.AddComponent<Text>();
            text.font = useSerif ? serif : sans; text.fontSize = fontSize; text.color = color; text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false; text.supportRichText = true; return text;
        }
        private static Image Fill(RectTransform parent, Color color)
        { var image = Image("Fill", parent, new Vector2(.5f, .5f), Vector2.zero, parent.sizeDelta, color); image.type = UnityEngine.UI.Image.Type.Filled; image.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; image.fillOrigin = 0; image.fillAmount = 1; return image; }
        private static Image Bar(RectTransform parent, float width, float height, Color color)
        {
            Image("Track", parent, new Vector2(.5f, .5f), Vector2.zero, new Vector2(width - 12, height - 4), new Color(.035f, .03f, .07f, .94f));
            var fillRoot = Rect("Fill area", parent, new Vector2(.5f, .5f), Vector2.zero, new Vector2(width - 24, height - 6));
            var fill = Fill(fillRoot, color);
            var shine = Image("Light edge", fill.transform, new Vector2(.5f, 1), new Vector2(0, -1), new Vector2(width - 28, 2), new Color(1, 1, 1, .22f)); shine.transform.SetAsLastSibling();
            Border(parent, width, height, height * .45f); return fill;
        }
        private static void Panel(RectTransform parent, float width, float height, float bevel)
        { Image("Panel", parent, new Vector2(.5f, .5f), Vector2.zero, new Vector2(width - 4, height - 4), Ink); Border(parent, width, height, bevel); }
        private static void Border(Transform parent, float width, float height, float bevel)
        {
            float x = width * .5f, y = height * .5f;
            Vector2[] points = { new Vector2(-x + bevel, y), new Vector2(x - bevel, y), new Vector2(x, y - bevel), new Vector2(x, -y + bevel), new Vector2(x - bevel, -y), new Vector2(-x + bevel, -y), new Vector2(-x, -y + bevel), new Vector2(-x, y - bevel) };
            for (int i = 0; i < points.Length; i++)
            { Stroke(parent, points[i] + new Vector2(0, -1), points[(i + 1) % points.Length] + new Vector2(0, -1), ShadowGold, 3); Stroke(parent, points[i], points[(i + 1) % points.Length], i < 3 ? LightGold : Gold, 1.3f); }
        }
        private static void Stroke(Transform parent, Vector2 a, Vector2 b, Color color, float width)
        { var image = Image("Gold filigree", parent, new Vector2(.5f, .5f), (a + b) * .5f, new Vector2((b - a).magnitude, width), color); image.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg); }
        private static void Ring(Transform parent, float radius, Color color, float width)
        { for (int i = 0; i < 64; i++) { float a = i * Mathf.PI * 2 / 64, b = (i + 1) * Mathf.PI * 2 / 64; Stroke(parent, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, color, width); } }
        private static void Star(Transform parent, Vector2 center, float radius)
        { for (int i = 0; i < 4; i++) { float a = i * Mathf.PI * .5f; Vector2 tip = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius; Vector2 next = center + new Vector2(Mathf.Cos(a + Mathf.PI * .25f), Mathf.Sin(a + Mathf.PI * .25f)) * radius * .24f; Stroke(parent, tip, next, LightGold, 1.4f); Stroke(parent, next, center + new Vector2(Mathf.Cos(a + Mathf.PI * .5f), Mathf.Sin(a + Mathf.PI * .5f)) * radius, Gold, 1); } }
    }
}
#endif
