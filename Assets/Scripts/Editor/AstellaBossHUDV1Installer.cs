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
    /// <summary>Authors the supplied, separated boss sprites without changing combat or UI at runtime.</summary>
    public static class AstellaBossHUDV1Installer
    {
        private const string Folder = "Assets/UI/AstellaBossHUDv1";
        private const string Profile = "Assets/Resources/UI/RaidUIPresentation.asset";
        private const float Width = 1360, Height = 340;
        private static readonly Color TextColor = new Color32(242, 230, 204, 255);
        private static Font serif, sans;

        [MenuItem("Tools/RaidRogue/UI/Apply Supplied Boss HUD v1 &F5")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open Astella SampleScene.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(Profile);
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            if (!path.StartsWith("Assets/UI/AstellaOrnate/Applied-", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected the currently applied celestial HUD.");
            ImportSprites();
            sans = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            serif = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/AstellaOrnate/Fonts/NanumMyeongjo-Regular.ttf") ?? sans;
            string backup = "Logs/UIRepairBackups/boss-v1-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            File.Copy(path, backup + "/RaidHUD.prefab"); File.Copy(path + ".meta", backup + "/RaidHUD.prefab.meta");
            File.Copy(scene.path, backup + "/SampleScene.unity"); File.Copy(scene.path + ".meta", backup + "/SampleScene.unity.meta");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var hud = root.GetComponentInChildren<AstellaOrnateHUD>(true);
                if (hud == null || hud.bossRoot == null) throw new InvalidOperationException("Live boss presenter missing.");
                if (hud.bossRoot.name == "Supplied Boss HUD v1") throw new InvalidOperationException("Already applied. Edit the authored prefab to refine it.");
                Build(hud);
                Validate(hud);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            RaidUIAuthoring.Validate(profile);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("BOSS HUD V1 APPLIED: supplied sprites, fixed frames, live HP/cast fills, responsive centered composition. Backup: " + backup);
        }

        private static void ImportSprites()
        {
            foreach (string path in Directory.GetFiles(Folder + "/Sprites", "*.png"))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 2048;
                bool hp = Path.GetFileName(path).StartsWith("hp-");
                bool sliced = path.EndsWith("-frame.png") || path.EndsWith("-track.png");
                importer.spriteBorder = !sliced ? Vector4.zero : hp ? new Vector4(48, 24, 48, 24) : new Vector4(104, 40, 40, 40);
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
        }

        private static void Build(AstellaOrnateHUD hud)
        {
            var old = hud.bossRoot;
            var outer = new GameObject("Supplied Boss HUD v1", typeof(RectTransform));
            outer.transform.SetParent(old.transform.parent, false);
            var available = (RectTransform)outer.transform;
            available.anchorMin = new Vector2(.04f, 1); available.anchorMax = new Vector2(.96f, 1);
            available.pivot = new Vector2(.5f, 1); available.sizeDelta = new Vector2(0, 270);
            available.anchoredPosition = new Vector2(0, -18);
            var design = new GameObject("Proportional composition", typeof(RectTransform), typeof(AspectRatioFitter));
            design.transform.SetParent(outer.transform, false);
            var fit = design.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = Width / Height;
            var content = (RectTransform)design.transform;

            // Every child uses proportional anchors in a 1360 x 340 design space.
            // FitInParent keeps that aspect at narrow widths, including the emblem and text spacing.
            var backdrop = Rect("Readability backdrop", content, 0, 0, Width, Height, Width, Height);
            var fade = backdrop.gameObject.AddComponent<Image>();
            fade.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/AstellaOrnate/Shapes/BottomFade.png");
            fade.color = new Color(.025f, .025f, .055f, .72f); fade.raycastTarget = false;
            backdrop.localRotation = Quaternion.Euler(0, 0, 180);
            hud.stageText = Label("Stage", content, 460, 0, 440, 32, 19, false, Width, Height);
            hud.stageText.text = "STAGE 01 · NORMAL";
            hud.bossTitleText = Label("Boss name", content, 170, 40, 1020, 60, 42, true, Width, Height);
            hud.bossTitleText.text = "마을 수호 석상";
            Picture("Name star left", content, "tiny-star", 145, 58, 24, 24, Width, Height);
            Picture("Name star right", content, "tiny-star", 1191, 58, 24, 24, Width, Height);

            // Supplied HP canvas 1200x64; interior 40,20,1120,24.
            var health = Rect("Boss health", content, 70, 112, 1200, 64, Width, Height);
            Picture("HP track", health, "hp-track", 0, 0, 1200, 64, 1200, 64);
            hud.bossHpFill = Filled("HP fill", health, "hp-fill", 40, 20, 1120, 24, 1200, 64);
            Picture("HP fixed frame", health, "hp-frame", 0, 0, 1200, 64, 1200, 64);
            Picture("Boss emblem", health, "emblem-boss", -27, -16, 96, 96, 1200, 64);
            hud.bossPercentText = Label("HP percent", content, 1274, 116, 86, 54, 30, false, Width, Height);
            hud.bossPercentText.text = "100%";

            // Supplied cast canvas 760x104; interior 108,68,620,10.
            var cast = Rect("Boss cast", content, 300, 188, 760, 104, Width, Height);
            Picture("Cast track", cast, "cast-track", 0, 0, 760, 104, 760, 104);
            hud.castFill = Filled("Cast fill", cast, "cast-fill", 108, 68, 620, 10, 760, 104);
            Picture("Cast fixed frame", cast, "cast-frame", 0, 0, 760, 104, 760, 104);
            Picture("Hourglass", cast, "icon-hourglass", 23, 20, 64, 64, 760, 104);
            hud.castNameText = Label("Cast name", cast, 108, 20, 500, 42, 25, false, 760, 104);
            hud.castNameText.alignment = TextAnchor.MiddleLeft;
            hud.castTimeText = Label("Cast remaining", cast, 620, 20, 108, 42, 25, false, 760, 104);
            hud.castTimeText.alignment = TextAnchor.MiddleRight;
            hud.castNameText.text = ""; hud.castTimeText.text = "";
            hud.bossRoot = outer; hud.castRoot = cast.gameObject;
            cast.gameObject.SetActive(false);
            old.SetActive(false); // Keep the prior authored design recoverable in the same prefab.
        }

        private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h, float parentW, float parentH)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(x / parentW, 1 - (y + h) / parentH);
            rt.anchorMax = new Vector2((x + w) / parentW, 1 - y / parentH);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }
        private static Image Picture(string name, Transform parent, string sprite, float x, float y, float w, float h, float parentW, float parentH)
        {
            var image = Rect(name, parent, x, y, w, h, parentW, parentH).gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/Sprites/" + sprite + ".png");
            if (image.sprite == null) throw new InvalidOperationException("Missing supplied sprite: " + sprite);
            image.color = Color.white; image.raycastTarget = false; image.type = Image.Type.Simple;
            return image;
        }
        private static Image Filled(string name, Transform parent, string sprite, float x, float y, float w, float h, float parentW, float parentH)
        {
            var image = Picture(name, parent, sprite, x, y, w, h, parentW, parentH);
            image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; image.fillOrigin = 0;
            image.fillAmount = 1; return image;
        }
        private static Text Label(string name, Transform parent, float x, float y, float w, float h, int size, bool useSerif, float parentW, float parentH)
        {
            var text = Rect(name, parent, x, y, w, h, parentW, parentH).gameObject.AddComponent<Text>();
            text.font = useSerif ? serif : sans; text.fontSize = size; text.color = TextColor;
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 8; text.resizeTextMaxSize = size;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        private static void Validate(AstellaOrnateHUD hud)
        {
            foreach (var image in new[] { hud.bossHpFill, hud.castFill })
            {
                if (image.type != Image.Type.Filled || image.fillMethod != Image.FillMethod.Horizontal || image.fillOrigin != 0)
                    throw new InvalidOperationException("Invalid left-to-right fill.");
                var frame = image.transform.parent.Find(image == hud.bossHpFill ? "HP fixed frame" : "Cast fixed frame").GetComponent<Image>();
                var rect = frame.rectTransform; var min = rect.anchorMin; var max = rect.anchorMax;
                foreach (float value in new[] { 0f, .5f, 1f })
                {
                    image.fillAmount = value;
                    if (frame.type != Image.Type.Simple || rect.anchorMin != min || rect.anchorMax != max)
                        throw new InvalidOperationException("Frame changed with fill.");
                }
                image.fillAmount = 1;
            }
            if (hud.castRoot.activeSelf) throw new InvalidOperationException("Idle cast should start hidden.");
            var fit = hud.bossRoot.GetComponentInChildren<AspectRatioFitter>(true);
            if (fit.aspectMode != AspectRatioFitter.AspectMode.FitInParent || !Mathf.Approximately(fit.aspectRatio, 4))
                throw new InvalidOperationException("Responsive group aspect is missing: " + fit.name + " / " + fit.aspectMode + " / " + fit.aspectRatio);
            Debug.Log("BOSS HUD V1 VALIDATED: 0/50/100% fills leave both frames fixed; idle cast hidden; group ratio 4:1.");
        }
    }
}
#endif
