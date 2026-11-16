#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Astella.PrismV4.Editor
{
    /// <summary>Creates independent, generic uGUI example prefabs. Never edits or activates a main-project HUD.</summary>
    public static class PrismV4PrefabBuilder
    {
        private const string Sprites = PrismV4Importer.Root + "/Sprites/";
        private const string Output = PrismV4Importer.Root + "/Prefabs";
        private static readonly string[] Keys = { "1", "2", "3", "4", "Q", "Space", "R", "F" };
        private static Scene _authoringScene;

        [MenuItem("Tools/Astella/Prism V4/Build Example Prefabs")]
        public static void BuildExamples()
        {
            PrismV4Importer.EnsureEditMode();
            PrismV4Importer.ImportSprites();
            if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(PrismV4Importer.Root, "Prefabs");
            _authoringScene = EditorSceneManager.NewPreviewScene();
            try
            {
                Save("Boss", BuildBoss());
                Save("Player", BuildPlayer());
                Save("PartyRow", BuildPartyRow("AEGIS"));
                Save("SkillSlot", BuildSkillSlot(0));
                Save("Center", BuildCenter());
                Save("PrismV4Preview", BuildPreview());
            }
            finally
            {
                if (_authoringScene.IsValid()) EditorSceneManager.ClosePreviewScene(_authoringScene);
                _authoringScene = default(Scene);
            }
            Debug.Log("Prism V4: created new standalone module prefabs in " + Output + ". Existing prefabs, active HUD profile and scenes were not saved or replaced.");
        }

        private static void Save(string name, GameObject root)
        {
            try
            {
                string path = AssetDatabase.GenerateUniqueAssetPath(Output + "/" + name + ".prefab");
                PrismV4Importer.RequirePackPath(path);
                if (!path.StartsWith(Output + "/", StringComparison.Ordinal) || File.Exists(path))
                    throw new IOException("Example output already exists or is outside Prefabs: " + path);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException("Unable to create " + path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject BuildBoss()
        {
            var root = Module("Boss", 1280, 204, PrismV4Bindings.Module.Boss, out var view);
            view.nameText = Label("Live boss name", root.transform, "PRISM WARDEN", 0, 0, 1280, 40, 30);
            var bar = Rect("Boss HP", root.transform, 0, 44, 1280, 96);
            Picture("Track", bar, "Boss/boss-track", 0, 0, 1280, 96);
            view.bossFill = Gauge("Live boss fill", bar, "Boss/boss-fill", 1280, 96, new Rect(27f / 1280, 29f / 96, 1226f / 1280, 38f / 96), 22f / 1280);
            Picture("Frame", bar, "Boss/boss-frame", 0, 0, 1280, 96);
            Picture("Left boss gem", bar, "Boss/boss-gem-left", -16, 0, 96, 96);
            Picture("Right boss gem", bar, "Boss/boss-gem-right", 1200, 0, 96, 96);
            view.valueText = Label("Live boss percentage", bar, "100%", 0, 0, 1280, 96, 34);
            var cast = Rect("Cast", root.transform, 384, 144, 512, 48);
            Picture("Track", cast, "Boss/cast-track", 0, 0, 512, 48);
            view.castFill = Gauge("Live cast fill", cast, "Boss/cast-fill", 512, 48, new Rect(15f / 512, 20f / 48, 482f / 512, 8f / 48), 7f / 512);
            view.castFill.SetValue(.6f);
            Picture("Frame", cast, "Boss/cast-frame", 0, 0, 512, 48);
            view.castNameText = Label("Live cast name", root.transform, "", 180, 176, 420, 28, 22);
            view.castTimeText = Label("Live cast time", root.transform, "", 680, 176, 420, 28, 22);
            view.integrationNote = "Bind existing boss/cast Image.fillAmount sources; keep actual boss title, cast/groggy visibility and timing. Boss gems are decorative; player bars have none.";
            return root;
        }

        private static GameObject BuildPlayer()
        {
            var root = Module("Player", 2560, 560, PrismV4Bindings.Module.Player, out var view);
            var hp = Rect("Health", root.transform, 0, 0, 1024, 96);
            Picture("Track", hp, "Player/gauge-track", 0, 0, 1024, 96);
            view.healthFill = Gauge("Live health fill", hp, "Player/hp-fill", 1024, 96, new Rect(29f / 1024, 18f / 96, 966f / 1024, 60f / 96), 36f / 1024);
            Picture("Frame", hp, "Player/gauge-frame", 0, 0, 1024, 96);
            Label("Health caption", hp, "HP", 90, 0, 160, 96, 34);
            view.valueText = Label("Live health value", hp, "48,639", 250, 0, 560, 96, 46);

            var resource = Rect("Job primary resource", root.transform, 1536, 0, 1024, 96);
            Picture("Track", resource, "Player/gauge-track", 0, 0, 1024, 96);
            view.jobResourceFill = Gauge("Live job resource fill", resource, "Player/resource-fill", 1024, 96, new Rect(29f / 1024, 18f / 96, 966f / 1024, 60f / 96), 36f / 1024);
            view.jobResourceFill.SetValue(.6f);
            Picture("Frame", resource, "Player/gauge-frame", 0, 0, 1024, 96);
            view.resourceText = Label("Live job resource value", resource, "60 / 100", 170, 0, 684, 96, 46);

            view.skillSlots = new PrismV4Bindings[8];
            for (int i = 0; i < 8; i++)
            {
                var slot = BuildSkillSlot(i);
                Attach(slot, root.transform, i < 4 ? i * 272 : 1536 + (i - 4) * 272, 140, 208, 248);
                view.skillSlots[i] = slot.GetComponent<PrismV4Bindings>();
            }
            var center = BuildCenter();
            Attach(center, root.transform, 1088, 0, 384, 432);
            var ultimate = center.GetComponent<PrismV4Bindings>();
            view.ultimateChargeFill = ultimate.ultimateChargeFill;
            view.keyText = ultimate.keyText;
            view.integrationNote = "Health and job PRIMARY resource are separate from central ULTIMATE charge. Connect eight existing buttons/cooldowns, remappable keys and EXISTING main-project icons. No new skill icon artwork is supplied.";
            return root;
        }

        private static GameObject BuildPartyRow(string name)
        {
            var root = Module("PartyRow", 452, 112, PrismV4Bindings.Module.PartyRow, out var view);
            view.artwork = ArtworkBinding("Existing role icon binding", root.transform, 0, 8, 96, 96);
            view.frame = Picture("Role frame", root.transform, "Party/role-frame", 0, 8, 96, 96);
            view.nameText = Label("Live member name", root.transform, name, 112, 4, 320, 40, 28);
            view.nameText.alignment = TextAnchor.MiddleLeft;
            var bar = Rect("Member health", root.transform, 112, 52, 320, 40);
            Picture("Track", bar, "Party/bar-track", 0, 0, 320, 40);
            view.healthFill = Gauge("Live member health fill", bar, "Party/bar-fill", 320, 40, new Rect(14f / 320, 12f / 40, 292f / 320, 16f / 40), 10f / 320);
            view.criticalHealthSprite = Load("Party/bar-fill-critical");
            Picture("Frame", bar, "Party/bar-frame", 0, 0, 320, 40);
            view.selectedOverlay = Picture("Selection overlay", bar, "Party/selected-overlay", 0, 0, 320, 40);
            view.selectedOverlay.enabled = false;
            view.deadOverlay = Picture("Defeated overlay", bar, "Party/dead-overlay", 0, 0, 320, 40);
            view.deadOverlay.enabled = false;
            var hit = root.AddComponent<Image>();
            hit.color = Color.clear;
            view.button = root.AddComponent<Button>();
            view.button.targetGraphic = hit;
            view.button.transition = Selectable.Transition.None;
            view.integrationNote = "One passive row. Main bridge supplies roster/index, name, job icon, health, statuses and the ORIGINAL party selection callback.";
            return root;
        }

        private static GameObject BuildSkillSlot(int index)
        {
            var root = Module("SkillSlot", 208, 248, PrismV4Bindings.Module.SkillSlot, out var view);
            var body = Rect("Slot", root.transform, 8, 48, 192, 192);
            Picture("Background", body, "Skills/slot-background", 0, 0, 192, 192);
            var maskRoot = Rect("Clipped contents", body, 0, 0, 192, 192);
            var mask = maskRoot.gameObject.AddComponent<Image>();
            mask.sprite = Load("Skills/slot-inner-mask"); mask.raycastTarget = false;
            maskRoot.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            view.artwork = ArtworkBinding("Existing skill icon binding", maskRoot, 0, 0, 192, 192);
            view.cooldownShade = Picture("Live cooldown shade", maskRoot, "States/cooldown-mask", 0, 0, 192, 192);
            view.cooldownShade.type = Image.Type.Filled;
            view.cooldownShade.fillMethod = Image.FillMethod.Radial360;
            view.cooldownShade.fillOrigin = (int)Image.Origin360.Top;
            view.cooldownShade.fillClockwise = true;
            view.cooldownShade.fillAmount = 0;
            view.frame = Picture("Normal frame", body, "Skills/slot-frame-normal", 0, 0, 192, 192);
            view.hoverOverlay = Picture("Hover overlay", body, "States/hover-overlay", 0, 0, 192, 192);
            view.hoverOverlay.enabled = false;
            view.disabledOverlay = Picture("Unavailable overlay", body, "States/disabled-overlay", 0, 0, 192, 192);
            view.disabledOverlay.enabled = false;
            view.cooldownText = Label("Live cooldown seconds", body, "", 0, 0, 192, 192, 42);
            bool wide = index == 5;
            float keyWidth = wide ? 112 : 72;
            Picture("Blank key badge", root.transform, wide ? "Skills/key-badge-wide" : "Skills/key-badge", (208 - keyWidth) * .5f, 24, keyWidth, 48);
            view.keyText = Label("Live key", root.transform, Keys[index], (208 - keyWidth) * .5f, 24, keyWidth, 48, wide ? 24 : 30);
            view.button = root.AddComponent<Button>();
            view.button.targetGraphic = InputTarget(root);
            view.button.transition = Selectable.Transition.None;
            view.integrationNote = "No action listener. Bind an existing slot/button, runtime key and cooldown. Optional hover/pressed/focused/disabled frames are separate sprites.";
            return root;
        }

        private static GameObject BuildCenter()
        {
            var root = Module("Center", 384, 432, PrismV4Bindings.Module.Center, out var view);
            var body = Rect("Prism", root.transform, 0, 48, 384, 384);
            Picture("Background", body, "Hub/hub-background", 0, 0, 384, 384);
            view.ultimateChargeFill = Gauge("Live ultimate crystal", body, "Hub/hub-crystal", 384, 384, new Rect(0, 0, 1, 1), 0);
            view.ultimateChargeFill.direction = PrismV4Gauge.Direction.BottomToTop;
            view.artwork = view.ultimateChargeFill;
            var ready = Picture("Ready crystal overlay", body, "Hub/ready-glow", 0, 0, 384, 384);
            view.readyGlow = ready.gameObject; view.readyGlow.SetActive(false);
            var unavailable = Picture("Unavailable crystal overlay", body, "Hub/disabled-overlay", 0, 0, 384, 384);
            view.unavailableOverlay = unavailable.gameObject; view.unavailableOverlay.SetActive(false);
            view.frame = Picture("Fixed hub frame", body, "Hub/hub-frame", 0, 0, 384, 384);
            Picture("Blank V badge", body, "Hub/ultimate-badge", 128, -24, 128, 80);
            view.keyText = Label("Live ultimate key", body, "V", 128, -24, 128, 80, 42);
            view.cooldownText = Label("Live ultimate cooldown", body, "", 0, 80, 384, 240, 54);
            view.button = root.AddComponent<Button>();
            view.button.targetGraphic = InputTarget(root);
            view.button.transition = Selectable.Transition.None;
            view.integrationNote = "Central V is ULTIMATE charge/readiness only, independent of the right job-resource bar. Main readiness bridge controls unavailable/ready state; this example submits no actions.";
            return root;
        }

        private static GameObject BuildPreview()
        {
            var root = Module("PrismV4Preview", 1672, 941, PrismV4Bindings.Module.Preview, out var view);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1672, 941);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root.AddComponent<GraphicRaycaster>();
            var boss = BuildBoss(); Attach(boss, root.transform, 376, 44, 920, 146.625f); ScaleContents(boss, 920f / 1280);
            var player = BuildPlayer(); Attach(player, root.transform, 78, 628, 1516, 331.625f); ScaleContents(player, 1516f / 2560);
            var playerView = player.GetComponent<PrismV4Bindings>();
            view.healthFill = playerView.healthFill; view.jobResourceFill = playerView.jobResourceFill;
            view.ultimateChargeFill = playerView.ultimateChargeFill; view.skillSlots = playerView.skillSlots;
            string[] names = { "AEGIS", "LYRA", "NOX", "IRIS" };
            view.partyRows = new PrismV4Bindings[4];
            for (int i = 0; i < 4; i++)
            {
                var row = BuildPartyRow(names[i]);
                Attach(row, root.transform, 52, 88 + 74 * i, 226, 56); ScaleContents(row, .5f);
                view.partyRows[i] = row.GetComponent<PrismV4Bindings>();
            }
            view.integrationNote = "Standalone transparent canvas for visual assembly only. It is not activated in any scene, creates no EventSystem and contains no live gameplay binding.";
            return root;
        }

        private static void ScaleContents(GameObject root, float scale)
        {
            // Scale the module as a whole. Do not resize fills independently of their frames.
            var rect = (RectTransform)root.transform;
            rect.sizeDelta /= scale;
            rect.localScale = Vector3.one * scale;
        }

        private static GameObject Module(string name, float width, float height, PrismV4Bindings.Module kind, out PrismV4Bindings view)
        {
            var root = new GameObject(name, typeof(RectTransform));
            if (_authoringScene.IsValid()) SceneManager.MoveGameObjectToScene(root, _authoringScene);
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(width, height);
            rect.pivot = new Vector2(0, 1);
            view = root.AddComponent<PrismV4Bindings>(); view.module = kind;
            return root;
        }

        private static void Attach(GameObject child, Transform parent, float x, float y, float width, float height)
        {
            child.transform.SetParent(parent, false);
            Set((RectTransform)child.transform, x, y, width, height);
        }

        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; Set(rect, x, y, width, height); return rect;
        }

        private static void Set(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Sprite Load(string relative)
        {
            string path = Sprites + relative + ".png";
            PrismV4Importer.RequirePackPath(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Required pack sprite missing: " + path);
            return sprite;
        }

        private static Image Picture(string name, Transform parent, string relative, float x, float y, float width, float height)
        {
            var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.sprite = Load(relative); image.color = Color.white; image.raycastTarget = false;
            return image;
        }

        private static Image ArtworkBinding(string name, Transform parent, float x, float y, float width, float height)
        {
            var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.sprite = null;
            image.enabled = false;
            image.raycastTarget = false;
            // The main bridge assigns an existing icon and enables this Image; no placeholder art is generated.
            return image;
        }

        private static Image InputTarget(GameObject root)
        {
            var hit = root.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            return hit;
        }

        private static PrismV4Gauge Gauge(string name, Transform parent, string relative, float width, float height, Rect bounds, float tip)
        {
            var image = Rect(name, parent, 0, 0, width, height).gameObject.AddComponent<PrismV4Gauge>();
            image.sprite = Load(relative); image.innerBounds = bounds; image.pointWidth = tip;
            image.color = Color.white; image.raycastTarget = false; return image;
        }

        private static Text Label(string name, Transform parent, string value, float x, float y, float width, float height, int fontSize)
        {
            var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = value; label.fontSize = fontSize; label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color32(242, 234, 222, 255); label.raycastTarget = false;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = fontSize;
            return label;
        }
    }
}
#endif
