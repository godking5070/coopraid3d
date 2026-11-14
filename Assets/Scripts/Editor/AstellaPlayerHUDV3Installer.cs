#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    /// <summary>One requested player-HUD resource/layout upgrade. Retains all action and layout identities.</summary>
    public static class AstellaPlayerHUDV3Installer
    {
        private const string Folder = "Assets/UI/AstellaPlayerHUDV3";
        private const string Stamp = "Logs/PlayerHUD-v3.applied.txt";

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            if (File.Exists(Stamp)) return;
            EditorApplication.update -= WhenIdle; EditorApplication.update += WhenIdle;
        }
        private static void WhenIdle()
        {
            if (File.Exists(Stamp)) { EditorApplication.update -= WhenIdle; return; }
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
            { EditorApplication.update -= WhenIdle; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || PrefabStageUtility.GetCurrentPrefabStage() != null) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity") return;
            EditorApplication.update -= WhenIdle;
            try { Apply(); }
            catch (Exception error)
            {
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/PlayerHUD-v3.error.txt", error.ToString());
                Debug.LogError("Player HUD v3 upgrade stopped; backups retained. " + error);
            }
        }

        [MenuItem("Tools/RaidRogue/UI/Apply Player HUD v3")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open Astella SampleScene.");
            var live = scene.GetRootGameObjects().Where(g => g.activeSelf).SelectMany(g => g.GetComponentsInChildren<AstellaOrnateHUD>(true))
                .Where(h => h.gameObject.activeInHierarchy).ToArray();
            if (live.Length != 1) throw new InvalidOperationException("Expected one active main-scene HUD.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            if (profile?.hudPrefab == null) throw new InvalidOperationException("Active HUD prefab missing.");
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            ImportSprites();
            string backup = "Logs/UIRepairBackups/player-v3-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            foreach (string file in new[] { path, path + ".meta", scene.path, scene.path + ".meta" }) File.Copy(file, backup + "/" + Path.GetFileName(file));
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Build(contents.GetComponentInChildren<AstellaOrnateHUD>(true));
                RaidUIAuthoring.ValidateScripts(contents);
                if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null) throw new IOException(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
            Build(live[0]); Sync(live[0]);
            RaidUIAuthoring.Validate(profile);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Stamp, DateTime.Now.ToString("s") + "\n" + path + "\nBackup: " + backup +
                "\nPlayer HUD v3: thick 560x72 gauges, 4 + circular ultimate + 4, class ultimate icons. Side artwork remains pending. Identity remains a separate real resource.");
            Debug.Log("PLAYER HUD V3 APPLIED: source layout at 0.5 scale, thick gauges, class ultimate circle/top key, actual gameplay bindings retained. Backup: " + backup);
        }

        private static void ImportSprites()
        {
            foreach (string path in Directory.GetFiles(Folder, "*.png", SearchOption.AllDirectories))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
        }

        private static void Build(AstellaOrnateHUD hud)
        {
            if (hud?.playerRoot == null || hud.secondaryResourceRoot == null) throw new InvalidOperationException("Bound player HUD is incomplete.");
            var player = hud.playerRoot.transform;
            if (player.GetComponent<AstellaPlayerHUDV3>() != null) return;
            var left = player.Find("Left - 1234") as RectTransform;
            var right = player.Find("Right - Q F R Space") as RectTransform;
            var center = player.Find("Center - V") as RectTransform;
            if (left == null || right == null || center == null) throw new InvalidOperationException("Keep the existing split-layout roots before upgrading.");
            Place(left, new Vector2(-210, 63), new Vector2(560, 128), .5f);
            Place(right, new Vector2(210, 63), new Vector2(560, 128), .5f);
            Place(center, new Vector2(0, 79), new Vector2(256, 256), .5f);
            CharacterSlot[] first = { CharacterSlot.Gcd1, CharacterSlot.Gcd2, CharacterSlot.OffGcd1, CharacterSlot.Counter };
            CharacterSlot[] second = { CharacterSlot.Job1, CharacterSlot.Movement, CharacterSlot.Identity, CharacterSlot.OffGcd2 };
            float[] x = { -216, -72, 72, 216 };
            for (int i = 0; i < 4; i++)
            {
                Side(hud, first[i], left, x[i]); Side(hud, second[i], right, x[i]);
            }

            var hp = hud.playerHpText.transform.parent as RectTransform;
            var ultimate = (RectTransform)hud.secondaryResourceRoot.transform;
            Place(hp, new Vector2(-210, 115), new Vector2(560, 72), .5f);
            Place(ultimate, new Vector2(210, 115), new Vector2(560, 72), .5f);
            Gauge(hp, hud.playerHpFill, hud.playerHpText, "hp");
            Gauge(ultimate, hud.secondaryResourceFill, hud.secondaryResourceText, "ultimate");
            // The manifest's center is V, not the job identity resource. Preserve that resource separately.
            if (hud.resourceRoot != null)
            {
                Place((RectTransform)hud.resourceRoot.transform, new Vector2(0, 208), new Vector2(140, 140), .6f);
                if (hud.resourceText != null) { hud.resourceText.fontSize = 20; hud.resourceText.resizeTextForBestFit = true; hud.resourceText.resizeTextMaxSize = 20; }
            }
            BuildUltimate(hud, center);
            hud.combatLayoutVersion = Mathf.Max(hud.combatLayoutVersion, 3);
            hud.showPlayerWarning = false;
            if (!AstellaSplitCombatLayout.IsApplied(hud)) throw new InvalidOperationException("Split layout/action identities were not preserved.");
        }

        private static void Side(AstellaOrnateHUD hud, CharacterSlot key, RectTransform parent, float x)
        {
            var slot = hud.slots.First(s => !s.sharedResurrection && s.slot == key);
            var rect = (RectTransform)slot.button.transform; rect.SetParent(parent, false);
            Center(rect, new Vector2(x, 0), new Vector2(128, 128));
            if (rect.TryGetComponent<LayoutElement>(out var element)) element.ignoreLayout = true;
            var skin = rect.GetComponent<AstellaSkillSlotSkin>();
            if (skin == null) throw new InvalidOperationException("Apply the pending-icon skillbar skin first: " + key);
            skin.normalFrame = Sprite("SkillFrames", "frame-normal"); skin.hoverFrame = Sprite("SkillFrames", "frame-hover");
            skin.pressedFrame = Sprite("SkillFrames", "frame-pressed"); skin.focusedFrame = Sprite("SkillFrames", "frame-focused"); skin.disabledFrame = Sprite("SkillFrames", "frame-disabled");
            if (skin.frame != null) skin.frame.sprite = skin.normalFrame;
            if (skin.hoverOverlay != null) skin.hoverOverlay.sprite = Sprite("SkillFrames", "overlay-hover");
            if (skin.pressedOverlay != null) skin.pressedOverlay.sprite = Sprite("SkillFrames", "overlay-pressed");
            if (skin.disabledOverlay != null) skin.disabledOverlay.sprite = Sprite("SkillFrames", "overlay-disabled");
            if (slot.cooldownShade != null) slot.cooldownShade.sprite = Sprite("SkillFrames", "cooldown-overlay");
            if (slot.keycap != null)
            {
                int size = key == CharacterSlot.Movement ? 20 : 23;
                slot.keycap.fontSize = size; slot.keycap.resizeTextMaxSize = size; slot.keycap.resizeTextMinSize = 10;
            }
            if (slot.cooldown != null) { slot.cooldown.fontSize = 40; slot.cooldown.resizeTextMaxSize = 40; }
        }

        private static void Gauge(RectTransform root, Image source, Text text, string prefix)
        {
            foreach (var gauge in root.GetComponentsInChildren<AstellaPointedGauge>(true)) gauge.enabled = false;
            foreach (var image in root.GetComponentsInChildren<Graphic>(true)) image.enabled = false;
            var visual = Rect("Gauge v3", root); Stretch(visual);
            Image("Track", visual, Sprite("Gauges", "track-inset"));
            var art = Image("Textured pointed fill", visual, Sprite("Gauges", prefix + "-fill"));
            string path = Folder + "/" + prefix + "-pointed.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Astella/UI/PointedGauge"); if (shader == null) throw new InvalidOperationException("Shared gauge shader missing.");
                material = new Material(shader) { name = "Player v3 " + prefix };
                material.SetTexture("_TipTex", Sprite("Gauges", prefix + "-tip-highlight").texture);
                material.SetFloat("_TipStrength", 1); material.SetColor("_TipColor", Color.white); AssetDatabase.CreateAsset(material, path);
            }
            art.material = material;
            Image("Fixed gold frame", visual, Sprite("Gauges", "frame-gold"));
            var clipped = visual.gameObject.AddComponent<AstellaPointedGauge>(); clipped.sourceFill = source; clipped.artwork = art;
            clipped.fillBounds = new Vector4(15f / 560, 16f / 72, 530f / 560, 40f / 72);
            clipped.pointWidth = 18f / 560; clipped.tipPadding = new Vector2(1f / 560, 1f / 72);
            text.transform.SetParent(root, false); Stretch(text.rectTransform); text.transform.SetAsLastSibling(); text.enabled = true;
            text.fontSize = 28; text.resizeTextForBestFit = true; text.resizeTextMaxSize = 28; text.resizeTextMinSize = 12;
            text.alignment = TextAnchor.MiddleCenter; text.fontStyle = FontStyle.Bold; text.color = new Color32(242, 230, 204, 255);
        }

        private static void BuildUltimate(AstellaOrnateHUD hud, RectTransform center)
        {
            var slot = hud.slots.First(s => !s.sharedResurrection && s.slot == CharacterSlot.Ultimate);
            var button = slot.button; button.transform.SetParent(center, false); Stretch((RectTransform)button.transform);
            if (button.TryGetComponent<AstellaSkillSlotSkin>(out var oldSkin)) oldSkin.enabled = false;
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            var body = Rect("Ultimate circle v3", button.transform);
            var track = Image("Circle track", body, Sprite("Sprites", "ultimate-circle-track")); track.raycastTarget = true;
            var mask = Rect("Circle interior", body);
            var clipImage = mask.gameObject.AddComponent<Image>(); clipImage.sprite = Sprite("Sprites", "ultimate-circle-mask"); clipImage.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            slot.stateGroup = mask.gameObject.AddComponent<CanvasGroup>(); slot.stateGroup.blocksRaycasts = false;
            var artwork = Image("Job ultimate artwork", mask, null); Center(artwork.rectTransform, Vector2.zero, new Vector2(224, 224));
            var cooldown = Image("Live ultimate cooldown", mask, Sprite("Sprites", "ultimate-circle-mask"));
            cooldown.type = UnityEngine.UI.Image.Type.Filled; cooldown.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            cooldown.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top; cooldown.fillClockwise = true; cooldown.fillAmount = 0;
            cooldown.color = new Color(0, 0, .025f, .7f); slot.cooldownShade = cooldown; cooldown.gameObject.SetActive(false);
            var disabled = Image("Unavailable", body, Sprite("Sprites", "ultimate-disabled-overlay")); disabled.gameObject.SetActive(false);
            Image("Fixed circle frame", body, Sprite("Sprites", "ultimate-circle-frame"));
            var ready = Image("Ready glow", body, Sprite("Sprites", "ultimate-ready-glow")); ready.gameObject.SetActive(false);
            var badge = Rect("Top key badge", body); Center(badge, new Vector2(0, 118), new Vector2(56, 36));
            Image("Badge", badge, Sprite("SkillFrames", "key-badge"));
            slot.keycap.transform.SetParent(badge, false); Stretch(slot.keycap.rectTransform); slot.keycap.enabled = true;
            slot.keycap.fontSize = 28; slot.keycap.resizeTextMaxSize = 28; slot.keycap.resizeTextMinSize = 12;
            slot.cooldown.transform.SetParent(body, false); Stretch(slot.cooldown.rectTransform); slot.cooldown.enabled = true;
            slot.cooldown.fontSize = 40; slot.cooldown.resizeTextMaxSize = 40;
            // This image is owned by the class-ultimate presenter, never by the skill palette/fallback-art updater.
            slot.artwork = null;
            button.targetGraphic = track; button.transition = Selectable.Transition.None;
            var state = hud.playerRoot.AddComponent<AstellaPlayerHUDV3>(); state.hud = hud;
            state.ultimateArtwork = artwork; state.readyGlow = ready.gameObject; state.disabledOverlay = disabled.gameObject;
            string[] jobs = { "aegis-paladin", "berserker", "dragon-breaker", "sylvan-oracle", "seraphic-bard", "blood-apothecary", "nightblade", "tempest-lancer", "gunslinger", "spirit-ranger", "astromancer", "mirror-phantom" };
            state.jobUltimateIcons = jobs.Select(id => new AstellaPlayerHUDV3.JobUltimateIcon { jobId = "job." + id, icon = Sprite("UltimateIcons", id) }).ToArray();
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size, float scale)
        { rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one * scale; }
        private static void Center(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one; }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
        private static RectTransform Rect(string name, Transform parent)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rect = (RectTransform)go.transform; Stretch(rect); return rect; }
        private static Sprite Sprite(string sub, string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/" + sub + "/" + name + ".png");
        private static Image Image(string name, Transform parent, Sprite sprite)
        { var image = Rect(name, parent).gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = Color.white; image.raycastTarget = false; return image; }

        private static void Sync(AstellaOrnateHUD hud)
        {
            foreach (var rect in new[] { hud.playerRoot.transform.Find("Left - 1234"), hud.playerRoot.transform.Find("Right - Q F R Space"),
                hud.playerRoot.transform.Find("Center - V"), hud.playerHpText.transform.parent, hud.secondaryResourceRoot.transform, hud.resourceRoot.transform }) SyncRect(rect);
            foreach (var slot in hud.slots)
            {
                if (slot?.button == null || slot.sharedResurrection || slot.slot == CharacterSlot.Potion) continue;
                SyncRect(slot.button.transform);
                foreach (var graphic in slot.button.GetComponentsInChildren<Graphic>(true)) Revert(graphic, "m_Enabled");
            }
            foreach (var root in new[] { hud.playerHpText.transform.parent, hud.secondaryResourceRoot.transform })
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) Revert(graphic, "m_Enabled");
            int index = Array.FindIndex(hud.slots, s => !s.sharedResurrection && s.slot == CharacterSlot.Ultimate);
            if (index >= 0) foreach (string field in new[] { "artwork", "cooldownShade", "stateGroup" }) Revert(hud, "slots.Array.data[" + index + "]." + field);
        }
        private static void SyncRect(Transform target)
        { foreach (string key in new[] { "m_AnchorMin", "m_AnchorMax", "m_Pivot", "m_AnchoredPosition", "m_SizeDelta", "m_LocalScale" }) Revert(target, key); }
        private static void Revert(UnityEngine.Object target, string key)
        {
            if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target) || PrefabUtility.GetCorrespondingObjectFromSource(target) == null) return;
            var property = new SerializedObject(target).FindProperty(key);
            if (property != null && property.prefabOverride) PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
        }
    }
}
#endif
