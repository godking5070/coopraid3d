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
    public static class AstellaPlayerPartyResourceInstaller
    {
        private const string Gauges = "Assets/UI/AstellaPlayerGaugesV2";
        private const string Party = "Assets/UI/AstellaPartyHUDV4";
        private static Font font;
        private static Sprite white;
        private static Color gold = new Color32(242, 230, 204, 255);

        [MenuItem("Tools/RaidRogue/UI/Apply Player Gauges v2 and Party HUD v4 &F3")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open the Astella main scene.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            Import(Gauges); Import(Party);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            white = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/AstellaOrnate/Shapes/White.png");
            string backup = "Logs/UIRepairBackups/player-party-v2-v4-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            foreach (string file in new[] { path, path + ".meta", scene.path, scene.path + ".meta" })
                File.Copy(file, backup + "/" + Path.GetFileName(file));
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentInChildren<AstellaPartyHUDV4>(true) != null) throw new InvalidOperationException("Resources already applied; edit the authored prefab.");
                ApplyToHierarchy(root);
                RaidUIAuthoring.ValidateScripts(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets(); RaidUIAuthoring.Validate(profile);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("PLAYER GAUGES V2 + PARTY HUD V4 APPLIED. Existing player/identity/skill layout roots retained. Backup: " + backup);
        }

        public static void ApplyToHierarchy(GameObject root)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            white = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/AstellaOrnate/Shapes/White.png");
            var hud = root.GetComponentInChildren<AstellaOrnateHUD>(true);
            var party = root.GetComponentInChildren<PartyListUI>(true);
            if (hud == null || party?.panelRoot == null || party.memberRows.Length != 4) throw new InvalidOperationException("Bound HUD/party is incomplete.");
            var health = (RectTransform)hud.playerHpText.transform.parent;
            if (health.Find("Health v2") == null)
                Gauge(health, hud.playerHpFill, hud.playerHpText, "Health v2", "hp-fill", Gauges, "frame-gold", "track-inset", "hp-tip-highlight");
            if (hud.secondaryResourceRoot != null && hud.secondaryResourceRoot.transform.Find("Ultimate v2") == null)
                Gauge((RectTransform)hud.secondaryResourceRoot.transform, hud.secondaryResourceFill, hud.secondaryResourceText,
                    "Ultimate v2", "ultimate-fill", Gauges, "frame-gold", "track-inset", "ultimate-tip-highlight");
            if (root.GetComponentInChildren<AstellaPartyHUDV4>(true) == null) BuildParty(party);
        }

        private static void Import(string folder)
        {
            foreach (string path in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 2048;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
        }
        private static Sprite Sprite(string folder, string name) => AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/Sprites/" + name + ".png");
        private static Material Material(string name, string folder, string tip)
        {
            string path = Gauges + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Astella/UI/PointedGauge");
            if (shader == null) throw new InvalidOperationException("PointedGauge shader not yet imported.");
            material = new Material(shader) { name = name };
            material.SetTexture("_TipTex", Sprite(folder, tip).texture);
            material.SetColor("_TipColor", Color.white); material.SetFloat("_TipStrength", 1);
            AssetDatabase.CreateAsset(material, path); return material;
        }

        private static AstellaPointedGauge Gauge(RectTransform outer, Image source, Text text, string name, string fill,
            string folder, string frame, string track, string tip)
        {
            // Preserve the outer layout ID, parent, pose, scale and size. Only the new graphic child follows the source art ratio.
            foreach (var graphic in outer.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            var group = Rect(name, outer, new Vector2(.5f, .5f), Vector2.zero, outer.rect.size);
            group.anchorMin = new Vector2(0, .5f); group.anchorMax = new Vector2(1, .5f); group.sizeDelta = Vector2.zero;
            var fit = group.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight; fit.aspectRatio = 1000f / 64;
            FullImage("Track", group, Sprite(folder, track));
            var artwork = FullImage("Textured fill", group, Sprite(folder, fill));
            artwork.material = Material(name.Replace(" ", "") + "Pointed", folder, tip);
            FullImage("Fixed metallic frame", group, Sprite(folder, frame));
            var gauge = group.gameObject.AddComponent<AstellaPointedGauge>(); gauge.sourceFill = source; gauge.artwork = artwork;
            if (text != null)
            {
                text.enabled = true; text.transform.SetParent(outer, false); text.transform.SetAsLastSibling();
                text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = new Vector2(16, -8); text.rectTransform.offsetMax = new Vector2(-16, 8);
                text.color = gold; text.resizeTextForBestFit = true; text.resizeTextMaxSize = Mathf.Max(18, text.fontSize); text.resizeTextMinSize = 8;
                text.alignment = TextAnchor.MiddleCenter;
            }
            return gauge;
        }

        private static void BuildParty(PartyListUI source)
        {
            var outer = source.panelRoot;
            // Preserve the user's layout-editor root, including its size and scale.
            foreach (var graphic in outer.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            foreach (var outline in outer.GetComponentsInChildren<Outline>(true)) outline.enabled = false;
            foreach (var icon in outer.GetComponentsInChildren<RaidUIIcon>(true)) icon.image = null;
            var panel = Rect("Party v4 artwork", outer, new Vector2(0, 1), Vector2.zero, new Vector2(760, 308));
            panel.pivot = new Vector2(0, 1); panel.localScale = Vector3.one * .64f;
            var ui = source.GetComponentInParent<RaidUIRoot>().gameObject.AddComponent<AstellaPartyHUDV4>(); ui.source = source; ui.panelRoot = outer.gameObject;
            ui.headerText = Label("Expedition size", panel, 14, 0, 720, 36, 28); ui.headerFormat = "원정대 {0}";
            ui.normalHpSprite = Sprite(Party, "hp-fill"); ui.criticalHpSprite = Sprite(Party, "hp-fill-critical");
            string[] ids = { "aegis-paladin", "berserker", "dragon-breaker", "sylvan-oracle", "seraphic-bard", "blood-apothecary", "nightblade", "tempest-lancer", "gunslinger", "spirit-ranger", "astromancer", "mirror-phantom" };
            var icons = ids.Select(id => new AstellaPartyHUDV4.JobIcon { jobId = "job." + id, icon = AssetDatabase.LoadAssetAtPath<Sprite>(Party + "/ClassIcons/" + id + ".png") }).ToList();
            string[] legacy = { "guardian", "oracle", "blade", "archmage" }; string[] fallback = { "aegis-paladin", "sylvan-oracle", "nightblade", "astromancer" };
            for (int i = 0; i < legacy.Length; i++) icons.Add(new AstellaPartyHUDV4.JobIcon { jobId = "test." + legacy[i], icon = AssetDatabase.LoadAssetAtPath<Sprite>(Party + "/ClassIcons/" + fallback[i] + ".png") });
            ui.jobIcons = icons.ToArray(); ui.rows = new AstellaPartyHUDV4.Row[4];
            for (int i = 0; i < ui.rows.Length; i++)
            {
                var old = source.memberRows[i];
                var rect = Rect("Member v4 " + (i + 1), panel, new Vector2(0, 1), new Vector2(0, -44 - 66 * i), new Vector2(760, 64));
                rect.pivot = new Vector2(0, 1);
                // Align legacy manual hit testing with the newly authored button.
                var oldRect = (RectTransform)old.rowRoot.transform;
                oldRect.SetParent(panel, false);
                oldRect.anchorMin = oldRect.anchorMax = new Vector2(0, 1); oldRect.pivot = new Vector2(0, 1);
                oldRect.anchoredPosition = rect.anchoredPosition; oldRect.sizeDelta = rect.sizeDelta;
                var hit = FullImage("Click target", rect, white); hit.color = Color.clear; hit.raycastTarget = true;
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = hit; button.transition = Selectable.Transition.None;
                ImageAt("Name shade", rect, Sprite(Party, "name-backplate"), 54, 0, 302, 64);
                var row = new AstellaPartyHUDV4.Row { sourceIndex = i, root = rect.gameObject, button = button };
                row.stateGroup = rect.gameObject.AddComponent<CanvasGroup>();
                row.jobIcon = ImageAt("Class icon", rect, icons[0].icon, 6, 8, 48, 48); row.fallbackJobIcon = icons[0].icon;
                row.nameText = Label("Name", rect, 64, 0, 210, 32, 28);
                row.hpText = Label("HP percent", rect, 280, 0, 64, 32, 25); row.hpText.alignment = TextAnchor.MiddleRight;
                row.keyText = Label("Party key", rect, 8, 48, 44, 16, 13); row.keyText.alignment = TextAnchor.MiddleCenter;
                var hp = Rect("Health artwork", rect, new Vector2(0, 1), new Vector2(62, -36), new Vector2(284, 22)); hp.pivot = new Vector2(0, 1);
                FullImage("Track", hp, Sprite(Party, "hp-track"));
                var art = FullImage("Textured fill", hp, ui.normalHpSprite); art.material = Material("PartyHPPointed", Party, "hp-tip-highlight");
                FullImage("Fixed metallic frame", hp, Sprite(Party, "hp-frame"));
                row.gauge = hp.gameObject.AddComponent<AstellaPointedGauge>(); row.gauge.artwork = art;
                row.dead = ImageAt("Defeated glyph", rect, Sprite(Party, "status-defeated"), 370, 20, 24, 24).gameObject;
                var deadText = Label("Defeated label", row.dead.transform, 32, -6, 180, 36, 25); deadText.text = "전투불능";
                row.dead.SetActive(false);
                row.selected = Label("Selected", rect, -18, 16, 22, 32, 24).gameObject; row.selected.GetComponent<Text>().text = "▶"; row.selected.GetComponent<Text>().color = new Color32(110, 221, 236, 255); row.selected.SetActive(false);
                row.aggro = Label("Aggro", rect, 350, 38, 24, 24, 16).gameObject; row.aggro.GetComponent<Text>().text = "!"; row.aggro.GetComponent<Text>().color = new Color32(238, 110, 100, 255); row.aggro.SetActive(false);
                var effects = Rect("Active effects", rect, new Vector2(0, 1), new Vector2(366, -8), new Vector2(368, 56)); effects.pivot = new Vector2(0, 1);
                var layout = effects.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 10; layout.childAlignment = TextAnchor.UpperLeft;
                layout.childControlWidth = layout.childControlHeight = false; layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                string[] status = { "shield", "revival-protection", "invulnerable", "morning", "knockback-immunity", "control-immunity", "flora-attack", "stun", "root", "bleed", "poison", "burn", "slow", "weakness" };
                row.statuses = status.Select(key => Status(effects, key)).ToArray();
                ui.rows[i] = row;
            }
        }

        private static AstellaPartyHUDV4.StatusView Status(RectTransform parent, string key)
        {
            bool debuff = new[] { "stun", "root", "bleed", "poison", "burn", "slow", "weakness" }.Contains(key);
            var rect = Rect(key, parent, new Vector2(0, 1), Vector2.zero, new Vector2(36, 56));
            ImageAt("Metallic slot", rect, Sprite(Party, debuff ? "debuff-slot" : "buff-slot"), 0, 0, 36, 36);
            string art = key == "bleed" || key == "poison" || key == "burn" ? "status-bleed" : key == "morning" || key == "revival-protection" ? "status-heal" : "status-shield";
            ImageAt("Effect glyph", rect, Sprite(Party, art), 4, 3, 28, 28);
            if (debuff && art == "status-shield")
            { var text = Label("Condition code", rect, 6, 5, 24, 25, 17); text.text = key == "stun" ? "!" : key == "root" ? "×" : key == "slow" ? "↓" : "−"; }
            var timer = Label("Remaining", rect, -6, 35, 48, 23, 16); timer.alignment = TextAnchor.MiddleCenter;
            rect.gameObject.SetActive(false);
            return new AstellaPartyHUDV4.StatusView { key = key, root = rect.gameObject, remainingText = timer };
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = anchor; rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
        }
        private static Image FullImage(string name, Transform parent, Sprite sprite)
        {
            var rt = Rect(name, parent, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var image = rt.gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = Color.white; image.raycastTarget = false; return image;
        }
        private static Image ImageAt(string name, Transform parent, Sprite sprite, float x, float y, float w, float h)
        {
            var rt = Rect(name, parent, new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h)); rt.pivot = new Vector2(0, 1);
            var image = rt.gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = Color.white; image.raycastTarget = false; return image;
        }
        private static Text Label(string name, Transform parent, float x, float y, float w, float h, int size)
        {
            var rt = Rect(name, parent, new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h)); rt.pivot = new Vector2(0, 1);
            var text = rt.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.color = gold;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false; text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = size; text.resizeTextMinSize = 10; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }
    }
}
#endif
