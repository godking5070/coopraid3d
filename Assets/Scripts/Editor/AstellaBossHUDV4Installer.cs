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
    /// <summary>One requested boss-resource upgrade. Existing boss/cast layout roots and gameplay references survive.</summary>
    public static class AstellaBossHUDV4Installer
    {
        private const string Folder = "Assets/UI/AstellaBossHUDV4";
        private const string Stamp = "Logs/BossHUD-v4.applied.txt";
        private static Font sans, serif;
        private static readonly Color Gold = new Color32(242, 230, 204, 255);

        // Authoring is explicit. A missing local Logs stamp must never reapply a design.
        [MenuItem("Tools/RaidRogue/UI/Apply Boss HUD v4")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open Astella SampleScene.");
            var live = scene.GetRootGameObjects().Where(g => g.activeSelf).SelectMany(g => g.GetComponentsInChildren<AstellaOrnateHUD>(true))
                .Where(h => h.gameObject.activeInHierarchy).ToArray();
            if (live.Length != 1) throw new InvalidOperationException("Expected exactly one active HUD.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            if (profile?.hudPrefab == null) throw new InvalidOperationException("Active HUD prefab missing.");
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            ImportSprites();
            sans = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            serif = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/AstellaOrnate/Fonts/NanumMyeongjo-Regular.ttf") ?? sans;
            string backup = "Logs/UIRepairBackups/boss-v4-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
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
            // An unpacked scene HUD also receives this change; inherited prefab references are synced narrowly.
            Build(live[0]);
            foreach (string field in new[] { "stageText", "bossTitleText", "bossPercentText", "castNameText", "castTimeText" }) Revert(live[0], field);
            foreach (var graphic in live[0].bossRoot.GetComponentsInChildren<Graphic>(true)) Revert(graphic, "m_Enabled");
            RaidUIAuthoring.Validate(profile);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Stamp, DateTime.Now.ToString("s") + "\n" + path + "\nBackup: " + backup +
                "\nBoss HUD v4: ruby HP, violet cast, pointed fill, fixed centered values, actual target line. Existing boss/cast layout IDs retained.");
            Debug.Log("BOSS HUD V4 APPLIED: supplied resource layers, actual HP/cast data, centered values and actual target. Backup: " + backup);
        }

        private static void ImportSprites()
        {
            foreach (string path in Directory.GetFiles(Folder + "/Sprites", "*.png"))
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
            if (hud?.bossRoot == null || hud.castRoot == null) throw new InvalidOperationException("Current boss/cast root missing.");
            var outer = hud.bossRoot.transform;
            if (outer.GetComponent<AstellaBossHUDV4>() != null) return;
            var cast = (RectTransform)hud.castRoot.transform;
            var content = cast.parent as RectTransform;
            if (content == null || !content.IsChildOf(outer)) throw new InvalidOperationException("Expected cast composition under boss root.");
            var ratio = content.GetComponent<AspectRatioFitter>();
            if (ratio == null) throw new InvalidOperationException("Proportional boss composition missing.");
            ratio.aspectMode = AspectRatioFitter.AspectMode.FitInParent; ratio.aspectRatio = 1280f / 280;
            foreach (var graphic in outer.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            foreach (var outline in outer.GetComponentsInChildren<Outline>(true)) outline.enabled = false;

            hud.stageText = Label("Stage v4", content, 360, 7, 560, 30, 18, false, 1280, 280);
            hud.stageText.text = "STAGE 01 · NORMAL";
            Picture("Title divider v4", content, "title-divider", 326, 48, 628, 24, 1280, 280);
            hud.bossTitleText = Label("Boss name v4", content, 464, 39, 352, 54, 42, true, 1280, 280);
            hud.bossTitleText.text = "마을 수호 석상";
            Picture("Title star left v4", content, "tiny-star", 435, 48, 24, 24, 1280, 280);
            Picture("Title star right v4", content, "tiny-star", 821, 48, 24, 24, 1280, 280);
            var hp = Rect("Health v4", content, 98, 92, 1032, 48, 1280, 280);
            Gauge(hp, "hp", hud.bossHpFill);
            Picture("Boss emblem v4", content, "emblem-boss", 54, 74, 84, 84, 1280, 280);
            hud.bossPercentText = Label("HP percent v4", hp, 0, 2, 1000, 60, 26, false, 1000, 64);
            hud.bossPercentText.text = "100%"; hud.bossPercentText.fontStyle = FontStyle.Bold; Shadow(hud.bossPercentText);

            // Reuse this exact cast root for the layout editor and presenter visibility.
            Set(cast, 378, 150, 524, 74, 1280, 280);
            hud.castNameText = Label("Cast name v4", cast, 22, 0, 480, 38, 22, false, 524, 74);
            var bar = Rect("Cast gauge v4", cast, 0, 40, 524, 32, 524, 74);
            Gauge(bar, "cast", hud.castFill);
            hud.castTimeText = Label("Cast remaining v4", bar, 0, 0, 1000, 64, 20, false, 1000, 64);
            hud.castTimeText.fontStyle = FontStyle.Bold; Shadow(hud.castTimeText);
            hud.castNameText.text = ""; hud.castTimeText.text = "";
            hud.castRoot.SetActive(false);

            var target = Rect("Actual target v4", content, 454, 232, 372, 32, 1280, 280);
            Picture("Target marker", target, "target-marker", 0, 8, 20, 16, 372, 32);
            var name = Label("Actual target name", target, 32, 0, 340, 32, 20, false, 372, 32);
            name.alignment = TextAnchor.MiddleLeft;
            var presenter = outer.gameObject.AddComponent<AstellaBossHUDV4>();
            presenter.hud = hud; presenter.targetRoot = target.gameObject; presenter.targetLine = target; presenter.targetText = name;
            presenter.targetPrefix = "공격 대상  "; presenter.castingAnchor = new Vector2(.5f, 1 - 248f / 280);
            presenter.idleAnchor = new Vector2(.5f, 1 - 176f / 280);
            target.anchorMin = target.anchorMax = presenter.idleAnchor;
            target.sizeDelta = new Vector2(372, 32); target.anchoredPosition = Vector2.zero; target.gameObject.SetActive(false);
        }

        private static void Gauge(RectTransform root, string prefix, Image source)
        {
            Picture("Track", root, prefix + "-track", 0, 0, 1000, 64, 1000, 64);
            var art = Picture("Pointed textured fill", root, prefix + "-fill", 0, 0, 1000, 64, 1000, 64);
            string path = Folder + "/" + prefix + "-pointed.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Astella/UI/PointedGauge");
                if (shader == null) throw new InvalidOperationException("Shared pointed-gauge shader is missing.");
                material = new Material(shader) { name = "Boss v4 " + prefix };
                material.SetTexture("_TipTex", Sprite(prefix + "-tip-highlight").texture);
                material.SetFloat("_TipStrength", 1); material.SetColor("_TipColor", Color.white);
                AssetDatabase.CreateAsset(material, path);
            }
            art.material = material;
            Picture("Fixed frame", root, prefix + "-frame", 0, 0, 1000, 64, 1000, 64);
            var gauge = root.gameObject.AddComponent<AstellaPointedGauge>(); gauge.sourceFill = source; gauge.artwork = art;
            gauge.fillBounds = new Vector4(.009f, 14f / 64, .982f, 36f / 64); gauge.pointWidth = .012f;
        }
        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/Sprites/" + name + ".png");
        private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h, float pw, float ph)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; Set(rect, x, y, w, h, pw, ph); return rect;
        }
        private static void Set(RectTransform rect, float x, float y, float w, float h, float pw, float ph)
        {
            rect.anchorMin = new Vector2(x / pw, 1 - (y + h) / ph); rect.anchorMax = new Vector2((x + w) / pw, 1 - y / ph);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private static Image Picture(string name, Transform parent, string sprite, float x, float y, float w, float h, float pw, float ph)
        {
            var image = Rect(name, parent, x, y, w, h, pw, ph).gameObject.AddComponent<Image>();
            image.sprite = Sprite(sprite); if (image.sprite == null) throw new InvalidOperationException("Missing v4 sprite: " + sprite);
            image.color = Color.white; image.raycastTarget = false; return image;
        }
        private static Text Label(string name, Transform parent, float x, float y, float w, float h, int size, bool useSerif, float pw, float ph)
        {
            var text = Rect(name, parent, x, y, w, h, pw, ph).gameObject.AddComponent<Text>();
            text.font = useSerif ? serif : sans; text.fontSize = size; text.color = Gold;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = size; text.resizeTextMinSize = 8;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
        }
        private static void Shadow(Text text)
        { var shadow = text.gameObject.AddComponent<UnityEngine.UI.Shadow>(); shadow.effectColor = new Color(0, 0, 0, .85f); shadow.effectDistance = new Vector2(1, -1); }
        private static void Revert(UnityEngine.Object target, string key)
        {
            if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target) || PrefabUtility.GetCorrespondingObjectFromSource(target) == null) return;
            var property = new SerializedObject(target).FindProperty(key);
            if (property != null && property.prefabOverride) PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
        }
    }
}
#endif
