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
    public static class AstellaSkillbarV1Installer
    {
        private const string Folder = "Assets/UI/AstellaSkillbarV1";

        [MenuItem("Tools/RaidRogue/UI/Apply Skillbar v1 (Icons Pending)")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open Astella SampleScene.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            ImportSprites();
            string backup = "Logs/UIRepairBackups/skillbar-v1-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            File.Copy(path, backup + "/RaidHUD.prefab"); File.Copy(path + ".meta", backup + "/RaidHUD.prefab.meta");
            // Includes any existing unsaved scene edits in the recovery copy before applying this requested skin.
            EditorSceneManager.SaveScene(scene, backup + "/SampleScene.unity", true);
            var root = PrefabUtility.LoadPrefabContents(path);
            int count = 0;
            try
            {
                count = ApplyToHierarchy(root);
                RaidUIAuthoring.ValidateScripts(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets(); RaidUIAuthoring.Validate(profile);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("SKILLBAR V1 APPLIED: " + count + " skill slots; icons pending, actual input/cooldown bindings retained. Backup: " + backup);
        }

        public static int ApplyToHierarchy(GameObject root)
        {
            var hud = root.GetComponentInChildren<AstellaOrnateHUD>(true);
            if (hud == null) throw new InvalidOperationException("Current HUD presenter missing.");
            int count = 0;
            foreach (var slot in hud.slots)
            {
                if (slot?.button == null || slot.sharedResurrection || slot.slot == CharacterSlot.Potion) continue;
                if (slot.button.GetComponent<AstellaSkillSlotSkin>() != null) continue;
                Skin(hud, slot, Array.IndexOf(hud.slots, slot)); count++;
            }
            return count;
        }

        private static void ImportSprites()
        {
            foreach (string path in Directory.GetFiles(Folder + "/Sprites", "*.png"))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false; importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
        }
        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/Sprites/" + name + ".png");
        private static void Skin(AstellaOrnateHUD hud, AstellaOrnateHUD.SlotBinding slot, int index)
        {
            var button = slot.button;
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            foreach (var outline in button.GetComponentsInChildren<Outline>(true)) outline.enabled = false;
            // Keep the old artwork field/data binding recoverable, but don't present any unapproved skill art.
            if (slot.artwork != null)
            {
                var stored = Rect("Pending icon binding (hidden)", button.transform);
                slot.artwork.transform.SetParent(stored, false);
                stored.gameObject.SetActive(false);
            }
            var composition = Rect("Skillbar v1", button.transform);
            var background = FullImage("Slot background", composition, "slot-background"); background.raycastTarget = true;
            var inner = Rect("Masked interior", composition);
            var mask = inner.gameObject.AddComponent<Image>(); mask.sprite = Sprite("slot-inner-mask"); mask.raycastTarget = false;
            inner.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            slot.stateGroup = inner.gameObject.AddComponent<CanvasGroup>(); slot.stateGroup.blocksRaycasts = false;
            // Empty, intentionally unassigned until the user's job artwork is selected.
            var placeholder = Text("Icon pending marker", inner, 22); placeholder.text = "+"; placeholder.color = new Color(.68f, .68f, .72f, .35f);
            var cooldown = FullImage("Live radial cooldown", inner, "cooldown-overlay");
            cooldown.type = Image.Type.Filled; cooldown.fillMethod = Image.FillMethod.Radial360;
            cooldown.fillOrigin = (int)Image.Origin360.Top; cooldown.fillClockwise = true; cooldown.fillAmount = 0;
            slot.cooldownShade = cooldown; cooldown.gameObject.SetActive(false);
            var hover = FullImage("Hover overlay", inner, "overlay-hover"); hover.enabled = false;
            var pressed = FullImage("Pressed overlay", inner, "overlay-pressed"); pressed.enabled = false;
            var disabled = FullImage("Unavailable overlay", inner, "overlay-disabled"); disabled.enabled = false;
            var frame = FullImage("Current metallic frame", composition, "frame-normal");
            bool wide = slot.slot == CharacterSlot.Movement;
            var badge = Rect("Binding badge", composition);
            badge.anchorMin = new Vector2(3f / 128, 1 - 29f / 128);
            badge.anchorMax = new Vector2((wide ? 73f : 45f) / 128, 1 - 1f / 128);
            badge.offsetMin = badge.offsetMax = Vector2.zero;
            var badgeImage = badge.gameObject.AddComponent<Image>(); badgeImage.sprite = Sprite(wide ? "key-badge-wide" : "key-badge"); badgeImage.raycastTarget = false;
            if (slot.keycap != null)
            {
                slot.keycap.transform.SetParent(badge, false); Stretch(slot.keycap.rectTransform);
                slot.keycap.enabled = true; slot.keycap.fontSize = wide ? 13 : 15;
                slot.keycap.resizeTextForBestFit = true; slot.keycap.resizeTextMaxSize = wide ? 13 : 15; slot.keycap.resizeTextMinSize = 7;
                slot.keycap.alignment = TextAnchor.MiddleCenter; slot.keycap.color = new Color32(242, 230, 204, 255);
            }
            if (slot.cooldown != null)
            {
                slot.cooldown.transform.SetParent(composition, false); Stretch(slot.cooldown.rectTransform);
                slot.cooldown.enabled = true; slot.cooldown.fontSize = 28;
                slot.cooldown.resizeTextForBestFit = true; slot.cooldown.resizeTextMaxSize = 28; slot.cooldown.resizeTextMinSize = 10;
                slot.cooldown.alignment = TextAnchor.MiddleCenter; slot.cooldown.color = Color.white;
            }
            button.targetGraphic = background; button.transition = Selectable.Transition.None;
            var skin = button.gameObject.AddComponent<AstellaSkillSlotSkin>();
            skin.button = button; skin.hud = hud; skin.slotIndex = index;
            skin.frame = frame; skin.hoverOverlay = hover; skin.pressedOverlay = pressed; skin.disabledOverlay = disabled;
            skin.normalFrame = Sprite("frame-normal"); skin.hoverFrame = Sprite("frame-hover");
            skin.pressedFrame = Sprite("frame-pressed"); skin.focusedFrame = Sprite("frame-focused"); skin.disabledFrame = Sprite("frame-disabled");
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; Stretch(rect); return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
        private static Image FullImage(string name, Transform parent, string sprite)
        { var image = Rect(name, parent).gameObject.AddComponent<Image>(); image.sprite = Sprite(sprite); image.color = Color.white; image.raycastTarget = false; return image; }
        private static Text Text(string name, Transform parent, int size)
        {
            var text = Rect(name, parent).gameObject.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
        }
    }
}
#endif
